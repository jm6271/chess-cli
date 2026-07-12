using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChessCli.Configuration;
using ChessCli.Game;

namespace ChessCli.Providers;

public interface IChessMoveClient
{
    Task<ChessMoveResponse> GetMoveAsync(
        ChessGame game,
        ProviderSettings settings,
        string? validationFeedback,
        CancellationToken cancellationToken);
}

public sealed record ChessMoveResponse(string Move, string? Reasoning, string Response);

public sealed class OpenAiCompatibleChessClient : IChessMoveClient
{
    // All supported providers speak the same chat-completions shape; only the
    // endpoint and optional authentication differ.
    private const string SystemPrompt =
        """
        You are playing standard chess.

        Carefully analyze the current position and choose the strongest move.

        Before committing to a move:
        - Identify all checks, captures, and immediate threats for both sides.
        - Consider the opponent's strongest response to each serious candidate move.
        - Reject any move that unnecessarily loses material, weakens king safety, or permits
          a forced tactic or checkmate.
        - Prefer sound moves over speculative sacrifices unless the sacrifice has a concrete,
          verified continuation.
        - After selecting a move, perform one final blunder check from the opponent's perspective.

        Choose exactly one move from the supplied list of legal SAN moves.

        Finish your response with:
        FINAL_MOVE: <one supplied legal SAN move>
        """;

    private readonly HttpClient _httpClient;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public OpenAiCompatibleChessClient(
        HttpClient httpClient,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _httpClient = httpClient;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public async Task<ChessMoveResponse> GetMoveAsync(
        ChessGame game,
        ProviderSettings settings,
        string? validationFeedback,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        var endpoint = BuildEndpoint(settings.Url!);
        var prompt = BuildPrompt(game, validationFeedback);
        var payload = new ChatCompletionRequest
        {
            Model = settings.Model!,
            Messages =
            [
                new ChatMessage { Role = "system", Content = SystemPrompt },
                new ChatMessage { Role = "user", Content = prompt }
            ],
            ReasoningEffort = settings.ReasoningEffort
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, ChatJsonContext.Default.ChatCompletionRequest),
                Encoding.UTF8,
                "application/json")
        };

        var apiKey = _getEnvironmentVariable("OPENAI_API_KEY");
        if (settings.Provider == ProviderNames.OpenAi && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not set. Set it in the environment before using the OpenAI provider.");
        }

        // Ollama is local and does not receive the OpenAI key even if one exists.
        if (settings.Provider != ProviderNames.Ollama && !string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Include a bounded response fragment because providers often put the
            // useful diagnosis in their JSON error body.
            var detail = responseBody.Length > 500 ? responseBody[..500] + "…" : responseBody;
            throw new HttpRequestException(
                $"Provider returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}",
                null,
                response.StatusCode);
        }

        ChatCompletionResponse? completion;
        try
        {
            completion = JsonSerializer.Deserialize(
                responseBody,
                ChatJsonContext.Default.ChatCompletionResponse);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Provider returned invalid JSON.", exception);
        }

        var message = completion?.Choices.FirstOrDefault()?.Message;
        var content = message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidDataException("Provider returned no move.");
        }

        return new ChessMoveResponse(
            ExtractFinalMove(content),
            message!.Reasoning ?? message.ReasoningContent,
            content);
    }

    private static string ExtractFinalMove(string response)
    {
        const string marker = "FINAL_MOVE:";
        // Use the final marker so analysis can safely mention the required response format.
        var markerIndex = response.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidDataException("Provider response did not contain a FINAL_MOVE marker.");
        }

        var move = response[(markerIndex + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(move))
        {
            throw new InvalidDataException("Provider response did not include a move after FINAL_MOVE.");
        }

        return move;
    }

    private static Uri BuildEndpoint(string baseUrl)
    {
        // Normalizing the slash makes both https://host/v1 and https://host/v1/
        // resolve to the same chat-completions endpoint.
        var normalized = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        return new Uri(new Uri(normalized, UriKind.Absolute), "chat/completions");
    }

    private static string BuildPrompt(ChessGame game, string? validationFeedback)
    {
        // Supplying the legal move set constrains the model and gives a retry
        // enough context to correct an invalid previous response.
        var feedback = string.IsNullOrWhiteSpace(validationFeedback)
            ? string.Empty
            : $"\nYour previous response was rejected: {validationFeedback}\nChoose again.";

        return $"""
            Side to move: {game.SideToMove}
            FEN: {game.Fen}
            Game so far: {game.ToSanMovetext()}
            Legal SAN moves: {string.Join(", ", game.LegalMoves)}{feedback}
            """;
    }
}

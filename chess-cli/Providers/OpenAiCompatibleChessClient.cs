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

        First, analyze the opponent's immediate threats before considering your own plans.

        Mandatory threat scan:
        1. List every legal check the opponent could play on the next move.
        2. Determine whether any of those checks is checkmate or begins a forced tactic.
        3. List immediate captures of queens, rooks, and undefended pieces.
        4. If the opponent has a mate-in-one threat, only consider moves that prevent it.

        Then compare candidate moves:
        - Calculate the opponent's strongest reply.
        - Track all captures and material changes explicitly.
        - Verify every attacked and defended square using the current board.
        - Reject moves that permit checkmate or major material loss.

        Before finalizing, reconstruct the resulting position and perform one final
        opponent check-and-capture scan.

        Finish with:
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

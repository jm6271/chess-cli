using System.Net;
using System.Text;
using System.Text.Json;
using ChessCli.Configuration;
using ChessCli.Game;
using ChessCli.Providers;

namespace ChessCli.Tests;

public sealed class ProviderTests
{
    [Fact]
    public async Task Client_SendsOpenAiCompatibleRequestToOllama()
    {
        var handler = new RecordingHandler("I considered the replies to e4 and found it sound.\nFINAL_MOVE: e4");
        var client = new OpenAiCompatibleChessClient(new HttpClient(handler), _ => null);
        var settings = Settings(ProviderNames.Ollama, "http://localhost:11434/v1");

        var move = await client.GetMoveAsync(new ChessGame(), settings, null, CancellationToken.None);

        Assert.Equal("e4", move);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.RequestUri!.ToString());
        Assert.Null(handler.Authorization);
        Assert.Contains("Legal SAN moves", handler.RequestBody);
        Assert.Contains("FINAL_MOVE:", handler.RequestBody);
        Assert.Contains("\"model\":\"test-model\"", handler.RequestBody);
        Assert.Contains("\"reasoning_effort\":\"medium\"", handler.RequestBody);
    }

    [Fact]
    public async Task Client_RequiresEnvironmentKeyForOpenAi()
    {
        var handler = new RecordingHandler("FINAL_MOVE: e4");
        var client = new OpenAiCompatibleChessClient(new HttpClient(handler), _ => null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetMoveAsync(
                new ChessGame(),
                Settings(ProviderNames.OpenAi, "https://api.openai.com/v1/"),
                null,
                CancellationToken.None));

        Assert.Contains("OPENAI_API_KEY", exception.Message);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Client_AddsEnvironmentKeyForCompatibleProvider()
    {
        var handler = new RecordingHandler("FINAL_MOVE: e4");
        var client = new OpenAiCompatibleChessClient(new HttpClient(handler), _ => "secret");

        await client.GetMoveAsync(
            new ChessGame(),
            Settings(ProviderNames.Compatible, "https://example.test/openai/v1/"),
            null,
            CancellationToken.None);

        Assert.Equal("Bearer secret", handler.Authorization);
    }

    [Fact]
    public async Task Client_RejectsResponseWithoutFinalMoveMarker()
    {
        var handler = new RecordingHandler("e4");
        var client = new OpenAiCompatibleChessClient(new HttpClient(handler), _ => null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetMoveAsync(
                new ChessGame(),
                Settings(ProviderNames.Ollama, "http://localhost:11434/v1"),
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task Coordinator_RetriesIllegalMovesThenMutatesOnce()
    {
        var fake = new QueueMoveClient("not-a-move", "e4");
        var coordinator = new LlmMoveCoordinator(fake);
        var game = new ChessGame();

        var result = await coordinator.MakeMoveAsync(
            game,
            Settings(ProviderNames.Ollama, "http://localhost:11434/v1/"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(new[] { "e4" }, game.Moves);
        Assert.NotNull(fake.Feedback[1]);
    }

    [Fact]
    public async Task Coordinator_LeavesBoardUnchangedAfterThreeFailures()
    {
        var fake = new QueueMoveClient("bad", "worse", "still bad");
        var coordinator = new LlmMoveCoordinator(fake);
        var game = new ChessGame();
        var initialFen = game.Fen;

        var result = await coordinator.MakeMoveAsync(
            game,
            Settings(ProviderNames.Ollama, "http://localhost:11434/v1/"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(initialFen, game.Fen);
        Assert.Empty(game.Moves);
    }

    private static ProviderSettings Settings(string provider, string url) => new()
    {
        Provider = provider,
        Model = "test-model",
        Url = url
    };

    private sealed class RecordingHandler(string responseMove) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            var json = JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { role = "assistant", content = responseMove } } }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class QueueMoveClient(params string[] moves) : IChessMoveClient
    {
        private readonly Queue<string> _moves = new(moves);

        public List<string?> Feedback { get; } = [];

        public Task<string> GetMoveAsync(
            ChessGame game,
            ProviderSettings settings,
            string? validationFeedback,
            CancellationToken cancellationToken)
        {
            Feedback.Add(validationFeedback);
            return Task.FromResult(_moves.Dequeue());
        }
    }
}

using ChessCli.Cli;
using ChessCli.Configuration;
using ChessCli.Game;
using ChessCli.Providers;

namespace ChessCli.Tests;

public sealed class InteractiveRunnerTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "chess-cli-runner-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SlashMove_PerformsExactlyOnePlyAndReturnsToPrompt()
    {
        var fake = new FakeMoveClient("e4");
        var game = new ChessGame();
        var output = new StringWriter();
        var runner = CreateRunner(game, fake, "/move\n/quit\ny\n", output, ChessSide.Black);

        var exitCode = await runner.RunAsync();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(new[] { "e4" }, game.Moves);
        Assert.Contains("[Black]>", output.ToString());
    }

    [Fact]
    public async Task HumanMove_TriggersConfiguredLlmColorAutomatically()
    {
        var fake = new FakeMoveClient("e5");
        var game = new ChessGame();
        var output = new StringWriter();
        var runner = CreateRunner(game, fake, "e4\n/quit\ny\n", output, ChessSide.Black);

        await runner.RunAsync();

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(new[] { "e4", "e5" }, game.Moves);
        Assert.Contains("LLM move: e5", output.ToString());
    }

    [Fact]
    public async Task WhiteLlm_MovesAtStartup()
    {
        var fake = new FakeMoveClient("d4");
        var game = new ChessGame();
        var runner = CreateRunner(game, fake, "/quit\ny\n", new StringWriter(), ChessSide.White);

        await runner.RunAsync();

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(new[] { "d4" }, game.Moves);
    }

    [Fact]
    public async Task LlmMove_UsesBrightColorWhenEnabled()
    {
        var output = new StringWriter();
        var runner = CreateRunner(new ChessGame(), new FakeMoveClient("e4"), "/move\n/quit\ny\n", output, ChessSide.Black, true);

        await runner.RunAsync();

        Assert.Contains("\u001b[1;93me4\u001b[0m", output.ToString());
    }

    [Fact]
    public async Task ProviderCommands_PersistPerProviderSettings()
    {
        var fake = new FakeMoveClient();
        var game = new ChessGame();
        var runner = CreateRunner(
            game,
            fake,
            "/provider openai\n/model gpt-test\n/url https://example.test/v1\n/quit\n",
            new StringWriter(),
            ChessSide.Black);

        await runner.RunAsync();
        var config = new ConfigStore(System.IO.Path.Combine(_directory, "config.json"))
            .Load(out var warning);

        Assert.Null(warning);
        Assert.Equal(ProviderNames.OpenAi, config.SelectedProvider);
        Assert.Equal("gpt-test", config.GetProfile(ProviderNames.OpenAi).Model);
        Assert.Equal("https://example.test/v1", config.GetProfile(ProviderNames.OpenAi).Url);
        Assert.Null(config.GetProfile(ProviderNames.Ollama).Model);
    }

    private InteractiveRunner CreateRunner(
        ChessGame game,
        FakeMoveClient client,
        string input,
        StringWriter output,
        ChessSide llmSide,
        bool useColor = false)
    {
        var config = new AppConfig();
        config.EnsureDefaults();
        return new InteractiveRunner(
            game,
            new NotationStore(),
            new LlmMoveCoordinator(client),
            config,
            new ConfigStore(System.IO.Path.Combine(_directory, "config.json")),
            new ProviderSettings
            {
                Provider = ProviderNames.Ollama,
                Model = "test-model",
                Url = "http://localhost:11434/v1/"
            },
            llmSide,
            null,
            new StringReader(input),
            output,
            output,
            useColor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeMoveClient(params string[] moves) : IChessMoveClient
    {
        private readonly Queue<string> _moves = new(moves);

        public int CallCount { get; private set; }

        public Task<string> GetMoveAsync(
            ChessGame game,
            ProviderSettings settings,
            string? validationFeedback,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_moves.Dequeue());
        }
    }
}

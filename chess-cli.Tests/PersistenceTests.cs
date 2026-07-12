using ChessCli.Configuration;
using ChessCli.Game;

namespace ChessCli.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "chess-cli-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConfigStore_RoundTripsPerProviderProfiles()
    {
        var path = System.IO.Path.Combine(_directory, "config.json");
        var store = new ConfigStore(path);
        var config = new AppConfig { SelectedProvider = ProviderNames.OpenAi };
        config.EnsureDefaults();
        config.GetProfile(ProviderNames.Ollama).Model = "llama";
        config.GetProfile(ProviderNames.OpenAi).Model = "gpt-test";

        await store.SaveAsync(config);
        var loaded = store.Load(out var warning);

        Assert.Null(warning);
        Assert.Equal(ProviderNames.OpenAi, loaded.SelectedProvider);
        Assert.Equal("llama", loaded.GetProfile(ProviderNames.Ollama).Model);
        Assert.Equal("gpt-test", loaded.GetProfile(ProviderNames.OpenAi).Model);
        Assert.Equal(ReasoningEfforts.Medium, loaded.GetProfile(ProviderNames.OpenAi).ReasoningEffort);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigStore_MalformedJsonWarnsAndUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        var path = System.IO.Path.Combine(_directory, "config.json");
        await File.WriteAllTextAsync(path, "{not-json");

        var config = new ConfigStore(path).Load(out var warning);

        Assert.NotNull(warning);
        Assert.Equal(ProviderNames.Ollama, config.SelectedProvider);
        Assert.Equal("http://localhost:11434/v1/", config.GetProfile(ProviderNames.Ollama).Url);
    }

    [Fact]
    public async Task NotationStore_UsesExtensionAndRoundTripsBothFormats()
    {
        var game = new ChessGame();
        Assert.True(game.TryMove("e4", out _, out _));
        Assert.True(game.TryMove("e5", out _, out _));
        var store = new NotationStore();
        var pgnPath = System.IO.Path.Combine(_directory, "game.pgn");
        var textPath = System.IO.Path.Combine(_directory, "game.txt");

        await store.SaveAsync(game, pgnPath);
        await store.SaveAsync(game, textPath);

        Assert.StartsWith("[Event", await File.ReadAllTextAsync(pgnPath));
        Assert.StartsWith("1. e4 e5", await File.ReadAllTextAsync(textPath));
        Assert.Equal(game.Moves, (await store.LoadAsync(pgnPath)).Moves);
        Assert.Equal(game.Moves, (await store.LoadAsync(textPath)).Moves);
        Assert.False(game.IsDirty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

using System.CommandLine;
using ChessCli.Cli;
using ChessCli.Configuration;
using ChessCli.Game;
using ChessCli.Providers;

namespace ChessCli;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Keep argument construction here so the interactive runner can remain
        // independent of System.CommandLine and straightforward to test.
        var loadOption = new Option<string?>("--load")
        {
            Description = "Load a PGN or plain SAN notation file."
        };
        var saveOption = new Option<string?>("--save")
        {
            Description = "Save notation to this path when the game ends."
        };
        var colorOption = new Option<string?>("--llm-color")
        {
            Description = "Color automatically played by the LLM: black or white. Default: black."
        };
        var providerOption = new Option<string?>("--provider")
        {
            Description = "LLM provider: ollama, openai, or compatible."
        };
        var modelOption = new Option<string?>("--model")
        {
            Description = "Model identifier. Required unless saved for the selected provider."
        };
        var urlOption = new Option<string?>("--url")
        {
            Description = "OpenAI-compatible API base URL, including /v1."
        };

        var root = new RootCommand("Play a physical chess game with an LLM opponent.");
        root.Options.Add(loadOption);
        root.Options.Add(saveOption);
        root.Options.Add(colorOption);
        root.Options.Add(providerOption);
        root.Options.Add(modelOption);
        root.Options.Add(urlOption);

        root.SetAction(parseResult =>
        {
            var options = new CliOptions(
                parseResult.GetValue(loadOption),
                parseResult.GetValue(saveOption),
                parseResult.GetValue(colorOption),
                parseResult.GetValue(providerOption),
                parseResult.GetValue(modelOption),
                parseResult.GetValue(urlOption));

            return RunAsync(options).GetAwaiter().GetResult();
        });

        return root.Parse(args).Invoke();
    }

    private static async Task<int> RunAsync(CliOptions options)
    {
        // Validate cheap user input before touching files or starting the provider.
        if (!TryParseSide(options.LlmColor, out var llmSide))
        {
            Console.Error.WriteLine("--llm-color must be 'black' or 'white'.");
            return 2;
        }

        if (options.Provider is not null && !ProviderNames.IsValid(options.Provider))
        {
            Console.Error.WriteLine("--provider must be 'ollama', 'openai', or 'compatible'.");
            return 2;
        }

        var configStore = new ConfigStore();
        var config = configStore.Load(out var warning);
        if (warning is not null)
        {
            Console.Error.WriteLine($"Warning: {warning}");
        }

        ProviderSettings providerSettings;
        try
        {
            providerSettings = ProviderSettings.Resolve(
                config,
                options.Provider,
                options.Model,
                options.Url);
            providerSettings.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        var notationStore = new NotationStore();
        ChessGame game;
        try
        {
            game = options.LoadPath is null
                ? new ChessGame()
                : await notationStore.LoadAsync(options.LoadPath);
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            // Turn Ctrl+C into cooperative cancellation so an in-flight request
            // can stop cleanly and the runner can return a conventional exit code.
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        var runner = new InteractiveRunner(
            game,
            notationStore,
            new LlmMoveCoordinator(new OpenAiCompatibleChessClient(httpClient)),
            config,
            configStore,
            providerSettings,
            llmSide,
            options.SavePath,
            Console.In,
            Console.Out,
            Console.Error,
            useColor: !Console.IsOutputRedirected &&
                      string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));

        try
        {
            return await runner.RunAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
    }

    private static bool TryParseSide(string? value, out ChessSide side)
    {
        // Black is the default because the human normally opens the game as white.
        if (value is null || value.Equals("black", StringComparison.OrdinalIgnoreCase))
        {
            side = ChessSide.Black;
            return true;
        }

        if (value.Equals("white", StringComparison.OrdinalIgnoreCase))
        {
            side = ChessSide.White;
            return true;
        }

        side = default;
        return false;
    }
}

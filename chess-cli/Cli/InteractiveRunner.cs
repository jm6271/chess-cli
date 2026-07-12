using ChessCli.Configuration;
using ChessCli.Game;
using ChessCli.Providers;

namespace ChessCli.Cli;

public sealed class InteractiveRunner
{
    private readonly ChessGame _game;
    private readonly NotationStore _notationStore;
    private readonly LlmMoveCoordinator _moveCoordinator;
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly TerminalText _terminalText;
    private ProviderSettings _provider;
    private string? _savePath;
    private bool _endAnnounced;

    public InteractiveRunner(
        ChessGame game,
        NotationStore notationStore,
        LlmMoveCoordinator moveCoordinator,
        AppConfig config,
        ConfigStore configStore,
        ProviderSettings provider,
        ChessSide llmSide,
        string? savePath,
        TextReader input,
        TextWriter output,
        TextWriter error,
        bool useColor = false)
    {
        _game = game;
        _notationStore = notationStore;
        _moveCoordinator = moveCoordinator;
        _config = config;
        _configStore = configStore;
        _provider = provider;
        LlmSide = llmSide;
        _savePath = savePath;
        _input = input;
        _output = output;
        _error = error;
        // Styling stays opt-in so files, pipes, and test output remain clean.
        _terminalText = new TerminalText(useColor);
    }

    public ChessSide LlmSide { get; }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await _terminalText.WriteLineAsync(_output, "chess-cli — enter a SAN move or /help", "1;96");
        await ShowBoardAsync();

        // This flag prevents an automatic response from repeating after a failed
        // human move or a command; it is re-enabled only after a human move (or
        // explicitly by /new).
        var allowAutomaticMove = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_game.IsOver)
            {
                // Announce and optionally save once; the loop still accepts useful
                // commands such as /save and /new after the game ends.
                await AnnounceEndAsync(cancellationToken);
                allowAutomaticMove = false;
            }
            else if (allowAutomaticMove && _game.SideToMove == LlmSide)
            {
                await MakeLlmMoveAsync(cancellationToken);
                allowAutomaticMove = false;
                continue;
            }

            await _output.WriteAsync($"[{_game.SideToMove}]> ");
            var input = await _input.ReadLineAsync(cancellationToken);
            if (input is null)
            {
                return 0;
            }

            input = input.Trim();
            if (input.Length == 0)
            {
                continue;
            }

            if (input.StartsWith('/'))
            {
                var commandResult = await HandleCommandAsync(input, cancellationToken);
                if (commandResult.Exit)
                {
                    return 0;
                }

                allowAutomaticMove = commandResult.AllowAutomaticMove;
                continue;
            }

            if (_game.IsOver)
            {
                await _error.WriteLineAsync("The game is over. Use /save, /new, or /quit.");
                continue;
            }

            if (_game.TryMove(input, out var move, out var moveError))
            {
                await _terminalText.WriteLineAsync(_output, $"Played: {move}", "92");
                // The human just completed a ply, so the configured LLM may move.
                allowAutomaticMove = true;
            }
            else
            {
                await _error.WriteLineAsync(moveError);
                allowAutomaticMove = false;
            }
        }

        return 130;
    }

    private async Task<CommandResult> HandleCommandAsync(string input, CancellationToken cancellationToken)
    {
        // Commands return a small state transition rather than mutating the loop's
        // automatic-move flag directly, keeping command behavior local and clear.
        var (command, argument) = SplitCommand(input);
        switch (command)
        {
            case "/help":
                await ShowHelpAsync();
                return CommandResult.Stay;
            case "/board":
                await ShowBoardAsync();
                return CommandResult.Stay;
            case "/move":
                if (_game.IsOver)
                {
                    await _error.WriteLineAsync("The game is already over.");
                }
                else
                {
                    await MakeLlmMoveAsync(cancellationToken);
                }

                return CommandResult.Stay;
            case "/save":
                await SaveAsync(Unquote(argument) ?? _savePath, cancellationToken);
                return CommandResult.Stay;
            case "/new":
                if (await CanDiscardAsync(cancellationToken))
                {
                    _game.Reset();
                    _endAnnounced = false;
                    await _output.WriteLineAsync("Started a new game.");
                    await ShowBoardAsync();
                    return CommandResult.Auto;
                }

                return CommandResult.Stay;
            case "/quit":
                return await CanDiscardAsync(cancellationToken) ? CommandResult.ExitNow : CommandResult.Stay;
            case "/provider":
                await ChangeProviderAsync(argument, cancellationToken);
                return CommandResult.Stay;
            case "/model":
                await ChangeModelAsync(argument, cancellationToken);
                return CommandResult.Stay;
            case "/url":
                await ChangeUrlAsync(argument, cancellationToken);
                return CommandResult.Stay;
            case "/reasoning":
                await ChangeReasoningEffortAsync(argument, cancellationToken);
                return CommandResult.Stay;
            default:
                await _error.WriteLineAsync($"Unknown command '{command}'. Use /help.");
                return CommandResult.Stay;
        }
    }

    private async Task MakeLlmMoveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _terminalText.WriteLineAsync(
                _output,
                $"Asking {_provider.Provider}/{_provider.Model} to move for {_game.SideToMove}...",
                "93");
            var result = await _moveCoordinator.MakeMoveAsync(_game, _provider, cancellationToken);
            if (result.Success)
            {
                // Keep the notation distinct from surrounding status text so it is
                // easy to spot while playing on a physical board.
                await _terminalText.WriteAsync(_output, "LLM move: ", "96");
                await _terminalText.WriteLineAsync(_output, result.Move!, "1;34");
                if (result.Attempts > 1)
                {
                    await _output.WriteLineAsync($"Accepted after {result.Attempts} attempts.");
                }
            }
            else
            {
                // A failed set of attempts does not change the board; leave the
                // user at the prompt so they can retry or inspect the position.
                await _error.WriteLineAsync(result.Error);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _error.WriteLineAsync("The provider request timed out; the board was not changed.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            await _error.WriteLineAsync($"Could not get an LLM move: {exception.Message}");
        }
    }

    private async Task AnnounceEndAsync(CancellationToken cancellationToken)
    {
        if (_endAnnounced)
        {
            return;
        }

        _endAnnounced = true;
        // Mark the announcement before saving so repeated loop iterations cannot
        // print the terminal board and movetext more than once.
        await _output.WriteLineAsync(_game.Ascii);
        await _output.WriteLineAsync($"Game over: {_game.EndDescription}");
        await _output.WriteLineAsync(_game.ToSanMovetext());
        if (_savePath is not null)
        {
            await SaveAsync(_savePath, cancellationToken);
        }
    }

    private async Task SaveAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            await _error.WriteLineAsync("No save path is set. Use /save <path>.");
            return;
        }

        try
        {
            await _notationStore.SaveAsync(_game, path, cancellationToken);
            _savePath = path;
            await _output.WriteLineAsync($"Saved notation to {System.IO.Path.GetFullPath(path)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await _error.WriteLineAsync(exception.Message);
        }
    }

    private async Task ChangeProviderAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!ProviderNames.IsValid(argument))
        {
            await _error.WriteLineAsync("Usage: /provider <ollama|openai|compatible>");
            return;
        }

        var provider = ProviderNames.Normalize(argument!);
        var profile = _config.GetProfile(provider);
        _provider = new ProviderSettings
        {
            Provider = provider,
            Model = profile.Model,
            Url = profile.Url,
            ReasoningEffort = profile.ReasoningEffort
        };
        _config.SelectedProvider = provider;
        await PersistConfigAsync(cancellationToken);
        await _output.WriteLineAsync(
            $"Provider: {provider}; model: {_provider.Model ?? "<not set>"}; URL: {_provider.Url ?? "<not set>"}");
    }

    private async Task ChangeModelAsync(string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            await _error.WriteLineAsync("Usage: /model <model-id>");
            return;
        }

        _provider.Model = argument.Trim();
        _config.GetProfile(_provider.Provider).Model = _provider.Model;
        await PersistConfigAsync(cancellationToken);
        await _output.WriteLineAsync($"Model: {_provider.Model}");
    }

    private async Task ChangeUrlAsync(string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument) ||
            !Uri.TryCreate(argument.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            await _error.WriteLineAsync("Usage: /url <http-or-https-base-url>");
            return;
        }

        _provider.Url = uri.ToString();
        _config.GetProfile(_provider.Provider).Url = _provider.Url;
        await PersistConfigAsync(cancellationToken);
        await _output.WriteLineAsync($"URL: {_provider.Url}");
    }

    private async Task ChangeReasoningEffortAsync(string? argument, CancellationToken cancellationToken)
    {
        if (!ReasoningEfforts.IsValid(argument))
        {
            await _error.WriteLineAsync("Usage: /reasoning <low|medium|high>");
            return;
        }

        // Save the active provider's level independently from its model and URL.
        _provider.ReasoningEffort = ReasoningEfforts.Normalize(argument!);
        _config.GetProfile(_provider.Provider).ReasoningEffort = _provider.ReasoningEffort;
        await PersistConfigAsync(cancellationToken);
        await _output.WriteLineAsync($"Reasoning effort: {_provider.ReasoningEffort}");
    }

    private async Task PersistConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _configStore.SaveAsync(_config, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await _error.WriteLineAsync($"Could not save preferences: {exception.Message}");
        }
    }

    private async Task<bool> CanDiscardAsync(CancellationToken cancellationToken)
    {
        if (!_game.IsDirty)
        {
            return true;
        }

        // Only an affirmative response permits /new or /quit to discard moves.
        await _output.WriteAsync("Unsaved moves will be lost. Continue? [y/N] ");
        var answer = await _input.ReadLineAsync(cancellationToken);
        return answer is not null &&
               (answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private Task ShowBoardAsync() => _output.WriteLineAsync(
        $"{_game.Ascii}{Environment.NewLine}Side to move: {_game.SideToMove}");

    private Task ShowHelpAsync() => _output.WriteLineAsync(
        """
        Commands:
          /move                 Ask the LLM to make exactly one move for the current side
          /board                Display the current board
          /save [path]          Save notation now (.pgn for PGN, otherwise plain SAN)
          /new                  Start over
          /provider <name>      Switch to ollama, openai, or compatible
          /model <id>           Switch model and persist it for the current provider
          /url <uri>            Change and persist the current provider's base URL
          /reasoning <level>    Set low, medium, or high reasoning and persist it
          /help                 Show this help
          /quit                 Exit
        """);

    private static (string Command, string? Argument) SplitCommand(string input)
    {
        var separator = input.IndexOf(' ');
        return separator < 0
            ? (input.ToLowerInvariant(), null)
            : (input[..separator].ToLowerInvariant(), input[(separator + 1)..].Trim());
    }

    private static string? Unquote(string? value)
    {
        if (value is null || value.Length < 2)
        {
            return value;
        }

        return value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }

    private readonly record struct CommandResult(bool Exit, bool AllowAutomaticMove)
    {
        public static CommandResult Stay => new(false, false);
        public static CommandResult Auto => new(false, true);
        public static CommandResult ExitNow => new(true, false);
    }
}

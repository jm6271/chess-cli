using ChessCli.Game;

namespace ChessCli.Providers;

public sealed record LlmMoveResult(bool Success, string? Move, string? Error, int Attempts);

public sealed class LlmMoveCoordinator
{
    private readonly IChessMoveClient _client;

    public LlmMoveCoordinator(IChessMoveClient client)
    {
        _client = client;
    }

    public async Task<LlmMoveResult> MakeMoveAsync(
        ChessGame game,
        ProviderSettings settings,
        CancellationToken cancellationToken)
    {
        string? feedback = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            // The board is mutated only by TryMove after a response matches a
            // legal move; invalid model output leaves the position unchanged.
            var response = await _client.GetMoveAsync(game, settings, feedback, cancellationToken);
            if (game.TryMove(response, out var canonicalMove, out var error))
            {
                return new LlmMoveResult(true, canonicalMove, null, attempt);
            }

            feedback = error;
        }

        return new LlmMoveResult(
            false,
            null,
            $"The model did not return a legal SAN move after 3 attempts. Last error: {feedback}",
            3);
    }
}

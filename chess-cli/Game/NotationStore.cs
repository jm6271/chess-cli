namespace ChessCli.Game;

public sealed class NotationStore
{
    public async Task<ChessGame> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            // Parsing is delegated to ChessGame so file I/O and notation errors
            // are presented to the caller through one InvalidDataException path.
            var notation = await File.ReadAllTextAsync(path, cancellationToken);
            return ChessGame.Load(notation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not load notation from '{path}': {exception.Message}", exception);
        }
    }

    public async Task SaveAsync(ChessGame game, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Only .pgn receives headers; every other extension is intentionally a
        // lightweight numbered SAN file that is easy to read and edit by hand.
        var contents = System.IO.Path.GetExtension(fullPath).Equals(".pgn", StringComparison.OrdinalIgnoreCase)
            ? game.ToPgn()
            : game.ToSanMovetext() + Environment.NewLine;

        try
        {
            await File.WriteAllTextAsync(fullPath, contents, cancellationToken);
            // Clear the prompt's unsaved-moves warning only after the write wins.
            game.MarkSaved();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not save notation to '{fullPath}': {exception.Message}", exception);
        }
    }
}

using System.Text;
using Chess;

namespace ChessCli.Game;

public enum ChessSide
{
    White,
    Black
}

public sealed class ChessGame
{
    // ChessBoard remains the source of truth for legal moves, turn order, and
    // endgame detection; this class adds the CLI-facing notation and dirty state.
    private ChessBoard _board;

    public ChessGame()
        : this(new ChessBoard { AutoEndgameRules = AutoEndgameRules.All }, isDirty: false)
    {
    }

    private ChessGame(ChessBoard board, bool isDirty)
    {
        _board = board;
        IsDirty = isDirty;
    }

    public bool IsDirty { get; private set; }

    public bool IsOver => _board.IsEndGame;

    public ChessSide SideToMove => _board.Turn == PieceColor.White ? ChessSide.White : ChessSide.Black;

    public string Fen => _board.ToFen();

    public string Ascii => _board.ToAscii();

    public IReadOnlyList<string> Moves => _board.MovesToSan;

    public IReadOnlyList<string> LegalMoves => _board
        // Generate SAN once, then normalize it so callers can compare model and
        // human input against one stable, sorted list.
        .Moves(allowAmbiguousCastle: false, generateSan: true)
        .Select(move => move.San)
        .Where(san => !string.IsNullOrWhiteSpace(san))
        .Select(san => san!)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public string Result
    {
        get
        {
            if (!IsOver || _board.EndGame is null)
            {
                return "*";
            }

            if (_board.EndGame.WonSide == PieceColor.White)
            {
                return "1-0";
            }

            if (_board.EndGame.WonSide == PieceColor.Black)
            {
                return "0-1";
            }

            return "1/2-1/2";
        }
    }

    public string EndDescription => _board.EndGame is null
        ? "Game in progress"
        : $"{_board.EndGame.EndgameType} ({Result})";

    public bool TryMove(string san, out string canonicalSan, out string error)
    {
        canonicalSan = string.Empty;
        error = string.Empty;

        if (IsOver)
        {
            error = "The game is already over.";
            return false;
        }

        // Validate against the generated legal SAN list before mutating the board.
        // This also gives the LLM coordinator a precise rejection message.
        var input = san.Trim();
        var canonical = LegalMoves.FirstOrDefault(move => move.Equals(input, StringComparison.Ordinal));
        if (canonical is null)
        {
            error = $"'{input}' is not a legal SAN move for {SideToMove}.";
            return false;
        }

        try
        {
            _board.Move(canonical);
            canonicalSan = canonical;
            IsDirty = true;
            return true;
        }
        catch (Exception exception) when (exception is ChessArgumentException or ChessSanNotFoundException or ChessSanTooAmbiguousException)
        {
            error = exception.Message;
            return false;
        }
    }

    public void Reset()
    {
        _board = new ChessBoard { AutoEndgameRules = AutoEndgameRules.All };
        IsDirty = false;
    }

    public void MarkSaved() => IsDirty = false;

    public string ToSanMovetext()
    {
        var builder = new StringBuilder();
        // MovesToSan is a flat ply list, so pair adjacent entries into numbered
        // white/black turns while preserving a final single white move.
        for (var index = 0; index < Moves.Count; index += 2)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(index / 2 + 1).Append(". ").Append(Moves[index]);
            if (index + 1 < Moves.Count)
            {
                builder.Append(' ').Append(Moves[index + 1]);
            }
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        return builder.Append(Result).ToString();
    }

    public string ToPgn(DateTimeOffset? now = null)
    {
        var date = (now ?? DateTimeOffset.Now).ToString("yyyy.MM.dd");
        return $"""
            [Event "chess-cli game"]
            [Site "?"]
            [Date "{date}"]
            [Round "-"]
            [White "White"]
            [Black "Black"]
            [Result "{Result}"]

            {ToSanMovetext()}
            """;
    }

    public static ChessGame Load(string notation)
    {
        try
        {
            // The chess library accepts both PGN headers and plain SAN movetext.
            var board = ChessBoard.LoadFromPgn(notation, AutoEndgameRules.All);
            return new ChessGame(board, isDirty: false);
        }
        catch (Exception exception) when (exception is ChessArgumentException or ChessSanNotFoundException or ChessSanTooAmbiguousException)
        {
            throw new InvalidDataException($"The notation is not valid PGN or SAN: {exception.Message}", exception);
        }
    }
}

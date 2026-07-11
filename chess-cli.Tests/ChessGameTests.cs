using ChessCli.Game;

namespace ChessCli.Tests;

public sealed class ChessGameTests
{
    [Fact]
    public void NewGame_HasExpectedInitialState()
    {
        var game = new ChessGame();

        Assert.Equal(ChessSide.White, game.SideToMove);
        Assert.Equal(20, game.LegalMoves.Count);
        Assert.Contains("e4", game.LegalMoves);
        Assert.False(game.IsDirty);
        Assert.Equal("*", game.Result);
    }

    [Fact]
    public void TryMove_UsesCanonicalSanAndRejectsIllegalMoveWithoutMutation()
    {
        var game = new ChessGame();

        Assert.True(game.TryMove("e4", out var move, out _));
        Assert.Equal("e4", move);
        Assert.Equal(ChessSide.Black, game.SideToMove);
        var fen = game.Fen;

        Assert.False(game.TryMove("e4", out _, out var error));
        Assert.Contains("not a legal SAN move", error);
        Assert.Equal(fen, game.Fen);
    }

    [Fact]
    public void FoolMate_IsDetectedAndSerialized()
    {
        var game = new ChessGame();
        Play(game, "f3", "e5", "g4", "Qh4#");

        Assert.True(game.IsOver);
        Assert.Equal("0-1", game.Result);
        Assert.Contains("1. f3 e5 2. g4 Qh4# 0-1", game.ToSanMovetext());
    }

    [Fact]
    public void Load_AcceptsPlainSanAndPgn()
    {
        var plain = ChessGame.Load("1. e4 e5 2. Nf3 *");
        var pgn = ChessGame.Load(
            "[Event \"Test\"]\n[Result \"*\"]\n\n1. d4 d5 2. c4 *");

        Assert.Equal(new[] { "e4", "e5", "Nf3" }, plain.Moves);
        Assert.Equal(ChessSide.Black, plain.SideToMove);
        Assert.Equal(new[] { "d4", "d5", "c4" }, pgn.Moves);
    }

    [Fact]
    public void GeneratedPgn_RoundTrips()
    {
        var game = new ChessGame();
        Play(game, "e4", "c5", "Nf3");

        var loaded = ChessGame.Load(game.ToPgn(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(game.Moves, loaded.Moves);
        Assert.Contains("[Date \"2026.07.11\"]", game.ToPgn(new DateTimeOffset(2026, 7, 11, 0, 0, 0, TimeSpan.Zero)));
    }

    private static void Play(ChessGame game, params string[] moves)
    {
        foreach (var move in moves)
        {
            Assert.True(game.TryMove(move, out _, out var error), error);
        }
    }
}

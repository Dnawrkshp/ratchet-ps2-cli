using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Abstractions;

internal static class TieGameFormats
{
    public const string SupportedTieGames = "GC, UYA, or DL";

    public static bool IsSupported(GameId gameId)
    {
        return gameId is GameId.GC or GameId.UYA or GameId.DL;
    }
}

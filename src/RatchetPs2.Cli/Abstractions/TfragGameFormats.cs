using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Abstractions;

internal static class TfragGameFormats
{
    public const string SupportedTfragGames = "UYA or DL";

    public static bool IsSupported(GameId gameId)
    {
        return gameId is GameId.UYA or GameId.DL;
    }
}

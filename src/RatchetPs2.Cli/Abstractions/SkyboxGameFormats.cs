using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;

namespace RatchetPs2.Cli.Abstractions;

internal static class SkyboxGameFormats
{
    public const string SupportedSkyboxGames = "UYA or DL";

    public static bool IsSupported(GameId gameId)
    {
        return gameId is GameId.UYA or GameId.DL;
    }

    public static SkyboxGameProfile ProfileFor(GameId gameId, bool preserveSourceAlpha)
    {
        if (!IsSupported(gameId))
        {
            throw new InvalidOperationException($"Skybox export currently supports only {SupportedSkyboxGames}. Received {gameId}.");
        }

        return SkyboxGameProfile.ForGame(gameId, preserveSourceAlpha);
    }

    public static int? InferLevelNumber(string pathOrLabel)
    {
        foreach (var segment in pathOrLabel.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length <= 5 || !segment.StartsWith("level", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(segment[5..], out var levelNumber))
            {
                return levelNumber;
            }
        }

        return null;
    }
}

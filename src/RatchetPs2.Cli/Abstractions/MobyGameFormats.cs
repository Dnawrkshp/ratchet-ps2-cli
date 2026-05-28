using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Cli.Abstractions;

internal static class MobyGameFormats
{
    public static MobyAnimationFormat Resolve(GameModuleResolver gameModuleResolver, GameId gameId)
    {
        var module = gameModuleResolver.Resolve(gameId);
        if (module is IMobyGameModule mobyModule)
        {
            return mobyModule.AnimationFormat;
        }

        throw new InvalidOperationException($"Game module '{gameId}' does not provide moby format support.");
    }
}

using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.DL;

public sealed class DLGameModule : IGameModule, IMobyGameModule
{
    public GameId Id => GameId.DL;
    public string DisplayName => "Deadlocked";
    public MobyAnimationFormat AnimationFormat => MobyAnimationFormat.Compact;
}

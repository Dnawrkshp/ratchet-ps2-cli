using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.UYA;

public sealed class UYAGameModule : IGameModule, IMobyGameModule
{
    public GameId Id => GameId.UYA;
    public string DisplayName => "Up Your Arsenal";
    public MobyAnimationFormat AnimationFormat => MobyAnimationFormat.Standard;
}

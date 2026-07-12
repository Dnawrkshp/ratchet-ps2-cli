namespace RatchetPs2.Games.UYA.Gameplay;

public sealed record UyaGameplayBlocks(
    string Kind,
    int HeaderSize,
    byte[] HeaderBytes,
    IReadOnlyList<UyaGameplayBlock> Blocks);

public sealed record UyaGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes,
    UyaLevelSettings? LevelSettings = null,
    UyaMobyInstances? MobyInstances = null);

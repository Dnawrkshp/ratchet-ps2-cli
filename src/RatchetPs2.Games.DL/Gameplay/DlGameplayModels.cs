namespace RatchetPs2.Games.DL.Gameplay;

public sealed record DlGameplayBlocks(
    string Kind,
    int HeaderSize,
    byte[] HeaderBytes,
    IReadOnlyList<DlGameplayBlock> Blocks);

public sealed record DlGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes,
    DlLevelSettings? LevelSettings = null,
    DlMobyInstances? MobyInstances = null);

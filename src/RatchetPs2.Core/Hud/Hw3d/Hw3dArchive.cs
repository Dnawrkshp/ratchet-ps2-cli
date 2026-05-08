namespace RatchetPs2.Core.Hud.Hw3d;

public sealed record Hw3dArchive(
    Hw3dHeader Header,
    IReadOnlyList<Hw3dTocEntry> TocEntries,
    IReadOnlyList<Hw3dEmbeddedSection> EmbeddedSections,
    int EndMagicOffset,
    int Length);
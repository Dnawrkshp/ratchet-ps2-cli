namespace RatchetPs2.Core.Hud.Hw3d;

public sealed record Hw3dEmbeddedSection(
    int Offset,
    string Magic,
    IReadOnlyList<uint> HeaderWords);
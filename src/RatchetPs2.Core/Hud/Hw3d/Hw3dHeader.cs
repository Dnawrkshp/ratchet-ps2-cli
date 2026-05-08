namespace RatchetPs2.Core.Hud.Hw3d;

public sealed record Hw3dHeader(
    string Magic,
    uint Version,
    uint TocEntryCount,
    uint DataStartOffset,
    uint ScreenCount,
    uint UnknownValue,
    uint Reserved)
{
    public const int SizeInBytes = 0x20;
}
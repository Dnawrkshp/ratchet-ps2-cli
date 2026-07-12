using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelConstants
{
    public const int SectorSize = Sector32.SizeInBytes;
    public const int RetailLevelInfoTableOffset = 0x1FBC00;
    public const int LevelInfoSize = 0x18;
    public const int LevelInfoCount = 100;
    public const int LevelWadHeaderSize = 0x60;
    public const int LevelDataHeaderSize = 0x58;
    public const int LevelWadHeaderSectorCount = 1;
}

public readonly record struct UyaFileBlock(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;

    public long OffsetBytes => (long)Offset * UyaLevelConstants.SectorSize;

    public long SectorLengthBytes => (long)Length * UyaLevelConstants.SectorSize;
}

public readonly record struct UyaByteBlock(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;
}

public sealed record UyaLevelInfoEntry(
    int LevelIndex,
    UyaFileBlock LevelAudioWad,
    UyaFileBlock LevelWad,
    UyaFileBlock LevelSceneWad);

public sealed record UyaLevelInfoSet(
    int RequestedLevelIndex,
    UyaLevelInfoEntry RequestedLevel);

public sealed record UyaLevelWad(
    int HeaderSize,
    int Sector,
    int Level,
    int ReverbType,
    UyaFileBlock Data,
    UyaFileBlock SoundBank,
    UyaFileBlock Gameplay,
    UyaFileBlock Occlusion,
    IReadOnlyList<UyaFileBlock> Chunks,
    IReadOnlyList<UyaFileBlock> ChunkBanks,
    byte[] HeaderBytes);

public sealed record UyaLevelDataWad(
    int HeaderSize,
    UyaByteBlock Overlay,
    UyaByteBlock CoreIndex,
    UyaByteBlock GsRam,
    UyaByteBlock HudHeader,
    IReadOnlyList<UyaByteBlock> HudBanks,
    UyaByteBlock CoreData,
    UyaByteBlock TransitionTextures,
    byte[] HeaderBytes);

public sealed record UyaLooseLevelWad(
    int LevelIndex,
    int HeaderSector,
    int PayloadBaseSector,
    int SectorCount,
    UyaLevelInfoSet LevelInfo,
    UyaLevelWad LevelWad,
    byte[] Bytes)
{
    public int ByteLength => Bytes.Length;
}

public sealed record UyaLevelWadPackage(
    UyaLevelWad LevelWad,
    IReadOnlyList<PackedFile> Files)
{
    public PackedFilePackage ToPackedPackage()
    {
        return PackedFilePackageBuilder.Pack(Files);
    }
}

public sealed record UyaMapExtractionSummary(
    string OutputDirectory,
    int FileCount,
    int SectorCount);

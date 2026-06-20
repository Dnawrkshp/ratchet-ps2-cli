using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.DL.Level;

public static class DlLevelConstants
{
    public const int SectorSize = Sector32.SizeInBytes;
    public const int BootWadInfoSector = 0x3e9;
    public const int WadInfoLevelsOffset = 0x0c430;
    public const int LevelInfoCount = 80;
    public const int LevelInfoSize = 0x18;
    public const int RetailLevelInfoTableOffset = (BootWadInfoSector * SectorSize) + WadInfoLevelsOffset;
    public const int LevelWadHeaderSize = 0x0c68;
    public const int LevelAudioWadHeaderSize = 0x02a0;
    public const int LevelSceneWadHeaderSize = 0x26f0;
    public const int LevelWadHeaderSectorCount = 2;
    public const int LevelAudioWadHeaderSectorCount = 1;
    public const int LevelSceneWadHeaderSectorCount = 5;
    public const int CoreLevelSegmentCount = 14;
    public const int CoreLevelSegmentTableLength = CoreLevelSegmentCount * 8;
}

public readonly record struct DlFileBlock(int Offset, int Length)
{
    public bool IsEmpty => Offset <= 0 || Length <= 0;

    public long OffsetBytes => (long)Offset * DlLevelConstants.SectorSize;

    public long SectorLengthBytes => (long)Length * DlLevelConstants.SectorSize;
}

public sealed record DlLevelInfoEntry(
    int LevelIndex,
    DlFileBlock LevelAudioWad,
    DlFileBlock LevelWad,
    DlFileBlock LevelSceneWad);

public sealed record DlLevelInfoSet(
    int RequestedLevelIndex,
    int MediaLevelIndex,
    DlLevelInfoEntry RequestedLevel,
    DlLevelInfoEntry MediaLevel);

public sealed record DlLevelWad(
    int HeaderSize,
    int Sector,
    int Level,
    int ReverbType,
    int MaxMissionSize1,
    int MaxMissionSize2,
    DlFileBlock Data,
    DlFileBlock CoreBank,
    IReadOnlyList<DlFileBlock> Chunks,
    IReadOnlyList<DlFileBlock> ChunkBanks,
    DlFileBlock GameplayCore,
    IReadOnlyList<DlFileBlock> GameplayMissionInstances,
    IReadOnlyList<DlFileBlock> GameplayMissionData,
    IReadOnlyList<DlFileBlock> MissionBanks,
    DlFileBlock ArtInstances,
    byte[] HeaderBytes);

public sealed record DlLevelAudioWad(
    int HeaderSize,
    int Sector,
    IReadOnlyList<DlFileBlock> AudioInstances,
    DlFileBlock UpgradeSample,
    DlFileBlock PlatinumBolt,
    DlFileBlock Spare,
    byte[] HeaderBytes);

public sealed record DlSceneBlock(
    int Index,
    int SpeechEnglishLeftOffset,
    int SpeechEnglishRightOffset,
    DlFileBlock Subtitles,
    int SpeechFrenchLeftOffset,
    int SpeechFrenchRightOffset,
    int SpeechGermanLeftOffset,
    int SpeechGermanRightOffset,
    int SpeechSpanishLeftOffset,
    int SpeechSpanishRightOffset,
    int SpeechItalianLeftOffset,
    int SpeechItalianRightOffset,
    DlFileBlock MobyLoad,
    IReadOnlyList<int> Chunks);

public sealed record DlLevelSceneWad(
    int HeaderSize,
    int Sector,
    IReadOnlyList<DlSceneBlock> Scenes,
    byte[] HeaderBytes);

public sealed record DlCoreLevelSegment(
    int Index,
    int HeaderOffset,
    int Offset,
    int Length,
    string Name,
    string SemanticName,
    byte[] RawBytes,
    byte[] PayloadBytes,
    bool WasCompressedWad,
    string OutputExtension);

public sealed record DlLooseLevelWad(
    int LevelIndex,
    int HeaderSector,
    int PayloadBaseSector,
    int SectorCount,
    DlLevelInfoSet LevelInfo,
    DlLevelWad LevelWad,
    byte[] Bytes)
{
    public int BaseSector => HeaderSector;

    public int ByteLength => Bytes.Length;
}

public sealed record DlLevelWadPackage(
    DlLevelWad LevelWad,
    IReadOnlyList<DlLevelWadFile> Files)
{
    public PackedFilePackage ToPackedPackage()
    {
        var entries = new PackedFileEntry[Files.Count];
        var totalLength = 0;

        for (var i = 0; i < Files.Count; i++)
        {
            var file = Files[i];
            entries[i] = new PackedFileEntry(file.Path, totalLength, file.Bytes.Length, file.ContentType);
            totalLength = checked(totalLength + file.Bytes.Length);
        }

        var packedBytes = new byte[totalLength];
        for (var i = 0; i < Files.Count; i++)
        {
            var file = Files[i];
            file.Bytes.AsSpan().CopyTo(packedBytes.AsSpan(entries[i].Offset, file.Bytes.Length));
        }

        return new PackedFilePackage(packedBytes, entries);
    }
}

public sealed record DlLevelWadFile(
    string Path,
    byte[] Bytes,
    string ContentType);

public sealed record PackedFilePackage(
    byte[] PackedBytes,
    IReadOnlyList<PackedFileEntry> Entries);

public sealed record PackedFileEntry(
    string Path,
    int Offset,
    int Length,
    string ContentType);

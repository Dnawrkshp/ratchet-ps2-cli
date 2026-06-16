using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.DL.Level;

public static class DlLevelInfoReader
{
    public static DlLevelInfoSet ReadLevelSet(Stream isoStream, int levelIndex)
    {
        ValidateIsoStream(isoStream);

        if (levelIndex < 0 || levelIndex >= DlLevelConstants.LevelInfoCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(levelIndex),
                $"DL level index must be between 0 and {DlLevelConstants.LevelInfoCount - 1}.");
        }

        var mediaLevelIndex = GetMediaLevelIndex(levelIndex);
        return new DlLevelInfoSet(
            levelIndex,
            mediaLevelIndex,
            ReadEntry(isoStream, levelIndex),
            ReadEntry(isoStream, mediaLevelIndex));
    }

    public static DlLevelInfoEntry ReadEntry(Stream isoStream, int levelIndex)
    {
        ValidateIsoStream(isoStream);

        if (levelIndex < 0 || levelIndex >= DlLevelConstants.LevelInfoCount)
        {
            throw new ArgumentOutOfRangeException(nameof(levelIndex));
        }

        var tableOffset = DlLevelConstants.RetailLevelInfoTableOffset;
        var entryOffset = checked(tableOffset + (levelIndex * DlLevelConstants.LevelInfoSize));
        if (entryOffset + DlLevelConstants.LevelInfoSize > isoStream.Length)
        {
            throw new InvalidDataException(
                $"DL levelinfo entry {levelIndex} at 0x{entryOffset:X} exceeds ISO stream length.");
        }

        isoStream.Position = entryOffset;
        return new DlLevelInfoEntry(
            levelIndex,
            new DlFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()),
            new DlFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()),
            new DlFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()));
    }

    public static byte[] ReadSectorBlock(Stream isoStream, DlFileBlock block)
    {
        return ReadSectorRelativeBlock(isoStream, baseSector: 0, block);
    }

    public static byte[] ReadSectorHeader(Stream isoStream, DlFileBlock block, int sectorCount)
    {
        if (sectorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectorCount));
        }

        if (block.IsEmpty)
        {
            return [];
        }

        return ReadAbsoluteSectorRange(isoStream, block.Offset, sectorCount);
    }

    public static byte[] ReadAbsoluteSectorRange(Stream isoStream, int absoluteSector, int sectorCount)
    {
        ValidateIsoStream(isoStream);

        if (absoluteSector < 0)
        {
            throw new InvalidDataException("DL sector ranges cannot start at a negative sector.");
        }

        if (sectorCount < 0)
        {
            throw new InvalidDataException("DL sector ranges cannot have a negative length.");
        }

        var offsetBytes = checked((long)absoluteSector * DlLevelConstants.SectorSize);
        var lengthBytes = checked((long)sectorCount * DlLevelConstants.SectorSize);

        if (offsetBytes + lengthBytes > isoStream.Length)
        {
            throw new InvalidDataException(
                $"DL sector range at sector 0x{absoluteSector:X} length 0x{sectorCount:X} exceeds ISO stream length.");
        }

        if (lengthBytes > int.MaxValue)
        {
            throw new InvalidDataException("DL sector range is too large to materialize.");
        }

        isoStream.Position = offsetBytes;
        return isoStream.ReadBytesExactly((int)lengthBytes);
    }

    public static byte[] ReadSectorRelativeBlock(Stream isoStream, int baseSector, DlFileBlock block)
    {
        return ReadRelativeBlock(isoStream, baseSector, block, lengthInSectors: true);
    }

    public static byte[] ReadByteLengthSectorRelativeBlock(Stream isoStream, int baseSector, DlFileBlock block)
    {
        return ReadRelativeBlock(isoStream, baseSector, block, lengthInSectors: false);
    }

    private static int GetMediaLevelIndex(int levelIndex)
    {
        if (levelIndex >= 0x28)
        {
            return 0;
        }

        return levelIndex > 0x13
            ? levelIndex - 0x14
            : levelIndex;
    }

    private static byte[] ReadRelativeBlock(
        Stream isoStream,
        int baseSector,
        DlFileBlock block,
        bool lengthInSectors)
    {
        ValidateIsoStream(isoStream);

        if (block.IsEmpty)
        {
            return [];
        }

        if (baseSector < 0 || block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException("DL fileblock cannot contain negative sector values.");
        }

        var absoluteSector = checked((long)baseSector + block.Offset);
        var offsetBytes = checked(absoluteSector * DlLevelConstants.SectorSize);
        var lengthBytes = lengthInSectors
            ? checked((long)block.Length * DlLevelConstants.SectorSize)
            : block.Length;

        if (offsetBytes + lengthBytes > isoStream.Length)
        {
            throw new InvalidDataException(
                $"DL fileblock at sector 0x{absoluteSector:X} length 0x{block.Length:X} exceeds ISO stream length.");
        }

        if (lengthBytes > int.MaxValue)
        {
            throw new InvalidDataException("DL fileblock is too large to materialize.");
        }

        isoStream.Position = offsetBytes;
        return isoStream.ReadBytesExactly((int)lengthBytes);
    }

    private static void ValidateIsoStream(Stream isoStream)
    {
        ArgumentNullException.ThrowIfNull(isoStream);

        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The provided ISO stream must be readable and seekable.", nameof(isoStream));
        }
    }
}

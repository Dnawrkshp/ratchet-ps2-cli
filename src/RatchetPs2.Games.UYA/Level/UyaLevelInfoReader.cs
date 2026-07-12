using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelInfoReader
{
    public static UyaLevelInfoSet ReadLevelSet(Stream isoStream, int levelIndex)
    {
        ValidateIsoStream(isoStream);

        if (levelIndex < 0 || levelIndex >= UyaLevelConstants.LevelInfoCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(levelIndex),
                $"UYA level index must be between 0 and {UyaLevelConstants.LevelInfoCount - 1}.");
        }

        return new UyaLevelInfoSet(levelIndex, ReadEntry(isoStream, levelIndex));
    }

    public static UyaLevelInfoEntry ReadEntry(Stream isoStream, int levelIndex)
    {
        ValidateIsoStream(isoStream);

        if (levelIndex < 0 || levelIndex >= UyaLevelConstants.LevelInfoCount)
        {
            throw new ArgumentOutOfRangeException(nameof(levelIndex));
        }

        var entryOffset = checked(UyaLevelConstants.RetailLevelInfoTableOffset + (levelIndex * UyaLevelConstants.LevelInfoSize));
        if (entryOffset + UyaLevelConstants.LevelInfoSize > isoStream.Length)
        {
            throw new InvalidDataException(
                $"UYA levelinfo entry {levelIndex} at 0x{entryOffset:X} exceeds ISO stream length.");
        }

        isoStream.Position = entryOffset;
        return new UyaLevelInfoEntry(
            levelIndex,
            new UyaFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()),
            new UyaFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()),
            new UyaFileBlock(isoStream.ReadInt32LittleEndian(), isoStream.ReadInt32LittleEndian()));
    }

    public static byte[] ReadSectorBlock(Stream isoStream, UyaFileBlock block)
    {
        return ReadSectorRelativeBlock(isoStream, baseSector: 0, block);
    }

    public static byte[] ReadSectorHeader(Stream isoStream, UyaFileBlock block, int sectorCount)
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
            throw new InvalidDataException("UYA sector ranges cannot start at a negative sector.");
        }

        if (sectorCount < 0)
        {
            throw new InvalidDataException("UYA sector ranges cannot have a negative length.");
        }

        var offsetBytes = checked((long)absoluteSector * UyaLevelConstants.SectorSize);
        var lengthBytes = checked((long)sectorCount * UyaLevelConstants.SectorSize);

        if (offsetBytes + lengthBytes > isoStream.Length)
        {
            throw new InvalidDataException(
                $"UYA sector range at sector 0x{absoluteSector:X} length 0x{sectorCount:X} exceeds ISO stream length.");
        }

        if (lengthBytes > int.MaxValue)
        {
            throw new InvalidDataException("UYA sector range is too large to materialize.");
        }

        isoStream.Position = offsetBytes;
        return isoStream.ReadBytesExactly((int)lengthBytes);
    }

    public static byte[] ReadSectorRelativeBlock(Stream isoStream, int baseSector, UyaFileBlock block)
    {
        ValidateIsoStream(isoStream);

        if (block.IsEmpty)
        {
            return [];
        }

        if (baseSector < 0 || block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException("UYA fileblock cannot contain negative sector values.");
        }

        var absoluteSector = checked((long)baseSector + block.Offset);
        var offsetBytes = checked(absoluteSector * UyaLevelConstants.SectorSize);
        var lengthBytes = checked((long)block.Length * UyaLevelConstants.SectorSize);

        if (offsetBytes + lengthBytes > isoStream.Length)
        {
            throw new InvalidDataException(
                $"UYA fileblock at sector 0x{absoluteSector:X} length 0x{block.Length:X} exceeds ISO stream length.");
        }

        if (lengthBytes > int.MaxValue)
        {
            throw new InvalidDataException("UYA fileblock is too large to materialize.");
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

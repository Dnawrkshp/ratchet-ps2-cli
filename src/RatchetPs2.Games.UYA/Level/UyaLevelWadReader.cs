using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadReader
{
    public static UyaLevelWad ReadLevelWad(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var headerSize = ReadHeaderSize(stream, UyaLevelConstants.LevelWadHeaderSize, nameof(UyaLevelWad));
        var headerBytes = ReadHeaderBytes(stream, headerSize);

        stream.Position = 4;
        var sector = stream.ReadInt32LittleEndian();
        var level = stream.ReadInt32LittleEndian();
        var reverb = stream.ReadInt32LittleEndian();

        var levelData = ReadSectorBlock(stream);
        var soundBank = ReadSectorBlock(stream);
        var gameplay = ReadSectorBlock(stream);
        var occlusion = ReadSectorBlock(stream);
        var chunks = ReadSectorBlocks(stream, 3);
        var chunkBanks = ReadSectorBlocks(stream, 3);

        return new UyaLevelWad(
            headerSize,
            sector,
            level,
            reverb,
            levelData,
            soundBank,
            gameplay,
            occlusion,
            chunks,
            chunkBanks,
            headerBytes);
    }

    public static UyaLevelDataWad ReadLevelDataWad(ReadOnlySpan<byte> data)
    {
        if (data.Length < UyaLevelConstants.LevelDataHeaderSize)
        {
            throw new InvalidDataException(
                $"UYA level data WAD is shorter than header size 0x{UyaLevelConstants.LevelDataHeaderSize:X}.");
        }

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var overlay = ReadByteBlock(stream);
        var coreIndex = ReadByteBlock(stream);
        var gsRam = ReadByteBlock(stream);
        var hudHeader = ReadByteBlock(stream);
        var hudBanks = ReadByteBlocks(stream, 5);
        var coreData = ReadByteBlock(stream);
        var transitionTextures = ReadByteBlock(stream);

        return new UyaLevelDataWad(
            UyaLevelConstants.LevelDataHeaderSize,
            overlay,
            coreIndex,
            gsRam,
            hudHeader,
            hudBanks,
            coreData,
            transitionTextures,
            data[..UyaLevelConstants.LevelDataHeaderSize].ToArray());
    }

    public static byte[] ReadSectorFileBlock(ReadOnlySpan<byte> container, UyaFileBlock block)
    {
        if (block.IsEmpty)
        {
            return [];
        }

        if (block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException("UYA sector fileblock cannot contain negative values.");
        }

        var offset = checked((long)block.Offset * UyaLevelConstants.SectorSize);
        var length = checked((long)block.Length * UyaLevelConstants.SectorSize);
        return ReadBlock(container, offset, length, "sector fileblock");
    }

    public static byte[] ReadByteFileBlock(ReadOnlySpan<byte> container, UyaByteBlock block)
    {
        if (block.IsEmpty)
        {
            return [];
        }

        if (block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException("UYA byte fileblock cannot contain negative values.");
        }

        return ReadBlock(container, block.Offset, block.Length, "byte fileblock");
    }

    private static byte[] ReadBlock(ReadOnlySpan<byte> container, long offset, long length, string description)
    {
        if (offset < 0 || length < 0 || offset + length > container.Length)
        {
            throw new InvalidDataException(
                $"UYA {description} offset 0x{offset:X} length 0x{length:X} exceeds container length 0x{container.Length:X}.");
        }

        if (length > int.MaxValue)
        {
            throw new InvalidDataException($"UYA {description} is too large to materialize.");
        }

        return container.Slice((int)offset, (int)length).ToArray();
    }

    private static int ReadHeaderSize(Stream stream, int expectedMinimumHeaderSize, string name)
    {
        stream.Position = 0;
        var headerSize = stream.ReadInt32LittleEndian();
        if (headerSize < expectedMinimumHeaderSize)
        {
            throw new InvalidDataException(
                $"{name} header size 0x{headerSize:X} is smaller than expected 0x{expectedMinimumHeaderSize:X}.");
        }

        if (headerSize > stream.Length)
        {
            throw new InvalidDataException($"{name} header exceeds stream length.");
        }

        return headerSize;
    }

    private static byte[] ReadHeaderBytes(Stream stream, int headerSize)
    {
        stream.Position = 0;
        return stream.ReadBytesExactly(headerSize);
    }

    private static UyaFileBlock ReadSectorBlock(Stream stream)
    {
        return new UyaFileBlock(stream.ReadInt32LittleEndian(), stream.ReadInt32LittleEndian());
    }

    private static IReadOnlyList<UyaFileBlock> ReadSectorBlocks(Stream stream, int count)
    {
        var blocks = new UyaFileBlock[count];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = ReadSectorBlock(stream);
        }

        return blocks;
    }

    private static UyaByteBlock ReadByteBlock(Stream stream)
    {
        return new UyaByteBlock(stream.ReadInt32LittleEndian(), stream.ReadInt32LittleEndian());
    }

    private static IReadOnlyList<UyaByteBlock> ReadByteBlocks(Stream stream, int count)
    {
        var blocks = new UyaByteBlock[count];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = ReadByteBlock(stream);
        }

        return blocks;
    }
}

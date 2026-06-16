using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.DL.Level;

public static class DlLevelWadReader
{
    public static DlLevelWad ReadLevelWad(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var headerSize = ReadHeaderSize(stream, DlLevelConstants.LevelWadHeaderSize, nameof(DlLevelWad));
        var headerBytes = ReadHeaderBytes(stream, headerSize);

        stream.Position = 4;
        var sector = stream.ReadInt32LittleEndian();
        var level = stream.ReadInt32LittleEndian();
        var reverb = stream.ReadInt32LittleEndian();
        var maxMissionSize1 = stream.ReadInt32LittleEndian();
        var maxMissionSize2 = stream.ReadInt32LittleEndian();

        var coreLevel = ReadFileBlock(stream);
        var coreBank = ReadFileBlock(stream);
        var chunks = ReadFileBlocks(stream, 3);
        var chunkBanks = ReadFileBlocks(stream, 3);
        var gameplayCore = ReadFileBlock(stream);
        var gameplayMissionInstances = ReadFileBlocks(stream, 128);
        var gameplayMissionData = ReadFileBlocks(stream, 128);
        var missionBanks = ReadFileBlocks(stream, 128);
        var artInstances = ReadFileBlock(stream);

        return new DlLevelWad(
            headerSize,
            sector,
            level,
            reverb,
            maxMissionSize1,
            maxMissionSize2,
            coreLevel,
            coreBank,
            chunks,
            chunkBanks,
            gameplayCore,
            gameplayMissionInstances,
            gameplayMissionData,
            missionBanks,
            artInstances,
            headerBytes);
    }

    public static DlLevelAudioWad ReadLevelAudioWad(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var headerSize = ReadHeaderSize(stream, DlLevelConstants.LevelAudioWadHeaderSize, nameof(DlLevelAudioWad));
        var headerBytes = ReadHeaderBytes(stream, headerSize);

        stream.Position = 4;
        var sector = stream.ReadInt32LittleEndian();
        var audioInstances = ReadFileBlocks(stream, 80);
        var upgradeSample = ReadFileBlock(stream);
        var platinumBolt = ReadFileBlock(stream);
        var spare = ReadFileBlock(stream);

        return new DlLevelAudioWad(
            headerSize,
            sector,
            audioInstances,
            upgradeSample,
            platinumBolt,
            spare,
            headerBytes);
    }

    public static DlLevelSceneWad ReadLevelSceneWad(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var headerSize = ReadHeaderSize(stream, DlLevelConstants.LevelSceneWadHeaderSize, nameof(DlLevelSceneWad));
        var headerBytes = ReadHeaderBytes(stream, headerSize);

        stream.Position = 4;
        var sector = stream.ReadInt32LittleEndian();
        var scenes = new DlSceneBlock[30];

        for (var i = 0; i < scenes.Length; i++)
        {
            scenes[i] = new DlSceneBlock(
                i,
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                ReadFileBlock(stream),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                ReadFileBlock(stream),
                ReadInt32Array(stream, 69));
        }

        return new DlLevelSceneWad(headerSize, sector, scenes, headerBytes);
    }

    public static bool IsHeaderOnlyLevelSceneWad(ReadOnlySpan<byte> data, DlLevelSceneWad sceneWad)
    {
        if (data.Length < sceneWad.HeaderSize)
        {
            return false;
        }

        if (sceneWad.Scenes.Any(scene => !IsEmptyScene(scene)))
        {
            return false;
        }

        for (var i = sceneWad.HeaderSize; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsEmptyScene(DlSceneBlock scene)
    {
        return scene.SpeechEnglishLeftOffset == 0
            && scene.SpeechEnglishRightOffset == 0
            && scene.Subtitles.Offset == 0
            && scene.Subtitles.Length == 0
            && scene.SpeechFrenchLeftOffset == 0
            && scene.SpeechFrenchRightOffset == 0
            && scene.SpeechGermanLeftOffset == 0
            && scene.SpeechGermanRightOffset == 0
            && scene.SpeechSpanishLeftOffset == 0
            && scene.SpeechSpanishRightOffset == 0
            && scene.SpeechItalianLeftOffset == 0
            && scene.SpeechItalianRightOffset == 0
            && scene.MobyLoad.Offset == 0
            && scene.MobyLoad.Length == 0
            && scene.Chunks.All(chunk => chunk == 0);
    }

    public static byte[] ReadSectorFileBlock(ReadOnlySpan<byte> container, DlFileBlock block)
    {
        return ReadFileBlock(container, block, lengthInSectors: true);
    }

    public static byte[] ReadByteLengthFileBlock(ReadOnlySpan<byte> container, DlFileBlock block)
    {
        return ReadFileBlock(container, block, lengthInSectors: false);
    }

    private static byte[] ReadFileBlock(ReadOnlySpan<byte> container, DlFileBlock block, bool lengthInSectors)
    {
        if (block.IsEmpty)
        {
            return [];
        }

        var offset = checked((long)block.Offset * DlLevelConstants.SectorSize);
        var length = lengthInSectors
            ? checked((long)block.Length * DlLevelConstants.SectorSize)
            : block.Length;

        if (offset < 0 || length < 0 || offset + length > container.Length)
        {
            throw new InvalidDataException(
                $"DL fileblock offset 0x{block.Offset:X} length 0x{block.Length:X} exceeds container length 0x{container.Length:X}.");
        }

        if (length > int.MaxValue)
        {
            throw new InvalidDataException("DL fileblock is too large to materialize.");
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

    private static DlFileBlock ReadFileBlock(Stream stream)
    {
        return new DlFileBlock(stream.ReadInt32LittleEndian(), stream.ReadInt32LittleEndian());
    }

    private static IReadOnlyList<DlFileBlock> ReadFileBlocks(Stream stream, int count)
    {
        var blocks = new DlFileBlock[count];
        for (var i = 0; i < blocks.Length; i++)
        {
            blocks[i] = ReadFileBlock(stream);
        }

        return blocks;
    }

    private static IReadOnlyList<int> ReadInt32Array(Stream stream, int count)
    {
        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = stream.ReadInt32LittleEndian();
        }

        return values;
    }
}

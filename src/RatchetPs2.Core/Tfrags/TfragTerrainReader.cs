using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Tfrags;

public static class TfragTerrainReader
{
    public const int TerrainHeaderSize = 0x40;
    public const int TfragRecordSize = 0x40;
    public const int TexturePrimitiveSize = 0x50;

    public static TfragTerrain Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var memory = new MemoryStream();
        input.CopyTo(memory);
        var bytes = memory.ToArray();
        return Read(bytes);
    }

    public static TfragTerrain Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < TerrainHeaderSize)
        {
            throw new InvalidDataException(
                $"Tfrag terrain stream is too small. Expected at least 0x{TerrainHeaderSize:X} bytes, got 0x{bytes.Length:X}.");
        }

        var tableOffset = BinarySpanReader.ReadInt32LittleEndian(bytes, 0x00);
        var tfragCount = BinarySpanReader.ReadInt32LittleEndian(bytes, 0x04);
        var tfragRadius = BinarySpanReader.ReadSingleLittleEndian(bytes, 0x08);
        var totalTfragCount = BinarySpanReader.ReadInt32LittleEndian(bytes, 0x0C);

        if (tableOffset < TerrainHeaderSize)
        {
            throw new InvalidDataException(
                $"Tfrag table offset 0x{tableOffset:X} is before the terrain header.");
        }

        if (tfragCount < 0)
        {
            throw new InvalidDataException($"Tfrag count {tfragCount} is invalid.");
        }

        var tableLength = checked(tfragCount * TfragRecordSize);
        BinarySpanReader.EnsureRange(bytes, tableOffset, tableLength, "Tfrag table");

        var dataOffsets = new int[tfragCount];
        for (var i = 0; i < tfragCount; i++)
        {
            var recordOffset = tableOffset + i * TfragRecordSize;
            var dataOffsetRaw = BinarySpanReader.ReadInt32LittleEndian(bytes, recordOffset + 0x10);
            dataOffsets[i] = checked(tableOffset + dataOffsetRaw);
        }

        var chunks = new List<TfragChunk>(tfragCount);
        for (var i = 0; i < tfragCount; i++)
        {
            var recordOffset = tableOffset + i * TfragRecordSize;
            var dataOffset = dataOffsets[i];
            var nextDataOffset = i + 1 < dataOffsets.Length && dataOffsets[i + 1] > dataOffset
                ? dataOffsets[i + 1]
                : bytes.Length;
            var dataLength = nextDataOffset - dataOffset;
            BinarySpanReader.EnsureRange(bytes, dataOffset, dataLength, $"Tfrag {i} data");

            var textureOffset = BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x1C);
            var textureCount = bytes[recordOffset + 0x28];
            var textureEntries = ReadTextureEntries(
                bytes,
                i,
                dataOffset,
                dataLength,
                textureOffset,
                textureCount);
            var rgbaEntries = ReadRgbaEntries(
                bytes,
                i,
                dataOffset,
                dataLength,
                BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x1E),
                bytes[recordOffset + 0x29]);

            chunks.Add(new TfragChunk(
                Index: i,
                RecordOffset: recordOffset,
                BoundingSphere: new TfragBoundingSphere(
                    BinarySpanReader.ReadSingleLittleEndian(bytes, recordOffset + 0x00),
                    BinarySpanReader.ReadSingleLittleEndian(bytes, recordOffset + 0x04),
                    BinarySpanReader.ReadSingleLittleEndian(bytes, recordOffset + 0x08),
                    BinarySpanReader.ReadSingleLittleEndian(bytes, recordOffset + 0x0C)),
                DataOffsetRaw: BinarySpanReader.ReadInt32LittleEndian(bytes, recordOffset + 0x10),
                DataOffset: dataOffset,
                DataLength: dataLength,
                Lod2Offset: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x14),
                SharedOffset: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x16),
                Lod1Offset: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x18),
                Lod0Offset: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x1A),
                TextureOffset: textureOffset,
                RgbaOffset: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x1E),
                CommonSize: bytes[recordOffset + 0x20],
                Lod2Size: bytes[recordOffset + 0x21],
                Lod1Size: bytes[recordOffset + 0x22],
                Lod0Size: bytes[recordOffset + 0x23],
                Lod2RgbaCount: bytes[recordOffset + 0x24],
                Lod1RgbaCount: bytes[recordOffset + 0x25],
                Lod0RgbaCount: bytes[recordOffset + 0x26],
                BaseOnly: bytes[recordOffset + 0x27],
                TextureCount: textureCount,
                RgbaSize: bytes[recordOffset + 0x29],
                RgbaVerticesLocation: bytes[recordOffset + 0x2A],
                OcclusionIndexStash: bytes[recordOffset + 0x2B],
                MSphereCount: bytes[recordOffset + 0x2C],
                Flags: bytes[recordOffset + 0x2D],
                MSphereOffset: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x2E),
                LightOffset: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x30),
                LightVertexStartOffset: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x32),
                DirectionalLightsOne: bytes[recordOffset + 0x34],
                DirectionalLightsUpdated: bytes[recordOffset + 0x35],
                PointLights: BinarySpanReader.ReadUInt16LittleEndian(bytes, recordOffset + 0x36),
                CubeOffset: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x38),
                OcclusionIndex: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x3A),
                VertexCount: bytes[recordOffset + 0x3C],
                TriangleCount: bytes[recordOffset + 0x3D],
                MipDistance: BinarySpanReader.ReadInt16LittleEndian(bytes, recordOffset + 0x3E),
                TextureEntries: textureEntries,
                RgbaEntries: rgbaEntries));
        }

        return new TfragTerrain(
            bytes.Length,
            tableOffset,
            tfragCount,
            tfragRadius,
            totalTfragCount,
            chunks,
            bytes.ToArray());
    }

    private static IReadOnlyList<TfragTextureEntry> ReadTextureEntries(
        ReadOnlySpan<byte> bytes,
        int chunkIndex,
        int dataOffset,
        int dataLength,
        ushort textureOffset,
        byte textureCount)
    {
        if (textureCount == 0)
        {
            return [];
        }

        var textureTableRelativeEnd = checked(textureOffset + textureCount * TexturePrimitiveSize);
        if (textureTableRelativeEnd > dataLength)
        {
            throw new InvalidDataException(
                $"Tfrag {chunkIndex} texture table range 0x{textureOffset:X}+0x{textureCount * TexturePrimitiveSize:X} exceeds chunk data length 0x{dataLength:X}.");
        }

        var entries = new List<TfragTextureEntry>(textureCount);
        for (var i = 0; i < textureCount; i++)
        {
            var entryOffset = dataOffset + textureOffset + i * TexturePrimitiveSize;
            entries.Add(new TfragTextureEntry(
                i,
                entryOffset,
                BinarySpanReader.ReadInt32LittleEndian(bytes, entryOffset),
                BinarySpanReader.ReadInt32LittleEndian(bytes, entryOffset + 0x20) != 0,
                BinarySpanReader.ReadInt32LittleEndian(bytes, entryOffset + 0x24) != 0));
        }

        return entries;
    }

    private static IReadOnlyList<TfragRgba> ReadRgbaEntries(
        ReadOnlySpan<byte> bytes,
        int chunkIndex,
        int dataOffset,
        int dataLength,
        ushort rgbaOffset,
        byte rgbaSize)
    {
        var rgbaCount = checked(rgbaSize * 4);
        if (rgbaCount == 0)
        {
            return [];
        }

        var rgbaByteLength = checked(rgbaCount * 4);
        if (rgbaOffset + rgbaByteLength > dataLength)
        {
            throw new InvalidDataException(
                $"Tfrag {chunkIndex} RGBA range 0x{rgbaOffset:X}+0x{rgbaByteLength:X} exceeds chunk data length 0x{dataLength:X}.");
        }

        var entries = new TfragRgba[rgbaCount];
        for (var i = 0; i < entries.Length; i++)
        {
            var offset = dataOffset + rgbaOffset + i * 4;
            entries[i] = new TfragRgba(
                bytes[offset + 0],
                bytes[offset + 1],
                bytes[offset + 2],
                bytes[offset + 3]);
        }

        return entries;
    }
}

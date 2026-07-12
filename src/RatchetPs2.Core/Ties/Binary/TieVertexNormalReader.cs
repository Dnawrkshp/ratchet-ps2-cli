using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TieVertexNormalReader
{
    private const int VertexNormalHeaderSize = 0x10;
    private const int VertexNormalRecordSize = 0x08;
    private const int VertexNormalRemapChunkHeaderSize = 0x30;
    private const int VertexNormalRemapNormalIndexMask = 0x3FFF;
    private const int VertexNormalRemapTargetIndexMask = 0x3FFC;

    public static List<TieVertexNormal> Read(
        byte[] bytes,
        TieClassHeader header,
        TieClassReadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (header.VertexNormalsOffset == 0 || header.VertexNormalsCount <= 0)
        {
            return [];
        }

        var count = header.VertexNormalsCount;
        var offset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        var headerSize = options.VertexNormalHeaderSize;
        if (headerSize < 0)
        {
            throw new InvalidDataException($"Vertex normal header size cannot be negative: {headerSize}.");
        }

        EnsureRange(
            bytes,
            offset,
            headerSize + count * VertexNormalRecordSize,
            "vertex normals");

        var normals = new List<TieVertexNormal>(count);
        var recordOffset = offset + headerSize;
        for (var i = 0; i < count; i++)
        {
            var normalOffset = recordOffset + i * VertexNormalRecordSize;
            normals.Add(new TieVertexNormal
            {
                Index = i,
                Offset = normalOffset,
                X = (sbyte)bytes[normalOffset],
                Y = (sbyte)bytes[normalOffset + 0x01],
                Z = (sbyte)bytes[normalOffset + 0x02],
                W = (sbyte)bytes[normalOffset + 0x03],
                Packed = BitConverter.ToUInt16(bytes, normalOffset + 0x06)
            });
        }

        return normals;
    }

    public static List<TieVertexNormalRemap> ReadRemaps(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TieLodTopology> lodTopologies,
        int vertexNormalCount)
    {
        return ReadLogicalVertexNormalRemaps(bytes, header, lodTopologies, vertexNormalCount);
    }

    private static List<TieVertexNormalRemap> ReadLogicalVertexNormalRemaps(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TieLodTopology> lodTopologies,
        int vertexNormalCount)
    {
        if (header.VertexNormalsOffset == 0 || header.VertexNormalsCount <= 0 || vertexNormalCount == 0)
        {
            return [];
        }

        var normalOffset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        var end = header.ShadersOffset > 0
            ? Math.Min(CheckedOffset(header.ShadersOffset, "shader table"), bytes.Length)
            : bytes.Length;
        var orderedTopologies = lodTopologies
            .Where(topology => topology.LogicalVertexCount > 0)
            .OrderBy(topology => topology.LodIndex)
            .ToArray();
        if (normalOffset >= end || orderedTopologies.Length == 0)
        {
            return [];
        }

        var remaps = new List<TieVertexNormalRemap>();
        foreach (var topology in orderedTopologies)
        {
            if (topology.LodIndex < 0
                || topology.LodIndex >= header.RgbaRemapOffsets.Length
                || header.RgbaRemapOffsets[topology.LodIndex] == 0)
            {
                continue;
            }

            if (!TryResolveRgbaRemapChunkOffset(
                    bytes,
                    header,
                    header.RgbaRemapOffsets[topology.LodIndex],
                    end,
                    out var chunkOffset)
                || !TryGetNormalRemapChunkSize(bytes, chunkOffset, end, out var payloadSize))
            {
                continue;
            }

            var payloadOffset = chunkOffset + VertexNormalRemapChunkHeaderSize;
            var payloadEnd = payloadOffset + payloadSize;
            for (var offset = payloadOffset; offset + sizeof(ushort) * 2 <= payloadEnd; offset += sizeof(ushort) * 2)
            {
                var rawNormal = BitConverter.ToUInt16(bytes, offset);
                var rawVertex = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
                if (TryDecodeNormalRemapNormalIndex(rawNormal, vertexNormalCount, out var normalIndex)
                    && TryDecodeNormalRemapTargetIndex(rawVertex, topology.LogicalVertexCount, out var logicalVertexIndex))
                {
                    var logicalVertex = topology.LogicalVertices[logicalVertexIndex];
                    remaps.Add(new TieVertexNormalRemap
                    {
                        ChunkIndex = topology.LodIndex,
                        LodIndex = topology.LodIndex,
                        PacketIndex = logicalVertex.PacketIndex,
                        Offset = offset,
                        NormalIndex = normalIndex,
                        VertexRowIndex = logicalVertex.VertexRowIndex
                            ?? logicalVertex.AddressRowIndex
                            ?? -1,
                        LogicalVertexIndex = logicalVertexIndex,
                        RawNormal = rawNormal,
                        RawVertex = rawVertex
                    });
                }
            }
        }

        return remaps;
    }

    internal static bool TryResolveRgbaRemapChunkOffset(
        byte[] bytes,
        TieClassHeader header,
        ushort rawOffset,
        int end,
        out int chunkOffset)
    {
        chunkOffset = 0;
        if (rawOffset == 0)
        {
            return false;
        }

        var normalOffset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        var relativeOffset = checked(normalOffset + rawOffset);
        if (TryGetNormalRemapChunkSize(bytes, relativeOffset, end, out _))
        {
            chunkOffset = relativeOffset;
            return true;
        }

        var absoluteOffset = rawOffset;
        if (absoluteOffset != relativeOffset
            && TryGetNormalRemapChunkSize(bytes, absoluteOffset, end, out _))
        {
            chunkOffset = absoluteOffset;
            return true;
        }

        return false;
    }

    private static bool TryGetNormalRemapChunkSize(
        byte[] bytes,
        int chunkOffset,
        int end,
        out int payloadSize)
    {
        payloadSize = 0;
        if (chunkOffset + VertexNormalRemapChunkHeaderSize > end)
        {
            return false;
        }

        payloadSize = BitConverter.ToUInt16(bytes, chunkOffset + 0x20);
        return payloadSize > 0
            && payloadSize % (sizeof(ushort) * 2) == 0
            && chunkOffset + VertexNormalRemapChunkHeaderSize + payloadSize <= end;
    }

    private static bool TryDecodeNormalRemapNormalIndex(ushort rawIndex, int count, out int index)
    {
        var unflagged = rawIndex & VertexNormalRemapNormalIndexMask;
        if (unflagged % 4 == 0)
        {
            index = unflagged / 4;
            return index >= 0 && index < count;
        }

        index = 0;
        return false;
    }

    private static bool TryDecodeNormalRemapTargetIndex(ushort rawIndex, int count, out int index)
    {
        var unflagged = rawIndex & VertexNormalRemapTargetIndexMask;
        index = unflagged / 4;
        return index >= 0 && index < count;
    }
}

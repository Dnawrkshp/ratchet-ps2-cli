using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TieVertexNormalReader
{
    private const int VertexNormalHeaderSize = 0x10;
    private const int VertexNormalRecordSize = 0x08;
    private const int VertexNormalRemapChunkHeaderSize = 0x30;
    private const int VertexNormalRemapNormalIndexMask = 0x3FFF;
    private const int VertexNormalRemapTargetIndexMask = 0x3FFC;

    public static List<TieVertexNormal> Read(byte[] bytes, TieClassHeader header)
    {
        if (header.VertexNormalsOffset == 0 || header.VertexNormalsCount <= 0)
        {
            return [];
        }

        var count = header.VertexNormalsCount;
        var offset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        EnsureRange(
            bytes,
            offset,
            VertexNormalHeaderSize + count * VertexNormalRecordSize,
            "vertex normals");

        var normals = new List<TieVertexNormal>(count);
        var recordOffset = offset + VertexNormalHeaderSize;
        for (var i = 0; i < count; i++)
        {
            var normalOffset = recordOffset + i * VertexNormalRecordSize;
            normals.Add(new TieVertexNormal
            {
                Index = i,
                Offset = normalOffset,
                X = BitConverter.ToInt16(bytes, normalOffset),
                Y = BitConverter.ToInt16(bytes, normalOffset + 0x02),
                Z = BitConverter.ToInt16(bytes, normalOffset + 0x04),
                W = BitConverter.ToInt16(bytes, normalOffset + 0x06)
            });
        }

        return normals;
    }

    public static List<TieVertexNormalRemap> ReadRemaps(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        IReadOnlyList<TieLodTopology> lodTopologies,
        int vertexNormalCount)
    {
        var remaps = ReadLogicalVertexNormalRemaps(bytes, header, lodTopologies, vertexNormalCount);
        remaps.AddRange(ReadPacketRowVertexNormalRemaps(bytes, header, packetDataBlocks, vertexNormalCount));
        return remaps;
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
        var cursor = checked(normalOffset + VertexNormalHeaderSize + header.VertexNormalsCount * VertexNormalRecordSize);
        var end = header.ShadersOffset > 0
            ? Math.Min(CheckedOffset(header.ShadersOffset, "shader table"), bytes.Length)
            : bytes.Length;
        var orderedTopologies = lodTopologies
            .Where(topology => topology.LogicalVertexCount > 0)
            .OrderBy(topology => topology.LodIndex)
            .ToArray();
        if (cursor >= end || orderedTopologies.Length == 0)
        {
            return [];
        }

        var remaps = new List<TieVertexNormalRemap>();
        var chunkIndex = 0;
        foreach (var topology in orderedTopologies)
        {
            if (!TryFindNormalRemapChunk(
                bytes,
                cursor,
                end,
                vertexNormalCount,
                topology.LogicalVertexCount,
                out var chunkOffset,
                out var payloadSize))
            {
                break;
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
                        ChunkIndex = chunkIndex,
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

            cursor = payloadEnd;
            chunkIndex++;
        }

        return remaps;
    }

    private static List<TieVertexNormalRemap> ReadPacketRowVertexNormalRemaps(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        int vertexNormalCount)
    {
        if (header.VertexNormalsOffset == 0 || header.VertexNormalsCount <= 0 || vertexNormalCount == 0)
        {
            return [];
        }

        var normalOffset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        var cursor = checked(normalOffset + VertexNormalHeaderSize + header.VertexNormalsCount * VertexNormalRecordSize);
        var end = header.ShadersOffset > 0
            ? Math.Min(CheckedOffset(header.ShadersOffset, "shader table"), bytes.Length)
            : bytes.Length;
        var orderedPacketDataBlocks = packetDataBlocks
            .OrderBy(block => block.LodIndex)
            .ThenBy(block => block.PacketIndex)
            .ToArray();
        var maxPacketVertexRowCount = orderedPacketDataBlocks.Length == 0
            ? 0
            : orderedPacketDataBlocks.Max(block => block.VertexRows.Count);
        if (cursor >= end || maxPacketVertexRowCount <= 0)
        {
            return [];
        }

        var remaps = new List<TieVertexNormalRemap>();
        var chunkIndex = 0;
        while (cursor + VertexNormalRemapChunkHeaderSize <= end)
        {
            if (!TryGetNormalRemapChunkSize(bytes, cursor, end, out var payloadSize))
            {
                var skippedCursor = cursor + 0x08;
                if (skippedCursor + VertexNormalRemapChunkHeaderSize > end
                    || !TryGetNormalRemapChunkSize(bytes, skippedCursor, end, out payloadSize))
                {
                    break;
                }

                cursor = skippedCursor;
            }

            var payloadOffset = cursor + VertexNormalRemapChunkHeaderSize;
            var payloadEnd = payloadOffset + payloadSize;
            for (var offset = payloadOffset; offset + sizeof(ushort) * 2 <= payloadEnd; offset += sizeof(ushort) * 2)
            {
                var rawNormal = BitConverter.ToUInt16(bytes, offset);
                var rawVertex = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
                var packetDataBlock = chunkIndex < orderedPacketDataBlocks.Length
                    ? orderedPacketDataBlocks[chunkIndex]
                    : null;
                if (packetDataBlock is not null
                    && TryDecodeNormalRemapNormalIndex(rawNormal, vertexNormalCount, out var normalIndex)
                    && TryDecodeNormalRemapTargetIndex(rawVertex, maxPacketVertexRowCount, out var vertexRowIndex)
                    && vertexRowIndex < packetDataBlock.VertexRows.Count)
                {
                    remaps.Add(new TieVertexNormalRemap
                    {
                        ChunkIndex = chunkIndex,
                        LodIndex = packetDataBlock.LodIndex,
                        PacketIndex = packetDataBlock.PacketIndex,
                        Offset = offset,
                        NormalIndex = normalIndex,
                        VertexRowIndex = vertexRowIndex,
                        RawNormal = rawNormal,
                        RawVertex = rawVertex
                    });
                }
            }

            cursor = payloadEnd;
            chunkIndex++;
        }

        return remaps;
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

    private static bool TryFindNormalRemapChunk(
        byte[] bytes,
        int startOffset,
        int end,
        int vertexNormalCount,
        int logicalVertexCount,
        out int chunkOffset,
        out int payloadSize)
    {
        for (var candidateOffset = startOffset;
             candidateOffset + VertexNormalRemapChunkHeaderSize <= end;
             candidateOffset += 0x04)
        {
            if (!TryGetNormalRemapChunkSize(bytes, candidateOffset, end, out payloadSize)
                || !HasPlausibleNormalRemapHeader(bytes, candidateOffset)
                || CountValidNormalRemapTargets(
                    bytes,
                    candidateOffset + VertexNormalRemapChunkHeaderSize,
                    payloadSize,
                    vertexNormalCount,
                    logicalVertexCount) == 0)
            {
                continue;
            }

            chunkOffset = candidateOffset;
            return true;
        }

        chunkOffset = 0;
        payloadSize = 0;
        return false;
    }

    private static bool HasPlausibleNormalRemapHeader(byte[] bytes, int chunkOffset)
    {
        for (var offset = chunkOffset + 0x08; offset < chunkOffset + 0x20; offset += sizeof(ushort))
        {
            if (BitConverter.ToUInt16(bytes, offset) != 0)
            {
                return false;
            }
        }

        return BitConverter.ToUInt16(bytes, chunkOffset + 0x24) == 0
            && BitConverter.ToUInt16(bytes, chunkOffset + 0x26) == 0
            && BitConverter.ToUInt16(bytes, chunkOffset + 0x28) == 0;
    }

    private static int CountValidNormalRemapTargets(
        byte[] bytes,
        int payloadOffset,
        int payloadSize,
        int vertexNormalCount,
        int logicalVertexCount)
    {
        var targetIndices = new HashSet<int>();
        var payloadEnd = payloadOffset + payloadSize;
        for (var offset = payloadOffset; offset + sizeof(ushort) * 2 <= payloadEnd; offset += sizeof(ushort) * 2)
        {
            var rawNormal = BitConverter.ToUInt16(bytes, offset);
            var rawVertex = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
            if (TryDecodeNormalRemapNormalIndex(rawNormal, vertexNormalCount, out _)
                && TryDecodeNormalRemapTargetIndex(rawVertex, logicalVertexCount, out var logicalVertexIndex))
            {
                targetIndices.Add(logicalVertexIndex);
            }
        }

        return targetIndices.Count;
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

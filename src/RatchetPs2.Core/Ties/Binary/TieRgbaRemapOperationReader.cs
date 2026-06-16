using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TieRgbaRemapOperationReader
{
    private const int RgbaRemapGroupHeaderSize = 0x20;
    private const int RgbaRemapGroupCountSize = 0x10;
    private const int RgbaRemapDirectRecordSize = 0x04;
    private const int RgbaRemapAverage2RecordSize = 0x04;
    private const int RgbaRemapWeightedAverage3To1RecordSize = 0x04;
    private const int RgbaRemapWeightedAverage2To1To1RecordSize = 0x08;
    private const int RgbaRemapAverage4RecordSize = 0x08;
    private const int RgbaRemapZeroRecordSize = 0x02;
    private const int RgbaRemapOffsetMask = 0x3FFC;
    private const int RgbaRemapSourceOffsetMask = 0x7FC;

    public static List<TieRgbaRemapOperation> Read(byte[] bytes, TieClassHeader header)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(header);

        if (header.VertexNormalsOffset == 0 || header.RgbaRemapOffsets.All(offset => offset == 0))
        {
            return [];
        }

        var normalOffset = CheckedOffset(header.VertexNormalsOffset, "vertex normals");
        var end = header.ShadersOffset > 0
            ? Math.Min(CheckedOffset(header.ShadersOffset, "shader table"), bytes.Length)
            : bytes.Length;
        var operations = new List<TieRgbaRemapOperation>();
        for (var lodIndex = 0; lodIndex < header.RgbaRemapOffsets.Length; lodIndex++)
        {
            var rgbaRemapOffset = header.RgbaRemapOffsets[lodIndex];
            if (rgbaRemapOffset == 0)
            {
                continue;
            }

            var chunkOffset = checked(normalOffset + rgbaRemapOffset);
            if (chunkOffset < 0 || chunkOffset + RgbaRemapGroupHeaderSize > end)
            {
                continue;
            }

            ReadLodOperations(bytes, chunkOffset, end, lodIndex, operations);
        }

        return operations;
    }

    private static void ReadLodOperations(
        byte[] bytes,
        int chunkOffset,
        int end,
        int lodIndex,
        List<TieRgbaRemapOperation> operations)
    {
        var groupOffset = chunkOffset + RgbaRemapGroupHeaderSize;
        var groupTargetSlotBase = 0;
        for (var groupIndex = 0; groupIndex < RgbaRemapGroupHeaderSize; groupIndex++)
        {
            var qwordCount = bytes[chunkOffset + groupIndex];
            if (qwordCount == 0)
            {
                break;
            }

            var groupByteLength = qwordCount * 0x10;
            if (groupByteLength < RgbaRemapGroupCountSize
                || groupOffset + groupByteLength > end)
            {
                break;
            }

            var groupOutputQwordCount = BitConverter.ToUInt16(bytes, groupOffset + 0x0c);
            ReadGroupOperations(
                bytes,
                groupOffset,
                groupByteLength,
                lodIndex,
                groupIndex,
                groupTargetSlotBase,
                operations);
            groupOffset += groupByteLength;
            groupTargetSlotBase = checked(groupTargetSlotBase + groupOutputQwordCount * 4);
        }
    }

    private static void ReadGroupOperations(
        byte[] bytes,
        int groupOffset,
        int groupByteLength,
        int lodIndex,
        int groupIndex,
        int groupTargetSlotBase,
        List<TieRgbaRemapOperation> operations)
    {
        var groupEnd = groupOffset + groupByteLength;
        var directByteCount = BitConverter.ToUInt16(bytes, groupOffset);
        var average2ByteCount = BitConverter.ToUInt16(bytes, groupOffset + sizeof(ushort));
        var weightedAverage3To1ByteCount = BitConverter.ToUInt16(bytes, groupOffset + sizeof(ushort) * 2);
        var weightedAverage2To1To1ByteCount = BitConverter.ToUInt16(bytes, groupOffset + sizeof(ushort) * 3);
        var average4ByteCount = BitConverter.ToUInt16(bytes, groupOffset + sizeof(ushort) * 4);
        var zeroByteCount = BitConverter.ToUInt16(bytes, groupOffset + sizeof(ushort) * 5);
        var cursor = groupOffset + RgbaRemapGroupCountSize;

        if (directByteCount % RgbaRemapDirectRecordSize == 0
            && cursor + directByteCount <= groupEnd)
        {
            var directEnd = cursor + directByteCount;
            var operationIndex = 0;
            for (var offset = cursor; offset + RgbaRemapDirectRecordSize <= directEnd; offset += RgbaRemapDirectRecordSize)
            {
                var sourceOffset = BitConverter.ToUInt16(bytes, offset);
                var targetOffset = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
                if (TryDecodeDirectOffset(sourceOffset, out var sourceSlot)
                    && TryDecodeDirectOffset(targetOffset, out var targetSlot))
                {
                    operations.Add(new TieRgbaRemapOperation
                    {
                        LodIndex = lodIndex,
                        GroupIndex = groupIndex,
                        OperationIndex = operationIndex,
                        Offset = offset,
                        Kind = TieRgbaRemapOperationKind.DirectCopy,
                        GroupTargetSlotBase = groupTargetSlotBase,
                        TargetSlot = targetSlot,
                        SourceSlots = [sourceSlot]
                    });
                }

                operationIndex++;
            }

            cursor = directEnd;
        }
        else
        {
            return;
        }

        if (average2ByteCount % RgbaRemapAverage2RecordSize != 0
            || cursor + average2ByteCount > groupEnd)
        {
            return;
        }

        var average2End = cursor + average2ByteCount;
        var average2OperationIndex = 0;
        for (var offset = cursor; offset + RgbaRemapAverage2RecordSize <= average2End; offset += RgbaRemapAverage2RecordSize)
        {
            var raw = BitConverter.ToUInt32(bytes, offset);
            var targetOffset = raw & RgbaRemapOffsetMask;
            var sourceAOffset = (raw >> 21) & RgbaRemapSourceOffsetMask;
            var sourceBOffset = (raw >> 12) & RgbaRemapSourceOffsetMask;
            if (!TryDecodePackedOffset(targetOffset, out var targetSlot)
                || !TryDecodePackedOffset(sourceAOffset, out var sourceASlot)
                || !TryDecodePackedOffset(sourceBOffset, out var sourceBSlot))
            {
                average2OperationIndex++;
                continue;
            }

            operations.Add(new TieRgbaRemapOperation
            {
                LodIndex = lodIndex,
                GroupIndex = groupIndex,
                OperationIndex = average2OperationIndex,
                Offset = offset,
                Kind = TieRgbaRemapOperationKind.Average2,
                GroupTargetSlotBase = groupTargetSlotBase,
                TargetSlot = targetSlot,
                SourceSlots = [sourceASlot, sourceBSlot]
            });
            average2OperationIndex++;
        }

        cursor = average2End;
        if (weightedAverage3To1ByteCount % RgbaRemapWeightedAverage3To1RecordSize != 0
            || cursor + weightedAverage3To1ByteCount > groupEnd)
        {
            return;
        }

        var weightedAverage3To1End = cursor + weightedAverage3To1ByteCount;
        var weightedAverage3To1OperationIndex = 0;
        for (var offset = cursor;
             offset + RgbaRemapWeightedAverage3To1RecordSize <= weightedAverage3To1End;
             offset += RgbaRemapWeightedAverage3To1RecordSize)
        {
            var raw = BitConverter.ToUInt32(bytes, offset);
            var targetOffset = raw & RgbaRemapOffsetMask;
            var sourceAOffset = (raw >> 12) & RgbaRemapSourceOffsetMask;
            var sourceBOffset = (raw >> 21) & RgbaRemapSourceOffsetMask;
            if (!TryDecodePackedOffset(targetOffset, out var targetSlot)
                || !TryDecodePackedOffset(sourceAOffset, out var sourceASlot)
                || !TryDecodePackedOffset(sourceBOffset, out var sourceBSlot))
            {
                weightedAverage3To1OperationIndex++;
                continue;
            }

            operations.Add(new TieRgbaRemapOperation
            {
                LodIndex = lodIndex,
                GroupIndex = groupIndex,
                OperationIndex = weightedAverage3To1OperationIndex,
                Offset = offset,
                Kind = TieRgbaRemapOperationKind.WeightedAverage3To1,
                GroupTargetSlotBase = groupTargetSlotBase,
                TargetSlot = targetSlot,
                SourceSlots = [sourceASlot, sourceASlot, sourceASlot, sourceBSlot]
            });
            weightedAverage3To1OperationIndex++;
        }

        cursor = weightedAverage3To1End;
        if (weightedAverage2To1To1ByteCount % RgbaRemapWeightedAverage2To1To1RecordSize != 0
            || cursor + weightedAverage2To1To1ByteCount > groupEnd)
        {
            return;
        }

        var weightedAverage2To1To1End = cursor + weightedAverage2To1To1ByteCount;
        var weightedAverage2To1To1OperationIndex = 0;
        for (var offset = cursor;
             offset + RgbaRemapWeightedAverage2To1To1RecordSize <= weightedAverage2To1To1End;
             offset += RgbaRemapWeightedAverage2To1To1RecordSize)
        {
            var sourceAOffset = BitConverter.ToUInt16(bytes, offset);
            var sourceBOffset = BitConverter.ToUInt16(bytes, offset + sizeof(ushort));
            var sourceCOffset = BitConverter.ToUInt16(bytes, offset + sizeof(ushort) * 2);
            var targetOffset = BitConverter.ToUInt16(bytes, offset + sizeof(ushort) * 3);
            if (!TryDecodeDirectOffset(targetOffset, out var targetSlot)
                || !TryDecodeDirectOffset(sourceAOffset, out var sourceASlot)
                || !TryDecodeDirectOffset(sourceBOffset, out var sourceBSlot)
                || !TryDecodeDirectOffset(sourceCOffset, out var sourceCSlot))
            {
                weightedAverage2To1To1OperationIndex++;
                continue;
            }

            operations.Add(new TieRgbaRemapOperation
            {
                LodIndex = lodIndex,
                GroupIndex = groupIndex,
                OperationIndex = weightedAverage2To1To1OperationIndex,
                Offset = offset,
                Kind = TieRgbaRemapOperationKind.WeightedAverage2To1To1,
                GroupTargetSlotBase = groupTargetSlotBase,
                TargetSlot = targetSlot,
                SourceSlots = [sourceASlot, sourceASlot, sourceBSlot, sourceCSlot]
            });
            weightedAverage2To1To1OperationIndex++;
        }

        cursor = weightedAverage2To1To1End;
        if (average4ByteCount % RgbaRemapAverage4RecordSize != 0
            || cursor + average4ByteCount > groupEnd)
        {
            return;
        }

        var average4End = cursor + average4ByteCount;
        var average4OperationIndex = 0;
        for (var offset = cursor; offset + RgbaRemapAverage4RecordSize <= average4End; offset += RgbaRemapAverage4RecordSize)
        {
            var packedSources = BitConverter.ToUInt32(bytes, offset);
            var sourceDOffset = BitConverter.ToUInt16(bytes, offset + sizeof(uint));
            var targetOffset = BitConverter.ToUInt16(bytes, offset + sizeof(uint) + sizeof(ushort));
            var sourceAOffset = packedSources & RgbaRemapSourceOffsetMask;
            var sourceBOffset = (packedSources >> 9) & RgbaRemapSourceOffsetMask;
            var sourceCOffset = (packedSources >> 18) & RgbaRemapSourceOffsetMask;
            if (!TryDecodeDirectOffset(targetOffset, out var targetSlot)
                || !TryDecodePackedOffset(sourceAOffset, out var sourceASlot)
                || !TryDecodePackedOffset(sourceBOffset, out var sourceBSlot)
                || !TryDecodePackedOffset(sourceCOffset, out var sourceCSlot)
                || !TryDecodeDirectOffset(sourceDOffset, out var sourceDSlot))
            {
                average4OperationIndex++;
                continue;
            }

            operations.Add(new TieRgbaRemapOperation
            {
                LodIndex = lodIndex,
                GroupIndex = groupIndex,
                OperationIndex = average4OperationIndex,
                Offset = offset,
                Kind = TieRgbaRemapOperationKind.Average4,
                GroupTargetSlotBase = groupTargetSlotBase,
                TargetSlot = targetSlot,
                SourceSlots = [sourceASlot, sourceBSlot, sourceCSlot, sourceDSlot]
            });
            average4OperationIndex++;
        }

        cursor = average4End;
        if (zeroByteCount % RgbaRemapZeroRecordSize != 0
            || cursor + zeroByteCount > groupEnd)
        {
            return;
        }
    }

    private static bool TryDecodeDirectOffset(ushort rawOffset, out int slot)
    {
        return TryDecodePackedOffset((uint)(rawOffset & RgbaRemapOffsetMask), out slot);
    }

    private static bool TryDecodePackedOffset(uint rawOffset, out int slot)
    {
        if ((rawOffset & 0x03) != 0)
        {
            slot = 0;
            return false;
        }

        slot = checked((int)rawOffset / 4);
        return slot >= 0;
    }
}

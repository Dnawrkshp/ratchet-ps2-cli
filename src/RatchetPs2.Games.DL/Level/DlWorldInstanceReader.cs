using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.DL.Level;

public static class DlWorldInstanceReader
{
    public const int PointerTableLength = 0x40;
    public const int DirectionalLightRecordSize = 0x40;
    public const int TieInstanceRecordSize = 0x60;
    public const int ShrubInstanceRecordSize = 0x70;
    public const int OcclusionMappingRecordSize = 0x08;

    public static DlWorldInstances Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < PointerTableLength)
        {
            throw new InvalidDataException("DL world instance data is too small to contain the 0x40-byte pointer table.");
        }

        var slots = ReadSlots(data);
        return new DlWorldInstances(
            data.Length,
            slots,
            ReadDirectionalLights(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x00)?.PayloadBytes),
            ReadClassIdList(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x04)?.PayloadBytes),
            ReadInstanceTable(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x08)?.PayloadBytes, TieInstanceRecordSize),
            ReadGroupTable(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x0c)?.PayloadBytes),
            ReadClassIdList(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x10)?.PayloadBytes),
            ReadInstanceTable(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x14)?.PayloadBytes, ShrubInstanceRecordSize),
            ReadGroupTable(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x18)?.PayloadBytes),
            ReadOcclusionMapping(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x1c)?.PayloadBytes),
            ReadTieInstanceColors(slots.FirstOrDefault(slot => slot.HeaderOffset == 0x20)?.PayloadBytes));
    }

    private static IReadOnlyList<DlWorldInstanceSlot> ReadSlots(ReadOnlySpan<byte> data)
    {
        var dataLength = data.Length;
        var slots = new List<DlWorldInstanceSlot>(PointerTableLength / sizeof(int));
        var pointers = new List<(int Index, int HeaderOffset, int Pointer)>(PointerTableLength / sizeof(int));

        for (var headerOffset = 0; headerOffset < PointerTableLength; headerOffset += sizeof(int))
        {
            pointers.Add((headerOffset / sizeof(int), headerOffset, ReadInt32LittleEndian(data, headerOffset)));
        }

        var sorted = pointers
            .Where(item => item.Pointer > 0 && item.Pointer < dataLength)
            .OrderBy(item => item.Pointer)
            .ToArray();

        foreach (var pointer in pointers)
        {
            if (pointer.Pointer < 0 || pointer.Pointer > dataLength)
            {
                throw new InvalidDataException(
                    $"DL world instance slot 0x{pointer.HeaderOffset:X2} points outside world instance bounds.");
            }

            byte[] payload = [];
            if (pointer.Pointer > 0 && pointer.Pointer < dataLength)
            {
                var nextPointer = sorted
                    .Where(item => item.Pointer > pointer.Pointer)
                    .Select(item => item.Pointer)
                    .DefaultIfEmpty(dataLength)
                    .Min();

                if (nextPointer < pointer.Pointer)
                {
                    throw new InvalidDataException(
                        $"DL world instance slot 0x{pointer.HeaderOffset:X2} has an invalid next pointer.");
                }

                payload = data.Slice(pointer.Pointer, nextPointer - pointer.Pointer).ToArray();
            }

            slots.Add(new DlWorldInstanceSlot(
                pointer.Index,
                pointer.HeaderOffset,
                pointer.Pointer,
                GetSemanticName(pointer.HeaderOffset),
                payload));
        }

        return slots;
    }

    private static DlDirectionalLightTable? ReadDirectionalLights(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        var count = payload.Length >= sizeof(int) ? ReadInt32LittleEndian(payload, 0) : 0;
        var expectedLength = checked(0x10 + Math.Max(count, 0) * DirectionalLightRecordSize);
        var isLengthValid = count >= 0 && expectedLength <= payload.Length;
        var recordCount = isLengthValid
            ? count
            : Math.Max(0, (payload.Length - 0x10) / DirectionalLightRecordSize);
        var records = new DlDirectionalLightRecord[recordCount];

        for (var i = 0; i < records.Length; i++)
        {
            var offset = 0x10 + (i * DirectionalLightRecordSize);
            records[i] = ReadDirectionalLightRecord(payload.AsSpan(offset, DirectionalLightRecordSize), i, offset);
        }

        return new DlDirectionalLightTable(
            count,
            DirectionalLightRecordSize,
            0x10,
            isLengthValid,
            isLengthValid ? payload.Length - expectedLength : 0,
            records);
    }

    private static DlDirectionalLightRecord ReadDirectionalLightRecord(ReadOnlySpan<byte> data, int index, int offset)
    {
        var vectors = new float[4][];
        for (var row = 0; row < vectors.Length; row++)
        {
            vectors[row] = new float[4];
            for (var column = 0; column < vectors[row].Length; column++)
            {
                vectors[row][column] = ReadSingleLittleEndian(data, (row * 0x10) + (column * sizeof(float)));
            }
        }

        return new DlDirectionalLightRecord(index, offset, vectors);
    }

    private static DlClassIdList? ReadClassIdList(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        var count = payload.Length >= sizeof(int) ? ReadInt32LittleEndian(payload, 0) : 0;
        var expectedLength = checked(sizeof(int) + Math.Max(count, 0) * sizeof(int));
        var isLengthValid = count >= 0 && expectedLength <= payload.Length;
        var idCount = isLengthValid
            ? count
            : Math.Max(0, (payload.Length - sizeof(int)) / sizeof(int));
        var classIds = new int[idCount];

        for (var i = 0; i < classIds.Length; i++)
        {
            classIds[i] = ReadInt32LittleEndian(payload, sizeof(int) + (i * sizeof(int)));
        }

        return new DlClassIdList(
            count,
            isLengthValid,
            isLengthValid ? payload.Length - expectedLength : 0,
            classIds);
    }

    private static DlCountedInstanceTable? ReadInstanceTable(byte[]? payload, int recordSize)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        var count = payload.Length >= sizeof(int) ? ReadInt32LittleEndian(payload, 0) : 0;
        var expectedLength = checked(0x10 + Math.Max(count, 0) * recordSize);
        var isLengthValid = count >= 0 && expectedLength <= payload.Length;
        return new DlCountedInstanceTable(
            count,
            recordSize,
            0x10,
            isLengthValid,
            isLengthValid ? payload.Length - expectedLength : 0);
    }

    private static DlWorldGroupTable? ReadGroupTable(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        if (payload.Length < 0x10)
        {
            return new DlWorldGroupTable(0, 0, 0x10, payload.Length, false, 0);
        }

        var groupCount = ReadInt32LittleEndian(payload, 0);
        var groupDataByteCount = ReadInt32LittleEndian(payload, 4);
        var groupOffsetsByteCount = checked(Math.Max(groupCount, 0) * sizeof(int));
        var groupDataStartOffset = 0x10 + Align(groupOffsetsByteCount, 0x10);
        var expectedLength = checked(groupDataStartOffset + Math.Max(groupDataByteCount, 0));
        var isLengthValid = groupCount >= 0
            && groupDataByteCount >= 0
            && expectedLength <= payload.Length;

        return new DlWorldGroupTable(
            groupCount,
            groupDataByteCount,
            groupDataStartOffset,
            payload.Length,
            isLengthValid,
            isLengthValid ? payload.Length - expectedLength : 0);
    }

    private static DlOcclusionMappingTable? ReadOcclusionMapping(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        if (payload.Length < 0x10)
        {
            return new DlOcclusionMappingTable(0, 0, 0, 0, 0x10, false, 0);
        }

        var tfragCount = ReadInt32LittleEndian(payload, 0);
        var tieCount = ReadInt32LittleEndian(payload, 4);
        var mobyCount = ReadInt32LittleEndian(payload, 8);
        var reserved = ReadInt32LittleEndian(payload, 12);
        var totalCount = checked(Math.Max(tfragCount, 0) + Math.Max(tieCount, 0) + Math.Max(mobyCount, 0));
        var expectedLength = Align(checked(0x10 + totalCount * OcclusionMappingRecordSize), 0x10);
        var isLengthValid = tfragCount >= 0
            && tieCount >= 0
            && mobyCount >= 0
            && expectedLength <= payload.Length;

        return new DlOcclusionMappingTable(
            tfragCount,
            tieCount,
            mobyCount,
            reserved,
            OcclusionMappingRecordSize,
            isLengthValid,
            isLengthValid ? payload.Length - expectedLength : 0);
    }

    private static DlTieInstanceColorTable? ReadTieInstanceColors(byte[]? payload)
    {
        if (payload is null || payload.Length == 0)
        {
            return null;
        }

        var mappedIds = new HashSet<int>();
        var entryCount = 0;
        var sentinelCount = 0;
        var duplicateIdCount = 0;
        var minInstanceId = int.MaxValue;
        var maxInstanceId = int.MinValue;
        int? malformedEntryOffset = null;
        var offset = 0;

        while (offset + 4 <= payload.Length)
        {
            var entryOffset = offset;
            var id = ReadInt16LittleEndian(payload, offset);
            var wordCount = ReadInt16LittleEndian(payload, offset + 2);
            offset += 4;

            if (wordCount < 0)
            {
                malformedEntryOffset = entryOffset;
                offset = entryOffset;
                break;
            }

            var wordByteCount = wordCount * sizeof(ushort);
            if (wordByteCount > payload.Length - offset)
            {
                malformedEntryOffset = entryOffset;
                offset = entryOffset;
                break;
            }

            entryCount++;
            if (id < 0)
            {
                sentinelCount++;
            }
            else if (!mappedIds.Add(id))
            {
                duplicateIdCount++;
            }
            else
            {
                minInstanceId = Math.Min(minInstanceId, id);
                maxInstanceId = Math.Max(maxInstanceId, id);
            }

            offset += wordByteCount;
            if ((offset & 1) != 0)
            {
                offset++;
            }
        }

        var isLengthValid = offset == payload.Length;
        var mappedInstanceCount = mappedIds.Count;
        return new DlTieInstanceColorTable(
            payload.Length,
            offset,
            isLengthValid,
            payload.Length - offset,
            entryCount,
            mappedInstanceCount,
            sentinelCount,
            duplicateIdCount,
            mappedInstanceCount == 0 ? null : minInstanceId,
            mappedInstanceCount == 0 ? null : maxInstanceId,
            malformedEntryOffset);
    }

    private static string GetSemanticName(int headerOffset)
    {
        return headerOffset switch
        {
            0x00 => "directional_lights",
            0x04 => "tie_class_ids",
            0x08 => "tie_instances",
            0x0c => "tie_groups",
            0x10 => "shrub_class_ids",
            0x14 => "shrub_instances",
            0x18 => "shrub_groups",
            0x1c => "occlusion_mapping",
            0x20 => "tie_instance_colors",
            _ => $"unknown_{headerOffset:X2}"
        };
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }

}

public sealed record DlWorldInstances(
    int Length,
    IReadOnlyList<DlWorldInstanceSlot> Slots,
    DlDirectionalLightTable? DirectionalLights,
    DlClassIdList? TieClasses,
    DlCountedInstanceTable? TieInstances,
    DlWorldGroupTable? TieGroups,
    DlClassIdList? ShrubClasses,
    DlCountedInstanceTable? ShrubInstances,
    DlWorldGroupTable? ShrubGroups,
    DlOcclusionMappingTable? OcclusionMapping,
    DlTieInstanceColorTable? TieInstanceColors);

public sealed record DlWorldInstanceSlot(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    byte[] PayloadBytes)
{
    public int Length => PayloadBytes.Length;
}

public sealed record DlDirectionalLightTable(
    int Count,
    int RecordSize,
    int DataOffset,
    bool IsLengthValid,
    int PaddingLength,
    IReadOnlyList<DlDirectionalLightRecord> Records);

public sealed record DlDirectionalLightRecord(
    int Index,
    int Offset,
    IReadOnlyList<IReadOnlyList<float>> Vectors);

public sealed record DlClassIdList(
    int Count,
    bool IsLengthValid,
    int PaddingLength,
    IReadOnlyList<int> ClassIds);

public sealed record DlCountedInstanceTable(
    int Count,
    int RecordSize,
    int DataOffset,
    bool IsLengthValid,
    int PaddingLength);

public sealed record DlWorldGroupTable(
    int GroupCount,
    int GroupDataByteCount,
    int GroupDataStartOffset,
    int PayloadLength,
    bool IsLengthValid,
    int PaddingLength);

public sealed record DlOcclusionMappingTable(
    int TfragCount,
    int TieCount,
    int MobyCount,
    int Reserved,
    int RecordSize,
    bool IsLengthValid,
    int PaddingLength);

public sealed record DlTieInstanceColorTable(
    int Length,
    int ParsedLength,
    bool IsLengthValid,
    int RemainingLength,
    int EntryCount,
    int MappedInstanceCount,
    int SentinelCount,
    int DuplicateIdCount,
    int? MinInstanceId,
    int? MaxInstanceId,
    int? MalformedEntryOffset);

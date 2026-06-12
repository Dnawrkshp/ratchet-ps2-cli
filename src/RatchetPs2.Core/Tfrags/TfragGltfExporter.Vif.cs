using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private const int VifCommandUnpackV3_16 = 0x69;
    private const int VifCommandUnpackV4_16 = 0x6D;
    private const int VifCommandUnpackV4_8 = 0x6E;
    private const int VifCommandStcycl = 0x01;
    private const int VifCommandStmod = 0x05;
    private const int VifCommandStmask = 0x20;
    private const int VifCommandStrow = 0x30;
    private const int VifCommandStcol = 0x31;

    private static TfragLodRecoveryLayout BuildRuntimeLodRecoveryLayout(TfragChunk chunk, int lodIndex)
    {
        var lod2Topology = BuildRuntimeSegmentRange(chunk, "lod_2_topology", chunk.Lod2Offset, chunk.SharedOffset);
        var common = BuildRuntimeSegmentRange(chunk, "common", chunk.SharedOffset, chunk.Lod1Offset);
        var lod1Topology = BuildRuntimeSegmentRange(chunk, "lod_1_topology", chunk.Lod1Offset, chunk.Lod0Offset);
        var lod01End = chunk.SharedOffset + checked(chunk.Lod1Size * 16);
        var lod01 = BuildRuntimeSegmentRange(chunk, "lod_01", chunk.Lod0Offset, lod01End);
        var lod0Topology = BuildRuntimeSegmentRange(chunk, "lod_0_topology", lod01End, chunk.RgbaOffset);

        switch (lodIndex)
        {
            case 0:
                return new TfragLodRecoveryLayout(
                    [common, lod01, lod0Topology],
                    lod0Topology,
                    TfragStripIndexOrder.StripsThenIndices);
            case 1:
                return new TfragLodRecoveryLayout(
                    [common, lod01, lod1Topology],
                    lod1Topology,
                    TfragStripIndexOrder.StripsThenIndices);
            case 2:
                return new TfragLodRecoveryLayout(
                    [common, lod2Topology],
                    lod2Topology,
                    TfragStripIndexOrder.IndicesThenStrips);
            default:
                throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "Tfrag LOD index must be 0, 1, or 2.");
        }
    }

    private static TfragLodSegment BuildRuntimeSegmentRange(
        TfragChunk chunk,
        string name,
        int relativeStart,
        int relativeEnd)
    {
        var expectedLength = Math.Max(0, relativeEnd - relativeStart);
        if (expectedLength <= 0 || relativeStart < 0 || relativeStart >= chunk.DataLength)
        {
            return new TfragLodSegment(
                name,
                chunk.DataOffset + Math.Clamp(relativeStart, 0, Math.Max(chunk.DataLength - 1, 0)),
                relativeStart,
                expectedLength,
                0,
                Truncated: expectedLength > 0);
        }

        var length = Math.Min(expectedLength, chunk.DataLength - relativeStart);
        return new TfragLodSegment(
            name,
            chunk.DataOffset + relativeStart,
            relativeStart,
            expectedLength,
            length,
            Truncated: length != expectedLength);
    }

    private static void ScanSegment(
        ReadOnlySpan<byte> bytes,
        TfragChunk chunk,
        TfragLodSegment segment,
        List<TfragPositionPacket> setupPackets,
        List<TfragPositionPacket> positionPackets,
        List<TfragPositionPacket> vertexReferencePackets,
        List<TfragTopologyPacket> topologyPackets,
        ref TfragVifState vifState,
        TfragReferenceState referenceState)
    {
        var endOffset = segment.Offset + segment.Length;
        for (var offset = segment.Offset; offset + 4 <= endOffset;)
        {
            var command = bytes[offset + 3] & 0x7F;
            var rowCount = bytes[offset + 2];
            var immediate = BinarySpanReader.ReadUInt16LittleEndian(bytes, offset);
            var payloadLength = GetVifUnpackPayloadLength(command, rowCount);
            if (payloadLength is null)
            {
                var commandPayloadLength = GetVifCommandPayloadLength(command, immediate, rowCount);
                var commandPayloadOffset = offset + 4;
                if (commandPayloadOffset + commandPayloadLength > endOffset)
                {
                    offset += 4;
                    continue;
                }

                ApplyVifCommand(bytes.Slice(commandPayloadOffset, commandPayloadLength), command, immediate, ref vifState);
                offset = commandPayloadOffset + commandPayloadLength;
                continue;
            }

            var payloadOffset = offset + 4;
            var alignedPayloadLength = Align4(payloadLength.Value);
            if (payloadOffset + alignedPayloadLength > endOffset)
            {
                offset += 4;
                continue;
            }

            if (command == VifCommandUnpackV3_16)
            {
                var payload = bytes.Slice(payloadOffset, payloadLength.Value);
                if (IsPlausibleCoordinatePacket(payload, chunk, immediate))
                {
                    positionPackets.Add(ReadV3PositionPacket(
                        segment,
                        offset,
                        immediate,
                        rowCount,
                        payload,
                        vifState));
                }
            }
            else if (command == VifCommandUnpackV4_16)
            {
                var payload = bytes.Slice(payloadOffset, payloadLength.Value);
                var address = immediate & 0x03FF;
                if (address == 0 && rowCount is > 0 and <= 8)
                {
                    setupPackets.Add(ReadV4PositionPacket(
                        segment,
                        offset,
                        immediate,
                        rowCount,
                        payload));
                }
                else if (IsPlausibleVertexReferencePacket(payload, chunk, immediate))
                {
                    var vertexReferencePacket = ReadV4PositionPacket(
                        segment,
                        offset,
                        immediate,
                        rowCount,
                        payload);
                    vertexReferencePackets.Add(vertexReferencePacket);
                    ApplyVertexReferencePacket(vertexReferencePacket, referenceState);
                }
            }
            else if (command == VifCommandUnpackV4_8)
            {
                var payload = bytes.Slice(payloadOffset, payloadLength.Value);
                if (IsLikelyDrawControlTopologyPacket(payload, immediate)
                    || IsLikelyControlStripDataPacket(payload, chunk, immediate, vifState, topologyPackets)
                    || IsPlausibleTopologyPacket(payload, chunk, immediate))
                {
                    topologyPackets.Add(new TfragTopologyPacket(
                        segment.Name,
                        offset,
                        offset - chunk.DataOffset,
                        immediate,
                        immediate & 0x03FF,
                        rowCount,
                        vifState.Mode == 1,
                        vifState.Mode == 1 ? vifState.RowX : 0,
                        vifState.Mode == 1 ? vifState.RowY : 0,
                        vifState.Mode == 1 ? vifState.RowZ : 0,
                        vifState.Mode == 1 ? vifState.RowW : 0,
                        payload.ToArray(),
                        referenceState.SnapshotTexCoords()));
                }
            }

            offset = payloadOffset + alignedPayloadLength;
        }
    }

    private static int? GetVifUnpackPayloadLength(int command, int rowCount)
    {
        if (command is < 0x60 or > 0x7F)
        {
            return null;
        }

        var unpackFormat = command & 0x0F;
        var componentCount = ((unpackFormat >> 2) & 0x03) + 1;
        var valueSizeBytes = (unpackFormat & 0x03) switch
        {
            0 => 4,
            1 => 2,
            2 => 1,
            // V4_5 packs one 16-bit value per row. Other *_5 forms are not valid VIF unpack formats.
            3 when componentCount == 4 => 2,
            _ => 0
        };
        if (valueSizeBytes == 0)
        {
            return null;
        }

        var effectiveRowCount = rowCount == 0 ? 256 : rowCount;
        return checked(effectiveRowCount * componentCount * valueSizeBytes);
    }

    private static int GetVifCommandPayloadLength(int command, ushort immediate, int rowCount)
    {
        return command switch
        {
            VifCommandStmask => 4,
            VifCommandStrow or VifCommandStcol => 16,
            _ => 0
        };
    }

    private static void ApplyVifCommand(
        ReadOnlySpan<byte> payload,
        int command,
        ushort immediate,
        ref TfragVifState state)
    {
        switch (command)
        {
            case VifCommandStcycl:
                state = state with { Cycle = immediate };
                break;
            case VifCommandStmod:
                state = state with { Mode = immediate & 0x03 };
                break;
            case VifCommandStrow when payload.Length >= 16:
                state = state with
                {
                    RowX = BinarySpanReader.ReadInt32LittleEndian(payload, 0x00),
                    RowY = BinarySpanReader.ReadInt32LittleEndian(payload, 0x04),
                    RowZ = BinarySpanReader.ReadInt32LittleEndian(payload, 0x08),
                    RowW = BinarySpanReader.ReadInt32LittleEndian(payload, 0x0C)
                };
                break;
        }
    }

    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static bool IsPlausibleCoordinatePacket(
        ReadOnlySpan<byte> payload,
        TfragChunk chunk,
        ushort immediate)
    {
        var rowCount = payload.Length / 6;
        if (rowCount <= 0 || rowCount > Math.Max(192, chunk.VertexCount + 64))
        {
            return false;
        }

        var address = immediate & 0x03FF;
        if (address is < 0x0E or > 0x180)
        {
            return false;
        }

        var nonZeroRows = 0;
        for (var i = 0; i < rowCount; i++)
        {
            var offset = i * 6;
            var x = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 0);
            var y = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 2);
            var z = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 4);
            if (x != 0 || y != 0 || z != 0)
            {
                nonZeroRows++;
            }
        }

        return nonZeroRows > 0 || rowCount == 1;
    }

    private static bool IsPlausibleVertexReferencePacket(
        ReadOnlySpan<byte> payload,
        TfragChunk chunk,
        ushort immediate)
    {
        var rowCount = payload.Length / 8;
        if (rowCount <= 0 || rowCount > Math.Max(192, chunk.VertexCount + 64))
        {
            return false;
        }

        var address = immediate & 0x03FF;
        if (address is < 0x10 or > 0x240)
        {
            return false;
        }

        if (rowCount == 1)
        {
            var w = BinarySpanReader.ReadInt16LittleEndian(payload, 0x06);
            return w >= 0 && (w & 1) == 0;
        }

        var nonZeroRows = 0;
        var largeRows = 0;
        for (var i = 0; i < rowCount; i++)
        {
            var offset = i * 8;
            var x = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 0);
            var y = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 2);
            var z = BinarySpanReader.ReadInt16LittleEndian(payload, offset + 4);
            if (x != 0 || y != 0 || z != 0)
            {
                nonZeroRows++;
            }

            if (Math.Max(Math.Max(Math.Abs((int)x), Math.Abs((int)y)), Math.Abs((int)z)) > 256)
            {
                largeRows++;
            }
        }

        return nonZeroRows >= Math.Min(3, rowCount)
            && largeRows >= Math.Min(2, Math.Max(1, rowCount / 8));
    }

    private static bool IsPlausibleTopologyPacket(
        ReadOnlySpan<byte> payload,
        TfragChunk chunk,
        ushort immediate)
    {
        var rowCount = payload.Length / 4;
        if (rowCount <= 0 || rowCount > 128 || (immediate & 0x8000) == 0)
        {
            return false;
        }

        var maxVertexReference = Math.Max(8, chunk.VertexCount + 32);
        var usefulTokens = 0;
        var invalidTokens = 0;
        var tokenStartOffset = payload.Length == 4 ? 0 : 4;
        for (var i = tokenStartOffset; i < payload.Length; i++)
        {
            var raw = payload[i];
            var decoded = (raw & 0x7F) - 1;
            if (decoded >= 0 && decoded <= maxVertexReference)
            {
                usefulTokens++;
            }
            else if (raw != 0x80 && raw != 0)
            {
                invalidTokens++;
            }
        }

        return usefulTokens >= 3 && invalidTokens <= Math.Max(2, usefulTokens / 4);
    }

    private static bool IsLikelyControlStripDataPacket(
        ReadOnlySpan<byte> payload,
        TfragChunk chunk,
        ushort immediate,
        TfragVifState vifState,
        IReadOnlyList<TfragTopologyPacket> topologyPackets)
    {
        if ((immediate & 0xC000) != 0xC000
            || payload.Length == 0
            || payload.Length % 4 != 0
            || vifState.Mode != 1
            || !topologyPackets.Any(IsDrawControlTopologyPacket))
        {
            return false;
        }

        if (vifState.RowX != vifState.RowY
            || vifState.RowX != vifState.RowZ
            || vifState.RowX != vifState.RowW
            || vifState.RowX is < 0x10 or > 0x240)
        {
            return false;
        }

        if (payload.Length > Math.Max(256, (chunk.VertexCount + 128) * 4))
        {
            return false;
        }

        var distinctTokens = new HashSet<byte>();
        foreach (var raw in payload)
        {
            distinctTokens.Add(raw);
        }

        return distinctTokens.Count >= 3;
    }

    private static TfragPositionPacket ReadV3PositionPacket(
        TfragLodSegment segment,
        int packetOffset,
        ushort immediate,
        int rowCount,
        ReadOnlySpan<byte> payload,
        TfragVifState vifState)
    {
        var positions = new TfragSourcePosition[rowCount];
        var usesVifBase = vifState.Mode == 1;
        for (var i = 0; i < rowCount; i++)
        {
            var offset = i * 6;
            positions[i] = new TfragSourcePosition(
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 0),
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 2),
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 4),
                0,
                usesVifBase,
                usesVifBase ? vifState.RowX : 0,
                usesVifBase ? vifState.RowY : 0,
                usesVifBase ? vifState.RowZ : 0,
                usesVifBase ? vifState.RowW : 0,
                packetOffset,
                i);
        }

        return new TfragPositionPacket(
            segment.Name,
            packetOffset,
            packetOffset - segment.Offset + segment.RelativeOffset,
            immediate,
            immediate & 0x03FF,
            rowCount,
            usesVifBase,
            usesVifBase ? vifState.RowX : 0,
            usesVifBase ? vifState.RowY : 0,
            usesVifBase ? vifState.RowZ : 0,
            usesVifBase ? vifState.RowW : 0,
            positions);
    }

    private static TfragPositionPacket ReadV4PositionPacket(
        TfragLodSegment segment,
        int packetOffset,
        ushort immediate,
        int rowCount,
        ReadOnlySpan<byte> payload)
    {
        var positions = new TfragSourcePosition[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            var offset = i * 8;
            positions[i] = new TfragSourcePosition(
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 0),
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 2),
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 4),
                BinarySpanReader.ReadInt16LittleEndian(payload, offset + 6),
                HasVifBase: false,
                BaseX: 0,
                BaseY: 0,
                BaseZ: 0,
                BaseW: 0,
                packetOffset,
                i);
        }

        return new TfragPositionPacket(
            segment.Name,
            packetOffset,
            packetOffset - segment.Offset + segment.RelativeOffset,
            immediate,
            immediate & 0x03FF,
            rowCount,
            UsesVifBase: false,
            BaseX: 0,
            BaseY: 0,
            BaseZ: 0,
            BaseW: 0,
            positions);
    }

    private static Vector3 ConvertPosition(
        TfragChunk chunk,
        TfragSourcePosition position,
        TfragGltfExportOptions options)
    {
        var baseX = position.HasVifBase ? position.BaseX : chunk.BoundingSphere.X;
        var baseY = position.HasVifBase ? position.BaseY : chunk.BoundingSphere.Y;
        var baseZ = position.HasVifBase ? position.BaseZ : chunk.BoundingSphere.Z;
        return GltfCoordinateBasis.FromPs2Position(
            baseX * options.WorldPositionScale + position.X * options.LocalPositionScale,
            baseY * options.WorldPositionScale + position.Y * options.LocalPositionScale,
            baseZ * options.WorldPositionScale + position.Z * options.LocalPositionScale);
    }

    private static int[] BuildTopologyPositionLookup(
        IReadOnlyList<TfragPositionPacket> vertexReferencePackets,
        int sourcePositionCount)
    {
        var lookup = Enumerable.Repeat(-1, 1024).ToArray();
        if (vertexReferencePackets.Count == 0 || sourcePositionCount == 0)
        {
            return lookup;
        }

        foreach (var packet in vertexReferencePackets)
        {
            for (var rowIndex = 0; rowIndex < packet.Positions.Count; rowIndex++)
            {
                var referenceAddress = packet.Address + rowIndex;
                var reference = packet.Positions[rowIndex];
                if (reference.W < 0 || (reference.W & 1) != 0)
                {
                    continue;
                }

                var sourceIndex = reference.W / 2;
                if ((uint)sourceIndex >= sourcePositionCount)
                {
                    continue;
                }

                if ((uint)referenceAddress < lookup.Length && lookup[referenceAddress] < 0)
                {
                    lookup[referenceAddress] = sourceIndex;
                }
            }
        }

        return lookup;
    }

    private static void ApplyVertexReferencePacket(
        TfragPositionPacket packet,
        TfragReferenceState referenceState)
    {
        for (var rowIndex = 0; rowIndex < packet.Positions.Count; rowIndex++)
        {
            var referenceAddress = packet.Address + rowIndex;
            if ((uint)referenceAddress >= referenceState.TexCoords.Length)
            {
                continue;
            }

            var reference = packet.Positions[rowIndex];
            referenceState.TexCoords[referenceAddress] = new Vector2(
                DecodeTfragTexCoordComponent(reference.X),
                DecodeTfragTexCoordComponent(reference.Y));
        }
    }

    private static Vector2?[] BuildReferenceTexCoordLookup(
        IReadOnlyList<TfragPositionPacket> vertexReferencePackets)
    {
        var texCoords = new Vector2?[1024];
        if (vertexReferencePackets.Count == 0)
        {
            return texCoords;
        }

        foreach (var packet in vertexReferencePackets)
        {
            for (var rowIndex = 0; rowIndex < packet.Positions.Count; rowIndex++)
            {
                var referenceAddress = packet.Address + rowIndex;
                if ((uint)referenceAddress >= texCoords.Length || texCoords[referenceAddress].HasValue)
                {
                    continue;
                }

                var reference = packet.Positions[rowIndex];
                texCoords[referenceAddress] = new Vector2(
                    DecodeTfragTexCoordComponent(reference.X),
                    DecodeTfragTexCoordComponent(reference.Y));
            }
        }

        return texCoords;
    }

    private static float DecodeTfragTexCoordComponent(short value)
    {
        var texCoord = value / 4096f;
        return texCoord < 0f
            ? texCoord * 0.5f
            : texCoord;
    }
}

using System.Numerics;
using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private static TfragTopologyDecode? TryDecodeWrenchStripTopology(
        ReadOnlySpan<byte> sourceBytes,
        TfragLodRecoveryLayout layout,
        IReadOnlyList<TfragPositionPacket> vertexReferencePackets,
        IReadOnlyList<Vector3> positions,
        int sourcePositionCount,
        IReadOnlyList<Vector2?> referenceTexCoords,
        IReadOnlyList<TfragTextureEntry> textureEntries,
        float? maxTriangleEdgeLength)
    {
        if (positions.Count == 0)
        {
            return null;
        }

        var vertexInfoRows = BuildVertexInfoRows(vertexReferencePackets, sourcePositionCount);
        if (vertexInfoRows.Count == 0)
        {
            return null;
        }

        var topologyUnpacks = ReadRawUnpackPackets(sourceBytes, layout.TopologySegment)
            .Where(packet => packet.Command == VifCommandUnpackV4_8)
            .ToArray();
        if (topologyUnpacks.Length < 2)
        {
            return null;
        }

        TfragRawUnpackPacket stripPacket;
        TfragRawUnpackPacket indexPacket;
        switch (layout.StripIndexOrder)
        {
            case TfragStripIndexOrder.IndicesThenStrips:
                indexPacket = topologyUnpacks[0];
                stripPacket = topologyUnpacks[1];
                break;
            case TfragStripIndexOrder.StripsThenIndices:
                stripPacket = topologyUnpacks[0];
                indexPacket = topologyUnpacks[1];
                break;
            default:
                return null;
        }

        if (stripPacket.Payload.Length < 4 || indexPacket.Payload.Length == 0)
        {
            return null;
        }

        var packet = new TfragTopologyPacket(
            stripPacket.SegmentName,
            stripPacket.Offset,
            stripPacket.RelativeOffset,
            stripPacket.Immediate,
            stripPacket.Address,
            stripPacket.RowCount,
            UsesVifBase: false,
            BaseX: 0,
            BaseY: 0,
            BaseZ: 0,
            BaseW: 0,
            stripPacket.Payload,
            vertexInfoRows
                .Select(row => (Vector2?)row.TexCoord)
                .ToArray());

        return DecodeWrenchStripTopologyPacket(
            packet,
            indexPacket.Payload,
            vertexInfoRows,
            positions,
            textureEntries,
            maxTriangleEdgeLength);
    }

    private static IReadOnlyList<TfragRawUnpackPacket> ReadRawUnpackPackets(
        ReadOnlySpan<byte> bytes,
        TfragLodSegment segment)
    {
        var packets = new List<TfragRawUnpackPacket>();
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

            packets.Add(new TfragRawUnpackPacket(
                segment.Name,
                offset,
                offset - segment.Offset + segment.RelativeOffset,
                immediate,
                immediate & 0x03FF,
                rowCount,
                command,
                bytes.Slice(payloadOffset, payloadLength.Value).ToArray()));
            offset = payloadOffset + alignedPayloadLength;
        }

        return packets;
    }

    private static IReadOnlyList<TfragVertexInfoRow> BuildVertexInfoRows(
        IReadOnlyList<TfragPositionPacket> vertexReferencePackets,
        int sourcePositionCount)
    {
        var rows = new List<TfragVertexInfoRow>();
        foreach (var packet in vertexReferencePackets)
        {
            for (var rowIndex = 0; rowIndex < packet.Positions.Count; rowIndex++)
            {
                var reference = packet.Positions[rowIndex];
                var sourceIndex = reference.W >= 0 && (reference.W & 1) == 0
                    ? reference.W / 2
                    : -1;
                if ((uint)sourceIndex >= (uint)sourcePositionCount)
                {
                    sourceIndex = -1;
                }

                rows.Add(new TfragVertexInfoRow(
                    rows.Count,
                    sourceIndex,
                    new Vector2(
                        DecodeTfragTexCoordComponent(reference.X),
                        DecodeTfragTexCoordComponent(reference.Y))));
            }
        }

        return rows;
    }

    private static TfragTopologyDecode DecodeWrenchStripTopologyPacket(
        TfragTopologyPacket packet,
        IReadOnlyList<byte> stripIndices,
        IReadOnlyList<TfragVertexInfoRow> vertexInfoRows,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<TfragTextureEntry> textureEntries,
        float? maxTriangleEdgeLength)
    {
        var indices = new List<uint>();
        var referenceAddresses = new List<int>();
        var materialRanges = new List<TfragMaterialRange>();
        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        var rawTriangleCount = 0;
        var rejectedDegenerateTriangleCount = 0;
        var rejectedInvalidTriangleCount = 0;
        var rejectedDuplicateTriangleCount = 0;
        var rejectedLongEdgeTriangleCount = 0;
        var indexOffset = 0;
        var activeTextureSlot = textureEntries.Count > 0 ? 0 : -1;

        for (var stripOffset = 0; stripOffset + 3 < packet.Payload.Length; stripOffset += 4)
        {
            var vertexCount = (int)(sbyte)packet.Payload[stripOffset];
            if (vertexCount <= 0)
            {
                if (vertexCount == 0)
                {
                    break;
                }

                var adGifOffset = (sbyte)packet.Payload[stripOffset + 2];
                if (adGifOffset >= 0 && textureEntries.Count != 0)
                {
                    activeTextureSlot = adGifOffset / 0x5;
                }

                vertexCount += 128;
            }

            if (vertexCount < 3)
            {
                indexOffset += Math.Max(vertexCount, 0);
                continue;
            }

            var stripStartIndex = indices.Count;
            if ((vertexCount & 1) == 0)
            {
                for (var i = 0; i < vertexCount - 2; i += 2)
                {
                    AppendWrenchTriangleCandidate(
                        indexOffset + i + 2,
                        indexOffset + i + 3,
                        indexOffset + i + 1,
                        stripIndices,
                        vertexInfoRows,
                        positions,
                        indices,
                        referenceAddresses,
                        seenTriangles,
                        ref rawTriangleCount,
                        ref rejectedDegenerateTriangleCount,
                        ref rejectedInvalidTriangleCount,
                        ref rejectedDuplicateTriangleCount,
                        ref rejectedLongEdgeTriangleCount,
                        maxTriangleEdgeLength);
                    AppendWrenchTriangleCandidate(
                        indexOffset + i + 1,
                        indexOffset + i + 0,
                        indexOffset + i + 2,
                        stripIndices,
                        vertexInfoRows,
                        positions,
                        indices,
                        referenceAddresses,
                        seenTriangles,
                        ref rawTriangleCount,
                        ref rejectedDegenerateTriangleCount,
                        ref rejectedInvalidTriangleCount,
                        ref rejectedDuplicateTriangleCount,
                        ref rejectedLongEdgeTriangleCount,
                        maxTriangleEdgeLength);
                }
            }
            else
            {
                for (var i = 0; i < vertexCount - 2; i++)
                {
                    AppendWrenchTriangleCandidate(
                        indexOffset + i + 0,
                        indexOffset + i + 1,
                        indexOffset + i + 2,
                        stripIndices,
                        vertexInfoRows,
                        positions,
                        indices,
                        referenceAddresses,
                        seenTriangles,
                        ref rawTriangleCount,
                        ref rejectedDegenerateTriangleCount,
                        ref rejectedInvalidTriangleCount,
                        ref rejectedDuplicateTriangleCount,
                        ref rejectedLongEdgeTriangleCount,
                        maxTriangleEdgeLength);
                }
            }

            var stripIndexCount = indices.Count - stripStartIndex;
            if (stripIndexCount > 0)
            {
                materialRanges.Add(new TfragMaterialRange(stripStartIndex, stripIndexCount, activeTextureSlot));
            }

            indexOffset += vertexCount;
        }

        return new TfragTopologyDecode(
            packet,
            "WrenchStrips",
            indices,
            referenceAddresses,
            materialRanges,
            rawTriangleCount,
            rejectedDegenerateTriangleCount,
            rejectedInvalidTriangleCount,
            rejectedDuplicateTriangleCount,
            rejectedLongEdgeTriangleCount,
            AlternateDiagonalRowCount: 0);
    }

    private static void AppendWrenchTriangleCandidate(
        int stripIndexOffset0,
        int stripIndexOffset1,
        int stripIndexOffset2,
        IReadOnlyList<byte> stripIndices,
        IReadOnlyList<TfragVertexInfoRow> vertexInfoRows,
        IReadOnlyList<Vector3> positions,
        List<uint> indices,
        List<int> referenceAddresses,
        HashSet<string> seenTriangles,
        ref int rawTriangleCount,
        ref int rejectedDegenerateTriangleCount,
        ref int rejectedInvalidTriangleCount,
        ref int rejectedDuplicateTriangleCount,
        ref int rejectedLongEdgeTriangleCount,
        float? maxTriangleEdgeLength)
    {
        if (!TryResolveWrenchVertex(stripIndexOffset0, stripIndices, vertexInfoRows, positions.Count, out var vertex0)
            || !TryResolveWrenchVertex(stripIndexOffset1, stripIndices, vertexInfoRows, positions.Count, out var vertex1)
            || !TryResolveWrenchVertex(stripIndexOffset2, stripIndices, vertexInfoRows, positions.Count, out var vertex2))
        {
            rawTriangleCount++;
            rejectedInvalidTriangleCount++;
            return;
        }

        AppendTriangleCandidate(
            (uint)vertex0.SourceIndex,
            (uint)vertex1.SourceIndex,
            (uint)vertex2.SourceIndex,
            positions,
            indices,
            referenceAddresses,
            vertex0.ReferenceAddress,
            vertex1.ReferenceAddress,
            vertex2.ReferenceAddress,
            seenTriangles,
            ref rawTriangleCount,
            ref rejectedDegenerateTriangleCount,
            ref rejectedDuplicateTriangleCount,
            ref rejectedLongEdgeTriangleCount,
            maxTriangleEdgeLength);
    }

    private static bool TryResolveWrenchVertex(
        int stripIndexOffset,
        IReadOnlyList<byte> stripIndices,
        IReadOnlyList<TfragVertexInfoRow> vertexInfoRows,
        int positionCount,
        out TfragVertexInfoRow vertex)
    {
        vertex = default;
        if ((uint)stripIndexOffset >= (uint)stripIndices.Count)
        {
            return false;
        }

        var vertexInfoIndex = stripIndices[stripIndexOffset];
        if ((uint)vertexInfoIndex >= (uint)vertexInfoRows.Count)
        {
            return false;
        }

        vertex = vertexInfoRows[vertexInfoIndex];
        return (uint)vertex.SourceIndex < (uint)positionCount;
    }

    private static TfragResolvedTexture ResolveTextureEntry(
        IReadOnlyList<TfragTextureEntry> textureEntries,
        int textureSlot,
        int fallbackGroupIndex)
    {
        if (textureEntries.Count == 0)
        {
            return TfragResolvedTexture.Untextured;
        }

        var entry = (uint)textureSlot < textureEntries.Count
            ? textureEntries[textureSlot]
            : textureEntries[fallbackGroupIndex % textureEntries.Count];

        return new TfragResolvedTexture(entry.TextureId, entry.ClampU, entry.ClampV);
    }

    private static TfragResolvedTexture ResolveTextureEntry(
        IReadOnlyList<TfragTextureEntry> textureEntries,
        int textureSlot)
    {
        if (textureEntries.Count == 0 || (uint)textureSlot >= textureEntries.Count)
        {
            return TfragResolvedTexture.Untextured;
        }

        var entry = textureEntries[textureSlot];
        return new TfragResolvedTexture(entry.TextureId, entry.ClampU, entry.ClampV);
    }
}

using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TiePacketVertexDecoder
{
    private const int DinkyVertexSize = 0x10;
    private const int FatVertexSize = 0x18;

    public static List<TiePacketVertexRow> DecodeVertexRows(
        byte[] bytes,
        TieClassHeader header,
        TiePacket packet,
        TiePacketUnpackHeader? unpackHeader)
    {
        if (packet.VertexSize == 0)
        {
            return [];
        }

        var hasTypedLayout = TryGetTypedVertexLayout(
            packet.VertexSize,
            unpackHeader,
            out var dinkyVertexCount,
            out var fatVertexCount);
        var rows = new List<TiePacketVertexRow>(packet.VertexSize);
        var scale = header.Scale / 1024f;
        var offset = packet.AbsoluteDataOffset + packet.VertexOffset * 0x10;
        EnsureRange(bytes, offset, packet.VertexSize * 0x10, $"vertex rows LOD{packet.LodIndex}[{packet.PacketIndex}]");
        using var stream = new MemoryStream(bytes, offset, packet.VertexSize * 0x10, writable: false);
        using var reader = new BinaryReader(stream);
        for (var i = 0; i < packet.VertexSize; i++)
        {
            var rowOffset = offset + i * 0x10;
            var x = reader.ReadInt16();
            var y = reader.ReadInt16();
            var z = reader.ReadInt16();
            var w = reader.ReadInt16();
            var rowKind = GetPacketVertexRowKind(
                i,
                hasTypedLayout,
                dinkyVertexCount,
                fatVertexCount,
                out var pairedVertexRowIndex);
            rows.Add(new TiePacketVertexRow
            {
                Index = i,
                Offset = rowOffset,
                Kind = rowKind,
                PairedVertexRowIndex = pairedVertexRowIndex,
                X = x,
                Y = y,
                Z = z,
                W = w,
                Data0 = reader.ReadInt16(),
                Data1 = reader.ReadInt16(),
                Data2 = reader.ReadInt16(),
                Data3 = reader.ReadInt16(),
                ModelX = x * scale,
                ModelY = y * scale,
                ModelZ = z * scale
            });
        }

        return rows;
    }

    public static List<TiePacketDecodedVertex> DecodePacketVertices(
        byte[] bytes,
        TiePacket packet,
        TiePacketUnpackHeader? unpackHeader,
        IReadOnlyList<TiePacketVertexRow> vertexRows)
    {
        if (packet.VertexSize == 0 || unpackHeader is null || unpackHeader.DinkyVerticesSizePlusFour < 4)
        {
            return [];
        }

        var vertexOffset = packet.AbsoluteDataOffset + packet.VertexOffset * 0x10;
        var vertexLength = packet.VertexSize * 0x10;
        EnsureRange(bytes, vertexOffset, vertexLength, $"decoded packet vertices LOD{packet.LodIndex}[{packet.PacketIndex}]");

        var dinkyPayload = unpackHeader.DinkyVerticesSizePlusFour - 4;
        if (dinkyPayload % 2 != 0)
        {
            return [];
        }

        var dinkyCount = dinkyPayload / 2;
        var dinkyLength = checked(dinkyCount * DinkyVertexSize);
        if (dinkyLength > vertexLength)
        {
            return [];
        }

        var vertices = new List<TiePacketDecodedVertex>();
        for (var i = 0; i < dinkyCount; i++)
        {
            var offset = vertexOffset + i * DinkyVertexSize;
            vertices.Add(new TiePacketDecodedVertex
            {
                Index = vertices.Count,
                SourceIndex = i,
                Kind = TiePacketDecodedVertexKind.Dinky,
                Offset = offset,
                Bytes = Slice(bytes, offset, DinkyVertexSize),
                SourceRowIndex = ResolvePacketVertexSourceRowIndex(vertexOffset, offset),
                SourceRow = ResolvePacketVertexSourceRow(vertexRows, vertexOffset, offset),
                X = BitConverter.ToInt16(bytes, offset),
                Y = BitConverter.ToInt16(bytes, offset + 0x02),
                Z = BitConverter.ToInt16(bytes, offset + 0x04),
                GsPacketWriteOffset = BitConverter.ToUInt16(bytes, offset + 0x06),
                S = BitConverter.ToUInt16(bytes, offset + 0x08),
                T = BitConverter.ToUInt16(bytes, offset + 0x0A),
                Q = BitConverter.ToUInt16(bytes, offset + 0x0C),
                SecondaryGsPacketWriteOffset = BitConverter.ToUInt16(bytes, offset + 0x0E)
            });
        }

        var fatStartOffset = vertexOffset + dinkyLength;
        var fatCount = (vertexLength - dinkyLength) / FatVertexSize;
        for (var i = 0; i < fatCount; i++)
        {
            var offset = fatStartOffset + i * FatVertexSize;
            var positionOffset = offset + 0x08;
            vertices.Add(new TiePacketDecodedVertex
            {
                Index = vertices.Count,
                SourceIndex = i,
                Kind = TiePacketDecodedVertexKind.Fat,
                Offset = offset,
                Bytes = Slice(bytes, offset, FatVertexSize),
                SourceRowIndex = ResolvePacketVertexSourceRowIndex(vertexOffset, positionOffset),
                SourceRow = ResolvePacketVertexSourceRow(vertexRows, vertexOffset, positionOffset),
                X = BitConverter.ToInt16(bytes, offset + 0x08),
                Y = BitConverter.ToInt16(bytes, offset + 0x0A),
                Z = BitConverter.ToInt16(bytes, offset + 0x0C),
                GsPacketWriteOffset = BitConverter.ToUInt16(bytes, offset + 0x06),
                S = BitConverter.ToUInt16(bytes, offset + 0x10),
                T = BitConverter.ToUInt16(bytes, offset + 0x12),
                Q = BitConverter.ToUInt16(bytes, offset + 0x14),
                SecondaryGsPacketWriteOffset = BitConverter.ToUInt16(bytes, offset + 0x16)
            });
        }

        return vertices;
    }

    private static int ResolvePacketVertexSourceRowIndex(int vertexSectionOffset, int sourceOffset)
    {
        return Math.Max(0, (sourceOffset - vertexSectionOffset) / 0x10);
    }

    private static TiePacketVertexRow? ResolvePacketVertexSourceRow(
        IReadOnlyList<TiePacketVertexRow> vertexRows,
        int vertexSectionOffset,
        int sourceOffset)
    {
        var rowIndex = ResolvePacketVertexSourceRowIndex(vertexSectionOffset, sourceOffset);
        return rowIndex >= 0 && rowIndex < vertexRows.Count ? vertexRows[rowIndex] : null;
    }

    private static TiePacketVertexRowKind GetPacketVertexRowKind(
        int rowIndex,
        bool hasTypedLayout,
        int dinkyVertexCount,
        int fatVertexCount,
        out int? pairedVertexRowIndex)
    {
        pairedVertexRowIndex = null;
        if (!hasTypedLayout)
        {
            return TiePacketVertexRowKind.Unknown;
        }

        if (rowIndex < dinkyVertexCount)
        {
            return TiePacketVertexRowKind.DinkyVertex;
        }

        var fatRelativeIndex = rowIndex - dinkyVertexCount;
        if (fatRelativeIndex < 0 || fatRelativeIndex >= fatVertexCount * 2)
        {
            return TiePacketVertexRowKind.Unknown;
        }

        if (fatRelativeIndex % 2 == 0)
        {
            pairedVertexRowIndex = rowIndex + 1;
            return TiePacketVertexRowKind.FatVertexHeader;
        }

        pairedVertexRowIndex = rowIndex - 1;
        return TiePacketVertexRowKind.FatVertexData;
    }

    private static bool TryGetTypedVertexLayout(
        int rowCount,
        TiePacketUnpackHeader? unpackHeader,
        out int dinkyVertexCount,
        out int fatVertexCount)
    {
        dinkyVertexCount = 0;
        fatVertexCount = 0;
        if (unpackHeader is null || unpackHeader.DinkyVerticesSizePlusFour < 4)
        {
            return false;
        }

        var dinkyPayload = unpackHeader.DinkyVerticesSizePlusFour - 4;
        if (dinkyPayload % 2 != 0)
        {
            return false;
        }

        dinkyVertexCount = dinkyPayload / 2;
        var remainingRows = rowCount - dinkyVertexCount;
        if (dinkyVertexCount < 0 || remainingRows < 0 || remainingRows % 2 != 0)
        {
            dinkyVertexCount = 0;
            return false;
        }

        fatVertexCount = remainingRows / 2;
        return true;
    }
}

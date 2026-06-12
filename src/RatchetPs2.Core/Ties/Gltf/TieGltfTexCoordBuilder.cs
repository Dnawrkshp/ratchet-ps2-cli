using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfTexCoordBuilder
{
    private const float PacketTextureCoordinateWrapPeriod = 16f;

    public static List<Vector2> BuildTexCoords(TieClass tie, TieLodTopology topology)
    {
        var texCoords = new List<Vector2>(topology.LogicalVertices.Count);
        var rowsByPacketIndex = tie.PacketDataBlocks
            .Where(block => block.LodIndex == topology.LodIndex)
            .ToDictionary(block => block.PacketIndex, block => block.VertexRows);

        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (vertex.DecodedVertex is null && vertex.VertexRow is null && vertex.AddressRow is null)
            {
                throw new InvalidDataException(
                    $"Tie LOD {topology.LodIndex} logical vertex {vertex.LogicalVertexIndex} has no decoded vertex.");
            }

            rowsByPacketIndex.TryGetValue(vertex.PacketIndex, out var packetRows);
            texCoords.Add(ToGltfTexCoord(vertex, packetRows));
        }

        UnwrapPacketTexCoords(topology, texCoords);
        return texCoords;
    }

    private static void UnwrapPacketTexCoords(TieLodTopology topology, List<Vector2> texCoords)
    {
        foreach (var strip in topology.Strips)
        {
            if (strip.LogicalVertices.Count < 2)
            {
                continue;
            }

            var previous = texCoords[strip.LogicalVertices[0].LogicalVertexIndex];
            for (var i = 1; i < strip.LogicalVertices.Count; i++)
            {
                var index = strip.LogicalVertices[i].LogicalVertexIndex;
                var current = texCoords[index];
                current = new Vector2(
                    UnwrapPacketTexCoordComponent(current.X, previous.X),
                    UnwrapPacketTexCoordComponent(current.Y, previous.Y));
                texCoords[index] = current;
                previous = current;
            }
        }
    }

    private static float UnwrapPacketTexCoordComponent(float value, float previous)
    {
        var delta = value - previous;
        while (delta > PacketTextureCoordinateWrapPeriod / 2f)
        {
            value -= PacketTextureCoordinateWrapPeriod;
            delta = value - previous;
        }

        while (delta < -PacketTextureCoordinateWrapPeriod / 2f)
        {
            value += PacketTextureCoordinateWrapPeriod;
            delta = value - previous;
        }

        return value;
    }

    private static Vector2 ToGltfTexCoord(TieLogicalVertex vertex, IReadOnlyList<TiePacketVertexRow>? packetRows)
    {
        if (vertex.DecodedVertex is { } decodedVertex)
        {
            return new Vector2(decodedVertex.S / 4096f, decodedVertex.T / 4096f);
        }

        if (!TrySelectTextureCoordinate(vertex.VertexRow, packetRows, out var u, out var v)
            && !TrySelectTextureCoordinate(vertex.AddressRow, packetRows, out u, out v))
        {
            u = 0;
            v = 0;
        }

        return new Vector2(u / 4096f, v / 4096f);
    }

    private static bool TrySelectTextureCoordinate(
        TiePacketVertexRow? row,
        IReadOnlyList<TiePacketVertexRow>? packetRows,
        out short u,
        out short v)
    {
        if (row is not null
            && TiePacketVertexRowClassifier.UsesSecondPositionSlot(row)
            && TrySelectAdjacentTextureCoordinate(row, packetRows, out u, out v))
        {
            return true;
        }

        return TrySelectTextureCoordinate(row, out u, out v);
    }

    private static bool TrySelectTextureCoordinate(TiePacketVertexRow? row, out short u, out short v)
    {
        if (row is null)
        {
            u = 0;
            v = 0;
            return false;
        }

        if (row.Data2 == 4096)
        {
            u = row.Data0;
            v = row.Data1;
            return true;
        }

        if (IsLikelyInlineTextureCoordinate(row))
        {
            u = row.X;
            v = row.Y;
            return true;
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool TrySelectAdjacentTextureCoordinate(
        TiePacketVertexRow row,
        IReadOnlyList<TiePacketVertexRow>? packetRows,
        out short u,
        out short v)
    {
        if (packetRows is not null)
        {
            if (TrySelectTextureCoordinateAt(packetRows, row.Index + 1, out u, out v)
                || TrySelectTextureCoordinateAt(packetRows, row.Index - 1, out u, out v))
            {
                return true;
            }
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool TrySelectTextureCoordinateAt(
        IReadOnlyList<TiePacketVertexRow> packetRows,
        int index,
        out short u,
        out short v)
    {
        if (index >= 0 && index < packetRows.Count)
        {
            return TrySelectTextureCoordinate(packetRows[index], out u, out v);
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool IsLikelyInlineTextureCoordinate(TiePacketVertexRow row)
    {
        return row.Z == 4096
            || (Math.Abs((int)row.X) <= 8192
                && Math.Abs((int)row.Y) <= 8192
                && row.Z is 0 or 14);
    }
}

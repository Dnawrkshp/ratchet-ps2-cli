using System.Numerics;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfTexCoordBuilder
{
    private const float TextureTileEdgeBiasTexels = 1f;
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

    public static Vector2[] AdjustTriangleTexCoords(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        TextureSize? textureSize,
        bool repeatU,
        bool repeatV)
    {
        var adjustedTexCoords = UnwrapTriangleTexCoords(a, b, c, repeatU, repeatV);
        return textureSize is { } resolvedTextureSize
            ? BiasTriangleTextureTileEdges(adjustedTexCoords, resolvedTextureSize, repeatU, repeatV)
            : adjustedTexCoords;
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

    private static Vector2[] UnwrapTriangleTexCoords(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        bool repeatU,
        bool repeatV)
    {
        var u = repeatU ? UnwrapRepeatedAxis(a.X, b.X, c.X) : new[] { a.X, b.X, c.X };
        var v = repeatV ? UnwrapRepeatedAxis(a.Y, b.Y, c.Y) : new[] { a.Y, b.Y, c.Y };
        return
        [
            new Vector2(u[0], v[0]),
            new Vector2(u[1], v[1]),
            new Vector2(u[2], v[2])
        ];
    }

    private static Vector2[] BiasTriangleTextureTileEdges(
        Vector2[] texCoords,
        TextureSize textureSize,
        bool repeatU,
        bool repeatV)
    {
        var u = BiasTriangleTextureTileEdges(
            texCoords[0].X,
            texCoords[1].X,
            texCoords[2].X,
            textureSize.Width,
            allowOuterTextureEdgeBias: !repeatU && ShouldAllowOuterTextureEdgeBias(texCoords[0].Y, texCoords[1].Y, texCoords[2].Y));
        var v = BiasTriangleTextureTileEdges(
            texCoords[0].Y,
            texCoords[1].Y,
            texCoords[2].Y,
            textureSize.Height,
            allowOuterTextureEdgeBias: !repeatV && ShouldAllowOuterTextureEdgeBias(texCoords[0].X, texCoords[1].X, texCoords[2].X));

        return
        [
            new Vector2(u[0], v[0]),
            new Vector2(u[1], v[1]),
            new Vector2(u[2], v[2])
        ];
    }

    private static float[] BiasTriangleTextureTileEdges(
        float a,
        float b,
        float c,
        int textureSize,
        bool allowOuterTextureEdgeBias)
    {
        if (textureSize <= 0)
        {
            return [a, b, c];
        }

        var min = MathF.Min(a, MathF.Min(b, c));
        var max = MathF.Max(a, MathF.Max(b, c));
        var span = max - min;
        var biasOuterTextureEdgesOnly = false;
        if (span < 1f - 0.000001f)
        {
            if (!allowOuterTextureEdgeBias
                || span < 0.49f
                || !TouchesOuterTextureEdge(a, b, c))
            {
                return [a, b, c];
            }

            biasOuterTextureEdgesOnly = true;
        }

        var texel = TextureTileEdgeBiasTexels / textureSize;
        return
        [
            BiasTextureTileEdge(a, min, max, texel, biasOuterTextureEdgesOnly),
            BiasTextureTileEdge(b, min, max, texel, biasOuterTextureEdgesOnly),
            BiasTextureTileEdge(c, min, max, texel, biasOuterTextureEdgesOnly)
        ];
    }

    private static bool TouchesOuterTextureEdge(float a, float b, float c)
    {
        return IsOuterTextureEdge(a) || IsOuterTextureEdge(b) || IsOuterTextureEdge(c);
    }

    private static bool IsOuterTextureEdge(float value)
    {
        const float epsilon = 0.000001f;

        return MathF.Abs(value) <= epsilon || MathF.Abs(value - 1f) <= epsilon;
    }

    private static bool IsBroadRepeatedTriangleAxis(float a, float b, float c)
    {
        return MathF.Min(MathF.Abs(a), MathF.Min(MathF.Abs(b), MathF.Abs(c))) > PacketTextureCoordinateWrapPeriod / 2f;
    }

    private static bool ShouldAllowOuterTextureEdgeBias(float a, float b, float c)
    {
        if (!IsBroadRepeatedTriangleAxis(a, b, c))
        {
            return true;
        }

        return MathF.Max(a, MathF.Max(b, c)) - MathF.Min(a, MathF.Min(b, c)) < 0.5f - 0.000001f;
    }

    private static float BiasTextureTileEdge(float value, float min, float max, float texel, bool outerTextureEdgesOnly)
    {
        const float epsilon = 0.000001f;

        var tile = MathF.Round(value);
        if (MathF.Abs(value - tile) > epsilon)
        {
            return value;
        }

        if (outerTextureEdgesOnly)
        {
            if (MathF.Abs(value) <= epsilon)
            {
                return value + texel;
            }

            return MathF.Abs(value - 1f) <= epsilon
                ? value - texel
                : value;
        }

        if (max - min <= epsilon)
        {
            return tile <= value && tile > 0f
                ? tile - texel
                : tile + texel;
        }

        if (value <= min + epsilon)
        {
            return value + texel;
        }

        if (value >= max - epsilon)
        {
            return value - texel;
        }

        return value;
    }

    private static float[] UnwrapRepeatedAxis(float a, float b, float c)
    {
        const float rangeImprovementTolerance = 0.01f;

        var bestB = b;
        var bestC = c;
        var bestRange = Range(a, b, c);
        var originalRange = bestRange;
        var bestInteriorBoundaryCount = CountInteriorIntegerBoundaries(a, b, c);

        var centeredBOffset = -(int)MathF.Round(b - a);
        var centeredCOffset = -(int)MathF.Round(c - a);
        for (var bOffset = centeredBOffset - 2; bOffset <= centeredBOffset + 2; bOffset++)
        {
            for (var cOffset = centeredCOffset - 2; cOffset <= centeredCOffset + 2; cOffset++)
            {
                var candidateB = b + bOffset;
                var candidateC = c + cOffset;
                var candidateRange = Range(a, candidateB, candidateC);
                if (CollapsesRepeatedTileSpan(originalRange, candidateRange))
                {
                    continue;
                }

                var candidateInteriorBoundaryCount = CountInteriorIntegerBoundaries(a, candidateB, candidateC);
                if (candidateInteriorBoundaryCount < bestInteriorBoundaryCount
                    || (candidateInteriorBoundaryCount == bestInteriorBoundaryCount
                        && candidateRange < bestRange - rangeImprovementTolerance))
                {
                    bestInteriorBoundaryCount = candidateInteriorBoundaryCount;
                    bestRange = candidateRange;
                    bestB = candidateB;
                    bestC = candidateC;
                }
            }
        }

        return [a, bestB, bestC];
    }

    private static bool CollapsesRepeatedTileSpan(float originalRange, float candidateRange)
    {
        const float minimumMeaningfulOriginalSpan = 0.5f;
        const float collapsedCandidateSpan = 0.0001f;
        const float deliberateMultiTileSpan = 1.5f;
        const float shrinkTolerance = 0.01f;

        return (originalRange >= minimumMeaningfulOriginalSpan
                && candidateRange <= collapsedCandidateSpan)
            || (originalRange >= deliberateMultiTileSpan
                && candidateRange < originalRange - shrinkTolerance);
    }

    private static int CountInteriorIntegerBoundaries(float a, float b, float c)
    {
        const float epsilon = 0.000001f;
        var min = MathF.Min(a, MathF.Min(b, c));
        var max = MathF.Max(a, MathF.Max(b, c));
        var count = 0;
        for (var boundary = MathF.Floor(min) + 1f; boundary < max; boundary += 1f)
        {
            if (boundary > min + epsilon && boundary < max - epsilon)
            {
                count++;
            }
        }

        return count;
    }

    private static float Range(float a, float b, float c)
    {
        return MathF.Max(a, MathF.Max(b, c)) - MathF.Min(a, MathF.Min(b, c));
    }
}

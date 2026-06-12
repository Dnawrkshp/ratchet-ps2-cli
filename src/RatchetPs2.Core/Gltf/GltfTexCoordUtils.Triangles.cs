using System.Numerics;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Gltf;

public static partial class GltfTexCoordUtils
{
    private const float TextureTileEdgeBiasTexels = 1f;
    private const float TriangleTextureCoordinateWrapPeriod = 16f;

    public static Vector2[] AdjustTriangleTexCoords(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        TextureSize? textureSize,
        bool repeatU,
        bool repeatV,
        bool normalizeClampedAxes = false)
    {
        var adjustedTexCoords = UnwrapTriangleTexCoords(a, b, c, repeatU, repeatV, normalizeClampedAxes);
        return textureSize is { } resolvedTextureSize
            ? BiasTriangleTextureTileEdges(adjustedTexCoords, resolvedTextureSize, repeatU, repeatV)
            : adjustedTexCoords;
    }

    public static float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        return MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) * 0.5f;
    }

    private static Vector2[] UnwrapTriangleTexCoords(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        bool repeatU,
        bool repeatV,
        bool normalizeClampedAxes)
    {
        var u = repeatU
            ? UnwrapRepeatedAxis(a.X, b.X, c.X)
            : normalizeClampedAxes
                ? NormalizeClampedAxis(a.X, b.X, c.X)
                : [a.X, b.X, c.X];
        var v = repeatV
            ? UnwrapRepeatedAxis(a.Y, b.Y, c.Y)
            : normalizeClampedAxes
                ? NormalizeClampedAxis(a.Y, b.Y, c.Y)
                : [a.Y, b.Y, c.Y];
        return
        [
            new Vector2(u[0], v[0]),
            new Vector2(u[1], v[1]),
            new Vector2(u[2], v[2])
        ];
    }

    private static float[] NormalizeClampedAxis(float a, float b, float c)
    {
        if (IsWithinUnitTile(a) && IsWithinUnitTile(b) && IsWithinUnitTile(c))
        {
            return [a, b, c];
        }

        var min = MathF.Min(a, MathF.Min(b, c));
        var max = MathF.Max(a, MathF.Max(b, c));
        var offset = ChooseClampedAxisOffset(a, b, c);
        if (max - min <= 1f + 0.000001f
            || CountUnitRangeValues(a - offset, b - offset, c - offset) >= 2)
        {
            return
            [
                ClampUnit(a - offset),
                ClampUnit(b - offset),
                ClampUnit(c - offset)
            ];
        }

        return
        [
            ClampUnit(NormalizeClampedCoordinate(a)),
            ClampUnit(NormalizeClampedCoordinate(b)),
            ClampUnit(NormalizeClampedCoordinate(c))
        ];
    }

    private static float ChooseClampedAxisOffset(float a, float b, float c)
    {
        var min = MathF.Min(a, MathF.Min(b, c));
        var max = MathF.Max(a, MathF.Max(b, c));
        var start = (int)MathF.Floor(min) - 1;
        var end = (int)MathF.Ceiling(max) + 1;
        var bestOffset = 0;
        var bestPenalty = float.PositiveInfinity;
        var bestUnitRangeCount = -1;
        var bestDistanceFromUnitCenter = float.PositiveInfinity;

        for (var offset = start; offset <= end; offset++)
        {
            var adjustedA = a - offset;
            var adjustedB = b - offset;
            var adjustedC = c - offset;
            var penalty = UnitRangePenalty(adjustedA)
                + UnitRangePenalty(adjustedB)
                + UnitRangePenalty(adjustedC);
            var adjustedMin = MathF.Min(adjustedA, MathF.Min(adjustedB, adjustedC));
            var adjustedMax = MathF.Max(adjustedA, MathF.Max(adjustedB, adjustedC));
            var unitRangeCount = CountUnitRangeValues(adjustedA, adjustedB, adjustedC);
            var distanceFromUnitCenter = MathF.Abs((adjustedMin + adjustedMax) * 0.5f - 0.5f);
            if (penalty < bestPenalty - 0.000001f
                || (MathF.Abs(penalty - bestPenalty) <= 0.000001f
                    && (unitRangeCount > bestUnitRangeCount
                        || (unitRangeCount == bestUnitRangeCount
                            && distanceFromUnitCenter < bestDistanceFromUnitCenter))))
            {
                bestPenalty = penalty;
                bestUnitRangeCount = unitRangeCount;
                bestDistanceFromUnitCenter = distanceFromUnitCenter;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }

    private static float UnitRangePenalty(float value)
    {
        return value < 0f
            ? -value
            : value > 1f
                ? value - 1f
                : 0f;
    }

    private static int CountUnitRangeValues(float a, float b, float c)
    {
        return (IsWithinUnitTile(a) ? 1 : 0)
            + (IsWithinUnitTile(b) ? 1 : 0)
            + (IsWithinUnitTile(c) ? 1 : 0);
    }

    private static float ClampUnit(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    private static bool IsWithinUnitTile(float value)
    {
        const float epsilon = 0.000001f;

        return value >= -epsilon && value <= 1f + epsilon;
    }

    private static float NormalizeClampedCoordinate(float value)
    {
        const float epsilon = 0.000001f;

        var normalized = value - MathF.Floor(value);
        if (MathF.Abs(normalized) <= epsilon)
        {
            return value > 0f
                ? 1f
                : 0f;
        }

        return normalized;
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
        return MathF.Min(MathF.Abs(a), MathF.Min(MathF.Abs(b), MathF.Abs(c))) > TriangleTextureCoordinateWrapPeriod / 2f;
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
        const float collapsedCandidateSpan = 0.001f;
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

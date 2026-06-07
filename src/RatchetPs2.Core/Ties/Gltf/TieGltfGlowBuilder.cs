using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfGlowBuilder
{
    private const float GlowEmissionStrengthScale = 1.5f;
    public static readonly Vector4 NoGlowColor = Vector4.Zero;

    public static TieGltfGlowBuildResult BuildColors(TieClass tie, TieLodTopology topology, int vertexCount)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);

        var sourceVertices = tie.GlowRgbaVertices
            .Where(vertex => vertex.LodIndex == topology.LodIndex)
            .OrderBy(vertex => vertex.LogicalVertexIndex)
            .ToArray();
        if (sourceVertices.Length == 0)
        {
            return new TieGltfGlowBuildResult([], TieRgba32.FromRaw(tie.Header.GlowRgba), 0);
        }

        var colors = Enumerable.Repeat(NoGlowColor, vertexCount).ToList();
        foreach (var vertex in sourceVertices)
        {
            if (vertex.LogicalVertexIndex >= 0 && vertex.LogicalVertexIndex < colors.Count)
            {
                colors[vertex.LogicalVertexIndex] = ToGltfColor(vertex.Rgba);
            }
        }

        return new TieGltfGlowBuildResult(colors, sourceVertices[0].Rgba, sourceVertices.Length);
    }

    public static TieGltfGlowEmissionMaterial BuildEmissionMaterial(TieRgba32 rgba)
    {
        return new TieGltfGlowEmissionMaterial(rgba, GetEmissionStrength(rgba));
    }

    public static float GetEmissionStrength(TieRgba32 rgba)
    {
        var rgbMagnitude = Math.Max(rgba.R, Math.Max(rgba.G, rgba.B)) / 128f;
        return rgba.A / 128f * rgbMagnitude * GlowEmissionStrengthScale;
    }

    public static Vector4 ToEmissionAttribute(Vector4 color)
    {
        if (!IsActiveColor(color))
        {
            return Vector4.Zero;
        }

        var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        if (max <= 1e-8f)
        {
            return Vector4.Zero;
        }

        var strength = color.W * (max * 255f / 128f) * GlowEmissionStrengthScale;
        return new Vector4(color.X / max, color.Y / max, color.Z / max, strength);
    }

    public static int CountActiveVertices(IReadOnlyList<Vector4> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);

        return colors.Count(IsActiveColor);
    }

    public static int CountActiveIndices(IReadOnlyList<uint> indices, IReadOnlyList<Vector4> colors)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(colors);

        if (colors.Count == 0)
        {
            return 0;
        }

        var count = 0;
        foreach (var index in indices)
        {
            var colorIndex = checked((int)index);
            if (colorIndex >= 0 && colorIndex < colors.Count && IsActiveColor(colors[colorIndex]))
            {
                count++;
            }
        }

        return count;
    }

    private static Vector4 ToGltfColor(TieRgba32 rgba)
    {
        return new Vector4(
            rgba.R / 255f,
            rgba.G / 255f,
            rgba.B / 255f,
            Math.Min(1f, rgba.A / 128f));
    }

    public static bool IsActiveColor(Vector4 color)
    {
        return color.W > 0.000001f;
    }
}

internal sealed record TieGltfGlowBuildResult(
    List<Vector4> Colors,
    TieRgba32 Rgba,
    int ResolvedVertexCount);

using System.Numerics;

namespace RatchetPs2.Core.Geometry;

public readonly record struct Bounds3(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 Center => (Min + Max) * 0.5f;

    public static Bounds3 From(IReadOnlyList<Vector3> positions)
    {
        if (positions.Count == 0)
        {
            return new Bounds3(Vector3.Zero, Vector3.Zero);
        }

        var min = positions[0];
        var max = positions[0];
        for (var i = 1; i < positions.Count; i++)
        {
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        return new Bounds3(min, max);
    }

    public static Bounds3 From(IEnumerable<Vector3> positions)
    {
        if (positions is IReadOnlyList<Vector3> list)
        {
            return From(list);
        }

        using var enumerator = positions.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return new Bounds3(Vector3.Zero, Vector3.Zero);
        }

        var min = enumerator.Current;
        var max = enumerator.Current;
        while (enumerator.MoveNext())
        {
            min = Vector3.Min(min, enumerator.Current);
            max = Vector3.Max(max, enumerator.Current);
        }

        return new Bounds3(min, max);
    }

    public Vector3 Normalize(Vector3 value)
    {
        return new Vector3(
            Normalize(value.X, Min.X, Max.X),
            Normalize(value.Y, Min.Y, Max.Y),
            Normalize(value.Z, Min.Z, Max.Z));
    }

    public Vector3 Lerp(Vector3 value)
    {
        return new Vector3(
            Min.X + (Max.X - Min.X) * value.X,
            Min.Y + (Max.Y - Min.Y) * value.Y,
            Min.Z + (Max.Z - Min.Z) * value.Z);
    }

    public float OverlapVolume(Bounds3 other)
    {
        var overlap = Vector3.Max(Vector3.Zero, Vector3.Min(Max, other.Max) - Vector3.Max(Min, other.Min));
        return overlap.X * overlap.Y * overlap.Z;
    }

    private static float Normalize(float value, float min, float max)
    {
        return Math.Abs(max - min) <= 0.000001f
            ? 0.5f
            : Math.Clamp((value - min) / (max - min), 0f, 1f);
    }
}

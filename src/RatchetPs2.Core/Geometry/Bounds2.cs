using System.Numerics;

namespace RatchetPs2.Core.Geometry;

public readonly record struct Bounds2(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;

    public Vector2 Center => (Min + Max) * 0.5f;

    public static Bounds2 From(IReadOnlyList<Vector2> values)
    {
        if (values.Count == 0)
        {
            return new Bounds2(Vector2.Zero, Vector2.Zero);
        }

        var min = values[0];
        var max = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            min = Vector2.Min(min, values[i]);
            max = Vector2.Max(max, values[i]);
        }

        return new Bounds2(min, max);
    }

    public static Bounds2 From(IEnumerable<Vector2> values)
    {
        if (values is IReadOnlyList<Vector2> list)
        {
            return From(list);
        }

        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return new Bounds2(Vector2.Zero, Vector2.Zero);
        }

        var min = enumerator.Current;
        var max = enumerator.Current;
        while (enumerator.MoveNext())
        {
            min = Vector2.Min(min, enumerator.Current);
            max = Vector2.Max(max, enumerator.Current);
        }

        return new Bounds2(min, max);
    }
}

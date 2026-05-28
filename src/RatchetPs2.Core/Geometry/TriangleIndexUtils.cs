namespace RatchetPs2.Core.Geometry;

public static class TriangleIndexUtils
{
    public static List<uint> BuildSequentialIndices(int positionCount)
    {
        var indices = new List<uint>(positionCount);
        for (var i = 0; i < positionCount; i++)
        {
            indices.Add((uint)i);
        }

        return indices;
    }

    public static List<uint> RemapIndices(IReadOnlyList<uint> indices, IReadOnlyList<int> indexByOriginalIndex)
    {
        var remapped = new List<uint>(indices.Count);
        foreach (var index in indices)
        {
            if (index >= indexByOriginalIndex.Count)
            {
                throw new InvalidDataException($"Mesh index {index} is outside the vertex list.");
            }

            remapped.Add(checked((uint)indexByOriginalIndex[(int)index]));
        }

        return remapped;
    }

    public static List<uint> BuildDoubleSidedTriangles(IReadOnlyList<uint> triangleIndices)
    {
        ValidateTriangleIndexList(triangleIndices, "Mesh indices");

        var result = new List<uint>(triangleIndices.Count * 2);
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            result.Add(a);
            result.Add(b);
            result.Add(c);
            result.Add(a);
            result.Add(c);
            result.Add(b);
        }

        return result;
    }

    public static int CountTriangles(IReadOnlyList<uint> triangleIndices)
    {
        ValidateTriangleIndexList(triangleIndices, "Mesh indices");
        return triangleIndices.Count / 3;
    }

    public static int DivideRoundUp(int value, int divisor)
    {
        return divisor <= 0
            ? value
            : (value + divisor - 1) / divisor;
    }

    public static void ValidateTriangleIndexList(IReadOnlyList<uint> indices, string context)
    {
        if (indices.Count == 0 || indices.Count % 3 != 0)
        {
            throw new InvalidDataException($"{context} must contain triangle indices.");
        }
    }
}

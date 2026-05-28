using System.Numerics;

namespace RatchetPs2.Core.Gltf;

public static class GltfTexCoordUtils
{
    public static Vector2? ApplyMaterialUvScale(
        List<Vector2>? texCoords,
        string? materialName,
        IReadOnlyDictionary<string, Vector2>? materialUvScales)
    {
        if (texCoords is null
            || string.IsNullOrWhiteSpace(materialName)
            || materialUvScales is null
            || !materialUvScales.TryGetValue(materialName, out var scale))
        {
            return null;
        }

        for (var i = 0; i < texCoords.Count; i++)
        {
            texCoords[i] *= scale;
        }

        return scale;
    }

    public static int ClampToUnitRange(List<Vector2>? texCoords)
    {
        if (texCoords is null)
        {
            return 0;
        }

        var clampedComponentCount = 0;
        for (var i = 0; i < texCoords.Count; i++)
        {
            var texCoord = texCoords[i];
            var clamped = Vector2.Clamp(texCoord, Vector2.Zero, Vector2.One);
            if (Math.Abs(clamped.X - texCoord.X) > 0.000001f)
            {
                clampedComponentCount++;
            }

            if (Math.Abs(clamped.Y - texCoord.Y) > 0.000001f)
            {
                clampedComponentCount++;
            }

            texCoords[i] = clamped;
        }

        return clampedComponentCount;
    }
}

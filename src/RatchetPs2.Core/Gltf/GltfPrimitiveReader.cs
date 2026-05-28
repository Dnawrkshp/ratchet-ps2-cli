using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Gltf;

public static class GltfPrimitiveReader
{
    public static List<string?> ReadMaterialNames(JsonElement root)
    {
        if (!root.TryGetProperty("materials", out var materials))
        {
            return [];
        }

        var names = new List<string?>(materials.GetArrayLength());
        for (var i = 0; i < materials.GetArrayLength(); i++)
        {
            var material = materials[i];
            names.Add(material.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null);
        }

        return names;
    }

    public static GltfPrimitiveData? ReadTrianglePrimitive(
        JsonElement primitive,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers,
        IReadOnlyList<string?> materialNames,
        int meshIndex,
        int primitiveIndex,
        string context)
    {
        if (primitive.TryGetProperty("mode", out var modeElement) && modeElement.GetInt32() != 4)
        {
            return null;
        }

        var attributes = primitive.GetProperty("attributes");
        if (!attributes.TryGetProperty("POSITION", out var positionAccessorElement))
        {
            throw new InvalidDataException($"{context} has no POSITION attribute.");
        }

        var positions = GltfAccessorReader.ReadVec3Accessor(positionAccessorElement.GetInt32(), accessors, bufferViews, buffers);
        var indices = primitive.TryGetProperty("indices", out var indexAccessorElement)
            ? GltfAccessorReader.ReadIndexAccessor(indexAccessorElement.GetInt32(), accessors, bufferViews, buffers)
            : TriangleIndexUtils.BuildSequentialIndices(positions.Count);
        var texCoords = attributes.TryGetProperty("TEXCOORD_0", out var texCoordAccessorElement)
            ? GltfAccessorReader.ReadVec2Accessor(texCoordAccessorElement.GetInt32(), accessors, bufferViews, buffers)
            : null;
        var joints = attributes.TryGetProperty("JOINTS_0", out var jointsAccessorElement)
            ? GltfAccessorReader.ReadVec4UShortAccessor(jointsAccessorElement.GetInt32(), accessors, bufferViews, buffers)
            : null;
        var weights = attributes.TryGetProperty("WEIGHTS_0", out var weightsAccessorElement)
            ? GltfAccessorReader.ReadVec4FloatAccessor(weightsAccessorElement.GetInt32(), accessors, bufferViews, buffers)
            : null;

        ValidateAttributeCounts(context, positions, indices, texCoords, joints, weights);

        int? materialIndex = primitive.TryGetProperty("material", out var materialElement)
            ? materialElement.GetInt32()
            : null;
        var materialName = materialIndex is >= 0 && materialIndex.Value < materialNames.Count
            ? materialNames[materialIndex.Value]
            : null;

        return new GltfPrimitiveData(
            meshIndex,
            primitiveIndex,
            materialIndex,
            materialName,
            positions,
            indices,
            texCoords,
            joints,
            weights);
    }

    private static void ValidateAttributeCounts(
        string context,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        IReadOnlyList<Vector2>? texCoords,
        IReadOnlyList<ushort[]>? joints,
        IReadOnlyList<float[]>? weights)
    {
        if (texCoords is not null && texCoords.Count != positions.Count)
        {
            throw new InvalidDataException($"{context} TEXCOORD_0 count must match POSITION count.");
        }

        if (joints is not null && joints.Count != positions.Count)
        {
            throw new InvalidDataException($"{context} JOINTS_0 count must match POSITION count.");
        }

        if (weights is not null && weights.Count != positions.Count)
        {
            throw new InvalidDataException($"{context} WEIGHTS_0 count must match POSITION count.");
        }

        if ((joints is null) != (weights is null))
        {
            throw new InvalidDataException($"{context} must provide both JOINTS_0 and WEIGHTS_0, or neither.");
        }

        TriangleIndexUtils.ValidateTriangleIndexList(indices, context);
    }
}

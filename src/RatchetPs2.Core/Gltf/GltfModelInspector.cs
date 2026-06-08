using System.Text.Json;

namespace RatchetPs2.Core.Gltf;

public sealed record GltfModelInfo(
    int MeshCount,
    int PrimitiveCount,
    int VertexCount,
    int TriangleCount,
    int MaterialCount,
    int TextureCount,
    IReadOnlyList<string> ImageUris,
    GltfModelBounds? Bounds);

public sealed record GltfModelBounds(float[] Min, float[] Max);

public static class GltfModelInspector
{
    public static GltfModelInfo Inspect(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var document = GltfJsonReader.Read(input);
        return Inspect(document.RootElement);
    }

    public static GltfModelInfo Inspect(JsonElement root)
    {
        var meshCount = root.TryGetProperty("meshes", out var meshes) && meshes.ValueKind == JsonValueKind.Array
            ? meshes.GetArrayLength()
            : 0;
        var materialCount = root.TryGetProperty("materials", out var materials) && materials.ValueKind == JsonValueKind.Array
            ? materials.GetArrayLength()
            : 0;
        var textureCount = root.TryGetProperty("textures", out var textures) && textures.ValueKind == JsonValueKind.Array
            ? textures.GetArrayLength()
            : 0;
        var imageUris = ReadImageUris(root);

        var primitiveCount = 0;
        var vertexCount = 0;
        var triangleCount = 0;
        float[]? min = null;
        float[]? max = null;

        if (!root.TryGetProperty("accessors", out var accessors)
            || accessors.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("meshes", out meshes)
            || meshes.ValueKind != JsonValueKind.Array)
        {
            return new GltfModelInfo(
                meshCount,
                primitiveCount,
                vertexCount,
                triangleCount,
                materialCount,
                textureCount,
                imageUris,
                Bounds: null);
        }

        foreach (var mesh in meshes.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out var primitives) || primitives.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var primitive in primitives.EnumerateArray())
            {
                var mode = primitive.TryGetProperty("mode", out var modeElement)
                    ? modeElement.GetInt32()
                    : 4;
                if (mode != 4)
                {
                    continue;
                }

                primitiveCount++;
                if (!primitive.TryGetProperty("attributes", out var attributes)
                    || !attributes.TryGetProperty("POSITION", out var positionAccessorElement))
                {
                    continue;
                }

                var positionAccessorIndex = positionAccessorElement.GetInt32();
                if ((uint)positionAccessorIndex >= (uint)accessors.GetArrayLength())
                {
                    continue;
                }

                var positionAccessor = accessors[positionAccessorIndex];
                var primitiveVertexCount = positionAccessor.TryGetProperty("count", out var positionCountElement)
                    ? positionCountElement.GetInt32()
                    : 0;
                vertexCount += primitiveVertexCount;
                MergeBounds(positionAccessor, ref min, ref max);

                if (primitive.TryGetProperty("indices", out var indicesAccessorElement))
                {
                    var indicesAccessorIndex = indicesAccessorElement.GetInt32();
                    if ((uint)indicesAccessorIndex < (uint)accessors.GetArrayLength())
                    {
                        var indexCount = accessors[indicesAccessorIndex].TryGetProperty("count", out var indexCountElement)
                            ? indexCountElement.GetInt32()
                            : 0;
                        triangleCount += indexCount / 3;
                    }
                }
                else
                {
                    triangleCount += primitiveVertexCount / 3;
                }
            }
        }

        return new GltfModelInfo(
            meshCount,
            primitiveCount,
            vertexCount,
            triangleCount,
            materialCount,
            textureCount,
            imageUris,
            min is null || max is null ? null : new GltfModelBounds(min, max));
    }

    private static IReadOnlyList<string> ReadImageUris(JsonElement root)
    {
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var uris = new List<string>(images.GetArrayLength());
        foreach (var image in images.EnumerateArray())
        {
            if (image.TryGetProperty("uri", out var uriElement)
                && uriElement.ValueKind == JsonValueKind.String
                && uriElement.GetString() is { } uri
                && !string.IsNullOrWhiteSpace(uri))
            {
                uris.Add(uri);
            }
        }

        return uris;
    }

    private static void MergeBounds(JsonElement accessor, ref float[]? min, ref float[]? max)
    {
        if (!accessor.TryGetProperty("min", out var accessorMin)
            || !accessor.TryGetProperty("max", out var accessorMax)
            || accessorMin.ValueKind != JsonValueKind.Array
            || accessorMax.ValueKind != JsonValueKind.Array
            || accessorMin.GetArrayLength() < 3
            || accessorMax.GetArrayLength() < 3)
        {
            return;
        }

        var nextMin = new[]
        {
            accessorMin[0].GetSingle(),
            accessorMin[1].GetSingle(),
            accessorMin[2].GetSingle()
        };
        var nextMax = new[]
        {
            accessorMax[0].GetSingle(),
            accessorMax[1].GetSingle(),
            accessorMax[2].GetSingle()
        };

        if (min is null || max is null)
        {
            min = nextMin;
            max = nextMax;
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            min[i] = Math.Min(min[i], nextMin[i]);
            max[i] = Math.Max(max[i], nextMax[i]);
        }
    }
}

public static class GltfJsonReader
{
    public static JsonDocument Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return JsonDocument.Parse(input);
    }
}

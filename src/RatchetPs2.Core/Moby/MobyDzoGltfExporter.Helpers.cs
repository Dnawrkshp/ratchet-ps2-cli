using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfExporter
{
    private static bool ShouldExportDzoMesh(MobyMeshType meshType, BangleMeshGroup? bangleGroup)
    {
        return meshType == MobyMeshType.HighLod
            || meshType == MobyMeshType.Metal
            || meshType == MobyMeshType.Bangle && bangleGroup?.IsLowLod != true;
    }

    private static int GetGlowVertexCount(MobyMeshTableEntry entry)
    {
        if (entry.MeshType == MobyMeshType.Metal || entry.VertexData.Length < 0x10)
        {
            return 0;
        }

        // The packet header marks glow geometry with the number of affected transfer vertices.
        return BitConverter.ToUInt16(entry.VertexData, 0x0E);
    }

    private static int AddDzoMetalMaterial(List<object> materials)
    {
        var index = materials.Count;
        materials.Add(new Dictionary<string, object?>
        {
            ["name"] = "metal",
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = new Dictionary<string, object>
            {
                ["baseColorFactor"] = new[] { 1f, 1f, 1f, 1f },
                ["metallicFactor"] = 1f,
                ["roughnessFactor"] = 0f
            },
            ["extras"] = new Dictionary<string, object?>
            {
                ["MobyMaterialKind"] = "Metal",
                ["MobyMetalReflectionScaleAttribute"] = "_MOBY_METAL_REFLECTION_SCALE"
            }
        });
        return index;
    }

    private static void ConfigureDzoEmission(IEnumerable<object> materials)
    {
        var emissiveFactor = new[] { 0f, 0f, 0f };
        foreach (var material in materials.OfType<Dictionary<string, object?>>())
        {
            if (material.TryGetValue("name", out var name)
                && string.Equals(name as string, "metal", StringComparison.Ordinal))
            {
                material["emissiveFactor"] = emissiveFactor;
                material.Remove("emissiveTexture");
                continue;
            }

            if (material.TryGetValue("pbrMetallicRoughness", out var pbrValue)
                && pbrValue is Dictionary<string, object> pbr
                && pbr.TryGetValue("baseColorTexture", out var baseColorTexture))
            {
                material["emissiveTexture"] = baseColorTexture;
            }

            material["emissiveFactor"] = emissiveFactor;
        }
    }

    private static DzoAlphaPass ClassifyDzoAlphaPass(
        int? textureId,
        IReadOnlyList<Vector2>? texCoords,
        IReadOnlyList<uint> indices,
        IReadOnlyDictionary<int, MobyDzoGltfTextureAlphaMap>? alphaMaps,
        IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha)
    {
        if (!textureId.HasValue
            || texCoords is null
            || alphaMaps is null
            || !alphaMaps.TryGetValue(textureId.Value, out var alphaMap)
            || alphaMap.Width <= 0
            || alphaMap.Height <= 0
            || alphaMap.Alpha.Length < checked(alphaMap.Width * alphaMap.Height))
        {
            return textureId.HasValue
                && textureAlpha is not null
                && textureAlpha.TryGetValue(textureId.Value, out var alpha)
                ? ToDzoAlphaPass(alpha)
                : DzoAlphaPass.Opaque;
        }

        var sampleCount = 0;
        var transparentSampleCount = 0;
        var intermediateSampleCount = 0;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 >= texCoords.Count || i1 >= texCoords.Count || i2 >= texCoords.Count)
            {
                continue;
            }

            ScanWrappedTriangleAlpha(
                alphaMap,
                texCoords[(int)i0],
                texCoords[(int)i1],
                texCoords[(int)i2],
                AddAlpha);
            if (sampleCount == 0)
            {
                AddAlpha(SampleWrappedAlpha(alphaMap, texCoords[(int)i0]));
                AddAlpha(SampleWrappedAlpha(alphaMap, texCoords[(int)i1]));
                AddAlpha(SampleWrappedAlpha(alphaMap, texCoords[(int)i2]));
            }
        }

        if (sampleCount == 0)
        {
            return textureAlpha is not null
                && textureAlpha.TryGetValue(textureId.Value, out var alpha)
                ? ToDzoAlphaPass(alpha)
                : DzoAlphaPass.Opaque;
        }

        // A shared atlas can contain a few translucent texels used by another
        // packet. Do not move the whole packet to the transparent pass merely
        // because its UV footprint touches one of those texels. The original
        // moby packet is non-opaque only when non-opaque alpha dominates the
        // texels covered by that packet.
        var nonOpaqueSampleCount = transparentSampleCount + intermediateSampleCount;
        if ((long)nonOpaqueSampleCount * 2 < sampleCount)
        {
            return DzoAlphaPass.Opaque;
        }

        return intermediateSampleCount > 0
            ? DzoAlphaPass.Blend
            : DzoAlphaPass.Mask;

        void AddAlpha(byte alpha)
        {
            sampleCount++;
            transparentSampleCount += alpha == 0 ? 1 : 0;
            intermediateSampleCount += alpha is > 0 and < byte.MaxValue ? 1 : 0;
        }
    }

    private static DzoAlphaPass ToDzoAlphaPass(TextureAlphaInfo alpha)
    {
        return alpha.AlphaMode switch
        {
            TextureAlphaMode.Mask => DzoAlphaPass.Mask,
            TextureAlphaMode.Blend => DzoAlphaPass.Blend,
            _ => DzoAlphaPass.Opaque
        };
    }

    private static void ScanWrappedTriangleAlpha(
        MobyDzoGltfTextureAlphaMap alphaMap,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Action<byte> addAlpha)
    {
        a = WrapUv(a);
        b = a + WrapUvDelta(b - a);
        c = a + WrapUvDelta(c - a);
        for (var tileY = -1; tileY <= 1; tileY++)
        {
            for (var tileX = -1; tileX <= 1; tileX++)
            {
                var offset = new Vector2(tileX, tileY);
                ScanTriangleAlpha(alphaMap, a + offset, b + offset, c + offset, addAlpha);
            }
        }
    }

    private static Vector2 WrapUvDelta(Vector2 delta)
    {
        return new Vector2(WrapUvDeltaComponent(delta.X), WrapUvDeltaComponent(delta.Y));
    }

    private static float WrapUvDeltaComponent(float delta)
    {
        var nearestInteger = MathF.Round(delta);
        return MathF.Abs(delta - nearestInteger) <= 1e-6f
            ? delta
            : delta - nearestInteger;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static void ScanTriangleAlpha(
        MobyDzoGltfTextureAlphaMap alphaMap,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Action<byte> addAlpha)
    {
        var area = Cross(b - a, c - a);
        if (MathF.Abs(area) <= 1e-8f)
        {
            return;
        }

        var minX = Math.Clamp((int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) * alphaMap.Width), 0, alphaMap.Width - 1);
        var maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)) * alphaMap.Width), 0, alphaMap.Width - 1);
        var minY = Math.Clamp((int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) * alphaMap.Height), 0, alphaMap.Height - 1);
        var maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) * alphaMap.Height), 0, alphaMap.Height - 1);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new Vector2(
                    (x + 0.5f) / alphaMap.Width,
                    (y + 0.5f) / alphaMap.Height);
                var w0 = Cross(b - a, point - a) / area;
                var w1 = Cross(c - b, point - b) / area;
                var w2 = Cross(a - c, point - c) / area;
                if (w0 >= -1e-5f && w1 >= -1e-5f && w2 >= -1e-5f)
                {
                    addAlpha(alphaMap.Alpha[y * alphaMap.Width + x]);
                }
            }
        }
    }

    private static byte SampleWrappedAlpha(MobyDzoGltfTextureAlphaMap alphaMap, Vector2 uv)
    {
        uv = WrapUv(uv);
        var x = Math.Clamp((int)MathF.Floor(uv.X * alphaMap.Width), 0, alphaMap.Width - 1);
        var y = Math.Clamp((int)MathF.Floor(uv.Y * alphaMap.Height), 0, alphaMap.Height - 1);
        return alphaMap.Alpha[y * alphaMap.Width + x];
    }

    private static int ResolveDzoAlphaPassMaterialIndex(
        int baseMaterialIndex,
        int? textureId,
        DzoAlphaPass alphaPass,
        List<object> materials,
        IDictionary<(int MaterialIndex, DzoAlphaPass AlphaPass), int> materialIndices,
        ISet<int> claimedBaseMaterialIndices)
    {
        if (baseMaterialIndex < 0)
        {
            return -1;
        }

        var key = (baseMaterialIndex, alphaPass);
        if (materialIndices.TryGetValue(key, out var existingIndex))
        {
            return existingIndex;
        }

        if (baseMaterialIndex >= materials.Count
            || materials[baseMaterialIndex] is not Dictionary<string, object?> baseMaterial)
        {
            return baseMaterialIndex;
        }

        var reuseBaseMaterial = claimedBaseMaterialIndices.Add(baseMaterialIndex);
        var material = reuseBaseMaterial
            ? baseMaterial
            : new Dictionary<string, object?>(baseMaterial);
        var passName = alphaPass.ToString().ToLowerInvariant();
        if (textureId.HasValue)
        {
            material["name"] = $"tex_{passName}_{textureId.Value:0000}";
        }
        else if (!material.ContainsKey("name"))
        {
            material["name"] = $"tex_{passName}_none";
        }
        material["extras"] = MergeDzoAlphaPassExtras(
            baseMaterial.TryGetValue("extras", out var extras) ? extras : null,
            alphaPass);
        switch (alphaPass)
        {
            case DzoAlphaPass.Opaque:
                material.Remove("alphaMode");
                material.Remove("alphaCutoff");
                break;
            case DzoAlphaPass.Mask:
                material["alphaMode"] = "MASK";
                material["alphaCutoff"] = 0.5f;
                break;
            case DzoAlphaPass.Blend:
                material["alphaMode"] = "BLEND";
                material.Remove("alphaCutoff");
                break;
        }

        var materialIndex = reuseBaseMaterial ? baseMaterialIndex : materials.Count;
        if (!reuseBaseMaterial)
        {
            materials.Add(material);
        }
        materialIndices.Add(key, materialIndex);
        return materialIndex;
    }

    private static Dictionary<string, object?> MergeDzoAlphaPassExtras(object? existingExtras, DzoAlphaPass alphaPass)
    {
        var extras = existingExtras is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(existingExtras)) ?? new Dictionary<string, object?>();
        extras["MobyAlphaPass"] = alphaPass.ToString();
        return extras;
    }

    private static Dictionary<string, object> WriteMergedDzoPrimitive(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<DzoPrimitiveSource> sources,
        int materialIndex)
    {
        var includeMetalReflectionScale = sources.Any(source => source.MetalReflectionScales is not null);
        // Some glTF importers discard TEXCOORD_1 when TEXCOORD_0 is absent. Metal
        // geometry has no authored texture UVs, so provide a zero-filled first set
        // to ensure DZO's bangle/emission metadata survives in the second set.
        var includeTexCoords = includeMetalReflectionScale
            || sources.Any(source => source.TexCoords is not null);
        var includeSkin = sources.Any(source => source.Joints is not null && source.Weights is not null);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = includeTexCoords ? new List<Vector2>() : null;
        var metalReflectionScales = includeMetalReflectionScale ? new List<float>() : null;
        var metadataUvs = new List<Vector2>();
        var joints = includeSkin ? new List<ushort[]>() : null;
        var weights = includeSkin ? new List<float[]>() : null;
        var indices = new List<uint>();

        foreach (var source in sources)
        {
            var remappedIndices = new Dictionary<uint, uint>();
            foreach (var sourceIndex in source.Indices)
            {
                if (sourceIndex >= source.Positions.Count
                    || sourceIndex >= source.Normals.Count)
                {
                    throw new InvalidDataException("DZO primitive contains an out-of-range vertex index.");
                }

                if (!remappedIndices.TryGetValue(sourceIndex, out var mergedIndex))
                {
                    mergedIndex = checked((uint)positions.Count);
                    remappedIndices.Add(sourceIndex, mergedIndex);
                    positions.Add(source.Positions[(int)sourceIndex]);
                    normals.Add(source.Normals[(int)sourceIndex]);
                    texCoords?.Add(source.TexCoords is not null && sourceIndex < source.TexCoords.Count
                        ? source.TexCoords[(int)sourceIndex]
                        : Vector2.Zero);
                    metalReflectionScales?.Add(
                        source.MetalReflectionScales is not null && sourceIndex < source.MetalReflectionScales.Count
                            ? source.MetalReflectionScales[(int)sourceIndex]
                            : 0.5f);
                    var metadataUv = source.MetadataUv;
                    if (source.MetalReflectionScales is not null
                        && sourceIndex < source.MetalReflectionScales.Count)
                    {
                        // Unity's glTF import path flips UV Y, so write one minus the
                        // value here and expose the reflection strength as UV2.y there.
                        metadataUv.Y = 1f - Math.Clamp(
                            source.MetalReflectionScales[(int)sourceIndex],
                            0f,
                            1f);
                    }
                    metadataUvs.Add(metadataUv);
                    joints?.Add(source.Joints is not null && sourceIndex < source.Joints.Count
                        ? source.Joints[(int)sourceIndex]
                        : [0, 0, 0, 0]);
                    weights?.Add(source.Weights is not null && sourceIndex < source.Weights.Count
                        ? source.Weights[(int)sourceIndex]
                        : [1f, 0f, 0f, 0f]);
                }

                indices.Add(mergedIndex);
            }
        }

        var attributes = new Dictionary<string, int>
        {
            ["POSITION"] = WriteVector3Accessor(writer, bufferViews, accessors, positions, includeBounds: true),
            ["NORMAL"] = WriteVector3Accessor(writer, bufferViews, accessors, normals, includeBounds: false),
            ["TEXCOORD_1"] = WriteVector2Accessor(writer, bufferViews, accessors, metadataUvs)
        };
        if (texCoords is not null)
        {
            attributes["TEXCOORD_0"] = WriteVector2Accessor(writer, bufferViews, accessors, texCoords);
        }
        if (metalReflectionScales is not null)
        {
            attributes["_MOBY_METAL_REFLECTION_SCALE"] = WriteFloatAccessor(
                writer,
                bufferViews,
                accessors,
                metalReflectionScales);
        }
        if (joints is not null && weights is not null)
        {
            attributes["JOINTS_0"] = WriteJointsAccessor(writer, bufferViews, accessors, joints);
            attributes["WEIGHTS_0"] = WriteWeightsAccessor(writer, bufferViews, accessors, weights);
        }

        var primitive = new Dictionary<string, object>
        {
            ["attributes"] = attributes,
            ["indices"] = WriteIndexAccessor(writer, bufferViews, accessors, indices),
            ["mode"] = 4,
            ["extras"] = BuildMergedDzoExtras(sources)
        };
        if (materialIndex >= 0)
        {
            primitive["material"] = materialIndex;
        }

        return primitive;
    }

    private static object BuildMergedDzoExtras(IReadOnlyList<DzoPrimitiveSource> sources)
    {
        var sourceExtras = sources
            .Select(source => source.Primitive.TryGetValue("extras", out var extras) ? extras : null)
            .Where(extras => extras is not null)
            .ToArray();
        if (sourceExtras.Length == 1)
        {
            return sourceExtras[0]!;
        }

        var extrasDictionaries = sourceExtras.OfType<Dictionary<string, object?>>().ToArray();
        return new Dictionary<string, object?>
        {
            ["MobyMeshIndices"] = extrasDictionaries
                .Select(extras => extras.TryGetValue("MobyMeshIndex", out var value) ? value : null)
                .Where(value => value is not null)
                .Distinct()
                .ToArray(),
            ["MobyMeshTypes"] = extrasDictionaries
                .Select(extras => extras.TryGetValue("MobyMeshType", out var value) ? value : null)
                .Where(value => value is not null)
                .Distinct()
                .ToArray(),
            ["MobyGlowVertexCount"] = extrasDictionaries.Sum(extras =>
                extras.TryGetValue("MobyGlowVertexCount", out var value) && value is int count ? count : 0),
            ["MobyGlowRgba"] = extrasDictionaries
                .Select(extras => extras.TryGetValue("MobyGlowRgba", out var value) ? value : null)
                .FirstOrDefault(value => value is not null),
            ["MobyAlphaPass"] = extrasDictionaries
                .Select(extras => extras.TryGetValue("MobyAlphaPass", out var value) ? value : null)
                .FirstOrDefault(value => value is not null),
            ["MobyBangleIds"] = extrasDictionaries
                .Select(extras => extras.TryGetValue("MobyBangleId", out var value) ? value : null)
                .Where(value => value is not null)
                .Distinct()
                .ToArray(),
            ["MobySourcePrimitives"] = sourceExtras
        };
    }

    private static int WriteVector3Accessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<Vector3> values,
        bool includeBounds)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        var bufferView = AddVertexBufferView(bufferViews, byteOffset, checked(values.Count * 3 * sizeof(float)));
        var accessor = accessors.Count;
        if (includeBounds)
        {
            var min = values.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = values.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            accessors.Add(new
            {
                bufferView,
                byteOffset = 0,
                componentType = 5126,
                count = values.Count,
                type = "VEC3",
                min = new[] { min.X, min.Y, min.Z },
                max = new[] { max.X, max.Y, max.Z }
            });
        }
        else
        {
            accessors.Add(new
            {
                bufferView,
                byteOffset = 0,
                componentType = 5126,
                count = values.Count,
                type = "VEC3"
            });
        }
        return accessor;
    }

    private static int WriteVector2Accessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<Vector2> values)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
        }

        var min = values.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        var max = values.Aggregate(new Vector2(float.MinValue), Vector2.Max);
        var bufferView = AddVertexBufferView(bufferViews, byteOffset, checked(values.Count * 2 * sizeof(float)));
        var accessor = accessors.Count;
        accessors.Add(new
        {
            bufferView,
            byteOffset = 0,
            componentType = 5126,
            count = values.Count,
            type = "VEC2",
            min = new[] { min.X, min.Y },
            max = new[] { max.X, max.Y }
        });
        return accessor;
    }

    private static int WriteFloatAccessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<float> values)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            writer.Write(value);
        }

        var bufferView = AddVertexBufferView(bufferViews, byteOffset, checked(values.Count * sizeof(float)));
        var accessor = accessors.Count;
        accessors.Add(new
        {
            bufferView,
            byteOffset = 0,
            componentType = 5126,
            count = values.Count,
            type = "SCALAR",
            min = new[] { values.Min() },
            max = new[] { values.Max() }
        });
        return accessor;
    }

    private static int WriteJointsAccessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<ushort[]> values)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            for (var component = 0; component < 4; component++)
            {
                writer.Write(value[component]);
            }
        }

        var bufferView = AddVertexBufferView(bufferViews, byteOffset, checked(values.Count * 4 * sizeof(ushort)));
        var accessor = accessors.Count;
        accessors.Add(new
        {
            bufferView,
            byteOffset = 0,
            componentType = 5123,
            count = values.Count,
            type = "VEC4"
        });
        return accessor;
    }

    private static int WriteWeightsAccessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<float[]> values)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            for (var component = 0; component < 4; component++)
            {
                writer.Write(value[component]);
            }
        }

        var bufferView = AddVertexBufferView(bufferViews, byteOffset, checked(values.Count * 4 * sizeof(float)));
        var accessor = accessors.Count;
        accessors.Add(new
        {
            bufferView,
            byteOffset = 0,
            componentType = 5126,
            count = values.Count,
            type = "VEC4"
        });
        return accessor;
    }

    private static int AddVertexBufferView(List<object> bufferViews, int byteOffset, int byteLength)
    {
        var bufferView = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset,
            byteLength,
            target = 34962
        });
        return bufferView;
    }

    private sealed record DzoPrimitiveSource(
        Dictionary<string, object> Primitive,
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<Vector3> Normals,
        IReadOnlyList<Vector2>? TexCoords,
        IReadOnlyList<float>? MetalReflectionScales,
        Vector2 MetadataUv,
        IReadOnlyList<ushort[]>? Joints,
        IReadOnlyList<float[]>? Weights,
        IReadOnlyList<uint> Indices,
        int? TextureId,
        DzoAlphaPass AlphaPass);

    private readonly record struct DzoMaterialGroupKey(int MaterialIndex, DzoAlphaPass AlphaPass);

    private enum DzoAlphaPass
    {
        Opaque,
        Mask,
        Blend
    }
}

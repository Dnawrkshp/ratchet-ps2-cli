using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Moby;

public sealed record MobyDzoGltfExportOptions
{
    public const float DefaultNonOpaqueAlphaCoverageThreshold = 0.1f;

    public bool IncludeDebugUvColors { get; init; }
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
    public MobyGltfSkeletonParentMode SkeletonParentMode { get; init; } = MobyGltfSkeletonParentMode.Auto;
    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }
    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }
    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }
    public IReadOnlyDictionary<int, MobyDzoGltfTextureAlphaMap>? ExternalTextureAlphaMaps { get; init; }
    public float NonOpaqueAlphaCoverageThreshold { get; init; } = DefaultNonOpaqueAlphaCoverageThreshold;
    public MobyGltfLowLodTextureMode LowLodTextureMode { get; init; } = MobyGltfLowLodTextureMode.Rolling;
    public IReadOnlyDictionary<int, int>? MeshTextureOverrides { get; init; }
    public bool InferTextureIdsFromUvTiles { get; init; } = true;
    public bool HonorSkeletonParentRotationFlags { get; init; } = true;
    public IReadOnlyList<Matrix4x4>? InverseBindMatrices { get; init; }
    public byte TextureFullOpacityAlpha { get; init; } = byte.MaxValue;
    public string? BufferFileName { get; init; }
    public bool FlattenJointHierarchy { get; init; }
}

public sealed record MobyDzoGltfTextureAlphaMap(int Width, int Height, byte[] Alpha);

/// <summary>
/// Exports moby geometry using the DZO-specific glTF layout.
/// </summary>
public static class MobyDzoGltfExporter
{
    public static MobyGltfExport Export(
        Stream input,
        string gltfFileName = "moby.gltf",
        MobyDzoGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new MobyDzoGltfExportOptions();
        return MobyGltfExporter.ExportDzo(
            MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    SkipAnimationSequences = true,
                    AnimationFormat = options.AnimationFormat
                }),
            gltfFileName,
            options);
    }

    public static MobyGltfExport Export(
        MobyModel model,
        string gltfFileName = "moby.gltf",
        MobyDzoGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        return MobyGltfExporter.ExportDzo(
            model,
            gltfFileName,
            options ?? new MobyDzoGltfExportOptions());
    }
}

// This partial shares only the low-level moby decoding/accessor primitives. The
// DZO orchestration remains independent from MobyGltfExporter.Export.
public static partial class MobyGltfExporter
{
    internal static MobyGltfExport ExportDzo(
        MobyModel model,
        string gltfFileName,
        MobyDzoGltfExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!float.IsFinite(options.NonOpaqueAlphaCoverageThreshold)
            || options.NonOpaqueAlphaCoverageThreshold is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.NonOpaqueAlphaCoverageThreshold,
                "The non-opaque alpha coverage threshold must be between 0 and 1.");
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        var materials = new List<object>();
        var images = new List<object>();
        var textures = new List<object>();
        var materialIndexByTextureId = new Dictionary<int, int>();
        var nodes = new List<object>();
        var sceneNodes = new List<int>();
        var hierarchy = new GltfNodeHierarchy(nodes, sceneNodes);
        var dzoPrimitiveSources = new List<DzoPrimitiveSource>();
        var diagnostics = new List<object>();
        var scale = 1f / 1024f;
        var rollingVertexCache = new Vector3?[512];
        var rollingNormalCache = new Vector3?[512];
        var rollingJointCache = new ushort[512][];
        var rollingWeightCache = new float[512][];
        var rollingBlendCache = new SkinBlend?[64];
        var sourceSkinByPosition = new Dictionary<(short X, short Y, short Z), (ushort[] Joints, float[] Weights)>();
        var highLodTextureBounds = new List<TextureBounds>();
        var highLodTextureTriangles = new List<TextureTriangle>();

        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var skins = new List<object>();
        var skinContext = TryBuildDzoSkinContext(model, scale, nodes, hierarchy, skins, options);
        var debugUvMaterialIndex = options.IncludeDebugUvColors
            ? AddDebugUvMaterial(materials)
            : (int?)null;
        int? dzoMetalMaterialIndex = null;
        var inferTextureIdsFromUvTiles = options.InferTextureIdsFromUvTiles
            && options.ExternalTextureUris is not null
            && options.ExternalTextureUris.ContainsKey(0)
            && options.ExternalTextureUris.ContainsKey(1);
        var activeTextureIdByMeshType = new Dictionary<MobyMeshType, int?>();

        for (var meshIndex = 0; meshIndex < (model.MeshTable?.Entries.Count ?? 0); meshIndex++)
        {
            var entry = model.MeshTable!.Entries[meshIndex];
            var bangleGroup = entry.MeshType == MobyMeshType.Bangle
                ? ResolveBangleMeshGroup(model.BangleTable, meshIndex)
                : null;
            if (!ShouldExportDzoMesh(entry.MeshType, bangleGroup))
            {
                diagnostics.Add(new
                {
                    MeshIndex = meshIndex,
                    entry.MeshType,
                    entry.VertexCount,
                    Skipped = true,
                    Reason = "DZO mesh selection"
                });
                continue;
            }
            var glowVertexCount = GetGlowVertexCount(entry);

            var explicitTextureId = TryGetPrimaryTextureId(entry, out var primaryTextureId)
                ? primaryTextureId
                : (int?)null;
            var effectiveTextureId = activeTextureIdByMeshType.TryGetValue(entry.MeshType, out var activeTextureId)
                ? activeTextureId
                : 0;
            if (!TryExtractMesh(
                    entry,
                    scale,
                    rollingVertexCache,
                    rollingNormalCache,
                    rollingJointCache,
                    rollingWeightCache,
                    rollingBlendCache,
                    sourceSkinByPosition,
                    effectiveTextureId,
                    out var positions,
                    out var normals,
                    out var validMask,
                    out var joints,
                    out var weights,
                    out var indices,
                    out var topologyTextureGroups,
                    out var finalTextureId,
                    out var meshDiagnostic))
            {
                diagnostics.Add(new
                {
                    MeshIndex = meshIndex,
                    entry.MeshType,
                    entry.VertexCount,
                    Skipped = true,
                    Reason = "No usable positions or topology",
                    Detail = meshDiagnostic
                });
                continue;
            }
            activeTextureIdByMeshType[entry.MeshType] = finalTextureId;

            Align(writer, 4);
            var positionByteOffset = checked((int)writer.BaseStream.Position);
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var position in positions)
            {
                writer.Write(position.X);
                writer.Write(position.Y);
                writer.Write(position.Z);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var positionBounds = new Bounds3(min, max);
            var materialTextureId = effectiveTextureId;
            if (entry.MeshType == MobyMeshType.LowLod)
            {
                materialTextureId = ResolveLowLodTextureId(
                    positionBounds,
                    explicitTextureId,
                    effectiveTextureId,
                    highLodTextureBounds,
                    options.LowLodTextureMode);
            }

            if (options.MeshTextureOverrides is not null
                && options.MeshTextureOverrides.TryGetValue(meshIndex, out var overrideTextureId))
            {
                materialTextureId = overrideTextureId;
            }
            else if (entry.MeshType == MobyMeshType.HighLod && effectiveTextureId.HasValue)
            {
                highLodTextureBounds.Add(new TextureBounds(positionBounds, effectiveTextureId.Value));
            }

            var positionBufferView = bufferViews.Count;
            bufferViews.Add(new
            {
                buffer = 0,
                byteOffset = positionByteOffset,
                byteLength = positions.Count * 3 * sizeof(float),
                target = 34962
            });

            var positionAccessor = accessors.Count;
            accessors.Add(new
            {
                bufferView = positionBufferView,
                byteOffset = 0,
                componentType = 5126,
                count = positions.Count,
                type = "VEC3",
                min = new[] { min.X, min.Y, min.Z },
                max = new[] { max.X, max.Y, max.Z }
            });

            Align(writer, 4);
            var normalByteOffset = checked((int)writer.BaseStream.Position);
            foreach (var normal in normals)
            {
                writer.Write(normal.X);
                writer.Write(normal.Y);
                writer.Write(normal.Z);
            }

            var normalBufferView = bufferViews.Count;
            bufferViews.Add(new
            {
                buffer = 0,
                byteOffset = normalByteOffset,
                byteLength = normals.Count * 3 * sizeof(float),
                target = 34962
            });

            var normalAccessor = accessors.Count;
            accessors.Add(new
            {
                bufferView = normalBufferView,
                byteOffset = 0,
                componentType = 5126,
                count = normals.Count,
                type = "VEC3"
            });

            int? metalReflectionScaleAccessor = null;
            float[]? metalReflectionScales = null;
            if (entry.MeshType == MobyMeshType.Metal)
            {
                Align(writer, 4);
                var reflectionScaleByteOffset = checked((int)writer.BaseStream.Position);
                metalReflectionScales = new float[positions.Count];
                for (var i = 0; i < metalReflectionScales.Length; i++)
                {
                    // The VU maps the unit reflection vector from [-1, 1] into texture space.
                    metalReflectionScales[i] = 0.5f;
                    writer.Write(metalReflectionScales[i]);
                }

                var reflectionScaleBufferView = bufferViews.Count;
                bufferViews.Add(new
                {
                    buffer = 0,
                    byteOffset = reflectionScaleByteOffset,
                    byteLength = metalReflectionScales.Length * sizeof(float),
                    target = 34962
                });

                metalReflectionScaleAccessor = accessors.Count;
                accessors.Add(new
                {
                    bufferView = reflectionScaleBufferView,
                    byteOffset = 0,
                    componentType = 5126,
                    count = metalReflectionScales.Length,
                    type = "SCALAR",
                    min = new[] { metalReflectionScales.Min() },
                    max = new[] { metalReflectionScales.Max() }
                });
            }

            int? jointsAccessor = null;
            int? weightsAccessor = null;
            int? texCoordAccessor = null;
            int? debugColorAccessor = null;
            int? dzoMetadataUvAccessor = null;
            Vector2? dzoMetadataUv = null;
            List<Vector2>? texCoordsForMaterialMapping = null;
            if (TryExtractTexCoords(entry, positions.Count, out var texCoords))
            {
                texCoordsForMaterialMapping = texCoords;
                Align(writer, 4);
                var texCoordByteOffset = checked((int)writer.BaseStream.Position);
                foreach (var texCoord in texCoords)
                {
                    writer.Write(texCoord.X);
                    writer.Write(texCoord.Y);
                }

                var texCoordBufferView = bufferViews.Count;
                bufferViews.Add(new
                {
                    buffer = 0,
                    byteOffset = texCoordByteOffset,
                    byteLength = texCoords.Count * 2 * sizeof(float),
                    target = 34962
                });

                texCoordAccessor = accessors.Count;
                accessors.Add(new
                {
                    bufferView = texCoordBufferView,
                    byteOffset = 0,
                    componentType = 5126,
                    count = texCoords.Count,
                    type = "VEC2"
                });

                if (debugUvMaterialIndex.HasValue)
                {
                    Align(writer, 4);
                    var colorByteOffset = checked((int)writer.BaseStream.Position);
                    foreach (var color in BuildDebugUvColors(texCoords))
                    {
                        writer.Write(color.X);
                        writer.Write(color.Y);
                        writer.Write(color.Z);
                        writer.Write(color.W);
                    }

                    var colorBufferView = bufferViews.Count;
                    bufferViews.Add(new
                    {
                        buffer = 0,
                        byteOffset = colorByteOffset,
                        byteLength = texCoords.Count * 4 * sizeof(float),
                        target = 34962
                    });

                    debugColorAccessor = accessors.Count;
                    accessors.Add(new
                    {
                        bufferView = colorBufferView,
                        byteOffset = 0,
                        componentType = 5126,
                        count = texCoords.Count,
                        type = "VEC4"
                    });
                }
            }

            if (skinContext is not null && joints.Count == positions.Count && weights.Count == positions.Count)
            {
                NormalizeSkinRows(joints, weights, entry.CommonTransformJointIndex, skinContext);

                Align(writer, 4);
                var jointsByteOffset = checked((int)writer.BaseStream.Position);
                foreach (var row in joints)
                {
                    writer.Write(row[0]);
                    writer.Write(row[1]);
                    writer.Write(row[2]);
                    writer.Write(row[3]);
                }

                var jointsBufferView = bufferViews.Count;
                bufferViews.Add(new
                {
                    buffer = 0,
                    byteOffset = jointsByteOffset,
                    byteLength = joints.Count * 4 * sizeof(ushort),
                    target = 34962
                });

                jointsAccessor = accessors.Count;
                accessors.Add(new
                {
                    bufferView = jointsBufferView,
                    byteOffset = 0,
                    componentType = 5123,
                    count = joints.Count,
                    type = "VEC4"
                });

                Align(writer, 4);
                var weightsByteOffset = checked((int)writer.BaseStream.Position);
                foreach (var row in weights)
                {
                    writer.Write(row[0]);
                    writer.Write(row[1]);
                    writer.Write(row[2]);
                    writer.Write(row[3]);
                }

                var weightsBufferView = bufferViews.Count;
                bufferViews.Add(new
                {
                    buffer = 0,
                    byteOffset = weightsByteOffset,
                    byteLength = weights.Count * 4 * sizeof(float),
                    target = 34962
                });

                weightsAccessor = accessors.Count;
                accessors.Add(new
                {
                    bufferView = weightsBufferView,
                    byteOffset = 0,
                    componentType = 5126,
                    count = weights.Count,
                    type = "VEC4"
                });
            }

            Align(writer, 4);
            var bangleUvByteOffset = checked((int)writer.BaseStream.Position);
            var bangleIndex = entry.MeshType == MobyMeshType.Bangle && bangleGroup.HasValue
                ? bangleGroup.Value.Index
                : 15;
            var normalizedBangleIndex = bangleIndex / 255f;
            // DZO/Unity flips the imported UV Y coordinate. Non-metal geometry
            // stores the inverse emission mask; metal stores one minus reflection
            // strength so Unity exposes the authored strength directly in UV2.y.
            var metadataY = entry.MeshType == MobyMeshType.Metal
                ? 1f - (metalReflectionScales?.FirstOrDefault() ?? 0.5f)
                : model.GlowRgba != 0 && glowVertexCount > 0 ? 0f : 1f;
            dzoMetadataUv = new Vector2(normalizedBangleIndex, metadataY);
            for (var i = 0; i < positions.Count; i++)
            {
                writer.Write(normalizedBangleIndex);
                writer.Write(metadataY);
            }

            var bangleUvBufferView = bufferViews.Count;
            bufferViews.Add(new
            {
                buffer = 0,
                byteOffset = bangleUvByteOffset,
                byteLength = positions.Count * 2 * sizeof(float),
                target = 34962
            });
            dzoMetadataUvAccessor = accessors.Count;
            accessors.Add(new
            {
                bufferView = bangleUvBufferView,
                byteOffset = 0,
                componentType = 5126,
                count = positions.Count,
                type = "VEC2",
                min = new[] { normalizedBangleIndex, metadataY },
                max = new[] { normalizedBangleIndex, metadataY }
            });

            var attributes = new Dictionary<string, int>
            {
                ["POSITION"] = positionAccessor,
                ["NORMAL"] = normalAccessor
            };
            if (texCoordAccessor.HasValue)
            {
                attributes["TEXCOORD_0"] = texCoordAccessor.Value;
            }

            if (metalReflectionScaleAccessor.HasValue)
            {
                attributes["_MOBY_METAL_REFLECTION_SCALE"] = metalReflectionScaleAccessor.Value;
            }

            if (dzoMetadataUvAccessor.HasValue)
            {
                attributes["TEXCOORD_1"] = dzoMetadataUvAccessor.Value;
            }
            else if (debugColorAccessor.HasValue)
            {
                attributes["COLOR_0"] = debugColorAccessor.Value;
            }

            if (jointsAccessor.HasValue && weightsAccessor.HasValue)
            {
                attributes["JOINTS_0"] = jointsAccessor.Value;
                attributes["WEIGHTS_0"] = weightsAccessor.Value;
            }

            var inferTextureIdsFromUvTilesForMesh = inferTextureIdsFromUvTiles && explicitTextureId.HasValue;
            var primitiveIndexGroups = BuildPrimitiveIndexGroups(
                entry.MeshType,
                options.LowLodTextureMode,
                positions,
                texCoordsForMaterialMapping,
                indices,
                topologyTextureGroups,
                materialTextureId,
                highLodTextureTriangles,
                inferTextureIdsFromUvTilesForMesh);
            if (entry.MeshType == MobyMeshType.HighLod)
            {
                foreach (var group in primitiveIndexGroups)
                {
                    if (!group.TextureId.HasValue)
                    {
                        continue;
                    }

                    AddTextureTriangleSamples(
                        highLodTextureTriangles,
                        positions,
                        texCoordsForMaterialMapping,
                        group.Indices,
                        group.TextureId.Value,
                        inferTextureIdsFromUvTilesForMesh);
                }
            }

            var primitives = new List<Dictionary<string, object>>();
            foreach (var group in primitiveIndexGroups)
            {
                var dzoAlphaPass = ClassifyDzoAlphaPass(
                    group.TextureId,
                    texCoordsForMaterialMapping,
                    group.Indices,
                    options.ExternalTextureAlphaMaps,
                    options.ExternalTextureAlpha,
                    options.NonOpaqueAlphaCoverageThreshold);

                var indexAccessor = WriteIndexAccessor(writer, bufferViews, accessors, group.Indices);
                var primitive = new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["mode"] = 4
                };
                if (debugUvMaterialIndex.HasValue && debugColorAccessor.HasValue)
                {
                    primitive["material"] = debugUvMaterialIndex.Value;
                }
                else if (entry.MeshType == MobyMeshType.Metal)
                {
                    dzoMetalMaterialIndex ??= AddDzoMetalMaterial(materials);
                    primitive["material"] = dzoMetalMaterialIndex.Value;
                }
                else if (entry.MeshType != MobyMeshType.Metal
                         && TryGetExternalTextureMaterialIndex(
                             group.TextureId,
                             options.ExternalTextureUris,
                             options.ExternalTextureSizes,
                             options.ExternalTextureAlpha,
                             options.TextureFullOpacityAlpha,
                             images,
                             textures,
                             materials,
                             materialIndexByTextureId,
                             out var textureMaterialIndex))
                {
                    primitive["material"] = textureMaterialIndex;
                }
                primitive["extras"] = new Dictionary<string, object?>
                {
                    ["MobyMeshIndex"] = meshIndex,
                    ["MobyMeshType"] = entry.MeshType.ToString(),
                    ["MobyGlowVertexCount"] = glowVertexCount,
                    ["MobyGlowRgba"] = $"0x{unchecked((uint)model.GlowRgba):X8}",
                    ["MobyAlphaPass"] = dzoAlphaPass.ToString(),
                    ["MobyBangleId"] = entry.MeshType == MobyMeshType.Bangle && bangleGroup.HasValue
                        ? bangleGroup.Value.Index
                        : 15
                };

                primitives.Add(primitive);
            }

            for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
            {
                dzoPrimitiveSources.Add(new DzoPrimitiveSource(
                    primitives[primitiveIndex],
                    positions,
                    normals,
                    texCoordsForMaterialMapping,
                    metalReflectionScales,
                    dzoMetadataUv ?? Vector2.Zero,
                    jointsAccessor.HasValue ? joints : null,
                    weightsAccessor.HasValue ? weights : null,
                    primitiveIndexGroups[primitiveIndex].Indices,
                    primitiveIndexGroups[primitiveIndex].TextureId,
                    ClassifyDzoAlphaPass(
                        primitiveIndexGroups[primitiveIndex].TextureId,
                        texCoordsForMaterialMapping,
                        primitiveIndexGroups[primitiveIndex].Indices,
                        options.ExternalTextureAlphaMaps,
                        options.ExternalTextureAlpha,
                        options.NonOpaqueAlphaCoverageThreshold)));
            }

            diagnostics.Add(new
            {
                MeshIndex = meshIndex,
                entry.MeshType,
                entry.VertexCount,
                PositionCount = positions.Count,
                TriangleCount = indices.Count / 3,
                InvalidVertexCount = validMask.Count(v => !v),
                Skinning = skinContext is null
                    ? "not_exported"
                    : jointsAccessor.HasValue && weightsAccessor.HasValue
                        ? "exported"
                        : "missing_vertex_influences",
                Detail = meshDiagnostic
            });
        }

        var passMaterialIndices = new Dictionary<(int MaterialIndex, DzoAlphaPass AlphaPass), int>();
        var claimedBaseMaterialIndices = new HashSet<int>();
        var textureIdByMaterialIndex = materialIndexByTextureId
            .ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (var materialGroup in dzoPrimitiveSources
                     .GroupBy(source => new DzoMaterialGroupKey(
                         source.Primitive.TryGetValue("material", out var materialValue)
                             ? (int)materialValue
                             : -1,
                         source.AlphaPass))
                     .OrderBy(group => group.Key.MaterialIndex)
                     .ThenBy(group => group.Key.AlphaPass))
        {
            var textureId = textureIdByMaterialIndex.TryGetValue(
                materialGroup.Key.MaterialIndex,
                out var mappedTextureId)
                ? mappedTextureId
                : (int?)null;
            var materialIndex = ResolveDzoAlphaPassMaterialIndex(
                materialGroup.Key.MaterialIndex,
                textureId,
                materialGroup.Key.AlphaPass,
                materials,
                passMaterialIndices,
                claimedBaseMaterialIndices);
            var mergedPrimitive = WriteMergedDzoPrimitive(
                writer,
                bufferViews,
                accessors,
                materialGroup.ToList(),
                materialIndex);
            var meshIndex = meshes.Count;
            var passName = materialGroup.Key.AlphaPass.ToString().ToLowerInvariant();
            var suffix = textureId.HasValue
                ? $"{passName}_{textureId.Value:0000}"
                : $"{passName}_none";
            meshes.Add(new Dictionary<string, object?>
            {
                ["name"] = $"material_{suffix}",
                ["primitives"] = new[] { mergedPrimitive }
            });
            var nodeIndex = nodes.Count;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"material_{suffix}",
                ["mesh"] = meshIndex
            };
            if (skinContext is not null)
            {
                node["skin"] = skinContext.SkinIndex;
            }
            nodes.Add(node);
            sceneNodes.Add(nodeIndex);
        }

        ConfigureDzoEmission(materials);

        if (skinContext is not null)
        {
            WriteInverseBindMatrices(skinContext, writer, bufferViews, accessors);
        }

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "RatchetPs2 moby glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = sceneNodes.ToArray() } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["buffers"] = new[] { new { uri = binFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors
        };
        if (skins.Count > 0)
        {
            gltf["skins"] = skins;
        }

        if (materials.Count > 0)
        {
            gltf["materials"] = materials;
        }

        if (images.Count > 0)
        {
            gltf["images"] = images;
        }

        if (textures.Count > 0)
        {
            gltf["textures"] = textures;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = "moby geometry",
            Note = "Geometry is reconstructed from moby vertex tables and VIF UNPACK_V4_8 topology. Supplied animation channels are exported when skeleton data are present. Degenerate strip-control triangles are skipped; true duplicate faces are reported separately as a topology warning.",
            Meshes = diagnostics,
            Animations = Array.Empty<object>()
        }, jsonOptions);

        return new MobyGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

}

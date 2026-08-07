using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Moby;

public sealed record MobyGltfExport(byte[] GltfBytes, byte[] BinBytes, byte[] DiagnosticsBytes);

public sealed record MobyGltfAnimationClip(
    int SourceIndex,
    string Name,
    float[] Times,
    IReadOnlyDictionary<int, Quaternion[]> Rotations,
    IReadOnlyDictionary<int, Vector3[]> Scales,
    IReadOnlyDictionary<int, Vector3[]> Translations);

public sealed record MobyGltfAnimationFailure(int SourceIndex, string Reason);

public sealed record MobyGltfExportOptions
{
    public bool IncludeDebugUvColors { get; init; }
    public bool SkipAnimationSequences { get; init; }
    public int? LodIndex { get; init; }
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
    public MobyGltfSkeletonParentMode SkeletonParentMode { get; init; } = MobyGltfSkeletonParentMode.Auto;
    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }
    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }
    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }
    public MobyGltfLowLodTextureMode LowLodTextureMode { get; init; } = MobyGltfLowLodTextureMode.Rolling;
    public IReadOnlyDictionary<int, int>? MeshTextureOverrides { get; init; }
    public bool InferTextureIdsFromUvTiles { get; init; } = true;
    public bool RefineSkinFromInfluences { get; init; } = true;
    public bool HonorSkeletonParentRotationFlags { get; init; } = true;
    public IReadOnlyList<Matrix4x4>? InverseBindMatrices { get; init; }
    public IReadOnlyList<MobyGltfAnimationClip>? Animations { get; init; }
    public IReadOnlyList<MobyGltfAnimationFailure>? AnimationFailures { get; init; }
    public byte TextureFullOpacityAlpha { get; init; } = byte.MaxValue;
    public string? BufferFileName { get; init; }
}

public enum MobyGltfLowLodTextureMode
{
    Rolling,
    ExplicitOnly,
    HighLodOverlap,
    HighLodNearestCenter,
    HighLodNearestTriangle
}

public enum MobyGltfSkeletonParentMode
{
    Auto,
    SixBitShifted,
    SevenBitLow
}

public static class MobyGltfExporter
{
    public static MobyGltfExport Export(Stream input, string gltfFileName = "moby.gltf", MobyGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new MobyGltfExportOptions();
        ValidateLodIndex(options.LodIndex);
        return Export(
            MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    SkipAnimationSequences = options.SkipAnimationSequences,
                    AnimationFormat = options.AnimationFormat
                }),
            gltfFileName,
            options);
    }

    public static MobyGltfExport Export(MobyModel model, string gltfFileName = "moby.gltf", MobyGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new MobyGltfExportOptions();
        ValidateLodIndex(options.LodIndex);

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
        var animations = new List<object>();
        var sceneNodes = new List<int>();
        var hierarchy = new GltfNodeHierarchy(nodes, sceneNodes);
        var diagnostics = new List<object>();
        var animationDiagnostics = new List<object>();
        var modelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
        var scale = modelScale / 1024f;
        var rollingVertexCache = new Vector3?[512];
        var rollingJointCache = new ushort[512][];
        var rollingWeightCache = new float[512][];
        var rollingBlendCache = new SkinBlend?[64];
        var highLodTextureBounds = new List<TextureBounds>();
        var highLodTextureTriangles = new List<TextureTriangle>();

        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var skins = new List<object>();
        var skinContext = TryBuildSkinContext(model, scale, nodes, hierarchy, skins, options);
        var skinAccumulator = skinContext is null || !options.RefineSkinFromInfluences
            ? null
            : new SkinInfluenceAccumulator(skinContext.JointPaletteIndexByJoint.Length);
        var debugUvMaterialIndex = options.IncludeDebugUvColors
            ? AddDebugUvMaterial(materials)
            : (int?)null;
        var inferTextureIdsFromUvTiles = options.InferTextureIdsFromUvTiles
            && options.ExternalTextureUris is not null
            && options.ExternalTextureUris.ContainsKey(0)
            && options.ExternalTextureUris.ContainsKey(1);
        var activeTextureIdByMeshType = new Dictionary<MobyMeshType, int?>();

        for (var meshIndex = 0; meshIndex < (model.MeshTable?.Entries.Count ?? 0); meshIndex++)
        {
            var entry = model.MeshTable!.Entries[meshIndex];
            if (!ShouldExportMesh(entry.MeshType, options.LodIndex))
            {
                diagnostics.Add(new
                {
                    MeshIndex = meshIndex,
                    entry.MeshType,
                    entry.VertexCount,
                    Skipped = true,
                    Reason = $"LOD {options.LodIndex} export"
                });
                continue;
            }

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
                    rollingJointCache,
                    rollingWeightCache,
                    rollingBlendCache,
                    effectiveTextureId,
                    out var positions,
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

            if (skinAccumulator is not null && entry.MeshType != MobyMeshType.LowLod)
            {
                AccumulateJointInfluences(skinAccumulator, positions, validMask, joints, weights, indices);
            }

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

            int? jointsAccessor = null;
            int? weightsAccessor = null;
            int? texCoordAccessor = null;
            int? debugColorAccessor = null;
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

            var gltfMeshIndex = meshes.Count;
            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (texCoordAccessor.HasValue)
            {
                attributes["TEXCOORD_0"] = texCoordAccessor.Value;
            }

            if (debugColorAccessor.HasValue)
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
                else if (TryGetExternalTextureMaterialIndex(
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

                primitives.Add(primitive);
            }

            meshes.Add(new Dictionary<string, object?>
            {
                ["name"] = $"mesh_{meshIndex:0000}_{entry.MeshType}",
                ["primitives"] = primitives,
                ["extras"] = BuildMobyMeshExtras(model, entry, meshIndex, scale, explicitTextureId, effectiveTextureId, materialTextureId)
            });

            var nodeIndex = nodes.Count;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"node_{meshIndex:0000}_{entry.MeshType}",
                ["mesh"] = gltfMeshIndex,
                ["extras"] = BuildMobyNodeExtras(entry, meshIndex)
            };
            if (skinContext is not null && jointsAccessor.HasValue && weightsAccessor.HasValue)
            {
                node["skin"] = skinContext.SkinIndex;
            }

            nodes.Add(node);
            hierarchy.AddMeshNode(entry.MeshType, nodeIndex);

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

        if (skinContext is not null)
        {
            RefineSkinFromInfluences(skinContext, skinAccumulator);
            WriteInverseBindMatrices(skinContext, writer, bufferViews, accessors);
            AddAnimations(
                options,
                skinContext,
                writer,
                bufferViews,
                accessors,
                animations,
                animationDiagnostics);
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

        if (animations.Count > 0)
        {
            gltf["animations"] = animations;
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
            Animations = animationDiagnostics
        }, jsonOptions);

        return new MobyGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

    private static void ValidateLodIndex(int? lodIndex)
    {
        if (lodIndex is not null and not 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "Moby glTF export currently supports only LOD 0, or null for all mesh groups.");
        }
    }

    private static bool ShouldExportMesh(MobyMeshType meshType, int? lodIndex)
    {
        return lodIndex is null
            || meshType is MobyMeshType.HighLod or MobyMeshType.Bangle or MobyMeshType.Metal;
    }

    private sealed class GltfSkinContext
    {
        public required int SkinIndex { get; init; }
        public required int[] JointPaletteIndexByJoint { get; init; }
        public required int[] JointNodeIndices { get; init; }
        public required int[] ParentByJoint { get; init; }
        public required List<int>[] ChildrenByJoint { get; init; }
        public required Vector3[] LocalPositions { get; init; }
        public required Quaternion[] LocalRotations { get; init; }
        public required Vector3[] WorldPositions { get; init; }
        public required Quaternion[] WorldRotations { get; init; }
        public IReadOnlyList<Matrix4x4>? InverseBindMatrices { get; init; }
        public required Dictionary<string, object>[] JointNodes { get; init; }
        public required Dictionary<string, object> Skin { get; init; }
    }

    private sealed class SkinInfluenceAccumulator
    {
        public SkinInfluenceAccumulator(int jointCount)
        {
            PositionSums = new Vector3[jointCount];
            WeightSums = new float[jointCount];
        }

        public Vector3[] PositionSums { get; }
        public float[] WeightSums { get; }
    }

    private readonly struct SkinBlend
    {
        public SkinBlend(byte count, sbyte joint0, sbyte joint1, sbyte joint2, byte weight0, byte weight1, byte weight2)
        {
            Count = count;
            Joint0 = joint0;
            Joint1 = joint1;
            Joint2 = joint2;
            Weight0 = weight0;
            Weight1 = weight1;
            Weight2 = weight2;
        }

        public byte Count { get; }
        public sbyte Joint0 { get; }
        public sbyte Joint1 { get; }
        public sbyte Joint2 { get; }
        public byte Weight0 { get; }
        public byte Weight1 { get; }
        public byte Weight2 { get; }
    }

    private readonly record struct TextureBounds(Bounds3 Bounds, int TextureId);
    private readonly record struct TextureTriangle(Vector3 Centroid, Vector2? UvCentroid, int TextureId);
    private readonly record struct PrimitiveIndexGroup(int? TextureId, List<uint> Indices);
    private readonly record struct VifTextureIndexGroup(int? TextureId, List<uint> Indices);

    private static GltfSkinContext? TryBuildSkinContext(
        MobyModel model,
        float scale,
        List<object> nodes,
        GltfNodeHierarchy hierarchy,
        List<object> skins,
        MobyGltfExportOptions options)
    {
        var bones = model.Skeleton?.Bones;
        var jointCount = Math.Min(model.JointCount, bones?.Count ?? 0);
        if (bones is null || jointCount <= 0)
        {
            return null;
        }

        var parentMode = ResolveSkeletonParentMode(model.CommonTransforms, jointCount, options.SkeletonParentMode);
        var parentByJoint = ReadCommonTransformParents(model.CommonTransforms, jointCount, parentMode);
        var ignoresParentRotation = options.HonorSkeletonParentRotationFlags
            ? ReadCommonTransformParentRotationFlags(model.CommonTransforms, jointCount, parentMode)
            : new bool[jointCount];
        var commonLocalPositions = ReadCommonTransformLocalPositions(model.CommonTransforms, jointCount, scale);
        var worldPositions = new Vector3[jointCount];
        var worldRotations = new Quaternion[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            (worldPositions[i], worldRotations[i]) = DecodeBoneWorldTransform(bones[i], scale);
        }

        var jointNodeIndices = new int[jointCount];
        var exportedLocalPositions = new Vector3[jointCount];
        var exportedLocalRotations = new Quaternion[jointCount];
        var exportedWorldPositions = new Vector3[jointCount];
        var exportedWorldRotations = new Quaternion[jointCount];
        var jointNodes = new Dictionary<string, object>[jointCount];
        var childrenByJoint = new List<int>[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            childrenByJoint[i] = [];
        }

        for (var i = 0; i < jointCount; i++)
        {
            var localPosition = worldPositions[i];
            var localRotation = worldRotations[i];
            var parent = parentByJoint[i];
            if (parent >= 0)
            {
                var inverseParentRotation = Quaternion.Inverse(worldRotations[parent]);
                localPosition = Vector3.Transform(worldPositions[i] - worldPositions[parent], inverseParentRotation);
                localRotation = Quaternion.Normalize(inverseParentRotation * worldRotations[i]);
                childrenByJoint[parent].Add(i);
            }

            if (commonLocalPositions[i].HasValue)
            {
                localPosition = commonLocalPositions[i]!.Value;
            }

            if (parent >= 0)
            {
                if (ignoresParentRotation[i])
                {
                    var inverseParentRotation = Quaternion.Inverse(exportedWorldRotations[parent]);
                    exportedWorldRotations[i] = localRotation;
                    exportedWorldPositions[i] = exportedWorldPositions[parent] + localPosition;
                    localPosition = Vector3.Transform(localPosition, inverseParentRotation);
                    localRotation = Quaternion.Normalize(inverseParentRotation * localRotation);
                }
                else
                {
                    exportedWorldRotations[i] = Quaternion.Normalize(exportedWorldRotations[parent] * localRotation);
                    exportedWorldPositions[i] = exportedWorldPositions[parent] + Vector3.Transform(localPosition, exportedWorldRotations[parent]);
                }
            }
            else
            {
                exportedWorldRotations[i] = localRotation;
                exportedWorldPositions[i] = localPosition;
            }

            exportedLocalPositions[i] = localPosition;
            exportedLocalRotations[i] = localRotation;

            var nodeIndex = nodes.Count;
            jointNodeIndices[i] = nodeIndex;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"bone_{i:0000}",
                ["translation"] = new[] { localPosition.X, localPosition.Y, localPosition.Z },
                ["rotation"] = new[] { localRotation.X, localRotation.Y, localRotation.Z, localRotation.W }
            };
            jointNodes[i] = node;
            nodes.Add(node);
        }

        for (var i = 0; i < jointCount; i++)
        {
            if (childrenByJoint[i].Count > 0)
            {
                jointNodes[i]["children"] = childrenByJoint[i].Select(child => jointNodeIndices[child]).ToArray();
            }
        }

        var skeletonRootNodeIndex = hierarchy.EnsureGroup(["Armature"]);
        for (var i = 0; i < jointCount; i++)
        {
            if (parentByJoint[i] < 0)
            {
                hierarchy.AddNodeToGroup(["Armature"], jointNodeIndices[i]);
            }
        }

        var skinIndex = skins.Count;
        var skin = new Dictionary<string, object>
        {
            ["name"] = "moby_skin",
            ["skeleton"] = skeletonRootNodeIndex,
            ["joints"] = jointNodeIndices
        };

        skins.Add(skin);

        var jointPaletteIndexByJoint = Enumerable.Range(0, jointCount).ToArray();
        return new GltfSkinContext
        {
            SkinIndex = skinIndex,
            JointPaletteIndexByJoint = jointPaletteIndexByJoint,
            JointNodeIndices = jointNodeIndices,
            ParentByJoint = parentByJoint,
            ChildrenByJoint = childrenByJoint,
            LocalPositions = exportedLocalPositions,
            LocalRotations = exportedLocalRotations,
            WorldPositions = exportedWorldPositions,
            WorldRotations = exportedWorldRotations,
            InverseBindMatrices = ResolveInverseBindMatrices(options.InverseBindMatrices, jointCount),
            JointNodes = jointNodes,
            Skin = skin
        };
    }

    private static void AccumulateJointInfluences(
        SkinInfluenceAccumulator accumulator,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<bool> validMask,
        IReadOnlyList<ushort[]> joints,
        IReadOnlyList<float[]> weights,
        IReadOnlyList<uint> indices)
    {
        var count = Math.Min(positions.Count, Math.Min(joints.Count, weights.Count));
        var usedVertices = new HashSet<uint>(indices);
        for (var i = 0; i < count; i++)
        {
            if (!validMask[i] || !usedVertices.Contains((uint)i))
            {
                continue;
            }

            for (var influence = 0; influence < 4; influence++)
            {
                var joint = joints[i][influence];
                var weight = weights[i][influence];
                if (joint >= accumulator.WeightSums.Length || weight <= 0f)
                {
                    continue;
                }

                accumulator.PositionSums[joint] += positions[i] * weight;
                accumulator.WeightSums[joint] += weight;
            }
        }
    }

    private static void RefineSkinFromInfluences(GltfSkinContext skinContext, SkinInfluenceAccumulator? accumulator)
    {
        if (accumulator is null)
        {
            return;
        }

        var refinedWorldPositions = new Vector3[skinContext.WorldPositions.Length];
        var hasRefinedPosition = new bool[skinContext.WorldPositions.Length];
        for (var i = 0; i < refinedWorldPositions.Length; i++)
        {
            refinedWorldPositions[i] = skinContext.WorldPositions[i];
            if (accumulator.WeightSums[i] > 0.001f)
            {
                refinedWorldPositions[i] = accumulator.PositionSums[i] / accumulator.WeightSums[i];
                hasRefinedPosition[i] = true;
            }
        }

        for (var i = refinedWorldPositions.Length - 1; i >= 0; i--)
        {
            if (hasRefinedPosition[i])
            {
                continue;
            }

            var childPositionSum = Vector3.Zero;
            var childPositionCount = 0;
            foreach (var child in skinContext.ChildrenByJoint[i])
            {
                if (!hasRefinedPosition[child])
                {
                    continue;
                }

                childPositionSum += refinedWorldPositions[child];
                childPositionCount++;
            }

            if (childPositionCount > 0)
            {
                refinedWorldPositions[i] = childPositionSum / childPositionCount;
                hasRefinedPosition[i] = true;
            }
        }

        for (var i = 0; i < refinedWorldPositions.Length; i++)
        {
            var parent = skinContext.ParentByJoint[i];
            var localPosition = refinedWorldPositions[i];
            if (parent >= 0)
            {
                localPosition = refinedWorldPositions[i] - refinedWorldPositions[parent];
            }

            skinContext.JointNodes[i]["translation"] = new[] { localPosition.X, localPosition.Y, localPosition.Z };
            skinContext.JointNodes[i]["rotation"] = new[] { 0f, 0f, 0f, 1f };

            skinContext.WorldPositions[i] = refinedWorldPositions[i];
            skinContext.WorldRotations[i] = Quaternion.Identity;
        }
    }

    private static void WriteInverseBindMatrices(
        GltfSkinContext skinContext,
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors)
    {
        Align(writer, 4);
        var inverseBindByteOffset = checked((int)writer.BaseStream.Position);
        for (var i = 0; i < skinContext.WorldPositions.Length; i++)
        {
            Matrix4x4 inverseBind;
            if (skinContext.InverseBindMatrices is not null)
            {
                inverseBind = skinContext.InverseBindMatrices[i];
            }
            else
            {
                var world = Matrix4x4.CreateFromQuaternion(skinContext.WorldRotations[i]) * Matrix4x4.CreateTranslation(skinContext.WorldPositions[i]);
                if (!Matrix4x4.Invert(world, out inverseBind))
                {
                    inverseBind = Matrix4x4.Identity;
                }
            }

            WriteMatrix4x4(writer, inverseBind);
        }

        var inverseBindBufferView = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = inverseBindByteOffset,
            byteLength = skinContext.WorldPositions.Length * 16 * sizeof(float)
        });

        var inverseBindAccessor = accessors.Count;
        accessors.Add(new
        {
            bufferView = inverseBindBufferView,
            byteOffset = 0,
            componentType = 5126,
            count = skinContext.WorldPositions.Length,
            type = "MAT4"
        });

        skinContext.Skin["inverseBindMatrices"] = inverseBindAccessor;
    }

    private static void AddAnimations(
        MobyGltfExportOptions options,
        GltfSkinContext skinContext,
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        List<object> animations,
        List<object> diagnostics)
    {
        foreach (var failure in options.AnimationFailures ?? [])
        {
            diagnostics.Add(new { SequenceIndex = failure.SourceIndex, Exported = false, failure.Reason });
        }

        if (options.Animations is not { Count: > 0 })
        {
            return;
        }

        foreach (var animation in options.Animations)
        {
            if (animation.Times.Length == 0)
            {
                diagnostics.Add(new { SequenceIndex = animation.SourceIndex, Exported = false, Reason = "animation has no keyframe times" });
                continue;
            }
            if (animation.Rotations.Values.Any(track => track.Length != animation.Times.Length)
                || animation.Scales.Values.Any(track => track.Length != animation.Times.Length)
                || animation.Translations.Values.Any(track => track.Length != animation.Times.Length))
            {
                throw new ArgumentException($"Animation {animation.SourceIndex} contains a track with the wrong keyframe count.");
            }

            var rotationTracks = animation.Rotations
                .Where(track => ShouldExportRotationTrack(track.Key, track.Value, skinContext))
                .ToArray();
            var scaleTracks = animation.Scales
                .Where(track => ShouldExportVectorTrack(track.Key, track.Value, Vector3.One, skinContext.JointNodeIndices.Length))
                .ToArray();
            var translationTracks = animation.Translations
                .Where(track => track.Key >= 0
                    && track.Key < skinContext.LocalPositions.Length
                    && !IsConstant(track.Value, skinContext.LocalPositions[track.Key]))
                .ToArray();
            if (rotationTracks.Length + scaleTracks.Length + translationTracks.Length == 0)
            {
                var fallbackRotation = animation.Rotations.FirstOrDefault();
                var fallbackScale = animation.Scales.FirstOrDefault();
                var fallbackTranslation = animation.Translations.FirstOrDefault();
                if (fallbackRotation.Value is not null)
                {
                    rotationTracks = [fallbackRotation];
                }
                else if (fallbackScale.Value is not null)
                {
                    scaleTracks = [fallbackScale];
                }
                else if (fallbackTranslation.Value is not null)
                {
                    translationTracks = [fallbackTranslation];
                }
                else
                {
                    diagnostics.Add(new { SequenceIndex = animation.SourceIndex, Exported = false, Reason = "animation has no channels" });
                    continue;
                }
            }

            var timeAccessor = WriteFloatAccessor(
                writer,
                bufferViews,
                accessors,
                animation.Times,
                "SCALAR",
                animation.Times.Length,
                [animation.Times[0]],
                [animation.Times[^1]]);
            var samplers = new List<object>();
            var channels = new List<object>();

            void AddChannel(int joint, string path, IReadOnlyList<float> values, string accessorType)
            {
                if (joint < 0 || joint >= skinContext.JointNodeIndices.Length)
                {
                    return;
                }
                var componentCount = accessorType == "VEC4" ? 4 : 3;
                if (values.Count != animation.Times.Length * componentCount)
                {
                    throw new ArgumentException(
                        $"Animation {animation.SourceIndex} {path} track {joint} does not match its keyframe count.");
                }

                var outputAccessor = WriteFloatAccessor(
                    writer,
                    bufferViews,
                    accessors,
                    values,
                    accessorType,
                    animation.Times.Length);
                var samplerIndex = samplers.Count;
                samplers.Add(new { input = timeAccessor, output = outputAccessor, interpolation = "LINEAR" });
                channels.Add(new
                {
                    sampler = samplerIndex,
                    target = new { node = skinContext.JointNodeIndices[joint], path }
                });
            }

            foreach (var (joint, track) in rotationTracks)
            {
                var values = new float[animation.Times.Length * 4];
                var previous = track[0];
                for (var frame = 0; frame < animation.Times.Length; frame++)
                {
                    var rotation = track[frame];
                    if (Quaternion.Dot(previous, rotation) < 0f)
                    {
                        rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
                    }

                    var offset = frame * 4;
                    values[offset] = rotation.X;
                    values[offset + 1] = rotation.Y;
                    values[offset + 2] = rotation.Z;
                    values[offset + 3] = rotation.W;
                    previous = rotation;
                }
                AddChannel(joint, "rotation", values, "VEC4");
            }

            foreach (var (joint, track) in scaleTracks)
            {
                AddChannel(joint, "scale", Flatten(track), "VEC3");
            }
            foreach (var (joint, track) in translationTracks)
            {
                AddChannel(joint, "translation", Flatten(track), "VEC3");
            }

            animations.Add(new
            {
                name = animation.Name,
                samplers,
                channels
            });
            diagnostics.Add(new
            {
                SequenceIndex = animation.SourceIndex,
                Exported = true,
                FrameCount = animation.Times.Length - 1,
                RotationChannels = rotationTracks.Length,
                ScaleChannels = scaleTracks.Length,
                TranslationChannels = translationTracks.Length
            });
        }
    }

    private static bool ShouldExportRotationTrack(
        int joint,
        IReadOnlyList<Quaternion> values,
        GltfSkinContext skinContext)
    {
        return joint >= 0
            && joint < skinContext.LocalRotations.Length
            && !IsConstant(values, skinContext.LocalRotations[joint]);
    }

    private static bool ShouldExportVectorTrack(
        int joint,
        IReadOnlyList<Vector3> values,
        Vector3 bindValue,
        int jointCount)
    {
        return joint >= 0 && joint < jointCount && !IsConstant(values, bindValue);
    }

    private static bool IsConstant(IReadOnlyList<Vector3> values, Vector3 bindValue)
    {
        return values.Count > 0
            && Vector3.DistanceSquared(values[0], bindValue) < 1e-10f
            && values.All(value => Vector3.DistanceSquared(value, values[0]) < 1e-10f);
    }

    private static bool IsConstant(IReadOnlyList<Quaternion> values, Quaternion bindValue)
    {
        return values.Count > 0
            && MathF.Abs(Quaternion.Dot(values[0], bindValue)) > 0.999999f
            && values.All(value => MathF.Abs(Quaternion.Dot(value, values[0])) > 0.999999f);
    }

    private static float[] Flatten(IReadOnlyList<Vector3> values)
    {
        var flattened = new float[values.Count * 3];
        for (var i = 0; i < values.Count; i++)
        {
            flattened[i * 3] = values[i].X;
            flattened[i * 3 + 1] = values[i].Y;
            flattened[i * 3 + 2] = values[i].Z;
        }
        return flattened;
    }

    private static int WriteFloatAccessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<float> values,
        string type,
        int count,
        float[]? min = null,
        float[]? max = null)
    {
        Align(writer, 4);
        var byteOffset = checked((int)writer.BaseStream.Position);
        foreach (var value in values)
        {
            writer.Write(value);
        }

        var bufferView = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset,
            byteLength = values.Count * sizeof(float)
        });

        var accessor = new Dictionary<string, object>
        {
            ["bufferView"] = bufferView,
            ["byteOffset"] = 0,
            ["componentType"] = 5126,
            ["count"] = count,
            ["type"] = type
        };
        if (min is not null)
        {
            accessor["min"] = min;
        }
        if (max is not null)
        {
            accessor["max"] = max;
        }

        var accessorIndex = accessors.Count;
        accessors.Add(accessor);
        return accessorIndex;
    }

    private static MobyGltfSkeletonParentMode ResolveSkeletonParentMode(
        byte[]? commonTransforms,
        int jointCount,
        MobyGltfSkeletonParentMode requestedMode)
    {
        if (requestedMode != MobyGltfSkeletonParentMode.Auto
            || commonTransforms is null
            || commonTransforms.Length < jointCount * 0x10)
        {
            return requestedMode == MobyGltfSkeletonParentMode.Auto
                ? MobyGltfSkeletonParentMode.SixBitShifted
                : requestedMode;
        }

        var sixBitScore = ScoreCommonTransformParentMode(commonTransforms, jointCount, MobyGltfSkeletonParentMode.SixBitShifted);
        var sevenBitScore = ScoreCommonTransformParentMode(commonTransforms, jointCount, MobyGltfSkeletonParentMode.SevenBitLow);
        return sevenBitScore > sixBitScore
            ? MobyGltfSkeletonParentMode.SevenBitLow
            : MobyGltfSkeletonParentMode.SixBitShifted;
    }

    private static IReadOnlyList<Matrix4x4>? ResolveInverseBindMatrices(
        IReadOnlyList<Matrix4x4>? inverseBindMatrices,
        int jointCount)
    {
        if (inverseBindMatrices is null)
        {
            return null;
        }

        if (inverseBindMatrices.Count < jointCount)
        {
            throw new ArgumentException(
                $"Expected at least {jointCount} inverse bind matrices, got {inverseBindMatrices.Count}.",
                nameof(MobyGltfExportOptions.InverseBindMatrices));
        }

        return inverseBindMatrices;
    }

    private static int ScoreCommonTransformParentMode(
        byte[] commonTransforms,
        int jointCount,
        MobyGltfSkeletonParentMode mode)
    {
        var score = 0;
        for (var i = 1; i < jointCount; i++)
        {
            var parent = DecodeCommonTransformParent(commonTransforms, i, mode);
            if (parent >= 0 && parent < i)
            {
                score++;
            }
        }

        return score;
    }

    private static int[] ReadCommonTransformParents(
        byte[]? commonTransforms,
        int jointCount,
        MobyGltfSkeletonParentMode mode)
    {
        var parents = Enumerable.Repeat(-1, jointCount).ToArray();
        if (commonTransforms is null || commonTransforms.Length < jointCount * 0x10)
        {
            return parents;
        }

        for (var i = 0; i < jointCount; i++)
        {
            var parent = DecodeCommonTransformParent(commonTransforms, i, mode);
            parents[i] = parent >= 0 && parent < i ? parent : -1;
        }

        return parents;
    }

    private static bool[] ReadCommonTransformParentRotationFlags(
        byte[]? commonTransforms,
        int jointCount,
        MobyGltfSkeletonParentMode mode)
    {
        var flags = new bool[jointCount];
        if (mode != MobyGltfSkeletonParentMode.SevenBitLow
            || commonTransforms is null
            || commonTransforms.Length < jointCount * 0x10)
        {
            return flags;
        }

        for (var i = 0; i < jointCount; i++)
        {
            var rawParent = commonTransforms[i * 0x10 + 0x0C];
            var parentIndex = rawParent & 0x7F;
            flags[i] = parentIndex != 0x7F && (rawParent & 0x80) != 0;
        }

        return flags;
    }

    private static int DecodeCommonTransformParent(
        byte[] commonTransforms,
        int jointIndex,
        MobyGltfSkeletonParentMode mode)
    {
        var offset = jointIndex * 0x10 + 0x0C;
        return mode switch
        {
            MobyGltfSkeletonParentMode.SevenBitLow => (commonTransforms[offset] & 0x7F) == 0x7F
                ? -1
                : commonTransforms[offset] & 0x7F,
            _ => BitConverter.ToUInt16(commonTransforms, offset) >> 6
        };
    }

    private static Vector3?[] ReadCommonTransformLocalPositions(byte[]? commonTransforms, int jointCount, float scale)
    {
        var positions = new Vector3?[jointCount];
        if (commonTransforms is null || commonTransforms.Length < jointCount * 0x10)
        {
            return positions;
        }

        for (var i = 0; i < jointCount; i++)
        {
            var offset = i * 0x10;
            var x = BitConverter.ToSingle(commonTransforms, offset) * scale;
            var sourceY = BitConverter.ToSingle(commonTransforms, offset + 0x04) * scale;
            var sourceZ = BitConverter.ToSingle(commonTransforms, offset + 0x08) * scale;
            positions[i] = GltfCoordinateBasis.FromPs2Position(x, sourceY, sourceZ);
        }

        return positions;
    }

    private static (Vector3 Position, Quaternion Rotation) DecodeBoneWorldTransform(MobyMatrix4 bone, float scale)
    {
        var basis = new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, -1f, 0f, 0f,
            0f, 0f, 0f, 1f);
        var basisInverse = Matrix4x4.Transpose(basis);
        var sourceRotation = new Matrix4x4(
            bone.Row1.X, bone.Row1.Y, bone.Row1.Z, 0f,
            bone.Row2.X, bone.Row2.Y, bone.Row2.Z, 0f,
            bone.Row3.X, bone.Row3.Y, bone.Row3.Z, 0f,
            0f, 0f, 0f, 1f);
        var mappedRotation = basis * sourceRotation * basisInverse;
        var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(mappedRotation));

        var sourceX = bone.Row4.X * scale;
        var sourceY = bone.Row4.Y * scale;
        var sourceZ = bone.Row4.Z * scale;
        return (new Vector3(sourceX, -sourceZ, -sourceY), rotation);
    }

    private static void WriteMatrix4x4(BinaryWriter writer, Matrix4x4 matrix)
    {
        writer.Write(matrix.M11);
        writer.Write(matrix.M12);
        writer.Write(matrix.M13);
        writer.Write(matrix.M14);
        writer.Write(matrix.M21);
        writer.Write(matrix.M22);
        writer.Write(matrix.M23);
        writer.Write(matrix.M24);
        writer.Write(matrix.M31);
        writer.Write(matrix.M32);
        writer.Write(matrix.M33);
        writer.Write(matrix.M34);
        writer.Write(matrix.M41);
        writer.Write(matrix.M42);
        writer.Write(matrix.M43);
        writer.Write(matrix.M44);
    }

    private sealed class GltfNodeHierarchy
    {
        private readonly List<object> nodes;
        private readonly List<int> sceneNodes;
        private readonly Dictionary<string, int> groupNodes = [];
        private readonly Dictionary<int, List<int>> childrenByNode = [];

        public GltfNodeHierarchy(List<object> nodes, List<int> sceneNodes)
        {
            this.nodes = nodes;
            this.sceneNodes = sceneNodes;
        }

        public void AddMeshNode(MobyMeshType meshType, int meshNodeIndex)
        {
            var path = GetGroupPath(meshType);
            AddNodeToGroup(path, meshNodeIndex);
        }

        public void AddNodeToGroup(IReadOnlyList<string> path, int childNodeIndex)
        {
            var parent = EnsureGroupPath(path);
            childrenByNode[parent].Add(childNodeIndex);
        }

        public int EnsureGroup(IReadOnlyList<string> path)
        {
            return EnsureGroupPath(path);
        }

        private int EnsureGroupPath(IReadOnlyList<string> path)
        {
            var currentKey = string.Empty;
            var parentIndex = -1;

            foreach (var part in path)
            {
                currentKey = currentKey.Length == 0 ? part : $"{currentKey}/{part}";
                if (!groupNodes.TryGetValue(currentKey, out var nodeIndex))
                {
                    nodeIndex = nodes.Count;
                    groupNodes.Add(currentKey, nodeIndex);
                    childrenByNode.Add(nodeIndex, []);
                    nodes.Add(new Dictionary<string, object>
                    {
                        ["name"] = part,
                        ["children"] = childrenByNode[nodeIndex]
                    });

                    if (parentIndex >= 0)
                    {
                        childrenByNode[parentIndex].Add(nodeIndex);
                    }
                    else
                    {
                        sceneNodes.Add(nodeIndex);
                    }
                }

                parentIndex = nodeIndex;
            }

            return parentIndex;
        }

        private static string[] GetGroupPath(MobyMeshType meshType)
        {
            return meshType switch
            {
                MobyMeshType.HighLod => ["mesh", "high_lod"],
                MobyMeshType.LowLod => ["mesh", "low_lod"],
                MobyMeshType.MeshType2 => ["mesh", "mesh_type_2"],
                MobyMeshType.Bangle => ["bangles", "high_lod"],
                MobyMeshType.Metal => ["metals"],
                _ => ["mesh", "unknown"]
            };
        }
    }

    private static bool TryExtractMesh(
        MobyMeshTableEntry entry,
        float scale,
        Vector3?[] rollingVertexCache,
        ushort[][] rollingJointCache,
        float[][] rollingWeightCache,
        SkinBlend?[] rollingBlendCache,
        int? initialTextureId,
        out List<Vector3> positions,
        out List<bool> validMask,
        out List<ushort[]> joints,
        out List<float[]> weights,
        out List<uint> indices,
        out List<VifTextureIndexGroup> topologyTextureGroups,
        out int? finalTextureId,
        out object diagnostic)
    {
        positions = [];
        validMask = [];
        joints = [];
        weights = [];
        indices = [];
        topologyTextureGroups = [];
        finalTextureId = initialTextureId;
        var duplicateCacheMisses = 0;

        if (!TryDecodeVertexTablePositions(
                entry,
                scale,
                rollingVertexCache,
                rollingJointCache,
                rollingWeightCache,
                rollingBlendCache,
                out positions,
                out validMask,
                out joints,
                out weights,
                out duplicateCacheMisses))
        {
            diagnostic = new { UsedDecodedVertexTable = false };
            return false;
        }

        var unpacks = MobyVifPacketReader
            .Read(Combine(entry.VifData, entry.VifTextureData))
            .Where(packet => packet.IsUnpack)
            .ToList();
        var indexUnpack = unpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_8" && packet.Payload.Length >= 8);
        var textureUnpacks = entry.VifTextureData is null
            ? []
            : MobyVifPacketReader.Read(entry.VifTextureData).Where(packet => packet.IsUnpack).ToList();

        var usedVifTopology = false;
        var rawTriangleCount = 0;
        var rejectedDegenerateTriangleCount = 0;
        var rejectedInvalidTriangleCount = 0;
        var rejectedDuplicateTriangleCount = 0;
        if (indexUnpack is not null)
        {
            var texturePayload = SelectTexturePayload(indexUnpack, unpacks, textureUnpacks);
            usedVifTopology = TryBuildTrianglesFromVifV48(
                indexUnpack.Payload,
                texturePayload,
                positions.Count,
                validMask,
                positions,
                indices,
                topologyTextureGroups,
                initialTextureId,
                out finalTextureId,
                out rawTriangleCount,
                out rejectedDegenerateTriangleCount,
                out rejectedInvalidTriangleCount,
                out rejectedDuplicateTriangleCount);
            if (!usedVifTopology && texturePayload is not null)
            {
                indices.Clear();
                topologyTextureGroups.Clear();
                usedVifTopology = TryBuildTrianglesFromVifV48(
                    indexUnpack.Payload,
                    null,
                    positions.Count,
                    validMask,
                    positions,
                    indices,
                    topologyTextureGroups,
                    initialTextureId,
                    out finalTextureId,
                    out rawTriangleCount,
                    out rejectedDegenerateTriangleCount,
                    out rejectedInvalidTriangleCount,
                    out rejectedDuplicateTriangleCount);
            }
        }

        diagnostic = new
        {
            UsedVifTopology = usedVifTopology,
            RawTriangleCount = rawTriangleCount,
            RejectedDegenerateTriangles = rejectedDegenerateTriangleCount,
            RejectedInvalidTriangles = rejectedInvalidTriangleCount,
            RejectedDuplicateTriangles = rejectedDuplicateTriangleCount,
            DuplicateVertexCacheMisses = duplicateCacheMisses,
            IndexUnpackFound = indexUnpack is not null
        };

        return positions.Count >= 3 && indices.Count >= 3;
    }

    private static object BuildMobyNodeExtras(MobyMeshTableEntry entry, int meshIndex)
    {
        return new Dictionary<string, object?>
        {
            ["RatchetPs2"] = new Dictionary<string, object?>
            {
                ["moby"] = new Dictionary<string, object?>
                {
                    ["kind"] = "mobyMeshNode",
                    ["version"] = 1,
                    ["meshIndex"] = meshIndex,
                    ["meshType"] = entry.MeshType.ToString(),
                    ["commonTransformJointIndex"] = entry.CommonTransformJointIndex
                }
            }
        };
    }

    private static object BuildMobyMeshExtras(
        MobyModel model,
        MobyMeshTableEntry entry,
        int meshIndex,
        float scale,
        int? explicitTextureId,
        int? effectiveTextureId,
        int? materialTextureId)
    {
        var combinedVifData = Combine(entry.VifData, entry.VifTextureData);
        var topologyPacket = MobyVifPacketReader
            .Read(combinedVifData)
            .FirstOrDefault(packet => packet.Kind == "UNPACK_V4_8" && packet.Payload.Length >= 4);

        return new Dictionary<string, object?>
        {
            ["RatchetPs2"] = new Dictionary<string, object?>
            {
                ["moby"] = new Dictionary<string, object?>
                {
                    ["kind"] = "mobyMesh",
                    ["version"] = 1,
                    ["meshIndex"] = meshIndex,
                    ["meshType"] = entry.MeshType.ToString(),
                    ["modelScale"] = model.Scale,
                    ["positionScale"] = scale,
                    ["coordinateBasis"] = GltfCoordinateBasis.Ps2XzyBasisDescription,
                    ["commonTransformJointIndex"] = entry.CommonTransformJointIndex,
                    ["primaryTextureId"] = explicitTextureId,
                    ["effectiveTextureId"] = effectiveTextureId,
                    ["materialTextureId"] = materialTextureId,
                    ["gifTextureIds"] = entry.GifTag?.TextureIds.Select(id => (int)id).ToArray(),
                    ["meshEntry"] = new Dictionary<string, object?>
                    {
                        ["vertexCount"] = entry.VertexCount,
                        ["vertexDataSizeQw"] = entry.VertexDataSize,
                        ["vifListSizeQw"] = entry.VifListSize,
                        ["vifListTextureSizeQwMinusOne"] = entry.VifListTextureSize,
                        ["vifDataLength"] = entry.VifData.Length,
                        ["vifTextureDataLength"] = entry.VifTextureData?.Length ?? 0,
                        ["gifTagMatched"] = entry.GifTag is not null
                    },
                    ["vertexLayout"] = BuildMobyVertexLayoutExtras(entry),
                    ["topologyPacket"] = topologyPacket is null ? null : BuildMobyTopologyPacketExtras(combinedVifData, topologyPacket, entry.VifData.Length)
                }
            }
        };
    }

    private static object BuildMobyTopologyPacketExtras(byte[] combinedVifData, MobyVifPacket topologyPacket, int vifDataSplitOffset)
    {
        var payloadOffset = topologyPacket.Offset + 4;
        var alignedPayloadSize = Math.Min(topologyPacket.AlignedPayloadSize, Math.Max(0, combinedVifData.Length - payloadOffset));
        var suffixOffset = Math.Min(combinedVifData.Length, payloadOffset + alignedPayloadSize);
        var payloadPaddingOffset = payloadOffset + topologyPacket.Payload.Length;
        var payloadPaddingSize = Math.Max(0, suffixOffset - payloadPaddingOffset);

        return new Dictionary<string, object?>
        {
            ["offset"] = topologyPacket.Offset,
            ["immediate"] = topologyPacket.Immediate,
            ["num"] = topologyPacket.Num,
            ["command"] = topologyPacket.Command,
            ["commandByte"] = topologyPacket.Command | (topologyPacket.Irq << 7),
            ["irq"] = topologyPacket.Irq,
            ["kind"] = topologyPacket.Kind,
            ["rawPayloadSize"] = topologyPacket.RawPayloadSize,
            ["alignedPayloadSize"] = topologyPacket.AlignedPayloadSize,
            ["payloadBase64"] = Convert.ToBase64String(topologyPacket.Payload),
            ["payloadBytes"] = topologyPacket.Payload.Select(value => (int)value).ToArray(),
            ["payloadPrefixBytes"] = topologyPacket.Payload.Take(4).Select(value => (int)value).ToArray(),
            ["payloadTokens"] = BuildMobyTopologyPayloadTokens(topologyPacket.Payload),
            ["alignedPayloadBase64"] = Convert.ToBase64String(combinedVifData.AsSpan(payloadOffset, alignedPayloadSize)),
            ["payloadPaddingBase64"] = Convert.ToBase64String(combinedVifData.AsSpan(payloadPaddingOffset, payloadPaddingSize)),
            ["beforePacketBase64"] = Convert.ToBase64String(combinedVifData.AsSpan(0, topologyPacket.Offset)),
            ["afterPacketBase64"] = Convert.ToBase64String(combinedVifData.AsSpan(suffixOffset)),
            ["vifDataSplitOffset"] = vifDataSplitOffset
        };
    }

    private static List<object> BuildMobyTopologyPayloadTokens(byte[] payload)
    {
        var tokens = new List<object>();
        for (var i = 4; i < payload.Length; i++)
        {
            var value = payload[i];
            var signedValue = unchecked((sbyte)value);
            var kind = value == 0
                ? "zero"
                : signedValue < 0
                    ? "negative_index"
                    : "index";
            tokens.Add(new Dictionary<string, object?>
            {
                ["kind"] = kind,
                ["negative"] = signedValue < 0,
                ["vertexIndex"] = value == 0 ? null : (value & 0x7F) - 1
            });
        }

        return tokens;
    }

    private static object BuildMobyVertexLayoutExtras(MobyMeshTableEntry entry)
    {
        var data = entry.VertexData;
        if (data.Length < 0x10)
        {
            return new Dictionary<string, object?>
            {
                ["supported"] = false
            };
        }

        var matrixTransferCount = BitConverter.ToUInt16(data, 0x00);
        var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
        var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
        var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
        var duplicateVertexCount = BitConverter.ToUInt16(data, 0x08);
        var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);

        var matrixTransfers = new List<object>();
        for (var i = 0; i < matrixTransferCount; i++)
        {
            var offset = 0x10 + i * 2;
            if (offset + 2 > data.Length)
            {
                break;
            }

            matrixTransfers.Add(new Dictionary<string, object?>
            {
                ["joint"] = unchecked((sbyte)data[offset]),
                ["vu0DestinationAddress"] = data[offset + 1]
            });
        }

        var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
        if (duplicateIndicesOffset % 4 != 0)
        {
            duplicateIndicesOffset += 2;
        }
        if (duplicateIndicesOffset % 8 != 0)
        {
            duplicateIndicesOffset += 4;
        }

        var duplicateIndices = new List<int>();
        for (var i = 0; i < duplicateVertexCount; i++)
        {
            var offset = duplicateIndicesOffset + i * 2;
            if (offset + 2 > data.Length)
            {
                break;
            }

            duplicateIndices.Add((BitConverter.ToUInt16(data, offset) >> 7) & 0x01FF);
        }

        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        var vertexDataSizeQw = data.Length / 0x10;
        var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
        var low9StorageValues = new List<int>();
        var rowPrefixBytes = new List<byte>();
        var epilogueBytes = Array.Empty<byte>();
        if (vertexTableOffset > 0 && vertexTableOffset % 0x10 == 0 && epilogueVertexCount >= 0)
        {
            var rowCount = inFileVertexCount + epilogueVertexCount;
            var epilogueOffset = vertexTableOffset + inFileVertexCount * 0x10;
            var epilogueLength = epilogueVertexCount * 0x10;
            if (epilogueLength > 0 && epilogueOffset + epilogueLength <= data.Length)
            {
                epilogueBytes = data.AsSpan(epilogueOffset, epilogueLength).ToArray();
            }

            for (var i = 0; i < rowCount; i++)
            {
                var offset = vertexTableOffset + i * 0x10;
                if (offset + 0x0A > data.Length)
                {
                    break;
                }

                low9StorageValues.Add(BitConverter.ToUInt16(data, offset) & 0x01FF);
                var lowHalf = (ushort)(BitConverter.ToUInt16(data, offset) & ~0x01FF);
                rowPrefixBytes.Add((byte)(lowHalf & 0xFF));
                rowPrefixBytes.Add((byte)(lowHalf >> 8));
                for (var j = 2; j < 0x0A; j++)
                {
                    rowPrefixBytes.Add(data[offset + j]);
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["supported"] = true,
            ["matrixTransferCount"] = matrixTransferCount,
            ["twoWayBlendVertexCount"] = twoWayBlendVertexCount,
            ["threeWayBlendVertexCount"] = threeWayBlendVertexCount,
            ["mainVertexCount"] = mainVertexCount,
            ["duplicateVertexCount"] = duplicateVertexCount,
            ["vertexTableOffset"] = vertexTableOffset,
            ["duplicateIndicesOffset"] = duplicateIndicesOffset,
            ["epilogueVertexCount"] = Math.Max(epilogueVertexCount, 0),
            ["headerBytesBase64"] = Convert.ToBase64String(data.AsSpan(0, 0x10)),
            ["epilogueBytesBase64"] = Convert.ToBase64String(epilogueBytes),
            ["matrixTransfers"] = matrixTransfers,
            ["duplicateIndices"] = duplicateIndices,
            ["low9StorageValues"] = low9StorageValues,
            ["rowPrefixBytesBase64"] = Convert.ToBase64String(rowPrefixBytes.ToArray())
        };
    }

    private static bool TryDecodeVertexTablePositions(
        MobyMeshTableEntry entry,
        float scale,
        Vector3?[] rollingVertexCache,
        ushort[][] rollingJointCache,
        float[][] rollingWeightCache,
        SkinBlend?[] rollingBlendCache,
        out List<Vector3> positions,
        out List<bool> validMask,
        out List<ushort[]> joints,
        out List<float[]> weights,
        out int duplicateCacheMisses)
    {
        positions = [];
        validMask = [];
        joints = [];
        weights = [];
        duplicateCacheMisses = 0;

        var data = entry.VertexData;
        if (data.Length < 0x20)
        {
            return false;
        }

        try
        {
            var matrixTransferCount = BitConverter.ToUInt16(data, 0x00);
            var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
            var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
            var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
            var duplicateVertexCount = BitConverter.ToUInt16(data, 0x08);
            var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
            var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;

            for (var i = 0; i < matrixTransferCount; i++)
            {
                var offset = 0x10 + i * 2;
                if (offset + 2 > data.Length)
                {
                    break;
                }

                var sprJointIndex = unchecked((sbyte)data[offset]);
                var vu0DestinationAddress = data[offset + 1];
                if (vu0DestinationAddress % 4 == 0)
                {
                    var slot = vu0DestinationAddress / 4;
                    if (slot >= 0 && slot < rollingBlendCache.Length)
                    {
                        rollingBlendCache[slot] = new SkinBlend(1, sprJointIndex, 0, 0, 255, 0, 0);
                    }
                }
            }

            if (vertexTableOffset <= 0 || vertexTableOffset % 0x10 != 0 || vertexTableOffset > data.Length || inFileVertexCount <= 0)
            {
                return false;
            }

            var vertexDataSizeQw = data.Length / 0x10;
            var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
            if (epilogueVertexCount < 0 || epilogueVertexCount > 64)
            {
                return false;
            }

            var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
            if (duplicateIndicesOffset % 4 != 0)
            {
                duplicateIndicesOffset += 2;
            }
            if (duplicateIndicesOffset % 8 != 0)
            {
                duplicateIndicesOffset += 4;
            }

            var duplicateVertexIndices = new List<int>(duplicateVertexCount);
            for (var i = 0; i < duplicateVertexCount; i++)
            {
                var offset = duplicateIndicesOffset + i * 2;
                if (offset + 2 > data.Length)
                {
                    break;
                }

                duplicateVertexIndices.Add((BitConverter.ToUInt16(data, offset) >> 7) & 0x01FF);
            }

            var vertices = new List<byte[]>(inFileVertexCount);
            var vertexOffset = vertexTableOffset;
            for (var i = 0; i < inFileVertexCount; i++)
            {
                if (vertexOffset + 0x10 > data.Length)
                {
                    return false;
                }

                vertices.Add(data[vertexOffset..(vertexOffset + 0x10)]);
                vertexOffset += 0x10;
            }

            for (var i = 7; i < vertices.Count; i++)
            {
                WriteLow9Bits(vertices[i - 7], ReadLowHalfword(vertices[i]));
            }

            var epilogueReadOffset = vertexTableOffset + inFileVertexCount * 0x10;
            epilogueReadOffset += Math.Max(7 - inFileVertexCount, 0) * 0x10;

            for (var i = Math.Max(7 - inFileVertexCount, 0); i < epilogueVertexCount; i++)
            {
                if (epilogueReadOffset + 0x10 > data.Length)
                {
                    break;
                }

                var destinationIndex = inFileVertexCount + i - 7;
                if (destinationIndex >= 0 && destinationIndex < vertices.Count)
                {
                    WriteLow9Bits(vertices[destinationIndex], BitConverter.ToUInt16(data, epilogueReadOffset));
                }

                epilogueReadOffset += 0x10;
            }

            var lastVertexOffset = epilogueReadOffset - 0x10;
            if (lastVertexOffset < 0 || lastVertexOffset + 0x10 > data.Length)
            {
                lastVertexOffset = Math.Max(vertexTableOffset, Math.Min(data.Length - 0x10, vertexTableOffset + (inFileVertexCount - 1) * 0x10));
            }

            for (var i = Math.Max(7 - inFileVertexCount - epilogueVertexCount, 0); i < 6; i++)
            {
                var destinationIndex = inFileVertexCount + epilogueVertexCount + i - 7;
                if (destinationIndex >= 0 && destinationIndex < vertices.Count)
                {
                    WriteLow9Bits(vertices[destinationIndex], BitConverter.ToUInt16(data, lastVertexOffset + 0x04 + i * 2));
                }
            }

            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                var vertexIndex = ReadLowHalfword(vertex) & 0x01FF;
                var position = DecodePosition(vertex, scale);
                var (jointRow, weightRow) = DecodeSkinRow(vertex, i, twoWayBlendVertexCount, threeWayBlendVertexCount, rollingBlendCache);
                positions.Add(position);
                validMask.Add(true);
                joints.Add(jointRow);
                weights.Add(weightRow);
                if (vertexIndex >= 0 && vertexIndex < rollingVertexCache.Length)
                {
                    rollingVertexCache[vertexIndex] = position;
                    rollingJointCache[vertexIndex] = jointRow;
                    rollingWeightCache[vertexIndex] = weightRow;
                }
            }

            foreach (var duplicateIndex in duplicateVertexIndices)
            {
                if (duplicateIndex >= 0 && duplicateIndex < rollingVertexCache.Length && rollingVertexCache[duplicateIndex].HasValue)
                {
                    positions.Add(rollingVertexCache[duplicateIndex]!.Value);
                    validMask.Add(true);
                    joints.Add(rollingJointCache[duplicateIndex] ?? DefaultJoints());
                    weights.Add(rollingWeightCache[duplicateIndex] ?? DefaultWeights());
                    continue;
                }

                duplicateCacheMisses++;
                positions.Add(positions.Count > 0 ? positions[^1] : Vector3.Zero);
                validMask.Add(false);
                joints.Add(DefaultJoints());
                weights.Add(DefaultWeights());
            }

            return validMask.Count(v => v) >= 3;
        }
        catch
        {
            return false;
        }
    }

    private static (ushort[] Joints, float[] Weights) DecodeSkinRow(
        byte[] vertex,
        int vertexNumber,
        ushort twoWayBlendVertexCount,
        ushort threeWayBlendVertexCount,
        SkinBlend?[] rollingBlendCache)
    {
        var bits9To15 = (sbyte)((ReadLowHalfword(vertex) >> 9) & 0x7F);
        SkinBlend blend;
        if (vertexNumber < twoWayBlendVertexCount)
        {
            var source1 = LoadSkinBlend(rollingBlendCache, vertex[2]);
            var source2 = LoadSkinBlend(rollingBlendCache, vertex[3]);
            StoreSkinBlend(rollingBlendCache, vertex[6], new SkinBlend(1, bits9To15, 0, 0, 255, 0, 0));
            blend = new SkinBlend(2, source1.Joint0, source2.Joint0, 0, vertex[4], vertex[5], 0);
            StoreSkinBlend(rollingBlendCache, vertex[7], blend);
        }
        else if (vertexNumber < twoWayBlendVertexCount + threeWayBlendVertexCount)
        {
            var source1 = LoadSkinBlend(rollingBlendCache, vertex[2]);
            var source2 = LoadSkinBlend(rollingBlendCache, vertex[3]);
            var source3 = LoadSkinBlend(rollingBlendCache, (byte)(bits9To15 * 2));
            blend = new SkinBlend(3, source1.Joint0, source2.Joint0, source3.Joint0, vertex[4], vertex[5], vertex[6]);
            StoreSkinBlend(rollingBlendCache, vertex[7], blend);
        }
        else
        {
            StoreSkinBlend(rollingBlendCache, vertex[3], new SkinBlend(1, bits9To15, 0, 0, 255, 0, 0));
            if (!TryLoadSkinBlend(rollingBlendCache, vertex[2], out blend))
            {
                blend = new SkinBlend(1, bits9To15, 0, 0, 255, 0, 0);
            }
        }

        return SkinBlendToRows(blend);
    }

    private static SkinBlend LoadSkinBlend(SkinBlend?[] rollingBlendCache, byte vu0Address)
        => TryLoadSkinBlend(rollingBlendCache, vu0Address, out var blend)
            ? blend
            : new SkinBlend(1, 0, 0, 0, 255, 0, 0);

    private static bool TryLoadSkinBlend(SkinBlend?[] rollingBlendCache, byte vu0Address, out SkinBlend blend)
    {
        if (vu0Address % 4 == 0)
        {
            var slot = vu0Address / 4;
            if (slot >= 0 && slot < rollingBlendCache.Length && rollingBlendCache[slot].HasValue)
            {
                blend = rollingBlendCache[slot]!.Value;
                return true;
            }
        }

        blend = default;
        return false;
    }

    private static void StoreSkinBlend(SkinBlend?[] rollingBlendCache, byte vu0Address, SkinBlend blend)
    {
        if (vu0Address % 4 != 0)
        {
            return;
        }

        var slot = vu0Address / 4;
        if (slot >= 0 && slot < rollingBlendCache.Length)
        {
            rollingBlendCache[slot] = blend;
        }
    }

    private static (ushort[] Joints, float[] Weights) SkinBlendToRows(SkinBlend blend)
    {
        var joints = new[]
        {
            ToJointIndex(blend.Joint0),
            ToJointIndex(blend.Joint1),
            ToJointIndex(blend.Joint2),
            (ushort)0
        };
        var weights = new[]
        {
            blend.Weight0 / 255f,
            blend.Count >= 2 ? blend.Weight1 / 255f : 0f,
            blend.Count >= 3 ? blend.Weight2 / 255f : 0f,
            0f
        };

        NormalizeWeights(weights);
        return (joints, weights);
    }

    private static ushort ToJointIndex(sbyte joint)
    {
        return joint < 0 ? (ushort)0 : (ushort)joint;
    }

    private static ushort[] DefaultJoints() => [0, 0, 0, 0];

    private static float[] DefaultWeights() => [1f, 0f, 0f, 0f];

    private static void NormalizeSkinRows(
        List<ushort[]> joints,
        List<float[]> weights,
        byte fallbackJoint,
        GltfSkinContext skinContext)
    {
        for (var i = 0; i < joints.Count; i++)
        {
            if (weights[i].Length < 4 || joints[i].Length < 4)
            {
                joints[i] = DefaultJoints();
                weights[i] = DefaultWeights();
            }

            var hasInfluence = false;
            for (var j = 0; j < 4; j++)
            {
                var sourceJoint = joints[i][j];
                if (sourceJoint >= skinContext.JointPaletteIndexByJoint.Length)
                {
                    joints[i][j] = 0;
                    weights[i][j] = 0f;
                    continue;
                }

                joints[i][j] = (ushort)skinContext.JointPaletteIndexByJoint[sourceJoint];
                hasInfluence |= weights[i][j] > 0f;
            }

            if (!hasInfluence)
            {
                var mappedFallback = fallbackJoint < skinContext.JointPaletteIndexByJoint.Length
                    ? skinContext.JointPaletteIndexByJoint[fallbackJoint]
                    : 0;
                joints[i] = [(ushort)mappedFallback, 0, 0, 0];
                weights[i] = DefaultWeights();
            }
            else
            {
                NormalizeWeights(weights[i]);
            }
        }
    }

    private static void NormalizeWeights(float[] weights)
    {
        var total = weights[0] + weights[1] + weights[2] + weights[3];
        if (total <= 0f)
        {
            weights[0] = 1f;
            weights[1] = 0f;
            weights[2] = 0f;
            weights[3] = 0f;
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            weights[i] /= total;
        }
    }

    private static bool TryBuildTrianglesFromVifV48(
        byte[] indexPayload,
        byte[]? texturePayload,
        int positionCount,
        IReadOnlyList<bool> validMask,
        IReadOnlyList<Vector3> positions,
        List<uint> indices,
        List<VifTextureIndexGroup> textureGroups,
        int? initialTextureId,
        out int? finalTextureId,
        out int rawTriangleCount,
        out int rejectedDegenerateTriangleCount,
        out int rejectedInvalidTriangleCount,
        out int rejectedDuplicateTriangleCount)
    {
        rawTriangleCount = 0;
        rejectedDegenerateTriangleCount = 0;
        rejectedInvalidTriangleCount = 0;
        rejectedDuplicateTriangleCount = 0;
        finalTextureId = initialTextureId;
        if (indexPayload.Length < 8 || positionCount < 3)
        {
            return false;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        int? textureId = initialTextureId;
        VifStrip? currentStrip = null;
        var strips = new List<VifStrip>();
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var idx = unchecked((sbyte)indexPayload[j]);

            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    if (currentStrip is null || currentStrip.Indices.Count < 3)
                    {
                        break;
                    }

                    currentStrip.Indices.RemoveAt(currentStrip.Indices.Count - 1);
                    currentStrip.Indices.RemoveAt(currentStrip.Indices.Count - 1);
                    currentStrip.Indices.RemoveAt(currentStrip.Indices.Count - 1);
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    textureId = ReadTexturePrimitiveTextureId(texturePayload, adGifIndex);
                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = new VifStrip(textureId);
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Indices.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Indices.Add(currentStrip.Indices[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = new VifStrip(textureId);
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Indices.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Indices.Count >= 3))
        {
            var flip = false;
            for (var k = 2; k < strip.Indices.Count; k++)
            {
                var a = strip.Indices[k - 2];
                var b = strip.Indices[k - 1];
                var c = strip.Indices[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;
                rawTriangleCount++;

                switch (TryAppendTriangle(indices, seenTriangles, i0, i1, i2, validMask, positions))
                {
                    case TriangleAppendResult.Added:
                        AddTriangleToTextureGroup(textureGroups, strip.TextureId, i0, i1, i2);
                        break;
                    case TriangleAppendResult.Degenerate:
                        rejectedDegenerateTriangleCount++;
                        break;
                    case TriangleAppendResult.Invalid:
                        rejectedInvalidTriangleCount++;
                        break;
                    case TriangleAppendResult.Duplicate:
                        rejectedDuplicateTriangleCount++;
                        break;
                }
            }
        }

        finalTextureId = textureId;
        return indices.Count >= 3;
    }

    private sealed class VifStrip(int? textureId)
    {
        public int? TextureId { get; } = textureId;
        public List<uint> Indices { get; } = [];
    }

    private static int? ReadTexturePrimitiveTextureId(byte[]? texturePayload, int texturePrimitiveIndex)
    {
        var offset = texturePrimitiveIndex * 0x40 + 0x20;
        if (texturePayload is null || offset + 4 > texturePayload.Length)
        {
            return null;
        }

        var raw = BitConverter.ToInt32(texturePayload, offset);
        return raw >= 0 ? raw : null;
    }

    private static void AddTriangleToTextureGroup(
        List<VifTextureIndexGroup> textureGroups,
        int? textureId,
        uint i0,
        uint i1,
        uint i2)
    {
        if (textureGroups.Count == 0 || textureGroups[^1].TextureId != textureId)
        {
            textureGroups.Add(new VifTextureIndexGroup(textureId, []));
        }

        textureGroups[^1].Indices.Add(i0);
        textureGroups[^1].Indices.Add(i1);
        textureGroups[^1].Indices.Add(i2);
    }

    private enum TriangleAppendResult
    {
        Added,
        Degenerate,
        Invalid,
        Duplicate
    }

    private static TriangleAppendResult TryAppendTriangle(
        List<uint> indices,
        HashSet<string> seenTriangles,
        uint i0,
        uint i1,
        uint i2,
        IReadOnlyList<bool> validMask,
        IReadOnlyList<Vector3> positions)
    {
        if (i0 == i1 || i1 == i2 || i0 == i2)
        {
            return TriangleAppendResult.Degenerate;
        }

        if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count)
        {
            return TriangleAppendResult.Invalid;
        }

        if (!validMask[(int)i0] || !validMask[(int)i1] || !validMask[(int)i2])
        {
            return TriangleAppendResult.Invalid;
        }

        var key = BuildGeometricTriangleKey(positions[(int)i0], positions[(int)i1], positions[(int)i2]);
        if (!seenTriangles.Add(key))
        {
            return TriangleAppendResult.Duplicate;
        }

        indices.Add(i0);
        indices.Add(i1);
        indices.Add(i2);
        return TriangleAppendResult.Added;
    }

    private static string BuildGeometricTriangleKey(Vector3 a, Vector3 b, Vector3 c)
    {
        var keys = new[]
        {
            BuildPositionKey(a),
            BuildPositionKey(b),
            BuildPositionKey(c)
        };
        Array.Sort(keys, StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static string BuildPositionKey(Vector3 position)
    {
        return $"{MathF.Round(position.X, 5):R},{MathF.Round(position.Y, 5):R},{MathF.Round(position.Z, 5):R}";
    }

    private static Vector3 DecodePosition(byte[] vertex, float scale)
    {
        var x = BitConverter.ToInt16(vertex, 0x0A) * scale;
        var sourceY = BitConverter.ToInt16(vertex, 0x0C) * scale;
        var sourceZ = BitConverter.ToInt16(vertex, 0x0E) * scale;
        return GltfCoordinateBasis.FromPs2Position(x, sourceY, sourceZ);
    }

    private static byte[] Combine(byte[] first, byte[]? second)
    {
        if (second is null || second.Length == 0)
        {
            return first;
        }

        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static byte[]? SelectTexturePayload(MobyVifPacket indexUnpack, List<MobyVifPacket> mainUnpacks, List<MobyVifPacket> textureListUnpacks)
    {
        if (indexUnpack.Payload.Length < 4)
        {
            return null;
        }

        var expectedTextureAddr = indexUnpack.Immediate + indexUnpack.Payload[1];
        return mainUnpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_32" && packet.Payload.Length >= 0x10 && packet.Immediate == expectedTextureAddr)?.Payload
            ?? textureListUnpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_32" && packet.Payload.Length >= 0x10 && packet.Immediate == expectedTextureAddr)?.Payload
            ?? textureListUnpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_32" && packet.Payload.Length >= 0x10)?.Payload
            ?? mainUnpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_32" && packet.Payload.Length >= 0x10)?.Payload;
    }

    private static bool TryExtractTexCoords(
        MobyMeshTableEntry entry,
        int vertexCount,
        out List<Vector2> texCoords)
    {
        texCoords = [];
        if (vertexCount <= 0 || entry.VifData.Length < 8)
        {
            return false;
        }

        var packet = MobyVifPacketReader
            .Read(entry.VifData)
            .FirstOrDefault(packet => packet.Kind == "UNPACK_V2_16" && packet.Payload.Length >= vertexCount * 4);
        if (packet is null)
        {
            return false;
        }

        const float uvScale = 4096f;
        texCoords = new List<Vector2>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            var offset = i * 4;
            texCoords.Add(new Vector2(
                BitConverter.ToInt16(packet.Payload, offset) / uvScale,
                BitConverter.ToInt16(packet.Payload, offset + 2) / uvScale));
        }

        return true;
    }

    private static int AddDebugUvMaterial(List<object> materials)
    {
        var index = materials.Count;
        materials.Add(new
        {
            name = "debug_uv_vertex_colors",
            doubleSided = true,
            pbrMetallicRoughness = new
            {
                baseColorFactor = new[] { 1f, 1f, 1f, 1f },
                metallicFactor = 0f,
                roughnessFactor = 1f
            }
        });
        return index;
    }

    private static bool TryGetExternalTextureMaterialIndex(
        int? textureId,
        IReadOnlyDictionary<int, string>? textureUris,
        IReadOnlyDictionary<int, TextureSize>? textureSizes,
        IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha,
        byte textureFullOpacityAlpha,
        List<object> images,
        List<object> textures,
        List<object> materials,
        Dictionary<int, int> materialIndexByTextureId,
        out int materialIndex)
    {
        materialIndex = 0;
        if (textureUris is null || !textureId.HasValue)
        {
            return false;
        }

        if (!textureUris.TryGetValue(textureId.Value, out var uri) || string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (materialIndexByTextureId.TryGetValue(textureId.Value, out materialIndex))
        {
            return true;
        }

        var imageIndex = images.Count;
        images.Add(new
        {
            name = $"tex_{textureId.Value:0000}",
            uri
        });

        var textureIndex = textures.Count;
        textures.Add(new
        {
            source = imageIndex
        });

        var alpha = textureAlpha is not null && textureAlpha.TryGetValue(textureId.Value, out var alphaInfo)
            ? alphaInfo
            : TextureAlphaInfo.Opaque;
        var size = textureSizes is not null && textureSizes.TryGetValue(textureId.Value, out var resolvedSize)
            ? resolvedSize
            : new TextureSize(0, 0);
        var pbr = new Dictionary<string, object>
        {
            ["baseColorTexture"] = new
            {
                index = textureIndex
            },
            ["baseColorFactor"] = new[] { 1f, 1f, 1f, 1f },
            ["metallicFactor"] = 0f,
            ["roughnessFactor"] = 1f
        };
        var material = new Dictionary<string, object?>
        {
            ["name"] = $"tex_{textureId.Value:0000}",
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = pbr,
            ["extras"] = new
            {
                MobyTextureId = textureId.Value,
                MobyTextureUri = uri,
                TextureWidth = size.Width,
                TextureHeight = size.Height,
                alpha.HasAlpha,
                AlphaMode = alpha.AlphaMode.ToString(),
                alpha.GltfAlphaMode,
                alpha.MinAlpha,
                alpha.MaxAlpha,
                alpha.UsesBinaryAlpha,
                TextureFullOpacityAlpha = textureFullOpacityAlpha
            }
        };
        if (alpha.GltfAlphaMode is { } alphaMode)
        {
            material["alphaMode"] = alphaMode;
            if (alpha.AlphaMode == TextureAlphaMode.Mask)
            {
                material["alphaCutoff"] = 0.5f;
            }
        }

        materialIndex = materials.Count;
        materials.Add(material);

        materialIndexByTextureId.Add(textureId.Value, materialIndex);
        return true;
    }

    private static int WriteIndexAccessor(
        BinaryWriter writer,
        List<object> bufferViews,
        List<object> accessors,
        IReadOnlyList<uint> indices)
    {
        Align(writer, 4);
        var indexByteOffset = checked((int)writer.BaseStream.Position);
        foreach (var index in indices)
        {
            writer.Write(index);
        }

        var indexBufferView = bufferViews.Count;
        bufferViews.Add(new
        {
            buffer = 0,
            byteOffset = indexByteOffset,
            byteLength = indices.Count * sizeof(uint),
            target = 34963
        });

        var indexAccessor = accessors.Count;
        accessors.Add(new
        {
            bufferView = indexBufferView,
            byteOffset = 0,
            componentType = 5125,
            count = indices.Count,
            type = "SCALAR",
            min = new[] { indices.Count == 0 ? 0L : indices.Min(i => (long)i) },
            max = new[] { indices.Count == 0 ? 0L : indices.Max(i => (long)i) }
        });

        return indexAccessor;
    }

    private static IReadOnlyList<PrimitiveIndexGroup> BuildPrimitiveIndexGroups(
        MobyMeshType meshType,
        MobyGltfLowLodTextureMode lowLodTextureMode,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2>? texCoords,
        IReadOnlyList<uint> indices,
        IReadOnlyList<VifTextureIndexGroup> topologyTextureGroups,
        int? materialTextureId,
        IReadOnlyList<TextureTriangle> highLodTextureTriangles,
        bool inferTextureIdsFromUvTiles)
    {
        if (topologyTextureGroups.Count > 0
            && topologyTextureGroups.Sum(group => group.Indices.Count) == indices.Count)
        {
            return MergeTopologyTextureGroups(topologyTextureGroups);
        }

        if (meshType != MobyMeshType.LowLod
            || lowLodTextureMode != MobyGltfLowLodTextureMode.HighLodNearestTriangle
            || highLodTextureTriangles.Count == 0)
        {
            return BuildUvTilePrimitiveIndexGroups(positions, texCoords, indices, materialTextureId, inferTextureIdsFromUvTiles);
        }

        var groups = new Dictionary<int, List<uint>>();
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count)
            {
                continue;
            }

            var centroid = (positions[(int)i0] + positions[(int)i1] + positions[(int)i2]) / 3f;
            var uvCentroid = texCoords is not null && i0 < texCoords.Count && i1 < texCoords.Count && i2 < texCoords.Count
                ? (texCoords[(int)i0] + texCoords[(int)i1] + texCoords[(int)i2]) / 3f
                : (Vector2?)null;
            var textureId = FindNearestTextureTriangle(centroid, uvCentroid, highLodTextureTriangles);
            if (!groups.TryGetValue(textureId, out var groupIndices))
            {
                groupIndices = [];
                groups.Add(textureId, groupIndices);
            }

            groupIndices.Add(i0);
            groupIndices.Add(i1);
            groupIndices.Add(i2);
        }

        return groups.Count == 0
            ? [new PrimitiveIndexGroup(materialTextureId, indices.ToList())]
            : groups
                .OrderBy(group => group.Key)
                .Select(group => new PrimitiveIndexGroup(group.Key, group.Value))
                .ToList();
    }

    private static IReadOnlyList<PrimitiveIndexGroup> MergeTopologyTextureGroups(IReadOnlyList<VifTextureIndexGroup> topologyTextureGroups)
    {
        var groups = new Dictionary<int, List<uint>>();
        var untextured = new List<uint>();
        foreach (var sourceGroup in topologyTextureGroups)
        {
            if (!sourceGroup.TextureId.HasValue)
            {
                untextured.AddRange(sourceGroup.Indices);
                continue;
            }

            if (!groups.TryGetValue(sourceGroup.TextureId.Value, out var groupIndices))
            {
                groupIndices = [];
                groups.Add(sourceGroup.TextureId.Value, groupIndices);
            }

            groupIndices.AddRange(sourceGroup.Indices);
        }

        var result = groups
            .OrderBy(group => group.Key)
            .Select(group => new PrimitiveIndexGroup(group.Key, group.Value))
            .ToList();
        if (untextured.Count > 0)
        {
            result.Insert(0, new PrimitiveIndexGroup(null, untextured));
        }

        return result.Count > 0 ? result : [new PrimitiveIndexGroup(null, [])];
    }

    private static int FindNearestTextureTriangle(Vector3 centroid, Vector2? uvCentroid, IReadOnlyList<TextureTriangle> triangles)
    {
        var bestDistance = float.MaxValue;
        var bestTextureId = triangles[0].TextureId;
        foreach (var triangle in triangles)
        {
            var distance = Vector3.DistanceSquared(centroid, triangle.Centroid);
            if (uvCentroid.HasValue && triangle.UvCentroid.HasValue)
            {
                var uvDistance = Vector2.DistanceSquared(WrapUv(uvCentroid.Value), WrapUv(triangle.UvCentroid.Value));
                distance += uvDistance * 0.25f;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTextureId = triangle.TextureId;
            }
        }

        return bestTextureId;
    }

    private static IReadOnlyList<PrimitiveIndexGroup> BuildUvTilePrimitiveIndexGroups(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2>? texCoords,
        IReadOnlyList<uint> indices,
        int? materialTextureId,
        bool inferTextureIdsFromUvTiles)
    {
        if (!inferTextureIdsFromUvTiles || !materialTextureId.HasValue || texCoords is null)
        {
            return [new PrimitiveIndexGroup(materialTextureId, indices.ToList())];
        }

        var groups = new Dictionary<int, List<uint>>();
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count
                || i0 >= texCoords.Count || i1 >= texCoords.Count || i2 >= texCoords.Count)
            {
                continue;
            }

            var uvCentroid = (texCoords[(int)i0] + texCoords[(int)i1] + texCoords[(int)i2]) / 3f;
            var textureId = ResolveTextureIdFromUvTile(uvCentroid, materialTextureId.Value);
            if (!groups.TryGetValue(textureId, out var groupIndices))
            {
                groupIndices = [];
                groups.Add(textureId, groupIndices);
            }

            groupIndices.Add(i0);
            groupIndices.Add(i1);
            groupIndices.Add(i2);
        }

        return groups.Count == 0
            ? [new PrimitiveIndexGroup(materialTextureId, indices.ToList())]
            : groups
                .OrderBy(group => group.Key)
                .Select(group => new PrimitiveIndexGroup(group.Key, group.Value))
                .ToList();
    }

    private static Vector2 WrapUv(Vector2 uv)
    {
        return new Vector2(uv.X - MathF.Floor(uv.X), uv.Y - MathF.Floor(uv.Y));
    }

    private static int ResolveTextureIdFromUvTile(Vector2 uv, int fallbackTextureId)
    {
        if (fallbackTextureId is not 0 and not 1)
        {
            return fallbackTextureId;
        }

        var uTile = (int)MathF.Floor(uv.X);
        var page = ((uTile % 2) + 2) % 2;
        return page == 0 ? 1 : 0;
    }

    private static void AddTextureTriangleSamples(
        List<TextureTriangle> samples,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2>? texCoords,
        IReadOnlyList<uint> indices,
        int textureId,
        bool inferTextureIdsFromUvTiles)
    {
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count)
            {
                continue;
            }

            var uvCentroid = texCoords is not null && i0 < texCoords.Count && i1 < texCoords.Count && i2 < texCoords.Count
                ? (texCoords[(int)i0] + texCoords[(int)i1] + texCoords[(int)i2]) / 3f
                : (Vector2?)null;
            samples.Add(new TextureTriangle(
                (positions[(int)i0] + positions[(int)i1] + positions[(int)i2]) / 3f,
                uvCentroid,
                uvCentroid.HasValue && inferTextureIdsFromUvTiles
                    ? ResolveTextureIdFromUvTile(uvCentroid.Value, textureId)
                    : textureId));
        }
    }

    private static int? ResolveLowLodTextureId(
        Bounds3 bounds,
        int? explicitTextureId,
        int? fallbackTextureId,
        IReadOnlyList<TextureBounds> highLodTextureBounds,
        MobyGltfLowLodTextureMode mode)
    {
        if (mode == MobyGltfLowLodTextureMode.Rolling)
        {
            return fallbackTextureId;
        }

        if (mode == MobyGltfLowLodTextureMode.ExplicitOnly)
        {
            return explicitTextureId;
        }

        if (highLodTextureBounds.Count == 0)
        {
            return fallbackTextureId;
        }

        if (mode == MobyGltfLowLodTextureMode.HighLodNearestCenter)
        {
            var center = bounds.Center;
            var bestDistance = float.MaxValue;
            int? nearestTextureId = null;
            foreach (var highLod in highLodTextureBounds)
            {
                var distance = Vector3.DistanceSquared(center, highLod.Bounds.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearestTextureId = highLod.TextureId;
                }
            }

            return nearestTextureId ?? fallbackTextureId;
        }

        var bestOverlap = 0f;
        int? bestTextureId = null;
        foreach (var highLod in highLodTextureBounds)
        {
            var overlap = bounds.OverlapVolume(highLod.Bounds);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                bestTextureId = highLod.TextureId;
            }
        }

        return bestOverlap > 1e-5f ? bestTextureId : fallbackTextureId;
    }

    private static bool TryGetPrimaryTextureId(MobyMeshTableEntry entry, out int textureId)
    {
        textureId = 0;
        var ids = entry.GifTag?.TextureIds;
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                if (id != 0xFF)
                {
                    textureId = id;
                    return true;
                }
            }
        }

        var activeTextureId = TryReadActiveTextureIdFromVifTextureData(entry.VifTextureData);
        if (activeTextureId.HasValue && activeTextureId.Value <= int.MaxValue)
        {
            textureId = (int)activeTextureId.Value;
            return true;
        }

        return false;
    }

    private static uint? TryReadActiveTextureIdFromVifTextureData(byte[]? vifTextureData)
    {
        if (vifTextureData is null)
        {
            return null;
        }

        foreach (var packet in MobyVifPacketReader.Read(vifTextureData))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x0C || packet.Payload.Length < 0x30)
            {
                continue;
            }

            return BitConverter.ToUInt32(packet.Payload, 0x20);
        }

        return null;
    }

    private static IEnumerable<Vector4> BuildDebugUvColors(IReadOnlyList<Vector2> texCoords)
    {
        foreach (var texCoord in texCoords)
        {
            var u = texCoord.X - MathF.Floor(texCoord.X);
            var v = texCoord.Y - MathF.Floor(texCoord.Y);
            var checker = (((int)MathF.Floor(u * 12f) + (int)MathF.Floor(v * 12f)) & 1) == 0;
            yield return checker
                ? new Vector4(1f, u, 0.08f, 1f)
                : new Vector4(0.05f, 0.25f, 1f - v, 1f);
        }
    }

    private static ushort ReadLowHalfword(byte[] block)
    {
        return BitConverter.ToUInt16(block, 0x00);
    }

    private static void WriteLow9Bits(byte[] block, ushort value)
    {
        var current = BitConverter.ToUInt16(block, 0x00);
        var next = (ushort)((current & ~0x01FF) | (value & 0x01FF));
        var bytes = BitConverter.GetBytes(next);
        block[0] = bytes[0];
        block[1] = bytes[1];
    }

    private static void Align(BinaryWriter writer, int alignment)
    {
        var remainder = writer.BaseStream.Position % alignment;
        if (remainder != 0)
        {
            writer.Write(new byte[alignment - remainder]);
        }
    }
}

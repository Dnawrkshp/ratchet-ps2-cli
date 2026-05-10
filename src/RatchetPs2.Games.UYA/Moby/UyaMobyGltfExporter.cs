using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RatchetPs2.Games.UYA.Moby;

public sealed record UyaMobyGltfExport(byte[] GltfBytes, byte[] BinBytes, byte[] DiagnosticsBytes);

public static class UyaMobyGltfExporter
{
    public static UyaMobyGltfExport Export(Stream input, string gltfFileName = "moby.gltf")
    {
        ArgumentNullException.ThrowIfNull(input);
        return Export(UyaMobyModelReader.Read(input), gltfFileName);
    }

    public static UyaMobyGltfExport Export(UyaMobyModel model, string gltfFileName = "moby.gltf")
    {
        ArgumentNullException.ThrowIfNull(model);

        var binFileName = Path.ChangeExtension(Path.GetFileName(gltfFileName), ".bin") ?? "moby.bin";
        var bufferViews = new List<object>();
        var accessors = new List<object>();
        var meshes = new List<object>();
        var nodes = new List<object>();
        var sceneNodes = new List<int>();
        var hierarchy = new GltfNodeHierarchy(nodes, sceneNodes);
        var diagnostics = new List<object>();
        var modelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
        var scale = modelScale / 1024f;
        var rollingVertexCache = new Vector3?[512];
        var rollingJointCache = new ushort[512][];
        var rollingWeightCache = new float[512][];
        var rollingBlendCache = new SkinBlend?[64];

        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var skins = new List<object>();
        var skinContext = TryBuildSkinContext(model, scale, nodes, hierarchy, skins);
        var skinAccumulator = skinContext is null ? null : new SkinInfluenceAccumulator(skinContext.JointPaletteIndexByJoint.Length);

        for (var meshIndex = 0; meshIndex < (model.MeshTable?.Entries.Count ?? 0); meshIndex++)
        {
            var entry = model.MeshTable!.Entries[meshIndex];
            if (!TryExtractMesh(
                    entry,
                    scale,
                    rollingVertexCache,
                    rollingJointCache,
                    rollingWeightCache,
                    rollingBlendCache,
                    out var positions,
                    out var validMask,
                    out var joints,
                    out var weights,
                    out var indices,
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

            if (skinAccumulator is not null && entry.MeshType != UyaMobyMeshType.LowLod)
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

            var gltfMeshIndex = meshes.Count;
            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (jointsAccessor.HasValue && weightsAccessor.HasValue)
            {
                attributes["JOINTS_0"] = jointsAccessor.Value;
                attributes["WEIGHTS_0"] = weightsAccessor.Value;
            }

            meshes.Add(new
            {
                name = $"mesh_{meshIndex:0000}_{entry.MeshType}",
                primitives = new[]
                {
                    new
                    {
                        attributes,
                        indices = indexAccessor,
                        mode = 4
                    }
                }
            });

            var nodeIndex = nodes.Count;
            var node = new Dictionary<string, object>
            {
                ["name"] = $"node_{meshIndex:0000}_{entry.MeshType}",
                ["mesh"] = gltfMeshIndex
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
        }

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = "RatchetPs2 UYA moby glTF exporter" },
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

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = "UYA moby geometry",
            Note = "Geometry is reconstructed from moby vertex tables and VIF UNPACK_V4_8 topology. Static skeleton skinning is exported when skeleton data is present; animation channels are intentionally omitted. Degenerate strip-control triangles are skipped; true duplicate faces are reported separately as a topology warning.",
            Meshes = diagnostics
        }, jsonOptions);

        return new UyaMobyGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

    private sealed class GltfSkinContext
    {
        public required int SkinIndex { get; init; }
        public required int[] JointPaletteIndexByJoint { get; init; }
        public required int[] JointNodeIndices { get; init; }
        public required int[] ParentByJoint { get; init; }
        public required List<int>[] ChildrenByJoint { get; init; }
        public required Vector3[] WorldPositions { get; init; }
        public required Quaternion[] WorldRotations { get; init; }
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

    private static GltfSkinContext? TryBuildSkinContext(
        UyaMobyModel model,
        float scale,
        List<object> nodes,
        GltfNodeHierarchy hierarchy,
        List<object> skins)
    {
        var bones = model.Skeleton?.Bones;
        var jointCount = Math.Min(model.JointCount, bones?.Count ?? 0);
        if (bones is null || jointCount <= 0)
        {
            return null;
        }

        var parentByJoint = ReadCommonTransformParents(model.CommonTransforms, jointCount);
        var commonLocalPositions = ReadCommonTransformLocalPositions(model.CommonTransforms, jointCount, scale);
        var worldPositions = new Vector3[jointCount];
        var worldRotations = new Quaternion[jointCount];
        for (var i = 0; i < jointCount; i++)
        {
            (worldPositions[i], worldRotations[i]) = DecodeBoneWorldTransform(bones[i], scale);
        }

        var jointNodeIndices = new int[jointCount];
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
                exportedWorldRotations[i] = Quaternion.Normalize(exportedWorldRotations[parent] * localRotation);
                exportedWorldPositions[i] = exportedWorldPositions[parent] + Vector3.Transform(localPosition, exportedWorldRotations[parent]);
            }
            else
            {
                exportedWorldRotations[i] = localRotation;
                exportedWorldPositions[i] = localPosition;
            }

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
            WorldPositions = exportedWorldPositions,
            WorldRotations = exportedWorldRotations,
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
            var world = Matrix4x4.CreateFromQuaternion(skinContext.WorldRotations[i]) * Matrix4x4.CreateTranslation(skinContext.WorldPositions[i]);
            if (!Matrix4x4.Invert(world, out var inverseBind))
            {
                inverseBind = Matrix4x4.Identity;
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

    private static int[] ReadCommonTransformParents(byte[]? commonTransforms, int jointCount)
    {
        var parents = Enumerable.Repeat(-1, jointCount).ToArray();
        if (commonTransforms is null || commonTransforms.Length < jointCount * 0x10)
        {
            return parents;
        }

        for (var i = 0; i < jointCount; i++)
        {
            var rawParent = BitConverter.ToUInt16(commonTransforms, i * 0x10 + 0x0C) >> 6;
            parents[i] = rawParent >= i ? -1 : rawParent;
        }

        return parents;
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
            positions[i] = new Vector3(x, sourceZ, -sourceY);
        }

        return positions;
    }

    private static (Vector3 Position, Quaternion Rotation) DecodeBoneWorldTransform(UyaMatrix4 bone, float scale)
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

        public void AddMeshNode(UyaMobyMeshType meshType, int meshNodeIndex)
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

        private static string[] GetGroupPath(UyaMobyMeshType meshType)
        {
            return meshType switch
            {
                UyaMobyMeshType.HighLod => ["mesh", "high_lod"],
                UyaMobyMeshType.LowLod => ["mesh", "low_lod"],
                UyaMobyMeshType.MeshType2 => ["mesh", "mesh_type_2"],
                UyaMobyMeshType.Bangle => ["bangles", "high_lod"],
                UyaMobyMeshType.Metal => ["metals"],
                _ => ["mesh", "unknown"]
            };
        }
    }

    private static bool TryExtractMesh(
        UyaMobyMeshTableEntry entry,
        float scale,
        Vector3?[] rollingVertexCache,
        ushort[][] rollingJointCache,
        float[][] rollingWeightCache,
        SkinBlend?[] rollingBlendCache,
        out List<Vector3> positions,
        out List<bool> validMask,
        out List<ushort[]> joints,
        out List<float[]> weights,
        out List<uint> indices,
        out object diagnostic)
    {
        positions = [];
        validMask = [];
        joints = [];
        weights = [];
        indices = [];
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

        var unpacks = UyaVifPacketReader
            .Read(Combine(entry.VifData, entry.VifTextureData))
            .Where(packet => packet.IsUnpack)
            .ToList();
        var indexUnpack = unpacks.FirstOrDefault(packet => packet.Kind == "UNPACK_V4_8" && packet.Payload.Length >= 8);
        var textureUnpacks = entry.VifTextureData is null
            ? []
            : UyaVifPacketReader.Read(entry.VifTextureData).Where(packet => packet.IsUnpack).ToList();

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
                out rawTriangleCount,
                out rejectedDegenerateTriangleCount,
                out rejectedInvalidTriangleCount,
                out rejectedDuplicateTriangleCount);
            if (!usedVifTopology && texturePayload is not null)
            {
                indices.Clear();
                usedVifTopology = TryBuildTrianglesFromVifV48(
                    indexUnpack.Payload,
                    null,
                    positions.Count,
                    validMask,
                    positions,
                    indices,
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

    private static bool TryDecodeVertexTablePositions(
        UyaMobyMeshTableEntry entry,
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
            blend = LoadSkinBlend(rollingBlendCache, vertex[2]);
            if (blend.Count == 1 && blend.Joint0 == 0 && bits9To15 != 0)
            {
                blend = new SkinBlend(1, bits9To15, 0, 0, 255, 0, 0);
            }
        }

        return SkinBlendToRows(blend);
    }

    private static SkinBlend LoadSkinBlend(SkinBlend?[] rollingBlendCache, byte vu0Address)
    {
        if (vu0Address % 4 == 0)
        {
            var slot = vu0Address / 4;
            if (slot >= 0 && slot < rollingBlendCache.Length && rollingBlendCache[slot].HasValue)
            {
                return rollingBlendCache[slot]!.Value;
            }
        }

        return new SkinBlend(1, 0, 0, 0, 255, 0, 0);
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
        out int rawTriangleCount,
        out int rejectedDegenerateTriangleCount,
        out int rejectedInvalidTriangleCount,
        out int rejectedDuplicateTriangleCount)
    {
        rawTriangleCount = 0;
        rejectedDegenerateTriangleCount = 0;
        rejectedInvalidTriangleCount = 0;
        rejectedDuplicateTriangleCount = 0;
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
        List<uint>? currentStrip = null;
        var strips = new List<List<uint>>();
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
                    if (currentStrip is null || currentStrip.Count < 3)
                    {
                        break;
                    }

                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = [];
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Add(currentStrip[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = [];
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Count >= 3))
        {
            var flip = false;
            for (var k = 2; k < strip.Count; k++)
            {
                var a = strip[k - 2];
                var b = strip[k - 1];
                var c = strip[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;
                rawTriangleCount++;

                switch (TryAppendTriangle(indices, seenTriangles, i0, i1, i2, validMask, positions))
                {
                    case TriangleAppendResult.Added:
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

        return indices.Count >= 3;
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
        return new Vector3(x, sourceZ, -sourceY);
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

    private static byte[]? SelectTexturePayload(UyaVifPacket indexUnpack, List<UyaVifPacket> mainUnpacks, List<UyaVifPacket> textureListUnpacks)
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

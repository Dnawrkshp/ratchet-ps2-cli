using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    internal static void ExportSkinTransferDebugGltfCore(
        Stream gltf,
        Func<string, Stream> openBuffer,
        Stream skinReferenceMoby,
        Stream outputGltf,
        string bufferUri,
        Stream outputBuffer,
        MobySkinTransferDebugCoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(gltf);
        ArgumentNullException.ThrowIfNull(openBuffer);
        ArgumentNullException.ThrowIfNull(skinReferenceMoby);
        ArgumentNullException.ThrowIfNull(outputGltf);
        ArgumentException.ThrowIfNullOrWhiteSpace(bufferUri);
        ArgumentNullException.ThrowIfNull(outputBuffer);

        options ??= new MobySkinTransferDebugCoreOptions();
        var importOptions = new MobyGltfImportOptions
        {
            AnimationFormat = options.AnimationFormat,
            CustomStatic = true,
            CustomStaticScale = options.CustomStaticScale,
            CustomStaticYawDegrees = options.CustomStaticYawDegrees,
            CustomStaticPitchDegrees = options.CustomStaticPitchDegrees,
            CustomStaticRollDegrees = options.CustomStaticRollDegrees,
            CustomStaticSplitConnectedComponents = options.SplitConnectedComponents,
            CustomStaticSplitSideAxis = options.SplitSideAxis,
            CustomStaticSplitSideDeadzoneRatio = options.SplitSideDeadzoneRatio,
            OutputModelScale = options.OutputModelScale,
            CustomStaticTransferReferenceSkinning = true,
            CustomStaticReferenceSkinningSampleCount = options.SampleCount,
            CustomStaticReferenceSkinningVerticalWindow = options.VerticalWindow,
            CustomStaticReferenceSkinningSameSide = options.SameSide,
            CustomStaticReferenceSkinningSideAxis = options.SideAxis,
            CustomStaticReferenceSkinningSideDeadzoneRatio = options.SideDeadzoneRatio,
            CustomStaticReferenceSkinningMaterialRegions = options.MaterialRegions,
            CustomStaticReferenceSkinningDisableAnatomicalFilters = options.DisableAnatomicalFilters,
            CustomStaticReferenceSkinningPreserveLowerBodyFilters = options.PreserveLowerBodyFilters,
            CustomStaticReferenceSkinningPreserveShoulderFilters = options.PreserveShoulderFilters,
            CustomStaticReferenceSkinningShoulderInwardBias = options.ShoulderInwardBias,
            CustomStaticReferenceSkinningTriangleCoherent = options.TriangleCoherent,
            CustomStaticReferenceSkinningSplitPrimarySeams = options.SplitPrimarySeams,
            CustomStaticReferenceSkinningRigidMeshCentroid = options.RigidMeshCentroid,
            CustomStaticReferenceSkinningRigidTriangleCentroid = options.RigidTriangleCentroid,
            CustomStaticReferenceSkinningSmoothPrimaryIterations = options.SmoothPrimaryIterations,
            CustomStaticReferenceSkinningDistancePower = options.DistancePower,
            CustomStaticReferenceSkinningYawDegrees = options.ReferenceYawDegrees,
            CustomStaticMaterialUvScales = options.MaterialUvScales,
            CustomStaticClampUvs = options.ClampUvs
        };

        using var document = JsonDocument.Parse(gltf);
        var root = document.RootElement;
        var buffers = GltfAccessorReader.ReadBuffers(root, openBuffer);
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var meshesElement = root.GetProperty("meshes");
        var materialNames = GltfPrimitiveReader.ReadMaterialNames(root);
        var nodeTransforms = GltfNodeTransforms.ReadMeshNodeTransforms(root);
        var sources = ReadCustomStaticSourceMeshes(
            meshesElement,
            accessors,
            bufferViews,
            buffers,
            materialNames,
            nodeTransforms,
            importOptions);

        if (options.SplitConnectedComponents)
        {
            sources = SplitCustomStaticSourcesByConnectedComponents(sources, 0);
        }

        if (!string.IsNullOrWhiteSpace(options.SplitSideAxis))
        {
            sources = SplitCustomStaticSourcesBySide(sources, options.SplitSideAxis, options.SplitSideDeadzoneRatio);
        }

        if (options.MaterialRegions)
        {
            sources = SplitCustomStaticSourcesByAnatomicalRegion(sources, splitGenericMaterials: false);
        }

        if (sources.Count == 0)
        {
            throw new InvalidDataException("Debug skin-transfer glTF has no triangle primitives.");
        }

        var meshes = sources.Select((source, index) => new ImportedMesh(
            index,
            MobyMeshType.HighLod,
            source.Positions.Select(position => position).ToList(),
            source.Indices.Select(index => index).ToList(),
            source.TexCoords?.Select(texCoord => texCoord).ToList(),
            source.Joints?.Select(row => row.ToArray()).ToList(),
            source.Weights?.Select(row => row.ToArray()).ToList(),
            metadata: null)
        {
            CustomStaticSourceMeshIndex = source.MeshIndex,
            CustomStaticSourcePrimitiveIndex = source.PrimitiveIndex,
            CustomStaticSourceMaterialIndex = source.MaterialIndex,
            CustomStaticSourceMaterialName = source.MaterialName,
            CustomStaticAppliedUvScale = source.AppliedUvScale
        }).ToList();

        if (Math.Abs(options.CustomStaticScale - 1f) > 0.000001f)
        {
            ScaleImportedMeshes(meshes, options.CustomStaticScale);
        }
        if (Math.Abs(options.CustomStaticYawDegrees) > 0.000001f)
        {
            RotateImportedMeshesYaw(meshes, options.CustomStaticYawDegrees);
        }
        if (Math.Abs(options.CustomStaticPitchDegrees) > 0.000001f)
        {
            RotateImportedMeshesPitch(meshes, options.CustomStaticPitchDegrees);
        }
        if (Math.Abs(options.CustomStaticRollDegrees) > 0.000001f)
        {
            RotateImportedMeshesRoll(meshes, options.CustomStaticRollDegrees);
        }

        var skinReference = MobyModelReader.Read(
            skinReferenceMoby,
            new MobyModelReadOptions { AnimationFormat = options.AnimationFormat });
        var outputScale = options.OutputModelScale
            ?? (TryEstimateOutputModelScaleFromGeometry(meshes, out var estimatedScale)
                ? estimatedScale
                : (IsUsableScale(skinReference.Scale) ? skinReference.Scale : 1f));
        var outputQuantizationScale = outputScale / 1024f;

        TransferReferenceSkinning(
            meshes,
            skinReference,
            outputQuantizationScale,
            options.SampleCount,
            options.VerticalWindow,
            options.SameSide,
            options.SideAxis,
            options.SideDeadzoneRatio,
            options.MaterialRegions,
            options.DisableAnatomicalFilters,
            options.PreserveLowerBodyFilters,
            options.PreserveShoulderFilters,
            options.ShoulderInwardBias,
            options.TriangleCoherent,
            options.SplitPrimarySeams,
            options.RigidMeshCentroid,
            options.RigidTriangleCentroid,
            options.SmoothPrimaryIterations,
            options.DistancePower,
            options.ReferenceYawDegrees,
            null,
            null,
            options.AnimationFormat);

        var fittedSamples = FitReferenceSkinSamplesToImportedMeshes(
            BuildReferenceSkinSamples(skinReference, outputQuantizationScale),
            meshes,
            options.ReferenceYawDegrees);
        fittedSamples = BiasReferenceShoulderSamplesInward(fittedSamples, options.ShoulderInwardBias, options.AnimationFormat);
        WriteSkinTransferDebugGltf(meshes, fittedSamples, outputGltf, bufferUri, outputBuffer);
    }

    private static void WriteSkinTransferDebugGltf(
        IReadOnlyList<ImportedMesh> meshes,
        IReadOnlyList<ReferenceSkinSample> fittedSamples,
        Stream outputGltf,
        string bufferUri,
        Stream outputBuffer)
    {
        var bin = new MemoryStream();
        var bufferViews = new List<Dictionary<string, object>>();
        var accessors = new List<Dictionary<string, object>>();
        var gltfMeshes = new List<Dictionary<string, object>>();
        var nodes = new List<Dictionary<string, object>>();
        var sceneNodes = new List<int>();

        foreach (var mesh in meshes)
        {
            var positionAccessor = AddVec3Accessor(bin, bufferViews, accessors, mesh.Positions);
            var colors = Enumerable.Range(0, mesh.Positions.Count)
                .Select(index => JointColor(GetDebugPrimaryJoint(mesh, index)))
                .ToList();
            var colorAccessor = AddVec4Accessor(bin, bufferViews, accessors, colors);
            var indexAccessor = AddIndexAccessor(bin, bufferViews, accessors, mesh.Indices);
            var meshIndex = gltfMeshes.Count;
            gltfMeshes.Add(new Dictionary<string, object>
            {
                ["name"] = $"target_{mesh.TemplateMeshIndex:0000}_{mesh.CustomStaticSourceMaterialName ?? "material"}",
                ["primitives"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["attributes"] = new Dictionary<string, object>
                        {
                            ["POSITION"] = positionAccessor,
                            ["COLOR_0"] = colorAccessor
                        },
                        ["indices"] = indexAccessor,
                        ["mode"] = 4,
                        ["material"] = 0
                    }
                },
                ["extras"] = new Dictionary<string, object?>
                {
                    ["sourceMesh"] = mesh.CustomStaticSourceMeshIndex,
                    ["sourcePrimitive"] = mesh.CustomStaticSourcePrimitiveIndex,
                    ["sourceMaterial"] = mesh.CustomStaticSourceMaterialName
                }
            });
            nodes.Add(new Dictionary<string, object>
            {
                ["name"] = $"target_{mesh.TemplateMeshIndex:0000}",
                ["mesh"] = meshIndex
            });
            sceneNodes.Add(nodes.Count - 1);
        }

        if (fittedSamples.Count > 0)
        {
            BuildReferenceSampleMarkers(meshes, fittedSamples, out var samplePositions, out var sampleColors, out var sampleIndices);
            var samplePositionAccessor = AddVec3Accessor(bin, bufferViews, accessors, samplePositions);
            var sampleColorAccessor = AddVec4Accessor(bin, bufferViews, accessors, sampleColors);
            var sampleIndexAccessor = AddIndexAccessor(bin, bufferViews, accessors, sampleIndices);
            var meshIndex = gltfMeshes.Count;
            gltfMeshes.Add(new Dictionary<string, object>
            {
                ["name"] = "reference_samples",
                ["primitives"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["attributes"] = new Dictionary<string, object>
                        {
                            ["POSITION"] = samplePositionAccessor,
                            ["COLOR_0"] = sampleColorAccessor
                        },
                        ["indices"] = sampleIndexAccessor,
                        ["mode"] = 4,
                        ["material"] = 0
                    }
                }
            });
            nodes.Add(new Dictionary<string, object>
            {
                ["name"] = "reference_samples",
                ["mesh"] = meshIndex
            });
            sceneNodes.Add(nodes.Count - 1);
        }

        var bufferBytes = bin.ToArray();
        outputBuffer.Write(bufferBytes, 0, bufferBytes.Length);
        var document = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "ratchet-ps2 skin-transfer-debug" },
            ["buffers"] = new object[] { new Dictionary<string, object> { ["uri"] = bufferUri, ["byteLength"] = bufferBytes.Length } },
            ["bufferViews"] = bufferViews,
            ["accessors"] = accessors,
            ["materials"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["name"] = "joint_debug_vertex_colors",
                    ["pbrMetallicRoughness"] = new Dictionary<string, object>
                    {
                        ["baseColorFactor"] = new[] { 1f, 1f, 1f, 1f },
                        ["metallicFactor"] = 0f,
                        ["roughnessFactor"] = 1f
                    },
                    ["doubleSided"] = true
                }
            },
            ["meshes"] = gltfMeshes,
            ["nodes"] = nodes,
            ["scenes"] = new object[] { new Dictionary<string, object> { ["nodes"] = sceneNodes } },
            ["scene"] = 0
        };

        JsonSerializer.Serialize(
            outputGltf,
            document,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void BuildReferenceSampleMarkers(
        IReadOnlyList<ImportedMesh> meshes,
        IReadOnlyList<ReferenceSkinSample> fittedSamples,
        out List<Vector3> positions,
        out List<Vector4> colors,
        out List<uint> indices)
    {
        var visiblePositions = meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions)
            .ToList();
        var size = 0.025f;
        if (visiblePositions.Count > 0)
        {
            var span = Bounds3.From(visiblePositions).Size;
            size = MathF.Max(0.01f, MathF.Max(span.X, MathF.Max(span.Y, span.Z)) * 0.006f);
        }

        positions = new List<Vector3>(fittedSamples.Count * 6);
        colors = new List<Vector4>(fittedSamples.Count * 6);
        indices = new List<uint>(fittedSamples.Count * 24);
        foreach (var sample in fittedSamples)
        {
            var baseIndex = checked((uint)positions.Count);
            var color = JointColor(sample.PrimaryJoint);
            var p = sample.Position;
            positions.Add(p + new Vector3(size, 0f, 0f));
            positions.Add(p - new Vector3(size, 0f, 0f));
            positions.Add(p + new Vector3(0f, size, 0f));
            positions.Add(p - new Vector3(0f, size, 0f));
            positions.Add(p + new Vector3(0f, 0f, size));
            positions.Add(p - new Vector3(0f, 0f, size));
            for (var i = 0; i < 6; i++)
            {
                colors.Add(color);
            }

            AddTriangle(indices, baseIndex, 0, 2, 4);
            AddTriangle(indices, baseIndex, 2, 1, 4);
            AddTriangle(indices, baseIndex, 1, 3, 4);
            AddTriangle(indices, baseIndex, 3, 0, 4);
            AddTriangle(indices, baseIndex, 2, 0, 5);
            AddTriangle(indices, baseIndex, 1, 2, 5);
            AddTriangle(indices, baseIndex, 3, 1, 5);
            AddTriangle(indices, baseIndex, 0, 3, 5);
        }
    }

    private static void AddTriangle(List<uint> indices, uint baseIndex, uint a, uint b, uint c)
    {
        indices.Add(baseIndex + a);
        indices.Add(baseIndex + b);
        indices.Add(baseIndex + c);
    }

    private static int GetDebugPrimaryJoint(ImportedMesh mesh, int vertexIndex)
    {
        if (mesh.Joints is not null
            && mesh.Weights is not null
            && vertexIndex >= 0
            && vertexIndex < mesh.Joints.Count
            && vertexIndex < mesh.Weights.Count)
        {
            return GetPrimaryJoint(mesh.Joints[vertexIndex], mesh.Weights[vertexIndex]);
        }

        return vertexIndex >= 0 && vertexIndex < mesh.SkinTransferDiagnostics.Count
            ? mesh.SkinTransferDiagnostics[vertexIndex].PrimaryJoint
            : -1;
    }

    private static int AddVec3Accessor(
        MemoryStream bin,
        List<Dictionary<string, object>> bufferViews,
        List<Dictionary<string, object>> accessors,
        IReadOnlyList<Vector3> values)
    {
        var offset = Align(bin, 4);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.X);
            bin.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.Y);
            bin.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.Z);
            bin.Write(bytes);
        }

        return AddAccessor(bufferViews, accessors, offset, values.Count * 12, values.Count, 5126, "VEC3");
    }

    private static int AddVec4Accessor(
        MemoryStream bin,
        List<Dictionary<string, object>> bufferViews,
        List<Dictionary<string, object>> accessors,
        IReadOnlyList<Vector4> values)
    {
        var offset = Align(bin, 4);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.X);
            bin.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.Y);
            bin.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.Z);
            bin.Write(bytes);
            BinaryPrimitives.WriteSingleLittleEndian(bytes, value.W);
            bin.Write(bytes);
        }

        return AddAccessor(bufferViews, accessors, offset, values.Count * 16, values.Count, 5126, "VEC4");
    }

    private static int AddIndexAccessor(
        MemoryStream bin,
        List<Dictionary<string, object>> bufferViews,
        List<Dictionary<string, object>> accessors,
        IReadOnlyList<uint> values)
    {
        var offset = Align(bin, 4);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            bin.Write(bytes);
        }

        return AddAccessor(bufferViews, accessors, offset, values.Count * 4, values.Count, 5125, "SCALAR");
    }

    private static int AddAccessor(
        List<Dictionary<string, object>> bufferViews,
        List<Dictionary<string, object>> accessors,
        int byteOffset,
        int byteLength,
        int count,
        int componentType,
        string type)
    {
        var bufferViewIndex = bufferViews.Count;
        bufferViews.Add(new Dictionary<string, object>
        {
            ["buffer"] = 0,
            ["byteOffset"] = byteOffset,
            ["byteLength"] = byteLength
        });
        var accessorIndex = accessors.Count;
        accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = bufferViewIndex,
            ["byteOffset"] = 0,
            ["componentType"] = componentType,
            ["count"] = count,
            ["type"] = type
        });
        return accessorIndex;
    }

    private static int Align(MemoryStream stream, int alignment)
    {
        var offset = checked((int)stream.Position);
        var padding = (alignment - offset % alignment) % alignment;
        for (var i = 0; i < padding; i++)
        {
            stream.WriteByte(0);
        }

        return checked((int)stream.Position);
    }

    private static Vector4 JointColor(int joint)
    {
        if (joint < 0)
        {
            return new Vector4(0.1f, 0.1f, 0.1f, 1f);
        }

        var hue = (joint * 0.61803398875f) % 1f;
        var rgb = HsvToRgb(hue, 0.72f, 0.95f);
        return new Vector4(rgb.X, rgb.Y, rgb.Z, 1f);
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 1f / 6f => (c, x, 0f),
            < 2f / 6f => (x, c, 0f),
            < 3f / 6f => (0f, c, x),
            < 4f / 6f => (0f, x, c),
            < 5f / 6f => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return new Vector3(r + m, g + m, b + m);
    }
}

internal sealed class MobySkinTransferDebugCoreOptions
{
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
    public float CustomStaticScale { get; init; } = 1f;
    public float CustomStaticYawDegrees { get; init; }
    public float CustomStaticPitchDegrees { get; init; }
    public float CustomStaticRollDegrees { get; init; }
    public bool SplitConnectedComponents { get; init; }
    public string? SplitSideAxis { get; init; }
    public float SplitSideDeadzoneRatio { get; init; } = 0.02f;
    public float? OutputModelScale { get; init; }
    public int SampleCount { get; init; } = 1;
    public float? VerticalWindow { get; init; }
    public bool SameSide { get; init; }
    public string SideAxis { get; init; } = "x";
    public float SideDeadzoneRatio { get; init; } = 0.03f;
    public bool MaterialRegions { get; init; }
    public bool DisableAnatomicalFilters { get; init; }
    public bool PreserveLowerBodyFilters { get; init; }
    public bool PreserveShoulderFilters { get; init; }
    public float ShoulderInwardBias { get; init; }
    public bool TriangleCoherent { get; init; }
    public bool SplitPrimarySeams { get; init; }
    public bool RigidMeshCentroid { get; init; }
    public bool RigidTriangleCentroid { get; init; }
    public int SmoothPrimaryIterations { get; init; }
    public float DistancePower { get; init; } = 1f;
    public float ReferenceYawDegrees { get; init; }
    public IReadOnlyDictionary<string, Vector2>? MaterialUvScales { get; init; }
    public bool ClampUvs { get; init; }
}

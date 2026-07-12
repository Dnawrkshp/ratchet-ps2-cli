using System.Diagnostics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public sealed record TfragGltfExport(byte[] GltfBytes, byte[] BinBytes, byte[] DiagnosticsBytes);

public sealed class TfragGltfExportOptions
{
    public string? BufferFileName { get; init; }

    public string GameLabel { get; init; } = "Tfrag";

    public float WorldPositionScale { get; init; } = 1f / 1024f;

    public float LocalPositionScale { get; init; } = 1f / 1024f;

    public int TopologyPayloadPrefixBytes { get; init; } = 4;

    public float? MaxTriangleEdgeLength { get; init; } = 40f;

    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }

    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }

    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }

    public bool IncludeDiagnostics { get; init; } = true;

    public bool Minify { get; init; }

    public GltfExportMetadataMode MetadataMode { get; init; } = GltfExportMetadataMode.Full;

    public int? LodIndex { get; init; }

    public Action<string, string, double, string?>? TimingSink { get; init; }
}

public static partial class TfragGltfExporter
{
    private const string UnlitExtensionName = "KHR_materials_unlit";

    public static TfragGltfExport Export(
        Stream input,
        string gltfFileName = "tfrag.gltf",
        TfragGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        options ??= new TfragGltfExportOptions();
        var readStart = Stopwatch.GetTimestamp();
        var terrain = TfragTerrainReader.Read(input);
        AddTiming(options, "tfrag.read", "Terrain WAD parse", readStart);
        return Export(terrain, gltfFileName, options);
    }

    public static TfragGltfExport Export(
        TfragTerrain terrain,
        string gltfFileName = "tfrag.gltf",
        TfragGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        options ??= new TfragGltfExportOptions();
        ValidateOptions(options);

        var decodeStart = Stopwatch.GetTimestamp();
        var decoded = BuildDecodedTerrain(terrain, options);
        AddTiming(
            options,
            "tfrag.decode",
            "Terrain decode",
            decodeStart,
            $"{decoded.Meshes.Count} meshes");
        if (decoded.Meshes.Count == 0)
        {
            throw new InvalidDataException("Tfrag terrain has no decoded chunk LOD geometry to export.");
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        var materialStart = Stopwatch.GetTimestamp();
        var materialBuild = BuildMaterials(decoded.MaterialKeys, options);
        AddTiming(
            options,
            "tfrag.materials",
            "Terrain material build",
            materialStart,
            $"{materialBuild.Materials.Count} materials");

        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var gltfBufferWriter = new GltfBufferWriter(writer);
        var meshes = new List<object>();
        var nodes = new List<Dictionary<string, object?>>();
        var rootChildren = new List<int>();
        var lodChildren = new Dictionary<int, List<int>>();

        nodes.Add(new Dictionary<string, object?>
        {
            ["name"] = "tfrag",
            ["children"] = rootChildren
        });
        if (ShouldWriteFullMetadata(options))
        {
            nodes[0]["extras"] = BuildRootNodeExtras(terrain, options);
        }

        foreach (var lodIndex in GetExportLodIndices(options))
        {
            var children = new List<int>();
            lodChildren[lodIndex] = children;
            rootChildren.Add(nodes.Count);
            var node = new Dictionary<string, object?>
            {
                ["name"] = $"lod_{lodIndex}",
                ["children"] = children
            };
            if (ShouldWriteFullMetadata(options))
            {
                node["extras"] = BuildLodGroupExtras(decoded, lodIndex);
            }

            nodes.Add(node);
        }

        var geometryWriteStart = Stopwatch.GetTimestamp();
        var exportMeshes = BuildGltfMeshes(decoded.Meshes, options);
        foreach (var mesh in exportMeshes)
        {
            var meshIndex = meshes.Count;
            var primitiveDefinitions = new List<Dictionary<string, object>>();
            foreach (var group in mesh.Groups)
            {
                var positionAccessor = gltfBufferWriter.WriteVector3Accessor(
                    group.Positions,
                    target: GltfBufferWriter.ArrayBufferTarget,
                    includeMinMax: true);
                var normalAccessor = gltfBufferWriter.WriteVector3Accessor(
                    group.Normals,
                    target: GltfBufferWriter.ArrayBufferTarget);
                var texCoordAccessor = gltfBufferWriter.WriteVector2Accessor(
                    group.TexCoords,
                    target: GltfBufferWriter.ArrayBufferTarget);
                var colorAccessor = group.Colors.Count == group.Positions.Count
                    ? gltfBufferWriter.WriteNormalizedByteVector4Accessor(
                        group.Colors,
                        target: GltfBufferWriter.ArrayBufferTarget)
                    : (int?)null;
                var lightSelectorAccessor = group.LightSelectors.Count == group.Positions.Count
                    ? gltfBufferWriter.WriteScalarFloatAccessor(
                        group.LightSelectors,
                        target: GltfBufferWriter.ArrayBufferTarget,
                        includeMinMax: true)
                    : (int?)null;
                var lightBaseColorAccessor = group.LightBaseColors.Count == group.Positions.Count
                    ? gltfBufferWriter.WriteNormalizedByteVector4Accessor(
                        group.LightBaseColors,
                        target: GltfBufferWriter.ArrayBufferTarget)
                    : (int?)null;
                var lightNormalAccessor = group.LightNormals.Count == group.Positions.Count
                    ? gltfBufferWriter.WriteVector3Accessor(
                        group.LightNormals,
                        target: GltfBufferWriter.ArrayBufferTarget)
                    : (int?)null;
                var lightPostScaleAccessor = group.LightPostScales.Count == group.Positions.Count
                    ? gltfBufferWriter.WriteScalarFloatAccessor(
                        group.LightPostScales,
                        target: GltfBufferWriter.ArrayBufferTarget,
                        includeMinMax: true)
                    : (int?)null;
                var indexAccessor = gltfBufferWriter.WriteUInt32IndexAccessor(group.Indices);
                var attributes = new Dictionary<string, int>
                {
                    ["POSITION"] = positionAccessor,
                    ["NORMAL"] = normalAccessor,
                    ["TEXCOORD_0"] = texCoordAccessor
                };
                if (colorAccessor.HasValue)
                {
                    attributes["COLOR_0"] = colorAccessor.Value;
                }
                if (lightSelectorAccessor.HasValue)
                {
                    attributes[LightSelectorAttributeName] = lightSelectorAccessor.Value;
                }
                if (lightBaseColorAccessor.HasValue)
                {
                    attributes[LightBaseColorAttributeName] = lightBaseColorAccessor.Value;
                }
                if (lightNormalAccessor.HasValue)
                {
                    attributes[LightNormalAttributeName] = lightNormalAccessor.Value;
                }
                if (lightPostScaleAccessor.HasValue)
                {
                    attributes[LightPostScaleAttributeName] = lightPostScaleAccessor.Value;
                }

                var primitiveDefinition = new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["mode"] = 4,
                    ["material"] = materialBuild.MaterialIndexByKey[group.MaterialKey]
                };
                if (ShouldWriteFullMetadata(options))
                {
                    primitiveDefinition["extras"] = BuildPrimitiveExtras(group);
                }

                primitiveDefinitions.Add(primitiveDefinition);
            }

            var meshDefinition = new Dictionary<string, object?>
            {
                ["name"] = mesh.Name,
                ["primitives"] = primitiveDefinitions
            };
            if (ShouldWriteFullMetadata(options) && mesh.SourceMesh is { } sourceMesh)
            {
                meshDefinition["extras"] = BuildChunkLodMeshExtras(sourceMesh);
            }

            meshes.Add(meshDefinition);

            var nodeIndex = nodes.Count;
            lodChildren[mesh.LodIndex].Add(nodeIndex);
            var nodeDefinition = new Dictionary<string, object?>
            {
                ["name"] = mesh.Name,
                ["mesh"] = meshIndex
            };
            if (ShouldWriteFullMetadata(options) && mesh.SourceMesh is { } nodeSourceMesh)
            {
                nodeDefinition["extras"] = BuildChunkNodeExtras(nodeSourceMesh);
            }

            nodes.Add(nodeDefinition);
        }
        AddTiming(
            options,
            "tfrag.geometry-write",
            "Terrain glTF buffer write",
            geometryWriteStart,
            $"{meshes.Count} meshes, {gltfBufferWriter.Accessors.Count} accessors");

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object?>
        {
            ["asset"] = new
            {
                version = "2.0",
                generator = $"RatchetPs2 {NormalizeLabel(options.GameLabel)} tfrag glTF exporter"
            },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = nodes,
            ["meshes"] = meshes,
            ["materials"] = materialBuild.Materials,
            ["buffers"] = new[] { new { uri = binFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = gltfBufferWriter.BufferViews,
            ["accessors"] = gltfBufferWriter.Accessors,
            ["extensionsUsed"] = new[] { UnlitExtensionName }
        };
        if (ShouldWriteFullMetadata(options))
        {
            gltf["extras"] = BuildRootExtras(terrain, decoded, options);
        }

        if (materialBuild.TextureIds.Count > 0)
        {
            gltf["samplers"] = materialBuild.Samplers;
            gltf["images"] = materialBuild.Images;
            gltf["textures"] = materialBuild.Textures;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = !options.Minify };
        var jsonStart = Stopwatch.GetTimestamp();
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        AddTiming(options, "tfrag.json", "Terrain glTF JSON serialize", jsonStart, $"{gltfBytes.Length} bytes");

        var diagnosticsStart = Stopwatch.GetTimestamp();
        var diagnosticsBytes = options.IncludeDiagnostics
            ? BuildDiagnostics(terrain, decoded, options, jsonOptions)
            : [];
        AddTiming(
            options,
            "tfrag.diagnostics",
            "Terrain diagnostics serialize",
            diagnosticsStart,
            options.IncludeDiagnostics ? $"{diagnosticsBytes.Length} bytes" : "disabled");

        return new TfragGltfExport(
            gltfBytes,
            binBytes,
            diagnosticsBytes);
    }

    private static void ValidateOptions(TfragGltfExportOptions options)
    {
        if (options.WorldPositionScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.WorldPositionScale));
        }

        if (options.LocalPositionScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.LocalPositionScale));
        }

        if (options.TopologyPayloadPrefixBytes is < 0 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(options.TopologyPayloadPrefixBytes));
        }

        if (options.MaxTriangleEdgeLength is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxTriangleEdgeLength));
        }

        if (options.LodIndex is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options.LodIndex));
        }
    }

    private static bool ShouldWriteFullMetadata(TfragGltfExportOptions options)
    {
        return options.MetadataMode == GltfExportMetadataMode.Full;
    }

    private static IEnumerable<int> GetExportLodIndices(TfragGltfExportOptions options)
    {
        return options.LodIndex is { } lodIndex
            ? [lodIndex]
            : [0, 1, 2];
    }

    private static IReadOnlyList<TfragGltfMesh> BuildGltfMeshes(
        IReadOnlyList<TfragChunkLodMesh> meshes,
        TfragGltfExportOptions options)
    {
        if (ShouldWriteFullMetadata(options))
        {
            return meshes
                .OrderBy(mesh => mesh.LodIndex)
                .ThenBy(mesh => mesh.Chunk.Index)
                .Select(mesh => new TfragGltfMesh(
                    $"chunk_{mesh.Chunk.Index:0000}_lod_{mesh.LodIndex}",
                    mesh.LodIndex,
                    mesh.Groups,
                    mesh))
                .ToArray();
        }

        return meshes
            .GroupBy(mesh => mesh.LodIndex)
            .OrderBy(group => group.Key)
            .SelectMany(group => group
                .SelectMany(mesh => mesh.Groups)
                .GroupBy(primitive => primitive.MaterialKey)
                .OrderBy(primitiveGroup => primitiveGroup.Key.TextureId)
                .ThenBy(primitiveGroup => primitiveGroup.Key.ClampU)
                .ThenBy(primitiveGroup => primitiveGroup.Key.ClampV)
                .Select((primitiveGroup, index) => new TfragGltfMesh(
                    $"lod_{group.Key}_material_{index:0000}",
                    group.Key,
                    [MergePrimitiveGroups(primitiveGroup)],
                    SourceMesh: null)))
            .ToArray();
    }

    private static TfragPrimitiveGroup MergePrimitiveGroups(IEnumerable<TfragPrimitiveGroup> groups)
    {
        using var enumerator = groups.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("Cannot merge an empty tfrag primitive group.");
        }

        var first = enumerator.Current;
        var merged = new TfragPrimitiveGroup(
            first.MaterialKey,
            first.TopologyPacket,
            first.TopologyDecode,
            first.MaterialRange,
            first.NormalBuildResult);
        AppendPrimitiveGroup(merged, first);
        while (enumerator.MoveNext())
        {
            AppendPrimitiveGroup(merged, enumerator.Current);
        }

        return merged;
    }

    private static void AppendPrimitiveGroup(TfragPrimitiveGroup target, TfragPrimitiveGroup source)
    {
        var baseVertex = checked((uint)target.Positions.Count);
        target.Positions.AddRange(source.Positions);
        target.Normals.AddRange(source.Normals);
        target.TexCoords.AddRange(source.TexCoords);
        target.Colors.AddRange(source.Colors);
        target.LightSelectors.AddRange(source.LightSelectors);
        target.LightBaseColors.AddRange(source.LightBaseColors);
        target.LightNormals.AddRange(source.LightNormals);
        target.LightPostScales.AddRange(source.LightPostScales);
        foreach (var index in source.Indices)
        {
            target.Indices.Add(checked(baseVertex + index));
        }

        target.WindingCorrectedTriangleCount += source.WindingCorrectedTriangleCount;
    }

    private static void AddTiming(
        TfragGltfExportOptions options,
        string key,
        string label,
        long startTimestamp,
        string? detail = null)
    {
        options.TimingSink?.Invoke(
            key,
            label,
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
            detail);
    }
}

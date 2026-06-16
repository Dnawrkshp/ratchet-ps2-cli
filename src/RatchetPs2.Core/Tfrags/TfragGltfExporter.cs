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

        return Export(TfragTerrainReader.Read(input), gltfFileName, options);
    }

    public static TfragGltfExport Export(
        TfragTerrain terrain,
        string gltfFileName = "tfrag.gltf",
        TfragGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        options ??= new TfragGltfExportOptions();
        ValidateOptions(options);

        var decoded = BuildDecodedTerrain(terrain, options);
        if (decoded.Meshes.Count == 0)
        {
            throw new InvalidDataException("Tfrag terrain has no decoded chunk LOD geometry to export.");
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        var materialBuild = BuildMaterials(decoded.MaterialKeys, options);

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
            ["children"] = rootChildren,
            ["extras"] = BuildRootNodeExtras(terrain, options)
        });

        for (var lodIndex = 0; lodIndex <= 2; lodIndex++)
        {
            var children = new List<int>();
            lodChildren[lodIndex] = children;
            rootChildren.Add(nodes.Count);
            nodes.Add(new Dictionary<string, object?>
            {
                ["name"] = $"lod_{lodIndex}",
                ["children"] = children,
                ["extras"] = BuildLodGroupExtras(decoded, lodIndex)
            });
        }

        foreach (var mesh in decoded.Meshes.OrderBy(mesh => mesh.LodIndex).ThenBy(mesh => mesh.Chunk.Index))
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

                primitiveDefinitions.Add(new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["mode"] = 4,
                    ["material"] = materialBuild.MaterialIndexByKey[group.MaterialKey],
                    ["extras"] = BuildPrimitiveExtras(group)
                });
            }

            meshes.Add(new Dictionary<string, object?>
            {
                ["name"] = $"chunk_{mesh.Chunk.Index:0000}_lod_{mesh.LodIndex}",
                ["primitives"] = primitiveDefinitions,
                ["extras"] = BuildChunkLodMeshExtras(mesh)
            });

            var nodeIndex = nodes.Count;
            lodChildren[mesh.LodIndex].Add(nodeIndex);
            nodes.Add(new Dictionary<string, object?>
            {
                ["name"] = $"chunk_{mesh.Chunk.Index:0000}_lod_{mesh.LodIndex}",
                ["mesh"] = meshIndex,
                ["extras"] = BuildChunkNodeExtras(mesh)
            });
        }

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
            ["extensionsUsed"] = new[] { UnlitExtensionName },
            ["extras"] = BuildRootExtras(terrain, decoded, options)
        };

        if (materialBuild.TextureIds.Count > 0)
        {
            gltf["samplers"] = materialBuild.Samplers;
            gltf["images"] = materialBuild.Images;
            gltf["textures"] = materialBuild.Textures;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        return new TfragGltfExport(
            JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions),
            binBytes,
            BuildDiagnostics(terrain, decoded, options, jsonOptions));
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
    }
}

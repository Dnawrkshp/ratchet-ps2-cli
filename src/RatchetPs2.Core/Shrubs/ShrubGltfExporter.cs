using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public sealed record ShrubGltfExport(
    byte[] GltfBytes,
    byte[] BinBytes,
    byte[] DiagnosticsBytes);

public sealed class ShrubGltfExportOptions
{
    public string? BufferFileName { get; init; }

    public string GameLabel { get; init; } = "Shrub";

    public float PositionScale { get; init; } = 1f / 1024f;

    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }

    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }

    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }

    public string? ExternalBillboardTextureUri { get; init; }

    public TextureSize? ExternalBillboardTextureSize { get; init; }

    public TextureAlphaInfo? ExternalBillboardTextureAlpha { get; init; }

    public bool IncludeDiagnostics { get; init; } = true;

    public bool Minify { get; init; }

    public GltfExportMetadataMode MetadataMode { get; init; } = GltfExportMetadataMode.Full;
}

public static partial class ShrubGltfExporter
{
    private const string UnlitExtensionName = "KHR_materials_unlit";
    private const int GltfLinearFilter = 9729;
    private const int GltfWrapRepeat = 10497;

    public static ShrubGltfExport Export(
        Stream input,
        string gltfFileName = "shrub.gltf",
        ShrubGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Export(ShrubClassReader.Read(input), gltfFileName, options);
    }

    public static ShrubGltfExport Export(
        ShrubClass shrub,
        string gltfFileName = "shrub.gltf",
        ShrubGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shrub);
        options ??= new ShrubGltfExportOptions();
        if (options.PositionScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.PositionScale));
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        var mesh = BuildMesh(shrub, options);
        if (mesh.Groups.Count == 0)
        {
            throw new InvalidDataException("Shrub has no decoded triangles to export.");
        }

        var materialBuild = BuildMaterials(mesh.TextureIds, options);
        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var gltfBufferWriter = new GltfBufferWriter(writer);
        var primitives = new List<Dictionary<string, object>>();

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
            var indexAccessor = gltfBufferWriter.WriteUInt32IndexAccessor(group.Indices);

            var primitive = new Dictionary<string, object>
            {
                ["attributes"] = new Dictionary<string, int>
                {
                    ["POSITION"] = positionAccessor,
                    ["NORMAL"] = normalAccessor,
                    ["TEXCOORD_0"] = texCoordAccessor
                },
                ["indices"] = indexAccessor,
                ["mode"] = 4,
                ["material"] = materialBuild.MaterialIndexByTextureId[group.TextureId]
            };
            if (ShouldWriteFullMetadata(options))
            {
                primitive["extras"] = new
                {
                    ShrubTextureId = group.TextureId,
                    group.PacketIndex,
                    group.FirstSourcePrimitiveIndex,
                    group.LastSourcePrimitiveIndex,
                    group.SourceVertexCount,
                    group.TriangleCount,
                    group.WindingCorrectedTriangleCount
                };
            }

            primitives.Add(primitive);
        }

        var gameLabel = NormalizeLabel(options.GameLabel);
        var meshes = new List<object>
        {
            new
            {
                name = "shrub",
                primitives,
                extras = ShouldWriteFullMetadata(options) ? BuildMeshExtras(shrub, mesh) : null
            }
        };
        var nodes = new List<object>
        {
            new
            {
                name = "shrub",
                mesh = 0,
                extras = ShouldWriteFullMetadata(options) ? BuildNodeExtras(shrub, gameLabel, options.PositionScale) : null
            }
        };
        var gltfTextureCount = materialBuild.TextureIds.Count;

        if (BuildBillboardMesh(shrub, mesh, options, gltfBufferWriter, meshes.Count, materialBuild.Materials.Count, gltfTextureCount)
            is { } billboardMesh)
        {
            materialBuild.Materials.Add(BuildBillboardMaterial(options, gltfTextureCount));
            meshes.Add(billboardMesh.Mesh);
            nodes.Add(billboardMesh.Node);
        }

        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = $"RatchetPs2 {gameLabel} shrub glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = Enumerable.Range(0, nodes.Count).ToArray() } },
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
            gltf["extras"] = BuildRootExtras(shrub, mesh, gameLabel);
        }

        var images = materialBuild.TextureIds.Select(textureId => new
        {
            name = $"tex_{textureId:0000}",
            uri = options.ExternalTextureUris![textureId]
        }).ToList();
        if (!string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri))
        {
            images.Add(new
            {
                name = "shrub_billboard",
                uri = options.ExternalBillboardTextureUri!
            });
        }

        if (images.Count > 0)
        {
            gltf["samplers"] = new[]
            {
                new
                {
                    magFilter = GltfLinearFilter,
                    minFilter = GltfLinearFilter,
                    wrapS = GltfWrapRepeat,
                    wrapT = GltfWrapRepeat
                }
            };
            gltf["images"] = images;
            gltf["textures"] = Enumerable.Range(0, images.Count).Select(sourceIndex => new
            {
                sampler = 0,
                source = sourceIndex
            }).ToArray();
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = !options.Minify };
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = options.IncludeDiagnostics
            ? BuildDiagnostics(shrub, mesh, gameLabel, options, jsonOptions)
            : [];

        return new ShrubGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

    private static bool ShouldWriteFullMetadata(ShrubGltfExportOptions options)
    {
        return options.MetadataMode == GltfExportMetadataMode.Full;
    }

}

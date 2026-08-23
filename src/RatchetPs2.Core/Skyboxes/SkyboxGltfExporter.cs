using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Skyboxes;

public sealed record SkyboxGltfExport(
    byte[] GltfBytes,
    byte[] BinBytes,
    byte[] DiagnosticsBytes,
    IReadOnlyList<SkyboxGltfTextureResource> Textures);

public sealed record SkyboxGltfTextureResource(
    int Index,
    string Uri,
    string FileName,
    byte[] PngBytes,
    TextureSize Size,
    TextureAlphaInfo Alpha);

public sealed class SkyboxGltfExportOptions
{
    public string? BufferFileName { get; init; }

    public string GameLabel { get; init; } = "Skybox";

    public string TextureDirectoryName { get; init; } = "textures";

    public TextureConversionOptions? TextureConversionOptions { get; init; }

    public float PositionScale { get; init; } = 1f / 1024f;

    public bool DecodeUntexturedGouraudColors { get; init; } = true;

    public float RuntimeFrameRate { get; init; } = 60f;

    public IReadOnlyDictionary<int, SkyboxShellRotationOverride> ShellRotationOverrides { get; init; }
        = new Dictionary<int, SkyboxShellRotationOverride>();

    public bool IncludeDiagnostics { get; init; } = true;

    public bool Minify { get; init; }

    public GltfExportMetadataMode MetadataMode { get; init; } = GltfExportMetadataMode.Full;
}

public sealed record SkyboxShellRotationOverride(
    short? RotationX = null,
    short? RotationY = null,
    short? RotationZ = null,
    string Reason = "",
    Vector3? RotationDeltaRadiansPerFrame = null);

public static partial class SkyboxGltfExporter
{
    private const byte UntexturedTextureId = 0xFF;
    private const string UnlitExtensionName = "KHR_materials_unlit";
    private const string EmissiveStrengthExtensionName = "KHR_materials_emissive_strength";
    private const float BloomEmissionStrength = 1f;
    private const float RotationTickRadians = MathF.PI / 32768f;
    private const int GltfLinearFilter = 9729;
    private const int GltfWrapClampToEdge = 33071;

    public static SkyboxGltfExport Export(
        Stream input,
        string gltfFileName = "skybox.gltf",
        SkyboxGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Export(SkyboxReader.Read(input), gltfFileName, options);
    }

    public static SkyboxGltfExport Export(
        Skybox skybox,
        string gltfFileName = "skybox.gltf",
        SkyboxGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(skybox);
        options ??= new SkyboxGltfExportOptions();

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);
        if (options.RuntimeFrameRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RuntimeFrameRate));
        }

        var shellRotationOverrides = options.ShellRotationOverrides
            ?? new Dictionary<int, SkyboxShellRotationOverride>();
        var textureResources = BuildTextureResources(skybox, options);
        var mesh = BuildMesh(skybox, options);
        var materialResult = BuildMaterials(
            mesh.Primitives,
            textureResources,
            mesh.VertexAlphaByTextureId,
            skybox.Header.Color,
            mesh.UsesUntexturedGouraudColors,
            options.MetadataMode);

        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var gltfBufferWriter = new GltfBufferWriter(writer);
        var shellMeshes = new List<Dictionary<string, object>>();
        var shellNodes = new List<Dictionary<string, object>>();
        var gameLabel = NormalizeLabel(options.GameLabel);
        var meshName = "skybox";
        foreach (var shell in skybox.Shells)
        {
            var shellPrimitives = mesh.Primitives
                .Where(primitive => primitive.ShellIndex == shell.Index)
                .OrderBy(primitive => primitive.DrawOrder)
                .ToArray();
            if (shellPrimitives.Length == 0)
            {
                continue;
            }

            var shellGeometry = BuildShellGeometry(mesh, shellPrimitives);
            var positionAccessor = gltfBufferWriter.WriteVector3Accessor(
                shellGeometry.Positions,
                target: GltfBufferWriter.ArrayBufferTarget,
                includeMinMax: true);
            var normalAccessor = gltfBufferWriter.WriteVector3Accessor(
                shellGeometry.Normals,
                target: GltfBufferWriter.ArrayBufferTarget);
            var texCoordAccessor = gltfBufferWriter.WriteVector2Accessor(
                shellGeometry.TexCoords,
                target: GltfBufferWriter.ArrayBufferTarget);
            var colorAccessor = gltfBufferWriter.WriteVector4Accessor(
                shellGeometry.Colors,
                target: GltfBufferWriter.ArrayBufferTarget);
            var attributes = new Dictionary<string, int>
            {
                ["POSITION"] = positionAccessor,
                ["NORMAL"] = normalAccessor,
                ["TEXCOORD_0"] = texCoordAccessor,
                ["COLOR_0"] = colorAccessor
            };
            var primitives = new List<Dictionary<string, object>>();
            foreach (var primitiveGeometry in shellGeometry.Primitives)
            {
                var primitive = primitiveGeometry.Primitive;
                var indexAccessor = gltfBufferWriter.WriteUInt32IndexAccessor(primitiveGeometry.Indices);
                var primitiveDefinition = new Dictionary<string, object>
                {
                    ["attributes"] = attributes,
                    ["indices"] = indexAccessor,
                    ["mode"] = 4,
                    ["material"] = materialResult.MaterialIndexByKey[SkyboxMaterialKey.ForPrimitive(primitive)]
                };
                if (options.MetadataMode != GltfExportMetadataMode.None)
                {
                    primitiveDefinition["extras"] = BuildPrimitiveExtras(primitive, shell, options.RuntimeFrameRate, shellRotationOverrides);
                }

                primitives.Add(primitiveDefinition);
            }

            var shellName = BuildShellName(meshName, shell.Index);
            var meshIndex = shellMeshes.Count;
            shellMeshes.Add(new Dictionary<string, object>
            {
                ["name"] = shellName,
                ["primitives"] = primitives
            });
            if (options.MetadataMode != GltfExportMetadataMode.None)
            {
                shellMeshes[^1]["extras"] = BuildShellMeshExtras(shell, shellGeometry, shellPrimitives, options.RuntimeFrameRate, shellRotationOverrides);
            }

            shellNodes.Add(new Dictionary<string, object>
            {
                ["name"] = shellName,
                ["mesh"] = meshIndex
            });
            if (options.MetadataMode != GltfExportMetadataMode.None)
            {
                shellNodes[^1]["extras"] = BuildShellNodeExtras(shell, options.RuntimeFrameRate, shellRotationOverrides);
            }
        }

        var nodes = new List<Dictionary<string, object>>
        {
            new()
            {
                ["name"] = meshName,
                ["children"] = Enumerable.Range(1, shellNodes.Count).ToArray()
            }
        };
        if (options.MetadataMode != GltfExportMetadataMode.None)
        {
            nodes[0]["extras"] = BuildNodeExtras(skybox, gameLabel, options.PositionScale, options.RuntimeFrameRate, shellRotationOverrides);
        }

        nodes.AddRange(shellNodes);
        var binBytes = binStream.ToArray();
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = $"RatchetPs2 {gameLabel} skybox glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = nodes,
            ["meshes"] = shellMeshes,
            ["materials"] = materialResult.Materials,
            ["buffers"] = new[] { new { uri = binFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = gltfBufferWriter.BufferViews,
            ["accessors"] = gltfBufferWriter.Accessors,
            ["extensionsUsed"] = BuildExtensionsUsed(materialResult.UsesBloomEmission)
        };
        if (options.MetadataMode == GltfExportMetadataMode.Full)
        {
            gltf["extras"] = BuildMeshExtras(skybox, mesh, options.RuntimeFrameRate, shellRotationOverrides);
        }

        if (textureResources.Count > 0)
        {
            gltf["samplers"] = new[]
            {
                new
                {
                    magFilter = GltfLinearFilter,
                    minFilter = GltfLinearFilter,
                    wrapS = GltfWrapClampToEdge,
                    wrapT = GltfWrapClampToEdge
                }
            };
            gltf["images"] = textureResources.Select(texture => new
            {
                name = $"tex_{texture.Index:0000}",
                uri = texture.Uri
            }).ToArray();
            gltf["textures"] = textureResources.Select((texture, sourceIndex) => new
            {
                sampler = 0,
                source = sourceIndex
            }).ToArray();
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = !options.Minify };
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = options.IncludeDiagnostics
            ? BuildDiagnosticsBytes(
                skybox,
                mesh,
                textureResources,
                gameLabel,
                options.RuntimeFrameRate,
                shellRotationOverrides,
                jsonOptions)
            : [];

        return new SkyboxGltfExport(gltfBytes, binBytes, diagnosticsBytes, textureResources);
    }

    private static string NormalizeLabel(string? label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? "Skybox"
            : label.Trim().ToUpperInvariant();
    }

    private static string FormatOffset(long offset)
    {
        return $"0x{offset:X}";
    }
}

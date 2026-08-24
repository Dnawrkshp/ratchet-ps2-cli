using System.Diagnostics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

public sealed record TieGltfExport(byte[] GltfBytes, byte[] BinBytes, byte[] DiagnosticsBytes);

public enum TieMaterialAlphaUsage
{
    Opaque,
    Opacity,
    ReflectiveMask
}

public sealed class TieGltfExportOptions
{
    public int LodIndex { get; init; }
    /// <summary>Quantized instance distance used to resolve the game's distance-dependent culling in static glTF.</summary>
    public int BackfaceCullDistanceBucket { get; init; } = 3;
    public string? BufferFileName { get; init; }
    public string GameLabel { get; init; } = TieGameProfile.Default.GameLabel;
    public TieGameProfile? GameProfile { get; init; }
    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }
    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }
    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }
    public bool IncludeDiagnostics { get; init; } = true;
    public bool Minify { get; init; }
    public GltfExportMetadataMode MetadataMode { get; init; } = GltfExportMetadataMode.Full;
    public Action<string, string, double, string?>? TimingSink { get; init; }
}

public static class TieGltfExporter
{
    public static TieGltfExport Export(Stream input, string gltfFileName = "tie.gltf", TieGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        options ??= new TieGltfExportOptions();
        var profile = options.GameProfile
            ?? TieGameProfile.Default.WithGameLabel(options.GameLabel);
        var readStart = Stopwatch.GetTimestamp();
        var tie = TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(profile));
        AddTiming(options, "tie.read", "Tie class parse", readStart);
        return Export(
            tie,
            gltfFileName,
            options);
    }

    public static TieGltfExport Export(TieClass tie, string gltfFileName = "tie.gltf", TieGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tie);
        options ??= new TieGltfExportOptions();

        var lodIndex = options.LodIndex;
        var profile = options.GameProfile
            ?? TieGameProfile.Default.WithGameLabel(options.GameLabel);
        var topology = tie.LodTopologies.FirstOrDefault(topology => topology.LodIndex == lodIndex)
            ?? throw new InvalidDataException($"Tie LOD {lodIndex} was not decoded.");
        if (topology.LogicalVertexCount == 0)
        {
            throw new InvalidDataException($"Tie LOD {lodIndex} has no decoded logical vertices.");
        }

        if (topology.UnresolvedLogicalVertexCount > 0)
        {
            throw new InvalidDataException(
                $"Tie LOD {lodIndex} has {topology.UnresolvedLogicalVertexCount} unresolved logical vertices.");
        }

        var binFileName = string.IsNullOrWhiteSpace(options.BufferFileName)
            ? $"{Path.GetFileNameWithoutExtension(gltfFileName)}.buffer.bin"
            : Path.GetFileName(options.BufferFileName);

        var positionsStart = Stopwatch.GetTimestamp();
        var positions = TieGltfPositionBuilder.BuildPositions(tie, topology);
        AddTiming(options, "tie.positions", "Tie positions", positionsStart, $"{positions.Count} vertices");

        var texCoordStart = Stopwatch.GetTimestamp();
        var texCoords = TieGltfTexCoordBuilder.BuildTexCoords(tie, topology);
        var multipassTexCoords = TieGltfTexCoordBuilder.BuildMultipassTexCoords(tie, topology, texCoords);
        var environmentNormals = profile.GameLabel == "DL"
            ? TieGltfTexCoordBuilder.BuildEnvironmentNormals(tie, topology)
            : [];
        AddTiming(options, "tie.texcoords", "Tie texture coordinates", texCoordStart, $"{texCoords.Count} vertices");

        var glowStart = Stopwatch.GetTimestamp();
        var glowColorResult = TieGltfGlowBuilder.BuildColors(tie, topology, positions.Count);
        AddTiming(options, "tie.glow", "Tie glow colors", glowStart);

        var sourceNormalPhaseStart = Stopwatch.GetTimestamp();
        var sourceNormalPhaseAnalysis = profile.UsePackedVertexNormalTableSource
            && tie.RgbaRemapOperations.Any(operation => operation.LodIndex == topology.LodIndex)
                ? TieGltfSourceNormalPhaseAnalysis.Empty
                : TieGltfSourceNormalPhaseAnalyzer.Analyze(tie, topology, positions, profile);
        AddTiming(
            options,
            "tie.source-normal-phase",
            "Tie source normal phase",
            sourceNormalPhaseStart,
            $"{sourceNormalPhaseAnalysis.Strips.Count} strips");
        var packetGroupsStart = Stopwatch.GetTimestamp();
        var packetGroupResult = TieGltfPacketIndexGroupBuilder.Build(
            tie,
            topology,
            glowColorResult.Colors);
        var flatIndices = packetGroupResult.PacketIndexGroups.SelectMany(group => group.Indices).ToArray();
        AddTiming(
            options,
            "tie.packet-groups",
            "Tie packet index groups",
            packetGroupsStart,
            $"{packetGroupResult.PacketIndexGroups.Count} groups, {flatIndices.Length} indices");

        var normalStart = Stopwatch.GetTimestamp();
        var normalResult = TieGltfNormalBuilder.Build(
            tie,
            topology,
            positions,
            flatIndices,
            sourceNormalPhaseAnalysis,
            profile);
        if (profile.OrientTriangleWindingToNormals
            && TieGltfGeometryBuilder.OrientTrianglesConsistently(
                positions,
                normalResult.IndexNormals,
                normalResult.SourceNormalIndexStates,
                packetGroupResult.PacketIndexGroups) > 0)
        {
            flatIndices = packetGroupResult.PacketIndexGroups.SelectMany(group => group.Indices).ToArray();
            normalResult = TieGltfNormalBuilder.Build(
                tie,
                topology,
                positions,
                flatIndices,
                sourceNormalPhaseAnalysis,
                profile);
        }
        AddTiming(options, "tie.normals", "Tie normals", normalStart, $"{normalResult.Normals.Count} normals");

        var ambientStart = Stopwatch.GetTimestamp();
        var ambientIndexResult = profile.UseAmbientIndexAttribute
            ? TieGltfAmbientBuilder.BuildIndices(
                tie,
                topology,
                normalResult.TableNormalTargetMode,
                positions.Count,
                flatIndices,
                normalResult.IndexNormals,
                normalResult.TableNormalLayout)
            : TieGltfAmbientBuilder.Empty;
        AddTiming(
            options,
            "tie.ambient",
            "Tie ambient indices",
            ambientStart,
            $"{ambientIndexResult.ResolvedIndexCount} resolved indices");

        var geometryStart = Stopwatch.GetTimestamp();
        var geometry = TieGltfGeometryBuilder.Build(
            tie.Shaders,
            positions,
            normalResult.Normals,
            environmentNormals,
            normalResult.IndexNormals,
            normalResult.SourceNormalVertexIndices,
            normalResult.SourceNormalIndexOffsets,
            normalResult.SourceNormalVertexStates,
            normalResult.SourceNormalIndexStates,
            profile.SuppressGeneratedNormalFallback,
            texCoords,
            multipassTexCoords,
            glowColorResult.Colors,
            ambientIndexResult.Indices,
            ambientIndexResult.IndexIndices,
            packetGroupResult.PacketIndexGroups,
            options.ExternalTextureSizes);
        AddTiming(
            options,
            "tie.geometry",
            "Tie geometry expand",
            geometryStart,
            $"{geometry.Positions.Count} vertices");

        var documentStart = Stopwatch.GetTimestamp();
        var export = TieGltfDocumentBuilder.Build(
            tie,
            topology,
            geometry,
            normalResult,
            sourceNormalPhaseAnalysis,
            glowColorResult,
            ambientIndexResult,
            packetGroupResult.PacketIndexGroups,
            packetGroupResult.PacketRgbaSlotCount,
            positions.Count,
            binFileName,
            profile,
            options.BackfaceCullDistanceBucket,
            options.ExternalTextureUris,
            options.ExternalTextureSizes,
            options.ExternalTextureAlpha,
            options.IncludeDiagnostics,
            options.Minify,
            options.MetadataMode);
        AddTiming(
            options,
            "tie.document",
            "Tie glTF document serialize",
            documentStart,
            $"{export.GltfBytes.Length} gltf bytes, {export.BinBytes.Length} bin bytes");
        return export;
    }

    private static void AddTiming(
        TieGltfExportOptions options,
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

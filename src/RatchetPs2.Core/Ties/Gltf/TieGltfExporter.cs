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
    public string? BufferFileName { get; init; }
    public string GameLabel { get; init; } = TieGameProfile.Default.GameLabel;
    public TieGameProfile? GameProfile { get; init; }
    public IReadOnlyDictionary<int, string>? ExternalTextureUris { get; init; }
    public IReadOnlyDictionary<int, TextureSize>? ExternalTextureSizes { get; init; }
    public IReadOnlyDictionary<int, TextureAlphaInfo>? ExternalTextureAlpha { get; init; }
}

public static class TieGltfExporter
{
    public static TieGltfExport Export(Stream input, string gltfFileName = "tie.gltf", TieGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        options ??= new TieGltfExportOptions();
        var profile = options.GameProfile
            ?? TieGameProfile.Default.WithGameLabel(options.GameLabel);
        return Export(
            TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(profile)),
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

        var positions = TieGltfPositionBuilder.BuildPositions(tie, topology);
        var texCoords = TieGltfTexCoordBuilder.BuildTexCoords(tie, topology);
        var glowColorResult = TieGltfGlowBuilder.BuildColors(tie, topology, positions.Count);
        var sourceNormalPhaseAnalysis = TieGltfSourceNormalPhaseAnalyzer.Analyze(tie, topology, positions, profile);
        var sourceNormalPhaseRepairTriangles = profile.UseSourceNormalPhaseWindingRepair
            ? sourceNormalPhaseAnalysis.RepairTriangles
            : new HashSet<TieGltfSourceNormalPhaseTriangleKey>();
        var packetGroupResult = TieGltfPacketIndexGroupBuilder.Build(
            tie,
            topology,
            glowColorResult.Colors,
            sourceNormalPhaseRepairTriangles);
        var flatIndices = packetGroupResult.PacketIndexGroups.SelectMany(group => group.Indices).ToArray();
        var normalResult = TieGltfNormalBuilder.Build(
            tie,
            topology,
            positions,
            flatIndices,
            sourceNormalPhaseAnalysis,
            profile);
        var ambientIndexResult = TieGltfAmbientBuilder.BuildIndices(
            tie,
            topology,
            normalResult.TableNormalTargetMode,
            positions.Count,
            flatIndices,
            normalResult.IndexNormals,
            normalResult.TableNormalLayout);
        var geometry = TieGltfGeometryBuilder.Build(
            tie.Shaders,
            positions,
            normalResult.Normals,
            normalResult.IndexNormals,
            normalResult.SourceNormalVertexIndices,
            normalResult.SourceNormalIndexOffsets,
            normalResult.SourceNormalVertexStates,
            normalResult.SourceNormalIndexStates,
            profile.SuppressGeneratedNormalFallback,
            profile.UseGeometryWindingRepair,
            texCoords,
            glowColorResult.Colors,
            ambientIndexResult.Indices,
            ambientIndexResult.IndexIndices,
            packetGroupResult.PacketIndexGroups,
            options.ExternalTextureSizes);

        return TieGltfDocumentBuilder.Build(
            tie,
            topology,
            geometry,
            normalResult,
            sourceNormalPhaseAnalysis,
            glowColorResult,
            ambientIndexResult,
            packetGroupResult.PacketIndexGroups,
            packetGroupResult.PacketRgbaSlotCount,
            packetGroupResult.SourceNormalPhaseWindingRepairStripCount,
            packetGroupResult.SourceNormalPhaseWindingRepairTriangleCount,
            positions.Count,
            binFileName,
            profile,
            options.ExternalTextureUris,
            options.ExternalTextureSizes,
            options.ExternalTextureAlpha);
    }
}

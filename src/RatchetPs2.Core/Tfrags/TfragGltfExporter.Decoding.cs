using System.Numerics;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private const float ChunkLongEdgeRadiusMultiplier = 2.5f;

    private static TfragDecodedTerrain BuildDecodedTerrain(TfragTerrain terrain, TfragGltfExportOptions options)
    {
        var meshes = new List<TfragChunkLodMesh>();
        var decodes = new List<TfragChunkLodDecode>();
        foreach (var chunk in terrain.Chunks)
        {
            for (var lodIndex = 0; lodIndex <= 2; lodIndex++)
            {
                var decode = DecodeChunkLod(terrain, chunk, lodIndex, options);
                decodes.Add(decode);
                if (decode.Mesh is not null)
                {
                    meshes.Add(decode.Mesh);
                }
            }
        }

        return new TfragDecodedTerrain(meshes, decodes);
    }

    private static TfragChunkLodDecode DecodeChunkLod(
        TfragTerrain terrain,
        TfragChunk chunk,
        int lodIndex,
        TfragGltfExportOptions options)
    {
        var recoveryLayout = BuildRuntimeLodRecoveryLayout(chunk, lodIndex);
        var segments = recoveryLayout.ScanSegments;
        var setupPackets = new List<TfragPositionPacket>();
        var positionPackets = new List<TfragPositionPacket>();
        var vertexReferencePackets = new List<TfragPositionPacket>();
        var topologyPackets = new List<TfragTopologyPacket>();
        var sourceBytes = terrain.SourceBytes.Span;
        var vifState = TfragVifState.Default;
        var referenceState = new TfragReferenceState();
        foreach (var segment in segments)
        {
            ScanSegment(
                sourceBytes,
                chunk,
                segment,
                setupPackets,
                positionPackets,
                vertexReferencePackets,
                topologyPackets,
                ref vifState,
                referenceState);
        }

        var sourcePositions = positionPackets.SelectMany(packet => packet.Positions).ToArray();
        var positions = sourcePositions
            .Select(position => ConvertPosition(chunk, position, options))
            .ToArray();
        var topologyPositionLookup = BuildTopologyPositionLookup(vertexReferencePackets, sourcePositions.Length);
        var referenceTexCoords = BuildReferenceTexCoordLookup(vertexReferencePackets);
        var groups = new List<TfragPrimitiveGroup>();
        var topologyDecodes = new List<TfragTopologyDecode>();
        var textureEntries = chunk.TextureEntries
            .Where(entry => entry.TextureId >= 0)
            .ToArray();
        var controlStripPlans = BuildControlStripPlans(topologyPackets);
        var maxTriangleEdgeLength = ResolveMaxTriangleEdgeLength(chunk, options);

        var wrenchTopologyDecode = TryDecodeWrenchStripTopology(
            sourceBytes,
            recoveryLayout,
            vertexReferencePackets,
            positions,
            sourcePositions.Length,
            referenceTexCoords,
            textureEntries,
            maxTriangleEdgeLength);
        if (wrenchTopologyDecode is not null && wrenchTopologyDecode.Indices.Count > 0)
        {
            topologyDecodes.Add(wrenchTopologyDecode);
            AddPrimitiveGroups(
                wrenchTopologyDecode,
                sourcePositions,
                chunk.RgbaEntries,
                referenceTexCoords,
                positions,
                textureEntries,
                options,
                groups);
        }
        else
        {
            for (var i = 0; i < topologyPackets.Count; i++)
            {
                var packet = topologyPackets[i];
                var fallbackTexture = ResolveTextureEntry(textureEntries, textureSlot: -1, groups.Count);
                var topologyDecode = DecodeTopologyPacket(
                    i,
                    packet,
                    positions,
                    topologyPositionLookup,
                    referenceTexCoords,
                    fallbackTexture,
                    maxTriangleEdgeLength,
                    options.TopologyPayloadPrefixBytes,
                    controlStripPlans);
                topologyDecodes.Add(topologyDecode);
                if (topologyDecode.Indices.Count == 0)
                {
                    continue;
                }

                AddPrimitiveGroups(
                    topologyDecode,
                    sourcePositions,
                    chunk.RgbaEntries,
                    referenceTexCoords,
                    positions,
                    textureEntries,
                    options,
                    groups);
            }
        }

        var mesh = groups.Count == 0
            ? null
            : new TfragChunkLodMesh(
                chunk,
                lodIndex,
                segments,
                setupPackets,
                positionPackets,
                vertexReferencePackets,
                topologyPackets,
                topologyDecodes,
                groups);

        return new TfragChunkLodDecode(
            chunk,
            lodIndex,
            segments,
            setupPackets,
            positionPackets,
            vertexReferencePackets,
            topologyPackets,
            topologyDecodes,
            mesh);
    }

    private static void AddPrimitiveGroups(
        TfragTopologyDecode topologyDecode,
        IReadOnlyList<TfragSourcePosition> sourcePositions,
        IReadOnlyList<TfragRgba> sourceColors,
        IReadOnlyList<Vector2?> referenceTexCoords,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<TfragTextureEntry> textureEntries,
        TfragGltfExportOptions options,
        List<TfragPrimitiveGroup> groups)
    {
        var materialRanges = topologyDecode.MaterialRanges.Count > 0
            ? topologyDecode.MaterialRanges
            : [new TfragMaterialRange(0, topologyDecode.Indices.Count, TextureSlot: -1)];

        foreach (var materialRange in materialRanges)
        {
            var texture = ResolveTextureEntry(textureEntries, materialRange.TextureSlot, groups.Count);
            var textureSize = options.ExternalTextureSizes is not null
                && options.ExternalTextureSizes.TryGetValue(texture.TextureId, out var resolvedTextureSize)
                    ? resolvedTextureSize
                    : default(TextureSize?);
            if (BuildPrimitiveGroup(
                texture,
                topologyDecode.Packet,
                topologyDecode,
                materialRange,
                sourcePositions,
                sourceColors,
                topologyDecode.Packet.ReferenceTexCoords,
                referenceTexCoords,
                positions,
                textureSize) is { } group)
            {
                groups.Add(group);
            }
        }
    }

    private static float? ResolveMaxTriangleEdgeLength(
        TfragChunk chunk,
        TfragGltfExportOptions options)
    {
        if (!options.MaxTriangleEdgeLength.HasValue)
        {
            return null;
        }

        var chunkScaledRadius = chunk.BoundingSphere.Radius * options.WorldPositionScale;
        return Math.Max(options.MaxTriangleEdgeLength.Value, chunkScaledRadius * ChunkLongEdgeRadiusMultiplier);
    }
}

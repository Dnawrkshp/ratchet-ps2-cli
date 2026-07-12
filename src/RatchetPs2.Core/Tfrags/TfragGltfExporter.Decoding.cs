using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private const float ChunkLongEdgeRadiusMultiplier = 2.5f;

    private static TfragDecodedTerrain BuildDecodedTerrain(TfragTerrain terrain, TfragGltfExportOptions options)
    {
        var meshes = new List<TfragChunkLodMesh>();
        var decodes = new List<TfragChunkLodDecode>();
        var lodIndices = GetExportLodIndices(options).ToArray();
        foreach (var chunk in terrain.Chunks)
        {
            foreach (var lodIndex in lodIndices)
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
        var lightSelectors = BuildChunkLightSelectors(terrain, chunk, sourcePositions.Length);
        var lightBaseColors = BuildChunkLightBaseColors(terrain, chunk, sourcePositions.Length, chunk.RgbaEntries);
        var lightNormals = BuildChunkLightNormals(terrain, chunk, sourcePositions.Length);
        var lightPostScales = BuildChunkLightPostScales(terrain, chunk, sourcePositions.Length);
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
                lightSelectors,
                lightBaseColors,
                lightNormals,
                lightPostScales,
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
                    lightSelectors,
                    lightBaseColors,
                    lightNormals,
                    lightPostScales,
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
        IReadOnlyList<float> sourceLightSelectors,
        IReadOnlyList<Vector4> sourceLightBaseColors,
        IReadOnlyList<Vector3> sourceLightNormals,
        IReadOnlyList<float> sourceLightPostScales,
        IReadOnlyList<Vector2?> referenceTexCoords,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<TfragTextureEntry> textureEntries,
        TfragGltfExportOptions options,
        List<TfragPrimitiveGroup> groups)
    {
        var materialRanges = topologyDecode.MaterialRanges.Count > 0
            ? topologyDecode.MaterialRanges
            : [new TfragMaterialRange(0, topologyDecode.Indices.Count, TextureSlot: -1)];
        var normalBuildResult = TfragGltfNormalBuilder.Build(positions, topologyDecode.Indices);

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
                normalBuildResult,
                sourcePositions,
                sourceColors,
                sourceLightSelectors,
                sourceLightBaseColors,
                sourceLightNormals,
                sourceLightPostScales,
                topologyDecode.Packet.ReferenceTexCoords,
                referenceTexCoords,
                positions,
                textureSize) is { } group)
            {
                groups.Add(group);
            }
        }
    }

    private static IReadOnlyList<float> BuildChunkLightSelectors(
        TfragTerrain terrain,
        TfragChunk chunk,
        int sourcePositionCount)
    {
        if (sourcePositionCount <= 0)
        {
            return [];
        }

        const ushort invalidSelector = 0x000F;
        var selectors = new float[sourcePositionCount];
        var fixedLight = unchecked((sbyte)chunk.DirectionalLightsOne);
        if (fixedLight >= 0)
        {
            Array.Fill(selectors, (float)(byte)fixedLight);
            return selectors;
        }

        Array.Fill(selectors, invalidSelector);
        var lightRowsOffset = chunk.DataOffset + chunk.LightOffset + 0x10;
        var rowCount = Math.Min(sourcePositionCount, chunk.VertexCount);
        var lightRowsLength = checked(rowCount * 8);
        var sourceBytes = terrain.SourceBytes.Span;
        if (rowCount <= 0
            || lightRowsOffset < 0
            || lightRowsOffset + lightRowsLength > sourceBytes.Length)
        {
            return selectors;
        }

        for (var row = 0; row < rowCount; row++)
        {
            selectors[row] = BinarySpanReader.ReadUInt16LittleEndian(
                sourceBytes,
                lightRowsOffset + row * 8 + 6);
        }

        return selectors;
    }

    private static IReadOnlyList<Vector4> BuildChunkLightBaseColors(
        TfragTerrain terrain,
        TfragChunk chunk,
        int sourcePositionCount,
        IReadOnlyList<TfragRgba> fallbackColors)
    {
        if (sourcePositionCount <= 0)
        {
            return [];
        }

        var colors = new Vector4[sourcePositionCount];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = BuildVertexColor(fallbackColors, (uint)i);
        }

        var lightRowsOffset = chunk.DataOffset + chunk.LightOffset + 0x10;
        var rowCount = Math.Min(sourcePositionCount, chunk.VertexCount);
        var lightRowsLength = checked(rowCount * 8);
        var sourceBytes = terrain.SourceBytes.Span;
        if (rowCount <= 0
            || lightRowsOffset < 0
            || lightRowsOffset + lightRowsLength > sourceBytes.Length)
        {
            return colors;
        }

        for (var row = 0; row < rowCount; row++)
        {
            var packed = BinarySpanReader.ReadUInt16LittleEndian(
                sourceBytes,
                lightRowsOffset + row * 8 + 4);
            colors[row] = DecodePackedLightBaseColor(packed);
        }

        return colors;
    }

    private static IReadOnlyList<Vector3> BuildChunkLightNormals(
        TfragTerrain terrain,
        TfragChunk chunk,
        int sourcePositionCount)
    {
        if (sourcePositionCount <= 0)
        {
            return [];
        }

        var normals = new Vector3[sourcePositionCount];
        var lightRowsOffset = chunk.DataOffset + chunk.LightOffset + 0x10;
        var rowCount = Math.Min(sourcePositionCount, chunk.VertexCount);
        var lightRowsLength = checked(rowCount * 8);
        var sourceBytes = terrain.SourceBytes.Span;
        if (rowCount <= 0
            || lightRowsOffset < 0
            || lightRowsOffset + lightRowsLength > sourceBytes.Length)
        {
            return normals;
        }

        for (var row = 0; row < rowCount; row++)
        {
            var packed = BinarySpanReader.ReadUInt16LittleEndian(
                sourceBytes,
                lightRowsOffset + row * 8 + 2);
            normals[row] = DecodePackedLightNormal(packed);
        }

        return normals;
    }

    private static IReadOnlyList<float> BuildChunkLightPostScales(
        TfragTerrain terrain,
        TfragChunk chunk,
        int sourcePositionCount)
    {
        if (sourcePositionCount <= 0)
        {
            return [];
        }

        var scales = new float[sourcePositionCount];
        Array.Fill(scales, 1f);
        if ((chunk.Flags & 1) == 0)
        {
            return scales;
        }

        var lightRowsOffset = chunk.DataOffset + chunk.LightOffset + 0x10;
        var rowCount = Math.Min(sourcePositionCount, chunk.VertexCount);
        var lightRowsLength = checked(rowCount * 8);
        var sourceBytes = terrain.SourceBytes.Span;
        if (rowCount <= 0
            || lightRowsOffset < 0
            || lightRowsOffset + lightRowsLength > sourceBytes.Length)
        {
            return scales;
        }

        for (var row = 0; row < rowCount; row++)
        {
            scales[row] = sourceBytes[lightRowsOffset + row * 8 + 1] / 128f;
        }

        return scales;
    }

    private static Vector4 DecodePackedLightBaseColor(ushort packed)
    {
        const float scale = 1f / 31f;
        return new Vector4(
            (packed & 0x1F) * scale,
            ((packed >> 5) & 0x1F) * scale,
            ((packed >> 10) & 0x1F) * scale,
            1f);
    }

    private static Vector3 DecodePackedLightNormal(ushort packed)
    {
        const float angleScale = 2f * MathF.PI / 256f;
        var azimuth = (packed & 0xFF) * angleScale;
        var elevation = ((packed >> 8) & 0xFF) * angleScale;

        var azimuthX = MathF.Cos(azimuth);
        var azimuthY = MathF.Sin(azimuth);
        var elevationX = MathF.Cos(elevation);
        var elevationY = MathF.Sin(elevation);

        var ps2Normal = new Vector3(
            -azimuthX * elevationX,
            -azimuthY * elevationX,
            -elevationY);
        var gltfNormal = GltfCoordinateBasis.FromPs2Position(ps2Normal.X, ps2Normal.Y, ps2Normal.Z);
        return gltfNormal.LengthSquared() <= 0.00000001f
            ? Vector3.UnitY
            : Vector3.Normalize(gltfNormal);
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

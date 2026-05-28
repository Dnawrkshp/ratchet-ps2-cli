using System.Numerics;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static MeshReplacement BuildMeshReplacement(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        MobyGltfImportOptions options,
        TemplateDecodedMesh? templateMesh,
        MobyGltfImportPacketMode packetMode)
    {
        if (mesh.Positions.Count > 127)
        {
            throw new InvalidDataException(
                $"Mesh {mesh.TemplateMeshIndex:0000} has {mesh.Positions.Count} vertices. v1 importer supports at most 127 vertices per mesh.");
        }

        var quantizationClipCount = 0;
        var truncatedInfluenceCount = 0;
        var hasTemplateVertexData = TryBuildTemplateVertexData(templateEntry, mesh, templateMesh, options, out var templateVertexBuild);

        var useTemplateVertexLayoutWithGeneratedTopology = options.CustomStatic
            && options.CustomStaticPreserveTemplateVertexLayout
            && !options.CustomStaticPreserveTemplatePackets
            && !(options.CustomStaticGenerateRigidVertexData && !mesh.CustomStaticHideMesh)
            && !(options.CustomStaticGenerateRigidRowsInTemplateLayout && !mesh.CustomStaticHideMesh)
            && packetMode != MobyGltfImportPacketMode.GenerateVertexPositions
            && packetMode != MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape;
        var useTemplateVertexPositions =
            useTemplateVertexLayoutWithGeneratedTopology
            || (packetMode == MobyGltfImportPacketMode.GenerateVertexPositions
                && !(options.CustomStaticGenerateRigidVertexData && !mesh.CustomStaticHideMesh)
                && !(options.CustomStaticGenerateRigidRowsInTemplateLayout && !mesh.CustomStaticHideMesh));
        var vertexBuild = useTemplateVertexPositions
            ? BuildTemplatePositionVertexData(templateEntry, mesh, scale, templateMesh, options, ref quantizationClipCount)
            : options.CustomStatic
                && HasUsableSkinRows(mesh)
                && !mesh.CustomStaticHideMesh
                && packetMode != MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape
                ? BuildVertexData(templateEntry, mesh, scale, options, ref quantizationClipCount, ref truncatedInfluenceCount)
            : options.CustomStatic
                && options.CustomStaticGenerateCompactRigidRows
                && packetMode != MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape
                ? BuildCompactRigidRows(templateEntry, mesh, scale, options, ref quantizationClipCount)
            : options.CustomStatic && options.CustomStaticGenerateRigidRowsInTemplateLayout && !mesh.CustomStaticHideMesh
                ? BuildRigidRowsInTemplateVertexLayout(templateEntry, mesh, scale, options, ref quantizationClipCount)
            : packetMode switch
        {
            MobyGltfImportPacketMode.Passthrough => RequireTemplateVertexData(mesh, hasTemplateVertexData, templateVertexBuild, options.PacketMode),
            MobyGltfImportPacketMode.GenerateTopology => RequireTemplateVertexData(mesh, hasTemplateVertexData, templateVertexBuild, options.PacketMode),
            MobyGltfImportPacketMode.GenerateVertexPositions => BuildTemplatePositionVertexData(templateEntry, mesh, scale, templateMesh, options, ref quantizationClipCount),
            MobyGltfImportPacketMode.GenerateVertexDataFromMetadata => BuildMetadataVertexData(templateEntry, mesh, scale, templateMesh, options, ref quantizationClipCount),
            MobyGltfImportPacketMode.GenerateTopologyFromMetadataShape => mesh.Metadata?.VertexLayout is not null
                ? BuildMetadataVertexData(templateEntry, mesh, scale, templateMesh, options, ref quantizationClipCount)
                : RequireTemplateVertexData(mesh, hasTemplateVertexData, templateVertexBuild, options.PacketMode),
            MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape => BuildMetadataVertexData(templateEntry, mesh, scale, templateMesh, options, ref quantizationClipCount),
            MobyGltfImportPacketMode.GenerateVertexData => BuildVertexData(templateEntry, mesh, scale, options, ref quantizationClipCount, ref truncatedInfluenceCount),
            MobyGltfImportPacketMode.GenerateAll => BuildVertexData(templateEntry, mesh, scale, options, ref quantizationClipCount, ref truncatedInfluenceCount),
            _ => hasTemplateVertexData
                ? templateVertexBuild
                : BuildVertexData(templateEntry, mesh, scale, options, ref quantizationClipCount, ref truncatedInfluenceCount)
        };
        var sourceIndices = options.CustomStatic && options.CustomStaticDoubleSided && !mesh.CustomStaticHideMesh
            ? TriangleIndexUtils.BuildDoubleSidedTriangles(mesh.Indices)
            : mesh.Indices;
        var remappedIndices = TriangleIndexUtils.RemapIndices(sourceIndices, vertexBuild.IndexByOriginalIndex);
        var preserveTopologyTail = options.CustomStaticPreserveTopologyTail && !mesh.CustomStaticHideMesh;
        var compactTopologyPacket = options.CustomStaticCompactTopologyPacket && !mesh.CustomStaticHideMesh;
        var forceZeroMarkerTopology = options.CustomStaticForceZeroMarkerTopology && !mesh.CustomStaticHideMesh;
        var forceIsolatedTriangleTopology = options.CustomStaticIsolatedTriangleTopology && !mesh.CustomStaticHideMesh;
        var generateMinimalVifContainer = options.CustomStaticGenerateMinimalVifContainer && !mesh.CustomStaticHideMesh;
        var generatedVifDomainCapacity = options.CustomStaticGenerateVifDomainCapacity && !mesh.CustomStaticHideMesh
            ? ResolveGeneratedDomainCapacity(mesh)
            : (byte?)null;
        var templateVifTextureData = options.CustomStatic
            && options.CustomStaticUseGeneratedTextureMetadataPrototype
            && options.CustomStaticGenerateTextureMetadata
            && !mesh.CustomStaticHideMesh
                ? BuildCustomStaticTextureMetadataPayload(distance: options.CustomStaticTextureMetadataDistance)
                : templateEntry.VifTextureData;
        var vifBuild = useTemplateVertexLayoutWithGeneratedTopology
            ? BuildVifData(templateEntry.VifData, templateVifTextureData, remappedIndices, preserveTopologyTail, compactTopologyPacket, forceZeroMarkerTopology, forceIsolatedTriangleTopology, generateMinimalVifContainer, generatedVifDomainCapacity)
            : packetMode switch
        {
            MobyGltfImportPacketMode.Passthrough => BuildTemplateVifPassthrough(templateEntry),
            MobyGltfImportPacketMode.GenerateTopology => BuildVifData(templateEntry.VifData, templateVifTextureData, remappedIndices, preserveTopologyTail, compactTopologyPacket, forceZeroMarkerTopology, forceIsolatedTriangleTopology, generateMinimalVifContainer, generatedVifDomainCapacity),
            MobyGltfImportPacketMode.GenerateVertexPositions => BuildTemplateVifPassthrough(templateEntry),
            MobyGltfImportPacketMode.GenerateVertexDataFromMetadata => BuildMetadataVifData(templateEntry, mesh),
            MobyGltfImportPacketMode.GenerateTopologyFromMetadataShape => BuildMetadataShapeTopologyVifData(templateEntry, mesh, remappedIndices),
            MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape => BuildMetadataShapeTopologyVifData(
                templateEntry,
                mesh,
                remappedIndices,
                allowExactSourceShapeMismatch: options.CustomStatic,
                preferExactSourceTopology: options.CustomStatic),
            MobyGltfImportPacketMode.GenerateVertexData => TryBuildTemplateTopologyReplacement(templateEntry, vertexBuild.IndexByOriginalIndex, remappedIndices, out var templateVifBuild)
                ? templateVifBuild
                : BuildVifData(templateEntry.VifData, templateVifTextureData, remappedIndices, preserveTopologyTail, compactTopologyPacket, forceZeroMarkerTopology, forceIsolatedTriangleTopology, generateMinimalVifContainer, generatedVifDomainCapacity),
            MobyGltfImportPacketMode.GenerateAll => BuildVifData(templateEntry.VifData, templateVifTextureData, remappedIndices, preserveTopologyTail, compactTopologyPacket, forceZeroMarkerTopology, forceIsolatedTriangleTopology, generateMinimalVifContainer, generatedVifDomainCapacity),
            _ => vertexBuild.UsedTemplateVertexData
                ? BuildTemplateVifPassthrough(templateEntry)
                : TryBuildTemplateTopologyReplacement(templateEntry, vertexBuild.IndexByOriginalIndex, remappedIndices, out var templateVifBuild)
                    ? templateVifBuild
                    : BuildVifData(templateEntry.VifData, templateVifTextureData, remappedIndices, preserveTopologyTail, compactTopologyPacket, forceZeroMarkerTopology, forceIsolatedTriangleTopology, generateMinimalVifContainer, generatedVifDomainCapacity)
        };
        var vifData = vifBuild.VifData;
        var wroteTexCoords = false;
        var texCoordWriteCount = 0;
        var texCoordPaddingWriteCount = 0;
        if (!mesh.CustomStaticHideMesh
            && mesh.TexCoords is not null
            && !(options.CustomStatic && options.CustomStaticPreserveTemplatePackets)
            && !(options.CustomStatic && options.CustomStaticSkipTexCoordVifWrite))
        {
            vifData = (byte[])vifBuild.VifData.Clone();
            wroteTexCoords = TryWriteTexCoordsToVifData(
                vifData,
                mesh.TexCoords,
                mesh.Positions.Count,
                out texCoordWriteCount,
                out texCoordPaddingWriteCount);
        }

        return new MeshReplacement(
            vertexBuild.VertexData,
            vifData,
            vifBuild.VifTextureData,
            quantizationClipCount,
            truncatedInfluenceCount,
            vifBuild.ConnectorIndexCount,
            vertexBuild.UsedTemplateVertexData,
            vertexBuild.UsedMetadataVertexLayout,
            vertexBuild.UsedMetadataRowPrefixes,
            vertexBuild.UsedMetadataLowVertexBits,
            vertexBuild.PreservedTemplateLow9MaxValue,
            vifBuild.UsedMetadataTopologyLayout,
            vifBuild.PreservedTemplateLayout,
            vifBuild.ExpandedTopologyPacket,
            vifBuild.OriginalTopologyPayloadBytes,
            vifBuild.NewTopologyPayloadBytes,
            vifBuild.ReusedTemplateTopology,
            vifBuild.RemappedTemplateTopology,
            vifBuild.GeneratedTopologyFromGltf,
            vifBuild.GeneratedTopologyTokenCount,
            vifBuild.GeneratedTopologySourceTriangleCount,
            vifBuild.GeneratedTopologyPayloadFitsMetadata,
            vifBuild.GeneratedTopologyMatchesSourceTriangles,
            vifBuild.GeneratedTopologyPreservesTemplateControlMarkers,
            vifBuild.GeneratedTopologyMatchesTemplateControlShape,
            vifBuild.TemplateTopologyRestartCount,
            vifBuild.GeneratedTopologyRestartCount,
            vifBuild.TemplateTopologyNegativeTokenCount,
            vifBuild.GeneratedTopologyNegativeTokenCount,
            vifBuild.TemplateTopologyShape,
            vifBuild.GeneratedTopologyShape,
            vifBuild.TemplateTopologyTrace,
            vifBuild.GeneratedTopologyTrace,
            vifBuild.GeneratedTopologyRowUsage,
            vifBuild.TemplateTopologyZeroMarkers,
            vifBuild.GeneratedTopologyZeroMarkers,
            vifBuild.TopologySourceDiff,
            vifBuild.TopologyPayloadDiff,
            vifBuild.CompactTopologyTextureOverlapBytes,
            wroteTexCoords,
            texCoordWriteCount,
            texCoordPaddingWriteCount);
    }

    private static bool TryWriteTexCoordsToVifData(
        byte[] vifData,
        IReadOnlyList<Vector2> texCoords,
        int vertexCount,
        out int writeCount,
        out int paddingWriteCount)
    {
        writeCount = 0;
        paddingWriteCount = 0;
        if (texCoords.Count < vertexCount)
        {
            return false;
        }

        foreach (var packet in Ps2VifPacket.ReadSpans(vifData))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x05 || packet.PayloadLength < vertexCount * 4)
            {
                continue;
            }

            var payloadOffset = packet.Offset + 4;
            var domainCount = packet.PayloadLength / 4;
            var count = Math.Min(vertexCount, domainCount);
            for (var i = 0; i < count; i++)
            {
                var texCoord = texCoords[i];
                WriteInt16(vifData, payloadOffset + i * 4, QuantizeTexCoord(texCoord.X));
                WriteInt16(vifData, payloadOffset + i * 4 + 2, QuantizeTexCoord(texCoord.Y));
            }

            for (var i = count; i < domainCount; i++)
            {
                WriteInt16(vifData, payloadOffset + i * 4, 0);
                WriteInt16(vifData, payloadOffset + i * 4 + 2, 0);
            }

            writeCount = count;
            paddingWriteCount = Math.Max(0, domainCount - count);
            return true;
        }

        return false;
    }

    private static short QuantizeTexCoord(float value)
    {
        const float uvScale = 4096f;
        var rounded = MathF.Round(value * uvScale);
        return checked((short)Math.Clamp(rounded, short.MinValue, short.MaxValue));
    }
}

using System.Text.Json;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private static object BuildRootExtras(
        TfragTerrain terrain,
        TfragDecodedTerrain decoded,
        TfragGltfExportOptions options)
    {
        return new
        {
            ExportType = $"{NormalizeLabel(options.GameLabel)} tfrag terrain",
            Note = "Preview geometry reconstructed from tfrag VIF upload ranges. Texture assignment follows draw-control texture slots when present.",
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription,
            WorldPositionScale = options.WorldPositionScale,
            LocalPositionScale = options.LocalPositionScale,
            TopologyPayloadPrefixBytes = options.TopologyPayloadPrefixBytes,
            MaxTriangleEdgeLength = options.MaxTriangleEdgeLength,
            LodSemantics = "Wrench-style recovery: lod2 topology=(lod_2_ofs,shared_ofs), common=(shared_ofs,lod_1_ofs), lod1 topology=(lod_1_ofs,lod_0_ofs), lod01=(lod_0_ofs,shared_ofs+lod_1_size*0x10), lod0 topology=(shared_ofs+lod_1_size*0x10,rgba_ofs).",
            Header = BuildHeaderExtras(terrain),
            Geometry = BuildGeometryExtras(decoded)
        };
    }

    private static object BuildRootNodeExtras(TfragTerrain terrain, TfragGltfExportOptions options)
    {
        return new
        {
            Game = NormalizeLabel(options.GameLabel),
            terrain.TfragCount,
            terrain.TotalTfragCount,
            terrain.TfragRadius,
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription
        };
    }

    private static object BuildLodGroupExtras(TfragDecodedTerrain decoded, int lodIndex)
    {
        var lodMeshes = decoded.Meshes.Where(mesh => mesh.LodIndex == lodIndex).ToArray();
        return new
        {
            LodIndex = lodIndex,
            ChunkCount = lodMeshes.Length,
            PrimitiveCount = lodMeshes.Sum(mesh => mesh.Groups.Count),
            VertexCount = lodMeshes.Sum(mesh => mesh.VertexCount),
            TriangleCount = lodMeshes.Sum(mesh => mesh.TriangleCount),
            RuntimeRangeSemantics = lodIndex switch
            {
                0 => "shared_ofs/common_size plus lod_0_ofs/lod_0_size",
                1 => "shared_ofs/lod_1_size",
                _ => "lod_2_ofs/lod_2_size"
            }
        };
    }

    private static object BuildChunkNodeExtras(TfragChunkLodMesh mesh)
    {
        return new
        {
            mesh.Chunk.Index,
            mesh.LodIndex,
            mesh.VertexCount,
            mesh.TriangleCount,
            TextureIds = mesh.TextureIds,
            TextureAssignment = "DrawControlTextureSlot"
        };
    }

    private static object BuildChunkLodMeshExtras(TfragChunkLodMesh mesh)
    {
        return new
        {
            Chunk = BuildChunkExtras(mesh.Chunk),
            mesh.LodIndex,
            RuntimeSegments = mesh.Segments.Select(BuildSegmentExtras).ToArray(),
            PositionPackets = mesh.PositionPackets.Select(BuildPositionPacketExtras).ToArray(),
            SetupPackets = mesh.SetupPackets.Select(BuildSetupPacketExtras).ToArray(),
            VertexReferencePackets = mesh.VertexReferencePackets.Select(BuildPositionPacketExtras).ToArray(),
            TopologyPackets = mesh.TopologyPackets.Select(BuildTopologyPacketExtras).ToArray(),
            TopologyDecodes = mesh.TopologyDecodes.Select(BuildTopologyDecodeExtras).ToArray(),
            mesh.VertexCount,
            mesh.TriangleCount,
            TextureIds = mesh.TextureIds,
            TextureAssignment = "DrawControlTextureSlot"
        };
    }

    private static object BuildPrimitiveExtras(TfragPrimitiveGroup group)
    {
        return new
        {
            TfragTextureId = group.TextureId,
            group.ClampU,
            group.ClampV,
            group.TopologyPacket.SegmentName,
            TopologyPacketOffset = $"0x{group.TopologyPacket.Offset:X}",
            TopologyPacketRelativeOffset = $"0x{group.TopologyPacket.RelativeOffset:X}",
            group.TopologyPacket.RowCount,
            VertexCount = group.Positions.Count,
            VertexColorCount = group.Colors.Count,
            TriangleCount = group.Indices.Count / 3,
            TextureAssignment = group.MaterialRange.TextureSlot >= 0
                ? "DrawControlTextureSlot"
                : "FallbackSequentialTfragTextureEntry",
            group.MaterialRange.TextureSlot,
            MaterialRangeStartIndex = group.MaterialRange.StartIndex,
            MaterialRangeIndexCount = group.MaterialRange.IndexCount,
            Decode = BuildTopologyDecodeExtras(group.TopologyDecode)
        };
    }

    private static object BuildHeaderExtras(TfragTerrain terrain)
    {
        return new
        {
            terrain.ByteLength,
            TfragTableOffset = $"0x{terrain.TfragTableOffset:X}",
            terrain.TfragCount,
            terrain.TfragRadius,
            terrain.TotalTfragCount
        };
    }

    private static object BuildGeometryExtras(TfragDecodedTerrain decoded)
    {
        return new
        {
            MeshCount = decoded.Meshes.Count,
            PrimitiveCount = decoded.Meshes.Sum(mesh => mesh.Groups.Count),
            VertexCount = decoded.Meshes.Sum(mesh => mesh.VertexCount),
            VertexColorCount = decoded.Meshes.Sum(mesh => mesh.Groups.Sum(group => group.Colors.Count)),
            TriangleCount = decoded.Meshes.Sum(mesh => mesh.TriangleCount),
            TextureIds = decoded.TextureIds,
            LodTriangleCounts = decoded.Meshes
                .GroupBy(mesh => mesh.LodIndex)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(), group => group.Sum(mesh => mesh.TriangleCount)),
            LodChunkCounts = decoded.Meshes
                .GroupBy(mesh => mesh.LodIndex)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key.ToString(), group => group.Count())
        };
    }

    private static object BuildChunkExtras(TfragChunk chunk)
    {
        return new
        {
            chunk.Index,
            RecordOffset = $"0x{chunk.RecordOffset:X}",
            DataOffsetRaw = $"0x{chunk.DataOffsetRaw:X}",
            DataOffset = $"0x{chunk.DataOffset:X}",
            DataLength = $"0x{chunk.DataLength:X}",
            BoundingSphere = new
            {
                chunk.BoundingSphere.X,
                chunk.BoundingSphere.Y,
                chunk.BoundingSphere.Z,
                chunk.BoundingSphere.Radius
            },
            Offsets = new
            {
                Lod2 = $"0x{chunk.Lod2Offset:X}",
                Shared = $"0x{chunk.SharedOffset:X}",
                Lod1 = $"0x{chunk.Lod1Offset:X}",
                Lod0 = $"0x{chunk.Lod0Offset:X}",
                Texture = $"0x{chunk.TextureOffset:X}",
                Rgba = $"0x{chunk.RgbaOffset:X}"
            },
            QwordSizes = new
            {
                chunk.CommonSize,
                chunk.Lod2Size,
                chunk.Lod1Size,
                chunk.Lod0Size
            },
            RgbaCounts = new
            {
                chunk.Lod2RgbaCount,
                chunk.Lod1RgbaCount,
                chunk.Lod0RgbaCount,
                chunk.RgbaSize,
                chunk.RgbaVerticesLocation,
                DecodedRgbaEntries = chunk.RgbaEntries.Count
            },
            chunk.BaseOnly,
            chunk.TextureCount,
            chunk.MSphereCount,
            Flags = $"0x{chunk.Flags:X2}",
            chunk.VertexCount,
            chunk.TriangleCount,
            chunk.MipDistance,
            Textures = chunk.TextureEntries.Select(entry => new
            {
                entry.Index,
                Offset = $"0x{entry.Offset:X}",
                entry.TextureId,
                entry.ClampU,
                entry.ClampV
            }).ToArray()
        };
    }

    private static object BuildSegmentExtras(TfragLodSegment segment)
    {
        return new
        {
            segment.Name,
            Offset = $"0x{segment.Offset:X}",
            RelativeOffset = $"0x{segment.RelativeOffset:X}",
            ExpectedLength = $"0x{segment.ExpectedLength:X}",
            Length = $"0x{segment.Length:X}",
            segment.Truncated
        };
    }

    private static object BuildPositionPacketExtras(TfragPositionPacket packet)
    {
        return new
        {
            packet.SegmentName,
            Offset = $"0x{packet.Offset:X}",
            RelativeOffset = $"0x{packet.RelativeOffset:X}",
            Immediate = $"0x{packet.Immediate:X4}",
            packet.Address,
            packet.RowCount,
            packet.UsesVifBase,
            VifBase = packet.UsesVifBase
                ? new[] { packet.BaseX, packet.BaseY, packet.BaseZ, packet.BaseW }
                : null
        };
    }

    private static object BuildSetupPacketExtras(TfragPositionPacket packet)
    {
        return new
        {
            packet.SegmentName,
            Offset = $"0x{packet.Offset:X}",
            RelativeOffset = $"0x{packet.RelativeOffset:X}",
            Immediate = $"0x{packet.Immediate:X4}",
            packet.Address,
            packet.RowCount,
            packet.UsesVifBase,
            VifBase = packet.UsesVifBase
                ? new[] { packet.BaseX, packet.BaseY, packet.BaseZ, packet.BaseW }
                : null,
            Rows = packet.Positions
                .Select(position => new[] { position.X, position.Y, position.Z, position.W })
                .ToArray()
        };
    }

    private static object BuildTopologyPacketExtras(TfragTopologyPacket packet)
    {
        return new
        {
            packet.SegmentName,
            Offset = $"0x{packet.Offset:X}",
            RelativeOffset = $"0x{packet.RelativeOffset:X}",
            Immediate = $"0x{packet.Immediate:X4}",
            packet.Address,
            packet.RowCount,
            packet.UsesVifBase,
            VifBase = packet.UsesVifBase
                ? new[] { packet.BaseX, packet.BaseY, packet.BaseZ, packet.BaseW }
                : null,
            PayloadLength = packet.Payload.Length
        };
    }

    private static object BuildTopologyDecodeExtras(TfragTopologyDecode decode)
    {
        return new
        {
            TopologyPacketOffset = $"0x{decode.Packet.Offset:X}",
            decode.DecodeMode,
            TriangleCount = decode.Indices.Count / 3,
            decode.RawTriangleCount,
            decode.RejectedDegenerateTriangleCount,
            decode.RejectedInvalidTriangleCount,
            decode.RejectedLongEdgeTriangleCount,
            decode.RejectedDuplicateTriangleCount,
            decode.AlternateDiagonalRowCount,
            ReferenceAddressCount = decode.ReferenceAddresses.Count,
            MaterialRangeCount = decode.MaterialRanges.Count,
            TextureSlots = decode.MaterialRanges
                .Select(range => range.TextureSlot)
                .Distinct()
                .OrderBy(slot => slot)
                .ToArray()
        };
    }

    private static byte[] BuildDiagnostics(
        TfragTerrain terrain,
        TfragDecodedTerrain decoded,
        TfragGltfExportOptions options,
        JsonSerializerOptions jsonOptions)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = $"{NormalizeLabel(options.GameLabel)} tfrag terrain",
            Header = BuildHeaderExtras(terrain),
            Geometry = BuildGeometryExtras(decoded),
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription,
            WorldPositionScale = options.WorldPositionScale,
            LocalPositionScale = options.LocalPositionScale,
            TopologyPayloadPrefixBytes = options.TopologyPayloadPrefixBytes,
            MaxTriangleEdgeLength = options.MaxTriangleEdgeLength,
            LodSemantics = "Wrench-style recovery: lod2 topology=(lod_2_ofs,shared_ofs), common=(shared_ofs,lod_1_ofs), lod1 topology=(lod_1_ofs,lod_0_ofs), lod01=(lod_0_ofs,shared_ofs+lod_1_size*0x10), lod0 topology=(shared_ofs+lod_1_size*0x10,rgba_ofs).",
            DecodePasses = decoded.Decodes.Select(decode => new
            {
                ChunkIndex = decode.Chunk.Index,
                decode.LodIndex,
                RuntimeSegments = decode.Segments.Select(BuildSegmentExtras).ToArray(),
                SetupPackets = decode.SetupPackets.Select(BuildSetupPacketExtras).ToArray(),
                PositionPackets = decode.PositionPackets.Select(BuildPositionPacketExtras).ToArray(),
                VertexReferencePackets = decode.VertexReferencePackets.Select(BuildPositionPacketExtras).ToArray(),
                TopologyPackets = decode.TopologyPackets.Select(BuildTopologyPacketExtras).ToArray(),
                TopologyDecodes = decode.TopologyDecodes.Select(BuildTopologyDecodeExtras).ToArray(),
                Exported = decode.Mesh is not null,
                VertexCount = decode.Mesh?.VertexCount ?? 0,
                TriangleCount = decode.Mesh?.TriangleCount ?? 0
            }).ToArray(),
            Textures = decoded.TextureIds.Select(textureId => BuildMaterialExtras(textureId, options)).ToArray()
        }, jsonOptions);
    }

    private static string NormalizeLabel(string? label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? "Tfrag"
            : label.Trim().ToUpperInvariant();
    }
}

using System.Numerics;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    public const string LightSelectorAttributeName = "_DL_LIGHT_SELECTOR";
    public const string LightBaseColorAttributeName = "_DL_LIGHT_BASE_COLOR";
    public const string LightNormalAttributeName = "_DL_LIGHT_NORMAL";
    public const string LightPostScaleAttributeName = "_DL_LIGHT_POST_SCALE";

    private sealed record TfragDecodedTerrain(
        IReadOnlyList<TfragChunkLodMesh> Meshes,
        IReadOnlyList<TfragChunkLodDecode> Decodes)
    {
        public IReadOnlyList<int> TextureIds => Meshes
            .SelectMany(mesh => mesh.TextureIds)
            .Distinct()
            .OrderBy(textureId => textureId)
            .ToArray();

        public IReadOnlyList<TfragMaterialKey> MaterialKeys => Meshes
            .SelectMany(mesh => mesh.MaterialKeys)
            .Distinct()
            .OrderBy(key => key.TextureId)
            .ThenBy(key => key.ClampU)
            .ThenBy(key => key.ClampV)
            .ToArray();
    }

    private sealed record TfragChunkLodDecode(
        TfragChunk Chunk,
        int LodIndex,
        IReadOnlyList<TfragLodSegment> Segments,
        IReadOnlyList<TfragPositionPacket> SetupPackets,
        IReadOnlyList<TfragPositionPacket> PositionPackets,
        IReadOnlyList<TfragPositionPacket> VertexReferencePackets,
        IReadOnlyList<TfragTopologyPacket> TopologyPackets,
        IReadOnlyList<TfragTopologyDecode> TopologyDecodes,
        TfragChunkLodMesh? Mesh);

    private sealed record TfragChunkLodMesh(
        TfragChunk Chunk,
        int LodIndex,
        IReadOnlyList<TfragLodSegment> Segments,
        IReadOnlyList<TfragPositionPacket> SetupPackets,
        IReadOnlyList<TfragPositionPacket> PositionPackets,
        IReadOnlyList<TfragPositionPacket> VertexReferencePackets,
        IReadOnlyList<TfragTopologyPacket> TopologyPackets,
        IReadOnlyList<TfragTopologyDecode> TopologyDecodes,
        IReadOnlyList<TfragPrimitiveGroup> Groups)
    {
        public int VertexCount => Groups.Sum(group => group.Positions.Count);

        public int TriangleCount => Groups.Sum(group => group.Indices.Count / 3);

        public IReadOnlyList<int> TextureIds => Groups
            .Select(group => group.TextureId)
            .Distinct()
            .OrderBy(textureId => textureId)
            .ToArray();

        public IReadOnlyList<TfragMaterialKey> MaterialKeys => Groups
            .Select(group => group.MaterialKey)
            .Distinct()
            .OrderBy(key => key.TextureId)
            .ThenBy(key => key.ClampU)
            .ThenBy(key => key.ClampV)
            .ToArray();
    }

    private sealed class TfragPrimitiveGroup
    {
        public TfragPrimitiveGroup(
            TfragMaterialKey materialKey,
            TfragTopologyPacket topologyPacket,
            TfragTopologyDecode topologyDecode,
            TfragMaterialRange materialRange,
            TfragNormalBuildResult normalBuildResult)
        {
            MaterialKey = materialKey;
            TopologyPacket = topologyPacket;
            TopologyDecode = topologyDecode;
            MaterialRange = materialRange;
            NormalBuildResult = normalBuildResult;
        }

        public TfragMaterialKey MaterialKey { get; }

        public int TextureId => MaterialKey.TextureId;

        public bool ClampU => MaterialKey.ClampU;

        public bool ClampV => MaterialKey.ClampV;

        public TfragTopologyPacket TopologyPacket { get; }

        public TfragTopologyDecode TopologyDecode { get; }

        public TfragMaterialRange MaterialRange { get; }

        public TfragNormalBuildResult NormalBuildResult { get; }

        public int WindingCorrectedTriangleCount { get; set; }

        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> TexCoords { get; } = [];

        public List<Vector4> Colors { get; } = [];

        public List<float> LightSelectors { get; } = [];

        public List<Vector4> LightBaseColors { get; } = [];

        public List<Vector3> LightNormals { get; } = [];

        public List<float> LightPostScales { get; } = [];

        public List<uint> Indices { get; } = [];
    }

    private sealed record TfragMaterialBuildResult(
        List<Dictionary<string, object?>> Materials,
        Dictionary<TfragMaterialKey, int> MaterialIndexByKey,
        IReadOnlyList<int> TextureIds,
        IReadOnlyList<object> Samplers,
        IReadOnlyList<object> Images,
        IReadOnlyList<object> Textures);

    private readonly record struct TfragResolvedTexture(int TextureId, bool ClampU, bool ClampV)
    {
        public static TfragResolvedTexture Untextured { get; } = new(TextureId: -1, ClampU: false, ClampV: false);
    }

    private readonly record struct TfragMaterialKey(int TextureId, bool ClampU, bool ClampV);

    private readonly record struct TfragTextureWrapMode(bool ClampU, bool ClampV);

    private readonly record struct TfragPrimitiveVertexKey(
        int SourceIndex,
        int ReferenceAddress,
        int NormalX,
        int NormalY,
        int NormalZ)
    {
        public static TfragPrimitiveVertexKey From(uint sourceIndex, int referenceAddress, Vector3 normal)
        {
            const float scale = 1000000f;
            return new TfragPrimitiveVertexKey(
                checked((int)sourceIndex),
                referenceAddress,
                (int)MathF.Round(normal.X * scale),
                (int)MathF.Round(normal.Y * scale),
                (int)MathF.Round(normal.Z * scale));
        }
    }

    private readonly record struct TfragExpandedVertexKey(
        int SourceIndex,
        int U,
        int V,
        int NormalX,
        int NormalY,
        int NormalZ)
    {
        public static TfragExpandedVertexKey From(int sourceIndex, Vector2 texCoord, Vector3 normal)
        {
            const float scale = 1000000f;
            return new TfragExpandedVertexKey(
                sourceIndex,
                (int)MathF.Round(texCoord.X * scale),
                (int)MathF.Round(texCoord.Y * scale),
                (int)MathF.Round(normal.X * scale),
                (int)MathF.Round(normal.Y * scale),
                (int)MathF.Round(normal.Z * scale));
        }
    }

    private readonly record struct TfragInterpretedAlpha(
        bool HasOpacityAlpha,
        TextureAlphaMode AlphaMode,
        string? GltfAlphaMode,
        bool UsesBinaryAlpha);

    private sealed record TfragLodSegment(
        string Name,
        int Offset,
        int RelativeOffset,
        int ExpectedLength,
        int Length,
        bool Truncated);

    private sealed record TfragLodRecoveryLayout(
        IReadOnlyList<TfragLodSegment> ScanSegments,
        TfragLodSegment TopologySegment,
        TfragStripIndexOrder StripIndexOrder);

    private enum TfragStripIndexOrder
    {
        StripsThenIndices,
        IndicesThenStrips
    }

    private readonly record struct TfragVifState(
        int Cycle,
        int Mode,
        int RowX,
        int RowY,
        int RowZ,
        int RowW)
    {
        public static TfragVifState Default { get; } = new(0, 0, 0, 0, 0, 0);
    }

    private sealed class TfragReferenceState
    {
        public Vector2?[] TexCoords { get; } = new Vector2?[1024];

        public Vector2?[] SnapshotTexCoords()
        {
            return (Vector2?[])TexCoords.Clone();
        }
    }

    private readonly record struct TfragSourcePosition(
        short X,
        short Y,
        short Z,
        short W,
        bool HasVifBase,
        int BaseX,
        int BaseY,
        int BaseZ,
        int BaseW,
        int PacketOffset,
        int RowIndex);

    private sealed record TfragPositionPacket(
        string SegmentName,
        int Offset,
        int RelativeOffset,
        ushort Immediate,
        int Address,
        int RowCount,
        bool UsesVifBase,
        int BaseX,
        int BaseY,
        int BaseZ,
        int BaseW,
        IReadOnlyList<TfragSourcePosition> Positions);

    private sealed record TfragTopologyPacket(
        string SegmentName,
        int Offset,
        int RelativeOffset,
        ushort Immediate,
        int Address,
        int RowCount,
        bool UsesVifBase,
        int BaseX,
        int BaseY,
        int BaseZ,
        int BaseW,
        byte[] Payload,
        IReadOnlyList<Vector2?> ReferenceTexCoords);

    private sealed record TfragRawUnpackPacket(
        string SegmentName,
        int Offset,
        int RelativeOffset,
        ushort Immediate,
        int Address,
        int RowCount,
        int Command,
        byte[] Payload);

    private readonly record struct TfragVertexInfoRow(
        int ReferenceAddress,
        int SourceIndex,
        Vector2 TexCoord);

    private sealed record TfragControlStripPlans(
        IReadOnlyDictionary<int, TfragTopologyPacket> Targets,
        IReadOnlySet<int> ControlPacketIndices);

    private readonly record struct TfragDrawControlStrip(
        int VertexCount,
        int TextureSlot);

    private readonly record struct TfragTopologyVertex(
        uint SourceIndex,
        int ReferenceAddress);

    private readonly record struct TfragMaterialRange(
        int StartIndex,
        int IndexCount,
        int TextureSlot);

    private sealed record TfragTopologyDecode(
        TfragTopologyPacket Packet,
        string DecodeMode,
        IReadOnlyList<uint> Indices,
        IReadOnlyList<int> ReferenceAddresses,
        IReadOnlyList<TfragMaterialRange> MaterialRanges,
        int RawTriangleCount,
        int RejectedDegenerateTriangleCount,
        int RejectedInvalidTriangleCount,
        int RejectedDuplicateTriangleCount,
        int RejectedLongEdgeTriangleCount,
        int AlternateDiagonalRowCount);
}

using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private sealed record ImportedGltf(
        List<ImportedMesh> Meshes,
        IReadOnlyList<CustomStaticSourceMesh>? CustomStaticSourceMeshes = null,
        int? OriginalTemplateMeshCount = null);

    private sealed class ImportedMesh(
        int templateMeshIndex,
        MobyMeshType meshType,
        List<Vector3> positions,
        List<uint> indices,
        List<Vector2>? texCoords,
        List<ushort[]>? joints,
        List<float[]>? weights,
        ImportedMeshMetadata? metadata)
    {
        public int TemplateMeshIndex { get; } = templateMeshIndex;
        public MobyMeshType MeshType { get; set; } = meshType;
        public List<Vector3> Positions { get; } = positions;
        public List<uint> Indices { get; } = indices;
        public List<Vector2>? TexCoords { get; } = texCoords;
        public List<ushort[]>? Joints { get; set; } = joints;
        public List<float[]>? Weights { get; set; } = weights;
        public ImportedMeshMetadata? Metadata { get; } = metadata;
        public bool CustomStaticHideMesh { get; set; }
        public int? CustomStaticSourceMeshIndex { get; set; }
        public int? CustomStaticSourcePrimitiveIndex { get; set; }
        public int? CustomStaticSourceMaterialIndex { get; set; }
        public string? CustomStaticSourceMaterialName { get; set; }
        public Vector2? CustomStaticAppliedUvScale { get; set; }
        public Dictionary<int, Vector3>? RigBindWorldPositions { get; set; }
        public Dictionary<int, Matrix4x4>? RigBindWorldToLocalTransforms { get; set; }
        public int? CustomStaticSourceStartTriangle { get; set; }
        public int? CustomStaticSourceTriangleCount { get; set; }
        public List<int>? CustomStaticSourceTriangleIndices { get; set; }
        public ushort? CustomStaticForcedSkinJoint { get; set; }
        public List<SkinTransferVertexDiagnostics> SkinTransferDiagnostics { get; } = [];
    }

    private sealed record CustomStaticSourceMesh(
        int MeshIndex,
        int PrimitiveIndex,
        int? MaterialIndex,
        string? MaterialName,
        Vector2? AppliedUvScale,
        int ClampedUvComponentCount,
        int? OriginalOrder,
        int? SplitOrder,
        List<Vector3> Positions,
        List<uint> Indices,
        List<int>? SourceTriangleIndices,
        List<Vector2>? TexCoords,
        List<ushort[]>? Joints,
        List<float[]>? Weights,
        ushort? ForcedSkinJoint = null);

    private sealed record CustomStaticChunk(
        List<Vector3> Positions,
        List<uint> Indices,
        List<Vector2>? TexCoords,
        List<ushort[]>? Joints,
        List<float[]>? Weights,
        int StartTriangleOffset,
        int NextTriangleOffset,
        int SourceTriangleCount,
        List<int>? SourceTriangleIndices);

    private sealed record ImportedMeshMetadata(
        string Kind,
        int Version,
        ImportedTopologyMetadata? Topology,
        ImportedVertexLayoutMetadata? VertexLayout);

    private sealed record ImportedTopologyMetadata(
        int Offset,
        int Immediate,
        int CommandByte,
        int VifDataSplitOffset,
        string PayloadBase64,
        byte[] AlignedPayloadBytes,
        byte[] PayloadPaddingBytes,
        List<int> PayloadBytes,
        List<int> PayloadPrefixBytes,
        List<ImportedTopologyPayloadToken> PayloadTokens,
        byte[] BeforePacketBytes,
        byte[] AfterPacketBytes);

    private sealed record ImportedTopologyPayloadToken(string Kind, bool Negative, int VertexIndex);

    private sealed record ImportedVertexLayoutMetadata(
        bool Supported,
        int MatrixTransferCount,
        int TwoWayBlendVertexCount,
        int ThreeWayBlendVertexCount,
        int MainVertexCount,
        int DuplicateVertexCount,
        int VertexTableOffset,
        int DuplicateIndicesOffset,
        int EpilogueVertexCount,
        byte[] HeaderBytes,
        byte[] EpilogueBytes,
        List<ImportedMatrixTransferMetadata> MatrixTransfers,
        List<int> DuplicateIndices,
        List<int> Low9StorageValues,
        byte[] RowPrefixBytes);

    private sealed record ImportedMatrixTransferMetadata(int Joint, int Vu0DestinationAddress);

    private sealed record SourceVertex(int OriginalIndex, Vector3 Position, List<MobySkinInfluence> Influences);

    private sealed record ReferenceSkinSample(
        Vector3 Position,
        ushort[] Joints,
        float[] Weights,
        int SourceMeshIndex,
        int SourceVertexIndex,
        ushort PrimaryJoint);

    private sealed record SkinTransferResult(
        ushort[] Joints,
        float[] Weights,
        SkinTransferVertexDiagnostics Diagnostics);

    private sealed record SkinTransferVertexDiagnostics(
        Vector3 Position,
        ushort PrimaryJoint,
        ushort NearestSamplePrimaryJoint,
        float NearestSampleDistance,
        float SecondSampleDistance,
        float Confidence,
        int CandidateCount,
        int NearestSampleMeshIndex,
        int NearestSampleVertexIndex,
        Vector3 NearestSamplePosition);

    internal sealed record TemplateDecodedMesh(List<Vector3> Positions, List<ushort[]> Joints, List<float[]> Weights);

    internal readonly record struct TemplateSkinBlend(byte Count, sbyte Joint0, sbyte Joint1, sbyte Joint2, byte Weight0, byte Weight1, byte Weight2);

    private sealed record TopologyShapeSummary(
        int SegmentCount,
        int EffectiveTokenCount,
        int[] SegmentTokenLengths,
        int[] SegmentNegativeTokenCounts,
        int[] SegmentMidStripNegativeTokenCounts);

    private readonly record struct TopologyTraceToken(bool IsNonPositive, int VertexIndex, bool IsZeroMarker);

    private sealed record TopologyTraceSummary(
        int SegmentCount,
        int RawTriangleCount,
        int UniqueTriangleCount,
        int DegenerateTriangleCount,
        int DuplicateTriangleCount,
        int MidStripControlCount,
        TopologyTraceSegment[] Segments,
        uint[] UniqueTriangleIndices);

    private sealed record TopologyZeroMarkerSummary(int Count, int[] TokenIndices);

    private sealed record TopologyRowUsageSummary(
        int ResolvedTokenCount,
        int? MinVertexIndex,
        int? MaxVertexIndex,
        int UniqueVertexIndexCount,
        int NonPositiveTokenCount,
        int ZeroMarkerCount,
        int[] FirstResolvedVertexIndices,
        int[] UniqueVertexIndexSamples);

    private sealed record TopologySourceDiff(
        int TemplateUniqueTriangleCount,
        int SourceTriangleCount,
        int GeneratedDecodedTriangleCount,
        int TemplateOnlyTriangleCount,
        int SourceOnlyTriangleCount,
        int GeneratedOnlyTriangleCount,
        int SourceMissingFromGeneratedTriangleCount,
        TopologyVertexRange? TemplateVertexRange,
        TopologyVertexRange? SourceVertexRange,
        TopologyVertexRange? GeneratedVertexRange,
        string[] TemplateOnlyTriangleSamples,
        string[] SourceOnlyTriangleSamples,
        string[] GeneratedOnlyTriangleSamples,
        string[] SourceMissingFromGeneratedTriangleSamples);

    private sealed record TopologyPayloadDiff(
        int TemplatePayloadBytes,
        int GeneratedPayloadBytes,
        int DifferingByteCount,
        TopologyPayloadByteDiff[] FirstDiffs);

    private sealed record TopologyPayloadByteDiff(int Offset, byte? Template, byte? Generated);

    private sealed record TopologyVertexRange(uint Min, uint Max, int UniqueCount);

    private sealed record TopologyTraceSegment(
        int SegmentIndex,
        int TokenStart,
        int TokenCount,
        int RawTriangleCount,
        int UniqueTriangleCount,
        int DegenerateTriangleCount,
        int DuplicateTriangleCount,
        int MidStripControlCount,
        TopologyTraceControlEvent[] ControlEvents);

    private sealed record TopologyTraceControlEvent(
        int TokenIndex,
        int SegmentIndex,
        int SegmentTokenIndex,
        bool NonPositive,
        int VertexIndex,
        string Action,
        int StripVertexCount,
        TopologyTraceEmittedTriangle[] EmittedTriangles);

    private sealed record TopologyTraceEmittedTriangle(
        uint[] Indices,
        bool Flipped,
        bool IsDegenerate,
        bool IsDuplicate,
        int? UniqueTriangleIndex);

    private sealed record VertexBuildResult(
        byte[] VertexData,
        int[] IndexByOriginalIndex,
        bool UsedTemplateVertexData,
        bool UsedMetadataVertexLayout,
        bool UsedMetadataRowPrefixes,
        bool UsedMetadataLowVertexBits,
        int? PreservedTemplateLow9MaxValue = null);

    private sealed record VifBuildResult(
        byte[] VifData,
        byte[]? VifTextureData,
        int ConnectorIndexCount,
        bool PreservedTemplateLayout,
        bool ExpandedTopologyPacket,
        int OriginalTopologyPayloadBytes,
        int NewTopologyPayloadBytes,
        bool ReusedTemplateTopology,
        bool RemappedTemplateTopology,
        bool UsedMetadataTopologyLayout,
        bool GeneratedTopologyFromGltf,
        int GeneratedTopologyTokenCount,
        int GeneratedTopologySourceTriangleCount,
        bool GeneratedTopologyPayloadFitsMetadata,
        bool GeneratedTopologyMatchesSourceTriangles,
        bool GeneratedTopologyPreservesTemplateControlMarkers,
        bool GeneratedTopologyMatchesTemplateControlShape,
        int TemplateTopologyRestartCount,
        int GeneratedTopologyRestartCount,
        int TemplateTopologyNegativeTokenCount,
        int GeneratedTopologyNegativeTokenCount,
        TopologyShapeSummary? TemplateTopologyShape,
        TopologyShapeSummary? GeneratedTopologyShape,
        TopologyTraceSummary? TemplateTopologyTrace = null,
        TopologyTraceSummary? GeneratedTopologyTrace = null,
        TopologyRowUsageSummary? GeneratedTopologyRowUsage = null,
        TopologyZeroMarkerSummary? TemplateTopologyZeroMarkers = null,
        TopologyZeroMarkerSummary? GeneratedTopologyZeroMarkers = null,
        TopologySourceDiff? TopologySourceDiff = null,
        TopologyPayloadDiff? TopologyPayloadDiff = null,
        int? CompactTopologyTextureOverlapBytes = null);

    private sealed record CompactTopologyLayout(
        int PayloadLength,
        int TextureOverlapBytes);

    private sealed record MeshReplacement(
        byte[] VertexData,
        byte[] VifData,
        byte[]? VifTextureData,
        int QuantizationClipCount,
        int TruncatedInfluenceCount,
        int TopologyConnectorIndexCount,
        bool UsedTemplateVertexData,
        bool UsedMetadataVertexLayout,
        bool UsedMetadataRowPrefixes,
        bool UsedMetadataLowVertexBits,
        int? PreservedTemplateLow9MaxValue,
        bool UsedMetadataTopologyLayout,
        bool PreservedTemplateVifLayout,
        bool ExpandedTopologyVifPacket,
        int OriginalTopologyPayloadBytes,
        int NewTopologyPayloadBytes,
        bool ReusedTemplateTopology,
        bool RemappedTemplateTopology,
        bool GeneratedTopologyFromGltf,
        int GeneratedTopologyTokenCount,
        int GeneratedTopologySourceTriangleCount,
        bool GeneratedTopologyPayloadFitsMetadata,
        bool GeneratedTopologyMatchesSourceTriangles,
        bool GeneratedTopologyPreservesTemplateControlMarkers,
        bool GeneratedTopologyMatchesTemplateControlShape,
        int TemplateTopologyRestartCount,
        int GeneratedTopologyRestartCount,
        int TemplateTopologyNegativeTokenCount,
        int GeneratedTopologyNegativeTokenCount,
        TopologyShapeSummary? TemplateTopologyShape,
        TopologyShapeSummary? GeneratedTopologyShape,
        TopologyTraceSummary? TemplateTopologyTrace,
        TopologyTraceSummary? GeneratedTopologyTrace,
        TopologyRowUsageSummary? GeneratedTopologyRowUsage,
        TopologyZeroMarkerSummary? TemplateTopologyZeroMarkers,
        TopologyZeroMarkerSummary? GeneratedTopologyZeroMarkers,
        TopologySourceDiff? TopologySourceDiff,
        TopologyPayloadDiff? TopologyPayloadDiff,
        int? CompactTopologyTextureOverlapBytes,
        bool WroteTexCoords,
        int TexCoordWriteCount,
        int TexCoordPaddingWriteCount);
}

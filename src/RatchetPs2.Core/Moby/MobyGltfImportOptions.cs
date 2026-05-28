using System.Numerics;

namespace RatchetPs2.Core.Moby;

public sealed class MobyGltfImportOptions
{
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
    public int MaxInfluences { get; init; } = 3;
    public float ScaleTolerance { get; init; } = 0.0001f;
    public bool IncludeDiagnostics { get; init; } = true;
    public MobyGltfImportPacketMode PacketMode { get; init; } = MobyGltfImportPacketMode.Auto;
    public IReadOnlySet<int>? PacketModeMeshIndices { get; init; }
    public bool CustomStatic { get; init; }
    public bool CustomStaticUseGeneratedContainer { get; init; }
    public int CustomStaticReplaceMeshIndex { get; init; }
    public float CustomStaticScale { get; init; } = 1f;
    public float CustomStaticYawDegrees { get; init; }
    public float CustomStaticPitchDegrees { get; init; }
    public float CustomStaticRollDegrees { get; init; }
    public float CustomStaticPostSkinYawDegrees { get; init; }
    public bool CustomStaticSplitMeshes { get; init; }
    public bool CustomStaticSplitConnectedComponents { get; init; }
    public int CustomStaticSplitConnectedComponentMinTriangles { get; init; }
    public bool CustomStaticSplitAnatomicalRegions { get; init; }
    public string? CustomStaticSplitSideAxis { get; init; }
    public float CustomStaticSplitSideDeadzoneRatio { get; init; } = 0.02f;
    public bool CustomStaticExpandTemplateMeshes { get; init; }
    public bool CustomStaticUseOnlyReplaceMeshAsTemplate { get; init; }
    public bool CustomStaticUseMinimalExpandedMeshSlots { get; init; } = true;
    public bool CustomStaticGenerateMeshSlots { get; init; }
    public bool CustomStaticGenerateMeshTable { get; init; } = true;
    public int CustomStaticGeneratedMeshSlotCapacity { get; init; } = 127;
    public bool CustomStaticGenerateGlobalScaffold { get; init; } = true;
    public bool CustomStaticGenerateHeaderDefaults { get; init; } = true;
    public byte? CustomStaticHeaderLodTrans { get; init; }
    public byte? CustomStaticHeaderMipmapDistance { get; init; }
    public float? CustomStaticTextureMetadataDistance { get; init; }
    public IReadOnlySet<int>? CustomStaticProbeMeshIndices { get; init; }
    public bool CustomStaticSkipUnprobedMeshes { get; init; }
    public float? OutputModelScale { get; init; }
    public bool CustomStaticRecalculateBoundingSphere { get; init; }
    public float CustomStaticBoundingSpherePadding { get; init; } = 8f;
    public bool CustomStaticPreserveTemplatePackets { get; init; }
    public bool CustomStaticPreserveTemplateVertexLayout { get; init; }
    public bool CustomStaticHideOtherMeshes { get; init; }
    public bool CustomStaticDropTemplateAttachments { get; init; }
    public bool CustomStaticDropTemplateNonBodyMeshes { get; init; }
    public bool CustomStaticStripTemplateGameplayData { get; init; }
    public bool CustomStaticDropTemplateCollision { get; init; }
    public bool CustomStaticDropTemplateAnimations { get; init; }
    public bool CustomStaticGenerateDefaultAnimation { get; init; }
    public bool CustomStaticDropTemplateAnimationJoints { get; init; }
    public bool CustomStaticDropTemplateSounds { get; init; }
    public bool CustomStaticDropTemplateShadow { get; init; }
    public bool CustomStaticDropTextures { get; init; }
    public bool CustomStaticConstantTextures { get; init; }
    public bool CustomStaticGenerateTextureMetadata { get; init; } = true;
    public bool CustomStaticUseGeneratedTextureMetadataPrototype { get; init; } = true;
    public bool CustomStaticGenerateMeshEntryMetadata { get; init; } = true;
    public bool CustomStaticGenerateMeshEntryUnknown0A { get; init; }
    public bool CustomStaticGenerateMeshEntryUnknown0ATotalQw { get; init; }
    public bool CustomStaticZeroCommonTransformJoint { get; init; }
    public bool CustomStaticZeroCommonTransformJointHeaderOnly { get; init; }
    public bool CustomStaticUseDominantSkinJointAsCommonTransform { get; init; }
    public bool CustomStaticUseDominantHeadSkinJointAsCommonTransform { get; init; }
    public bool CustomStaticUseReferenceMeshCommonTransform { get; init; }
    public bool CustomStaticGenerateCommonTransforms { get; init; } = true;
    public bool CustomStaticGenerateCommonTransformSkeleton { get; init; }
    public bool CustomStaticApproximateRigSkinning { get; init; }
    public bool CustomStaticApproximateRigSkinningUseSourcePose { get; init; }
    public bool CustomStaticWriteFittedRigCommonTransforms { get; init; }
    public bool CustomStaticSkinPositionsRelativeToBind { get; init; }
    public bool CustomStaticTransferReferenceSkinning { get; init; }
    public int CustomStaticReferenceSkinningSampleCount { get; init; } = 1;
    public float? CustomStaticReferenceSkinningVerticalWindow { get; init; }
    public bool CustomStaticReferenceSkinningSameSide { get; init; }
    public string CustomStaticReferenceSkinningSideAxis { get; init; } = "x";
    public float CustomStaticReferenceSkinningSideDeadzoneRatio { get; init; } = 0.03f;
    public bool CustomStaticReferenceSkinningMaterialRegions { get; init; }
    public bool CustomStaticReferenceSkinningDisableAnatomicalFilters { get; init; }
    public bool CustomStaticReferenceSkinningPreserveLowerBodyFilters { get; init; }
    public bool CustomStaticReferenceSkinningPreserveShoulderFilters { get; init; }
    public float CustomStaticReferenceSkinningShoulderInwardBias { get; init; }
    public bool CustomStaticReferenceSkinningTriangleCoherent { get; init; }
    public bool CustomStaticReferenceSkinningSplitPrimarySeams { get; init; }
    public bool CustomStaticReferenceSkinningRigidMeshCentroid { get; init; }
    public bool CustomStaticReferenceSkinningRigidTriangleCentroid { get; init; }
    public int CustomStaticReferenceSkinningSmoothPrimaryIterations { get; init; }
    public float CustomStaticReferenceSkinningDistancePower { get; init; } = 1f;
    public float CustomStaticReferenceSkinningYawDegrees { get; init; }
    public IReadOnlyDictionary<int, ushort>? CustomStaticForcedSkinJointsByMeshIndex { get; init; }
    public IReadOnlyList<MobyGltfSourceTriangleSkinJoint>? CustomStaticForcedSourceTriangleSkinJoints { get; init; }
    public bool CustomStaticCopyRigAnimation0 { get; init; }
    public int? CustomStaticCopyRigAnimationIndex { get; init; }
    public bool CustomStaticDoubleSided { get; init; }
    public bool CustomStaticPreserveTopologyTail { get; init; }
    public bool CustomStaticCompactTopologyPacket { get; init; } = true;
    public bool CustomStaticStrictTriangleCap { get; init; }
    public bool CustomStaticForceZeroMarkerTopology { get; init; } = true;
    public bool CustomStaticGenerateMinimalVifContainer { get; init; } = true;
    public bool CustomStaticGenerateVifDomainCapacity { get; init; } = true;
    public bool CustomStaticGenerateVertexHeaderDomainCapacity { get; init; } = true;
    public bool CustomStaticGenerateMeshTableVertexCount { get; init; } = true;
    public bool CustomStaticGenerateRigidVertexData { get; init; }
    public bool CustomStaticGenerateRigidRowsInTemplateLayout { get; init; }
    public bool CustomStaticGenerateCompactRigidRows { get; init; } = true;
    public bool CustomStaticGenerateCompactVertexHeader { get; init; } = true;
    public bool CustomStaticPreserveTemplateRowContract { get; init; }
    public bool CustomStaticPadCompactRigidRowsToTemplateSize { get; init; }
    public bool CustomStaticPreserveTemplateMeshVertexCount { get; init; }
    public bool CustomStaticPreserveTemplateVertexHeaderCounts { get; init; }
    public bool CustomStaticRewriteTemplateEpilogueRows { get; init; }
    public bool CustomStaticRewriteTemplateEpiloguePrefixes { get; init; }
    public bool CustomStaticRewriteTemplateEpiloguePositions { get; init; } = true;
    public bool CustomStaticGenerateTemplateEpilogueControlPrefix { get; init; } = true;
    public bool CustomStaticClearTemplateEpilogueFinalMarker { get; init; }
    public bool CustomStaticGenerateTemplateEpilogueFinalMarker { get; init; } = true;
    public bool CustomStaticNeutralizeTemplateSkinning { get; init; }
    public bool CustomStaticFlattenVertexPrefixes { get; init; }
    public IReadOnlyList<byte>? CustomStaticVertexPrefixBytes { get; init; }
    public byte? CustomStaticVertexPrefixShade { get; init; }
    public bool CustomStaticAutoVertexPrefixShade { get; init; } = true;
    public bool CustomStaticPreserveTemplateVertexControlWords { get; init; }
    public bool CustomStaticZeroVertexControlHighBits { get; init; }
    public bool CustomStaticPreserveTemplateVertexControlLowBits { get; init; }
    public int? CustomStaticVertexControlLow9Value { get; init; }
    public bool CustomStaticAutoVertexControlLow9Tail { get; init; } = true;
    public int? CustomStaticVertexControlLow9WarmupZeroCount { get; init; }
    public int? CustomStaticPreserveTemplateSparseLow9Count { get; init; }
    public int? CustomStaticPreserveTemplateLow9MaxValue { get; init; }
    public bool CustomStaticAutoPreserveTemplateLow9MaxValue { get; init; } = true;
    public bool CustomStaticPreserveDuplicateLow9Values { get; init; }
    public bool CustomStaticPreserveLow9UpToMaxDuplicate { get; init; }
    public bool CustomStaticIsolatedTriangleTopology { get; init; }
    public int? CustomStaticMaxTrianglesPerMesh { get; init; }
    public int? CustomStaticMaxGeneratedMeshes { get; init; }
    public int? CustomStaticMaxHighLodMeshes { get; init; }
    public int? CustomStaticInitialTriangleCap { get; init; }
    public int? CustomStaticInitialTriangleCount { get; init; }
    public IReadOnlyDictionary<string, byte>? CustomStaticMaterialTextureIds { get; init; }
    public IReadOnlyDictionary<string, Vector2>? CustomStaticMaterialUvScales { get; init; }
    public bool CustomStaticClampUvs { get; init; }
    public bool CustomStaticSkipTexCoordVifWrite { get; init; }
}

public sealed record MobyGltfSourceTriangleSkinJoint(
    int MeshIndex,
    int PrimitiveIndex,
    IReadOnlySet<int> TriangleIndices,
    ushort Joint);

public enum MobyGltfImportPacketMode
{
    Auto,
    Passthrough,
    GenerateTopology,
    GenerateVertexPositions,
    GenerateVertexDataFromMetadata,
    GenerateTopologyFromMetadataShape,
    GenerateVertexDataWithMetadataShape,
    GenerateVertexData,
    GenerateAll
}

public sealed record MobyGltfImportResult(MobyModel Model, byte[] DiagnosticsBytes);

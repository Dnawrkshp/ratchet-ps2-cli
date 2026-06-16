using System.Numerics;

namespace RatchetPs2.Core.Ties;


internal sealed record TieGltfNormalBuildResult(
    List<Vector3> Normals,
    List<Vector3> IndexNormals,
    IReadOnlyList<int> SourceNormalVertexIndices,
    IReadOnlyList<int> SourceNormalIndexOffsets,
    IReadOnlyList<TieGltfSourceNormalState> SourceNormalVertexStates,
    IReadOnlyList<TieGltfSourceNormalState> SourceNormalIndexStates,
    int SourceNormalVertexCount,
    int PacketRowNormalVertexCount,
    int TableNormalVertexCount,
    int RgbaRecipeNormalVertexCount,
    int RgbaRecipeConstantColorVertexCount,
    int RgbaRecipeDegenerateBlendVertexCount,
    int CrossLodExactNormalVertexCount,
    int DuplicatePositionExactNormalVertexCount,
    string? TableNormalLayout,
    string? TableNormalTargetMode,
    bool TableNormalPreserveSourceOrientation,
    int TableNormalCandidateVertexCount,
    int TableNormalAcceptedVertexCount,
    int TableNormalSignedAcceptedVertexCount,
    int TableNormalInvertedAcceptedVertexCount,
    int TableNormalUpperHemisphereVertexCount,
    int TableNormalUpperHemisphereStrongDownVertexCount,
    string DuplicatePositionNormalWeldMode,
    int DuplicatePositionNormalPairCount,
    int DuplicatePositionIncompatibleNormalPairCount,
    float DuplicatePositionCurrentAverageFaceDot,
    float DuplicatePositionWeldedAverageFaceDot,
    float DuplicatePositionWeldedMinimumFaceDot);

internal readonly record struct TieGltfRgbaRecipeNormalApplyResult(
    int NormalVertexCount,
    int ConstantColorVertexCount,
    int DegenerateBlendVertexCount)
{
    public static TieGltfRgbaRecipeNormalApplyResult Empty { get; } = new(0, 0, 0);
}

internal enum TieGltfRgbaRecipeNormalResolution
{
    Unresolved,
    Normal,
    ConstantColor,
    DegenerateBlend
}

internal enum TieGltfSourceNormalState
{
    Missing = 0,
    TableExact = 1,
    PacketRowExact = 2,
    AmbiguousRemap = 3,
    RejectedRemap = 4,
    CrossLodExact = 5,
    DuplicatePositionExact = 6,
    RgbaRecipeConstantColor = 7,
    RgbaRecipeDegenerateBlend = 8
}

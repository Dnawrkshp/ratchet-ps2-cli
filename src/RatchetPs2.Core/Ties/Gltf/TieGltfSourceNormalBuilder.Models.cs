namespace RatchetPs2.Core.Ties;

internal enum TieGltfVertexNormalRemapTargetMode
{
    VuAddress,
    LogicalVertex,
    PacketDinkyUpload,
    PacketVertexRow
}

internal readonly record struct TieGltfSourceNormalTableLayoutSelection(
    TieGltfRawSourceNormalLayout Layout,
    TieGltfVertexNormalRemapTargetMode TargetMode,
    bool PreserveSourceOrientation,
    TieGltfSourceNormalTableLayoutScore BestScore);

internal readonly record struct TieGltfSourceNormalTableLayoutScore(
    TieGltfRawSourceNormalLayout Layout,
    TieGltfVertexNormalRemapTargetMode TargetMode,
    int CandidateVertexCount,
    int AcceptedVertexCount,
    float DotSum,
    int SignedAcceptedVertexCount,
    float SignedDotSum,
    int InvertedAcceptedVertexCount,
    int UpperHemisphereVertexCount,
    int UpperHemisphereStrongDownVertexCount);

internal readonly record struct TieGltfSourceNormalTableApplyResult(
    int VertexCount,
    TieGltfSourceNormalTableLayoutSelection? Selection)
{
    public bool PreserveSourceOrientation => Selection?.PreserveSourceOrientation ?? false;
}

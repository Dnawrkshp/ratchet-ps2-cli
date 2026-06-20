namespace RatchetPs2.Core.Ties;

internal readonly record struct PacketIndexGroup(
    int PacketIndex,
    int ShaderIndex,
    int MultipassOffset,
    int PassFlags,
    int MultipassUvSize,
    TieRgba32? EnvPassBleedColor,
    IReadOnlyList<int> PacketShaderIndices,
    IReadOnlyList<int> PacketShaderSwitchVuAddresses,
    bool UseGlowEmission,
    List<uint> Indices);

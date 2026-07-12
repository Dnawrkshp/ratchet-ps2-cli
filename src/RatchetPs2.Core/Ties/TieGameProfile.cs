using RatchetPs2.Core.Games;
using RatchetPs2.Core.Textures;

namespace RatchetPs2.Core.Ties;

public sealed record TieGameProfile
{
    public static TieGameProfile Default { get; } = new();

    public string GameLabel { get; init; } = "TIE";
    public string GlowEmissionAttributeName { get; init; } = "_TIE_GLOW_0";
    public string AmbientIndexAttributeName { get; init; } = "_TIE_AMBIENT_INDEX";
    public bool UseAmbientIndexAttribute { get; init; }
    public string SourceNormalAttributeName { get; init; } = "_TIE_SOURCE_NORMAL_PRESENT";
    public string SourceNormalStateAttributeName { get; init; } = "_TIE_SOURCE_NORMAL_STATE";
    public int ReflectiveMaskModeBit { get; init; } = 0x20;
    public int ReflectiveMaskPassFlags { get; init; } = TiePassFlags.ReflectiveMaskPassFlags;
    public float ReflectiveMaskMetallicFactor { get; init; } = 0.37f;
    public float ReflectiveMaskRoughnessFactor { get; init; } = 0.24f;
    public byte FullOpacityAlpha { get; init; } = Ps2Color.FullOpacityAlpha;
    public bool SuppressGeneratedNormalFallback { get; init; }
    public bool UseStripTokenReferencesForTopology { get; init; }
    public bool UsePreviousStripReferencePhaseForTopology { get; init; }
    public bool UseSourceNormalPhaseWindingRepair { get; init; }
    public bool PreferVuAddressSourceNormalRemaps { get; init; }
    public bool InvertDecodedFatVertexSourceNormals { get; init; }
    public bool UsePackedVertexNormalTableSource { get; init; }
    public bool UsePacketRowSourceNormals { get; init; } = true;
    public bool UseRgbaRecipeSourceNormals { get; init; }
    public bool UseGeometryWindingRepair { get; init; } = true;
    public bool UseLocalInwardGeometryWindingRepair { get; init; }
    public bool UsePartialSmallStripSourceNormalPhaseWindingRepair { get; init; }
    public int VertexNormalHeaderSize { get; init; } = 0x10;
    public float SourceNormalPhaseWindingRepairAverageDot { get; init; } = 0.72f;

    public static TieGameProfile ForGame(GameId gameId)
    {
        return Default.WithGameLabel(gameId.ToString());
    }

    public TieGameProfile WithGameLabel(string? gameLabel)
    {
        var normalized = NormalizeGameLabel(gameLabel);
        return this with
        {
            GameLabel = normalized,
            SuppressGeneratedNormalFallback = normalized == "GC",
            UseStripTokenReferencesForTopology = false,
            UsePreviousStripReferencePhaseForTopology = normalized == "GC",
            UseSourceNormalPhaseWindingRepair = normalized is "GC" or "UYA",
            PreferVuAddressSourceNormalRemaps = normalized is "DL" or "UYA",
            InvertDecodedFatVertexSourceNormals = normalized is "DL" or "UYA",
            UsePackedVertexNormalTableSource = normalized is "DL" or "UYA",
            UsePacketRowSourceNormals = normalized == "UYA",
            UseRgbaRecipeSourceNormals = normalized == "UYA",
            UseAmbientIndexAttribute = normalized is "DL" or "UYA",
            UseGeometryWindingRepair = normalized != "GC",
            UseLocalInwardGeometryWindingRepair = normalized == "UYA",
            UsePartialSmallStripSourceNormalPhaseWindingRepair = normalized == "UYA",
            VertexNormalHeaderSize = normalized is "GC" or "UYA" ? 0 : 0x10,
            SourceNormalPhaseWindingRepairAverageDot = 0.72f
        };
    }

    internal static string NormalizeGameLabel(string? gameLabel)
    {
        return string.IsNullOrWhiteSpace(gameLabel)
            ? "TIE"
            : gameLabel.Trim().ToUpperInvariant();
    }
}

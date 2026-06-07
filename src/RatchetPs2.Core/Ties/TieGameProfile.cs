using RatchetPs2.Core.Games;

namespace RatchetPs2.Core.Ties;

public sealed record TieGameProfile
{
    public static TieGameProfile Default { get; } = new();

    public string GameLabel { get; init; } = "TIE";
    public string GlowEmissionAttributeName { get; init; } = "_TIE_GLOW_0";
    public int ReflectiveMaskModeBit { get; init; } = 0x20;
    public int ReflectiveMaskMultipassType { get; init; } = 10;
    public float ReflectiveMaskMetallicFactor { get; init; } = 0.37f;
    public float ReflectiveMaskRoughnessFactor { get; init; } = 0.24f;
    public bool SuppressGeneratedNormalFallback { get; init; }
    public bool UseStripTokenReferencesForTopology { get; init; }
    public bool UsePreviousStripReferencePhaseForTopology { get; init; }
    public bool UseSourceNormalPhaseWindingRepair { get; init; }
    public bool UseGeometryWindingRepair { get; init; } = true;

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
            UseSourceNormalPhaseWindingRepair = normalized == "GC",
            UseGeometryWindingRepair = normalized != "GC"
        };
    }

    internal static string NormalizeGameLabel(string? gameLabel)
    {
        return string.IsNullOrWhiteSpace(gameLabel)
            ? "TIE"
            : gameLabel.Trim().ToUpperInvariant();
    }
}

using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal sealed record TieGltfSourceNormalPhaseAnalysis(
    TieGltfRawSourceNormalLayout? DominantLayout,
    IReadOnlyList<TieGltfSourceNormalPhaseStripDiagnostic> Strips)
{
    public static TieGltfSourceNormalPhaseAnalysis Empty { get; } = new(null, []);

    public int ScoredStripCount => Strips.Count;
    public int CurrentVoteStripCount => Strips.Count(strip => strip.PhaseVote == TieGltfSourceNormalPhaseVote.Current);
    public int InvertedVoteStripCount => Strips.Count(strip => strip.PhaseVote == TieGltfSourceNormalPhaseVote.Inverted);
    public int AmbiguousVoteStripCount => Strips.Count(strip => strip.PhaseVote == TieGltfSourceNormalPhaseVote.Ambiguous);
    public int InsufficientVoteStripCount => Strips.Count(strip => strip.PhaseVote == TieGltfSourceNormalPhaseVote.Insufficient);
    public IReadOnlySet<int> RepairStripIndices => Strips
        .Where(strip => strip.ShouldApplyWindingRepair)
        .Where(strip => strip.WindingRepairTriangleCount > 0)
        .Select(strip => strip.StripIndex)
        .ToHashSet();
    public IReadOnlySet<TieGltfSourceNormalPhaseTriangleKey> RepairTriangles => Strips
        .Where(strip => strip.ShouldApplyWindingRepair)
        .SelectMany(strip => strip.WindingRepairTriangleIndices.Select(triangleIndex =>
            new TieGltfSourceNormalPhaseTriangleKey(strip.StripIndex, triangleIndex)))
        .ToHashSet();
    public int RepairTriangleCount => RepairTriangles.Count;
}

internal sealed record TieGltfSourceNormalPhaseStripDiagnostic(
    int LodIndex,
    int StripIndex,
    int PacketIndex,
    int PacketStripIndex,
    int? ShaderIndex,
    int TriangleCount,
    string? FirstToken,
    int PacketDinkyUploadRemappedVertexCount,
    int LogicalRemappedVertexCount,
    int PacketRowRemappedVertexCount,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> PacketDinkyUploadNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> LogicalNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> PacketRowNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> UsedNormalRemapChunks,
    int? DominantUsedNormalRemapChunkIndex,
    int DominantUsedNormalRemapChunkRemapCount,
    bool UsesPreviousStripReferencePhase,
    string SelectedTargetMode,
    IReadOnlyList<TieGltfSourceNormalPhaseTargetDiagnostic> TargetModeVotes,
    IReadOnlyList<TieGltfSourceNormalPhaseTriangleDiagnostic> TriangleVotes,
    int ScoredTriangleCount,
    string BestCurrentLayout,
    int BestCurrentStrongTriangleCount,
    float BestCurrentAverageDot,
    string BestInvertedLayout,
    int BestInvertedStrongTriangleCount,
    float BestInvertedAverageDot,
    float WindingRepairAverageDot,
    bool EnablePartialSmallStripWindingRepair,
    TieGltfSourceNormalPhaseVote PhaseVote)
{
    private const float PartialSmallStripExactHalfMaximumAverageDot = 0.55f;
    private const float NearDenseWindingRepairAverageDot = 0.45f;
    private const int NearDenseWindingRepairMinimumStrongTriangleLead = 6;

    public bool ShouldApplyWindingRepair =>
        !UsesPreviousStripReferencePhase
        && (
            PhaseVote == TieGltfSourceNormalPhaseVote.Inverted
                && (UsesDenseSourceNormalRepair
                    || UsesNearDenseSourceNormalRepair
                    || UsesSmallStripSourceNormalRepair
                    || UsesPartialSmallStripSourceNormalRepair)
            || UsesMixedTriangleSourceNormalRepair);

    public bool UsesDenseSourceNormalRepair =>
        ScoredTriangleCount >= 8
        && BestInvertedStrongTriangleCount >= Math.Max(2, ScoredTriangleCount / 2)
        && BestInvertedStrongTriangleCount >= BestCurrentStrongTriangleCount + 2
        && BestInvertedAverageDot >= WindingRepairAverageDot;

    public bool UsesNearDenseSourceNormalRepair =>
        !UsesDenseSourceNormalRepair
        && WindingRepairAverageDot <= 0.5f
        && ScoredTriangleCount >= 8
        && BestInvertedStrongTriangleCount >= Math.Max(2, ScoredTriangleCount / 2)
        && BestInvertedStrongTriangleCount >= BestCurrentStrongTriangleCount + NearDenseWindingRepairMinimumStrongTriangleLead
        && BestCurrentAverageDot <= -NearDenseWindingRepairAverageDot
        && BestInvertedAverageDot >= NearDenseWindingRepairAverageDot;

    public bool UsesSmallStripSourceNormalRepair =>
        TriangleCount > 0
        && ScoredTriangleCount == TriangleCount
        && ScoredTriangleCount < 8
        && BestInvertedStrongTriangleCount == ScoredTriangleCount
        && BestCurrentStrongTriangleCount == 0
        && BestCurrentAverageDot <= -0.5f
        && BestInvertedAverageDot >= WindingRepairAverageDot;

    public bool UsesPartialSmallStripSourceNormalRepair =>
        EnablePartialSmallStripWindingRepair
        && !UsesDenseSourceNormalRepair
        && !UsesSmallStripSourceNormalRepair
        && PhaseVote == TieGltfSourceNormalPhaseVote.Inverted
        && TriangleCount > 0
        && ScoredTriangleCount > 0
        && ScoredTriangleCount < 8
        && BestInvertedStrongTriangleCount >= 2
        && HasPartialSmallStripInvertedMajority
        && BestInvertedStrongTriangleCount >= BestCurrentStrongTriangleCount + 2
        && BestCurrentAverageDot <= -0.5f
        && BestInvertedAverageDot > WindingRepairAverageDot
        && TriangleVotes.Any(vote => vote.PrefersPartialStripInvertedWinding);

    private bool HasPartialSmallStripInvertedMajority =>
        BestInvertedStrongTriangleCount * 2 > ScoredTriangleCount
        || BestInvertedStrongTriangleCount * 2 == ScoredTriangleCount
        && BestInvertedAverageDot < PartialSmallStripExactHalfMaximumAverageDot;

    public bool UsesMixedTriangleSourceNormalRepair =>
        PhaseVote == TieGltfSourceNormalPhaseVote.Ambiguous
        && TriangleCount > 0
        && ScoredTriangleCount == TriangleCount
        && BestInvertedStrongTriangleCount >= 2
        && BestCurrentStrongTriangleCount >= 2
        && TriangleVotes.Any(vote => vote.PrefersInvertedWinding);

    public IReadOnlyList<int> WindingRepairTriangleIndices
    {
        get
        {
            if (!ShouldApplyWindingRepair)
            {
                return [];
            }

            if (UsesMixedTriangleSourceNormalRepair)
            {
                return TriangleVotes
                    .Where(vote => vote.PrefersInvertedWinding)
                    .Select(vote => vote.TriangleIndexInStrip)
                    .ToArray();
            }

            if (UsesPartialSmallStripSourceNormalRepair)
            {
                return TriangleVotes
                    .Where(vote => vote.PrefersPartialStripInvertedWinding)
                    .Select(vote => vote.TriangleIndexInStrip)
                    .ToArray();
            }

            return Enumerable.Range(0, TriangleCount).ToArray();
        }
    }

    public int WindingRepairTriangleCount => WindingRepairTriangleIndices.Count;
}

internal readonly record struct TieGltfSourceNormalPhaseTriangleKey(
    int StripIndex,
    int TriangleIndexInStrip);

internal readonly record struct TieGltfSourceNormalPhaseTargetScore(
    TieGltfSourceNormalPhaseRemapTargetMode TargetMode,
    TieGltfSourceNormalPhaseLayoutScore Score);

internal sealed record TieGltfSourceNormalPhaseTriangleDiagnostic(
    int TriangleIndexInStrip,
    float CurrentAverageDot,
    float InvertedAverageDot)
{
    private const float InvertedWindingMinimumAverageDot = 0.5f;
    private const float PartialStripInvertedWindingMinimumAverageDot = 0.45f;

    public bool PrefersInvertedWinding =>
        InvertedAverageDot >= InvertedWindingMinimumAverageDot
        && InvertedAverageDot > CurrentAverageDot;

    public bool PrefersPartialStripInvertedWinding =>
        InvertedAverageDot >= PartialStripInvertedWindingMinimumAverageDot
        && InvertedAverageDot > CurrentAverageDot;
}

internal sealed record TieGltfSourceNormalPhaseTargetDiagnostic(
    string TargetMode,
    int ScoredTriangleCount,
    int CurrentStrongTriangleCount,
    float CurrentAverageDot,
    int InvertedStrongTriangleCount,
    float InvertedAverageDot,
    string PhaseVote);

internal sealed record TieGltfSourceNormalPhaseRemapChunkDiagnostic(
    int ChunkIndex,
    int RemapCount);

internal sealed record TieGltfSourceNormalPhaseRemapChunkUsage(
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> PacketDinkyUploadNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> LogicalNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> PacketRowNormalRemapChunks,
    IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> UsedNormalRemapChunks,
    int? DominantUsedNormalRemapChunkIndex,
    int DominantUsedNormalRemapChunkRemapCount);

internal enum TieGltfSourceNormalPhaseVote
{
    Insufficient,
    Current,
    Inverted,
    Ambiguous
}

internal enum TieGltfSourceNormalPhaseRemapTargetMode
{
    PacketDinkyUpload,
    LogicalFirst,
    LogicalVertex,
    PacketVertexRow
}

internal readonly record struct TieGltfSourceNormalPhaseLayoutScore(
    TieGltfRawSourceNormalLayout Layout,
    int ScoredTriangleCount,
    int CurrentStrongTriangleCount,
    int InvertedStrongTriangleCount,
    float CurrentDotSum,
    float InvertedDotSum);

internal readonly record struct TieGltfSourceNormalPhaseTopologyLayoutScore(
    TieGltfRawSourceNormalLayout Layout,
    int ScoredTriangleCount,
    int StrongAbsoluteTriangleCount,
    float AbsoluteDotSum);

internal readonly record struct TieGltfRawSourceNormalLayout(
    int X,
    int Y,
    int Z,
    int SignX,
    int SignY,
    int SignZ)
{
    public static TieGltfRawSourceNormalLayout Default { get; } = new(0, 2, 1, 1, 1, -1);

    public Vector3 Apply(TieVertexNormal normal)
    {
        return new Vector3(
            SignX * Get(normal, X),
            SignY * Get(normal, Y),
            SignZ * Get(normal, Z));
    }

    public Vector3 Apply(TieVertexNormal normal, bool usePackedSource)
    {
        return usePackedSource ? ApplyPacked(normal) : Apply(normal);
    }

    public override string ToString()
    {
        return $"{Format(SignX, X)}{Format(SignY, Y)}{Format(SignZ, Z)}";
    }

    public static bool TryParse(string? value, out TieGltfRawSourceNormalLayout layout)
    {
        layout = default;
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var components = new List<(int Sign, int Axis)>(3);
        for (var i = 0; i < text.Length; i++)
        {
            var sign = 1;
            if (text[i] == '-')
            {
                sign = -1;
                i++;
            }

            if (i >= text.Length || !TryParseAxis(text[i], out var axis))
            {
                return false;
            }

            components.Add((sign, axis));
        }

        if (components.Count != 3 || components.Select(component => component.Axis).Distinct().Count() != 3)
        {
            return false;
        }

        layout = new TieGltfRawSourceNormalLayout(
            components[0].Axis,
            components[1].Axis,
            components[2].Axis,
            components[0].Sign,
            components[1].Sign,
            components[2].Sign);
        return true;
    }

    private static short Get(TieVertexNormal normal, int index)
    {
        return index switch
        {
            0 => normal.X,
            1 => normal.Y,
            2 => normal.Z,
            3 => normal.W,
            _ => 0
        };
    }

    private Vector3 ApplyPacked(TieVertexNormal normal)
    {
        var azimuth = DecodePackedNormalLookup(normal.Packed & 0xFF);
        var elevation = DecodePackedNormalLookup(normal.Packed >> 8);
        var x = -azimuth.X * elevation.X;
        var y = -azimuth.Y * elevation.X;
        var z = -elevation.Y;

        return new Vector3(
            SignX * GetPacked(x, y, z, X),
            SignY * GetPacked(x, y, z, Y),
            SignZ * GetPacked(x, y, z, Z));
    }

    private static float GetPacked(float x, float y, float z, int index)
    {
        return index switch
        {
            0 => x,
            1 => y,
            2 => z,
            _ => 0
        };
    }

    private static Vector2 DecodePackedNormalLookup(int index)
    {
        var angle = (index & 0xFF) * (MathF.PI * 2f / 256f);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static string Format(int sign, int index)
    {
        var name = index switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "w",
            _ => "?"
        };
        return sign < 0 ? "-" + name : name;
    }

    private static bool TryParseAxis(char value, out int axis)
    {
        axis = char.ToLowerInvariant(value) switch
        {
            'x' => 0,
            'y' => 1,
            'z' => 2,
            'w' => 3,
            _ => -1
        };
        return axis >= 0;
    }
}

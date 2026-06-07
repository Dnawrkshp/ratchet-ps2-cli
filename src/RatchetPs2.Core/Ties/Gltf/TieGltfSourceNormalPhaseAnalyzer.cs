using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfSourceNormalPhaseAnalyzer
{
    private const float StrongNormalAgreementDot = 0.5f;

    public static TieGltfSourceNormalPhaseAnalysis Analyze(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(positions);

        if (tie.VertexNormals.Count == 0 || tie.VertexNormalRemaps.Count == 0)
        {
            return TieGltfSourceNormalPhaseAnalysis.Empty;
        }

        var remapsByLogicalVertexIndex = tie.VertexNormalRemaps
            .Where(remap => remap.LodIndex == topology.LodIndex && remap.LogicalVertexIndex.HasValue)
            .GroupBy(remap => remap.LogicalVertexIndex!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var remapsByPacketVertexRow = tie.VertexNormalRemaps
            .Where(remap => remap.LodIndex == topology.LodIndex
                && !remap.LogicalVertexIndex.HasValue
                && remap.VertexRowIndex >= 0)
            .GroupBy(remap => (remap.PacketIndex, remap.VertexRowIndex))
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (remapsByLogicalVertexIndex.Count == 0 && remapsByPacketVertexRow.Count == 0)
        {
            return TieGltfSourceNormalPhaseAnalysis.Empty;
        }

        var strips = new List<TieGltfSourceNormalPhaseStripDiagnostic>();
        var layouts = BuildRawNormalLayouts();
        var dominantLayout = SelectDominantSourceNormalLayout(
            tie,
            topology,
            positions,
            remapsByLogicalVertexIndex,
            remapsByPacketVertexRow,
            layouts);
        foreach (var strip in topology.Strips.OrderBy(strip => strip.StripIndex))
        {
            var logicalRemappedVertexCount = strip.LogicalVertices.Count(
                vertex => remapsByLogicalVertexIndex.ContainsKey(vertex.LogicalVertexIndex));
            var packetRowRemappedVertexCount = strip.LogicalVertices.Count(
                vertex => vertex.VertexRowIndex.HasValue
                    && remapsByPacketVertexRow.ContainsKey((vertex.PacketIndex, vertex.VertexRowIndex.Value)));
            var remapChunkUsage = AnalyzeStripNormalRemapChunks(
                strip,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow);
            if (logicalRemappedVertexCount == 0 && packetRowRemappedVertexCount == 0)
            {
                continue;
            }

            var targetModeScores = BuildTargetModeScores(
                tie,
                topology,
                strip,
                positions,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                dominantLayout);
            var selectedTargetModeScore = SelectTargetMode(strip, targetModeScores);
            var score = selectedTargetModeScore.Score;
            if (score.ScoredTriangleCount == 0)
            {
                continue;
            }

            var targetModeVotes = BuildTargetModeVotes(targetModeScores);
            var triangleVotes = BuildTriangleVotes(
                tie,
                topology,
                strip,
                positions,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                dominantLayout,
                selectedTargetModeScore.TargetMode);

            strips.Add(new TieGltfSourceNormalPhaseStripDiagnostic(
                strip.LodIndex,
                strip.StripIndex,
                strip.PacketIndex,
                strip.PacketStripIndex,
                strip.ShaderIndex,
                strip.TriangleCount,
                strip.Tokens.Length == 0 ? null : $"0x{strip.Tokens[0]:X2}",
                logicalRemappedVertexCount,
                packetRowRemappedVertexCount,
                remapChunkUsage.LogicalNormalRemapChunks,
                remapChunkUsage.PacketRowNormalRemapChunks,
                remapChunkUsage.UsedNormalRemapChunks,
                remapChunkUsage.DominantUsedNormalRemapChunkIndex,
                remapChunkUsage.DominantUsedNormalRemapChunkRemapCount,
                strip.UsesPreviousStripReferencePhase,
                selectedTargetModeScore.TargetMode.ToString(),
                targetModeVotes,
                triangleVotes,
                score.ScoredTriangleCount,
                dominantLayout.ToString(),
                score.CurrentStrongTriangleCount,
                Average(score.CurrentDotSum, score.ScoredTriangleCount),
                dominantLayout.ToString(),
                score.InvertedStrongTriangleCount,
                Average(score.InvertedDotSum, score.ScoredTriangleCount),
                ResolvePhaseVote(score)));
        }

        return new TieGltfSourceNormalPhaseAnalysis(strips);
    }

    private static TieGltfRawSourceNormalLayout SelectDominantSourceNormalLayout(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        IReadOnlyList<TieGltfRawSourceNormalLayout> layouts)
    {
        return layouts
            .Select(layout => ScoreTopologyLayout(
                tie,
                topology,
                positions,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                layout))
            .OrderByDescending(score => score.StrongAbsoluteTriangleCount)
            .ThenByDescending(score => score.AbsoluteDotSum)
            .ThenByDescending(score => score.ScoredTriangleCount)
            .ThenBy(score => score.Layout.ToString())
            .First()
            .Layout;
    }

    private static TieGltfSourceNormalPhaseTopologyLayoutScore ScoreTopologyLayout(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout)
    {
        var scoredTriangleCount = 0;
        var strongAbsoluteTriangleCount = 0;
        var absoluteDotSum = 0f;
        foreach (var triangle in topology.Triangles)
        {
            if (!TryAverageSourceNormal(
                    tie,
                    topology,
                    triangle,
                    remapsByLogicalVertexIndex,
                    remapsByPacketVertexRow,
                    layout,
                    TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst,
                    out var sourceNormal)
                || !TryFaceNormal(
                    positions[triangle.A],
                    positions[triangle.B],
                    positions[triangle.C],
                    out var faceNormal))
            {
                continue;
            }

            var absoluteDot = MathF.Abs(Vector3.Dot(faceNormal, sourceNormal));
            scoredTriangleCount++;
            absoluteDotSum += absoluteDot;
            if (absoluteDot >= StrongNormalAgreementDot)
            {
                strongAbsoluteTriangleCount++;
            }
        }

        return new TieGltfSourceNormalPhaseTopologyLayoutScore(
            layout,
            scoredTriangleCount,
            strongAbsoluteTriangleCount,
            absoluteDotSum);
    }

    private static TieGltfSourceNormalPhaseLayoutScore ScoreStripLayout(
        TieClass tie,
        TieLodTopology topology,
        TieTriangleStrip strip,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        TieGltfSourceNormalPhaseRemapTargetMode targetMode)
    {
        var scoredTriangleCount = 0;
        var currentStrongTriangleCount = 0;
        var invertedStrongTriangleCount = 0;
        var currentDotSum = 0f;
        var invertedDotSum = 0f;
        foreach (var triangle in topology.Triangles.Where(triangle => triangle.StripIndex == strip.StripIndex))
        {
            if (!TryAverageSourceNormal(
                    tie,
                    topology,
                    triangle,
                    remapsByLogicalVertexIndex,
                    remapsByPacketVertexRow,
                    layout,
                    targetMode,
                    out var sourceNormal)
                || !TryFaceNormal(
                    positions[triangle.A],
                    positions[triangle.B],
                    positions[triangle.C],
                    out var faceNormal))
            {
                continue;
            }

            var currentDot = Vector3.Dot(faceNormal, sourceNormal);
            var invertedDot = -currentDot;
            scoredTriangleCount++;
            currentDotSum += currentDot;
            invertedDotSum += invertedDot;
            if (currentDot >= StrongNormalAgreementDot)
            {
                currentStrongTriangleCount++;
            }

            if (invertedDot >= StrongNormalAgreementDot)
            {
                invertedStrongTriangleCount++;
            }
        }

        return new TieGltfSourceNormalPhaseLayoutScore(
            layout,
            scoredTriangleCount,
            currentStrongTriangleCount,
            invertedStrongTriangleCount,
            currentDotSum,
            invertedDotSum);
    }

    private static IReadOnlyList<TieGltfSourceNormalPhaseTriangleDiagnostic> BuildTriangleVotes(
        TieClass tie,
        TieLodTopology topology,
        TieTriangleStrip strip,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        TieGltfSourceNormalPhaseRemapTargetMode targetMode)
    {
        return topology.Triangles
            .Where(triangle => triangle.StripIndex == strip.StripIndex)
            .Select(triangle =>
            {
                if (!TryAverageSourceNormal(
                        tie,
                        topology,
                        triangle,
                        remapsByLogicalVertexIndex,
                        remapsByPacketVertexRow,
                        layout,
                        targetMode,
                        out var sourceNormal)
                    || !TryFaceNormal(
                        positions[triangle.A],
                        positions[triangle.B],
                        positions[triangle.C],
                        out var faceNormal))
                {
                    return null;
                }

                var currentDot = Vector3.Dot(faceNormal, sourceNormal);
                return new TieGltfSourceNormalPhaseTriangleDiagnostic(
                    triangle.TriangleIndexInStrip,
                    currentDot,
                    -currentDot);
            })
            .Where(vote => vote is not null)
            .Select(vote => vote!)
            .ToArray();
    }

    private static IReadOnlyList<TieGltfSourceNormalPhaseTargetScore> BuildTargetModeScores(
        TieClass tie,
        TieLodTopology topology,
        TieTriangleStrip strip,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout)
    {
        return
        [
            BuildTargetModeScore(TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst),
            BuildTargetModeScore(TieGltfSourceNormalPhaseRemapTargetMode.LogicalVertex),
            BuildTargetModeScore(TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow)
        ];

        TieGltfSourceNormalPhaseTargetScore BuildTargetModeScore(
            TieGltfSourceNormalPhaseRemapTargetMode targetMode)
        {
            var score = ScoreStripLayout(
                tie,
                topology,
                strip,
                positions,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                layout,
                targetMode);
            return new TieGltfSourceNormalPhaseTargetScore(targetMode, score);
        }
    }

    private static TieGltfSourceNormalPhaseTargetScore SelectTargetMode(
        TieTriangleStrip strip,
        IReadOnlyList<TieGltfSourceNormalPhaseTargetScore> scores)
    {
        var logicalFirstScore = scores.First(score =>
            score.TargetMode == TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst);
        if (!strip.UsesPreviousStripReferencePhase)
        {
            return logicalFirstScore;
        }

        var packetRowScore = scores.First(score =>
            score.TargetMode == TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow);
        return HasMeaningfulPacketRowSourceNormalCoverage(strip, packetRowScore.Score)
            ? packetRowScore
            : logicalFirstScore;
    }

    private static bool HasMeaningfulPacketRowSourceNormalCoverage(
        TieTriangleStrip strip,
        TieGltfSourceNormalPhaseLayoutScore score)
    {
        return ResolvePhaseVote(score) != TieGltfSourceNormalPhaseVote.Insufficient
            && score.ScoredTriangleCount >= Math.Max(2, strip.TriangleCount / 2);
    }

    private static IReadOnlyList<TieGltfSourceNormalPhaseTargetDiagnostic> BuildTargetModeVotes(
        IReadOnlyList<TieGltfSourceNormalPhaseTargetScore> scores)
    {
        return scores
            .Select(score => new TieGltfSourceNormalPhaseTargetDiagnostic(
                score.TargetMode.ToString(),
                score.Score.ScoredTriangleCount,
                score.Score.CurrentStrongTriangleCount,
                Average(score.Score.CurrentDotSum, score.Score.ScoredTriangleCount),
                score.Score.InvertedStrongTriangleCount,
                Average(score.Score.InvertedDotSum, score.Score.ScoredTriangleCount),
                ResolvePhaseVote(score.Score).ToString()))
            .ToArray();
    }

    private static TieGltfSourceNormalPhaseRemapChunkUsage AnalyzeStripNormalRemapChunks(
        TieTriangleStrip strip,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow)
    {
        var logicalChunks = new Dictionary<int, int>();
        var packetRowChunks = new Dictionary<int, int>();
        var usedChunks = new Dictionary<int, int>();
        foreach (var vertex in strip.LogicalVertices)
        {
            var hasLogicalRemaps = remapsByLogicalVertexIndex.TryGetValue(
                vertex.LogicalVertexIndex,
                out var logicalRemaps);
            if (hasLogicalRemaps)
            {
                CountChunks(logicalChunks, logicalRemaps);
                CountChunks(usedChunks, logicalRemaps);
            }

            if (vertex.VertexRowIndex.HasValue
                && remapsByPacketVertexRow.TryGetValue(
                    (vertex.PacketIndex, vertex.VertexRowIndex.Value),
                    out var rowRemaps))
            {
                CountChunks(packetRowChunks, rowRemaps);
                if (!hasLogicalRemaps)
                {
                    CountChunks(usedChunks, rowRemaps);
                }
            }
        }

        var usedChunkDiagnostics = BuildChunkDiagnostics(usedChunks);
        var dominantUsedChunk = usedChunkDiagnostics
            .OrderByDescending(chunk => chunk.RemapCount)
            .ThenBy(chunk => chunk.ChunkIndex)
            .FirstOrDefault();
        return new TieGltfSourceNormalPhaseRemapChunkUsage(
            BuildChunkDiagnostics(logicalChunks),
            BuildChunkDiagnostics(packetRowChunks),
            usedChunkDiagnostics,
            dominantUsedChunk?.ChunkIndex,
            dominantUsedChunk?.RemapCount ?? 0);

        static void CountChunks(
            IDictionary<int, int> chunks,
            IReadOnlyList<TieVertexNormalRemap>? remaps)
        {
            if (remaps is null)
            {
                return;
            }

            foreach (var remap in remaps)
            {
                chunks[remap.ChunkIndex] = chunks.TryGetValue(remap.ChunkIndex, out var count)
                    ? count + 1
                    : 1;
            }
        }
    }

    private static IReadOnlyList<TieGltfSourceNormalPhaseRemapChunkDiagnostic> BuildChunkDiagnostics(
        IReadOnlyDictionary<int, int> chunks)
    {
        return chunks
            .OrderBy(pair => pair.Key)
            .Select(pair => new TieGltfSourceNormalPhaseRemapChunkDiagnostic(pair.Key, pair.Value))
            .ToArray();
    }

    private static bool TryAverageSourceNormal(
        TieClass tie,
        TieLodTopology topology,
        TieTriangle triangle,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        TieGltfSourceNormalPhaseRemapTargetMode targetMode,
        out Vector3 normal)
    {
        var sum = Vector3.Zero;
        var count = 0;
        Add(triangle.A);
        Add(triangle.B);
        Add(triangle.C);
        if (count == 0 || sum.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(sum);
        return true;

        void Add(int logicalVertexIndex)
        {
            if (logicalVertexIndex < 0 || logicalVertexIndex >= topology.LogicalVertices.Count)
            {
                return;
            }

            var vertex = topology.LogicalVertices[logicalVertexIndex];
            if (targetMode != TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow
                && remapsByLogicalVertexIndex.TryGetValue(logicalVertexIndex, out var logicalRemaps))
            {
                AddRemaps(logicalRemaps);
                if (targetMode == TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst)
                {
                    return;
                }
            }

            if (targetMode != TieGltfSourceNormalPhaseRemapTargetMode.LogicalVertex
                && vertex.VertexRowIndex.HasValue
                && remapsByPacketVertexRow.TryGetValue((vertex.PacketIndex, vertex.VertexRowIndex.Value), out var rowRemaps))
            {
                AddRemaps(rowRemaps);
            }
        }

        void AddRemaps(IReadOnlyList<TieVertexNormalRemap> remaps)
        {
            foreach (var remap in remaps)
            {
                if (remap.NormalIndex < 0 || remap.NormalIndex >= tie.VertexNormals.Count)
                {
                    continue;
                }

                var candidate = layout.Apply(tie.VertexNormals[remap.NormalIndex]);
                if (candidate.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                sum += Vector3.Normalize(candidate);
                count++;
            }
        }
    }

    private static bool TryFaceNormal(Vector3 a, Vector3 b, Vector3 c, out Vector3 faceNormal)
    {
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared() <= 1e-12f)
        {
            faceNormal = default;
            return false;
        }

        faceNormal = Vector3.Normalize(normal);
        return true;
    }

    private static TieGltfSourceNormalPhaseVote ResolvePhaseVote(
        TieGltfSourceNormalPhaseLayoutScore score)
    {
        var bestCurrentStrong = score.CurrentStrongTriangleCount;
        var bestInvertedStrong = score.InvertedStrongTriangleCount;
        if (bestCurrentStrong == 0 && bestInvertedStrong == 0)
        {
            return TieGltfSourceNormalPhaseVote.Insufficient;
        }

        if (bestCurrentStrong > bestInvertedStrong)
        {
            return TieGltfSourceNormalPhaseVote.Current;
        }

        if (bestInvertedStrong > bestCurrentStrong)
        {
            return TieGltfSourceNormalPhaseVote.Inverted;
        }

        return TieGltfSourceNormalPhaseVote.Ambiguous;
    }

    private static float Average(float sum, int count)
    {
        return count == 0 ? 0f : sum / count;
    }

    private static TieGltfRawSourceNormalLayout[] BuildRawNormalLayouts()
    {
        var axes = new[] { 0, 1, 2, 3 };
        var layouts = new List<TieGltfRawSourceNormalLayout>();
        foreach (var x in axes)
        foreach (var y in axes.Where(axis => axis != x))
        foreach (var z in axes.Where(axis => axis != x && axis != y))
        foreach (var signX in new[] { -1, 1 })
        foreach (var signZ in new[] { -1, 1 })
        {
            // Keep the glTF-up component anchored so a full-vector sign flip
            // cannot make the phase vote tautological.
            layouts.Add(new TieGltfRawSourceNormalLayout(x, y, z, signX, SignY: 1, signZ));
        }

        return layouts.ToArray();
    }
}

internal sealed record TieGltfSourceNormalPhaseAnalysis(
    IReadOnlyList<TieGltfSourceNormalPhaseStripDiagnostic> Strips)
{
    public static TieGltfSourceNormalPhaseAnalysis Empty { get; } = new([]);

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
    int LogicalRemappedVertexCount,
    int PacketRowRemappedVertexCount,
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
    TieGltfSourceNormalPhaseVote PhaseVote)
{
    private const float WindingRepairAverageDot = 0.72f;

    public bool ShouldApplyWindingRepair =>
        !UsesPreviousStripReferencePhase
        && (
            PhaseVote == TieGltfSourceNormalPhaseVote.Inverted
                && (UsesDenseSourceNormalRepair || UsesSmallStripSourceNormalRepair)
            || UsesMixedTriangleSourceNormalRepair);

    public bool UsesDenseSourceNormalRepair =>
        ScoredTriangleCount >= 8
        && BestInvertedStrongTriangleCount >= Math.Max(2, ScoredTriangleCount / 2)
        && BestInvertedStrongTriangleCount >= BestCurrentStrongTriangleCount + 2
        && BestInvertedAverageDot >= WindingRepairAverageDot;

    public bool UsesSmallStripSourceNormalRepair =>
        TriangleCount > 0
        && ScoredTriangleCount == TriangleCount
        && ScoredTriangleCount < 8
        && BestInvertedStrongTriangleCount == ScoredTriangleCount
        && BestCurrentStrongTriangleCount == 0
        && BestCurrentAverageDot <= -0.5f
        && BestInvertedAverageDot >= WindingRepairAverageDot;

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
    public bool PrefersInvertedWinding =>
        InvertedAverageDot >= 0.5f
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
    public Vector3 Apply(TieVertexNormal normal)
    {
        return new Vector3(
            SignX * Get(normal, X),
            SignY * Get(normal, Y),
            SignZ * Get(normal, Z));
    }

    public override string ToString()
    {
        return $"{Format(SignX, X)}{Format(SignY, Y)}{Format(SignZ, Z)}";
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
}

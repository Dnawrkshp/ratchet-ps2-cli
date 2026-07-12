using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfSourceNormalPhaseAnalyzer
{
    private const float StrongNormalAgreementDot = 0.5f;
    private const int VertexNormalRemapTargetIndexMask = 0x3FFC;

    public static TieGltfSourceNormalPhaseAnalysis Analyze(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        TieGameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(profile);

        if (tie.VertexNormals.Count == 0 || tie.VertexNormalRemaps.Count == 0)
        {
            return TieGltfSourceNormalPhaseAnalysis.Empty;
        }

        var remapsByLogicalVertexIndex = tie.VertexNormalRemaps
            .Where(remap => remap.LodIndex == topology.LodIndex && remap.LogicalVertexIndex.HasValue)
            .GroupBy(remap => remap.LogicalVertexIndex!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var packetDinkyUploadBases = profile.PreferVuAddressSourceNormalRemaps
            ? TieGltfNormalRemapTargetResolver.BuildPacketDinkyUploadBases(tie, topology)
            : new Dictionary<int, int>();
        var remapsByPacketDinkyUpload = profile.PreferVuAddressSourceNormalRemaps
            ? tie.VertexNormalRemaps
                .Where(remap => remap.LodIndex == topology.LodIndex)
                .GroupBy(remap => DecodeNormalRemapTargetIndex(remap.RawVertex))
                .ToDictionary(group => group.Key, group => group.ToArray())
            : new Dictionary<int, TieVertexNormalRemap[]>();
        var remapsByPacketVertexRow = tie.VertexNormalRemaps
            .Where(remap => remap.LodIndex == topology.LodIndex
                && !remap.LogicalVertexIndex.HasValue
                && remap.VertexRowIndex >= 0)
            .GroupBy(remap => (remap.PacketIndex, remap.VertexRowIndex))
            .ToDictionary(group => group.Key, group => group.ToArray());
        if (remapsByPacketDinkyUpload.Count == 0
            && remapsByLogicalVertexIndex.Count == 0
            && remapsByPacketVertexRow.Count == 0)
        {
            return TieGltfSourceNormalPhaseAnalysis.Empty;
        }

        var strips = new List<TieGltfSourceNormalPhaseStripDiagnostic>();
        var layouts = RawNormalLayouts;
        var usePackedVertexNormalTableSource = profile.UsePackedVertexNormalTableSource;
        var invertSourceNormals = profile.InvertDecodedFatVertexSourceNormals;
        var dominantLayout = SelectDominantSourceNormalLayout(
                tie,
                topology,
                positions,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                layouts,
                usePackedVertexNormalTableSource,
                invertSourceNormals);
        foreach (var strip in topology.Strips.OrderBy(strip => strip.StripIndex))
        {
            var packetDinkyUploadRemappedVertexCount = strip.LogicalVertices.Count(
                vertex => TryGetPacketDinkyUploadRemaps(
                    vertex,
                    packetDinkyUploadBases,
                    remapsByPacketDinkyUpload,
                    out _));
            var logicalRemappedVertexCount = strip.LogicalVertices.Count(
                vertex => remapsByLogicalVertexIndex.ContainsKey(vertex.LogicalVertexIndex));
            var packetRowRemappedVertexCount = strip.LogicalVertices.Count(
                vertex => vertex.VertexRowIndex.HasValue
                    && remapsByPacketVertexRow.ContainsKey((vertex.PacketIndex, vertex.VertexRowIndex.Value)));
            var remapChunkUsage = AnalyzeStripNormalRemapChunks(
                strip,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow);
            if (packetDinkyUploadRemappedVertexCount == 0
                && logicalRemappedVertexCount == 0
                && packetRowRemappedVertexCount == 0)
            {
                continue;
            }

            var targetModeScores = BuildTargetModeScores(
                tie,
                topology,
                strip,
                positions,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                dominantLayout,
                usePackedVertexNormalTableSource,
                invertSourceNormals);
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
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                dominantLayout,
                selectedTargetModeScore.TargetMode,
                usePackedVertexNormalTableSource,
                invertSourceNormals);

            strips.Add(new TieGltfSourceNormalPhaseStripDiagnostic(
                strip.LodIndex,
                strip.StripIndex,
                strip.PacketIndex,
                strip.PacketStripIndex,
                strip.ShaderIndex,
                strip.TriangleCount,
                strip.Tokens.Length == 0 ? null : $"0x{strip.Tokens[0]:X2}",
                packetDinkyUploadRemappedVertexCount,
                logicalRemappedVertexCount,
                packetRowRemappedVertexCount,
                remapChunkUsage.PacketDinkyUploadNormalRemapChunks,
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
                profile.SourceNormalPhaseWindingRepairAverageDot,
                profile.UsePartialSmallStripSourceNormalPhaseWindingRepair,
                ResolvePhaseVote(score)));
        }

        return new TieGltfSourceNormalPhaseAnalysis(dominantLayout, strips);
    }

    private static TieGltfRawSourceNormalLayout SelectDominantSourceNormalLayout(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        IReadOnlyList<TieGltfRawSourceNormalLayout> layouts,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals)
    {
        return layouts
            .Select(layout => ScoreTopologyLayout(
                tie,
                topology,
                positions,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                layout,
                usePackedVertexNormalTableSource,
                invertSourceNormals))
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals)
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
                    packetDinkyUploadBases,
                    remapsByPacketDinkyUpload,
                    remapsByLogicalVertexIndex,
                    remapsByPacketVertexRow,
                    layout,
                    usePackedVertexNormalTableSource,
                    invertSourceNormals,
                    remapsByPacketDinkyUpload.Count > 0
                        ? TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload
                        : TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst,
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        TieGltfSourceNormalPhaseRemapTargetMode targetMode,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals)
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
                    packetDinkyUploadBases,
                    remapsByPacketDinkyUpload,
                    remapsByLogicalVertexIndex,
                    remapsByPacketVertexRow,
                    layout,
                    usePackedVertexNormalTableSource,
                    invertSourceNormals,
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        TieGltfSourceNormalPhaseRemapTargetMode targetMode,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals)
    {
        return topology.Triangles
            .Where(triangle => triangle.StripIndex == strip.StripIndex)
            .Select(triangle =>
            {
                if (!TryAverageSourceNormal(
                        tie,
                        topology,
                        triangle,
                        packetDinkyUploadBases,
                        remapsByPacketDinkyUpload,
                        remapsByLogicalVertexIndex,
                        remapsByPacketVertexRow,
                        layout,
                        usePackedVertexNormalTableSource,
                        invertSourceNormals,
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals)
    {
        var targetModes = new List<TieGltfSourceNormalPhaseRemapTargetMode>();
        if (remapsByPacketDinkyUpload.Count > 0)
        {
            targetModes.Add(TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload);
        }

        targetModes.Add(TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst);
        targetModes.Add(TieGltfSourceNormalPhaseRemapTargetMode.LogicalVertex);
        targetModes.Add(TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow);
        return targetModes.Select(BuildTargetModeScore).ToArray();

        TieGltfSourceNormalPhaseTargetScore BuildTargetModeScore(
            TieGltfSourceNormalPhaseRemapTargetMode targetMode)
        {
            var score = ScoreStripLayout(
                tie,
                topology,
                strip,
                positions,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                remapsByLogicalVertexIndex,
                remapsByPacketVertexRow,
                layout,
                targetMode,
                usePackedVertexNormalTableSource,
                invertSourceNormals);
            return new TieGltfSourceNormalPhaseTargetScore(targetMode, score);
        }
    }

    private static TieGltfSourceNormalPhaseTargetScore SelectTargetMode(
        TieTriangleStrip strip,
        IReadOnlyList<TieGltfSourceNormalPhaseTargetScore> scores)
    {
        var packetDinkyUploadScores = scores
            .Where(score => score.TargetMode == TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload)
            .ToArray();
        if (packetDinkyUploadScores.Length > 0
            && HasMeaningfulSourceNormalCoverage(strip, packetDinkyUploadScores[0].Score))
        {
            return packetDinkyUploadScores[0];
        }

        var logicalFirstScore = scores.First(score =>
            score.TargetMode == TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst);
        if (!strip.UsesPreviousStripReferencePhase)
        {
            return logicalFirstScore;
        }

        var packetRowScore = scores.First(score =>
            score.TargetMode == TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow);
        return HasMeaningfulSourceNormalCoverage(strip, packetRowScore.Score)
            ? packetRowScore
            : logicalFirstScore;
    }

    private static bool HasMeaningfulSourceNormalCoverage(
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow)
    {
        var packetDinkyUploadChunks = new Dictionary<int, int>();
        var logicalChunks = new Dictionary<int, int>();
        var packetRowChunks = new Dictionary<int, int>();
        var usedChunks = new Dictionary<int, int>();
        foreach (var vertex in strip.LogicalVertices)
        {
            var hasPacketDinkyUploadRemaps = TryGetPacketDinkyUploadRemaps(
                vertex,
                packetDinkyUploadBases,
                remapsByPacketDinkyUpload,
                out var packetDinkyUploadRemaps);
            if (hasPacketDinkyUploadRemaps)
            {
                CountChunks(packetDinkyUploadChunks, packetDinkyUploadRemaps);
                CountChunks(usedChunks, packetDinkyUploadRemaps);
            }

            var hasLogicalRemaps = remapsByLogicalVertexIndex.TryGetValue(
                vertex.LogicalVertexIndex,
                out var logicalRemaps);
            if (hasLogicalRemaps)
            {
                CountChunks(logicalChunks, logicalRemaps);
                if (!hasPacketDinkyUploadRemaps)
                {
                    CountChunks(usedChunks, logicalRemaps);
                }
            }

            if (vertex.VertexRowIndex.HasValue
                && remapsByPacketVertexRow.TryGetValue(
                    (vertex.PacketIndex, vertex.VertexRowIndex.Value),
                out var rowRemaps))
            {
                CountChunks(packetRowChunks, rowRemaps);
                if (!hasPacketDinkyUploadRemaps && !hasLogicalRemaps)
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
            BuildChunkDiagnostics(packetDinkyUploadChunks),
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
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), TieVertexNormalRemap[]> remapsByPacketVertexRow,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        bool invertSourceNormals,
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
            if (targetMode == TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload
                && TryGetPacketDinkyUploadRemaps(
                    vertex,
                    packetDinkyUploadBases,
                    remapsByPacketDinkyUpload,
                    out var packetDinkyUploadRemaps))
            {
                AddRemaps(packetDinkyUploadRemaps);
                return;
            }

            if (targetMode != TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow
                && remapsByLogicalVertexIndex.TryGetValue(logicalVertexIndex, out var logicalRemaps))
            {
                AddRemaps(logicalRemaps);
                if (targetMode is TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload
                    or TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst
                    or TieGltfSourceNormalPhaseRemapTargetMode.LogicalVertex)
                {
                    return;
                }
            }

            if ((targetMode is TieGltfSourceNormalPhaseRemapTargetMode.PacketDinkyUpload
                    or TieGltfSourceNormalPhaseRemapTargetMode.LogicalFirst
                    or TieGltfSourceNormalPhaseRemapTargetMode.PacketVertexRow)
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

                var candidate = layout.Apply(tie.VertexNormals[remap.NormalIndex], usePackedVertexNormalTableSource);
                if (invertSourceNormals)
                {
                    candidate = -candidate;
                }

                if (candidate.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                sum += Vector3.Normalize(candidate);
                count++;
            }
        }
    }

    private static bool TryGetPacketDinkyUploadRemaps(
        TieLogicalVertex vertex,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, TieVertexNormalRemap[]> remapsByPacketDinkyUpload,
        out TieVertexNormalRemap[] remaps)
    {
        if (TieGltfNormalRemapTargetResolver.TryGetPacketDinkyUploadTarget(
                vertex,
                packetDinkyUploadBases,
                out var target)
            && remapsByPacketDinkyUpload.TryGetValue(target, out var targetRemaps))
        {
            remaps = targetRemaps;
            return true;
        }

        remaps = [];
        return false;
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

    private static int DecodeNormalRemapTargetIndex(ushort rawIndex)
    {
        return (rawIndex & VertexNormalRemapTargetIndexMask) / 4;
    }

    public static IReadOnlyList<TieGltfRawSourceNormalLayout> RawNormalLayouts { get; } = BuildRawNormalLayouts();

    private static TieGltfRawSourceNormalLayout[] BuildRawNormalLayouts()
    {
        return [TieGltfRawSourceNormalLayout.Default];
    }
}

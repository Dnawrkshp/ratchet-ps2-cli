using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfSourceNormalBuilder
{
    public const float PacketRowSourceNormalMinimumGeneratedDot = 0.95f;

    private const float SourceTableNormalMinimumGeneratedDot = 0.85f;
    private const int SourceTableNormalLayoutMinimumDominantAcceptedVertices = 8;
    private const float SourceTableNormalLayoutDominanceRatio = 2f;
    private const int SourceTableNormalPreserveMinimumAcceptedVertices = 24;
    private const float SourceTableNormalPreserveMinimumAcceptedRatio = 0.25f;
    private const float SourceTableNormalPreserveMinimumSignedToInvertedRatio = 1f;
    private const float SourceTableNormalPreserveMaximumInvertedAcceptedRatio = 0.1f;
    private const float SourceTableNormalPreserveMaximumUpperStrongDownRatio = 0.1f;
    private const float SourceTableNormalUpperStrongDownY = -0.25f;
    private const float FlatHorizontalSourceNormalMinimumGeneratedDot = 0.95f;
    private const int VertexNormalRemapTargetIndexMask = 0x3FFC;
    private static readonly TieGltfRawSourceNormalLayout[] SourceTableNormalLayouts =
    [
        TieGltfRawSourceNormalLayout.Default
    ];

    public static TieGltfSourceNormalTableApplyResult ApplyVertexNormalTableRemaps(
        TieClass tie,
        TieLodTopology topology,
        bool allowLogicalVertexRemaps,
        bool preferVuAddressRemaps,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates,
        bool usePackedVertexNormalTableSource,
        TieGltfRawSourceNormalLayout? preferredLayout)
    {
        if (tie.VertexNormals.Count == 0 || tie.VertexNormalRemaps.Count == 0)
        {
            return new TieGltfSourceNormalTableApplyResult(0, null);
        }

        var remapByLogicalVertexIndex = new Dictionary<int, List<TieVertexNormalRemap>>();
        var remapByPacketVertexRow = new Dictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>>();
        var remapByVuAddress = new Dictionary<int, List<TieVertexNormalRemap>>();
        foreach (var remap in tie.VertexNormalRemaps)
        {
            if (remap.LodIndex == topology.LodIndex)
            {
                AddNormalRemapCandidate(remapByVuAddress, DecodeNormalRemapTargetIndex(remap.RawVertex), remap);

                if (remap.LogicalVertexIndex is { } logicalVertexIndex)
                {
                    AddNormalRemapCandidate(remapByLogicalVertexIndex, logicalVertexIndex, remap);
                }

                if (remap.LogicalVertexIndex is null && remap.VertexRowIndex >= 0)
                {
                    AddNormalRemapCandidate(remapByPacketVertexRow, (remap.PacketIndex, remap.VertexRowIndex), remap);
                }
            }
        }

        var packetDinkyUploadBases = TieGltfNormalRemapTargetResolver.BuildPacketDinkyUploadBases(tie, topology);
        var tableLayout = SelectVertexNormalTableLayout(
            tie,
            topology,
            allowLogicalVertexRemaps,
            preferVuAddressRemaps,
            packetDinkyUploadBases,
            remapByVuAddress,
            remapByLogicalVertexIndex,
            remapByPacketVertexRow,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            usePackedVertexNormalTableSource,
            preferredLayout);
        var appliedCount = 0;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            var rowIndex = vertex.VertexRowIndex ?? vertex.AddressRowIndex;
            if (rowIndex is null
                || vertex.LogicalVertexIndex < 0
                || vertex.LogicalVertexIndex >= normals.Length
                || !TryGetVertexNormalRemaps(
                    vertex,
                    rowIndex.Value,
                    tableLayout.TargetMode,
                    packetDinkyUploadBases,
                    remapByVuAddress,
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    out var remaps))
            {
                continue;
            }

            if (tableLayout.PreserveSourceOrientation)
            {
                if (ApplySourceNormalDirect(
                    tie,
                    remaps,
                    tableLayout.Layout,
                    usePackedVertexNormalTableSource,
                    vertex.LogicalVertexIndex,
                    indexOffsetsByVertex,
                    generatedIndexNormals,
                    normals,
                    indexNormals,
                    sourceIndexOffsets,
                    sourceVertexStates,
                    sourceIndexStates,
                    out var failureState))
                {
                    sourceVertexIndices.Add(vertex.LogicalVertexIndex);
                    appliedCount++;
                }
                else
                {
                    MarkSourceNormalState(
                        vertex.LogicalVertexIndex,
                        indexOffsetsByVertex,
                        sourceVertexStates,
                        sourceIndexStates,
                        failureState);
                }

                continue;
            }

            if (!TrySelectBestSourceTableNormal(
                    tie,
                    remaps,
                    tableLayout.Layout,
                    usePackedVertexNormalTableSource,
                    vertex.LogicalVertexIndex,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    out var sourceNormal,
                    out _,
                    out _,
                    out _,
                    out _,
                    out var hasCandidate))
            {
                MarkSourceNormalState(
                    vertex.LogicalVertexIndex,
                    indexOffsetsByVertex,
                    sourceVertexStates,
                    sourceIndexStates,
                    hasCandidate
                        ? ClassifyRemapFailure(
                            tie,
                            remaps,
                            tableLayout.Layout,
                            usePackedVertexNormalTableSource,
                            vertex.LogicalVertexIndex,
                            indexOffsetsByVertex)
                        : TieGltfSourceNormalState.RejectedRemap);
                continue;
            }

            if (TryApplySourceNormal(
                    vertex.LogicalVertexIndex,
                    sourceNormal,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    normals,
                    indexNormals,
                    SourceTableNormalMinimumGeneratedDot,
                    out var orientedNormal,
                    sourceIndexOffsets,
                    sourceIndexStates,
                    TieGltfSourceNormalState.TableExact))
            {
                normals[vertex.LogicalVertexIndex] = orientedNormal;
                sourceVertexIndices.Add(vertex.LogicalVertexIndex);
                sourceVertexStates[vertex.LogicalVertexIndex] = TieGltfSourceNormalState.TableExact;
                appliedCount++;
            }
            else
            {
                MarkSourceNormalState(
                    vertex.LogicalVertexIndex,
                    indexOffsetsByVertex,
                    sourceVertexStates,
                    sourceIndexStates,
                    ClassifyRemapFailure(
                        tie,
                        remaps,
                        tableLayout.Layout,
                        usePackedVertexNormalTableSource,
                        vertex.LogicalVertexIndex,
                        indexOffsetsByVertex));
            }
        }

        return new TieGltfSourceNormalTableApplyResult(appliedCount, tableLayout);
    }

    public static bool TryApplySourceNormal(
        int logicalVertexIndex,
        Vector3 sourceNormal,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        float minimumGeneratedDot,
        out Vector3 orientedNormal,
        HashSet<int>? sourceIndexOffsets = null,
        TieGltfSourceNormalState[]? sourceIndexStates = null,
        TieGltfSourceNormalState sourceState = TieGltfSourceNormalState.TableExact)
    {
        orientedNormal = default;
        if (logicalVertexIndex < 0 || logicalVertexIndex >= normals.Length)
        {
            return false;
        }

        if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            return TryOrientSourceNormal(
                sourceNormal,
                generatedNormals[logicalVertexIndex],
                minimumGeneratedDot,
                out orientedNormal);
        }

        var applied = false;
        foreach (var indexOffset in indexOffsets)
        {
            if (indexOffset < 0 || indexOffset >= indexNormals.Length || indexOffset >= generatedIndexNormals.Count)
            {
                continue;
            }

            if (!TryOrientSourceNormal(
                    sourceNormal,
                    generatedIndexNormals[indexOffset],
                    minimumGeneratedDot,
                    out var indexNormal))
            {
                continue;
            }

            indexNormals[indexOffset] = indexNormal;
            sourceIndexOffsets?.Add(indexOffset);
            if (sourceIndexStates is not null)
            {
                sourceIndexStates[indexOffset] = sourceState;
            }

            orientedNormal = indexNormal;
            applied = true;
        }

        return applied;
    }

    private static void AddNormalRemapCandidate<TKey>(
        Dictionary<TKey, List<TieVertexNormalRemap>> remapsByTarget,
        TKey target,
        TieVertexNormalRemap remap)
        where TKey : notnull
    {
        if (!remapsByTarget.TryGetValue(target, out var remaps))
        {
            remaps = [];
            remapsByTarget[target] = remaps;
        }

        remaps.Add(remap);
    }

    private static TieGltfSourceNormalTableLayoutSelection SelectVertexNormalTableLayout(
        TieClass tie,
        TieLodTopology topology,
        bool allowLogicalVertexRemaps,
        bool preferVuAddressRemaps,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByVuAddress,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        bool usePackedVertexNormalTableSource,
        TieGltfRawSourceNormalLayout? preferredLayout)
    {
        var targetModes = preferVuAddressRemaps && remapByVuAddress.Count > 0
            ? new[] { TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload }
            : allowLogicalVertexRemaps && remapByLogicalVertexIndex.Count > 0
                ? new[] { TieGltfVertexNormalRemapTargetMode.LogicalVertex }
                : new[] { TieGltfVertexNormalRemapTargetMode.PacketVertexRow };
        var usesPreferredLayout = preferredLayout.HasValue;
        var selectedPreferredLayout = preferredLayout.GetValueOrDefault();
        TieGltfRawSourceNormalLayout[] layoutCandidates = usesPreferredLayout
            ? [selectedPreferredLayout]
            : SourceTableNormalLayouts;
        var defaultScore = ScoreVertexNormalTableLayout(
            TieGltfRawSourceNormalLayout.Default,
            targetModes[0],
            tie,
            topology,
            packetDinkyUploadBases,
            remapByVuAddress,
            remapByLogicalVertexIndex,
            remapByPacketVertexRow,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            usePackedVertexNormalTableSource);

        var bestScore = usesPreferredLayout
            ? ScoreVertexNormalTableLayout(
                selectedPreferredLayout,
                targetModes[0],
                tie,
                topology,
                packetDinkyUploadBases,
                remapByVuAddress,
                remapByLogicalVertexIndex,
                remapByPacketVertexRow,
                positions,
                indexOffsetsByVertex,
                generatedNormals,
                generatedIndexNormals,
                usePackedVertexNormalTableSource)
            : defaultScore;
        foreach (var targetMode in targetModes)
        {
            foreach (var layout in layoutCandidates)
            {
                var score = ScoreVertexNormalTableLayout(
                    layout,
                    targetMode,
                    tie,
                    topology,
                    packetDinkyUploadBases,
                    remapByVuAddress,
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    positions,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    usePackedVertexNormalTableSource);
                if (score.AcceptedVertexCount > bestScore.AcceptedVertexCount
                    || (score.AcceptedVertexCount == bestScore.AcceptedVertexCount
                        && score.DotSum > bestScore.DotSum))
                {
                    bestScore = score;
                }
            }
        }

        var preserveSourceOrientation =
            HasReliableSourceNormalPreserveScore(bestScore)
            && (usesPreferredLayout
                || bestScore.Layout != TieGltfRawSourceNormalLayout.Default
                && bestScore.AcceptedVertexCount >= SourceTableNormalLayoutMinimumDominantAcceptedVertices
                && bestScore.AcceptedVertexCount >= defaultScore.AcceptedVertexCount * SourceTableNormalLayoutDominanceRatio);

        return new TieGltfSourceNormalTableLayoutSelection(
            bestScore.Layout,
            bestScore.TargetMode,
            preserveSourceOrientation,
            bestScore);
    }

    private static TieGltfSourceNormalTableLayoutScore ScoreVertexNormalTableLayout(
        TieGltfRawSourceNormalLayout layout,
        TieGltfVertexNormalRemapTargetMode targetMode,
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByVuAddress,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        bool usePackedVertexNormalTableSource)
    {
        var acceptedVertexCount = 0;
        var candidateVertexCount = 0;
        var signedAcceptedVertexCount = 0;
        var invertedAcceptedVertexCount = 0;
        var upperHemisphereVertexCount = 0;
        var upperHemisphereStrongDownVertexCount = 0;
        var dotSum = 0f;
        var signedDotSum = 0f;
        var bounds = TieGltfGeneratedNormalBuilder.GetPositionBounds(positions);
        var yMidpoint = (bounds.Min.Y + bounds.Max.Y) * 0.5f;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            var rowIndex = vertex.VertexRowIndex ?? vertex.AddressRowIndex;
            if (rowIndex is null
                || vertex.LogicalVertexIndex < 0
                || !TryGetVertexNormalRemaps(
                    vertex,
                    rowIndex.Value,
                    targetMode,
                    packetDinkyUploadBases,
                    remapByVuAddress,
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    out var remaps))
            {
                continue;
            }

            var accepted = TrySelectBestSourceTableNormal(
                tie,
                remaps,
                layout,
                usePackedVertexNormalTableSource,
                vertex.LogicalVertexIndex,
                indexOffsetsByVertex,
                generatedNormals,
                generatedIndexNormals,
                out var sourceNormal,
                out var bestDot,
                out var bestSignedDot,
                out var signedAccepted,
                out var invertedAccepted,
                out var hasCandidate);
            if (hasCandidate)
            {
                candidateVertexCount++;
            }

            if (accepted)
            {
                acceptedVertexCount++;
                dotSum += bestDot;
                if (signedAccepted)
                {
                    signedAcceptedVertexCount++;
                    signedDotSum += MathF.Max(0f, bestSignedDot);
                }

                if (invertedAccepted)
                {
                    invertedAcceptedVertexCount++;
                }

                if (vertex.LogicalVertexIndex < positions.Count
                    && positions[vertex.LogicalVertexIndex].Y >= yMidpoint)
                {
                    upperHemisphereVertexCount++;
                    if (sourceNormal.Y <= SourceTableNormalUpperStrongDownY)
                    {
                        upperHemisphereStrongDownVertexCount++;
                    }
                }
            }
        }

        return new TieGltfSourceNormalTableLayoutScore(
            layout,
            targetMode,
            candidateVertexCount,
            acceptedVertexCount,
            dotSum,
            signedAcceptedVertexCount,
            signedDotSum,
            invertedAcceptedVertexCount,
            upperHemisphereVertexCount,
            upperHemisphereStrongDownVertexCount);
    }

    private static bool HasTooManyUpperStrongDownSourceNormals(TieGltfSourceNormalTableLayoutScore score)
    {
        return score.UpperHemisphereVertexCount > 0
            && score.UpperHemisphereStrongDownVertexCount
                > score.UpperHemisphereVertexCount * SourceTableNormalPreserveMaximumUpperStrongDownRatio;
    }

    private static bool HasReliableSourceNormalPreserveScore(TieGltfSourceNormalTableLayoutScore score)
    {
        return HasEnoughSourceNormalPreserveCoverage(score)
            && HasEnoughSignedSourceNormalAgreement(score)
            && !HasTooManyUpperStrongDownSourceNormals(score);
    }

    private static bool HasEnoughSourceNormalPreserveCoverage(TieGltfSourceNormalTableLayoutScore score)
    {
        return score.AcceptedVertexCount >= SourceTableNormalPreserveMinimumAcceptedVertices
            && score.CandidateVertexCount > 0
            && score.AcceptedVertexCount
                >= score.CandidateVertexCount * SourceTableNormalPreserveMinimumAcceptedRatio;
    }

    private static bool HasEnoughSignedSourceNormalAgreement(TieGltfSourceNormalTableLayoutScore score)
    {
        return score.InvertedAcceptedVertexCount == 0
            || score.SignedAcceptedVertexCount
                >= score.InvertedAcceptedVertexCount * SourceTableNormalPreserveMinimumSignedToInvertedRatio
            && score.InvertedAcceptedVertexCount
                <= score.AcceptedVertexCount * SourceTableNormalPreserveMaximumInvertedAcceptedRatio;
    }

    private static int DecodeNormalRemapTargetIndex(ushort rawIndex)
    {
        return (rawIndex & VertexNormalRemapTargetIndexMask) / 4;
    }

    private static bool TryGetVertexNormalRemaps(
        TieLogicalVertex vertex,
        int rowIndex,
        TieGltfVertexNormalRemapTargetMode targetMode,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByVuAddress,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        out IReadOnlyList<TieVertexNormalRemap> remaps)
    {
        if (targetMode == TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload
            && TieGltfNormalRemapTargetResolver.TryGetPacketDinkyUploadTarget(
                vertex,
                packetDinkyUploadBases,
                out var packetDinkyUploadTarget)
            && remapByVuAddress.TryGetValue(packetDinkyUploadTarget, out var packetDinkyUploadRemaps))
        {
            remaps = packetDinkyUploadRemaps;
            return true;
        }

        if (targetMode == TieGltfVertexNormalRemapTargetMode.VuAddress
            && remapByVuAddress.TryGetValue(vertex.VuAddress, out var vuAddressRemaps))
        {
            remaps = vuAddressRemaps;
            return true;
        }

        if (targetMode == TieGltfVertexNormalRemapTargetMode.LogicalVertex
            && remapByLogicalVertexIndex.TryGetValue(vertex.LogicalVertexIndex, out var logicalRemaps))
        {
            remaps = logicalRemaps;
            return true;
        }

        if (targetMode == TieGltfVertexNormalRemapTargetMode.PacketVertexRow
            && remapByPacketVertexRow.TryGetValue((vertex.PacketIndex, rowIndex), out var rowRemaps))
        {
            remaps = rowRemaps;
            return true;
        }

        remaps = [];
        return false;
    }

    private static bool TrySelectBestSourceTableNormal(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        int logicalVertexIndex,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        out Vector3 sourceNormal,
        out float bestDot,
        out float bestSignedDot,
        out bool signedAccepted,
        out bool invertedAccepted,
        out bool hasCandidate)
    {
        sourceNormal = default;
        bestDot = -1f;
        bestSignedDot = -1f;
        signedAccepted = false;
        invertedAccepted = false;
        hasCandidate = false;

        foreach (var remap in remaps)
        {
            if (remap.NormalIndex < 0
                || remap.NormalIndex >= tie.VertexNormals.Count
                || !TryNormalizeGltfNormal(
                    tie.VertexNormals[remap.NormalIndex],
                    layout,
                    usePackedVertexNormalTableSource,
                    out var candidateNormal))
            {
                continue;
            }

            hasCandidate = true;
            if (!TryScoreSourceNormal(
                    logicalVertexIndex,
                    candidateNormal,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    SourceTableNormalMinimumGeneratedDot,
                    out var candidateDot,
                    out var candidateSignedDot,
                    out var candidateSignedAccepted,
                    out var candidateInvertedAccepted))
            {
                continue;
            }

            if (candidateDot <= bestDot
                && (candidateDot < bestDot || candidateSignedDot <= bestSignedDot))
            {
                continue;
            }

            sourceNormal = candidateNormal;
            bestDot = candidateDot;
            bestSignedDot = candidateSignedDot;
            signedAccepted = candidateSignedAccepted;
            invertedAccepted = candidateInvertedAccepted;
        }

        return bestDot >= 0f;
    }

    private static bool ApplySourceNormalDirect(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        int logicalVertexIndex,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates,
        out TieGltfSourceNormalState failureState)
    {
        failureState = TieGltfSourceNormalState.RejectedRemap;
        if (logicalVertexIndex < 0 || logicalVertexIndex >= normals.Length)
        {
            return false;
        }

        var candidates = new List<(int Offset, int NormalIndex, Vector3 SourceNormal)>();
        foreach (var remap in remaps.OrderBy(remap => remap.Offset))
        {
            if (remap.NormalIndex < 0
                || remap.NormalIndex >= tie.VertexNormals.Count
                || !TryNormalizeGltfNormal(
                    tie.VertexNormals[remap.NormalIndex],
                    layout,
                    usePackedVertexNormalTableSource,
                    out var sourceNormal))
            {
                continue;
            }

            candidates.Add((remap.Offset, remap.NormalIndex, sourceNormal));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            normals[logicalVertexIndex] = AverageSourceNormalCandidates(candidates);
            sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.TableExact;
            return true;
        }

        var validIndexOffsets = indexOffsets
            .Where(indexOffset => indexOffset >= 0 && indexOffset < indexNormals.Length)
            .ToArray();
        var sourceNormalsByIndexOffset = ResolveSourceNormalCandidates(candidates, validIndexOffsets.Length);
        if (sourceNormalsByIndexOffset is null)
        {
            failureState = TieGltfSourceNormalState.AmbiguousRemap;
            return false;
        }

        normals[logicalVertexIndex] = AverageSourceNormalCandidates(candidates);
        sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.TableExact;
        for (var i = 0; i < validIndexOffsets.Length; i++)
        {
            var indexOffset = validIndexOffsets[i];
            indexNormals[indexOffset] = sourceNormalsByIndexOffset[i];
            sourceIndexOffsets.Add(indexOffset);
            sourceIndexStates[indexOffset] = TieGltfSourceNormalState.TableExact;
        }

        return true;

        static Vector3 AverageSourceNormalCandidates(
            IReadOnlyList<(int Offset, int NormalIndex, Vector3 SourceNormal)> candidates)
        {
            var sum = Vector3.Zero;
            foreach (var candidate in candidates)
            {
                sum += candidate.SourceNormal;
            }

            return sum.LengthSquared() <= 1e-12f
                ? candidates[0].SourceNormal
                : Vector3.Normalize(sum);
        }

        static Vector3[]? ResolveSourceNormalCandidates(
            IReadOnlyList<(int Offset, int NormalIndex, Vector3 SourceNormal)> candidates,
            int targetCount)
        {
            if (targetCount == 0)
            {
                return [];
            }

            if (candidates.Count == targetCount)
            {
                return candidates.Select(candidate => candidate.SourceNormal).ToArray();
            }

            if (candidates.All(candidate => NearlyEqual(candidate.SourceNormal, candidates[0].SourceNormal)))
            {
                return Enumerable.Repeat(candidates[0].SourceNormal, targetCount).ToArray();
            }

            return null;
        }

        static bool NearlyEqual(Vector3 left, Vector3 right)
        {
            return MathF.Abs(left.X - right.X) <= 0.000001f
                && MathF.Abs(left.Y - right.Y) <= 0.000001f
                && MathF.Abs(left.Z - right.Z) <= 0.000001f;
        }
    }

    private static void MarkSourceNormalState(
        int logicalVertexIndex,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates,
        TieGltfSourceNormalState state)
    {
        if (logicalVertexIndex >= 0 && logicalVertexIndex < sourceVertexStates.Length)
        {
            sourceVertexStates[logicalVertexIndex] = MaxState(sourceVertexStates[logicalVertexIndex], state);
        }

        if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            return;
        }

        foreach (var indexOffset in indexOffsets)
        {
            if (indexOffset >= 0 && indexOffset < sourceIndexStates.Length)
            {
                sourceIndexStates[indexOffset] = MaxState(sourceIndexStates[indexOffset], state);
            }
        }
    }

    private static TieGltfSourceNormalState ClassifyRemapFailure(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        int logicalVertexIndex,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex)
    {
        var candidates = remaps
            .Where(remap => remap.NormalIndex >= 0 && remap.NormalIndex < tie.VertexNormals.Count)
            .Select(remap => TryNormalizeGltfNormal(
                    tie.VertexNormals[remap.NormalIndex],
                    layout,
                    usePackedVertexNormalTableSource,
                    out var sourceNormal)
                ? sourceNormal
                : default(Vector3?))
            .Where(normal => normal.HasValue)
            .Select(normal => normal!.Value)
            .ToArray();
        if (candidates.Length == 0)
        {
            return TieGltfSourceNormalState.RejectedRemap;
        }

        var targetCount = indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets)
            ? indexOffsets.Count(indexOffset => indexOffset >= 0)
            : 0;
        var hasMultipleDistinctCandidates = candidates
            .Skip(1)
            .Any(candidate => !NearlyEqual(candidate, candidates[0]));
        return hasMultipleDistinctCandidates && candidates.Length != targetCount
            ? TieGltfSourceNormalState.AmbiguousRemap
            : TieGltfSourceNormalState.RejectedRemap;
    }

    private static TieGltfSourceNormalState MaxState(
        TieGltfSourceNormalState current,
        TieGltfSourceNormalState next)
    {
        return (int)next > (int)current ? next : current;
    }

    private static bool NearlyEqual(Vector3 left, Vector3 right)
    {
        return MathF.Abs(left.X - right.X) <= 0.000001f
            && MathF.Abs(left.Y - right.Y) <= 0.000001f
            && MathF.Abs(left.Z - right.Z) <= 0.000001f;
    }

    private static bool TryScoreSourceNormal(
        int logicalVertexIndex,
        Vector3 sourceNormal,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        float minimumGeneratedDot,
        out float bestDot,
        out float bestSignedDot,
        out bool signedAccepted,
        out bool invertedAccepted)
    {
        bestDot = -1f;
        bestSignedDot = -1f;
        signedAccepted = false;
        invertedAccepted = false;
        if (logicalVertexIndex < 0 || logicalVertexIndex >= generatedNormals.Count)
        {
            return false;
        }

        if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            bestSignedDot = Vector3.Dot(sourceNormal, generatedNormals[logicalVertexIndex]);
            bestDot = MathF.Abs(bestSignedDot);
            var minimumDot = ResolveSourceNormalMinimumGeneratedDot(
                generatedNormals[logicalVertexIndex],
                minimumGeneratedDot);
            signedAccepted = bestSignedDot >= minimumDot;
            invertedAccepted = -bestSignedDot >= minimumDot;
            return signedAccepted || invertedAccepted;
        }

        var accepted = false;
        foreach (var indexOffset in indexOffsets)
        {
            if (indexOffset < 0 || indexOffset >= generatedIndexNormals.Count)
            {
                continue;
            }

            var generatedNormal = generatedIndexNormals[indexOffset];
            var signedDot = Vector3.Dot(sourceNormal, generatedNormal);
            var dot = MathF.Abs(signedDot);
            bestDot = MathF.Max(bestDot, dot);
            bestSignedDot = MathF.Max(bestSignedDot, signedDot);
            var minimumDot = ResolveSourceNormalMinimumGeneratedDot(generatedNormal, minimumGeneratedDot);
            if (signedDot >= minimumDot)
            {
                signedAccepted = true;
                accepted = true;
            }

            if (-signedDot >= minimumDot)
            {
                invertedAccepted = true;
                accepted = true;
            }
        }

        return accepted;
    }

    private static bool TryOrientSourceNormal(
        Vector3 sourceNormal,
        Vector3 generatedNormal,
        float minimumGeneratedDot,
        out Vector3 orientedNormal)
    {
        var dot = Vector3.Dot(sourceNormal, generatedNormal);
        var flippedDot = -dot;
        if (flippedDot > dot)
        {
            sourceNormal = -sourceNormal;
            dot = flippedDot;
        }

        orientedNormal = sourceNormal;
        return dot >= ResolveSourceNormalMinimumGeneratedDot(generatedNormal, minimumGeneratedDot);
    }

    private static float ResolveSourceNormalMinimumGeneratedDot(Vector3 generatedNormal, float minimumGeneratedDot)
    {
        return generatedNormal.Y >= TieGltfGeneratedNormalBuilder.FlatHorizontalGeneratedNormalY
            ? MathF.Max(minimumGeneratedDot, FlatHorizontalSourceNormalMinimumGeneratedDot)
            : minimumGeneratedDot;
    }

    private static bool TryNormalizeGltfNormal(
        TieVertexNormal source,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        out Vector3 normal)
    {
        normal = layout.Apply(source, usePackedVertexNormalTableSource);
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(normal);
        return true;
    }
}

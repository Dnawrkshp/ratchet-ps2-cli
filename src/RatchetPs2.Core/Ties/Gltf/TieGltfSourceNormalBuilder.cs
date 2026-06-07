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
    private static readonly TieGltfSourceNormalTableLayout[] SourceTableNormalLayouts =
    [
        TieGltfSourceNormalTableLayout.Xzw,
        TieGltfSourceNormalTableLayout.Xzy,
        TieGltfSourceNormalTableLayout.Yzx,
        TieGltfSourceNormalTableLayout.Yzw,
        TieGltfSourceNormalTableLayout.Wzx,
        TieGltfSourceNormalTableLayout.Wzy
    ];

    public static TieGltfSourceNormalTableApplyResult ApplyVertexNormalTableRemaps(
        TieClass tie,
        TieLodTopology topology,
        bool allowLogicalVertexRemaps,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices)
    {
        if (tie.VertexNormals.Count == 0 || tie.VertexNormalRemaps.Count == 0)
        {
            return new TieGltfSourceNormalTableApplyResult(0, null);
        }

        var remapByLogicalVertexIndex = new Dictionary<int, List<TieVertexNormalRemap>>();
        var remapByPacketVertexRow = new Dictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>>();
        foreach (var remap in tie.VertexNormalRemaps)
        {
            if (remap.LodIndex == topology.LodIndex)
            {
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

        var tableLayout = SelectVertexNormalTableLayout(
            tie,
            topology,
            allowLogicalVertexRemaps,
            remapByLogicalVertexIndex,
            remapByPacketVertexRow,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals);
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
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    out var remaps))
            {
                continue;
            }

            var hasAcceptedSourceNormal = TrySelectBestSourceTableNormal(
                tie,
                remaps,
                tableLayout.Layout,
                vertex.LogicalVertexIndex,
                indexOffsetsByVertex,
                generatedNormals,
                generatedIndexNormals,
                out var sourceNormal,
                out _,
                out _,
                out _,
                out _,
                out _);
            if (!hasAcceptedSourceNormal
                && (!tableLayout.PreserveSourceOrientation
                    || !TrySelectFirstSourceTableNormal(tie, remaps, tableLayout.Layout, out sourceNormal)))
            {
                continue;
            }

            if (tableLayout.PreserveSourceOrientation)
            {
                if (ApplySourceNormalDirect(
                    vertex.LogicalVertexIndex,
                    sourceNormal,
                    indexOffsetsByVertex,
                    normals,
                    indexNormals))
                {
                    sourceVertexIndices.Add(vertex.LogicalVertexIndex);
                    appliedCount++;
                }

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
                    out var orientedNormal))
            {
                normals[vertex.LogicalVertexIndex] = orientedNormal;
                sourceVertexIndices.Add(vertex.LogicalVertexIndex);
                appliedCount++;
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
        out Vector3 orientedNormal)
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
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals)
    {
        var targetModes = allowLogicalVertexRemaps && remapByLogicalVertexIndex.Count > 0
            ? new[] { TieGltfVertexNormalRemapTargetMode.LogicalVertex }
            : new[] { TieGltfVertexNormalRemapTargetMode.PacketVertexRow };
        var defaultScore = ScoreVertexNormalTableLayout(
            TieGltfSourceNormalTableLayout.Xzw,
            targetModes[0],
            tie,
            topology,
            remapByLogicalVertexIndex,
            remapByPacketVertexRow,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals);

        var bestScore = defaultScore;
        foreach (var targetMode in targetModes)
        {
            foreach (var layout in SourceTableNormalLayouts)
            {
                var score = ScoreVertexNormalTableLayout(
                    layout,
                    targetMode,
                    tie,
                    topology,
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    positions,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals);
                if (score.AcceptedVertexCount > bestScore.AcceptedVertexCount
                    || (score.AcceptedVertexCount == bestScore.AcceptedVertexCount
                        && score.DotSum > bestScore.DotSum))
                {
                    bestScore = score;
                }
            }
        }

        var preserveSourceOrientation = bestScore.Layout != TieGltfSourceNormalTableLayout.Xzw
            && bestScore.AcceptedVertexCount >= SourceTableNormalLayoutMinimumDominantAcceptedVertices
            && bestScore.AcceptedVertexCount >= defaultScore.AcceptedVertexCount * SourceTableNormalLayoutDominanceRatio
            && HasEnoughSourceNormalPreserveCoverage(bestScore)
            && HasEnoughSignedSourceNormalAgreement(bestScore)
            && !HasTooManyUpperStrongDownSourceNormals(bestScore);

        return new TieGltfSourceNormalTableLayoutSelection(
            bestScore.Layout,
            bestScore.TargetMode,
            preserveSourceOrientation,
            bestScore);
    }

    private static TieGltfSourceNormalTableLayoutScore ScoreVertexNormalTableLayout(
        TieGltfSourceNormalTableLayout layout,
        TieGltfVertexNormalRemapTargetMode targetMode,
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals)
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

    private static bool TryGetVertexNormalRemaps(
        TieLogicalVertex vertex,
        int rowIndex,
        TieGltfVertexNormalRemapTargetMode targetMode,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        out IReadOnlyList<TieVertexNormalRemap> remaps)
    {
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
        TieGltfSourceNormalTableLayout layout,
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
                || !TryNormalizeGltfNormal(tie.VertexNormals[remap.NormalIndex], layout, out var candidateNormal))
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

    private static bool TrySelectFirstSourceTableNormal(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfSourceNormalTableLayout layout,
        out Vector3 sourceNormal)
    {
        foreach (var remap in remaps)
        {
            if (remap.NormalIndex >= 0
                && remap.NormalIndex < tie.VertexNormals.Count
                && TryNormalizeGltfNormal(tie.VertexNormals[remap.NormalIndex], layout, out sourceNormal))
            {
                return true;
            }
        }

        sourceNormal = default;
        return false;
    }

    private static bool ApplySourceNormalDirect(
        int logicalVertexIndex,
        Vector3 sourceNormal,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals)
    {
        if (logicalVertexIndex < 0 || logicalVertexIndex >= normals.Length)
        {
            return false;
        }

        normals[logicalVertexIndex] = sourceNormal;
        if (indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            foreach (var indexOffset in indexOffsets)
            {
                if (indexOffset >= 0 && indexOffset < indexNormals.Length)
                {
                    indexNormals[indexOffset] = sourceNormal;
                }
            }
        }

        return true;
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
        TieGltfSourceNormalTableLayout layout,
        out Vector3 normal)
    {
        normal = layout switch
        {
            TieGltfSourceNormalTableLayout.Xzw => new Vector3(source.X, source.Z, -source.W),
            TieGltfSourceNormalTableLayout.Xzy => new Vector3(source.X, source.Z, -source.Y),
            TieGltfSourceNormalTableLayout.Yzx => new Vector3(source.Y, source.Z, -source.X),
            TieGltfSourceNormalTableLayout.Yzw => new Vector3(source.Y, source.Z, -source.W),
            TieGltfSourceNormalTableLayout.Wzx => new Vector3(source.W, source.Z, -source.X),
            TieGltfSourceNormalTableLayout.Wzy => new Vector3(source.W, source.Z, -source.Y),
            _ => new Vector3(source.X, source.Z, -source.W)
        };
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(normal);
        return true;
    }
}

internal enum TieGltfSourceNormalTableLayout
{
    Xzw,
    Xzy,
    Yzx,
    Yzw,
    Wzx,
    Wzy
}

internal enum TieGltfVertexNormalRemapTargetMode
{
    LogicalVertex,
    PacketVertexRow
}

internal readonly record struct TieGltfSourceNormalTableLayoutSelection(
    TieGltfSourceNormalTableLayout Layout,
    TieGltfVertexNormalRemapTargetMode TargetMode,
    bool PreserveSourceOrientation,
    TieGltfSourceNormalTableLayoutScore BestScore);

internal readonly record struct TieGltfSourceNormalTableLayoutScore(
    TieGltfSourceNormalTableLayout Layout,
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

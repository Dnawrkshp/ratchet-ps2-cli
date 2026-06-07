using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfNormalBuilder
{
    private const float DominantSourceNormalMinimumY = 0.4f;
    private const float DominantSourceFallbackNormalMaximumY = 0.25f;

    public static TieGltfNormalBuildResult Build(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);

        var flatHorizontalBounds = TieGltfGeneratedNormalBuilder.IsFlatHorizontalBounds(
            TieGltfGeneratedNormalBuilder.GetPositionBounds(positions));
        var generatedNormals = TieGltfGeneratedNormalBuilder.BuildGeneratedNormals(positions, indices).ToArray();
        var generatedIndexNormals = flatHorizontalBounds
            ? TieGltfGeneratedNormalBuilder.BuildIndexNormalsFromVertexNormals(generatedNormals, indices).ToArray()
            : TieGltfGeneratedNormalBuilder.BuildGeneratedIndexNormals(positions, indices).ToArray();
        var normals = generatedNormals.ToArray();
        var indexNormals = generatedIndexNormals.ToArray();
        var sourceVertexIndices = new HashSet<int>();
        var indexOffsetsByVertex = TieGltfGeneratedNormalBuilder.BuildIndexOffsetsByVertex(indices);
        var tableNormalResult = TieGltfSourceNormalBuilder.ApplyVertexNormalTableRemaps(
            tie,
            topology,
            allowLogicalVertexRemaps: !flatHorizontalBounds,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            normals,
            indexNormals,
            sourceVertexIndices);
        var packetRowNormalVertexCount = ApplyPacketRowNormals(
            tie,
            topology,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            normals,
            indexNormals,
            sourceVertexIndices);
        if (tableNormalResult.PreserveSourceOrientation)
        {
            PropagateDominantSourceNormalsByDuplicatePosition(
                positions,
                indexOffsetsByVertex,
                normals,
                indexNormals,
                sourceVertexIndices);
            FillWeakDominantSourceNormalsFromStripNeighbors(
                topology,
                indexOffsetsByVertex,
                normals,
                indexNormals,
                sourceVertexIndices);
        }

        var duplicatePositionNormalWeldDecision = TieGltfDuplicatePositionNormalWeldDecision.None;
        if (flatHorizontalBounds)
        {
            TieGltfGeneratedNormalBuilder.WeldNormalsByPosition(positions, normals);
            TieGltfGeneratedNormalBuilder.RestoreFlatHorizontalGeneratedNormals(generatedNormals, normals, sourceVertexIndices);
            indexNormals = TieGltfGeneratedNormalBuilder.BuildIndexNormalsFromVertexNormals(normals, indices).ToArray();
            TieGltfGeneratedNormalBuilder.RestoreStronglyTiltedFlatFaceIndexNormals(
                positions,
                indices,
                indexNormals,
                flipDownwardHorizontalFaces: true,
                restoreOpposedNonHorizontalFaces: true);
        }
        else
        {
            duplicatePositionNormalWeldDecision = TieGltfGeneratedNormalBuilder.EvaluateDuplicatePositionIndexNormalWeld(positions, indices, indexNormals);
            if (duplicatePositionNormalWeldDecision.ShouldWeld)
            {
                TieGltfGeneratedNormalBuilder.WeldIndexNormalsByPosition(positions, indices, indexNormals);
            }
            else
            {
                TieGltfGeneratedNormalBuilder.SmoothCompatibleIndexNormalsByPosition(positions, indices, indexNormals);
            }

            TieGltfGeneratedNormalBuilder.RestoreStronglyTiltedFlatFaceIndexNormals(
                positions,
                indices,
                indexNormals,
                flipDownwardHorizontalFaces: false,
                restoreOpposedNonHorizontalFaces: false);
        }

        return new TieGltfNormalBuildResult(
            normals.ToList(),
            indexNormals.ToList(),
            sourceVertexIndices.OrderBy(index => index).ToArray(),
            sourceVertexIndices.Count,
            packetRowNormalVertexCount,
            tableNormalResult.VertexCount,
            tableNormalResult.Selection?.Layout.ToString(),
            tableNormalResult.Selection?.TargetMode.ToString(),
            tableNormalResult.PreserveSourceOrientation,
            tableNormalResult.Selection?.BestScore.CandidateVertexCount ?? 0,
            tableNormalResult.Selection?.BestScore.AcceptedVertexCount ?? 0,
            tableNormalResult.Selection?.BestScore.SignedAcceptedVertexCount ?? 0,
            tableNormalResult.Selection?.BestScore.InvertedAcceptedVertexCount ?? 0,
            tableNormalResult.Selection?.BestScore.UpperHemisphereVertexCount ?? 0,
            tableNormalResult.Selection?.BestScore.UpperHemisphereStrongDownVertexCount ?? 0,
            duplicatePositionNormalWeldDecision.Mode,
            duplicatePositionNormalWeldDecision.DuplicatePairCount,
            duplicatePositionNormalWeldDecision.IncompatiblePairCount,
            duplicatePositionNormalWeldDecision.CurrentScore.AverageDot,
            duplicatePositionNormalWeldDecision.WeldedScore.AverageDot,
            duplicatePositionNormalWeldDecision.WeldedScore.MinimumDot);
    }

    private static void PropagateDominantSourceNormalsByDuplicatePosition(
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices)
    {
        var sourceNormalSumsByPosition = new Dictionary<TieGltfPositionKey, Vector3>();
        foreach (var vertexIndex in sourceVertexIndices)
        {
            if (vertexIndex < 0
                || vertexIndex >= positions.Count
                || vertexIndex >= normals.Length
                || normals[vertexIndex].Y < DominantSourceNormalMinimumY)
            {
                continue;
            }

            var key = TieGltfPositionKey.From(positions[vertexIndex]);
            sourceNormalSumsByPosition.TryGetValue(key, out var sum);
            sourceNormalSumsByPosition[key] = sum + normals[vertexIndex];
        }

        foreach (var pair in sourceNormalSumsByPosition.ToArray())
        {
            sourceNormalSumsByPosition[pair.Key] = pair.Value.LengthSquared() <= 1e-12f
                ? Vector3.UnitY
                : Vector3.Normalize(pair.Value);
        }

        for (var i = 0; i < positions.Count && i < normals.Length; i++)
        {
            if (sourceVertexIndices.Contains(i)
                || normals[i].Y >= DominantSourceFallbackNormalMaximumY
                || !sourceNormalSumsByPosition.TryGetValue(TieGltfPositionKey.From(positions[i]), out var sourceNormal))
            {
                continue;
            }

            ApplyDominantSourceNormal(i, sourceNormal, indexOffsetsByVertex, normals, indexNormals, sourceVertexIndices);
        }
    }

    private static void FillWeakDominantSourceNormalsFromStripNeighbors(
        TieLodTopology topology,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices)
    {
        foreach (var strip in topology.Strips)
        {
            var logicalVertexIndices = strip.LogicalVertices
                .Select(vertex => vertex.LogicalVertexIndex)
                .Where(index => index >= 0 && index < normals.Length)
                .ToArray();
            for (var i = 0; i < logicalVertexIndices.Length; i++)
            {
                var logicalVertexIndex = logicalVertexIndices[i];
                if (sourceVertexIndices.Contains(logicalVertexIndex)
                    || normals[logicalVertexIndex].Y >= DominantSourceFallbackNormalMaximumY)
                {
                    continue;
                }

                var sourceNormal = Vector3.Zero;
                if (TryFindNeighborSourceNormal(-1, out var previousNormal))
                {
                    sourceNormal += previousNormal;
                }

                if (TryFindNeighborSourceNormal(1, out var nextNormal))
                {
                    sourceNormal += nextNormal;
                }

                if (sourceNormal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                ApplyDominantSourceNormal(
                    logicalVertexIndex,
                    Vector3.Normalize(sourceNormal),
                    indexOffsetsByVertex,
                    normals,
                    indexNormals,
                    sourceVertexIndices);

                bool TryFindNeighborSourceNormal(int direction, out Vector3 normal)
                {
                    for (var j = i + direction; j >= 0 && j < logicalVertexIndices.Length; j += direction)
                    {
                        var neighborIndex = logicalVertexIndices[j];
                        if (sourceVertexIndices.Contains(neighborIndex)
                            && normals[neighborIndex].Y >= DominantSourceNormalMinimumY)
                        {
                            normal = normals[neighborIndex];
                            return true;
                        }
                    }

                    normal = default;
                    return false;
                }
            }
        }
    }

    private static void ApplyDominantSourceNormal(
        int logicalVertexIndex,
        Vector3 sourceNormal,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices)
    {
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

        sourceVertexIndices.Add(logicalVertexIndex);
    }

    private static int ApplyPacketRowNormals(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices)
    {
        var count = 0;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (sourceVertexIndices.Contains(vertex.LogicalVertexIndex))
            {
                continue;
            }

            if ((!TrySelectPacketRowSourceNormal(tie.Header, vertex.VertexRow, out var sourceNormal)
                    || !TryApplyPacketRowSourceNormal(vertex.LogicalVertexIndex, sourceNormal))
                && !ReferenceEquals(vertex.AddressRow, vertex.VertexRow)
                && TrySelectPacketRowSourceNormal(tie.Header, vertex.AddressRow, out sourceNormal))
            {
                TryApplyPacketRowSourceNormal(vertex.LogicalVertexIndex, sourceNormal);
            }
        }

        return count;

        bool TryApplyPacketRowSourceNormal(int logicalVertexIndex, Vector3 sourceNormal)
        {
            if (logicalVertexIndex < 0 || logicalVertexIndex >= normals.Length)
            {
                return false;
            }

            if (!TieGltfSourceNormalBuilder.TryApplySourceNormal(
                    logicalVertexIndex,
                    sourceNormal,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    normals,
                    indexNormals,
                    TieGltfSourceNormalBuilder.PacketRowSourceNormalMinimumGeneratedDot,
                    out var orientedNormal))
            {
                return false;
            }

            normals[logicalVertexIndex] = orientedNormal;
            sourceVertexIndices.Add(logicalVertexIndex);
            count++;

            return true;
        }
    }

    private static bool TrySelectPacketRowSourceNormal(
        TieClassHeader header,
        TiePacketVertexRow? row,
        out Vector3 normal)
    {
        normal = default;
        if (row is null
            || TiePacketVertexRowClassifier.IsNonPositionVector(row.X, row.Y, row.Z)
            || TiePacketVertexRowClassifier.IsAttributeVector(row.X, row.Y, row.Z)
            || !IsLikelySourceNormalVector(header, row.X, row.Y, row.Z)
            || !TiePacketVertexRowClassifier.UsesSecondPositionSlot(row))
        {
            return false;
        }

        return TryNormalizeGltfNormal(row.X, row.Y, row.Z, out normal);
    }

    private static bool IsLikelySourceNormalVector(TieClassHeader header, short x, short y, short z)
    {
        var scale = header.Scale / 1024f;
        var length = MathF.Sqrt(
            x * scale * x * scale
            + y * scale * y * scale
            + z * scale * z * scale);
        return length is >= 0.05f and <= 3f;
    }
    private static bool TryNormalizeGltfNormal(short sourceX, short sourceY, short sourceZ, out Vector3 normal)
    {
        normal = new Vector3(sourceX, sourceZ, -sourceY);
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(normal);
        return true;
    }
}

internal sealed record TieGltfNormalBuildResult(
    List<Vector3> Normals,
    List<Vector3> IndexNormals,
    IReadOnlyList<int> SourceNormalVertexIndices,
    int SourceNormalVertexCount,
    int PacketRowNormalVertexCount,
    int TableNormalVertexCount,
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

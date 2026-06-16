using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfNormalBuilder
{
    private const float DominantSourceNormalMinimumY = 0.4f;
    private const float DominantSourceFallbackNormalMaximumY = 0.25f;
    private const float CrossLodExactPositionMinimumMissingCoverage = 0.95f;
    private const int RgbaRecipeConstantColorSourceSlot = 0x7FC / sizeof(int);

    public static TieGltfNormalBuildResult Build(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        TieGltfSourceNormalPhaseAnalysis sourceNormalPhaseAnalysis,
        TieGameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(sourceNormalPhaseAnalysis);
        ArgumentNullException.ThrowIfNull(profile);

        var flatHorizontalBounds = TieGltfGeneratedNormalBuilder.IsFlatHorizontalBounds(
            TieGltfGeneratedNormalBuilder.GetPositionBounds(positions));
        var generatedNormals = TieGltfGeneratedNormalBuilder.BuildGeneratedNormals(positions, indices).ToArray();
        var generatedIndexNormals = flatHorizontalBounds
            ? TieGltfGeneratedNormalBuilder.BuildIndexNormalsFromVertexNormals(generatedNormals, indices).ToArray()
            : TieGltfGeneratedNormalBuilder.BuildGeneratedIndexNormals(positions, indices).ToArray();
        var normals = generatedNormals.ToArray();
        var indexNormals = generatedIndexNormals.ToArray();
        var sourceVertexIndices = new HashSet<int>();
        var sourceIndexOffsets = new HashSet<int>();
        var sourceVertexStates = new TieGltfSourceNormalState[positions.Count];
        var sourceIndexStates = new TieGltfSourceNormalState[indices.Count];
        var indexOffsetsByVertex = TieGltfGeneratedNormalBuilder.BuildIndexOffsetsByVertex(indices);
        var tableNormalResult = TieGltfSourceNormalBuilder.ApplyVertexNormalTableRemaps(
            tie,
            topology,
            allowLogicalVertexRemaps: !flatHorizontalBounds,
            preferVuAddressRemaps: profile.PreferVuAddressSourceNormalRemaps,
            positions,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates,
            profile.UsePackedVertexNormalTableSource,
            sourceNormalPhaseAnalysis.DominantLayout);
        var rgbaRecipeNormalResult = ApplyRgbaRecipeSourceNormals(
            tie,
            topology,
            tableNormalResult.Selection?.Layout
                ?? sourceNormalPhaseAnalysis.DominantLayout
                ?? TieGltfRawSourceNormalLayout.Default,
            profile.UsePackedVertexNormalTableSource,
            indexOffsetsByVertex,
            generatedNormals,
            generatedIndexNormals,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates);
        var packetRowNormalVertexCount = ApplyPacketRowNormals(
            topology,
            profile.InvertDecodedFatVertexSourceNormals,
            indexOffsetsByVertex,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates);
        var crossLodExactNormalVertexCount = ApplyCrossLodExactPositionNormals(
            tie,
            topology,
            profile.PreferVuAddressSourceNormalRemaps,
            positions,
            tableNormalResult.Selection?.Layout
                ?? sourceNormalPhaseAnalysis.DominantLayout
                ?? TieGltfRawSourceNormalLayout.Default,
            profile.UsePackedVertexNormalTableSource,
            indexOffsetsByVertex,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates);
        var duplicatePositionExactNormalVertexCount = ApplyDuplicatePositionExactNormals(
            positions,
            indexOffsetsByVertex,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates);
        SealSourceNormalIndexOffsets(
            indices,
            generatedIndexNormals,
            normals,
            indexNormals,
            sourceVertexIndices,
            sourceIndexOffsets,
            sourceVertexStates,
            sourceIndexStates);

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
                restoreOpposedNonHorizontalFaces: true,
                protectedIndexOffsets: sourceIndexOffsets);
        }
        else
        {
            duplicatePositionNormalWeldDecision = TieGltfGeneratedNormalBuilder.EvaluateDuplicatePositionIndexNormalWeld(positions, indices, indexNormals);
            if (duplicatePositionNormalWeldDecision.ShouldWeld)
            {
                TieGltfGeneratedNormalBuilder.WeldIndexNormalsByPosition(
                    positions,
                    indices,
                    indexNormals,
                    sourceIndexOffsets);
            }
            else
            {
                TieGltfGeneratedNormalBuilder.SmoothCompatibleIndexNormalsByPosition(
                    positions,
                    indices,
                    indexNormals,
                    sourceIndexOffsets);
            }

            TieGltfGeneratedNormalBuilder.RestoreStronglyTiltedFlatFaceIndexNormals(
                positions,
                indices,
                indexNormals,
                flipDownwardHorizontalFaces: false,
                restoreOpposedNonHorizontalFaces: false,
                protectedIndexOffsets: sourceIndexOffsets);
        }

        return new TieGltfNormalBuildResult(
            normals.ToList(),
            indexNormals.ToList(),
            sourceVertexIndices.OrderBy(index => index).ToArray(),
            sourceIndexOffsets.OrderBy(index => index).ToArray(),
            sourceVertexStates.ToArray(),
            sourceIndexStates.ToArray(),
            sourceVertexIndices.Count,
            packetRowNormalVertexCount,
            tableNormalResult.VertexCount,
            rgbaRecipeNormalResult.NormalVertexCount,
            rgbaRecipeNormalResult.ConstantColorVertexCount,
            rgbaRecipeNormalResult.DegenerateBlendVertexCount,
            crossLodExactNormalVertexCount,
            duplicatePositionExactNormalVertexCount,
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

    private static int ApplyDuplicatePositionExactNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        var sourceNormalByPosition = new Dictionary<TieGltfPositionKey, Vector3>();
        var ambiguousPositions = new HashSet<TieGltfPositionKey>();
        for (var i = 0; i < positions.Count && i < normals.Length && i < sourceVertexStates.Length; i++)
        {
            if (!sourceVertexIndices.Contains(i)
                || !IsExactPositionSourceState(sourceVertexStates[i]))
            {
                continue;
            }

            var key = TieGltfPositionKey.From(positions[i]);
            if (ambiguousPositions.Contains(key))
            {
                continue;
            }

            var sourceNormal = normals[i];
            if (sourceNormal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            sourceNormal = Vector3.Normalize(sourceNormal);
            if (sourceNormalByPosition.TryGetValue(key, out var existingNormal))
            {
                if (!NearlyEqual(existingNormal, sourceNormal))
                {
                    sourceNormalByPosition.Remove(key);
                    ambiguousPositions.Add(key);
                }

                continue;
            }

            sourceNormalByPosition[key] = sourceNormal;
        }

        if (sourceNormalByPosition.Count == 0)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < positions.Count && i < normals.Length && i < sourceVertexStates.Length; i++)
        {
            if (sourceVertexIndices.Contains(i)
                || !CanApplyDuplicatePositionExactNormal(sourceVertexStates[i])
                || !sourceNormalByPosition.TryGetValue(TieGltfPositionKey.From(positions[i]), out var sourceNormal))
            {
                continue;
            }

            normals[i] = sourceNormal;
            sourceVertexIndices.Add(i);
            sourceVertexStates[i] = TieGltfSourceNormalState.DuplicatePositionExact;
            if (indexOffsetsByVertex.TryGetValue(i, out var indexOffsets))
            {
                foreach (var indexOffset in indexOffsets)
                {
                    if (indexOffset < 0 || indexOffset >= indexNormals.Length)
                    {
                        continue;
                    }

                    indexNormals[indexOffset] = sourceNormal;
                    sourceIndexOffsets.Add(indexOffset);
                    sourceIndexStates[indexOffset] = TieGltfSourceNormalState.DuplicatePositionExact;
                }
            }

            count++;
        }

        return count;
    }

    private static bool CanApplyDuplicatePositionExactNormal(TieGltfSourceNormalState state)
    {
        return state is TieGltfSourceNormalState.Missing
            or TieGltfSourceNormalState.RgbaRecipeConstantColor;
    }

    private static bool IsExactPositionSourceState(TieGltfSourceNormalState state)
    {
        return state is TieGltfSourceNormalState.TableExact
            or TieGltfSourceNormalState.PacketRowExact
            or TieGltfSourceNormalState.CrossLodExact
            or TieGltfSourceNormalState.DuplicatePositionExact;
    }

    private static void SealSourceNormalIndexOffsets(
        IReadOnlyList<uint> indices,
        IReadOnlyList<Vector3> generatedIndexNormals,
        IReadOnlyList<Vector3> normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        IReadOnlyList<TieGltfSourceNormalState> sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        for (var indexOffset = 0; indexOffset < indices.Count; indexOffset++)
        {
            var vertexIndex = checked((int)indices[indexOffset]);
            if (vertexIndex < 0
                || vertexIndex >= normals.Count
                || !sourceVertexIndices.Contains(vertexIndex)
                || sourceIndexOffsets.Contains(indexOffset))
            {
                continue;
            }

            indexNormals[indexOffset] = indexOffset < generatedIndexNormals.Count
                ? OrientSourceNormalToReference(normals[vertexIndex], generatedIndexNormals[indexOffset])
                : normals[vertexIndex];
            sourceIndexOffsets.Add(indexOffset);
            if (indexOffset < sourceIndexStates.Length)
            {
                sourceIndexStates[indexOffset] =
                    vertexIndex < sourceVertexStates.Count
                        ? sourceVertexStates[vertexIndex]
                        : TieGltfSourceNormalState.TableExact;
            }
        }
    }

    private static Vector3 OrientSourceNormalToReference(Vector3 sourceNormal, Vector3 referenceNormal)
    {
        return Vector3.Dot(sourceNormal, referenceNormal) < 0f
            ? -sourceNormal
            : sourceNormal;
    }

    private static int ApplyPacketRowNormals(
        TieLodTopology topology,
        bool invertDecodedFatVertexSourceNormals,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        var count = 0;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (sourceVertexIndices.Contains(vertex.LogicalVertexIndex))
            {
                continue;
            }

            if (TrySelectDecodedFatVertexSourceNormal(vertex, invertDecodedFatVertexSourceNormals, out var sourceNormal)
                && TryApplyPacketRowSourceNormal(vertex.LogicalVertexIndex, sourceNormal))
            {
                count++;
            }
        }

        return count;

        bool TryApplyPacketRowSourceNormal(int logicalVertexIndex, Vector3 sourceNormal)
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
                    if (indexOffset < 0 || indexOffset >= indexNormals.Length)
                    {
                        continue;
                    }

                    indexNormals[indexOffset] = sourceNormal;
                    sourceIndexOffsets.Add(indexOffset);
                    sourceIndexStates[indexOffset] = TieGltfSourceNormalState.PacketRowExact;
                }
            }

            sourceVertexIndices.Add(logicalVertexIndex);
            sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.PacketRowExact;

            return true;
        }
    }

    private static TieGltfRgbaRecipeNormalApplyResult ApplyRgbaRecipeSourceNormals(
        TieClass tie,
        TieLodTopology topology,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        if (tie.RgbaRemapOperations.Count == 0 || tie.VertexNormals.Count == 0)
        {
            return TieGltfRgbaRecipeNormalApplyResult.Empty;
        }

        var packetUploadLayouts = TieGltfNormalRemapTargetResolver.BuildPacketUploadLayouts(tie, topology);
        var recipesByTargetSlot = tie.RgbaRemapOperations
            .Where(operation => operation.LodIndex == topology.LodIndex)
            .OrderBy(operation => operation.GroupIndex)
            .ThenBy(operation => operation.Offset)
            .ThenBy(operation => operation.OperationIndex)
            .GroupBy(operation => operation.TargetCacheSlot)
            .ToDictionary(group => group.Key, group => group.Last());
        if (recipesByTargetSlot.Count == 0)
        {
            return TieGltfRgbaRecipeNormalApplyResult.Empty;
        }

        var normalCount = 0;
        var constantColorCount = 0;
        var degenerateBlendCount = 0;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            var logicalVertexIndex = vertex.LogicalVertexIndex;
            if (logicalVertexIndex < 0
                || logicalVertexIndex >= normals.Length
                || !CanApplyRgbaRecipeSourceNormal(sourceVertexStates[logicalVertexIndex])
                || !TieGltfNormalRemapTargetResolver.TryGetPacketUploadTarget(vertex, packetUploadLayouts, out var targetSlot)
                || !recipesByTargetSlot.TryGetValue(targetSlot, out var recipe))
            {
                continue;
            }

            var resolution = ResolveRgbaRecipeSourceNormal(
                tie,
                recipe,
                tableNormalLayout,
                usePackedVertexNormalTableSource,
                out var sourceNormal);
            if (resolution == TieGltfRgbaRecipeNormalResolution.Unresolved)
            {
                continue;
            }

            if (resolution == TieGltfRgbaRecipeNormalResolution.ConstantColor)
            {
                MarkRgbaRecipeNonNormalState(
                    logicalVertexIndex,
                    TieGltfSourceNormalState.RgbaRecipeConstantColor,
                    indexOffsetsByVertex,
                    sourceIndexStates);
                constantColorCount++;
                continue;
            }

            if (resolution == TieGltfRgbaRecipeNormalResolution.DegenerateBlend)
            {
                MarkRgbaRecipeNonNormalState(
                    logicalVertexIndex,
                    TieGltfSourceNormalState.RgbaRecipeDegenerateBlend,
                    indexOffsetsByVertex,
                    sourceIndexStates);
                degenerateBlendCount++;
                continue;
            }

            if (ApplyRgbaRecipeSourceNormal(
                    logicalVertexIndex,
                    sourceNormal,
                    indexOffsetsByVertex,
                    generatedNormals,
                    generatedIndexNormals,
                    normals,
                    indexNormals,
                    sourceVertexIndices,
                    sourceIndexOffsets,
                    sourceVertexStates,
                    sourceIndexStates))
            {
                normalCount++;
            }
        }

        return new TieGltfRgbaRecipeNormalApplyResult(
            normalCount,
            constantColorCount,
            degenerateBlendCount);

        static bool CanApplyRgbaRecipeSourceNormal(TieGltfSourceNormalState state)
        {
            return state is TieGltfSourceNormalState.Missing
                or TieGltfSourceNormalState.AmbiguousRemap
                or TieGltfSourceNormalState.RejectedRemap
                or TieGltfSourceNormalState.RgbaRecipeConstantColor
                or TieGltfSourceNormalState.RgbaRecipeDegenerateBlend;
        }

        void MarkRgbaRecipeNonNormalState(
            int logicalVertexIndex,
            TieGltfSourceNormalState state,
            IReadOnlyDictionary<int, List<int>> offsetsByVertex,
            TieGltfSourceNormalState[] indexStates)
        {
            sourceVertexStates[logicalVertexIndex] = state;
            if (!offsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
            {
                return;
            }

            foreach (var indexOffset in indexOffsets)
            {
                if (indexOffset < 0 || indexOffset >= indexStates.Length)
                {
                    continue;
                }

                indexStates[indexOffset] = state;
            }
        }
    }

    private static bool ApplyRgbaRecipeSourceNormal(
        int logicalVertexIndex,
        Vector3 sourceNormal,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        IReadOnlyList<Vector3> generatedNormals,
        IReadOnlyList<Vector3> generatedIndexNormals,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        if (logicalVertexIndex < 0 || logicalVertexIndex >= normals.Length)
        {
            return false;
        }

        if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
        {
            normals[logicalVertexIndex] = logicalVertexIndex < generatedNormals.Count
                ? OrientSourceNormalToReference(sourceNormal, generatedNormals[logicalVertexIndex])
                : sourceNormal;
            sourceVertexIndices.Add(logicalVertexIndex);
            sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.TableExact;
            return true;
        }

        var orientedSum = Vector3.Zero;
        var applied = false;
        foreach (var indexOffset in indexOffsets)
        {
            if (indexOffset < 0 || indexOffset >= indexNormals.Length)
            {
                continue;
            }

            var orientedNormal = indexOffset < generatedIndexNormals.Count
                ? OrientSourceNormalToReference(sourceNormal, generatedIndexNormals[indexOffset])
                : sourceNormal;
            indexNormals[indexOffset] = orientedNormal;
            sourceIndexOffsets.Add(indexOffset);
            sourceIndexStates[indexOffset] = TieGltfSourceNormalState.TableExact;
            orientedSum += orientedNormal;
            applied = true;
        }

        if (!applied)
        {
            return false;
        }

        normals[logicalVertexIndex] = orientedSum.LengthSquared() <= 1e-12f
            ? sourceNormal
            : Vector3.Normalize(orientedSum);
        sourceVertexIndices.Add(logicalVertexIndex);
        sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.TableExact;
        return true;
    }

    private static TieGltfRgbaRecipeNormalResolution ResolveRgbaRecipeSourceNormal(
        TieClass tie,
        TieRgbaRemapOperation recipe,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource,
        out Vector3 normal)
    {
        normal = default;

        var sum = Vector3.Zero;
        var sourceCount = 0;
        var hasConstantColorSource = false;
        var hasZeroNormalSource = false;
        foreach (var sourceSlot in recipe.SourceSlots)
        {
            if (!TryResolveRgbaRecipeSourceSlotNormal(
                    tie,
                    sourceSlot,
                    tableNormalLayout,
                    usePackedVertexNormalTableSource,
                    out var sourceNormal,
                    out var isConstantColorSource,
                    out var isZeroNormalSource))
            {
                return TieGltfRgbaRecipeNormalResolution.Unresolved;
            }

            if (isConstantColorSource)
            {
                hasConstantColorSource = true;
                continue;
            }

            if (isZeroNormalSource)
            {
                hasZeroNormalSource = true;
                continue;
            }

            sum += sourceNormal;
            sourceCount++;
        }

        if (hasConstantColorSource)
        {
            return TieGltfRgbaRecipeNormalResolution.ConstantColor;
        }

        if (hasZeroNormalSource)
        {
            return TieGltfRgbaRecipeNormalResolution.DegenerateBlend;
        }

        if (sourceCount == 0 || sum.LengthSquared() <= 1e-12f)
        {
            return TieGltfRgbaRecipeNormalResolution.DegenerateBlend;
        }

        normal = Vector3.Normalize(sum);
        return TieGltfRgbaRecipeNormalResolution.Normal;
    }

    private static bool TryResolveRgbaRecipeSourceSlotNormal(
        TieClass tie,
        int sourceSlot,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource,
        out Vector3 normal,
        out bool isConstantColorSource,
        out bool isZeroNormalSource)
    {
        isConstantColorSource = false;
        isZeroNormalSource = false;
        if (sourceSlot >= 0
            && sourceSlot < tie.VertexNormals.Count
            && TryNormalizeGltfNormalSource(
                tie.VertexNormals[sourceSlot],
                tableNormalLayout,
                usePackedVertexNormalTableSource,
                out normal,
                out isZeroNormalSource))
        {
            return true;
        }

        if (sourceSlot == RgbaRecipeConstantColorSourceSlot)
        {
            isConstantColorSource = true;
            normal = default;
            return true;
        }

        normal = default;
        return false;
    }

    private static int ApplyCrossLodExactPositionNormals(
        TieClass tie,
        TieLodTopology topology,
        bool enabled,
        IReadOnlyList<Vector3> positions,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3[] normals,
        Vector3[] indexNormals,
        HashSet<int> sourceVertexIndices,
        HashSet<int> sourceIndexOffsets,
        TieGltfSourceNormalState[] sourceVertexStates,
        TieGltfSourceNormalState[] sourceIndexStates)
    {
        if (!enabled)
        {
            return 0;
        }

        var sourceNormalsByPosition = BuildCrossLodSourceNormalsByPosition(
            tie,
            topology.LodIndex,
            tableNormalLayout,
            usePackedVertexNormalTableSource);
        if (sourceNormalsByPosition.Count == 0)
        {
            return 0;
        }

        var missingVertices = topology.LogicalVertices
            .OrderBy(vertex => vertex.LogicalVertexIndex)
            .Where(vertex => vertex.LogicalVertexIndex >= 0
                && vertex.LogicalVertexIndex < normals.Length
                && vertex.LogicalVertexIndex < positions.Count
                && !sourceVertexIndices.Contains(vertex.LogicalVertexIndex)
                && sourceVertexStates[vertex.LogicalVertexIndex] == TieGltfSourceNormalState.Missing)
            .ToArray();
        if (missingVertices.Length == 0)
        {
            return 0;
        }

        var matchedMissingVertexCount = missingVertices.Count(
            vertex => sourceNormalsByPosition.ContainsKey(TieGltfPositionKey.From(positions[vertex.LogicalVertexIndex])));
        if (matchedMissingVertexCount
            < missingVertices.Length * CrossLodExactPositionMinimumMissingCoverage)
        {
            return 0;
        }

        var count = 0;
        foreach (var vertex in missingVertices)
        {
            var logicalVertexIndex = vertex.LogicalVertexIndex;
            if (!sourceNormalsByPosition.TryGetValue(TieGltfPositionKey.From(positions[logicalVertexIndex]), out var sourceNormal))
            {
                continue;
            }

            normals[logicalVertexIndex] = sourceNormal;
            sourceVertexIndices.Add(logicalVertexIndex);
            sourceVertexStates[logicalVertexIndex] = TieGltfSourceNormalState.CrossLodExact;
            if (indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
            {
                foreach (var indexOffset in indexOffsets)
                {
                    if (indexOffset < 0 || indexOffset >= indexNormals.Length)
                    {
                        continue;
                    }

                    indexNormals[indexOffset] = sourceNormal;
                    sourceIndexOffsets.Add(indexOffset);
                    sourceIndexStates[indexOffset] = TieGltfSourceNormalState.CrossLodExact;
                }
            }

            count++;
        }

        return count;
    }

    private static IReadOnlyDictionary<TieGltfPositionKey, Vector3> BuildCrossLodSourceNormalsByPosition(
        TieClass tie,
        int targetLodIndex,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource)
    {
        var normalsByPosition = new Dictionary<TieGltfPositionKey, Vector3>();
        var ambiguousPositions = new HashSet<TieGltfPositionKey>();
        foreach (var sourceTopology in tie.LodTopologies
                     .Where(sourceTopology => sourceTopology.LodIndex != targetLodIndex && sourceTopology.LogicalVertexCount > 0)
                     .OrderBy(sourceTopology => sourceTopology.LodIndex))
        {
            var sourcePositions = TieGltfPositionBuilder.BuildPositions(tie, sourceTopology);
            var packetDinkyUploadBases = TieGltfNormalRemapTargetResolver.BuildPacketDinkyUploadBases(tie, sourceTopology);
            var remapsByTarget = tie.VertexNormalRemaps
                .Where(remap => remap.LodIndex == sourceTopology.LodIndex)
                .GroupBy(remap => DecodeNormalRemapTargetIndex(remap.RawVertex))
                .ToDictionary(group => group.Key, group => group.ToArray());
            foreach (var vertex in sourceTopology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
            {
                if (vertex.LogicalVertexIndex < 0
                    || vertex.LogicalVertexIndex >= sourcePositions.Count
                    || !TieGltfNormalRemapTargetResolver.TryGetPacketDinkyUploadTarget(
                        vertex,
                        packetDinkyUploadBases,
                        out var target)
                    || !remapsByTarget.TryGetValue(target, out var remaps)
                    || !TryGetSingleSourceNormal(
                        tie,
                        remaps,
                        tableNormalLayout,
                        usePackedVertexNormalTableSource,
                        out var sourceNormal))
                {
                    continue;
                }

                var positionKey = TieGltfPositionKey.From(sourcePositions[vertex.LogicalVertexIndex]);
                if (ambiguousPositions.Contains(positionKey))
                {
                    continue;
                }

                if (normalsByPosition.TryGetValue(positionKey, out var existingNormal))
                {
                    if (!NearlyEqual(existingNormal, sourceNormal))
                    {
                        normalsByPosition.Remove(positionKey);
                        ambiguousPositions.Add(positionKey);
                    }

                    continue;
                }

                normalsByPosition[positionKey] = sourceNormal;
            }
        }

        return normalsByPosition;
    }

    private static bool TryGetSingleSourceNormal(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfRawSourceNormalLayout tableNormalLayout,
        bool usePackedVertexNormalTableSource,
        out Vector3 sourceNormal)
    {
        sourceNormal = default;
        var hasNormal = false;
        foreach (var remap in remaps.OrderBy(remap => remap.Offset))
        {
            if (remap.NormalIndex < 0
                || remap.NormalIndex >= tie.VertexNormals.Count
                || !TryNormalizeGltfNormal(
                    tie.VertexNormals[remap.NormalIndex],
                    tableNormalLayout,
                    usePackedVertexNormalTableSource,
                    out var candidateNormal))
            {
                continue;
            }

            if (hasNormal)
            {
                if (!NearlyEqual(sourceNormal, candidateNormal))
                {
                    sourceNormal = default;
                    return false;
                }

                continue;
            }

            sourceNormal = candidateNormal;
            hasNormal = true;
        }

        return hasNormal;
    }

    private static int DecodeNormalRemapTargetIndex(ushort rawIndex)
    {
        const int vertexNormalRemapTargetIndexMask = 0x3FFC;
        return (rawIndex & vertexNormalRemapTargetIndexMask) / 4;
    }

    private static bool TrySelectDecodedFatVertexSourceNormal(
        TieLogicalVertex vertex,
        bool invertDecodedFatVertexSourceNormals,
        out Vector3 normal)
    {
        normal = default;
        var decodedVertex = vertex.DecodedVertex;
        if (decodedVertex?.Kind != TiePacketDecodedVertexKind.Fat
            || decodedVertex.Bytes.Length < 0x06)
        {
            return false;
        }

        return TryNormalizeDecodedFatVertexNormal(
            BitConverter.ToInt16(decodedVertex.Bytes, 0x00),
            BitConverter.ToInt16(decodedVertex.Bytes, 0x02),
            BitConverter.ToInt16(decodedVertex.Bytes, 0x04),
            invertDecodedFatVertexSourceNormals,
            out normal);
    }

    private static bool TryNormalizeDecodedFatVertexNormal(
        short sourceX,
        short sourceY,
        short sourceZ,
        bool invert,
        out Vector3 normal)
    {
        normal = invert
            ? new Vector3(-sourceX, -sourceZ, sourceY)
            : new Vector3(sourceX, sourceZ, -sourceY);
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(normal);
        return true;
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

    private static bool TryNormalizeGltfNormalSource(
        TieVertexNormal source,
        TieGltfRawSourceNormalLayout layout,
        bool usePackedVertexNormalTableSource,
        out Vector3 normal,
        out bool isZeroNormalSource)
    {
        normal = layout.Apply(source, usePackedVertexNormalTableSource);
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            isZeroNormalSource = true;
            return true;
        }

        normal = Vector3.Normalize(normal);
        isZeroNormalSource = false;
        return true;
    }

    private static bool NearlyEqual(Vector3 left, Vector3 right)
    {
        return MathF.Abs(left.X - right.X) <= 0.000001f
            && MathF.Abs(left.Y - right.Y) <= 0.000001f
            && MathF.Abs(left.Z - right.Z) <= 0.000001f;
    }
}

using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfAmbientBuilder
{
    private const int VertexNormalHeaderAmbientWordCount = 2;
    private const int VertexNormalRemapTargetIndexMask = 0x3FFC;
    private const float AmbientNormalSelectionMinimumDot = 0.85f;
    private const float AmbientNormalSelectionTieEpsilon = 0.000001f;

    public static TieGltfAmbientBuildResult BuildIndices(
        TieClass tie,
        TieLodTopology topology,
        string? preferredTargetMode,
        int vertexCount,
        IReadOnlyList<uint> indexVertexIndices,
        IReadOnlyList<Vector3> indexNormals,
        string? sourceNormalLayout)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);

        var ambientWordCount = tie.Header.AmbientSize > 0
            ? tie.Header.AmbientSize / sizeof(ushort)
            : 0;
        var colorRecipes = BuildColorRecipes(tie, topology.LodIndex, ambientWordCount);
        var colorRecipesByTargetIndex = colorRecipes.ToDictionary(recipe => recipe.TargetIndex);
        var ambientSlotCount = Math.Max(
            ambientWordCount,
            colorRecipes.Count == 0 ? 0 : colorRecipes.Max(recipe => recipe.TargetIndex) + 1);
        if (ambientWordCount <= VertexNormalHeaderAmbientWordCount
            || tie.VertexNormals.Count == 0
            || vertexCount <= 0)
        {
            return new TieGltfAmbientBuildResult(
                [],
                [],
                ambientWordCount,
                ambientSlotCount,
                VertexNormalHeaderAmbientWordCount,
                0,
                0,
                0,
                0,
                colorRecipes,
                null);
        }

        var remapByRawTarget = new Dictionary<int, List<TieVertexNormalRemap>>();
        var remapByLogicalVertexIndex = new Dictionary<int, List<TieVertexNormalRemap>>();
        var remapByPacketVertexRow = new Dictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>>();
        foreach (var remap in tie.VertexNormalRemaps)
        {
            if (remap.LodIndex != topology.LodIndex)
            {
                continue;
            }

            AddRemap(remapByRawTarget, DecodeNormalRemapTargetIndex(remap.RawVertex), remap);
            if (remap.LogicalVertexIndex is { } logicalVertexIndex)
            {
                AddRemap(remapByLogicalVertexIndex, logicalVertexIndex, remap);
            }

            if (remap.LogicalVertexIndex is null && remap.VertexRowIndex >= 0)
            {
                AddRemap(remapByPacketVertexRow, (remap.PacketIndex, remap.VertexRowIndex), remap);
            }
        }

        var packetDinkyUploadBases = TieGltfNormalRemapTargetResolver.BuildPacketDinkyUploadBases(tie, topology);
        var packetUploadLayouts = TieGltfNormalRemapTargetResolver.BuildPacketUploadLayouts(tie, topology);
        var primaryTargetMode = ResolveTargetMode(preferredTargetMode, remapByLogicalVertexIndex.Count > 0);
        var allowFallbackTargetMode = primaryTargetMode is not TieGltfVertexNormalRemapTargetMode.VuAddress
            and not TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload;
        var fallbackTargetMode = primaryTargetMode == TieGltfVertexNormalRemapTargetMode.LogicalVertex
            ? TieGltfVertexNormalRemapTargetMode.PacketVertexRow
            : TieGltfVertexNormalRemapTargetMode.LogicalVertex;
        var sourceNormalTableLayout = ResolveSourceNormalTableLayout(sourceNormalLayout);
        var indices = Enumerable.Repeat(-1f, vertexCount).ToList();
        var resolvedVertexCount = 0;
        var outOfRangeVertexCount = 0;
        var fallbackResolvedVertexCount = 0;
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            var rowIndex = vertex.VertexRowIndex ?? vertex.AddressRowIndex;
            if (rowIndex is null
                || vertex.LogicalVertexIndex < 0
                || vertex.LogicalVertexIndex >= indices.Count)
            {
                continue;
            }

            if (TryResolveColorRecipeIndex(
                vertex,
                packetUploadLayouts,
                colorRecipesByTargetIndex,
                out var targetAmbientIndex))
            {
                indices[vertex.LogicalVertexIndex] = targetAmbientIndex;
                resolvedVertexCount++;
                continue;
            }

            if (!TryResolveRemaps(
                    vertex,
                    rowIndex.Value,
                    primaryTargetMode,
                    fallbackTargetMode,
                    allowFallbackTargetMode,
                    packetDinkyUploadBases,
                    remapByRawTarget,
                    remapByLogicalVertexIndex,
                    remapByPacketVertexRow,
                    out var remaps,
                    out var usedFallbackTargetMode)
                || remaps.Count == 0)
            {
                continue;
            }

            var normalIndex = remaps
                .Where(remap => remap.NormalIndex >= 0 && remap.NormalIndex < tie.VertexNormals.Count)
                .OrderBy(remap => remap.Offset)
                .Select(remap => (int?)remap.NormalIndex)
                .FirstOrDefault();
            if (!normalIndex.HasValue)
            {
                continue;
            }

            var ambientIndex = normalIndex.Value + VertexNormalHeaderAmbientWordCount;
            if (ambientIndex >= ambientWordCount)
            {
                outOfRangeVertexCount++;
                continue;
            }

            indices[vertex.LogicalVertexIndex] = ambientIndex;
            resolvedVertexCount++;
            if (usedFallbackTargetMode)
            {
                fallbackResolvedVertexCount++;
            }
        }

        var indexIndices = BuildIndexAmbientIndices();
        return new TieGltfAmbientBuildResult(
            resolvedVertexCount > 0 ? indices : [],
            indexIndices,
            ambientWordCount,
            ambientSlotCount,
            VertexNormalHeaderAmbientWordCount,
            resolvedVertexCount,
            outOfRangeVertexCount,
            fallbackResolvedVertexCount,
            indexIndices.Count == 0 ? 0 : indexIndices.Count(index => index >= 0f),
            colorRecipes,
            remapByRawTarget.Count > 0 ? primaryTargetMode.ToString() : null);

        List<float> BuildIndexAmbientIndices()
        {
            if (indexVertexIndices.Count == 0 || indexVertexIndices.Count != indexNormals.Count)
            {
                return [];
            }

            var vertexByLogicalIndex = topology.LogicalVertices
                .Where(vertex => vertex.LogicalVertexIndex >= 0)
                .GroupBy(vertex => vertex.LogicalVertexIndex)
                .ToDictionary(group => group.Key, group => group.First());
            var indexOffsetsByVertex = BuildIndexOffsetsByVertex(indexVertexIndices);
            var ambientIndices = Enumerable.Repeat(-1f, indexVertexIndices.Count).ToList();
            for (var indexOffset = 0; indexOffset < indexVertexIndices.Count; indexOffset++)
            {
                var logicalVertexIndex = checked((int)indexVertexIndices[indexOffset]);
                if (!vertexByLogicalIndex.TryGetValue(logicalVertexIndex, out var vertex))
                {
                    continue;
                }

                var rowIndex = vertex.VertexRowIndex ?? vertex.AddressRowIndex;
                if (rowIndex is null)
                {
                    continue;
                }

                if (TryResolveColorRecipeIndex(
                    vertex,
                    packetUploadLayouts,
                    colorRecipesByTargetIndex,
                    out var recipeAmbientIndex))
                {
                    ambientIndices[indexOffset] = recipeAmbientIndex;
                    continue;
                }

                if (!TryResolveRemaps(
                        vertex,
                        rowIndex.Value,
                        primaryTargetMode,
                        fallbackTargetMode,
                        allowFallbackTargetMode,
                        packetDinkyUploadBases,
                        remapByRawTarget,
                        remapByLogicalVertexIndex,
                        remapByPacketVertexRow,
                        out var remaps,
                        out _)
                    || !TrySelectAmbientNormalIndex(
                        tie,
                        remaps,
                        sourceNormalTableLayout,
                        logicalVertexIndex,
                        indexOffset,
                        indexOffsetsByVertex,
                        indexNormals[indexOffset],
                        out var normalIndex))
                {
                    continue;
                }

                var ambientIndex = normalIndex + VertexNormalHeaderAmbientWordCount;
                if (ambientIndex >= ambientWordCount)
                {
                    continue;
                }

                ambientIndices[indexOffset] = ambientIndex;
            }

            return ambientIndices.Any(index => index >= 0f) ? ambientIndices : [];
        }
    }

    private static List<TieGltfAmbientColorRecipe> BuildColorRecipes(
        TieClass tie,
        int lodIndex,
        int ambientWordCount)
    {
        if (ambientWordCount <= VertexNormalHeaderAmbientWordCount
            || tie.RgbaRemapOperations.Count == 0)
        {
            return [];
        }

        var recipesByTargetIndex = new Dictionary<int, TieGltfAmbientColorRecipe>();
        foreach (var operation in tie.RgbaRemapOperations
                     .Where(operation => operation.LodIndex == lodIndex)
                     .OrderBy(operation => operation.GroupIndex)
                     .ThenBy(operation => operation.Offset)
                     .ThenBy(operation => operation.OperationIndex))
        {
            var sourceIndices = operation.SourceSlots
                .Select(slot => slot + VertexNormalHeaderAmbientWordCount)
                .ToArray();
            if (sourceIndices.Length == 0
                || sourceIndices.Any(index => index < VertexNormalHeaderAmbientWordCount || index >= ambientWordCount))
            {
                continue;
            }

            var targetIndex = operation.TargetCacheSlot + VertexNormalHeaderAmbientWordCount;
            if (targetIndex < VertexNormalHeaderAmbientWordCount)
            {
                continue;
            }

            recipesByTargetIndex[targetIndex] = new TieGltfAmbientColorRecipe(
                targetIndex,
                sourceIndices,
                sourceIndices.Length,
                operation.Kind.ToString());
        }

        return recipesByTargetIndex.Values
            .OrderBy(recipe => recipe.TargetIndex)
            .ToList();
    }

    private static bool TryResolveColorRecipeIndex(
        TieLogicalVertex vertex,
        IReadOnlyDictionary<int, TieGltfPacketUploadLayout> packetUploadLayouts,
        IReadOnlyDictionary<int, TieGltfAmbientColorRecipe> colorRecipesByTargetIndex,
        out int ambientIndex)
    {
        ambientIndex = -1;
        if (!TieGltfNormalRemapTargetResolver.TryGetPacketUploadTarget(
                vertex,
                packetUploadLayouts,
                out var targetSlot))
        {
            return false;
        }

        var targetIndex = targetSlot + VertexNormalHeaderAmbientWordCount;
        if (!colorRecipesByTargetIndex.ContainsKey(targetIndex))
        {
            return false;
        }

        ambientIndex = targetIndex;
        return true;
    }

    private static Dictionary<int, List<int>> BuildIndexOffsetsByVertex(IReadOnlyList<uint> indices)
    {
        var indexOffsetsByVertex = new Dictionary<int, List<int>>();
        for (var indexOffset = 0; indexOffset < indices.Count; indexOffset++)
        {
            var logicalVertexIndex = checked((int)indices[indexOffset]);
            if (!indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var indexOffsets))
            {
                indexOffsets = [];
                indexOffsetsByVertex[logicalVertexIndex] = indexOffsets;
            }

            indexOffsets.Add(indexOffset);
        }

        return indexOffsetsByVertex;
    }

    private static TieGltfVertexNormalRemapTargetMode ResolveTargetMode(
        string? preferredTargetMode,
        bool hasLogicalRemaps)
    {
        if (string.Equals(
                preferredTargetMode,
                TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload;
        }

        if (string.Equals(
                preferredTargetMode,
                TieGltfVertexNormalRemapTargetMode.VuAddress.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return TieGltfVertexNormalRemapTargetMode.VuAddress;
        }

        if (string.Equals(
                preferredTargetMode,
                TieGltfVertexNormalRemapTargetMode.PacketVertexRow.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return TieGltfVertexNormalRemapTargetMode.PacketVertexRow;
        }

        return hasLogicalRemaps
            ? TieGltfVertexNormalRemapTargetMode.LogicalVertex
            : TieGltfVertexNormalRemapTargetMode.PacketVertexRow;
    }

    private static bool TryGetRemaps(
        TieLogicalVertex vertex,
        int rowIndex,
        TieGltfVertexNormalRemapTargetMode primaryTargetMode,
        TieGltfVertexNormalRemapTargetMode fallbackTargetMode,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        out IReadOnlyList<TieVertexNormalRemap> remaps)
    {
        if (TryGetRemaps(
                vertex,
                rowIndex,
                primaryTargetMode,
                remapByLogicalVertexIndex,
                remapByPacketVertexRow,
                out remaps))
        {
            return true;
        }

        return TryGetRemaps(
            vertex,
            rowIndex,
            fallbackTargetMode,
            remapByLogicalVertexIndex,
            remapByPacketVertexRow,
            out remaps);
    }

    private static bool TryGetRemaps(
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

    private static bool TryResolveRemaps(
        TieLogicalVertex vertex,
        int rowIndex,
        TieGltfVertexNormalRemapTargetMode primaryTargetMode,
        TieGltfVertexNormalRemapTargetMode fallbackTargetMode,
        bool allowFallbackTargetMode,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByRawTarget,
        IReadOnlyDictionary<int, List<TieVertexNormalRemap>> remapByLogicalVertexIndex,
        IReadOnlyDictionary<(int PacketIndex, int VertexRowIndex), List<TieVertexNormalRemap>> remapByPacketVertexRow,
        out IReadOnlyList<TieVertexNormalRemap> remaps,
        out bool usedFallbackTargetMode)
    {
        usedFallbackTargetMode = false;
        if (primaryTargetMode == TieGltfVertexNormalRemapTargetMode.PacketDinkyUpload
            && TieGltfNormalRemapTargetResolver.TryGetPacketDinkyUploadTarget(
                vertex,
                packetDinkyUploadBases,
                out var packetDinkyUploadTarget)
            && remapByRawTarget.TryGetValue(packetDinkyUploadTarget, out var packetDinkyUploadRemaps)
            && packetDinkyUploadRemaps.Count > 0)
        {
            remaps = packetDinkyUploadRemaps;
            return true;
        }

        if (primaryTargetMode == TieGltfVertexNormalRemapTargetMode.VuAddress
            && remapByRawTarget.TryGetValue(vertex.VuAddress, out var vuAddressRemaps)
            && vuAddressRemaps.Count > 0)
        {
            remaps = vuAddressRemaps;
            return true;
        }

        if (!allowFallbackTargetMode)
        {
            remaps = [];
            return false;
        }

        if (TryGetRemaps(
                vertex,
                rowIndex,
                primaryTargetMode,
                fallbackTargetMode,
                remapByLogicalVertexIndex,
                remapByPacketVertexRow,
                out remaps)
            && remaps.Count > 0)
        {
            usedFallbackTargetMode = true;
            return true;
        }

        remaps = [];
        return false;
    }

    private static bool TrySelectAmbientNormalIndex(
        TieClass tie,
        IReadOnlyList<TieVertexNormalRemap> remaps,
        TieGltfRawSourceNormalLayout sourceNormalTableLayout,
        int logicalVertexIndex,
        int indexOffset,
        IReadOnlyDictionary<int, List<int>> indexOffsetsByVertex,
        Vector3? indexNormal,
        out int normalIndex)
    {
        normalIndex = -1;
        var candidates = new List<(int NormalIndex, Vector3 SourceNormal)>();
        foreach (var remap in remaps
                     .Where(remap => remap.NormalIndex >= 0 && remap.NormalIndex < tie.VertexNormals.Count)
                     .OrderBy(remap => remap.Offset))
        {
            if (TryNormalizeGltfNormal(
                    tie.VertexNormals[remap.NormalIndex],
                    sourceNormalTableLayout,
                    out var sourceNormal))
            {
                candidates.Add((remap.NormalIndex, sourceNormal));
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        if (indexOffsetsByVertex.TryGetValue(logicalVertexIndex, out var vertexIndexOffsets)
            && candidates.Count == vertexIndexOffsets.Count)
        {
            var occurrenceIndex = vertexIndexOffsets.IndexOf(indexOffset);
            if (occurrenceIndex >= 0 && occurrenceIndex < candidates.Count)
            {
                normalIndex = candidates[occurrenceIndex].NormalIndex;
                return true;
            }
        }

        if (!indexNormal.HasValue || indexNormal.Value.LengthSquared() <= 1e-12f)
        {
            normalIndex = candidates[0].NormalIndex;
            return true;
        }

        var targetNormal = Vector3.Normalize(indexNormal.Value);
        var bestDot = -1f;
        var bestNormalIndex = -1;
        var ambiguous = false;
        foreach (var candidate in candidates)
        {
            var dot = MathF.Abs(Vector3.Dot(candidate.SourceNormal, targetNormal));
            if (dot > bestDot + AmbientNormalSelectionTieEpsilon)
            {
                bestDot = dot;
                bestNormalIndex = candidate.NormalIndex;
                ambiguous = false;
                continue;
            }

            if (MathF.Abs(dot - bestDot) <= AmbientNormalSelectionTieEpsilon
                && candidate.NormalIndex != bestNormalIndex)
            {
                ambiguous = true;
            }
        }

        if (ambiguous || bestDot < AmbientNormalSelectionMinimumDot)
        {
            return false;
        }

        normalIndex = bestNormalIndex;
        return true;
    }

    private static void AddRemap<TKey>(
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

    private static int DecodeNormalRemapTargetIndex(ushort rawIndex)
    {
        return (rawIndex & VertexNormalRemapTargetIndexMask) / 4;
    }

    private static TieGltfRawSourceNormalLayout ResolveSourceNormalTableLayout(string? sourceNormalLayout)
    {
        return TieGltfRawSourceNormalLayout.TryParse(sourceNormalLayout, out var layout)
            ? layout
            : TieGltfRawSourceNormalLayout.Default;
    }

    private static bool TryNormalizeGltfNormal(
        TieVertexNormal source,
        TieGltfRawSourceNormalLayout layout,
        out Vector3 normal)
    {
        normal = layout.Apply(source);
        if (normal.LengthSquared() <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(normal);
        return true;
    }
}

internal sealed record TieGltfAmbientBuildResult(
    List<float> Indices,
    List<float> IndexIndices,
    int AmbientWordCount,
    int AmbientSlotCount,
    int NormalIndexOffset,
    int ResolvedVertexCount,
    int OutOfRangeVertexCount,
    int FallbackResolvedVertexCount,
    int ResolvedIndexCount,
    IReadOnlyList<TieGltfAmbientColorRecipe> ColorRecipes,
    string? TargetMode);

internal sealed record TieGltfAmbientColorRecipe(
    int TargetIndex,
    IReadOnlyList<int> SourceIndices,
    int Divisor,
    string Kind);

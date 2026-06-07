using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfGeneratedNormalBuilder
{
    internal const float FlatHorizontalGeneratedNormalY = 0.999f;

    private const float SmoothNormalMinimumFaceDot = 0.8f;
    private const float SmoothDuplicatePositionNormalMinimumDot = 0.8f;
    private const float FlatFaceIndexNormalMinimumDot = 0.88f;
    private const float OpposedFaceIndexNormalMinimumDot = 0f;
    private const float VerticalFaceNormalYClamp = 0.01f;
    private const int FullDuplicatePositionNormalWeldMinimumPairCount = 32;
    private const float FullDuplicatePositionNormalWeldMinimumIncompatibleRatio = 0.35f;
    private const float FullDuplicatePositionNormalWeldMinimumAverageFaceDot = 0.97f;
    private const float FullDuplicatePositionNormalWeldMinimumFaceDot = 0.55f;
    private const float FullDuplicatePositionNormalWeldMaximumAverageFaceDotDrop = 0.03f;
    private const float FullDuplicatePositionNormalWeldBackfaceMinimumDot = 0.5f;
    private const float FullDuplicatePositionNormalWeldHighConfidenceIncompatibleRatio = 0.05f;
    private const float FullDuplicatePositionNormalWeldHighConfidenceCurrentAverageFaceDot = 0.99f;
    private const float FullDuplicatePositionNormalWeldHighConfidenceAverageFaceDot = 0.99f;
    private const float FullDuplicatePositionNormalWeldHighConfidenceMinimumFaceDot = 0.85f;
    private const float FullDuplicatePositionNormalWeldHighConfidenceMaximumAverageFaceDotDrop = 0.01f;

    public static List<Vector3> BuildGeneratedNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices)
    {
        var bounds = GetPositionBounds(positions);
        var weldDuplicatePositions = IsFlatHorizontalBounds(bounds);
        var normals = new Vector3[positions.Count];
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = checked((int)indices[i]);
            var bIndex = checked((int)indices[i + 1]);
            var cIndex = checked((int)indices[i + 2]);
            var a = positions[aIndex];
            var b = positions[bIndex];
            var c = positions[cIndex];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            var faceNormal = Vector3.Normalize(normal);
            if (weldDuplicatePositions && faceNormal.Y < 0f)
            {
                faceNormal = -faceNormal;
            }

            normals[aIndex] += faceNormal;
            normals[bIndex] += faceNormal;
            normals[cIndex] += faceNormal;
        }

        if (weldDuplicatePositions)
        {
            WeldNormalsByPosition(positions, normals);
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() <= 1e-12f
                ? Vector3.UnitY
                : Vector3.Normalize(normals[i]);
        }

        return normals.ToList();
    }

    public static List<Vector3> BuildGeneratedIndexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices)
    {
        var triangleCount = indices.Count / 3;
        var faceNormals = new Vector3[triangleCount];
        var indexOffsetsByVertex = BuildIndexOffsetsByVertex(indices);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = checked((int)indices[i]);
            var bIndex = checked((int)indices[i + 1]);
            var cIndex = checked((int)indices[i + 2]);
            var normal = Vector3.Cross(
                positions[bIndex] - positions[aIndex],
                positions[cIndex] - positions[aIndex]);
            faceNormals[i / 3] = normal.LengthSquared() <= 1e-12f
                ? Vector3.Zero
                : Vector3.Normalize(normal);
        }

        var normals = new Vector3[indices.Count];
        for (var indexOffset = 0; indexOffset < indices.Count; indexOffset++)
        {
            var sourceIndex = checked((int)indices[indexOffset]);
            var faceNormal = faceNormals[indexOffset / 3];
            if (faceNormal.LengthSquared() <= 1e-12f)
            {
                normals[indexOffset] = Vector3.UnitY;
                continue;
            }

            var sum = Vector3.Zero;
            if (indexOffsetsByVertex.TryGetValue(sourceIndex, out var relatedIndexOffsets))
            {
                foreach (var relatedIndexOffset in relatedIndexOffsets)
                {
                    var relatedFaceNormal = faceNormals[relatedIndexOffset / 3];
                    if (relatedFaceNormal.LengthSquared() > 1e-12f
                        && Vector3.Dot(faceNormal, relatedFaceNormal) >= SmoothNormalMinimumFaceDot)
                    {
                        sum += relatedFaceNormal;
                    }
                }
            }

            normals[indexOffset] = sum.LengthSquared() <= 1e-12f
                ? faceNormal
                : Vector3.Normalize(sum);
        }

        return normals.ToList();
    }

    public static List<Vector3> BuildIndexNormalsFromVertexNormals(
        IReadOnlyList<Vector3> vertexNormals,
        IReadOnlyList<uint> indices)
    {
        var indexNormals = new List<Vector3>(indices.Count);
        foreach (var index in indices)
        {
            var vertexIndex = checked((int)index);
            indexNormals.Add(vertexIndex >= 0 && vertexIndex < vertexNormals.Count
                ? vertexNormals[vertexIndex]
                : Vector3.UnitY);
        }

        return indexNormals;
    }

    public static void RestoreStronglyTiltedFlatFaceIndexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        Vector3[] indexNormals,
        bool flipDownwardHorizontalFaces,
        bool restoreOpposedNonHorizontalFaces)
    {
        var indexCount = Math.Min(indices.Count, indexNormals.Length);
        for (var i = 0; i + 2 < indexCount; i += 3)
        {
            var aIndex = checked((int)indices[i]);
            var bIndex = checked((int)indices[i + 1]);
            var cIndex = checked((int)indices[i + 2]);
            var normal = Vector3.Cross(
                positions[bIndex] - positions[aIndex],
                positions[cIndex] - positions[aIndex]);
            if (normal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            var faceNormal = Vector3.Normalize(normal);
            if (flipDownwardHorizontalFaces && faceNormal.Y < 0f)
            {
                faceNormal = -faceNormal;
            }

            faceNormal = ClampNearVerticalFaceNormalY(faceNormal);
            var minimumDot = FlatFaceIndexNormalMinimumDot;
            if (MathF.Abs(faceNormal.Y) < FlatHorizontalGeneratedNormalY)
            {
                if (!restoreOpposedNonHorizontalFaces)
                {
                    continue;
                }

                minimumDot = OpposedFaceIndexNormalMinimumDot;
            }

            RestoreIndexNormal(i);
            RestoreIndexNormal(i + 1);
            RestoreIndexNormal(i + 2);

            void RestoreIndexNormal(int indexOffset)
            {
                if (Vector3.Dot(faceNormal, indexNormals[indexOffset]) < minimumDot)
                {
                    indexNormals[indexOffset] = faceNormal;
                }
            }
        }
    }

    public static Dictionary<int, List<int>> BuildIndexOffsetsByVertex(IReadOnlyList<uint> indices)
    {
        var indexOffsetsByVertex = new Dictionary<int, List<int>>();
        for (var i = 0; i < indices.Count; i++)
        {
            var vertexIndex = checked((int)indices[i]);
            if (!indexOffsetsByVertex.TryGetValue(vertexIndex, out var indexOffsets))
            {
                indexOffsets = [];
                indexOffsetsByVertex[vertexIndex] = indexOffsets;
            }

            indexOffsets.Add(i);
        }

        return indexOffsetsByVertex;
    }

    public static TieGltfDuplicatePositionNormalWeldDecision EvaluateDuplicatePositionIndexNormalWeld(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        Vector3[] indexNormals)
    {
        var duplicatePairCount = 0;
        var incompatiblePairCount = 0;
        foreach (var indexOffsets in BuildIndexOffsetsByPosition(positions, indices, indexNormals.Length).Values)
        {
            if (indexOffsets.Count < 2)
            {
                continue;
            }

            for (var i = 0; i < indexOffsets.Count; i++)
            {
                var a = indexNormals[indexOffsets[i]];
                for (var j = i + 1; j < indexOffsets.Count; j++)
                {
                    duplicatePairCount++;
                    if (Vector3.Dot(a, indexNormals[indexOffsets[j]]) < SmoothDuplicatePositionNormalMinimumDot)
                    {
                        incompatiblePairCount++;
                    }
                }
            }
        }

        if (duplicatePairCount < FullDuplicatePositionNormalWeldMinimumPairCount)
        {
            return TieGltfDuplicatePositionNormalWeldDecision.Compatible(duplicatePairCount, incompatiblePairCount);
        }

        var incompatibleRatio = incompatiblePairCount / (float)duplicatePairCount;
        var currentScore = ScoreIndexNormalFaceAlignment(positions, indices, indexNormals);
        var weldedIndexNormals = indexNormals.ToArray();
        WeldIndexNormalsByPosition(positions, indices, weldedIndexNormals);
        var weldedScore = ScoreIndexNormalFaceAlignment(positions, indices, weldedIndexNormals);

        if (weldedScore.CheckedTriangleCount == 0
            || weldedScore.BackfaceTriangleCount != 0)
        {
            return TieGltfDuplicatePositionNormalWeldDecision.Compatible(
                duplicatePairCount,
                incompatiblePairCount,
                currentScore,
                weldedScore);
        }

        var averageDotDrop = currentScore.AverageDot - weldedScore.AverageDot;
        var shouldWeld = incompatibleRatio >= FullDuplicatePositionNormalWeldMinimumIncompatibleRatio
                && weldedScore.MinimumDot >= FullDuplicatePositionNormalWeldMinimumFaceDot
                && weldedScore.AverageDot >= FullDuplicatePositionNormalWeldMinimumAverageFaceDot
                && averageDotDrop <= FullDuplicatePositionNormalWeldMaximumAverageFaceDotDrop
            || incompatibleRatio >= FullDuplicatePositionNormalWeldHighConfidenceIncompatibleRatio
                && currentScore.AverageDot >= FullDuplicatePositionNormalWeldHighConfidenceCurrentAverageFaceDot
                && weldedScore.MinimumDot >= FullDuplicatePositionNormalWeldHighConfidenceMinimumFaceDot
                && weldedScore.AverageDot >= FullDuplicatePositionNormalWeldHighConfidenceAverageFaceDot
                && averageDotDrop <= FullDuplicatePositionNormalWeldHighConfidenceMaximumAverageFaceDotDrop;
        return new TieGltfDuplicatePositionNormalWeldDecision(
            shouldWeld,
            shouldWeld ? "Full" : "Compatible",
            duplicatePairCount,
            incompatiblePairCount,
            currentScore,
            weldedScore);
    }

    public static void WeldIndexNormalsByPosition(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        Vector3[] indexNormals)
    {
        foreach (var indexOffsets in BuildIndexOffsetsByPosition(positions, indices, indexNormals.Length).Values)
        {
            if (indexOffsets.Count < 2)
            {
                continue;
            }

            var normal = AverageIndexNormal(indexNormals, indexOffsets);
            foreach (var indexOffset in indexOffsets)
            {
                indexNormals[indexOffset] = normal;
            }
        }
    }

    public static void SmoothCompatibleIndexNormalsByPosition(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        Vector3[] indexNormals)
    {
        foreach (var indexOffsets in BuildIndexOffsetsByPosition(positions, indices, indexNormals.Length).Values)
        {
            if (indexOffsets.Count < 2)
            {
                continue;
            }

            var clusters = new List<List<int>>();
            foreach (var indexOffset in indexOffsets)
            {
                var normal = indexNormals[indexOffset];
                var clusterIndex = -1;
                var bestDot = SmoothDuplicatePositionNormalMinimumDot;
                for (var i = 0; i < clusters.Count; i++)
                {
                    var clusterNormal = AverageIndexNormal(indexNormals, clusters[i]);
                    var dot = Vector3.Dot(normal, clusterNormal);
                    if (dot >= bestDot)
                    {
                        bestDot = dot;
                        clusterIndex = i;
                    }
                }

                if (clusterIndex >= 0)
                {
                    clusters[clusterIndex].Add(indexOffset);
                }
                else
                {
                    clusters.Add([indexOffset]);
                }
            }

            foreach (var cluster in clusters)
            {
                if (cluster.Count < 2)
                {
                    continue;
                }

                var normal = AverageIndexNormal(indexNormals, cluster);
                foreach (var indexOffset in cluster)
                {
                    indexNormals[indexOffset] = normal;
                }
            }
        }
    }

    public static (Vector3 Min, Vector3 Max) GetPositionBounds(IReadOnlyList<Vector3> positions)
    {
        if (positions.Count == 0)
        {
            return (Vector3.Zero, Vector3.Zero);
        }

        var min = positions[0];
        var max = positions[0];
        foreach (var position in positions.Skip(1))
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        return (min, max);
    }

    public static bool IsFlatHorizontalBounds((Vector3 Min, Vector3 Max) bounds)
    {
        const float flatHeightRatio = 0.15f;

        var extents = bounds.Max - bounds.Min;
        var horizontalExtent = MathF.Max(MathF.Abs(extents.X), MathF.Abs(extents.Z));
        return horizontalExtent > 1e-5f && MathF.Abs(extents.Y) <= horizontalExtent * flatHeightRatio;
    }

    public static void WeldNormalsByPosition(IReadOnlyList<Vector3> positions, Vector3[] normals)
    {
        var sumsByPosition = new Dictionary<TieGltfPositionKey, Vector3>();
        for (var i = 0; i < positions.Count && i < normals.Length; i++)
        {
            var key = TieGltfPositionKey.From(positions[i]);
            sumsByPosition.TryGetValue(key, out var sum);
            sumsByPosition[key] = sum + normals[i];
        }

        for (var i = 0; i < positions.Count && i < normals.Length; i++)
        {
            var sum = sumsByPosition[TieGltfPositionKey.From(positions[i])];
            normals[i] = sum.LengthSquared() <= 1e-12f
                ? normals[i]
                : Vector3.Normalize(sum);
        }
    }

    public static void RestoreFlatHorizontalGeneratedNormals(
        IReadOnlyList<Vector3> generatedNormals,
        Vector3[] normals,
        HashSet<int> sourceVertexIndices)
    {
        for (var i = 0; i < generatedNormals.Count && i < normals.Length; i++)
        {
            var generatedNormal = generatedNormals[i];
            if (generatedNormal.Y < FlatHorizontalGeneratedNormalY)
            {
                continue;
            }

            normals[i] = generatedNormal;
            sourceVertexIndices.Remove(i);
        }
    }

    private static TieGltfIndexNormalFaceAlignmentScore ScoreIndexNormalFaceAlignment(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        IReadOnlyList<Vector3> indexNormals)
    {
        var checkedTriangleCount = 0;
        var backfaceTriangleCount = 0;
        var minimumDot = 1f;
        var dotSum = 0f;
        var indexCount = Math.Min(indices.Count, indexNormals.Count);
        for (var i = 0; i + 2 < indexCount; i += 3)
        {
            var aIndex = checked((int)indices[i]);
            var bIndex = checked((int)indices[i + 1]);
            var cIndex = checked((int)indices[i + 2]);
            var normal = Vector3.Cross(
                positions[bIndex] - positions[aIndex],
                positions[cIndex] - positions[aIndex]);
            if (normal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            var averageNormal = indexNormals[i] + indexNormals[i + 1] + indexNormals[i + 2];
            if (averageNormal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            var dot = Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(averageNormal));
            checkedTriangleCount++;
            dotSum += dot;
            minimumDot = MathF.Min(minimumDot, dot);
            if (dot < FullDuplicatePositionNormalWeldBackfaceMinimumDot)
            {
                backfaceTriangleCount++;
            }
        }

        return new TieGltfIndexNormalFaceAlignmentScore(
            checkedTriangleCount,
            backfaceTriangleCount,
            minimumDot,
            checkedTriangleCount == 0 ? 0f : dotSum / checkedTriangleCount);
    }

    private static Dictionary<TieGltfPositionKey, List<int>> BuildIndexOffsetsByPosition(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        int indexNormalCount)
    {
        var indexOffsetsByPosition = new Dictionary<TieGltfPositionKey, List<int>>();
        for (var i = 0; i < indices.Count && i < indexNormalCount; i++)
        {
            var vertexIndex = checked((int)indices[i]);
            if (vertexIndex < 0 || vertexIndex >= positions.Count)
            {
                continue;
            }

            var key = TieGltfPositionKey.From(positions[vertexIndex]);
            if (!indexOffsetsByPosition.TryGetValue(key, out var indexOffsets))
            {
                indexOffsets = [];
                indexOffsetsByPosition[key] = indexOffsets;
            }

            indexOffsets.Add(i);
        }

        return indexOffsetsByPosition;
    }

    private static Vector3 AverageIndexNormal(IReadOnlyList<Vector3> indexNormals, IReadOnlyList<int> indexOffsets)
    {
        var sum = Vector3.Zero;
        foreach (var indexOffset in indexOffsets)
        {
            if (indexOffset >= 0 && indexOffset < indexNormals.Count)
            {
                sum += indexNormals[indexOffset];
            }
        }

        return sum.LengthSquared() <= 1e-12f
            ? Vector3.UnitY
            : Vector3.Normalize(sum);
    }

    private static Vector3 ClampNearVerticalFaceNormalY(Vector3 normal)
    {
        if (normal.Y != 0f && MathF.Abs(normal.Y) < VerticalFaceNormalYClamp)
        {
            var clamped = new Vector3(normal.X, 0f, normal.Z);
            return clamped.LengthSquared() <= 1e-12f
                ? normal
                : Vector3.Normalize(clamped);
        }

        return normal;
    }
}

internal readonly record struct TieGltfIndexNormalFaceAlignmentScore(
    int CheckedTriangleCount,
    int BackfaceTriangleCount,
    float MinimumDot,
    float AverageDot);

internal readonly record struct TieGltfDuplicatePositionNormalWeldDecision(
    bool ShouldWeld,
    string Mode,
    int DuplicatePairCount,
    int IncompatiblePairCount,
    TieGltfIndexNormalFaceAlignmentScore CurrentScore,
    TieGltfIndexNormalFaceAlignmentScore WeldedScore)
{
    public static TieGltfDuplicatePositionNormalWeldDecision None { get; } = new(
        false,
        "None",
        0,
        0,
        default,
        default);

    public static TieGltfDuplicatePositionNormalWeldDecision Compatible(
        int duplicatePairCount,
        int incompatiblePairCount,
        TieGltfIndexNormalFaceAlignmentScore currentScore = default,
        TieGltfIndexNormalFaceAlignmentScore weldedScore = default)
    {
        return new TieGltfDuplicatePositionNormalWeldDecision(
            false,
            "Compatible",
            duplicatePairCount,
            incompatiblePairCount,
            currentScore,
            weldedScore);
    }
}

internal readonly record struct TieGltfPositionKey(int X, int Y, int Z)
{
    public static TieGltfPositionKey From(Vector3 position)
    {
        const float scale = 100000f;
        return new TieGltfPositionKey(
            (int)MathF.Round(position.X * scale),
            (int)MathF.Round(position.Y * scale),
            (int)MathF.Round(position.Z * scale));
    }
}

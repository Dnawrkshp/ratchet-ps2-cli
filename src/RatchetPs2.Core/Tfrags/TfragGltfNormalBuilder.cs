using System.Numerics;

namespace RatchetPs2.Core.Tfrags;

internal static class TfragGltfNormalBuilder
{
    private const float FlatHorizontalGeneratedNormalY = 0.999f;
    private const float SmoothNormalMinimumFaceDot = 0.8f;
    private const float SmoothDuplicatePositionNormalMinimumDot = 0.8f;
    private const float FlatFaceIndexNormalMinimumDot = 0.88f;
    private const float OpposedFaceIndexNormalMinimumDot = 0f;
    private const float VerticalFaceNormalYClamp = 0.01f;
    private const float DownwardTerrainFaceNormalFlipMinimumY = 0.5f;
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

    public static TfragNormalBuildResult Build(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);

        var flatHorizontalBounds = IsFlatHorizontalBounds(GetPositionBounds(positions));
        var vertexNormals = BuildGeneratedVertexNormals(
            positions,
            indices,
            flipDownwardHorizontalFaces: flatHorizontalBounds).ToArray();
        var indexNormals = flatHorizontalBounds
            ? BuildIndexNormalsFromVertexNormals(vertexNormals, indices).ToArray()
            : BuildGeneratedIndexNormals(positions, indices, flipDownwardHorizontalFaces: flatHorizontalBounds).ToArray();
        var duplicatePositionNormalWeldDecision = TfragDuplicatePositionNormalWeldDecision.None;
        var restoredFaceNormalIndexCount = 0;

        if (flatHorizontalBounds)
        {
            WeldNormalsByPosition(positions, vertexNormals);
            indexNormals = BuildIndexNormalsFromVertexNormals(vertexNormals, indices).ToArray();
            restoredFaceNormalIndexCount = RestoreStronglyTiltedFlatFaceIndexNormals(
                positions,
                indices,
                indexNormals,
                flipDownwardHorizontalFaces: true,
                restoreOpposedNonHorizontalFaces: true);
            vertexNormals = BuildVertexNormalsFromIndexNormals(positions.Count, indices, indexNormals).ToArray();
        }
        else
        {
            duplicatePositionNormalWeldDecision = EvaluateDuplicatePositionIndexNormalWeld(
                positions,
                indices,
                indexNormals);
            if (duplicatePositionNormalWeldDecision.ShouldWeld)
            {
                WeldIndexNormalsByPosition(positions, indices, indexNormals);
            }
            else
            {
                SmoothCompatibleIndexNormalsByPosition(positions, indices, indexNormals);
            }

            restoredFaceNormalIndexCount = RestoreStronglyTiltedFlatFaceIndexNormals(
                positions,
                indices,
                indexNormals,
                flipDownwardHorizontalFaces: false,
                restoreOpposedNonHorizontalFaces: false);
            vertexNormals = BuildVertexNormalsFromIndexNormals(positions.Count, indices, indexNormals).ToArray();
        }

        return new TfragNormalBuildResult(
            vertexNormals,
            indexNormals,
            flatHorizontalBounds,
            duplicatePositionNormalWeldDecision.Mode,
            duplicatePositionNormalWeldDecision.DuplicatePairCount,
            duplicatePositionNormalWeldDecision.IncompatiblePairCount,
            duplicatePositionNormalWeldDecision.CurrentScore.AverageDot,
            duplicatePositionNormalWeldDecision.WeldedScore.AverageDot,
            duplicatePositionNormalWeldDecision.WeldedScore.MinimumDot,
            restoredFaceNormalIndexCount);
    }

    private static List<Vector3> BuildGeneratedVertexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        bool flipDownwardHorizontalFaces)
    {
        var normals = new Vector3[positions.Count];
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            if (!TryGetFaceNormal(positions, indices[i], indices[i + 1], indices[i + 2], out var faceNormal))
            {
                continue;
            }

            faceNormal = OrientTerrainFaceNormal(faceNormal, flipDownwardHorizontalFaces);
            normals[checked((int)indices[i])] += faceNormal;
            normals[checked((int)indices[i + 1])] += faceNormal;
            normals[checked((int)indices[i + 2])] += faceNormal;
        }

        if (flipDownwardHorizontalFaces)
        {
            WeldNormalsByPosition(positions, normals);
        }

        NormalizeNormals(normals);
        return normals.ToList();
    }

    private static List<Vector3> BuildGeneratedIndexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        bool flipDownwardHorizontalFaces)
    {
        var triangleCount = indices.Count / 3;
        var faceNormals = new Vector3[triangleCount];
        var indexOffsetsByVertex = BuildIndexOffsetsByVertex(indices);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            faceNormals[i / 3] = TryGetFaceNormal(positions, indices[i], indices[i + 1], indices[i + 2], out var faceNormal)
                ? OrientTerrainFaceNormal(faceNormal, flipDownwardHorizontalFaces)
                : Vector3.Zero;
        }

        var normals = new Vector3[indices.Count];
        for (var indexOffset = 0; indexOffset < indices.Count; indexOffset++)
        {
            var sourceIndex = checked((int)indices[indexOffset]);
            if (sourceIndex < 0 || sourceIndex >= positions.Count)
            {
                normals[indexOffset] = Vector3.UnitY;
                continue;
            }

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

            normals[indexOffset] = NormalizeTerrainNormal(sum.LengthSquared() <= 1e-12f
                ? faceNormal
                : sum);
        }

        return normals.ToList();
    }

    private static List<Vector3> BuildIndexNormalsFromVertexNormals(
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

    private static List<Vector3> BuildVertexNormalsFromIndexNormals(
        int vertexCount,
        IReadOnlyList<uint> indices,
        IReadOnlyList<Vector3> indexNormals)
    {
        var normals = new Vector3[vertexCount];
        for (var i = 0; i < indices.Count && i < indexNormals.Count; i++)
        {
            var vertexIndex = checked((int)indices[i]);
            if (vertexIndex >= 0 && vertexIndex < normals.Length)
            {
                normals[vertexIndex] += indexNormals[i];
            }
        }

        NormalizeNormals(normals);
        return normals.ToList();
    }

    private static int RestoreStronglyTiltedFlatFaceIndexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        Vector3[] indexNormals,
        bool flipDownwardHorizontalFaces,
        bool restoreOpposedNonHorizontalFaces)
    {
        var restoredIndexCount = 0;
        var indexCount = Math.Min(indices.Count, indexNormals.Length);
        for (var i = 0; i + 2 < indexCount; i += 3)
        {
            if (!TryGetFaceNormal(positions, indices[i], indices[i + 1], indices[i + 2], out var faceNormal))
            {
                continue;
            }

            faceNormal = OrientTerrainFaceNormal(faceNormal, flipDownwardHorizontalFaces);
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
                if (Vector3.Dot(faceNormal, indexNormals[indexOffset]) >= minimumDot)
                {
                    return;
                }

                indexNormals[indexOffset] = faceNormal;
                restoredIndexCount++;
            }
        }

        return restoredIndexCount;
    }

    private static TfragDuplicatePositionNormalWeldDecision EvaluateDuplicatePositionIndexNormalWeld(
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
            return TfragDuplicatePositionNormalWeldDecision.Compatible(duplicatePairCount, incompatiblePairCount);
        }

        var incompatibleRatio = incompatiblePairCount / (float)duplicatePairCount;
        var currentScore = ScoreIndexNormalFaceAlignment(positions, indices, indexNormals);
        var weldedIndexNormals = indexNormals.ToArray();
        WeldIndexNormalsByPosition(positions, indices, weldedIndexNormals);
        var weldedScore = ScoreIndexNormalFaceAlignment(positions, indices, weldedIndexNormals);

        if (weldedScore.CheckedTriangleCount == 0
            || weldedScore.BackfaceTriangleCount != 0)
        {
            return TfragDuplicatePositionNormalWeldDecision.Compatible(
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
        return new TfragDuplicatePositionNormalWeldDecision(
            shouldWeld,
            shouldWeld ? "Full" : "Compatible",
            duplicatePairCount,
            incompatiblePairCount,
            currentScore,
            weldedScore);
    }

    private static void WeldIndexNormalsByPosition(
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

    private static void SmoothCompatibleIndexNormalsByPosition(
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

    private static TfragIndexNormalFaceAlignmentScore ScoreIndexNormalFaceAlignment(
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
            if (!TryGetFaceNormal(positions, indices[i], indices[i + 1], indices[i + 2], out var faceNormal))
            {
                continue;
            }

            faceNormal = OrientTerrainFaceNormal(faceNormal, flipAnyDownwardFaceNormal: false);
            var averageNormal = indexNormals[i] + indexNormals[i + 1] + indexNormals[i + 2];
            if (averageNormal.LengthSquared() <= 1e-12f)
            {
                continue;
            }

            var dot = Vector3.Dot(faceNormal, Vector3.Normalize(averageNormal));
            checkedTriangleCount++;
            dotSum += dot;
            minimumDot = MathF.Min(minimumDot, dot);
            if (dot < FullDuplicatePositionNormalWeldBackfaceMinimumDot)
            {
                backfaceTriangleCount++;
            }
        }

        return new TfragIndexNormalFaceAlignmentScore(
            checkedTriangleCount,
            backfaceTriangleCount,
            minimumDot,
            checkedTriangleCount == 0 ? 0f : dotSum / checkedTriangleCount);
    }

    private static Dictionary<int, List<int>> BuildIndexOffsetsByVertex(IReadOnlyList<uint> indices)
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

    private static Dictionary<TfragGltfPositionKey, List<int>> BuildIndexOffsetsByPosition(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices,
        int indexNormalCount)
    {
        var indexOffsetsByPosition = new Dictionary<TfragGltfPositionKey, List<int>>();
        for (var i = 0; i < indices.Count && i < indexNormalCount; i++)
        {
            var vertexIndex = checked((int)indices[i]);
            if (vertexIndex < 0 || vertexIndex >= positions.Count)
            {
                continue;
            }

            var key = TfragGltfPositionKey.From(positions[vertexIndex]);
            if (!indexOffsetsByPosition.TryGetValue(key, out var indexOffsets))
            {
                indexOffsets = [];
                indexOffsetsByPosition[key] = indexOffsets;
            }

            indexOffsets.Add(i);
        }

        return indexOffsetsByPosition;
    }

    private static (Vector3 Min, Vector3 Max) GetPositionBounds(IReadOnlyList<Vector3> positions)
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

    private static bool IsFlatHorizontalBounds((Vector3 Min, Vector3 Max) bounds)
    {
        const float flatHeightRatio = 0.15f;

        var extents = bounds.Max - bounds.Min;
        var horizontalExtent = MathF.Max(MathF.Abs(extents.X), MathF.Abs(extents.Z));
        return horizontalExtent > 1e-5f && MathF.Abs(extents.Y) <= horizontalExtent * flatHeightRatio;
    }

    private static void WeldNormalsByPosition(IReadOnlyList<Vector3> positions, Vector3[] normals)
    {
        var sumsByPosition = new Dictionary<TfragGltfPositionKey, Vector3>();
        for (var i = 0; i < positions.Count && i < normals.Length; i++)
        {
            var key = TfragGltfPositionKey.From(positions[i]);
            sumsByPosition.TryGetValue(key, out var sum);
            sumsByPosition[key] = sum + normals[i];
        }

        for (var i = 0; i < positions.Count && i < normals.Length; i++)
        {
            var sum = sumsByPosition[TfragGltfPositionKey.From(positions[i])];
            normals[i] = sum.LengthSquared() <= 1e-12f
                ? normals[i]
                : NormalizeTerrainNormal(sum);
        }
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

        return NormalizeTerrainNormal(sum);
    }

    private static void NormalizeNormals(Vector3[] normals)
    {
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = NormalizeTerrainNormal(normals[i]);
        }
    }

    private static bool TryGetFaceNormal(
        IReadOnlyList<Vector3> positions,
        uint a,
        uint b,
        uint c,
        out Vector3 normal)
    {
        normal = Vector3.Zero;
        if (a >= (uint)positions.Count || b >= (uint)positions.Count || c >= (uint)positions.Count)
        {
            return false;
        }

        var cross = Vector3.Cross(
            positions[checked((int)b)] - positions[checked((int)a)],
            positions[checked((int)c)] - positions[checked((int)a)]);
        if (cross.LengthSquared() <= 1e-12f)
        {
            return false;
        }

        normal = Vector3.Normalize(cross);
        return true;
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

    private static Vector3 OrientTerrainFaceNormal(Vector3 faceNormal, bool flipAnyDownwardFaceNormal)
    {
        if (faceNormal.Y < 0f
            && (flipAnyDownwardFaceNormal || faceNormal.Y <= -DownwardTerrainFaceNormalFlipMinimumY))
        {
            return -faceNormal;
        }

        return faceNormal;
    }

    private static Vector3 NormalizeTerrainNormal(Vector3 normal)
    {
        if (normal.LengthSquared() <= 1e-12f)
        {
            return Vector3.UnitY;
        }

        return OrientTerrainFaceNormal(Vector3.Normalize(normal), flipAnyDownwardFaceNormal: false);
    }
}

internal sealed record TfragNormalBuildResult(
    IReadOnlyList<Vector3> VertexNormals,
    IReadOnlyList<Vector3> IndexNormals,
    bool FlatHorizontalBounds,
    string DuplicatePositionNormalWeldMode,
    int DuplicatePositionNormalPairCount,
    int DuplicatePositionIncompatibleNormalPairCount,
    float DuplicatePositionCurrentAverageFaceDot,
    float DuplicatePositionWeldedAverageFaceDot,
    float DuplicatePositionWeldedMinimumFaceDot,
    int RestoredFaceNormalIndexCount);

internal readonly record struct TfragIndexNormalFaceAlignmentScore(
    int CheckedTriangleCount,
    int BackfaceTriangleCount,
    float MinimumDot,
    float AverageDot);

internal readonly record struct TfragDuplicatePositionNormalWeldDecision(
    bool ShouldWeld,
    string Mode,
    int DuplicatePairCount,
    int IncompatiblePairCount,
    TfragIndexNormalFaceAlignmentScore CurrentScore,
    TfragIndexNormalFaceAlignmentScore WeldedScore)
{
    public static TfragDuplicatePositionNormalWeldDecision None { get; } = new(
        false,
        "None",
        0,
        0,
        default,
        default);

    public static TfragDuplicatePositionNormalWeldDecision Compatible(
        int duplicatePairCount,
        int incompatiblePairCount,
        TfragIndexNormalFaceAlignmentScore currentScore = default,
        TfragIndexNormalFaceAlignmentScore weldedScore = default)
    {
        return new TfragDuplicatePositionNormalWeldDecision(
            false,
            "Compatible",
            duplicatePairCount,
            incompatiblePairCount,
            currentScore,
            weldedScore);
    }
}

internal readonly record struct TfragGltfPositionKey(int X, int Y, int Z)
{
    public static TfragGltfPositionKey From(Vector3 position)
    {
        const float scale = 100000f;
        return new TfragGltfPositionKey(
            (int)MathF.Round(position.X * scale),
            (int)MathF.Round(position.Y * scale),
            (int)MathF.Round(position.Z * scale));
    }
}

using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfFlatProfileNormalRepairer
{
    private const float SmoothNormalMinimumFaceDot = 0.8f;
    private const float FlatFaceIndexNormalMinimumDot = 0.88f;
    private const float OpposedFaceIndexNormalMinimumDot = 0f;
    private const float FlatProfileSideFaceIndexNormalMinimumDot = 0.75f;
    private const float FlatProfileSideFaceNormalYMaximum = 0.05f;
    private const float VerticalFaceNormalYClamp = 0.01f;

    public static bool ShouldRestore(IReadOnlyList<Vector3> positions)
    {
        return TieGltfGeneratedNormalBuilder.IsFlatHorizontalBounds(
            TieGltfGeneratedNormalBuilder.GetPositionBounds(positions));
    }

    public static void RestoreFlatProfileExpandedFaceNormals(
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector3>? sourceOnlyNormals,
        List<float>? sourceNormalMask,
        List<float>? sourceNormalStates,
        List<Vector2> texCoords,
        List<Vector2> multipassTexCoords,
        bool includeMultipassTexCoords,
        List<Vector4> glowColors,
        bool includeGlowColors,
        List<float> ambientIndices,
        bool includeAmbientIndices,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups)
    {
        var sideNormalTargets = BuildFlatProfileSideNormalTargets(positions, packetIndexGroups);
        for (var groupIndex = 0; groupIndex < packetIndexGroups.Count; groupIndex++)
        {
            var group = packetIndexGroups[groupIndex];
            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var aIndex = checked((int)group.Indices[i]);
                var bIndex = checked((int)group.Indices[i + 1]);
                var cIndex = checked((int)group.Indices[i + 2]);
                var normal = Vector3.Cross(
                    positions[bIndex] - positions[aIndex],
                    positions[cIndex] - positions[aIndex]);
                if (normal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var faceNormal = Vector3.Normalize(normal);
                if (faceNormal.Y < 0f
                    && MathF.Abs(faceNormal.Y) >= TieGltfGeneratedNormalBuilder.FlatHorizontalGeneratedNormalY)
                {
                    faceNormal = -faceNormal;
                }

                faceNormal = ClampNearVerticalFaceNormalY(faceNormal);
                var isFlatProfileSideFace = MathF.Abs(faceNormal.Y) <= FlatProfileSideFaceNormalYMaximum;
                var minimumDot = FlatFaceIndexNormalMinimumDot;
                if (MathF.Abs(faceNormal.Y) < TieGltfGeneratedNormalBuilder.FlatHorizontalGeneratedNormalY)
                {
                    minimumDot = isFlatProfileSideFace
                        ? FlatProfileSideFaceIndexNormalMinimumDot
                        : OpposedFaceIndexNormalMinimumDot;
                }

                RestoreIndexNormal(i, faceNormal, minimumDot, isFlatProfileSideFace);
                RestoreIndexNormal(i + 1, faceNormal, minimumDot, isFlatProfileSideFace);
                RestoreIndexNormal(i + 2, faceNormal, minimumDot, isFlatProfileSideFace);

                void RestoreIndexNormal(
                    int indexOffset,
                    Vector3 targetFaceNormal,
                    float minimumNormalDot,
                    bool preserveSideSmoothing)
                {
                    var vertexIndex = checked((int)group.Indices[indexOffset]);
                    var currentNormal = normals[vertexIndex];
                    var targetNormal = targetFaceNormal;
                    if (preserveSideSmoothing)
                    {
                        sideNormalTargets.TryGetValue((groupIndex, indexOffset), out var sideSmoothNormal);
                        targetNormal = SelectFlatProfileSideNormal(
                            targetFaceNormal,
                            currentNormal,
                            minimumNormalDot,
                            sideSmoothNormal);
                    }

                    if (Vector3.Dot(targetFaceNormal, currentNormal) >= minimumNormalDot
                        && (!preserveSideSmoothing
                            || MathF.Abs(currentNormal.Y) <= VerticalFaceNormalYClamp
                            && Vector3.Dot(targetNormal, currentNormal) >= 0.999f))
                    {
                        return;
                    }

                    if (NearlyEqual(targetNormal, currentNormal))
                    {
                        return;
                    }

                    var expandedIndex = checked((uint)positions.Count);
                    positions.Add(positions[vertexIndex]);
                    normals.Add(targetNormal);
                    sourceOnlyNormals?.Add(sourceOnlyNormals[vertexIndex]);
                    sourceNormalMask?.Add(sourceNormalMask[vertexIndex]);
                    sourceNormalStates?.Add(sourceNormalStates[vertexIndex]);
                    texCoords.Add(texCoords[vertexIndex]);
                    if (includeMultipassTexCoords)
                    {
                        multipassTexCoords.Add(multipassTexCoords[vertexIndex]);
                    }
                    if (includeGlowColors)
                    {
                        glowColors.Add(glowColors[vertexIndex]);
                    }
                    if (includeAmbientIndices)
                    {
                        ambientIndices.Add(ambientIndices[vertexIndex]);
                    }

                    group.Indices[indexOffset] = expandedIndex;
                }
            }
        }
    }

    private static Dictionary<(int GroupIndex, int IndexOffset), Vector3> BuildFlatProfileSideNormalTargets(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups)
    {
        var cornersByPosition = new Dictionary<TieGltfPositionKey, List<TieGltfFlatProfileSideCorner>>();
        for (var groupIndex = 0; groupIndex < packetIndexGroups.Count; groupIndex++)
        {
            var group = packetIndexGroups[groupIndex];
            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var aIndex = checked((int)group.Indices[i]);
                var bIndex = checked((int)group.Indices[i + 1]);
                var cIndex = checked((int)group.Indices[i + 2]);
                var normal = Vector3.Cross(
                    positions[bIndex] - positions[aIndex],
                    positions[cIndex] - positions[aIndex]);
                if (normal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var faceNormal = ClampNearVerticalFaceNormalY(Vector3.Normalize(normal));
                if (MathF.Abs(faceNormal.Y) > FlatProfileSideFaceNormalYMaximum)
                {
                    continue;
                }

                AddCorner(i, aIndex);
                AddCorner(i + 1, bIndex);
                AddCorner(i + 2, cIndex);

                void AddCorner(int indexOffset, int vertexIndex)
                {
                    var positionKey = TieGltfPositionKey.From(positions[vertexIndex]);
                    if (!cornersByPosition.TryGetValue(positionKey, out var corners))
                    {
                        corners = [];
                        cornersByPosition[positionKey] = corners;
                    }

                    corners.Add(new TieGltfFlatProfileSideCorner(groupIndex, indexOffset, faceNormal));
                }
            }
        }

        var targets = new Dictionary<(int GroupIndex, int IndexOffset), Vector3>();
        foreach (var corners in cornersByPosition.Values)
        {
            foreach (var corner in corners)
            {
                var sum = Vector3.Zero;
                foreach (var relatedCorner in corners)
                {
                    if (Vector3.Dot(corner.FaceNormal, relatedCorner.FaceNormal) >= SmoothNormalMinimumFaceDot)
                    {
                        sum += relatedCorner.FaceNormal;
                    }
                }

                if (sum.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                targets[(corner.GroupIndex, corner.IndexOffset)] = Vector3.Normalize(sum);
            }
        }

        return targets;
    }

    private static Vector3 SelectFlatProfileSideNormal(
        Vector3 faceNormal,
        Vector3 normal,
        float minimumDot,
        Vector3 sideSmoothNormal)
    {
        if (sideSmoothNormal.LengthSquared() > 1e-12f
            && Vector3.Dot(faceNormal, sideSmoothNormal) >= minimumDot)
        {
            return sideSmoothNormal;
        }

        var flattened = new Vector3(normal.X, 0f, normal.Z);
        if (flattened.LengthSquared() <= 1e-12f)
        {
            return faceNormal;
        }

        flattened = Vector3.Normalize(flattened);
        if (Vector3.Dot(faceNormal, flattened) >= minimumDot)
        {
            return flattened;
        }

        var flipped = -flattened;
        return Vector3.Dot(faceNormal, flipped) >= minimumDot
            ? flipped
            : faceNormal;
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

    private static bool NearlyEqual(Vector3 left, Vector3 right)
    {
        return MathF.Abs(left.X - right.X) < 0.000001f
            && MathF.Abs(left.Y - right.Y) < 0.000001f
            && MathF.Abs(left.Z - right.Z) < 0.000001f;
    }

    private readonly record struct TieGltfFlatProfileSideCorner(
        int GroupIndex,
        int IndexOffset,
        Vector3 FaceNormal);
}

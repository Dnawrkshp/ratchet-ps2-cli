using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfWindingRepairer
{
    private const int InvertedComponentMinimumTriangleCount = 64;
    private const float InvertedComponentMaximumBoundaryEdgeRatio = 0.05f;
    private const float InvertedComponentMinimumVolumeToBoundsRatio = 0.05f;
    private const float InvertedComponentMinimumDirectionalTriangleRatio = 0.5f;
    private const float InvertedComponentMinimumInwardRatio = 0.65f;
    private const float InvertedComponentMaximumOutwardRatio = 0.2f;
    private const float InvertedComponentDirectionalDotThreshold = 0.2f;
    private const int LocalInwardComponentMinimumTriangleCount = 32;
    private const float LocalInwardComponentMaximumBoundaryEdgeRatio = 0.08f;
    private const float LocalInwardComponentMinimumVolumeToBoundsRatio = 0.02f;
    private const float LocalInwardComponentMinimumDirectionalTriangleRatio = 0.35f;
    private const float LocalInwardComponentMinimumOutwardRatio = 0.65f;
    private const float LocalInwardComponentMaximumInwardRatio = 0.35f;
    private const float LocalInwardTriangleDotThreshold = -0.2f;
    private const float OpposedFaceNormalMinimumDot = -0.75f;
    private const float UpperHorizontalFaceNormalY = 0.75f;
    private const float UpperHorizontalMinimumYRatio = 0.4f;

    public static TieGltfWindingRepairResult RestoreInvertedWindingConnectedComponents(
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vector3>? sourceOnlyNormals,
        List<float>? sourceNormalMask,
        List<float>? sourceNormalStates,
        List<Vector2> texCoords,
        List<Vector4> glowColors,
        bool includeGlowColors,
        List<float> ambientIndices,
        bool includeAmbientIndices,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups,
        bool enableFlatProfileLocalInwardRepair,
        bool enableUpperHorizontalFlatFaceFallback)
    {
        var triangles = BuildConnectedTriangleRefs(positions, packetIndexGroups);
        if (triangles.Count == 0)
        {
            return TieGltfWindingRepairResult.None;
        }

        var triangleIndicesByPosition = BuildTriangleIndexLookup(triangles);
        var visited = new bool[triangles.Count];
        var flippedVertexIndexByOriginal = new Dictionary<uint, uint>();
        var restoreFlatProfileFaces = enableFlatProfileLocalInwardRepair
            && TieGltfFlatProfileNormalRepairer.ShouldRestore(positions);
        var invertedComponentTriangleCount = 0;
        var localInwardTriangleCount = 0;
        var opposedNormalTriangleCount = 0;
        var upperHorizontalTriangleCount = 0;
        for (var i = 0; i < triangles.Count; i++)
        {
            if (visited[i])
            {
                continue;
            }

            var component = FindConnectedTriangleComponent(i);
            if (ShouldFlipConnectedComponent(component))
            {
                invertedComponentTriangleCount += component.Count;
                FlipTriangles(component);
            }
        }

        if (invertedComponentTriangleCount > 0)
        {
            triangles = BuildConnectedTriangleRefs(positions, packetIndexGroups);
            triangleIndicesByPosition = BuildTriangleIndexLookup(triangles);
        }

        if (restoreFlatProfileFaces)
        {
            visited = new bool[triangles.Count];
            for (var i = 0; i < triangles.Count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                var component = FindConnectedTriangleComponent(i);
                if (TryFindLocalInwardTriangles(component, out var inwardTriangles))
                {
                    localInwardTriangleCount += inwardTriangles.Count;
                    FlipTriangles(inwardTriangles);
                }
            }
        }

        if (localInwardTriangleCount > 0)
        {
            triangles = BuildConnectedTriangleRefs(positions, packetIndexGroups);
        }

        // GC ties still have primary-write continuation phase cases that are
        // not fully decoded. Keep this narrow preview fallback active, but do
        // not let the broader local/opposed normal repair paths override
        // authored strip/source-normal phase fixes.
        upperHorizontalTriangleCount = enableUpperHorizontalFlatFaceFallback
            && TieGltfFlatProfileNormalRepairer.ShouldRestore(positions)
            ? RestoreUpperHorizontalFlatFaces(triangles)
            : 0;

        return new TieGltfWindingRepairResult(
            invertedComponentTriangleCount,
            localInwardTriangleCount,
            opposedNormalTriangleCount,
            upperHorizontalTriangleCount);

        Dictionary<TieGltfPositionKey, List<int>> BuildTriangleIndexLookup(
            IReadOnlyList<TieGltfConnectedTriangleRef> triangleRefs)
        {
            var lookup = new Dictionary<TieGltfPositionKey, List<int>>();
            for (var i = 0; i < triangleRefs.Count; i++)
            {
                AddTriangleIndex(triangleRefs[i].AKey, i);
                AddTriangleIndex(triangleRefs[i].BKey, i);
                AddTriangleIndex(triangleRefs[i].CKey, i);
            }

            return lookup;

            void AddTriangleIndex(TieGltfPositionKey key, int triangleIndex)
            {
                if (!lookup.TryGetValue(key, out var indices))
                {
                    indices = [];
                    lookup[key] = indices;
                }

                indices.Add(triangleIndex);
            }
        }

        List<int> FindConnectedTriangleComponent(int startTriangleIndex)
        {
            var component = new List<int>();
            var pending = new Stack<int>();
            pending.Push(startTriangleIndex);
            visited[startTriangleIndex] = true;
            while (pending.Count > 0)
            {
                var triangleIndex = pending.Pop();
                component.Add(triangleIndex);
                foreach (var key in triangles[triangleIndex].PositionKeys)
                {
                    foreach (var connectedTriangleIndex in triangleIndicesByPosition[key])
                    {
                        if (visited[connectedTriangleIndex])
                        {
                            continue;
                        }

                        visited[connectedTriangleIndex] = true;
                        pending.Push(connectedTriangleIndex);
                    }
                }
            }

            return component;
        }

        bool ShouldFlipConnectedComponent(IReadOnlyList<int> component)
        {
            if (component.Count < InvertedComponentMinimumTriangleCount)
            {
                return false;
            }

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var edgeCounts = new Dictionary<TieGltfPositionEdgeKey, int>();
            var signedVolume = 0f;
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                var a = positions[checked((int)triangle.AIndex)];
                var b = positions[checked((int)triangle.BIndex)];
                var c = positions[checked((int)triangle.CIndex)];
                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
                signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                AddEdge(triangle.AKey, triangle.BKey);
                AddEdge(triangle.BKey, triangle.CKey);
                AddEdge(triangle.CKey, triangle.AKey);
            }

            if (signedVolume >= 0f)
            {
                return false;
            }

            var extents = max - min;
            var boundsVolume = MathF.Abs(extents.X * extents.Y * extents.Z);
            if (boundsVolume <= 1e-6f
                || MathF.Abs(signedVolume) < boundsVolume * InvertedComponentMinimumVolumeToBoundsRatio)
            {
                return false;
            }

            var boundaryEdgeCount = edgeCounts.Count(pair => pair.Value == 1);
            if (boundaryEdgeCount > component.Count * 3f * InvertedComponentMaximumBoundaryEdgeRatio)
            {
                return false;
            }

            var boundsCenter = (min + max) * 0.5f;
            var directionalTriangleCount = 0;
            var inwardTriangleCount = 0;
            var outwardTriangleCount = 0;
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                var a = positions[checked((int)triangle.AIndex)];
                var b = positions[checked((int)triangle.BIndex)];
                var c = positions[checked((int)triangle.CIndex)];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var centerVector = ((a + b + c) / 3f) - boundsCenter;
                if (centerVector.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var dot = Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(centerVector));
                if (MathF.Abs(dot) <= InvertedComponentDirectionalDotThreshold)
                {
                    continue;
                }

                directionalTriangleCount++;
                if (dot < 0f)
                {
                    inwardTriangleCount++;
                }
                else
                {
                    outwardTriangleCount++;
                }
            }

            return directionalTriangleCount >= component.Count * InvertedComponentMinimumDirectionalTriangleRatio
                && inwardTriangleCount >= directionalTriangleCount * InvertedComponentMinimumInwardRatio
                && outwardTriangleCount <= directionalTriangleCount * InvertedComponentMaximumOutwardRatio;

            void AddEdge(TieGltfPositionKey a, TieGltfPositionKey b)
            {
                var key = TieGltfPositionEdgeKey.From(a, b);
                edgeCounts.TryGetValue(key, out var count);
                edgeCounts[key] = count + 1;
            }
        }

        bool TryFindLocalInwardTriangles(IReadOnlyList<int> component, out List<int> inwardTriangles)
        {
            inwardTriangles = [];
            if (component.Count < LocalInwardComponentMinimumTriangleCount)
            {
                return false;
            }

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            var edgeCounts = new Dictionary<TieGltfPositionEdgeKey, int>();
            var signedVolume = 0f;
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                var a = positions[checked((int)triangle.AIndex)];
                var b = positions[checked((int)triangle.BIndex)];
                var c = positions[checked((int)triangle.CIndex)];
                min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
                signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6f;
                AddEdge(triangle.AKey, triangle.BKey);
                AddEdge(triangle.BKey, triangle.CKey);
                AddEdge(triangle.CKey, triangle.AKey);
            }

            if (signedVolume <= 0f)
            {
                return false;
            }

            var extents = max - min;
            var boundsVolume = MathF.Abs(extents.X * extents.Y * extents.Z);
            if (boundsVolume <= 1e-6f
                || signedVolume < boundsVolume * LocalInwardComponentMinimumVolumeToBoundsRatio)
            {
                return false;
            }

            var boundaryEdgeCount = edgeCounts.Count(pair => pair.Value == 1);
            if (boundaryEdgeCount > component.Count * 3f * LocalInwardComponentMaximumBoundaryEdgeRatio)
            {
                return false;
            }

            var boundsCenter = (min + max) * 0.5f;
            var directionalTriangleCount = 0;
            var outwardTriangleCount = 0;
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                var a = positions[checked((int)triangle.AIndex)];
                var b = positions[checked((int)triangle.BIndex)];
                var c = positions[checked((int)triangle.CIndex)];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var centerVector = ((a + b + c) / 3f) - boundsCenter;
                if (centerVector.LengthSquared() <= 1e-12f)
                {
                    continue;
                }

                var dot = Vector3.Dot(Vector3.Normalize(normal), Vector3.Normalize(centerVector));
                if (dot > -LocalInwardTriangleDotThreshold)
                {
                    directionalTriangleCount++;
                    outwardTriangleCount++;
                }
                else if (dot < LocalInwardTriangleDotThreshold)
                {
                    directionalTriangleCount++;
                    inwardTriangles.Add(triangleIndex);
                }
            }

            return inwardTriangles.Count > 0
                && directionalTriangleCount >= component.Count * LocalInwardComponentMinimumDirectionalTriangleRatio
                && outwardTriangleCount >= directionalTriangleCount * LocalInwardComponentMinimumOutwardRatio
                && inwardTriangles.Count <= directionalTriangleCount * LocalInwardComponentMaximumInwardRatio;

            void AddEdge(TieGltfPositionKey a, TieGltfPositionKey b)
            {
                var key = TieGltfPositionEdgeKey.From(a, b);
                edgeCounts.TryGetValue(key, out var count);
                edgeCounts[key] = count + 1;
            }
        }

        void FlipTriangles(IReadOnlyList<int> component)
        {
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                var group = packetIndexGroups[triangle.GroupIndex];
                var a = GetFlippedVertexIndex(triangle.AIndex);
                var b = GetFlippedVertexIndex(triangle.BIndex);
                var c = GetFlippedVertexIndex(triangle.CIndex);
                group.Indices[triangle.IndexOffset] = a;
                group.Indices[triangle.IndexOffset + 1] = c;
                group.Indices[triangle.IndexOffset + 2] = b;
            }
        }

        int RestoreUpperHorizontalFlatFaces(IReadOnlyList<TieGltfConnectedTriangleRef> triangleRefs)
        {
            if (!TieGltfFlatProfileNormalRepairer.ShouldRestore(positions))
            {
                return 0;
            }

            var bounds = TieGltfGeneratedNormalBuilder.GetPositionBounds(positions);
            var ySpan = bounds.Max.Y - bounds.Min.Y;
            if (ySpan <= 1e-6f)
            {
                return 0;
            }

            var minimumY = bounds.Min.Y + ySpan * UpperHorizontalMinimumYRatio;
            var repairedCount = 0;
            foreach (var triangle in triangleRefs)
            {
                if (!TryGetFaceNormal(triangle, out var faceNormal)
                    || faceNormal.Y >= -UpperHorizontalFaceNormalY)
                {
                    continue;
                }

                var centerY = GetTriangleCenterY(triangle);
                if (centerY < minimumY)
                {
                    continue;
                }

                ReverseTriangleWinding(triangle);
                repairedCount++;
            }

            return repairedCount;
        }

        bool TryGetFaceNormal(TieGltfConnectedTriangleRef triangle, out Vector3 faceNormal)
        {
            var a = positions[checked((int)triangle.AIndex)];
            var b = positions[checked((int)triangle.BIndex)];
            var c = positions[checked((int)triangle.CIndex)];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() <= 1e-12f)
            {
                faceNormal = default;
                return false;
            }

            faceNormal = Vector3.Normalize(normal);
            return true;
        }

        float GetTriangleCenterY(TieGltfConnectedTriangleRef triangle)
        {
            return (positions[checked((int)triangle.AIndex)].Y
                + positions[checked((int)triangle.BIndex)].Y
                + positions[checked((int)triangle.CIndex)].Y) / 3f;
        }

        void ReverseTriangleWinding(TieGltfConnectedTriangleRef triangle)
        {
            var group = packetIndexGroups[triangle.GroupIndex];
            (group.Indices[triangle.IndexOffset + 1], group.Indices[triangle.IndexOffset + 2]) =
                (group.Indices[triangle.IndexOffset + 2], group.Indices[triangle.IndexOffset + 1]);
        }

        uint GetFlippedVertexIndex(uint vertexIndex)
        {
            if (flippedVertexIndexByOriginal.TryGetValue(vertexIndex, out var flippedIndex))
            {
                return flippedIndex;
            }

            var sourceIndex = checked((int)vertexIndex);
            var expandedIndex = checked((uint)positions.Count);
            positions.Add(positions[sourceIndex]);
            normals.Add(-normals[sourceIndex]);
            sourceOnlyNormals?.Add(sourceOnlyNormals[sourceIndex]);
            sourceNormalMask?.Add(sourceNormalMask[sourceIndex]);
            sourceNormalStates?.Add(sourceNormalStates[sourceIndex]);
            texCoords.Add(texCoords[sourceIndex]);
            if (includeGlowColors)
            {
                glowColors.Add(glowColors[sourceIndex]);
            }
            if (includeAmbientIndices)
            {
                ambientIndices.Add(ambientIndices[sourceIndex]);
            }

            flippedVertexIndexByOriginal[vertexIndex] = expandedIndex;
            return expandedIndex;
        }
    }

    public static int RestoreTrianglesOpposedToVertexNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(packetIndexGroups);

        var repairedCount = 0;
        foreach (var triangle in BuildConnectedTriangleRefs(positions, packetIndexGroups))
        {
            if (!TryGetFaceNormal(triangle, out var faceNormal)
                || !TryGetAverageVertexNormal(triangle, out var vertexNormal)
                || Vector3.Dot(faceNormal, vertexNormal) >= OpposedFaceNormalMinimumDot)
            {
                continue;
            }

            ReverseTriangleWinding(triangle);
            repairedCount++;
        }

        return repairedCount;

        bool TryGetFaceNormal(TieGltfConnectedTriangleRef triangle, out Vector3 faceNormal)
        {
            var a = positions[checked((int)triangle.AIndex)];
            var b = positions[checked((int)triangle.BIndex)];
            var c = positions[checked((int)triangle.CIndex)];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() <= 1e-12f)
            {
                faceNormal = default;
                return false;
            }

            faceNormal = Vector3.Normalize(normal);
            return true;
        }

        bool TryGetAverageVertexNormal(TieGltfConnectedTriangleRef triangle, out Vector3 vertexNormal)
        {
            var a = normals[checked((int)triangle.AIndex)];
            var b = normals[checked((int)triangle.BIndex)];
            var c = normals[checked((int)triangle.CIndex)];
            var normal = a + b + c;
            if (normal.LengthSquared() <= 1e-12f)
            {
                vertexNormal = default;
                return false;
            }

            vertexNormal = Vector3.Normalize(normal);
            return true;
        }

        void ReverseTriangleWinding(TieGltfConnectedTriangleRef triangle)
        {
            var group = packetIndexGroups[triangle.GroupIndex];
            (group.Indices[triangle.IndexOffset + 1], group.Indices[triangle.IndexOffset + 2]) =
                (group.Indices[triangle.IndexOffset + 2], group.Indices[triangle.IndexOffset + 1]);
        }
    }

    private static List<TieGltfConnectedTriangleRef> BuildConnectedTriangleRefs(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups)
    {
        var triangles = new List<TieGltfConnectedTriangleRef>();
        for (var groupIndex = 0; groupIndex < packetIndexGroups.Count; groupIndex++)
        {
            var group = packetIndexGroups[groupIndex];
            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var aIndex = group.Indices[i];
                var bIndex = group.Indices[i + 1];
                var cIndex = group.Indices[i + 2];
                var aKey = TieGltfPositionKey.From(positions[checked((int)aIndex)]);
                var bKey = TieGltfPositionKey.From(positions[checked((int)bIndex)]);
                var cKey = TieGltfPositionKey.From(positions[checked((int)cIndex)]);
                triangles.Add(new TieGltfConnectedTriangleRef(
                    groupIndex,
                    i,
                    aIndex,
                    bIndex,
                    cIndex,
                    aKey,
                    bKey,
                    cKey));
            }
        }

        return triangles;
    }

    private readonly record struct TieGltfConnectedTriangleRef(
        int GroupIndex,
        int IndexOffset,
        uint AIndex,
        uint BIndex,
        uint CIndex,
        TieGltfPositionKey AKey,
        TieGltfPositionKey BKey,
        TieGltfPositionKey CKey)
    {
        public IEnumerable<TieGltfPositionKey> PositionKeys
        {
            get
            {
                yield return AKey;
                yield return BKey;
                yield return CKey;
            }
        }
    }

    private readonly record struct TieGltfPositionEdgeKey(TieGltfPositionKey A, TieGltfPositionKey B)
    {
        public static TieGltfPositionEdgeKey From(TieGltfPositionKey a, TieGltfPositionKey b)
        {
            return Compare(a, b) <= 0
                ? new TieGltfPositionEdgeKey(a, b)
                : new TieGltfPositionEdgeKey(b, a);
        }

        private static int Compare(TieGltfPositionKey left, TieGltfPositionKey right)
        {
            var x = left.X.CompareTo(right.X);
            if (x != 0)
            {
                return x;
            }

            var y = left.Y.CompareTo(right.Y);
            return y != 0 ? y : left.Z.CompareTo(right.Z);
        }
    }
}

internal readonly record struct TieGltfWindingRepairResult(
    int InvertedComponentTriangleCount,
    int LocalInwardTriangleCount,
    int OpposedNormalTriangleCount,
    int UpperHorizontalTriangleCount)
{
    public static TieGltfWindingRepairResult None { get; } = new(0, 0, 0, 0);

    public TieGltfWindingRepairResult WithOpposedNormalTriangleCount(int count)
    {
        return new TieGltfWindingRepairResult(
            InvertedComponentTriangleCount,
            LocalInwardTriangleCount,
            count,
            UpperHorizontalTriangleCount);
    }
}

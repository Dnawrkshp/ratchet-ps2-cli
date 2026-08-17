using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfGeometryBuilder
{

    public static GltfGeometry Build(
        IReadOnlyList<TieShader> shaders,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector3> environmentNormals,
        IReadOnlyList<Vector3> indexNormals,
        IReadOnlyList<int> sourceNormalVertexIndices,
        IReadOnlyList<int> sourceNormalIndexOffsets,
        IReadOnlyList<TieGltfSourceNormalState> sourceNormalVertexStates,
        IReadOnlyList<TieGltfSourceNormalState> sourceNormalIndexStates,
        bool suppressGeneratedNormalFallback,
        bool orientTriangleWindingToNormals,
        IReadOnlyList<Vector2> texCoords,
        IReadOnlyList<Vector2> multipassTexCoords,
        IReadOnlyList<Vector4> glowColors,
        IReadOnlyList<float> ambientIndices,
        IReadOnlyList<float> indexAmbientIndices,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups,
        IReadOnlyDictionary<int, TextureSize>? textureSizes)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(environmentNormals);
        ArgumentNullException.ThrowIfNull(indexNormals);
        ArgumentNullException.ThrowIfNull(sourceNormalIndexOffsets);
        ArgumentNullException.ThrowIfNull(sourceNormalVertexStates);
        ArgumentNullException.ThrowIfNull(sourceNormalIndexStates);
        ArgumentNullException.ThrowIfNull(texCoords);
        ArgumentNullException.ThrowIfNull(multipassTexCoords);
        ArgumentNullException.ThrowIfNull(glowColors);
        ArgumentNullException.ThrowIfNull(ambientIndices);
        ArgumentNullException.ThrowIfNull(indexAmbientIndices);
        ArgumentNullException.ThrowIfNull(packetIndexGroups);

        var includeGlowColors = glowColors.Count == positions.Count;
        var includeEnvironmentNormals = environmentNormals.Count == positions.Count;
        var includeMultipassTexCoords = multipassTexCoords.Count == positions.Count;
        var sourceIndexCount = packetIndexGroups.Sum(group => group.Indices.Count);
        var includeSourceAmbientIndices = ambientIndices.Count == positions.Count
            && ambientIndices.Any(index => index >= 0f);
        var includeIndexAmbientIndices = indexAmbientIndices.Count == sourceIndexCount
            && indexAmbientIndices.Any(index => index >= 0f);
        var includeAmbientIndices = includeSourceAmbientIndices || includeIndexAmbientIndices;
        var expandedPositions = positions.ToList();
        var expandedNormals = normals.ToList();
        var expandedEnvironmentNormals = includeEnvironmentNormals
            ? environmentNormals.ToList()
            : new List<Vector3>();
        var sourceNormalVertexIndexSet = sourceNormalVertexIndices.ToHashSet();
        var sourceNormalIndexOffsetSet = sourceNormalIndexOffsets.ToHashSet();
        var expandedSourceOnlyNormals = BuildSourceOnlyNormals();
        var expandedSourceNormalMask = BuildSourceNormalMask();
        var expandedSourceNormalStates = BuildSourceNormalStates();
        var expandedTexCoords = texCoords.ToList();
        var expandedMultipassTexCoords = includeMultipassTexCoords
            ? multipassTexCoords.ToList()
            : new List<Vector2>();
        var expandedGlowColors = includeGlowColors ? glowColors.ToList() : new List<Vector4>();
        var expandedAmbientIndices = includeAmbientIndices
            ? BuildInitialAmbientIndices()
            : new List<float>();
        var expandedVertexIndexByKey = new Dictionary<ExpandedVertexKey, uint>();
        var expandedGroups = new List<PacketIndexGroup>(packetIndexGroups.Count);
        var sourceIndexOffset = 0;

        foreach (var group in packetIndexGroups)
        {
            var shader = group.ShaderIndex >= 0 && group.ShaderIndex < shaders.Count
                ? shaders[group.ShaderIndex]
                : null;
            var repeatU = shader is not null && !shader.ClampU;
            var repeatV = shader is not null && !shader.ClampV;
            var textureSize = textureSizes is not null
                && textureSizes.TryGetValue(group.ShaderIndex, out var size)
                    ? size
                    : default(TextureSize?);
            var expandedIndices = new List<uint>(group.Indices.Count);

            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var aIndex = checked((int)group.Indices[i]);
                var bIndex = checked((int)group.Indices[i + 1]);
                var cIndex = checked((int)group.Indices[i + 2]);
                var adjustedTexCoords = GltfTexCoordUtils.AdjustTriangleTexCoords(
                    texCoords[aIndex],
                    texCoords[bIndex],
                    texCoords[cIndex],
                    textureSize,
                    repeatU,
                    repeatV);
                var adjustedMultipassTexCoords = includeMultipassTexCoords
                    ? AdjustMultipassTriangleTexCoords(
                        GetMultipassTexCoord(aIndex),
                        GetMultipassTexCoord(bIndex),
                        GetMultipassTexCoord(cIndex))
                    : null;

                expandedIndices.Add(GetExpandedVertexIndex(
                    aIndex,
                    sourceIndexOffset,
                    HasSourceNormal(sourceIndexOffset),
                    GetSourceNormalState(sourceIndexOffset, aIndex),
                    adjustedTexCoords[0],
                    GetAdjustedMultipassTexCoord(0),
                    GetIndexNormal(sourceIndexOffset, aIndex),
                    GetGlowColor(aIndex),
                    GetAmbientIndex(sourceIndexOffset, aIndex)));
                sourceIndexOffset++;
                expandedIndices.Add(GetExpandedVertexIndex(
                    bIndex,
                    sourceIndexOffset,
                    HasSourceNormal(sourceIndexOffset),
                    GetSourceNormalState(sourceIndexOffset, bIndex),
                    adjustedTexCoords[1],
                    GetAdjustedMultipassTexCoord(1),
                    GetIndexNormal(sourceIndexOffset, bIndex),
                    GetGlowColor(bIndex),
                    GetAmbientIndex(sourceIndexOffset, bIndex)));
                sourceIndexOffset++;
                expandedIndices.Add(GetExpandedVertexIndex(
                    cIndex,
                    sourceIndexOffset,
                    HasSourceNormal(sourceIndexOffset),
                    GetSourceNormalState(sourceIndexOffset, cIndex),
                    adjustedTexCoords[2],
                    GetAdjustedMultipassTexCoord(2),
                    GetIndexNormal(sourceIndexOffset, cIndex),
                    GetGlowColor(cIndex),
                    GetAmbientIndex(sourceIndexOffset, cIndex)));
                sourceIndexOffset++;

                Vector2 GetAdjustedMultipassTexCoord(int vertexOffset)
                {
                    return adjustedMultipassTexCoords is not null
                        ? adjustedMultipassTexCoords[vertexOffset]
                        : Vector2.Zero;
                }
            }

            expandedGroups.Add(new PacketIndexGroup(
                group.PacketIndex,
                group.ShaderIndex,
                group.MultipassOffset,
                group.PassFlags,
                group.MultipassUvSize,
                group.EnvPassBleedColor,
                group.PacketShaderIndices,
                group.PacketShaderSwitchVuAddresses,
                group.UseGlowEmission,
                expandedIndices));
        }

        var suppressedGeneratedNormalFallbackVertexCount = 0;
        if (suppressGeneratedNormalFallback)
        {
            if (expandedSourceOnlyNormals.Count != expandedNormals.Count)
            {
                throw new InvalidOperationException(
                    $"Source-only normal count {expandedSourceOnlyNormals.Count} does not match expanded normal count {expandedNormals.Count}.");
            }

            for (var i = 0; i < expandedNormals.Count; i++)
            {
                expandedNormals[i] = expandedSourceOnlyNormals[i];
                if (expandedSourceOnlyNormals[i].LengthSquared() <= 1e-12f)
                {
                    suppressedGeneratedNormalFallbackVertexCount++;
                }
            }
        }

        if (orientTriangleWindingToNormals)
        {
            OrientTrianglesToNormals(expandedPositions, expandedNormals, expandedGroups);
        }

        return new GltfGeometry(
            expandedPositions,
            expandedNormals,
            expandedEnvironmentNormals,
            expandedSourceNormalMask,
            expandedSourceNormalStates,
            expandedTexCoords,
            expandedMultipassTexCoords,
            expandedGlowColors,
            expandedAmbientIndices,
            expandedGroups,
            suppressedGeneratedNormalFallbackVertexCount);

        Vector3 GetIndexNormal(int indexOffset, int sourceIndex)
        {
            return indexOffset >= 0 && indexOffset < indexNormals.Count
                ? indexNormals[indexOffset]
                : normals[sourceIndex];
        }

        Vector4 GetGlowColor(int sourceIndex)
        {
            return includeGlowColors && sourceIndex >= 0 && sourceIndex < glowColors.Count
                ? glowColors[sourceIndex]
                : TieGltfGlowBuilder.NoGlowColor;
        }

        float GetAmbientIndex(int indexOffset, int sourceIndex)
        {
            if (includeIndexAmbientIndices && indexOffset >= 0 && indexOffset < indexAmbientIndices.Count)
            {
                return indexAmbientIndices[indexOffset];
            }

            return includeSourceAmbientIndices && sourceIndex >= 0 && sourceIndex < ambientIndices.Count
                ? ambientIndices[sourceIndex]
                : -1f;
        }

        Vector2 GetMultipassTexCoord(int sourceIndex)
        {
            return includeMultipassTexCoords && sourceIndex >= 0 && sourceIndex < multipassTexCoords.Count
                ? multipassTexCoords[sourceIndex]
                : Vector2.Zero;
        }

        uint GetExpandedVertexIndex(
            int sourceIndex,
            int sourceIndexOffset,
            bool sourceNormalPresent,
            TieGltfSourceNormalState sourceNormalState,
            Vector2 adjustedTexCoord,
            Vector2 multipassTexCoord,
            Vector3 normal,
            Vector4 glowColor,
            float ambientIndex)
        {
            if (NearlyEqual(adjustedTexCoord, texCoords[sourceIndex])
                && (!includeMultipassTexCoords
                    || NearlyEqual(multipassTexCoord, expandedMultipassTexCoords[sourceIndex]))
                && NearlyEqual(normal, normals[sourceIndex])
                && NearlyEqual(sourceNormalPresent ? 1f : 0f, expandedSourceNormalMask[sourceIndex])
                && NearlyEqual((float)sourceNormalState, expandedSourceNormalStates[sourceIndex])
                && (!includeGlowColors || NearlyEqual(glowColor, glowColors[sourceIndex]))
                && (!includeAmbientIndices || NearlyEqual(ambientIndex, ambientIndices[sourceIndex])))
            {
                return checked((uint)sourceIndex);
            }

            var key = ExpandedVertexKey.From(
                sourceIndex,
                adjustedTexCoord,
                includeMultipassTexCoords ? multipassTexCoord : Vector2.Zero,
                normal,
                sourceNormalPresent,
                sourceNormalState,
                includeGlowColors ? glowColor : TieGltfGlowBuilder.NoGlowColor,
                includeAmbientIndices ? ambientIndex : -1f);
            if (expandedVertexIndexByKey.TryGetValue(key, out var existingIndex))
            {
                return existingIndex;
            }

            var expandedIndex = checked((uint)expandedPositions.Count);
            expandedPositions.Add(positions[sourceIndex]);
            expandedNormals.Add(normal);
            if (includeEnvironmentNormals)
            {
                expandedEnvironmentNormals.Add(environmentNormals[sourceIndex]);
            }
            expandedSourceOnlyNormals.Add(sourceNormalPresent
                ? normal
                : Vector3.Zero);
            expandedSourceNormalMask.Add(sourceNormalPresent ? 1f : 0f);
            expandedSourceNormalStates.Add((float)sourceNormalState);
            expandedTexCoords.Add(adjustedTexCoord);
            if (includeMultipassTexCoords)
            {
                expandedMultipassTexCoords.Add(multipassTexCoord);
            }

            if (includeGlowColors)
            {
                expandedGlowColors.Add(glowColor);
            }

            if (includeAmbientIndices)
            {
                expandedAmbientIndices.Add(ambientIndex);
            }

            expandedVertexIndexByKey.Add(key, expandedIndex);
            return expandedIndex;
        }

        bool HasSourceNormal(int sourceIndexOffset)
        {
            return sourceNormalIndexOffsetSet.Contains(sourceIndexOffset);
        }

        TieGltfSourceNormalState GetSourceNormalState(int sourceIndexOffset, int sourceIndex)
        {
            if (sourceIndexOffset >= 0
                && sourceIndexOffset < sourceNormalIndexStates.Count
                && sourceNormalIndexStates[sourceIndexOffset] != TieGltfSourceNormalState.Missing)
            {
                return sourceNormalIndexStates[sourceIndexOffset];
            }

            return sourceIndex >= 0 && sourceIndex < sourceNormalVertexStates.Count
                ? sourceNormalVertexStates[sourceIndex]
                : TieGltfSourceNormalState.Missing;
        }

        List<Vector3> BuildSourceOnlyNormals()
        {
            var sourceOnlyNormals = new List<Vector3>(normals.Count);
            for (var i = 0; i < normals.Count; i++)
            {
                sourceOnlyNormals.Add(sourceNormalVertexIndexSet.Contains(i)
                    ? normals[i]
                    : Vector3.Zero);
            }

            return sourceOnlyNormals;
        }

        List<float> BuildSourceNormalMask()
        {
            var sourceNormalMask = new List<float>(normals.Count);
            for (var i = 0; i < normals.Count; i++)
            {
                sourceNormalMask.Add(sourceNormalVertexIndexSet.Contains(i) ? 1f : 0f);
            }

            return sourceNormalMask;
        }

        List<float> BuildSourceNormalStates()
        {
            var sourceNormalStates = new List<float>(normals.Count);
            for (var i = 0; i < normals.Count; i++)
            {
                sourceNormalStates.Add(i < sourceNormalVertexStates.Count
                    ? (float)sourceNormalVertexStates[i]
                    : (float)TieGltfSourceNormalState.Missing);
            }

            return sourceNormalStates;
        }

        List<float> BuildInitialAmbientIndices()
        {
            if (includeSourceAmbientIndices)
            {
                return ambientIndices.ToList();
            }

            return Enumerable.Repeat(-1f, positions.Count).ToList();
        }
    }

    private static void OrientTrianglesToNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<PacketIndexGroup> groups)
    {
        foreach (var group in groups)
        {
            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var a = checked((int)group.Indices[i]);
                var b = checked((int)group.Indices[i + 1]);
                var c = checked((int)group.Indices[i + 2]);
                var faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                var vertexNormal = normals[a] + normals[b] + normals[c];
                if (Vector3.Dot(faceNormal, vertexNormal) < 0f)
                {
                    (group.Indices[i + 1], group.Indices[i + 2]) =
                        (group.Indices[i + 2], group.Indices[i + 1]);
                }
            }
        }
    }

    private static bool NearlyEqual(Vector2 left, Vector2 right)
    {
        return MathF.Abs(left.X - right.X) < 0.000001f
            && MathF.Abs(left.Y - right.Y) < 0.000001f;
    }

    private static Vector2[] AdjustMultipassTriangleTexCoords(Vector2 a, Vector2 b, Vector2 c)
    {
        var u = UnwrapMultipassTriangleAxis(a.X, b.X, c.X);
        var v = UnwrapMultipassTriangleAxis(a.Y, b.Y, c.Y);
        return
        [
            new Vector2(u[0], v[0]),
            new Vector2(u[1], v[1]),
            new Vector2(u[2], v[2])
        ];
    }

    private static float[] UnwrapMultipassTriangleAxis(float a, float b, float c)
    {
        const float tolerance = 0.000001f;

        var bestB = b;
        var bestC = c;
        var bestRange = Range(a, b, c);
        var bestMaxEdge = MaxEdgeDelta(a, b, c);
        var centeredBOffset = -(int)MathF.Round(b - a);
        var centeredCOffset = -(int)MathF.Round(c - a);
        for (var bOffset = centeredBOffset - 2; bOffset <= centeredBOffset + 2; bOffset++)
        {
            for (var cOffset = centeredCOffset - 2; cOffset <= centeredCOffset + 2; cOffset++)
            {
                var candidateB = b + bOffset;
                var candidateC = c + cOffset;
                var candidateRange = Range(a, candidateB, candidateC);
                var candidateMaxEdge = MaxEdgeDelta(a, candidateB, candidateC);
                if (candidateRange < bestRange - tolerance
                    || (MathF.Abs(candidateRange - bestRange) <= tolerance
                        && candidateMaxEdge < bestMaxEdge - tolerance))
                {
                    bestRange = candidateRange;
                    bestMaxEdge = candidateMaxEdge;
                    bestB = candidateB;
                    bestC = candidateC;
                }
            }
        }

        return [a, bestB, bestC];
    }

    private static float MaxEdgeDelta(float a, float b, float c)
    {
        return MathF.Max(
            MathF.Abs(a - b),
            MathF.Max(MathF.Abs(b - c), MathF.Abs(c - a)));
    }

    private static float Range(float a, float b, float c)
    {
        return MathF.Max(a, MathF.Max(b, c)) - MathF.Min(a, MathF.Min(b, c));
    }

    private static bool NearlyEqual(Vector3 left, Vector3 right)
    {
        return MathF.Abs(left.X - right.X) < 0.000001f
            && MathF.Abs(left.Y - right.Y) < 0.000001f
            && MathF.Abs(left.Z - right.Z) < 0.000001f;
    }

    private static bool NearlyEqual(Vector4 left, Vector4 right)
    {
        return MathF.Abs(left.X - right.X) < 0.000001f
            && MathF.Abs(left.Y - right.Y) < 0.000001f
            && MathF.Abs(left.Z - right.Z) < 0.000001f
            && MathF.Abs(left.W - right.W) < 0.000001f;
    }

    private static bool NearlyEqual(float left, float right)
    {
        return MathF.Abs(left - right) < 0.000001f;
    }

    private readonly record struct ExpandedVertexKey(
        int SourceIndex,
        int U,
        int V,
        int MultipassU,
        int MultipassV,
        int NormalX,
        int NormalY,
        int NormalZ,
        int SourceNormalPresent,
        int SourceNormalState,
        int GlowR,
        int GlowG,
        int GlowB,
        int GlowA,
        int AmbientIndex)
    {
        public static ExpandedVertexKey From(
            int sourceIndex,
            Vector2 texCoord,
            Vector2 multipassTexCoord,
            Vector3 normal,
            bool sourceNormalPresent,
            TieGltfSourceNormalState sourceNormalState,
            Vector4 glowColor,
            float ambientIndex)
        {
            const float scale = 1000000f;
            return new ExpandedVertexKey(
                sourceIndex,
                (int)MathF.Round(texCoord.X * scale),
                (int)MathF.Round(texCoord.Y * scale),
                (int)MathF.Round(multipassTexCoord.X * scale),
                (int)MathF.Round(multipassTexCoord.Y * scale),
                (int)MathF.Round(normal.X * scale),
                (int)MathF.Round(normal.Y * scale),
                (int)MathF.Round(normal.Z * scale),
                sourceNormalPresent ? 1 : 0,
                (int)sourceNormalState,
                (int)MathF.Round(glowColor.X * scale),
                (int)MathF.Round(glowColor.Y * scale),
                (int)MathF.Round(glowColor.Z * scale),
                (int)MathF.Round(glowColor.W * scale),
                (int)MathF.Round(ambientIndex));
        }
    }
}

internal sealed record GltfGeometry(
    List<Vector3> Positions,
    List<Vector3> Normals,
    List<Vector3> EnvironmentNormals,
    List<float> SourceNormalMask,
    List<float> SourceNormalStates,
    List<Vector2> TexCoords,
    List<Vector2> MultipassTexCoords,
    List<Vector4> GlowColors,
    List<float> AmbientIndices,
    List<PacketIndexGroup> PacketIndexGroups,
    int SuppressedGeneratedNormalFallbackVertexCount);

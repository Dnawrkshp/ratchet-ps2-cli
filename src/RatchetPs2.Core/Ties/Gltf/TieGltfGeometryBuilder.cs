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
        IReadOnlyList<Vector3> indexNormals,
        IReadOnlyList<int> sourceNormalVertexIndices,
        bool suppressGeneratedNormalFallback,
        bool useGeometryWindingRepair,
        IReadOnlyList<Vector2> texCoords,
        IReadOnlyList<Vector4> glowColors,
        IReadOnlyList<PacketIndexGroup> packetIndexGroups,
        IReadOnlyDictionary<int, TextureSize>? textureSizes)
    {
        ArgumentNullException.ThrowIfNull(shaders);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(indexNormals);
        ArgumentNullException.ThrowIfNull(texCoords);
        ArgumentNullException.ThrowIfNull(glowColors);
        ArgumentNullException.ThrowIfNull(packetIndexGroups);

        var includeGlowColors = glowColors.Count == positions.Count;
        var enableFlatProfileNormalFallbacks = useGeometryWindingRepair && !suppressGeneratedNormalFallback;
        var restoreFlatProfileFaceNormals = enableFlatProfileNormalFallbacks
            && TieGltfFlatProfileNormalRepairer.ShouldRestore(positions);
        var expandedPositions = positions.ToList();
        var expandedNormals = normals.ToList();
        var sourceNormalVertexIndexSet = sourceNormalVertexIndices.ToHashSet();
        var expandedSourceOnlyNormals = BuildSourceOnlyNormals();
        var expandedTexCoords = texCoords.ToList();
        var expandedGlowColors = includeGlowColors ? glowColors.ToList() : new List<Vector4>();
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

                expandedIndices.Add(GetExpandedVertexIndex(
                    aIndex,
                    adjustedTexCoords[0],
                    GetIndexNormal(sourceIndexOffset, aIndex),
                    GetGlowColor(aIndex)));
                sourceIndexOffset++;
                expandedIndices.Add(GetExpandedVertexIndex(
                    bIndex,
                    adjustedTexCoords[1],
                    GetIndexNormal(sourceIndexOffset, bIndex),
                    GetGlowColor(bIndex)));
                sourceIndexOffset++;
                expandedIndices.Add(GetExpandedVertexIndex(
                    cIndex,
                    adjustedTexCoords[2],
                    GetIndexNormal(sourceIndexOffset, cIndex),
                    GetGlowColor(cIndex)));
                sourceIndexOffset++;
            }

            expandedGroups.Add(new PacketIndexGroup(
                group.PacketIndex,
                group.ShaderIndex,
                group.MultipassType,
                group.PacketShaderIndices,
                group.PacketShaderSwitchVuAddresses,
                group.UseGlowEmission,
                expandedIndices));
        }

        var windingRepairResult = useGeometryWindingRepair
            ? TieGltfWindingRepairer.RestoreInvertedWindingConnectedComponents(
                expandedPositions,
                expandedNormals,
                expandedSourceOnlyNormals,
                expandedTexCoords,
                expandedGlowColors,
                includeGlowColors,
                expandedGroups,
                enableFlatProfileLocalInwardRepair: enableFlatProfileNormalFallbacks,
                enableUpperHorizontalFlatFaceFallback: true)
            : TieGltfWindingRepairResult.None;

        if (restoreFlatProfileFaceNormals)
        {
            TieGltfFlatProfileNormalRepairer.RestoreFlatProfileExpandedFaceNormals(
                expandedPositions,
                expandedNormals,
                expandedSourceOnlyNormals,
                expandedTexCoords,
                expandedGlowColors,
                includeGlowColors,
                expandedGroups);
        }

        if (restoreFlatProfileFaceNormals)
        {
            windingRepairResult = windingRepairResult.WithOpposedNormalTriangleCount(
                TieGltfWindingRepairer.RestoreTrianglesOpposedToVertexNormals(
                    expandedPositions,
                    expandedNormals,
                    expandedGroups));
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

        return new GltfGeometry(
            expandedPositions,
            expandedNormals,
            expandedTexCoords,
            expandedGlowColors,
            expandedGroups,
            windingRepairResult,
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

        uint GetExpandedVertexIndex(int sourceIndex, Vector2 adjustedTexCoord, Vector3 normal, Vector4 glowColor)
        {
            if (NearlyEqual(adjustedTexCoord, texCoords[sourceIndex])
                && NearlyEqual(normal, normals[sourceIndex])
                && (!includeGlowColors || NearlyEqual(glowColor, glowColors[sourceIndex])))
            {
                return checked((uint)sourceIndex);
            }

            var key = ExpandedVertexKey.From(sourceIndex, adjustedTexCoord, normal, includeGlowColors ? glowColor : TieGltfGlowBuilder.NoGlowColor);
            if (expandedVertexIndexByKey.TryGetValue(key, out var existingIndex))
            {
                return existingIndex;
            }

            var expandedIndex = checked((uint)expandedPositions.Count);
            expandedPositions.Add(positions[sourceIndex]);
            expandedNormals.Add(normal);
            expandedSourceOnlyNormals.Add(sourceNormalVertexIndexSet.Contains(sourceIndex)
                ? normal
                : Vector3.Zero);
            expandedTexCoords.Add(adjustedTexCoord);

            if (includeGlowColors)
            {
                expandedGlowColors.Add(glowColor);
            }

            expandedVertexIndexByKey.Add(key, expandedIndex);
            return expandedIndex;
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
    }

    private static bool NearlyEqual(Vector2 left, Vector2 right)
    {
        return MathF.Abs(left.X - right.X) < 0.000001f
            && MathF.Abs(left.Y - right.Y) < 0.000001f;
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

    private readonly record struct ExpandedVertexKey(
        int SourceIndex,
        int U,
        int V,
        int NormalX,
        int NormalY,
        int NormalZ,
        int GlowR,
        int GlowG,
        int GlowB,
        int GlowA)
    {
        public static ExpandedVertexKey From(int sourceIndex, Vector2 texCoord, Vector3 normal, Vector4 glowColor)
        {
            const float scale = 1000000f;
            return new ExpandedVertexKey(
                sourceIndex,
                (int)MathF.Round(texCoord.X * scale),
                (int)MathF.Round(texCoord.Y * scale),
                (int)MathF.Round(normal.X * scale),
                (int)MathF.Round(normal.Y * scale),
                (int)MathF.Round(normal.Z * scale),
                (int)MathF.Round(glowColor.X * scale),
                (int)MathF.Round(glowColor.Y * scale),
                (int)MathF.Round(glowColor.Z * scale),
                (int)MathF.Round(glowColor.W * scale));
        }
    }
}

internal sealed record GltfGeometry(
    List<Vector3> Positions,
    List<Vector3> Normals,
    List<Vector2> TexCoords,
    List<Vector4> GlowColors,
    List<PacketIndexGroup> PacketIndexGroups,
    TieGltfWindingRepairResult WindingRepairResult,
    int SuppressedGeneratedNormalFallbackVertexCount);

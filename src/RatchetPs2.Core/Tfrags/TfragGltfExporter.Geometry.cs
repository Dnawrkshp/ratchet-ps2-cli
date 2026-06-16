using System.Numerics;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private const float TinyRepeatedTileBoundarySpan = 0.001f;
    private const float BroadRepeatedTileBoundaryCrossSpan = 0.75f;
    private const float RepeatedTileBoundaryEpsilon = 0.001f;
    private const float MinRepeatedTileBoundaryTriangleArea = 0.5f;
    private const float OpposedTriangleNormalDotEpsilon = -0.0001f;

    private static TfragPrimitiveGroup? BuildPrimitiveGroup(
        TfragResolvedTexture texture,
        TfragTopologyPacket topologyPacket,
        TfragTopologyDecode topologyDecode,
        TfragMaterialRange materialRange,
        TfragNormalBuildResult normalBuildResult,
        IReadOnlyList<TfragSourcePosition> sourcePositions,
        IReadOnlyList<TfragRgba> sourceColors,
        IReadOnlyList<float> sourceLightSelectors,
        IReadOnlyList<Vector4> sourceLightBaseColors,
        IReadOnlyList<Vector3> sourceLightNormals,
        IReadOnlyList<float> sourceLightPostScales,
        IReadOnlyList<Vector2?> packetReferenceTexCoords,
        IReadOnlyList<Vector2?> referenceTexCoords,
        IReadOnlyList<Vector3> positions,
        TextureSize? textureSize)
    {
        var remap = new Dictionary<TfragPrimitiveVertexKey, uint>();
        var materialKey = new TfragMaterialKey(texture.TextureId, texture.ClampU, texture.ClampV);
        var group = new TfragPrimitiveGroup(materialKey, topologyPacket, topologyDecode, materialRange, normalBuildResult);
        var startIndex = Math.Clamp(materialRange.StartIndex, 0, topologyDecode.Indices.Count);
        var endIndex = Math.Clamp(materialRange.StartIndex + materialRange.IndexCount, startIndex, topologyDecode.Indices.Count);

        for (var i = startIndex; i < endIndex; i++)
        {
            var sourceIndex = topologyDecode.Indices[i];
            if (sourceIndex >= (uint)positions.Count)
            {
                continue;
            }

            var referenceAddress = i < topologyDecode.ReferenceAddresses.Count
                ? topologyDecode.ReferenceAddresses[i]
                : -1;
            var normal = ResolveTopologyIndexNormal(normalBuildResult, i, sourceIndex);
            var key = TfragPrimitiveVertexKey.From(sourceIndex, referenceAddress, normal);
            if (!remap.TryGetValue(key, out var localIndex))
            {
                localIndex = checked((uint)group.Positions.Count);
                remap[key] = localIndex;
                group.Positions.Add(positions[(int)sourceIndex]);
                group.Normals.Add(normal);
                var texCoord = ResolveReferenceTexCoord(referenceAddress, packetReferenceTexCoords, referenceTexCoords);
                var fallbackTexCoord = sourceIndex < (uint)sourcePositions.Count
                    ? BuildPreviewTexCoord(sourcePositions[(int)sourceIndex])
                    : Vector2.Zero;
                group.TexCoords.Add(texCoord ?? fallbackTexCoord);
                group.Colors.Add(BuildVertexColor(sourceColors, sourceIndex));
                group.LightSelectors.Add(BuildLightSelector(sourceLightSelectors, sourceIndex));
                group.LightBaseColors.Add(BuildLightBaseColor(sourceLightBaseColors, sourceIndex));
                group.LightNormals.Add(BuildLightNormal(sourceLightNormals, sourceIndex, normal));
                group.LightPostScales.Add(BuildLightPostScale(sourceLightPostScales, sourceIndex));
            }

            group.Indices.Add(localIndex);
        }

        if (group.Indices.Count < 3)
        {
            return null;
        }

        AdjustPrimitiveGroupTextureSeams(group, textureSize);
        if (group.Normals.Count != group.Positions.Count)
        {
            ComputeNormals(group);
        }
        OrientPrimitiveGroupTriangleWindingToNormals(group);

        return group;
    }

    private static Vector2? ResolveReferenceTexCoord(
        int referenceAddress,
        IReadOnlyList<Vector2?> packetReferenceTexCoords,
        IReadOnlyList<Vector2?> referenceTexCoords)
    {
        if (referenceAddress < 0)
        {
            return null;
        }

        if (referenceAddress < packetReferenceTexCoords.Count
            && packetReferenceTexCoords[referenceAddress] is { } packetTexCoord)
        {
            return packetTexCoord;
        }

        return referenceAddress < referenceTexCoords.Count
            ? referenceTexCoords[referenceAddress]
            : null;
    }

    private static void AdjustPrimitiveGroupTextureSeams(TfragPrimitiveGroup group, TextureSize? textureSize)
    {
        if (textureSize is not { } resolvedTextureSize
            || group.Indices.Count < 3
            || group.Positions.Count != group.TexCoords.Count
            || group.Positions.Count != group.Normals.Count
            || group.Positions.Count != group.Colors.Count
            || group.Positions.Count != group.LightSelectors.Count
            || group.Positions.Count != group.LightBaseColors.Count
            || group.Positions.Count != group.LightNormals.Count
            || group.Positions.Count != group.LightPostScales.Count)
        {
            return;
        }

        var sourcePositions = group.Positions.ToArray();
        var sourceNormals = group.Normals.ToArray();
        var sourceTexCoords = group.TexCoords.ToArray();
        var sourceColors = group.Colors.ToArray();
        var sourceLightSelectors = group.LightSelectors.ToArray();
        var sourceLightBaseColors = group.LightBaseColors.ToArray();
        var sourceLightNormals = group.LightNormals.ToArray();
        var sourceLightPostScales = group.LightPostScales.ToArray();
        var expandedPositions = new List<Vector3>(group.Indices.Count);
        var expandedNormals = new List<Vector3>(group.Indices.Count);
        var expandedTexCoords = new List<Vector2>(group.Indices.Count);
        var expandedColors = new List<Vector4>(group.Indices.Count);
        var expandedLightSelectors = new List<float>(group.Indices.Count);
        var expandedLightBaseColors = new List<Vector4>(group.Indices.Count);
        var expandedLightNormals = new List<Vector3>(group.Indices.Count);
        var expandedLightPostScales = new List<float>(group.Indices.Count);
        var expandedIndices = new List<uint>(group.Indices.Count);
        var expandedVertexIndexByKey = new Dictionary<TfragExpandedVertexKey, uint>();

        for (var i = 0; i + 2 < group.Indices.Count; i += 3)
        {
            var a = checked((int)group.Indices[i + 0]);
            var b = checked((int)group.Indices[i + 1]);
            var c = checked((int)group.Indices[i + 2]);
            if ((uint)a >= (uint)sourceTexCoords.Length
                || (uint)b >= (uint)sourceTexCoords.Length
                || (uint)c >= (uint)sourceTexCoords.Length)
            {
                continue;
            }

            var repairedTexCoords = RepairRepeatedTileBoundaryTexCoords(
                sourcePositions[a],
                sourcePositions[b],
                sourcePositions[c],
                sourceTexCoords[a],
                sourceTexCoords[b],
                sourceTexCoords[c],
                repeatU: !group.ClampU,
                repeatV: !group.ClampV);

            var adjustedTexCoords = GltfTexCoordUtils.AdjustTriangleTexCoords(
                repairedTexCoords[0],
                repairedTexCoords[1],
                repairedTexCoords[2],
                resolvedTextureSize,
                repeatU: !group.ClampU,
                repeatV: !group.ClampV,
                normalizeClampedAxes: true);

            expandedIndices.Add(GetExpandedVertexIndex(a, adjustedTexCoords[0]));
            expandedIndices.Add(GetExpandedVertexIndex(b, adjustedTexCoords[1]));
            expandedIndices.Add(GetExpandedVertexIndex(c, adjustedTexCoords[2]));
        }

        if (expandedIndices.Count != group.Indices.Count)
        {
            return;
        }

        group.Positions.Clear();
        group.Positions.AddRange(expandedPositions);
        group.Normals.Clear();
        group.Normals.AddRange(expandedNormals);
        group.TexCoords.Clear();
        group.TexCoords.AddRange(expandedTexCoords);
        group.Colors.Clear();
        group.Colors.AddRange(expandedColors);
        group.LightSelectors.Clear();
        group.LightSelectors.AddRange(expandedLightSelectors);
        group.LightBaseColors.Clear();
        group.LightBaseColors.AddRange(expandedLightBaseColors);
        group.LightNormals.Clear();
        group.LightNormals.AddRange(expandedLightNormals);
        group.LightPostScales.Clear();
        group.LightPostScales.AddRange(expandedLightPostScales);
        group.Indices.Clear();
        group.Indices.AddRange(expandedIndices);

        uint GetExpandedVertexIndex(int sourceIndex, Vector2 texCoord)
        {
            var normal = sourceNormals[sourceIndex];
            var key = TfragExpandedVertexKey.From(sourceIndex, texCoord, normal);
            if (expandedVertexIndexByKey.TryGetValue(key, out var expandedIndex))
            {
                return expandedIndex;
            }

            expandedIndex = checked((uint)expandedPositions.Count);
            expandedVertexIndexByKey.Add(key, expandedIndex);
            expandedPositions.Add(sourcePositions[sourceIndex]);
            expandedNormals.Add(normal);
            expandedTexCoords.Add(texCoord);
            expandedColors.Add(sourceColors[sourceIndex]);
            expandedLightSelectors.Add(sourceLightSelectors[sourceIndex]);
            expandedLightBaseColors.Add(sourceLightBaseColors[sourceIndex]);
            expandedLightNormals.Add(sourceLightNormals[sourceIndex]);
            expandedLightPostScales.Add(sourceLightPostScales[sourceIndex]);
            return expandedIndex;
        }
    }

    private static Vector3 ResolveTopologyIndexNormal(
        TfragNormalBuildResult normalBuildResult,
        int indexOffset,
        uint sourceIndex)
    {
        if (indexOffset >= 0 && indexOffset < normalBuildResult.IndexNormals.Count)
        {
            return normalBuildResult.IndexNormals[indexOffset];
        }

        return sourceIndex < (uint)normalBuildResult.VertexNormals.Count
            ? normalBuildResult.VertexNormals[(int)sourceIndex]
            : Vector3.UnitY;
    }

    private static Vector4 BuildVertexColor(IReadOnlyList<TfragRgba> sourceColors, uint sourceIndex)
    {
        if (sourceIndex >= (uint)sourceColors.Count)
        {
            return Vector4.One;
        }

        var color = sourceColors[(int)sourceIndex];
        return Ps2Color.ToGltfVertexColor(color.R, color.G, color.B, color.A);
    }

    private static float BuildLightSelector(IReadOnlyList<float> sourceLightSelectors, uint sourceIndex)
    {
        return sourceIndex < (uint)sourceLightSelectors.Count
            ? sourceLightSelectors[(int)sourceIndex]
            : 0x000F;
    }

    private static Vector4 BuildLightBaseColor(IReadOnlyList<Vector4> sourceLightBaseColors, uint sourceIndex)
    {
        return sourceIndex < (uint)sourceLightBaseColors.Count
            ? sourceLightBaseColors[(int)sourceIndex]
            : Vector4.One;
    }

    private static Vector3 BuildLightNormal(
        IReadOnlyList<Vector3> sourceLightNormals,
        uint sourceIndex,
        Vector3 fallbackNormal)
    {
        if (sourceIndex < (uint)sourceLightNormals.Count)
        {
            var lightNormal = sourceLightNormals[(int)sourceIndex];
            if (lightNormal.LengthSquared() > 0.00000001f)
            {
                return lightNormal;
            }
        }

        return fallbackNormal.LengthSquared() > 0.00000001f
            ? Vector3.Normalize(fallbackNormal)
            : Vector3.UnitY;
    }

    private static float BuildLightPostScale(IReadOnlyList<float> sourceLightPostScales, uint sourceIndex)
    {
        return sourceIndex < (uint)sourceLightPostScales.Count
            ? sourceLightPostScales[(int)sourceIndex]
            : 1f;
    }

    private static Vector2[] RepairRepeatedTileBoundaryTexCoords(
        Vector3 positionA,
        Vector3 positionB,
        Vector3 positionC,
        Vector2 texCoordA,
        Vector2 texCoordB,
        Vector2 texCoordC,
        bool repeatU,
        bool repeatV)
    {
        var area = TriangleGeometryUtils.Area(positionA, positionB, positionC);
        var u = repeatU
            ? RepairRepeatedTileBoundaryAxis(
                texCoordA.X,
                texCoordB.X,
                texCoordC.X,
                texCoordA.Y,
                texCoordB.Y,
                texCoordC.Y,
                area)
            : [texCoordA.X, texCoordB.X, texCoordC.X];
        var v = repeatV
            ? RepairRepeatedTileBoundaryAxis(
                texCoordA.Y,
                texCoordB.Y,
                texCoordC.Y,
                texCoordA.X,
                texCoordB.X,
                texCoordC.X,
                area)
            : [texCoordA.Y, texCoordB.Y, texCoordC.Y];

        return
        [
            new Vector2(u[0], v[0]),
            new Vector2(u[1], v[1]),
            new Vector2(u[2], v[2])
        ];
    }

    private static float[] RepairRepeatedTileBoundaryAxis(
        float a,
        float b,
        float c,
        float crossA,
        float crossB,
        float crossC,
        float triangleArea)
    {
        var min = MathF.Min(a, MathF.Min(b, c));
        var max = MathF.Max(a, MathF.Max(b, c));
        var span = max - min;
        if (span <= 0f
            || span > TinyRepeatedTileBoundarySpan
            || triangleArea < MinRepeatedTileBoundaryTriangleArea)
        {
            return [a, b, c];
        }

        var crossSpan = MathF.Max(crossA, MathF.Max(crossB, crossC)) - MathF.Min(crossA, MathF.Min(crossB, crossC));
        if (crossSpan < BroadRepeatedTileBoundaryCrossSpan)
        {
            return [a, b, c];
        }

        var boundary = MathF.Round((min + max) * 0.5f);
        if (!IsNearRepeatedTileBoundary(min, boundary)
            && !IsNearRepeatedTileBoundary(max, boundary))
        {
            return [a, b, c];
        }

        if (max <= boundary + RepeatedTileBoundaryEpsilon && min < boundary)
        {
            return
            [
                a < boundary ? a - 1f : a,
                b < boundary ? b - 1f : b,
                c < boundary ? c - 1f : c
            ];
        }

        if (min >= boundary - RepeatedTileBoundaryEpsilon && max > boundary)
        {
            return
            [
                a > boundary ? a + 1f : a,
                b > boundary ? b + 1f : b,
                c > boundary ? c + 1f : c
            ];
        }

        if (min < boundary && max > boundary)
        {
            return
            [
                a < boundary ? a - 1f : a,
                b < boundary ? b - 1f : b,
                c < boundary ? c - 1f : c
            ];
        }

        return [a, b, c];
    }

    private static bool IsNearRepeatedTileBoundary(float value, float boundary)
    {
        return MathF.Abs(value - boundary) <= RepeatedTileBoundaryEpsilon;
    }

    private static Vector2 BuildPreviewTexCoord(TfragSourcePosition position)
    {
        return new Vector2(position.X / 4096f, position.Y / 4096f);
    }

    private static void ComputeNormals(TfragPrimitiveGroup group)
    {
        group.Normals.Clear();
        for (var i = 0; i < group.Positions.Count; i++)
        {
            group.Normals.Add(Vector3.Zero);
        }

        for (var i = 0; i + 2 < group.Indices.Count; i += 3)
        {
            var a = (int)group.Indices[i + 0];
            var b = (int)group.Indices[i + 1];
            var c = (int)group.Indices[i + 2];
            var normal = Vector3.Cross(
                group.Positions[b] - group.Positions[a],
                group.Positions[c] - group.Positions[a]);
            if (normal.LengthSquared() <= 0.00000001f)
            {
                continue;
            }

            normal = Vector3.Normalize(normal);
            group.Normals[a] += normal;
            group.Normals[b] += normal;
            group.Normals[c] += normal;
        }

        for (var i = 0; i < group.Normals.Count; i++)
        {
            group.Normals[i] = group.Normals[i].LengthSquared() <= 0.00000001f
                ? Vector3.UnitY
                : Vector3.Normalize(group.Normals[i]);
        }
    }

    private static void OrientPrimitiveGroupTriangleWindingToNormals(TfragPrimitiveGroup group)
    {
        if (group.Positions.Count != group.Normals.Count)
        {
            return;
        }

        for (var i = 0; i + 2 < group.Indices.Count; i += 3)
        {
            var a = checked((int)group.Indices[i + 0]);
            var b = checked((int)group.Indices[i + 1]);
            var c = checked((int)group.Indices[i + 2]);
            if ((uint)a >= (uint)group.Positions.Count
                || (uint)b >= (uint)group.Positions.Count
                || (uint)c >= (uint)group.Positions.Count)
            {
                continue;
            }

            var faceNormal = Vector3.Cross(
                group.Positions[b] - group.Positions[a],
                group.Positions[c] - group.Positions[a]);
            var averageNormal = group.Normals[a] + group.Normals[b] + group.Normals[c];
            if (faceNormal.LengthSquared() <= 0.00000001f
                || averageNormal.LengthSquared() <= 0.00000001f)
            {
                continue;
            }

            var dot = Vector3.Dot(Vector3.Normalize(faceNormal), Vector3.Normalize(averageNormal));
            if (dot >= OpposedTriangleNormalDotEpsilon)
            {
                continue;
            }

            group.Indices[i + 1] = (uint)c;
            group.Indices[i + 2] = (uint)b;
            group.WindingCorrectedTriangleCount++;
        }
    }

    private static string TriangleKey(uint a, uint b, uint c)
    {
        Span<uint> values = [a, b, c];
        values.Sort();
        return $"{values[0]}:{values[1]}:{values[2]}";
    }
}

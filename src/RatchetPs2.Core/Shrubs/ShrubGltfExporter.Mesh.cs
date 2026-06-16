using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Shrubs;

public static partial class ShrubGltfExporter
{
    private static ShrubMesh BuildMesh(ShrubClass shrub, ShrubGltfExportOptions options)
    {
        var groups = new List<ShrubPrimitiveGroup>();
        var currentTextureId = -1;
        var sourcePrimitiveIndex = 0;

        foreach (var packet in shrub.Packets)
        {
            foreach (var primitive in packet.Primitives)
            {
                if (primitive is ShrubTexturePrimitive texturePrimitive)
                {
                    currentTextureId = texturePrimitive.TextureId;
                    sourcePrimitiveIndex++;
                    continue;
                }

                if (primitive is not ShrubVertexPrimitive vertexPrimitive)
                {
                    sourcePrimitiveIndex++;
                    continue;
                }

                var group = groups.LastOrDefault();
                if (group is null || group.TextureId != currentTextureId || group.PacketIndex != packet.PacketIndex)
                {
                    group = new ShrubPrimitiveGroup(
                        currentTextureId,
                        packet.PacketIndex,
                        sourcePrimitiveIndex);
                    groups.Add(group);
                }

                group.LastSourcePrimitiveIndex = sourcePrimitiveIndex;
                AppendVertexPrimitive(shrub, vertexPrimitive, group, options.PositionScale);
                sourcePrimitiveIndex++;
            }
        }

        groups.RemoveAll(group => group.Indices.Count == 0);
        return new ShrubMesh(groups);
    }

    private static void AppendVertexPrimitive(
        ShrubClass shrub,
        ShrubVertexPrimitive primitive,
        ShrubPrimitiveGroup group,
        float positionScale)
    {
        var baseIndex = group.Positions.Count;
        foreach (var vertex in primitive.Vertices)
        {
            group.Positions.Add(GltfCoordinateBasis.FromPs2Position(
                vertex.X * shrub.Header.Scale * positionScale,
                vertex.Y * shrub.Header.Scale * positionScale,
                vertex.Z * shrub.Header.Scale * positionScale));
            group.Normals.Add(ReadNormal(shrub, vertex.NormalIndex));
            group.TexCoords.Add(new Vector2(vertex.S / 4096f, vertex.T / 4096f));
            group.SourceVertexCount++;
        }

        if (primitive.GeometryType == ShrubGeometryType.TriangleList)
        {
            for (var i = 0; i + 2 < primitive.Vertices.Count; i += 3)
            {
                AddTriangle(group, baseIndex + i, baseIndex + i + 1, baseIndex + i + 2);
            }
        }
        else
        {
            for (var i = 0; i + 2 < primitive.Vertices.Count; i++)
            {
                AddTriangle(group, baseIndex + i, baseIndex + i + 1, baseIndex + i + 2);
            }
        }
    }

    private static Vector3 ReadNormal(ShrubClass shrub, int normalIndex)
    {
        if ((uint)normalIndex >= (uint)shrub.Normals.Count)
        {
            return Vector3.UnitY;
        }

        var normal = shrub.Normals[normalIndex];
        var vector = GltfCoordinateBasis.FromPs2Position(
            normal.X / (float)short.MaxValue,
            normal.Y / (float)short.MaxValue,
            normal.Z / (float)short.MaxValue);
        return vector.LengthSquared() <= 0.00000001f ? Vector3.UnitY : Vector3.Normalize(vector);
    }

    private static void AddTriangle(ShrubPrimitiveGroup group, int a, int b, int c)
    {
        var pa = group.Positions[a];
        var pb = group.Positions[b];
        var pc = group.Positions[c];
        var faceNormal = Vector3.Cross(pb - pa, pc - pa);
        if (faceNormal.LengthSquared() <= 0.00000001f)
        {
            return;
        }

        var normal = group.Normals[a] + group.Normals[b] + group.Normals[c];
        if (normal.LengthSquared() > 0.00000001f && Vector3.Dot(faceNormal, normal) < 0)
        {
            (b, c) = (c, b);
            group.WindingCorrectedTriangleCount++;
        }

        group.Indices.Add(checked((uint)a));
        group.Indices.Add(checked((uint)b));
        group.Indices.Add(checked((uint)c));
        group.TriangleCount++;
    }
}

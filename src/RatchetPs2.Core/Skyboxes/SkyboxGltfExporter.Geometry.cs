using System.Numerics;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static SkyboxMesh BuildMesh(Skybox skybox, SkyboxGltfExportOptions options)
    {
        var positionScale = options.PositionScale;
        if (positionScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(positionScale));
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var colors = new List<Vector4>();
        var primitiveBuilders = new List<SkyboxPrimitiveBuilder>();
        SkyboxPrimitiveBuilder? currentPrimitive = null;
        var usesUntexturedGouraudColors = false;

        foreach (var shell in skybox.Shells)
        {
            foreach (var cluster in shell.Clusters)
            {
                foreach (var triangle in cluster.Triangles)
                {
                    var textureId = triangle.TextureId;
                    if (currentPrimitive is null
                        || currentPrimitive.TextureId != textureId
                        || currentPrimitive.ShellIndex != shell.Index)
                    {
                        currentPrimitive = new SkyboxPrimitiveBuilder(
                            primitiveBuilders.Count,
                            shell.Index,
                            shell.Flags,
                            HasShellRotation(shell),
                            cluster.Index,
                            textureId);
                        primitiveBuilders.Add(currentPrimitive);
                    }

                    var p0 = cluster.Vertices[triangle.A].ToGltfPosition(positionScale);
                    var p1 = cluster.Vertices[triangle.B].ToGltfPosition(positionScale);
                    var p2 = cluster.Vertices[triangle.C].ToGltfPosition(positionScale);
                    var normal = BuildNormal(p0, p1, p2);
                    var baseIndex = checked((uint)positions.Count);

                    positions.Add(p0);
                    positions.Add(p1);
                    positions.Add(p2);
                    normals.Add(normal);
                    normals.Add(normal);
                    normals.Add(normal);
                    texCoords.Add(cluster.TexCoords[triangle.A].ToGltfTexCoord());
                    texCoords.Add(cluster.TexCoords[triangle.B].ToGltfTexCoord());
                    texCoords.Add(cluster.TexCoords[triangle.C].ToGltfTexCoord());
                    var useGouraudColor = options.DecodeUntexturedGouraudColors
                        && textureId == UntexturedTextureId;
                    var c0 = BuildVertexColor(cluster.Vertices[triangle.A], cluster.TexCoords[triangle.A], useGouraudColor);
                    var c1 = BuildVertexColor(cluster.Vertices[triangle.B], cluster.TexCoords[triangle.B], useGouraudColor);
                    var c2 = BuildVertexColor(cluster.Vertices[triangle.C], cluster.TexCoords[triangle.C], useGouraudColor);
                    usesUntexturedGouraudColors |= useGouraudColor;
                    colors.Add(c0);
                    colors.Add(c1);
                    colors.Add(c2);
                    currentPrimitive.AddVertexAlpha(c0.W);
                    currentPrimitive.AddVertexAlpha(c1.W);
                    currentPrimitive.AddVertexAlpha(c2.W);
                    currentPrimitive.LastClusterIndex = cluster.Index;
                    currentPrimitive.Indices.Add(baseIndex);
                    currentPrimitive.Indices.Add(baseIndex + 1);
                    currentPrimitive.Indices.Add(baseIndex + 2);
                }
            }
        }

        if (positions.Count == 0)
        {
            throw new InvalidDataException("Skybox has no decoded triangles to export.");
        }

        var primitives = primitiveBuilders
            .OrderBy(builder => builder.SourceDrawOrder)
            .Select((builder, drawOrder) => builder.ToPrimitive(drawOrder))
            .ToArray();
        var vertexAlphaByTextureId = primitiveBuilders
            .GroupBy(builder => builder.TextureId)
            .ToDictionary(
                group => group.Key,
                group => SkyboxVertexAlphaInfo.Combine(group.Select(builder => builder.VertexAlpha)));
        var textureIds = primitives
            .Select(primitive => primitive.TextureId)
            .Distinct()
            .ToArray();
        return new SkyboxMesh(
            positions,
            normals,
            texCoords,
            colors,
            primitives,
            textureIds,
            vertexAlphaByTextureId,
            usesUntexturedGouraudColors);
    }

    private static SkyboxShellGeometry BuildShellGeometry(
        SkyboxMesh mesh,
        IReadOnlyList<SkyboxPrimitive> shellPrimitives)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var colors = new List<Vector4>();
        var primitiveGeometries = new List<SkyboxShellPrimitiveGeometry>(shellPrimitives.Count);
        var localIndexBySourceIndex = new Dictionary<uint, uint>();

        foreach (var primitive in shellPrimitives)
        {
            var localIndices = new List<uint>(primitive.Indices.Count);
            foreach (var sourceIndex in primitive.Indices)
            {
                if (!localIndexBySourceIndex.TryGetValue(sourceIndex, out var localIndex))
                {
                    localIndex = checked((uint)positions.Count);
                    localIndexBySourceIndex[sourceIndex] = localIndex;
                    positions.Add(mesh.Positions[checked((int)sourceIndex)]);
                    normals.Add(mesh.Normals[checked((int)sourceIndex)]);
                    texCoords.Add(mesh.TexCoords[checked((int)sourceIndex)]);
                    colors.Add(mesh.Colors[checked((int)sourceIndex)]);
                }

                localIndices.Add(localIndex);
            }

            primitiveGeometries.Add(new SkyboxShellPrimitiveGeometry(primitive, localIndices));
        }

        return new SkyboxShellGeometry(
            positions,
            normals,
            texCoords,
            colors,
            primitiveGeometries);
    }

    private static Vector4 BuildVertexColor(SkyboxVertex vertex, SkyboxTexCoord texCoord, bool useGouraudColor)
    {
        if (!useGouraudColor)
        {
            return vertex.ToGltfColor();
        }

        return texCoord.ToGltfGouraudColor();
    }

    private static Vector3 BuildNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a);
        return normal.LengthSquared() < 0.00000001f
            ? Vector3.UnitY
            : Vector3.Normalize(normal);
    }
}

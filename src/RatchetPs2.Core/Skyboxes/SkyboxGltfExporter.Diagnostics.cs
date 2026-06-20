using System.Text.Json;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static byte[] BuildDiagnosticsBytes(
        Skybox skybox,
        SkyboxMesh mesh,
        IReadOnlyList<SkyboxGltfTextureResource> textureResources,
        string gameLabel,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides,
        JsonSerializerOptions jsonOptions)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = $"{gameLabel} skybox geometry",
            Note = "Preview geometry reconstructed from packed skybox shell clusters, 8-byte source vertices, ST texture coordinates, and 4-byte triangle records.",
            skybox.ByteLength,
            Header = new
            {
                Color = new
                {
                    skybox.Header.Color.R,
                    skybox.Header.Color.G,
                    skybox.Header.Color.B,
                    skybox.Header.Color.A
                },
                skybox.Header.ClearScreen,
                skybox.Header.ShellCount,
                skybox.Header.SpriteCount,
                skybox.Header.SpriteMax,
                skybox.Header.TextureCount,
                skybox.Header.FxCount,
                TextureDefOffset = FormatOffset(skybox.Header.TextureDefOffset),
                TextureDataOffset = FormatOffset(skybox.Header.TextureDataOffset),
                FxListOffset = FormatOffset(skybox.Header.FxListOffset),
                SpritesOffset = FormatOffset(skybox.Header.SpritesOffset)
            },
            ShellCount = skybox.Shells.Count,
            ClusterCount = skybox.Shells.Sum(shell => shell.Clusters.Count),
            SourceVertexCount = skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Vertices.Count),
            mesh.PositionCount,
            mesh.ColorCount,
            mesh.TriangleCount,
            PrimitiveCount = mesh.Primitives.Count,
            TexturedTriangleCount = mesh.Primitives
                .Where(primitive => primitive.TextureId != UntexturedTextureId)
                .Sum(primitive => primitive.TriangleCount),
            UntexturedTriangleCount = mesh.Primitives
                .Where(primitive => primitive.TextureId == UntexturedTextureId)
                .Sum(primitive => primitive.TriangleCount),
            TextureTriangleCounts = mesh.Primitives
                .GroupBy(primitive => primitive.TextureId)
                .ToDictionary(
                    group => group.Key == UntexturedTextureId ? "untextured" : group.Key.ToString(),
                    group => group.Sum(primitive => primitive.TriangleCount)),
            DrawPrimitives = mesh.Primitives.Select(primitive => new
            {
                primitive.DrawOrder,
                primitive.SourceDrawOrder,
                primitive.ShellIndex,
                primitive.ShellFlags,
                DrawBlendMode = DrawBlendModeForShellFlags(primitive.ShellFlags),
                primitive.FirstClusterIndex,
                primitive.LastClusterIndex,
                TextureId = primitive.TextureId == UntexturedTextureId ? "untextured" : primitive.TextureId.ToString(),
                primitive.TriangleCount,
                RuntimeRotation = BuildShellRotationExtras(skybox.Shells[primitive.ShellIndex], runtimeFrameRate, shellRotationOverrides)
            }).ToArray(),
            Textures = textureResources.Select(texture => new
            {
                texture.Index,
                texture.Uri,
                texture.Size.Width,
                texture.Size.Height,
                texture.Alpha.MinAlpha,
                texture.Alpha.MaxAlpha,
                texture.Alpha.UsesBinaryAlpha,
                AlphaMode = texture.Alpha.AlphaMode.ToString()
            }).ToArray(),
            Shells = skybox.Shells.Select(shell => new
            {
                shell.Index,
                Offset = FormatOffset(shell.Offset),
                shell.Flags,
                shell.ClusterCount,
                Rotation = BuildShellRotationExtras(shell, runtimeFrameRate, shellRotationOverrides),
                VertexCount = shell.Clusters.Sum(cluster => cluster.VertexCount),
                TriangleCount = shell.Clusters.Sum(cluster => cluster.TriangleCount)
            }).ToArray()
        }, jsonOptions);
    }
}

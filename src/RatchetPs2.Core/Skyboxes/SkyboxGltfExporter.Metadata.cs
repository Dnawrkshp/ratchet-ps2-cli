using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static object BuildNodeExtras(
        Skybox skybox,
        string gameLabel,
        float positionScale,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        return new
        {
            Game = gameLabel,
            SourceFormat = "SKY",
            CoordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription,
            PositionScale = positionScale,
            RuntimeFrameRate = runtimeFrameRate,
            RotationTickRadians = RotationTickRadians,
            RuntimeRotatingShellCount = skybox.Shells.Count(HasShellRuntimeRotation),
            RuntimeRotationPatchCount = skybox.Shells.Count(shell => shellRotationOverrides.ContainsKey(shell.Index)),
            skybox.Header.ShellCount,
            skybox.Header.TextureCount
        };
    }

    private static object BuildMeshExtras(
        Skybox skybox,
        SkyboxMesh mesh,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        return new
        {
            ShellCount = skybox.Shells.Count,
            ClusterCount = skybox.Shells.Sum(shell => shell.Clusters.Count),
            SourceVertexCount = skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Vertices.Count),
            mesh.PositionCount,
            mesh.TriangleCount,
            mesh.UsesUntexturedGouraudColors,
            RuntimeFrameRate = runtimeFrameRate,
            RotationTickRadians = RotationTickRadians,
            RuntimeRotatingShellCount = skybox.Shells.Count(HasShellRuntimeRotation),
            RuntimeRotationPatchCount = skybox.Shells.Count(shell => shellRotationOverrides.ContainsKey(shell.Index)),
            TextureIds = mesh.TextureIds.Select(textureId => textureId == UntexturedTextureId ? "untextured" : textureId.ToString()).ToArray()
        };
    }

    private static object BuildShellNodeExtras(
        SkyboxShell shell,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        var rotation = BuildShellRotationMetadata(shell, runtimeFrameRate, shellRotationOverrides);
        return new
        {
            SkyboxShellIndex = shell.Index,
            SkyboxShellFlags = shell.Flags,
            shell.ClusterCount,
            VertexCount = shell.Clusters.Sum(cluster => cluster.VertexCount),
            TriangleCount = shell.Clusters.Sum(cluster => cluster.TriangleCount),
            rotation.SkyboxShellFileRotationRaw,
            rotation.SkyboxShellRotationRaw,
            rotation.SkyboxShellRotationRadians,
            rotation.SkyboxShellRotationDeltaRaw,
            rotation.SkyboxShellAngularVelocityRadiansPerSecond,
            rotation.SkyboxShellHasRuntimeRotation,
            rotation.SkyboxShellRotationPatchApplied,
            rotation.SkyboxShellRotationPatchReason,
            SkyboxRotationTickRadians = RotationTickRadians,
            SkyboxRuntimeFrameRate = runtimeFrameRate
        };
    }

    private static object BuildShellMeshExtras(
        SkyboxShell shell,
        SkyboxShellGeometry shellGeometry,
        IReadOnlyList<SkyboxPrimitive> primitives,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        var rotation = BuildShellRotationMetadata(shell, runtimeFrameRate, shellRotationOverrides);
        return new
        {
            SkyboxShellIndex = shell.Index,
            SkyboxShellFlags = shell.Flags,
            shell.ClusterCount,
            VertexCount = shellGeometry.Positions.Count,
            TriangleCount = primitives.Sum(primitive => primitive.TriangleCount),
            PrimitiveCount = primitives.Count,
            TextureIds = primitives
                .Select(primitive => primitive.TextureId)
                .Distinct()
                .Select(textureId => textureId == UntexturedTextureId ? "untextured" : textureId.ToString())
                .ToArray(),
            rotation.SkyboxShellFileRotationRaw,
            rotation.SkyboxShellRotationRaw,
            rotation.SkyboxShellRotationRadians,
            rotation.SkyboxShellRotationDeltaRaw,
            rotation.SkyboxShellAngularVelocityRadiansPerSecond,
            rotation.SkyboxShellHasRuntimeRotation,
            rotation.SkyboxShellRotationPatchApplied,
            rotation.SkyboxShellRotationPatchReason,
            SkyboxRotationTickRadians = RotationTickRadians,
            SkyboxRuntimeFrameRate = runtimeFrameRate
        };
    }

    private static object BuildPrimitiveExtras(
        SkyboxPrimitive primitive,
        SkyboxShell shell,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        var rotation = BuildShellRotationMetadata(shell, runtimeFrameRate, shellRotationOverrides);
        return new
        {
            SkyboxDrawOrder = primitive.DrawOrder,
            SkyboxSourceDrawOrder = primitive.SourceDrawOrder,
            SkyboxShellIndex = primitive.ShellIndex,
            SkyboxShellFlags = primitive.ShellFlags,
            SkyboxDrawBlendMode = DrawBlendModeForShellFlags(primitive.ShellFlags),
            SkyboxFirstClusterIndex = primitive.FirstClusterIndex,
            SkyboxLastClusterIndex = primitive.LastClusterIndex,
            SkyboxTextureId = primitive.TextureId,
            SkyboxTextureName = TextureName(primitive.TextureId),
            SkyboxTriangleCount = primitive.TriangleCount,
            rotation.SkyboxShellFileRotationRaw,
            rotation.SkyboxShellRotationRaw,
            rotation.SkyboxShellRotationRadians,
            rotation.SkyboxShellRotationDeltaRaw,
            rotation.SkyboxShellAngularVelocityRadiansPerSecond,
            rotation.SkyboxShellHasRuntimeRotation,
            rotation.SkyboxShellRotationPatchApplied,
            rotation.SkyboxShellRotationPatchReason,
            SkyboxRotationTickRadians = RotationTickRadians,
            SkyboxRuntimeFrameRate = runtimeFrameRate
        };
    }

    private static object BuildShellRotationExtras(
        SkyboxShell shell,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        var rotation = BuildShellRotationMetadata(shell, runtimeFrameRate, shellRotationOverrides);
        return new
        {
            rotation.SkyboxShellFileRotationRaw,
            rotation.SkyboxShellRotationRaw,
            rotation.SkyboxShellRotationRadians,
            rotation.SkyboxShellRotationDeltaRaw,
            rotation.SkyboxShellAngularVelocityRadiansPerSecond,
            rotation.SkyboxShellHasRuntimeRotation,
            rotation.SkyboxShellRotationPatchApplied,
            rotation.SkyboxShellRotationPatchReason,
            SkyboxRotationTickRadians = RotationTickRadians,
            SkyboxRuntimeFrameRate = runtimeFrameRate
        };
    }

    private static SkyboxShellRotationMetadata BuildShellRotationMetadata(
        SkyboxShell shell,
        float runtimeFrameRate,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        var hasRotationOverride = shellRotationOverrides.TryGetValue(shell.Index, out var rotationOverride);
        var rotationX = rotationOverride?.RotationX ?? shell.RotationX;
        var rotationY = rotationOverride?.RotationY ?? shell.RotationY;
        var rotationZ = rotationOverride?.RotationZ ?? shell.RotationZ;

        return new SkyboxShellRotationMetadata(
            SourceVector(shell.RotationX, shell.RotationY, shell.RotationZ),
            SourceVector(rotationX, rotationY, rotationZ),
            ToGltfRotationVector(rotationX, rotationY, rotationZ, RotationTickRadians),
            SourceVector(shell.RotationDeltaX, shell.RotationDeltaY, shell.RotationDeltaZ),
            ToGltfRotationVector(shell.RotationDeltaX, shell.RotationDeltaY, shell.RotationDeltaZ, RotationTickRadians * runtimeFrameRate),
            HasShellRuntimeRotation(shell),
            hasRotationOverride,
            hasRotationOverride ? rotationOverride?.Reason ?? string.Empty : string.Empty);
    }

    private static int[] SourceVector(short x, short y, short z)
    {
        return [x, y, z];
    }

    private static float[] ToGltfRotationVector(short sourceX, short sourceY, short sourceZ, float scale)
    {
        var gltf = GltfCoordinateBasis.FromPs2Position(sourceX * scale, sourceY * scale, sourceZ * scale);
        return [gltf.X, gltf.Y, gltf.Z];
    }
}

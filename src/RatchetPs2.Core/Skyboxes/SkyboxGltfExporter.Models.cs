using System.Numerics;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static string TextureName(byte textureId)
    {
        return textureId == UntexturedTextureId ? "untextured" : $"tex.{textureId:0000}";
    }

    private static string BuildShellName(string rootName, int shellIndex)
    {
        return $"{rootName}_shell_{shellIndex:00}";
    }

    private static string DrawBlendModeForShellFlags(short flags)
    {
        return (flags & 0x2) != 0 ? SkyboxDrawBlendMode.Bloom : SkyboxDrawBlendMode.SourceOver;
    }

    private static bool HasShellRotation(SkyboxShell shell)
    {
        return shell.RotationX != 0
            || shell.RotationY != 0
            || shell.RotationZ != 0
            || shell.RotationDeltaX != 0
            || shell.RotationDeltaY != 0
            || shell.RotationDeltaZ != 0;
    }

    private static bool HasShellRuntimeRotation(SkyboxShell shell)
    {
        return shell.RotationDeltaX != 0
            || shell.RotationDeltaY != 0
            || shell.RotationDeltaZ != 0;
    }

    private static bool HasShellRuntimeRotation(
        SkyboxShell shell,
        IReadOnlyDictionary<int, SkyboxShellRotationOverride> shellRotationOverrides)
    {
        return HasShellRuntimeRotation(shell)
            || (shellRotationOverrides.TryGetValue(shell.Index, out var rotationOverride)
                && rotationOverride.RotationDeltaRadiansPerFrame is { } rotationDelta
                && rotationDelta != Vector3.Zero);
    }

    private static class SkyboxDrawBlendMode
    {
        public const string SourceOver = "SourceOver";
        public const string Bloom = "Bloom";
    }

    private sealed record SkyboxMesh(
        List<Vector3> Positions,
        List<Vector3> Normals,
        List<Vector2> TexCoords,
        List<Vector4> Colors,
        IReadOnlyList<SkyboxPrimitive> Primitives,
        byte[] TextureIds,
        IReadOnlyDictionary<byte, SkyboxVertexAlphaInfo> VertexAlphaByTextureId,
        bool UsesUntexturedGouraudColors)
    {
        public int PositionCount => Positions.Count;

        public int ColorCount => Colors.Count;

        public int TriangleCount => Primitives.Sum(primitive => primitive.TriangleCount);
    }

    private sealed record SkyboxPrimitive(
        int DrawOrder,
        int SourceDrawOrder,
        int ShellIndex,
        short ShellFlags,
        int FirstClusterIndex,
        int LastClusterIndex,
        byte TextureId,
        List<uint> Indices)
    {
        public int TriangleCount => Indices.Count / 3;
    }

    private sealed record SkyboxShellGeometry(
        List<Vector3> Positions,
        List<Vector3> Normals,
        List<Vector2> TexCoords,
        List<Vector4> Colors,
        IReadOnlyList<SkyboxShellPrimitiveGeometry> Primitives);

    private sealed record SkyboxShellPrimitiveGeometry(
        SkyboxPrimitive Primitive,
        List<uint> Indices);

    private sealed class SkyboxPrimitiveBuilder
    {
        public SkyboxPrimitiveBuilder(
            int sourceDrawOrder,
            int shellIndex,
            short shellFlags,
            bool hasRotation,
            int firstClusterIndex,
            byte textureId)
        {
            SourceDrawOrder = sourceDrawOrder;
            ShellIndex = shellIndex;
            ShellFlags = shellFlags;
            HasRotation = hasRotation;
            FirstClusterIndex = firstClusterIndex;
            LastClusterIndex = firstClusterIndex;
            TextureId = textureId;
        }

        public int SourceDrawOrder { get; }

        public int ShellIndex { get; }

        public short ShellFlags { get; }

        public bool HasRotation { get; }

        public int VisualSortBucket => (ShellFlags & 1) != 0
            ? 0
            : HasRotation ? 1 : 2;

        public int FirstClusterIndex { get; }

        public int LastClusterIndex { get; set; }

        public byte TextureId { get; }

        public List<uint> Indices { get; } = [];

        public SkyboxVertexAlphaInfo VertexAlpha { get; private set; } = SkyboxVertexAlphaInfo.Empty;

        public void AddVertexAlpha(float alpha)
        {
            VertexAlpha = VertexAlpha.Add(alpha);
        }

        public SkyboxPrimitive ToPrimitive(int drawOrder)
        {
            return new SkyboxPrimitive(
                drawOrder,
                SourceDrawOrder,
                ShellIndex,
                ShellFlags,
                FirstClusterIndex,
                LastClusterIndex,
                TextureId,
                Indices);
        }
    }

    private readonly record struct SkyboxVertexAlphaInfo(float MinAlpha, float MaxAlpha, bool UsesBinaryAlpha)
    {
        public static SkyboxVertexAlphaInfo Empty { get; } = new(1f, 0f, true);

        public static SkyboxVertexAlphaInfo Opaque { get; } = new(1f, 1f, true);

        public bool HasAlpha => MinAlpha < 0.999f;

        public SkyboxVertexAlphaInfo Add(float alpha)
        {
            var clamped = Math.Clamp(alpha, 0f, 1f);
            return new SkyboxVertexAlphaInfo(
                Math.Min(MinAlpha, clamped),
                Math.Max(MaxAlpha, clamped),
                UsesBinaryAlpha && (clamped <= 0.001f || clamped >= 0.999f));
        }

        public static SkyboxVertexAlphaInfo Combine(IEnumerable<SkyboxVertexAlphaInfo> values)
        {
            var result = Empty;
            foreach (var value in values)
            {
                result = new SkyboxVertexAlphaInfo(
                    Math.Min(result.MinAlpha, value.MinAlpha),
                    Math.Max(result.MaxAlpha, value.MaxAlpha),
                    result.UsesBinaryAlpha && value.UsesBinaryAlpha);
            }

            return result.MaxAlpha <= 0f ? Opaque : result;
        }
    }

    private sealed record SkyboxShellRotationMetadata(
        int[] SkyboxShellFileRotationRaw,
        int[] SkyboxShellRotationRaw,
        float[] SkyboxShellRotationRadians,
        int[] SkyboxShellRotationDeltaRaw,
        float[] SkyboxShellSourceAngularVelocityRadiansPerSecond,
        float[] SkyboxShellAngularVelocityRadiansPerSecond,
        bool SkyboxShellHasRuntimeRotation,
        bool SkyboxShellRotationPatchApplied,
        string SkyboxShellRotationPatchReason);

    private readonly record struct SkyboxMaterialAlphaInfo(bool HasAlpha, bool UsesBinaryAlpha)
    {
        public TextureAlphaMode AlphaMode => !HasAlpha
            ? TextureAlphaMode.Opaque
            : UsesBinaryAlpha ? TextureAlphaMode.Mask : TextureAlphaMode.Blend;

        public string? GltfAlphaMode => AlphaMode switch
        {
            TextureAlphaMode.Mask => "MASK",
            TextureAlphaMode.Blend => "BLEND",
            _ => null
        };
    }

    private readonly record struct SkyboxMaterialKey(byte TextureId, string DrawBlendMode)
    {
        public static SkyboxMaterialKey ForPrimitive(SkyboxPrimitive primitive)
        {
            return new SkyboxMaterialKey(
                primitive.TextureId,
                DrawBlendModeForShellFlags(primitive.ShellFlags));
        }
    }

    private sealed record MaterialBuildResult(
        List<Dictionary<string, object>> Materials,
        Dictionary<SkyboxMaterialKey, int> MaterialIndexByKey,
        bool UsesBloomEmission);
}

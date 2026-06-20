using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public static partial class ShrubGltfExporter
{
    private static ShrubBillboardMeshBuild? BuildBillboardMesh(
        ShrubClass shrub,
        ShrubMesh mesh,
        ShrubGltfExportOptions options,
        GltfBufferWriter gltfBufferWriter,
        int meshIndex,
        int materialIndex,
        int textureIndex)
    {
        if (shrub.Billboard is not { } billboard)
        {
            return null;
        }

        var meshBounds = ComputeMeshBounds(mesh);
        var preview = ResolveBillboardPreview(billboard, meshBounds, options.PositionScale);
        var previewWidth = preview.Width;
        var previewHeight = preview.Height;
        if (previewWidth <= 0 || previewHeight <= 0)
        {
            return null;
        }

        var halfWidth = previewWidth * 0.5f;
        var halfHeight = previewHeight * 0.5f;
        var positions = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, -halfHeight, 0),
            new Vector3(halfWidth, halfHeight, 0),
            new Vector3(-halfWidth, halfHeight, 0)
        };
        var normals = Enumerable.Repeat(Vector3.UnitZ, 4).ToArray();
        var texCoords = new[]
        {
            new Vector2(0, 1),
            new Vector2(1, 1),
            new Vector2(1, 0),
            new Vector2(0, 0)
        };
        uint[] indices = [0, 1, 2, 0, 2, 3];

        var primitive = new Dictionary<string, object>
        {
            ["attributes"] = new Dictionary<string, int>
            {
                ["POSITION"] = gltfBufferWriter.WriteVector3Accessor(positions, includeMinMax: true),
                ["NORMAL"] = gltfBufferWriter.WriteVector3Accessor(normals),
                ["TEXCOORD_0"] = gltfBufferWriter.WriteVector2Accessor(texCoords)
            },
            ["indices"] = gltfBufferWriter.WriteUInt32IndexAccessor(indices),
            ["mode"] = 4,
            ["material"] = materialIndex,
            ["extras"] = BuildBillboardExtras(billboard, options, preview, textureIndex)
        };

        var extras = BuildBillboardExtras(billboard, options, preview, textureIndex);
        return new ShrubBillboardMeshBuild(
            new
            {
                name = "shrub_billboard",
                primitives = new[] { primitive },
                extras
            },
            new
            {
                name = "shrub_billboard",
                mesh = meshIndex,
                translation = new[] { 0f, preview.CenterY, 0f },
                extras
            });
    }

    private static Dictionary<string, object> BuildBillboardMaterial(ShrubGltfExportOptions options, int textureIndex)
    {
        var material = new Dictionary<string, object>
        {
            ["name"] = "shrub_billboard",
            ["doubleSided"] = true,
            ["extensions"] = new Dictionary<string, object>
            {
                [UnlitExtensionName] = new Dictionary<string, object>()
            }
        };
        var pbr = new Dictionary<string, object>
        {
            ["metallicFactor"] = 0f,
            ["roughnessFactor"] = 1f
        };

        if (!string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri))
        {
            pbr["baseColorTexture"] = new { index = textureIndex };
        }
        else
        {
            pbr["baseColorFactor"] = new[] { 0.85f, 0.95f, 0.72f, 0.65f };
        }

        var alpha = options.ExternalBillboardTextureAlpha ?? TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        if (shrubAlpha.GltfAlphaMode is { } alphaMode)
        {
            material["alphaMode"] = alphaMode;
            if (shrubAlpha.AlphaMode == TextureAlphaMode.Mask)
            {
                material["alphaCutoff"] = 0.5f;
            }
        }

        material["pbrMetallicRoughness"] = pbr;
        var extras = BuildBillboardMaterialExtras(options);
        if (extras is not null)
        {
            material["extras"] = extras;
        }

        return material;
    }

    private static object? BuildBillboardMaterialExtras(ShrubGltfExportOptions options)
    {
        if (options.MetadataMode == GltfExportMetadataMode.None)
        {
            return null;
        }

        var alpha = options.ExternalBillboardTextureAlpha ?? TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        var size = options.ExternalBillboardTextureSize ?? new TextureSize(0, 0);

        if (options.MetadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                ShrubBillboardMaterial = true,
                ShrubTextureHasAlpha = shrubAlpha.HasAlpha,
                ShrubTextureAlphaMode = shrubAlpha.AlphaMode.ToString(),
                ShrubTextureAlphaUsage = shrubAlpha.HasAlpha ? "Opacity" : "Opaque",
                ShrubTextureGltfAlphaMode = shrubAlpha.HasAlpha ? shrubAlpha.GltfAlphaMode : null,
                ShrubTextureFullOpacityAlpha = ShrubTextureAlpha.FullOpacityAlpha
            };
        }

        return new
        {
            ShrubBillboardMaterial = true,
            ShrubTextureId = (int?)null,
            ShrubTextureUri = options.ExternalBillboardTextureUri,
            TextureWidth = size.Width,
            TextureHeight = size.Height,
            ShrubTextureHasAlpha = shrubAlpha.HasAlpha,
            ShrubTextureAlphaMode = shrubAlpha.AlphaMode.ToString(),
            ShrubTextureAlphaUsage = shrubAlpha.HasAlpha ? "Opacity" : "Opaque",
            ShrubTextureGltfAlphaMode = shrubAlpha.HasAlpha ? shrubAlpha.GltfAlphaMode : null,
            ShrubTextureMinAlpha = alpha.MinAlpha,
            ShrubTextureMaxAlpha = alpha.MaxAlpha,
            ShrubTextureUsesBinaryAlpha = shrubAlpha.UsesBinaryAlpha,
            ShrubTextureFullOpacityAlpha = ShrubTextureAlpha.FullOpacityAlpha,
            shrubAlpha.HasAlpha,
            AlphaMode = shrubAlpha.AlphaMode.ToString(),
            shrubAlpha.GltfAlphaMode,
            alpha.MinAlpha,
            alpha.MaxAlpha,
            ShrubTextureAlpha.FullOpacityAlpha,
            shrubAlpha.UsesBinaryAlpha
        };
    }

    private static object BuildBillboardExtras(
        ShrubBillboard billboard,
        ShrubGltfExportOptions options,
        ShrubBillboardPreview preview,
        int textureIndex)
    {
        if (options.MetadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                ShrubBillboard = true
            };
        }

        return new
        {
            ShrubBillboard = true,
            Texture = options.ExternalBillboardTextureUri,
            TextureIndex = string.IsNullOrWhiteSpace(options.ExternalBillboardTextureUri) ? null : (int?)textureIndex,
            billboard.FadeDistance,
            billboard.Width,
            billboard.Height,
            billboard.ZOffset,
            PreviewWidth = preview.Width,
            PreviewHeight = preview.Height,
            PreviewCenterY = preview.CenterY,
            preview.SizingMode
        };
    }

    private static ShrubBillboardPreview ResolveBillboardPreview(
        ShrubBillboard billboard,
        ShrubMeshBounds meshBounds,
        float positionScale)
    {
        var sourceHasUsableDimensions = billboard.Width > 2f && billboard.Height > 2f;
        var width = sourceHasUsableDimensions
            ? billboard.Width * positionScale
            : FallbackBillboardWidth(meshBounds);
        var height = sourceHasUsableDimensions
            ? billboard.Height * positionScale
            : FallbackBillboardHeight(meshBounds, width);

        return new ShrubBillboardPreview(
            width,
            height,
            meshBounds.Center.Y,
            sourceHasUsableDimensions ? "SourceBillboard" : "MeshBoundsFallback");
    }

    private static float FallbackBillboardWidth(ShrubMeshBounds meshBounds)
    {
        var size = meshBounds.Size;
        var horizontalWidth = MathF.Max(size.X, size.Z);
        return horizontalWidth > 0.0001f ? horizontalWidth : MathF.Max(size.Y, 1f);
    }

    private static float FallbackBillboardHeight(ShrubMeshBounds meshBounds, float fallbackWidth)
    {
        var height = meshBounds.Size.Y;
        return MathF.Max(MathF.Max(height, fallbackWidth), 1f);
    }

    private static ShrubMeshBounds ComputeMeshBounds(ShrubMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var position in mesh.Groups.SelectMany(group => group.Positions))
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        if (min.X == float.MaxValue)
        {
            min = Vector3.Zero;
            max = Vector3.One;
        }

        return new ShrubMeshBounds(min, max);
    }
}

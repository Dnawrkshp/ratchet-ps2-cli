using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public static partial class ShrubGltfExporter
{
    private static ShrubMaterialBuildResult BuildMaterials(
        IReadOnlyList<int> textureIds,
        ShrubGltfExportOptions options)
    {
        var materials = new List<Dictionary<string, object>>();
        var materialIndexByTextureId = new Dictionary<int, int>();
        var gltfTextureSourceIndexByTextureId = new Dictionary<int, int>();
        var exportedTextureIds = textureIds
            .Where(textureId => textureId >= 0 && options.ExternalTextureUris?.ContainsKey(textureId) == true)
            .Distinct()
            .Order()
            .ToArray();

        for (var i = 0; i < exportedTextureIds.Length; i++)
        {
            gltfTextureSourceIndexByTextureId[exportedTextureIds[i]] = i;
        }

        foreach (var textureId in textureIds.Distinct())
        {
            materialIndexByTextureId[textureId] = materials.Count;
            var material = new Dictionary<string, object>
            {
                ["name"] = textureId < 0 ? "shrub_untextured_preview" : $"shrub_tex_{textureId:0000}",
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

            if (gltfTextureSourceIndexByTextureId.TryGetValue(textureId, out var gltfTextureIndex))
            {
                pbr["baseColorTexture"] = new { index = gltfTextureIndex };
                var alpha = options.ExternalTextureAlpha is not null
                    && options.ExternalTextureAlpha.TryGetValue(textureId, out var alphaInfo)
                        ? alphaInfo
                        : TextureAlphaInfo.Opaque;
                var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
                if (shrubAlpha.GltfAlphaMode is { } alphaMode)
                {
                    material["alphaMode"] = alphaMode;
                    if (shrubAlpha.AlphaMode == TextureAlphaMode.Mask)
                    {
                        material["alphaCutoff"] = 0.5f;
                    }
                }
            }
            else
            {
                pbr["baseColorFactor"] = textureId < 0
                    ? new[] { 0.72f, 0.82f, 0.58f, 1f }
                    : new[] { 1f, 1f, 1f, 1f };
            }

            material["pbrMetallicRoughness"] = pbr;
            var extras = BuildMaterialExtras(textureId, options);
            if (extras is not null)
            {
                material["extras"] = extras;
            }

            materials.Add(material);
        }

        return new ShrubMaterialBuildResult(
            materials,
            materialIndexByTextureId,
            exportedTextureIds);
    }

    private static object? BuildMaterialExtras(int textureId, ShrubGltfExportOptions options)
    {
        if (options.MetadataMode == GltfExportMetadataMode.None)
        {
            return null;
        }

        var alpha = options.ExternalTextureAlpha is not null && options.ExternalTextureAlpha.TryGetValue(textureId, out var alphaInfo)
            ? alphaInfo
            : TextureAlphaInfo.Opaque;
        var shrubAlpha = ShrubTextureAlpha.Interpret(alpha);
        var size = options.ExternalTextureSizes is not null && options.ExternalTextureSizes.TryGetValue(textureId, out var resolvedSize)
            ? resolvedSize
            : new TextureSize(0, 0);

        if (options.MetadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                ShrubTextureHasAlpha = shrubAlpha.HasAlpha,
                ShrubTextureAlphaMode = shrubAlpha.AlphaMode.ToString(),
                ShrubTextureAlphaUsage = shrubAlpha.HasAlpha ? "Opacity" : "Opaque",
                ShrubTextureGltfAlphaMode = shrubAlpha.HasAlpha ? shrubAlpha.GltfAlphaMode : null,
                ShrubTextureFullOpacityAlpha = ShrubTextureAlpha.FullOpacityAlpha
            };
        }

        return new
        {
            ShrubTextureId = textureId,
            ShrubTextureUri = options.ExternalTextureUris is not null && options.ExternalTextureUris.TryGetValue(textureId, out var uri)
                ? uri
                : null,
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
}

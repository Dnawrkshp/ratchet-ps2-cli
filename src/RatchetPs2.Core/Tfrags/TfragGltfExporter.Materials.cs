using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private const int GltfLinearFilter = 9729;
    private const int GltfWrapRepeat = 10497;
    private const int GltfWrapClampToEdge = 33071;

    private static TfragMaterialBuildResult BuildMaterials(
        IReadOnlyList<TfragMaterialKey> materialKeys,
        TfragGltfExportOptions options)
    {
        var materials = new List<Dictionary<string, object?>>();
        var materialIndexByKey = new Dictionary<TfragMaterialKey, int>();
        var materialKeysOrdered = materialKeys
            .Distinct()
            .OrderBy(key => key.TextureId)
            .ThenBy(key => key.ClampU)
            .ThenBy(key => key.ClampV)
            .ToArray();
        var exportedTextureIds = materialKeysOrdered
            .Select(key => key.TextureId)
            .Where(textureId => textureId >= 0 && options.ExternalTextureUris?.ContainsKey(textureId) == true)
            .Distinct()
            .OrderBy(textureId => textureId)
            .ToArray();
        var gltfTextureSourceIndexByTextureId = new Dictionary<int, int>();
        for (var i = 0; i < exportedTextureIds.Length; i++)
        {
            gltfTextureSourceIndexByTextureId[exportedTextureIds[i]] = i;
        }

        var samplerIndexByWrapMode = new Dictionary<TfragTextureWrapMode, int>();
        var samplers = new List<object>();
        var images = exportedTextureIds.Select(textureId => new
        {
            name = $"tex_{textureId:0000}",
            uri = options.ExternalTextureUris![textureId]
        }).Cast<object>().ToList();
        var textures = new List<object>();
        var textureIndexByMaterialKey = new Dictionary<TfragMaterialKey, int>();
        foreach (var key in materialKeysOrdered)
        {
            if (!gltfTextureSourceIndexByTextureId.TryGetValue(key.TextureId, out var sourceIndex))
            {
                continue;
            }

            var wrapMode = new TfragTextureWrapMode(key.ClampU, key.ClampV);
            if (!samplerIndexByWrapMode.TryGetValue(wrapMode, out var samplerIndex))
            {
                samplerIndex = samplers.Count;
                samplerIndexByWrapMode.Add(wrapMode, samplerIndex);
                samplers.Add(new
                {
                    magFilter = GltfLinearFilter,
                    minFilter = GltfLinearFilter,
                    wrapS = wrapMode.ClampU ? GltfWrapClampToEdge : GltfWrapRepeat,
                    wrapT = wrapMode.ClampV ? GltfWrapClampToEdge : GltfWrapRepeat
                });
            }

            textureIndexByMaterialKey.Add(key, textures.Count);
            textures.Add(new
            {
                sampler = samplerIndex,
                source = sourceIndex
            });
        }

        foreach (var key in materialKeysOrdered)
        {
            materialIndexByKey[key] = materials.Count;
            var material = new Dictionary<string, object?>
            {
                ["name"] = BuildMaterialName(key),
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

            if (textureIndexByMaterialKey.TryGetValue(key, out var gltfTextureIndex))
            {
                pbr["baseColorTexture"] = new { index = gltfTextureIndex };
                var alpha = options.ExternalTextureAlpha is not null
                    && options.ExternalTextureAlpha.TryGetValue(key.TextureId, out var alphaInfo)
                        ? alphaInfo
                        : TextureAlphaInfo.Opaque;
                var interpretedAlpha = InterpretAlpha(alpha);
                if (interpretedAlpha.GltfAlphaMode is { } alphaMode)
                {
                    material["alphaMode"] = alphaMode;
                    if (interpretedAlpha.AlphaMode == TextureAlphaMode.Mask)
                    {
                        material["alphaCutoff"] = 0.5f;
                    }
                }
            }
            else
            {
                pbr["baseColorFactor"] = key.TextureId < 0
                    ? new[] { 0.62f, 0.68f, 0.58f, 1f }
                    : new[] { 1f, 1f, 1f, 1f };
            }

            material["pbrMetallicRoughness"] = pbr;
            material["extras"] = BuildMaterialExtras(key, options);
            materials.Add(material);
        }

        return new TfragMaterialBuildResult(
            materials,
            materialIndexByKey,
            exportedTextureIds,
            samplers,
            images,
            textures);
    }

    private static string BuildMaterialName(TfragMaterialKey key)
    {
        if (key.TextureId < 0)
        {
            return "tfrag_untextured_preview";
        }

        if (!key.ClampU && !key.ClampV)
        {
            return $"tfrag_tex_{key.TextureId:0000}";
        }

        var wrapSuffix = key switch
        {
            { ClampU: true, ClampV: true } => "clamp_uv",
            { ClampU: true } => "clamp_u",
            _ => "clamp_v"
        };
        return $"tfrag_tex_{key.TextureId:0000}_{wrapSuffix}";
    }

    private static TfragInterpretedAlpha InterpretAlpha(TextureAlphaInfo alpha)
    {
        if (alpha.MinAlpha >= TfragTextureAlpha.FullOpacityAlpha)
        {
            return new TfragInterpretedAlpha(false, TextureAlphaMode.Opaque, null, true);
        }

        var alphaMode = alpha.UsesBinaryAlpha ? TextureAlphaMode.Mask : TextureAlphaMode.Blend;
        var gltfAlphaMode = alphaMode switch
        {
            TextureAlphaMode.Mask => "MASK",
            TextureAlphaMode.Blend => "BLEND",
            _ => null
        };
        return new TfragInterpretedAlpha(true, alphaMode, gltfAlphaMode, alpha.UsesBinaryAlpha);
    }

    private static object BuildMaterialExtras(int textureId, TfragGltfExportOptions options)
    {
        return BuildMaterialExtras(new TfragMaterialKey(textureId, ClampU: false, ClampV: false), options);
    }

    private static object BuildMaterialExtras(TfragMaterialKey key, TfragGltfExportOptions options)
    {
        var alpha = options.ExternalTextureAlpha is not null && options.ExternalTextureAlpha.TryGetValue(key.TextureId, out var alphaInfo)
            ? alphaInfo
            : TextureAlphaInfo.Opaque;
        var interpretedAlpha = InterpretAlpha(alpha);
        var size = options.ExternalTextureSizes is not null && options.ExternalTextureSizes.TryGetValue(key.TextureId, out var resolvedSize)
            ? resolvedSize
            : new TextureSize(0, 0);

        return new
        {
            TfragTextureId = key.TextureId,
            key.ClampU,
            key.ClampV,
            TfragTextureUri = options.ExternalTextureUris is not null && options.ExternalTextureUris.TryGetValue(key.TextureId, out var uri)
                ? uri
                : null,
            TextureWidth = size.Width,
            TextureHeight = size.Height,
            alpha.HasAlpha,
            AlphaMode = alpha.AlphaMode.ToString(),
            alpha.GltfAlphaMode,
            alpha.MinAlpha,
            alpha.MaxAlpha,
            alpha.UsesBinaryAlpha,
            FullOpacityAlpha = TfragTextureAlpha.FullOpacityAlpha,
            TfragTextureHasOpacityAlpha = interpretedAlpha.HasOpacityAlpha,
            TfragTextureEffectiveAlphaMode = interpretedAlpha.AlphaMode.ToString(),
            TfragTextureEffectiveGltfAlphaMode = interpretedAlpha.GltfAlphaMode,
            TfragTextureEffectiveUsesBinaryAlpha = interpretedAlpha.UsesBinaryAlpha
        };
    }
}

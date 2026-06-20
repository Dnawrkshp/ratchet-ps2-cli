using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static MaterialBuildResult BuildMaterials(
        IReadOnlyList<SkyboxPrimitive> primitives,
        IReadOnlyList<SkyboxGltfTextureResource> textureResources,
        IReadOnlyDictionary<byte, SkyboxVertexAlphaInfo> vertexAlphaByTextureId,
        SkyboxColor skyColor,
        bool usesUntexturedGouraudColors,
        GltfExportMetadataMode metadataMode)
    {
        var textureIndexByTextureId = textureResources
            .Select((texture, gltfTextureIndex) => new { TextureId = (byte)texture.Index, GltfTextureIndex = gltfTextureIndex })
            .ToDictionary(texture => texture.TextureId, texture => texture.GltfTextureIndex);
        var alphaByTextureId = textureResources.ToDictionary(texture => (byte)texture.Index, texture => texture.Alpha);
        var materials = new List<Dictionary<string, object>>();
        var materialIndexByKey = new Dictionary<SkyboxMaterialKey, int>();

        foreach (var key in primitives.Select(SkyboxMaterialKey.ForPrimitive).Distinct())
        {
            var textureId = key.TextureId;
            var usesBloomEmission = key.DrawBlendMode == SkyboxDrawBlendMode.Bloom;
            materialIndexByKey[key] = materials.Count;
            var material = new Dictionary<string, object>
            {
                ["name"] = BuildMaterialName(textureId, key.DrawBlendMode),
                ["doubleSided"] = true
            };
            var materialExtensions = new Dictionary<string, object>();
            if (!usesBloomEmission)
            {
                materialExtensions[UnlitExtensionName] = new Dictionary<string, object>();
            }

            var materialAlpha = BuildMaterialAlpha(
                textureId,
                alphaByTextureId,
                vertexAlphaByTextureId,
                skyColor,
                usesUntexturedGouraudColors);
            var pbr = new Dictionary<string, object>
            {
                ["metallicFactor"] = 0f,
                ["roughnessFactor"] = 1f
            };
            if (textureId != UntexturedTextureId && textureIndexByTextureId.TryGetValue(textureId, out var textureIndex))
            {
                pbr["baseColorTexture"] = new { index = textureIndex };
                if (usesBloomEmission)
                {
                    pbr["baseColorFactor"] = new[] { 0f, 0f, 0f, 1f };
                    material["emissiveFactor"] = new[] { 1f, 1f, 1f };
                    material["emissiveTexture"] = new { index = textureIndex };
                    materialExtensions[EmissiveStrengthExtensionName] = new
                    {
                        emissiveStrength = BloomEmissionStrength
                    };
                }

                if (materialAlpha.GltfAlphaMode is { } alphaMode)
                {
                    material["alphaMode"] = alphaMode;
                    if (materialAlpha.AlphaMode == TextureAlphaMode.Mask)
                    {
                        material["alphaCutoff"] = 0.5f;
                    }
                }
            }
            else
            {
                pbr["baseColorFactor"] = usesUntexturedGouraudColors
                    ? new[] { 1f, 1f, 1f, 1f }
                    : skyColor.ToGltfFactor();
                if (usesBloomEmission)
                {
                    material["emissiveFactor"] = new[] { 1f, 1f, 1f };
                    materialExtensions[EmissiveStrengthExtensionName] = new
                    {
                        emissiveStrength = BloomEmissionStrength
                    };
                }
            }

            if (materialExtensions.Count > 0)
            {
                material["extensions"] = materialExtensions;
            }

            if (materialAlpha.GltfAlphaMode is { } materialAlphaMode)
            {
                material["alphaMode"] = materialAlphaMode;
                if (materialAlpha.AlphaMode == TextureAlphaMode.Mask)
                {
                    material["alphaCutoff"] = 0.5f;
                }
            }

            material["pbrMetallicRoughness"] = pbr;
            var extras = BuildMaterialExtras(
                textureId,
                alphaByTextureId,
                vertexAlphaByTextureId,
                materialAlpha,
                skyColor,
                usesUntexturedGouraudColors,
                key.DrawBlendMode,
                metadataMode);
            if (extras is not null)
            {
                material["extras"] = extras;
            }

            materials.Add(material);
        }

        return new MaterialBuildResult(
            materials,
            materialIndexByKey,
            materialIndexByKey.Keys.Any(key => key.DrawBlendMode == SkyboxDrawBlendMode.Bloom));
    }

    private static object? BuildMaterialExtras(
        byte textureId,
        IReadOnlyDictionary<byte, TextureAlphaInfo> alphaByTextureId,
        IReadOnlyDictionary<byte, SkyboxVertexAlphaInfo> vertexAlphaByTextureId,
        SkyboxMaterialAlphaInfo materialAlpha,
        SkyboxColor skyColor,
        bool usesUntexturedGouraudColors,
        string drawBlendMode,
        GltfExportMetadataMode metadataMode)
    {
        if (metadataMode == GltfExportMetadataMode.None)
        {
            return null;
        }

        var alpha = textureId != UntexturedTextureId && alphaByTextureId.TryGetValue(textureId, out var resolvedAlpha)
            ? resolvedAlpha
            : TextureAlphaInfo.Opaque;
        var vertexAlpha = vertexAlphaByTextureId.TryGetValue(textureId, out var resolvedVertexAlpha)
            ? resolvedVertexAlpha
            : SkyboxVertexAlphaInfo.Opaque;
        if (metadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                SkyboxTextureAlphaMode = alpha.AlphaMode.ToString(),
                SkyboxTextureGltfAlphaMode = alpha.GltfAlphaMode,
                SkyboxTextureMaxAlpha = alpha.MaxAlpha,
                SkyboxBaseColorAlpha = textureId == UntexturedTextureId && !usesUntexturedGouraudColors
                    ? skyColor.A
                    : byte.MaxValue,
                SkyboxUsesVertexColor0 = true,
                SkyboxVertexAlphaMin = vertexAlpha.MinAlpha,
                SkyboxVertexAlphaMax = vertexAlpha.MaxAlpha,
                SkyboxMaterialAlphaMode = materialAlpha.AlphaMode.ToString(),
                SkyboxTextureAlphaUsage = materialAlpha.HasAlpha ? "Opacity" : "Opaque",
                SkyboxDrawBlendMode = drawBlendMode
            };
        }

        return new
        {
            SkyboxTextureId = textureId,
            SkyboxTextureName = TextureName(textureId),
            SkyboxTextureHasAlpha = textureId != UntexturedTextureId && alpha.HasAlpha,
            SkyboxTextureAlphaMode = alpha.AlphaMode.ToString(),
            SkyboxTextureGltfAlphaMode = alpha.GltfAlphaMode,
            SkyboxTextureMinAlpha = alpha.MinAlpha,
            SkyboxTextureMaxAlpha = alpha.MaxAlpha,
            SkyboxTextureUsesBinaryAlpha = alpha.UsesBinaryAlpha,
            SkyboxBaseColorAlpha = textureId == UntexturedTextureId && !usesUntexturedGouraudColors
                ? skyColor.A
                : byte.MaxValue,
            SkyboxUsesVertexColor0 = true,
            SkyboxUsesUntexturedGouraudColor = textureId == UntexturedTextureId && usesUntexturedGouraudColors,
            SkyboxVertexAlphaMin = vertexAlpha.MinAlpha,
            SkyboxVertexAlphaMax = vertexAlpha.MaxAlpha,
            SkyboxVertexAlphaUsesBinaryAlpha = vertexAlpha.UsesBinaryAlpha,
            SkyboxMaterialHasAlpha = materialAlpha.HasAlpha,
            SkyboxMaterialAlphaMode = materialAlpha.AlphaMode.ToString(),
            SkyboxMaterialGltfAlphaMode = materialAlpha.GltfAlphaMode,
            SkyboxTextureAlphaUsage = materialAlpha.HasAlpha ? "Opacity" : "Opaque",
            SkyboxBlendMode = materialAlpha.AlphaMode == TextureAlphaMode.Blend ? "Blend" : materialAlpha.AlphaMode == TextureAlphaMode.Mask ? "Mask" : "Opaque",
            SkyboxDrawBlendMode = drawBlendMode,
            SkyboxUsesBloomEmission = drawBlendMode == SkyboxDrawBlendMode.Bloom,
            SkyboxBloomEmissionStrength = drawBlendMode == SkyboxDrawBlendMode.Bloom ? BloomEmissionStrength : 0f
        };
    }

    private static SkyboxMaterialAlphaInfo BuildMaterialAlpha(
        byte textureId,
        IReadOnlyDictionary<byte, TextureAlphaInfo> alphaByTextureId,
        IReadOnlyDictionary<byte, SkyboxVertexAlphaInfo> vertexAlphaByTextureId,
        SkyboxColor skyColor,
        bool usesUntexturedGouraudColors)
    {
        var textureAlpha = textureId != UntexturedTextureId && alphaByTextureId.TryGetValue(textureId, out var resolvedTextureAlpha)
            ? resolvedTextureAlpha
            : TextureAlphaInfo.Opaque;
        var vertexAlpha = vertexAlphaByTextureId.TryGetValue(textureId, out var resolvedVertexAlpha)
            ? resolvedVertexAlpha
            : SkyboxVertexAlphaInfo.Opaque;
        var baseColorHasAlpha = textureId == UntexturedTextureId
            && !usesUntexturedGouraudColors
            && skyColor.A < byte.MaxValue;
        var usesBinaryAlpha = textureAlpha.UsesBinaryAlpha && vertexAlpha.UsesBinaryAlpha && !baseColorHasAlpha;

        return new SkyboxMaterialAlphaInfo(
            textureAlpha.HasAlpha || vertexAlpha.HasAlpha || baseColorHasAlpha,
            usesBinaryAlpha);
    }

    private static string[] BuildExtensionsUsed(bool usesBloomEmission)
    {
        return usesBloomEmission
            ? [UnlitExtensionName, EmissiveStrengthExtensionName]
            : [UnlitExtensionName];
    }

    private static string BuildMaterialName(byte textureId, string drawBlendMode)
    {
        var name = textureId == UntexturedTextureId ? "sky_untextured_preview" : $"sky_tex_{textureId:0000}";
        return drawBlendMode == SkyboxDrawBlendMode.Bloom ? $"{name}_bloom" : name;
    }
}

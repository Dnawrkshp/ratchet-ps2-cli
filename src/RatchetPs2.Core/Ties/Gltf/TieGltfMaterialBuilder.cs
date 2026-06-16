using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

internal sealed class TieGltfMaterialBuilder
{
    public const string EmissiveStrengthExtensionName = "KHR_materials_emissive_strength";

    private const int GltfWrapRepeat = 10497;
    private const int GltfWrapClampToEdge = 33071;
    private const int GltfMinFilterLinear = 9729;
    private const int GltfMagFilterLinear = 9729;

    private readonly IReadOnlyList<TieShader> _shaders;
    private readonly IReadOnlyDictionary<int, string>? _textureUris;
    private readonly IReadOnlyDictionary<int, TextureAlphaInfo>? _textureAlpha;
    private readonly TieGameProfile _profile;
    private readonly Dictionary<MaterialVariantKey, int> _materialIndexByKey = [];
    private readonly Dictionary<int, int> _textureIndexByShaderIndex = [];
    private readonly Dictionary<TextureWrapMode, int> _samplerIndexByWrapMode = [];

    public TieGltfMaterialBuilder(
        IReadOnlyList<TieShader> shaders,
        IReadOnlyDictionary<int, string>? textureUris,
        IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha,
        TieGameProfile profile)
    {
        _shaders = shaders ?? throw new ArgumentNullException(nameof(shaders));
        _textureUris = textureUris;
        _textureAlpha = textureAlpha;
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        Materials.Add(BuildUntexturedPreviewMaterial());
        Diagnostics.Add(new TieGltfMaterialDiagnostic(
            0,
            "tie_untextured_preview",
            ShaderIndex: null,
            TextureIndex: null,
            TextureHasAlpha: false,
            TextureAlphaMode: TextureAlphaMode.Opaque.ToString(),
            TextureAlphaUsage: TieMaterialAlphaUsage.Opaque.ToString(),
            TextureGltfAlphaMode: null,
            TextureMinAlpha: 255,
            TextureMaxAlpha: 255,
            TextureUsesBinaryAlpha: true,
            TieMultipassType: null,
            HeaderModeBits: null,
            TieUsesGlowEmission: false,
            TieGlowEmissionStrength: 0f));
    }

    public List<object> Images { get; } = [];

    public List<object> Textures { get; } = [];

    public List<object> Samplers { get; } = [];

    public List<object> Materials { get; } = [];

    public List<TieGltfMaterialDiagnostic> Diagnostics { get; } = [];

    public int GetMaterialIndex(
        int shaderIndex,
        int multipassType,
        short headerModeBits,
        TieGltfGlowEmissionMaterial? glowEmission)
    {
        string? uri = null;
        var hasTexture = _textureUris is not null
            && shaderIndex >= 0
            && _textureUris.TryGetValue(shaderIndex, out uri)
            && !string.IsNullOrWhiteSpace(uri);
        if (!hasTexture && glowEmission is null)
        {
            return 0;
        }

        var rgba = glowEmission?.Rgba ?? default;
        var alphaInfo = hasTexture
            && _textureAlpha is not null
            && _textureAlpha.TryGetValue(shaderIndex, out var resolvedAlphaInfo)
                ? resolvedAlphaInfo
                : TextureAlphaInfo.Opaque;
        var alphaUsage = ResolveMaterialAlphaUsage(alphaInfo, multipassType, headerModeBits, _profile);
        var key = new MaterialVariantKey(
            shaderIndex,
            glowEmission.HasValue,
            hasTexture ? alphaInfo.AlphaMode : TextureAlphaMode.Opaque,
            alphaUsage,
            rgba.R,
            rgba.G,
            rgba.B,
            rgba.A);
        if (_materialIndexByKey.TryGetValue(key, out var materialIndex))
        {
            return materialIndex;
        }

        int? textureIndex = null;
        if (hasTexture)
        {
            textureIndex = GetTextureIndex(shaderIndex, uri!);
        }

        materialIndex = Materials.Count;
        var materialName = textureIndex.HasValue
            ? BuildMaterialName(shaderIndex, alphaUsage)
            : "tie_untextured_preview";
        Materials.Add(BuildTieMaterial(
            materialName,
            textureIndex,
            alphaInfo,
            alphaUsage,
            multipassType,
            headerModeBits,
            _profile,
            glowEmission));
        Diagnostics.Add(BuildMaterialDiagnostic(
            materialIndex,
            materialName,
            hasTexture ? shaderIndex : null,
            textureIndex,
            textureIndex.HasValue ? alphaInfo : TextureAlphaInfo.Opaque,
            textureIndex.HasValue ? alphaUsage : TieMaterialAlphaUsage.Opaque,
            textureIndex.HasValue ? multipassType : null,
            textureIndex.HasValue ? headerModeBits : null,
            glowEmission));

        _materialIndexByKey.Add(key, materialIndex);
        return materialIndex;
    }

    private static TieMaterialAlphaUsage ResolveMaterialAlphaUsage(
        TextureAlphaInfo textureAlpha,
        int multipassType,
        short headerModeBits,
        TieGameProfile profile)
    {
        if (!textureAlpha.HasAlpha)
        {
            return TieMaterialAlphaUsage.Opaque;
        }

        if (multipassType == profile.ReflectiveMaskMultipassType
            && (((ushort)headerModeBits & profile.ReflectiveMaskModeBit) != 0))
        {
            return TieMaterialAlphaUsage.ReflectiveMask;
        }

        if (textureAlpha.MinAlpha >= profile.FullOpacityAlpha)
        {
            return TieMaterialAlphaUsage.Opaque;
        }

        return TieMaterialAlphaUsage.Opacity;
    }

    private static object BuildUntexturedPreviewMaterial()
    {
        return new
        {
            name = "tie_untextured_preview",
            doubleSided = true,
            pbrMetallicRoughness = new
            {
                baseColorFactor = new[] { 0.72f, 0.72f, 0.68f, 1f },
                metallicFactor = 0f,
                roughnessFactor = 0.85f
            },
            extras = new
            {
                TieUsesVertexColor0 = false,
            }
        };
    }

    private static string BuildMaterialName(int shaderIndex, TieMaterialAlphaUsage alphaUsage)
    {
        var name = $"tex_{shaderIndex:0000}";
        return alphaUsage == TieMaterialAlphaUsage.ReflectiveMask
            ? $"{name}_reflective_mask"
            : name;
    }

    private int GetTextureIndex(int shaderIndex, string uri)
    {
        if (_textureIndexByShaderIndex.TryGetValue(shaderIndex, out var textureIndex))
        {
            return textureIndex;
        }

        var imageIndex = Images.Count;
        Images.Add(new
        {
            name = $"tex_{shaderIndex:0000}",
            uri
        });

        var samplerIndex = GetSamplerIndex(shaderIndex);
        textureIndex = Textures.Count;
        Textures.Add(new
        {
            sampler = samplerIndex,
            source = imageIndex
        });
        _textureIndexByShaderIndex.Add(shaderIndex, textureIndex);
        return textureIndex;
    }

    private static Dictionary<string, object> BuildTieMaterial(
        string name,
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TieMaterialAlphaUsage alphaUsage,
        int multipassType,
        short headerModeBits,
        TieGameProfile profile,
        TieGltfGlowEmissionMaterial? glowEmission)
    {
        var useGlowEmission = glowEmission.HasValue;
        var alphaMode = textureIndex.HasValue ? textureAlpha.AlphaMode : TextureAlphaMode.Opaque;
        var emitsOpacity = textureIndex.HasValue && alphaUsage == TieMaterialAlphaUsage.Opacity;
        var usesReflectiveMask = textureIndex.HasValue && alphaUsage == TieMaterialAlphaUsage.ReflectiveMask;
        var pbr = new Dictionary<string, object>
        {
            ["baseColorFactor"] = textureIndex.HasValue
                ? new[] { 1f, 1f, 1f, 1f }
                : new[] { 0.72f, 0.72f, 0.68f, 1f },
            ["metallicFactor"] = usesReflectiveMask ? profile.ReflectiveMaskMetallicFactor : 0f,
            ["roughnessFactor"] = usesReflectiveMask
                ? profile.ReflectiveMaskRoughnessFactor
                : textureIndex.HasValue ? 1f : 0.85f
        };
        if (textureIndex.HasValue)
        {
            pbr["baseColorTexture"] = new
            {
                index = textureIndex.Value
            };
        }

        var material = new Dictionary<string, object>
        {
            ["name"] = name,
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = pbr
        };
        if (emitsOpacity && textureAlpha.GltfAlphaMode is { } gltfAlphaMode)
        {
            material["alphaMode"] = gltfAlphaMode;
            if (alphaMode == TextureAlphaMode.Mask)
            {
                material["alphaCutoff"] = 0.5f;
            }
        }

        var extras = new Dictionary<string, object>
        {
            ["TieUsesVertexColor0"] = false,
            ["TieUsesGlowEmission"] = useGlowEmission,
            ["TieTextureHasAlpha"] = textureIndex.HasValue && textureAlpha.HasAlpha,
            ["TieTextureAlphaMode"] = alphaMode.ToString(),
            ["TieTextureAlphaUsage"] = alphaUsage.ToString(),
            ["TieTextureGltfAlphaMode"] = emitsOpacity && textureAlpha.GltfAlphaMode is { } tieGltfAlphaMode
                ? tieGltfAlphaMode
                : null!,
            ["TieTextureMinAlpha"] = textureIndex.HasValue ? textureAlpha.MinAlpha : 255,
            ["TieTextureMaxAlpha"] = textureIndex.HasValue ? textureAlpha.MaxAlpha : 255,
            ["TieTextureUsesBinaryAlpha"] = !textureIndex.HasValue || textureAlpha.UsesBinaryAlpha,
            ["TieTextureFullOpacityAlpha"] = profile.FullOpacityAlpha,
            ["TieMultipassType"] = multipassType,
            ["HeaderModeBits"] = FormatModeBits(headerModeBits)
        };
        if (glowEmission is { } resolvedGlowEmission)
        {
            var rgba = resolvedGlowEmission.Rgba;
            material["emissiveFactor"] = ToGlowEmissionFactor(rgba);
            if (textureIndex.HasValue)
            {
                material["emissiveTexture"] = new
                {
                    index = textureIndex.Value
                };
            }
            material["extensions"] = new Dictionary<string, object>
            {
                [EmissiveStrengthExtensionName] = new
                {
                    emissiveStrength = resolvedGlowEmission.Strength
                }
            };
            extras["TieGlowRgba"] = rgba.ToRgbaHex();
            extras["TieGlowEmissionStrength"] = resolvedGlowEmission.Strength;
            extras["TieGlowPreviewMode"] = textureIndex.HasValue
                ? "TextureModulatedEmission"
                : "UniformEmission";
        }

        material["extras"] = extras;
        return material;
    }

    private static TieGltfMaterialDiagnostic BuildMaterialDiagnostic(
        int materialIndex,
        string name,
        int? shaderIndex,
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TieMaterialAlphaUsage alphaUsage,
        int? multipassType,
        short? headerModeBits,
        TieGltfGlowEmissionMaterial? glowEmission)
    {
        return new TieGltfMaterialDiagnostic(
            materialIndex,
            name,
            shaderIndex,
            textureIndex,
            textureAlpha.HasAlpha,
            textureAlpha.AlphaMode.ToString(),
            alphaUsage.ToString(),
            alphaUsage == TieMaterialAlphaUsage.Opacity ? textureAlpha.GltfAlphaMode : null,
            textureAlpha.MinAlpha,
            textureAlpha.MaxAlpha,
            textureAlpha.UsesBinaryAlpha,
            multipassType,
            headerModeBits.HasValue ? FormatModeBits(headerModeBits.Value) : null,
            glowEmission.HasValue,
            glowEmission?.Strength ?? 0f);
    }

    private int GetSamplerIndex(int shaderIndex)
    {
        var shader = shaderIndex >= 0 && shaderIndex < _shaders.Count ? _shaders[shaderIndex] : null;
        var wrapMode = new TextureWrapMode(shader?.ClampU == true, shader?.ClampV == true);
        if (_samplerIndexByWrapMode.TryGetValue(wrapMode, out var samplerIndex))
        {
            return samplerIndex;
        }

        samplerIndex = Samplers.Count;
        Samplers.Add(new
        {
            wrapS = wrapMode.ClampU ? GltfWrapClampToEdge : GltfWrapRepeat,
            wrapT = wrapMode.ClampV ? GltfWrapClampToEdge : GltfWrapRepeat,
            minFilter = GltfMinFilterLinear,
            magFilter = GltfMagFilterLinear
        });
        _samplerIndexByWrapMode.Add(wrapMode, samplerIndex);
        return samplerIndex;
    }

    private static float[] ToGlowEmissionFactor(TieRgba32 rgba)
    {
        var max = Math.Max(rgba.R, Math.Max(rgba.G, rgba.B));
        if (max == 0)
        {
            return [0f, 0f, 0f];
        }

        return
        [
            rgba.R / (float)max,
            rgba.G / (float)max,
            rgba.B / (float)max
        ];
    }

    private static string FormatModeBits(short modeBits)
    {
        return $"0x{(ushort)modeBits:X4}";
    }

    private readonly record struct TextureWrapMode(bool ClampU, bool ClampV);

    private readonly record struct MaterialVariantKey(
        int ShaderIndex,
        bool UseGlowEmission,
        TextureAlphaMode TextureAlphaMode,
        TieMaterialAlphaUsage TextureAlphaUsage,
        byte R,
        byte G,
        byte B,
        byte A);
}

internal readonly record struct TieGltfGlowEmissionMaterial(TieRgba32 Rgba, float Strength);

internal sealed record TieGltfMaterialDiagnostic(
    int Index,
    string Name,
    int? ShaderIndex,
    int? TextureIndex,
    bool TextureHasAlpha,
    string TextureAlphaMode,
    string TextureAlphaUsage,
    string? TextureGltfAlphaMode,
    int TextureMinAlpha,
    int TextureMaxAlpha,
    bool TextureUsesBinaryAlpha,
    int? TieMultipassType,
    string? HeaderModeBits,
    bool TieUsesGlowEmission,
    float TieGlowEmissionStrength);

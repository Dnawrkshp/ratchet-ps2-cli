using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

internal sealed class TieGltfMaterialBuilder
{
    public const string EmissiveStrengthExtensionName = "KHR_materials_emissive_strength";

    private const int GltfWrapRepeat = 10497;
    private const int GltfWrapClampToEdge = 33071;
    private const int GltfMinFilterLinear = 9729;
    private const int GltfMagFilterLinear = 9729;
    private const float ReflectiveMaskPreviewTextureRgbScale = 0.2f;
    private const float ReflectiveMaskFocusPower = 1.2f;
    private const float ReflectiveMaskEnvironmentStrength = 2.2f;
    private const float ReflectiveMaskMaxBlend = 0.82f;

    private readonly IReadOnlyList<TieShader> _shaders;
    private readonly IReadOnlyDictionary<int, string>? _textureUris;
    private readonly IReadOnlyDictionary<int, TextureAlphaInfo>? _textureAlpha;
    private readonly TieGameProfile _profile;
    private readonly GltfExportMetadataMode _metadataMode;
    private readonly int? _reflectiveEnvironmentShaderIndex;
    private readonly Dictionary<MaterialVariantKey, int> _materialIndexByKey = [];
    private readonly Dictionary<int, int> _textureIndexByShaderIndex = [];
    private readonly Dictionary<TextureWrapMode, int> _samplerIndexByWrapMode = [];

    public TieGltfMaterialBuilder(
        IReadOnlyList<TieShader> shaders,
        IReadOnlyDictionary<int, string>? textureUris,
        IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha,
        TieGameProfile profile,
        GltfExportMetadataMode metadataMode)
    {
        _shaders = shaders ?? throw new ArgumentNullException(nameof(shaders));
        _textureUris = textureUris;
        _textureAlpha = textureAlpha;
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _metadataMode = metadataMode;
        _reflectiveEnvironmentShaderIndex = ResolveReflectiveEnvironmentShaderIndex(textureUris);

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
            TieMultipassOffset: null,
            TieMultipassType: null,
            TiePassFlags: null,
            TiePassFlagsBits: null,
            TieSecondPassMode: null,
            TieEnvironmentPassBits: null,
            TieTextureMatrixSelector: null,
            TieMultipassUvSize: null,
            TieMultipassUvRole: null,
            TieReflectiveBleedColor: null,
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
        int multipassOffset,
        int passFlags,
        int multipassUvSize,
        TieRgba32? envPassBleedColor,
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
        var alphaUsage = ResolveMaterialAlphaUsage(alphaInfo, passFlags, headerModeBits, _profile);
        var key = new MaterialVariantKey(
            shaderIndex,
            passFlags,
            glowEmission.HasValue,
            hasTexture ? alphaInfo.AlphaMode : TextureAlphaMode.Opaque,
            alphaUsage,
            envPassBleedColor?.R ?? 0,
            envPassBleedColor?.G ?? 0,
            envPassBleedColor?.B ?? 0,
            envPassBleedColor?.A ?? 0,
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

        int? reflectiveEnvironmentTextureIndex = null;
        int? reflectiveEnvironmentShaderIndex = null;
        string? reflectiveEnvironmentUri = null;
        if (textureIndex.HasValue
            && alphaUsage == TieMaterialAlphaUsage.ReflectiveMask
            && TryGetReflectiveEnvironmentTexture(
                out var resolvedEnvironmentShaderIndex,
                out var resolvedEnvironmentUri))
        {
            reflectiveEnvironmentShaderIndex = resolvedEnvironmentShaderIndex;
            reflectiveEnvironmentUri = resolvedEnvironmentUri;
            reflectiveEnvironmentTextureIndex = GetTextureIndex(resolvedEnvironmentShaderIndex, resolvedEnvironmentUri);
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
            multipassOffset,
            passFlags,
            multipassUvSize,
            envPassBleedColor,
            reflectiveEnvironmentTextureIndex,
            reflectiveEnvironmentShaderIndex,
            reflectiveEnvironmentUri,
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
            textureIndex.HasValue ? multipassOffset : null,
            textureIndex.HasValue ? passFlags : null,
            textureIndex.HasValue ? multipassUvSize : null,
            textureIndex.HasValue ? envPassBleedColor : null,
            textureIndex.HasValue ? headerModeBits : null,
            glowEmission));

        _materialIndexByKey.Add(key, materialIndex);
        return materialIndex;
    }

    private static TieMaterialAlphaUsage ResolveMaterialAlphaUsage(
        TextureAlphaInfo textureAlpha,
        int passFlags,
        short headerModeBits,
        TieGameProfile profile)
    {
        if (!textureAlpha.HasAlpha)
        {
            return TieMaterialAlphaUsage.Opaque;
        }

        if (passFlags == profile.ReflectiveMaskPassFlags
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

    private object BuildUntexturedPreviewMaterial()
    {
        var material = new Dictionary<string, object?>
        {
            ["name"] = "tie_untextured_preview",
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = new
            {
                baseColorFactor = new[] { 0.72f, 0.72f, 0.68f, 1f },
                metallicFactor = 0f,
                roughnessFactor = 0.85f
            }
        };

        if (_metadataMode != GltfExportMetadataMode.None)
        {
            material["extras"] = new
            {
                TieUsesVertexColor0 = false,
            };
        }

        return material;
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

    private Dictionary<string, object> BuildTieMaterial(
        string name,
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TieMaterialAlphaUsage alphaUsage,
        int multipassOffset,
        int passFlags,
        int multipassUvSize,
        TieRgba32? envPassBleedColor,
        int? reflectiveEnvironmentTextureIndex,
        int? reflectiveEnvironmentShaderIndex,
        string? reflectiveEnvironmentUri,
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
        if (usesReflectiveMask && reflectiveEnvironmentTextureIndex.HasValue)
        {
            material["emissiveFactor"] = new[] { 0f, 0f, 0f };
            material["emissiveTexture"] = new
            {
                index = reflectiveEnvironmentTextureIndex.Value
            };
        }

        if (emitsOpacity && textureAlpha.GltfAlphaMode is { } gltfAlphaMode)
        {
            material["alphaMode"] = gltfAlphaMode;
            if (alphaMode == TextureAlphaMode.Mask)
            {
                material["alphaCutoff"] = 0.5f;
            }
        }

        var extras = _metadataMode switch
        {
            GltfExportMetadataMode.None => null,
            GltfExportMetadataMode.RuntimeOnly => BuildRuntimeMaterialExtras(
                textureIndex,
                textureAlpha,
                alphaMode,
                alphaUsage,
                emitsOpacity,
                profile,
                multipassOffset,
                passFlags,
                multipassUvSize,
                useGlowEmission),
            _ => BuildFullMaterialExtras(
                textureIndex,
                textureAlpha,
                alphaMode,
                alphaUsage,
                emitsOpacity,
                profile,
                multipassOffset,
                passFlags,
                multipassUvSize,
                headerModeBits,
                useGlowEmission)
        };
        if (usesReflectiveMask && extras is not null)
        {
            AddReflectiveMaskExtras(
                extras,
                envPassBleedColor,
                reflectiveEnvironmentTextureIndex,
                reflectiveEnvironmentShaderIndex,
                reflectiveEnvironmentUri,
                _metadataMode);
        }

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
            if (extras is not null)
            {
                extras["TieGlowRgba"] = rgba.ToRgbaHex();
                extras["TieGlowEmissionStrength"] = resolvedGlowEmission.Strength;
                if (_metadataMode == GltfExportMetadataMode.Full)
                {
                    extras["TieGlowPreviewMode"] = textureIndex.HasValue
                        ? "TextureModulatedEmission"
                        : "UniformEmission";
                }
            }
        }

        if (extras is not null)
        {
            material["extras"] = extras;
        }

        return material;
    }

    private static Dictionary<string, object> BuildRuntimeMaterialExtras(
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TextureAlphaMode alphaMode,
        TieMaterialAlphaUsage alphaUsage,
        bool emitsOpacity,
        TieGameProfile profile,
        int multipassOffset,
        int passFlags,
        int multipassUvSize,
        bool useGlowEmission)
    {
        return new Dictionary<string, object>
        {
            ["TieUsesGlowEmission"] = useGlowEmission,
            ["TieTextureHasAlpha"] = textureIndex.HasValue && textureAlpha.HasAlpha,
            ["TieTextureAlphaMode"] = alphaMode.ToString(),
            ["TieTextureAlphaUsage"] = alphaUsage.ToString(),
            ["TieTextureGltfAlphaMode"] = emitsOpacity && textureAlpha.GltfAlphaMode is { } tieGltfAlphaMode
                ? tieGltfAlphaMode
                : null!,
            ["TieTextureFullOpacityAlpha"] = profile.FullOpacityAlpha,
            ["TieMultipassOffset"] = multipassOffset,
            ["TieMultipassType"] = passFlags,
            ["TiePassFlags"] = passFlags,
            ["TieSecondPassMode"] = TiePassFlags.ResolveSecondPassMode(passFlags),
            ["TieEnvironmentPassBits"] = TiePassFlags.EnvironmentPassBits(passFlags),
            ["TieMultipassUvSize"] = multipassUvSize,
            ["TieMultipassUvRole"] = TiePassFlags.ResolveMultipassUvRole(passFlags, multipassUvSize)
        };
    }

    private static Dictionary<string, object> BuildFullMaterialExtras(
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TextureAlphaMode alphaMode,
        TieMaterialAlphaUsage alphaUsage,
        bool emitsOpacity,
        TieGameProfile profile,
        int multipassOffset,
        int passFlags,
        int multipassUvSize,
        short headerModeBits,
        bool useGlowEmission)
    {
        var extras = BuildRuntimeMaterialExtras(
            textureIndex,
            textureAlpha,
            alphaMode,
            alphaUsage,
            emitsOpacity,
            profile,
            multipassOffset,
            passFlags,
            multipassUvSize,
            useGlowEmission);
        extras["TieUsesVertexColor0"] = false;
        extras["TieTextureMinAlpha"] = textureIndex.HasValue ? textureAlpha.MinAlpha : 255;
        extras["TieTextureMaxAlpha"] = textureIndex.HasValue ? textureAlpha.MaxAlpha : 255;
        extras["TieTextureUsesBinaryAlpha"] = !textureIndex.HasValue || textureAlpha.UsesBinaryAlpha;
        extras["TiePassFlagsBits"] = TiePassFlags.FormatByteBits(passFlags);
        extras["TieTextureMatrixEnabled"] = TiePassFlags.UsesTextureMatrix(passFlags);
        extras["TieTextureMatrixSelector"] = TiePassFlags.TextureMatrixSelector(passFlags);
        extras["TieMultipassTypeBits"] = TiePassFlags.FormatByteBits(passFlags);
        extras["HeaderModeBits"] = FormatModeBits(headerModeBits);
        return extras;
    }

    private static void AddReflectiveMaskExtras(
        Dictionary<string, object> extras,
        TieRgba32? envPassBleedColor,
        int? reflectiveEnvironmentTextureIndex,
        int? reflectiveEnvironmentShaderIndex,
        string? reflectiveEnvironmentUri,
        GltfExportMetadataMode metadataMode)
    {
        extras["TieMaterialRole"] = "ReflectiveOverlay";
        extras["TieTextureRgbUsage"] = "ReflectivePreview";
        extras["TieReflectiveMaskChannel"] = "A";
        extras["TieReflectiveTintSource"] = "DirectionalLightSelector";
        extras["TieReflectiveEnvironmentSource"] = reflectiveEnvironmentTextureIndex.HasValue
            ? "TieTexture"
            : "LastSkyboxShell";
        if (reflectiveEnvironmentTextureIndex.HasValue)
        {
            extras["TieReflectiveEnvironmentTextureRole"] = "LastTieTexture";
            extras["TieReflectiveEnvironmentGltfTextureIndex"] = reflectiveEnvironmentTextureIndex.Value;
            extras["TieReflectiveEnvironmentShaderIndex"] = reflectiveEnvironmentShaderIndex ?? -1;
            extras["TieReflectiveEnvironmentTextureUri"] = reflectiveEnvironmentUri ?? string.Empty;
        }

        extras["TieReflectiveBlendMode"] = "EnvironmentOverlay";
        extras["TieReflectivePreviewBaseColorFactor"] = new[] { 0.035f, 0.045f, 0.06f };
        extras["TieReflectivePreviewTextureRgbScale"] = ReflectiveMaskPreviewTextureRgbScale;
        extras["TieReflectiveMaskFocusPower"] = ReflectiveMaskFocusPower;
        extras["TieReflectiveEnvironmentStrength"] = ReflectiveMaskEnvironmentStrength;
        extras["TieReflectiveMaxBlend"] = ReflectiveMaskMaxBlend;
        if (envPassBleedColor is { } rgba)
        {
            extras["TieReflectiveBleedColorFactor"] = ToPs2ColorFactor(rgba);
            extras["TieReflectiveBleedAlpha"] = rgba.A / 128f;
            if (metadataMode == GltfExportMetadataMode.Full)
            {
                extras["TieReflectiveBleedColor"] = rgba.ToRgbaHex();
                extras["TieReflectiveBleedColorSource"] = "PacketMultipassQword1";
            }
        }
    }

    private static TieGltfMaterialDiagnostic BuildMaterialDiagnostic(
        int materialIndex,
        string name,
        int? shaderIndex,
        int? textureIndex,
        TextureAlphaInfo textureAlpha,
        TieMaterialAlphaUsage alphaUsage,
        int? multipassOffset,
        int? passFlags,
        int? multipassUvSize,
        TieRgba32? envPassBleedColor,
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
            multipassOffset,
            passFlags,
            passFlags,
            passFlags.HasValue ? TiePassFlags.FormatByteBits(passFlags.Value) : null,
            passFlags.HasValue ? TiePassFlags.ResolveSecondPassMode(passFlags.Value) : null,
            passFlags.HasValue ? TiePassFlags.EnvironmentPassBits(passFlags.Value) : null,
            passFlags.HasValue ? TiePassFlags.TextureMatrixSelector(passFlags.Value) : null,
            multipassUvSize,
            passFlags.HasValue && multipassUvSize.HasValue
                ? TiePassFlags.ResolveMultipassUvRole(passFlags.Value, multipassUvSize.Value)
                : null,
            envPassBleedColor?.ToRgbaHex(),
            headerModeBits.HasValue ? FormatModeBits(headerModeBits.Value) : null,
            glowEmission.HasValue,
            glowEmission?.Strength ?? 0f);
    }

    private bool TryGetReflectiveEnvironmentTexture(out int shaderIndex, out string uri)
    {
        if (_reflectiveEnvironmentShaderIndex is { } resolvedShaderIndex
            && _textureUris is not null
            && _textureUris.TryGetValue(resolvedShaderIndex, out var resolvedUri)
            && !string.IsNullOrWhiteSpace(resolvedUri))
        {
            shaderIndex = resolvedShaderIndex;
            uri = resolvedUri;
            return true;
        }

        shaderIndex = 0;
        uri = string.Empty;
        return false;
    }

    private static int? ResolveReflectiveEnvironmentShaderIndex(IReadOnlyDictionary<int, string>? textureUris)
    {
        if (textureUris is null || textureUris.Count == 0)
        {
            return null;
        }

        return textureUris
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => (int?)pair.Key)
            .Max();
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

    private static float[] ToPs2ColorFactor(TieRgba32 rgba)
    {
        const float neutral = 128f;
        return
        [
            rgba.R / neutral,
            rgba.G / neutral,
            rgba.B / neutral
        ];
    }

    private static string FormatModeBits(short modeBits)
    {
        return $"0x{(ushort)modeBits:X4}";
    }

    private readonly record struct TextureWrapMode(bool ClampU, bool ClampV);

    private readonly record struct MaterialVariantKey(
        int ShaderIndex,
        int PassFlags,
        bool UseGlowEmission,
        TextureAlphaMode TextureAlphaMode,
        TieMaterialAlphaUsage TextureAlphaUsage,
        byte EnvPassBleedR,
        byte EnvPassBleedG,
        byte EnvPassBleedB,
        byte EnvPassBleedA,
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
    int? TieMultipassOffset,
    int? TieMultipassType,
    int? TiePassFlags,
    string? TiePassFlagsBits,
    string? TieSecondPassMode,
    int? TieEnvironmentPassBits,
    int? TieTextureMatrixSelector,
    int? TieMultipassUvSize,
    string? TieMultipassUvRole,
    string? TieReflectiveBleedColor,
    string? HeaderModeBits,
    bool TieUsesGlowEmission,
    float TieGlowEmissionStrength);

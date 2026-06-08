using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Shrubs;

public readonly record struct ShrubTextureAlphaInfo(
    bool HasAlpha,
    TextureAlphaMode AlphaMode,
    string? GltfAlphaMode,
    bool UsesBinaryAlpha);

public static class ShrubTextureAlpha
{
    public const byte FullOpacityAlpha = 128;

    public static ShrubTextureAlphaInfo Interpret(TextureAlphaInfo alpha)
    {
        var hasOpacityAlpha = alpha.MinAlpha < FullOpacityAlpha;
        if (!hasOpacityAlpha)
        {
            return new ShrubTextureAlphaInfo(false, TextureAlphaMode.Opaque, null, true);
        }

        var alphaMode = alpha.UsesBinaryAlpha ? TextureAlphaMode.Mask : TextureAlphaMode.Blend;
        var gltfAlphaMode = alphaMode switch
        {
            TextureAlphaMode.Mask => "MASK",
            TextureAlphaMode.Blend => "BLEND",
            _ => null
        };

        return new ShrubTextureAlphaInfo(
            true,
            alphaMode,
            gltfAlphaMode,
            alpha.UsesBinaryAlpha);
    }
}

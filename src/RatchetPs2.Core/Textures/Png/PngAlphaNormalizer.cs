using RatchetPs2.Core.Textures;

namespace RatchetPs2.Core.Textures.Png;

public static class PngAlphaNormalizer
{
    public static TextureMetadata WriteWithPs2AlphaNormalized(
        Stream source,
        Stream destination,
        byte fullOpacityAlpha)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
        {
            throw new ArgumentException("The provided destination stream must be writable.", nameof(destination));
        }

        if (fullOpacityAlpha == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fullOpacityAlpha));
        }

        var image = PngTextureMetadataReader.ReadRgba32(source);
        var alpha = NormalizePs2Alpha(image, fullOpacityAlpha);
        TextureConverter.WritePng(destination, image);
        return new TextureMetadata(new TextureSize(image.Width, image.Height), alpha);
    }

    public static TextureAlphaInfo NormalizePs2Alpha(Rgba32Image image, byte fullOpacityAlpha)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (fullOpacityAlpha == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fullOpacityAlpha));
        }

        byte minAlpha = 255;
        byte maxAlpha = 0;
        var usesBinaryAlpha = true;
        var pixels = image.PixelData;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            var normalizedAlpha = NormalizePs2Alpha(pixels[i], fullOpacityAlpha);
            pixels[i] = normalizedAlpha;
            minAlpha = Math.Min(minAlpha, normalizedAlpha);
            maxAlpha = Math.Max(maxAlpha, normalizedAlpha);
            usesBinaryAlpha &= normalizedAlpha is 0 or 255;
        }

        return new TextureAlphaInfo(minAlpha, maxAlpha, usesBinaryAlpha);
    }

    private static byte NormalizePs2Alpha(byte alpha, byte fullOpacityAlpha)
    {
        if (alpha >= fullOpacityAlpha)
        {
            return byte.MaxValue;
        }

        return fullOpacityAlpha == 128
            ? (byte)Math.Min(byte.MaxValue, alpha * 2)
            : (byte)Math.Clamp(MathF.Round(alpha * byte.MaxValue / (float)fullOpacityAlpha), 0f, byte.MaxValue);
    }
}

using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Skyboxes;

public static partial class SkyboxGltfExporter
{
    private static List<SkyboxGltfTextureResource> BuildTextureResources(
        Skybox skybox,
        SkyboxGltfExportOptions options)
    {
        var resources = new List<SkyboxGltfTextureResource>(skybox.Textures.Count);
        var textureDirectory = string.IsNullOrWhiteSpace(options.TextureDirectoryName)
            ? "textures"
            : options.TextureDirectoryName.Trim().Replace('\\', '/').Trim('/');

        foreach (var texture in skybox.Textures)
        {
            var image = TextureConverter.Decode(
                texture.PixelData,
                texture.Width,
                texture.Height,
                TexturePixelFormat.Indexed8,
                texture.PaletteData,
                options.TextureConversionOptions);
            if (options.StraightenPremultipliedAlpha)
            {
                StraightenPremultipliedAlpha(image);
            }

            var alphaCutoffPixelCount = ApplyAlphaCutoff(image, options.TextureAlphaCutoff);
            if (options.DilateTransparentRgb)
            {
                DilateTransparentRgb(image);
            }

            var fileName = $"tex.{texture.Index:0000}.png";
            var uri = string.IsNullOrEmpty(textureDirectory)
                ? fileName
                : $"{textureDirectory}/{fileName}";
            var alpha = AnalyzeAlpha(image);
            resources.Add(new SkyboxGltfTextureResource(
                texture.Index,
                uri,
                fileName,
                TextureConverter.EncodePng(image),
                new TextureSize(image.Width, image.Height),
                alpha,
                options.StraightenPremultipliedAlpha,
                options.TextureAlphaCutoff,
                alphaCutoffPixelCount,
                options.DilateTransparentRgb));
        }

        return resources;
    }

    private static void StraightenPremultipliedAlpha(Rgba32Image image)
    {
        var pixels = image.PixelData;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }

            pixels[i] = StraightenChannel(pixels[i], alpha);
            pixels[i + 1] = StraightenChannel(pixels[i + 1], alpha);
            pixels[i + 2] = StraightenChannel(pixels[i + 2], alpha);
        }
    }

    private static byte StraightenChannel(byte channel, byte alpha)
    {
        return (byte)Math.Clamp((int)MathF.Round(channel * 255f / alpha), 0, byte.MaxValue);
    }

    private static int ApplyAlphaCutoff(Rgba32Image image, byte cutoff)
    {
        if (cutoff == 0)
        {
            return 0;
        }

        var pixels = image.PixelData;
        var changed = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i];
            if (alpha == 0 || alpha >= cutoff)
            {
                continue;
            }

            pixels[i] = 0;
            changed++;
        }

        return changed;
    }

    private static void DilateTransparentRgb(Rgba32Image image)
    {
        var pixels = image.PixelData;
        var width = image.Width;
        var height = image.Height;
        var scratch = new byte[pixels.Length];
        var sourceMask = new bool[width * height];
        var scratchSourceMask = new bool[sourceMask.Length];
        const int Passes = 8;

        for (var i = 0; i < sourceMask.Length; i++)
        {
            sourceMask[i] = pixels[(i * 4) + 3] != 0;
        }

        for (var pass = 0; pass < Passes; pass++)
        {
            Buffer.BlockCopy(pixels, 0, scratch, 0, pixels.Length);
            Array.Copy(sourceMask, scratchSourceMask, sourceMask.Length);
            var changed = false;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixelIndex = (y * width) + x;
                    var index = pixelIndex * 4;
                    if (scratchSourceMask[pixelIndex])
                    {
                        continue;
                    }

                    var r = 0;
                    var g = 0;
                    var b = 0;
                    var count = 0;
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x - 1, y - 1, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x, y - 1, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x + 1, y - 1, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x - 1, y, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x + 1, y, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x - 1, y + 1, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x, y + 1, ref r, ref g, ref b, ref count);
                    AccumulateRgbBleedSource(scratch, scratchSourceMask, width, height, x + 1, y + 1, ref r, ref g, ref b, ref count);
                    if (count == 0)
                    {
                        continue;
                    }

                    pixels[index] = (byte)(r / count);
                    pixels[index + 1] = (byte)(g / count);
                    pixels[index + 2] = (byte)(b / count);
                    sourceMask[pixelIndex] = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private static void AccumulateRgbBleedSource(
        byte[] pixels,
        bool[] sourceMask,
        int width,
        int height,
        int x,
        int y,
        ref int r,
        ref int g,
        ref int b,
        ref int count)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return;
        }

        var pixelIndex = (y * width) + x;
        if (!sourceMask[pixelIndex])
        {
            return;
        }

        var index = pixelIndex * 4;
        r += pixels[index];
        g += pixels[index + 1];
        b += pixels[index + 2];
        count++;
    }

    private static TextureAlphaInfo AnalyzeAlpha(Rgba32Image image)
    {
        byte minAlpha = 255;
        byte maxAlpha = 0;
        var usesBinaryAlpha = true;

        for (var i = 3; i < image.PixelData.Length; i += 4)
        {
            var alpha = image.PixelData[i];
            minAlpha = Math.Min(minAlpha, alpha);
            maxAlpha = Math.Max(maxAlpha, alpha);
            usesBinaryAlpha &= alpha is 0 or 255;
        }

        return new TextureAlphaInfo(minAlpha, maxAlpha, usesBinaryAlpha);
    }
}

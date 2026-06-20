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
                alpha));
        }

        return resources;
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

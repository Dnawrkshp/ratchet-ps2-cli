using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Cli.Abstractions;

internal static class TieTextureResourcePreparer
{
    public static TieTextureResources? PrepareExternalTextures(
        DirectoryInfo? sourceDirectory,
        DirectoryInfo outputDirectory)
    {
        var resources = TextureResourcePreparer.PrepareExternalTextures(sourceDirectory, outputDirectory);
        return resources is null
            ? null
            : new TieTextureResources(
                resources.Uris,
                resources.Sizes,
                resources.Alpha,
                resources.Entries.Select(entry => new TieTextureResourceEntry(
                    entry.Index,
                    entry.Uri,
                    entry.Width,
                    entry.Height,
                    entry.HasAlpha,
                    entry.AlphaMode,
                    entry.GltfAlphaMode,
                    entry.MinAlpha,
                    entry.MaxAlpha,
                    entry.UsesBinaryAlpha)).ToArray());
    }

    public static string ToGltfUri(string relativePath)
    {
        return CliPathUtils.ToUriPath(relativePath);
    }
}

internal sealed record TieTextureResources(
    IReadOnlyDictionary<int, string> Uris,
    IReadOnlyDictionary<int, TextureSize> Sizes,
    IReadOnlyDictionary<int, TextureAlphaInfo> Alpha,
    IReadOnlyList<TieTextureResourceEntry> Entries);

internal sealed record TieTextureResourceEntry(
    int Index,
    string Uri,
    int Width,
    int Height,
    bool HasAlpha,
    string AlphaMode,
    string? GltfAlphaMode,
    byte MinAlpha,
    byte MaxAlpha,
    bool UsesBinaryAlpha);

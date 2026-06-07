using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Cli.Abstractions;

internal static class TieTextureResourcePreparer
{
    public static TieTextureResources? PrepareExternalTextures(
        DirectoryInfo? sourceDirectory,
        DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        if (sourceDirectory is null || !sourceDirectory.Exists)
        {
            return null;
        }

        var candidates = sourceDirectory
            .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
            .Select(file => (File: file, TextureId: TryParseTextureId(file.Name, out var textureId) ? textureId : (int?)null))
            .Where(item => item.TextureId.HasValue)
            .OrderBy(item => item.TextureId!.Value)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var textureOutputDirectory = new DirectoryInfo(Path.Combine(outputDirectory.FullName, "textures"));
        textureOutputDirectory.Create();

        var uris = new Dictionary<int, string>();
        var sizes = new Dictionary<int, TextureSize>();
        var alpha = new Dictionary<int, TextureAlphaInfo>();
        var entries = new List<TieTextureResourceEntry>();
        foreach (var (sourceFile, textureId) in candidates)
        {
            var destinationFile = new FileInfo(Path.Combine(textureOutputDirectory.FullName, sourceFile.Name));
            if (!Path.GetFullPath(sourceFile.FullName).Equals(Path.GetFullPath(destinationFile.FullName), StringComparison.Ordinal))
            {
                sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
            }

            var uri = ToGltfUri(Path.GetRelativePath(outputDirectory.FullName, destinationFile.FullName));
            using var textureInput = sourceFile.OpenRead();
            var metadata = PngTextureMetadataReader.ReadPng(textureInput);
            uris[textureId!.Value] = uri;
            sizes[textureId.Value] = metadata.Size;
            alpha[textureId.Value] = metadata.Alpha;
            entries.Add(new TieTextureResourceEntry(
                textureId.Value,
                uri,
                metadata.Size.Width,
                metadata.Size.Height,
                metadata.Alpha.HasAlpha,
                metadata.Alpha.AlphaMode.ToString(),
                metadata.Alpha.GltfAlphaMode,
                metadata.Alpha.MinAlpha,
                metadata.Alpha.MaxAlpha,
                metadata.Alpha.UsesBinaryAlpha));
        }

        return new TieTextureResources(uris, sizes, alpha, entries);
    }

    public static string ToGltfUri(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool TryParseTextureId(string fileName, out int textureId)
    {
        textureId = 0;
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out textureId)
            && textureId >= 0)
        {
            return true;
        }

        var parts = fileName.Split('.');
        return parts.Length == 4
            && parts[0] == "tex"
            && parts[2] == "0"
            && parts[3].Equals("png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out textureId)
            && textureId >= 0;
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

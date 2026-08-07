using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Cli.Abstractions;

internal static class TextureResourcePreparer
{
    public static TextureResources? PrepareExternalTextures(
        DirectoryInfo? sourceDirectory,
        DirectoryInfo outputDirectory,
        string? outputSubdirectoryName = "textures")
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);

        if (sourceDirectory is null || !sourceDirectory.Exists)
        {
            return null;
        }

        var candidates = EnumerateTextureFiles(sourceDirectory)
            .Select(file => (File: file, TextureId: TryParseTextureId(file.Name, out var textureId) ? textureId : (int?)null))
            .Where(item => item.TextureId.HasValue)
            .OrderBy(item => item.TextureId!.Value)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var textureOutputDirectory = string.IsNullOrWhiteSpace(outputSubdirectoryName)
            ? outputDirectory
            : new DirectoryInfo(Path.Combine(outputDirectory.FullName, outputSubdirectoryName));
        textureOutputDirectory.Create();

        var uris = new Dictionary<int, string>();
        var sizes = new Dictionary<int, TextureSize>();
        var alpha = new Dictionary<int, TextureAlphaInfo>();
        var entries = new List<TextureResourceEntry>();
        foreach (var (sourceFile, textureId) in candidates)
        {
            var destinationFile = new FileInfo(Path.Combine(textureOutputDirectory.FullName, sourceFile.Name));
            var metadata = ReadTextureMetadata(sourceFile);
            var samePath = Path.GetFullPath(sourceFile.FullName).Equals(
                Path.GetFullPath(destinationFile.FullName),
                StringComparison.Ordinal);
            if (!samePath)
            {
                sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
            }

            var uri = CliPathUtils.ToUriPath(Path.GetRelativePath(outputDirectory.FullName, destinationFile.FullName));
            uris[textureId!.Value] = uri;
            sizes[textureId.Value] = metadata.Size;
            alpha[textureId.Value] = metadata.Alpha;
            entries.Add(new TextureResourceEntry(
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

        return new TextureResources(uris, sizes, alpha, entries);
    }

    private static TextureMetadata ReadTextureMetadata(FileInfo sourceFile)
    {
        using var textureInput = sourceFile.OpenRead();
        return PngTextureMetadataReader.ReadPng(textureInput);
    }

    public static bool TryParseTextureId(string fileName, out int textureId)
    {
        textureId = 0;
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out textureId)
            && textureId >= 0)
        {
            return true;
        }

        var parts = fileName.Split('.');
        if (parts.Length == 3
            && parts[0] == "tex"
            && parts[2].Equals("png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out textureId)
            && textureId >= 0)
        {
            return true;
        }

        if (parts.Length == 4
            && parts[0] == "tex"
            && parts[2] == "0"
            && parts[3].Equals("png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out textureId)
            && textureId >= 0)
        {
            return true;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var dash = name.LastIndexOf('-');
        if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && dash > 0
            && int.TryParse(name[..dash], out _)
            && int.TryParse(name[(dash + 1)..], out textureId)
            && textureId >= 0)
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<FileInfo> EnumerateTextureFiles(DirectoryInfo sourceDirectory)
    {
        var yielded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in sourceDirectory.EnumerateFiles("*.png", SearchOption.TopDirectoryOnly))
        {
            if (yielded.Add(Path.GetFullPath(file.FullName)))
            {
                yield return file;
            }
        }

        foreach (var directoryName in new[] { "textures", "Textures" })
        {
            var texturesDirectory = new DirectoryInfo(Path.Combine(sourceDirectory.FullName, directoryName));
            if (!texturesDirectory.Exists)
            {
                continue;
            }

            foreach (var file in texturesDirectory.EnumerateFiles("*.png", SearchOption.TopDirectoryOnly))
            {
                if (yielded.Add(Path.GetFullPath(file.FullName)))
                {
                    yield return file;
                }
            }
        }
    }
}

internal sealed record TextureResources(
    IReadOnlyDictionary<int, string> Uris,
    IReadOnlyDictionary<int, TextureSize> Sizes,
    IReadOnlyDictionary<int, TextureAlphaInfo> Alpha,
    IReadOnlyList<TextureResourceEntry> Entries);

internal sealed record TextureResourceEntry(
    int Index,
    string Uri,
    int Width,
    int Height,
    bool HasAlpha,
    string AlphaMode,
    string? GltfAlphaMode,
    byte MinAlpha,
    byte MaxAlpha,
    bool UsesBinaryAlpha,
    byte FullOpacityAlpha = byte.MaxValue);

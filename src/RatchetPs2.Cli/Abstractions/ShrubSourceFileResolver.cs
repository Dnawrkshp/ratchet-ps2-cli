namespace RatchetPs2.Cli.Abstractions;

internal enum ShrubSourceKind
{
    Auto,
    Packed
}

internal sealed record ShrubSourceFileSet(
    string SourceKind,
    IReadOnlyList<FileInfo> Files);

internal static class ShrubSourceKindParser
{
    public static bool TryParse(string? value, out ShrubSourceKind sourceKind)
    {
        sourceKind = $"{value}".Trim().ToLowerInvariant() switch
        {
            "auto" => ShrubSourceKind.Auto,
            "packed" => ShrubSourceKind.Packed,
            _ => (ShrubSourceKind)(-1)
        };

        return Enum.IsDefined(typeof(ShrubSourceKind), sourceKind);
    }
}

internal static class ShrubSourceFileResolver
{
    public static ShrubSourceFileSet Resolve(
        DirectoryInfo inputRoot,
        DirectoryInfo outputRoot,
        ShrubSourceKind sourceKind,
        string coreFileName)
    {
        ArgumentNullException.ThrowIfNull(inputRoot);
        ArgumentNullException.ThrowIfNull(outputRoot);

        if (sourceKind is ShrubSourceKind.Packed or ShrubSourceKind.Auto)
        {
            var coreFiles = EnumerateFiles(inputRoot, outputRoot, coreFileName).ToArray();
            if (sourceKind == ShrubSourceKind.Packed || coreFiles.Length > 0)
            {
                return new ShrubSourceFileSet("packed", coreFiles);
            }
        }

        return new ShrubSourceFileSet("packed", []);
    }

    private static IEnumerable<FileInfo> EnumerateFiles(
        DirectoryInfo inputRoot,
        DirectoryInfo outputRoot,
        string pattern)
    {
        return inputRoot
            .EnumerateFiles(pattern, SearchOption.AllDirectories)
            .Where(file => !CliPathUtils.IsInsideDirectory(file, outputRoot))
            .OrderBy(file => Path.GetRelativePath(inputRoot.FullName, file.FullName), StringComparer.Ordinal);
    }
}

namespace RatchetPs2.Cli.Abstractions;

internal static class CliPathUtils
{
    public static string ToUriPath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static bool IsInsideDirectory(FileInfo file, DirectoryInfo directory)
    {
        var filePath = Path.GetFullPath(file.FullName);
        var directoryPath = Path.GetFullPath(directory.FullName)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return filePath.StartsWith(directoryPath, StringComparison.Ordinal);
    }
}

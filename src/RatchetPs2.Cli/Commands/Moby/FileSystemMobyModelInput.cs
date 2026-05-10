using RatchetPs2.Games.UYA.Moby;

namespace RatchetPs2.Cli.Commands.Moby;

internal sealed class FileSystemMobyModelInput : IMobyModelInput
{
    private readonly string inputDirectory;

    public FileSystemMobyModelInput(string inputDirectory)
    {
        this.inputDirectory = inputDirectory ?? throw new ArgumentNullException(nameof(inputDirectory));
    }

    public bool FileExists(string relativePath)
    {
        return File.Exists(FullPath(relativePath));
    }

    public bool DirectoryExists(string relativePath)
    {
        return Directory.Exists(FullPath(relativePath));
    }

    public byte[] ReadBytes(string relativePath)
    {
        return File.ReadAllBytes(FullPath(relativePath));
    }

    public IReadOnlyList<string> EnumerateDirectories(string relativePath)
    {
        var fullPath = FullPath(relativePath);
        if (!Directory.Exists(fullPath))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(fullPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Order(StringComparer.Ordinal)
            .ToArray()!;
    }

    public IReadOnlyList<string> EnumerateFiles(string relativePath, string searchPattern = "*")
    {
        var fullPath = FullPath(relativePath);
        if (!Directory.Exists(fullPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(fullPath, searchPattern)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Order(StringComparer.Ordinal)
            .ToArray()!;
    }

    private string FullPath(string relativePath)
    {
        return Path.Combine(inputDirectory, relativePath);
    }
}

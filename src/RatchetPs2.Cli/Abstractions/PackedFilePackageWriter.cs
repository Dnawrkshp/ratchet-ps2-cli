using System.Text.Json;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Cli.Abstractions;

internal static class PackedFilePackageWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void WriteFiles(DlLevelWadPackage package, DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);

        WriteFiles(package.Files, outputDirectory);
    }

    public static void WriteFiles(IReadOnlyList<PackedFile> files, DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        outputDirectory.Create();
        foreach (var file in files)
        {
            var outputPath = CombineSafe(outputDirectory.FullName, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, file.Bytes);
        }
    }

    public static void WriteIndexed(PackedFilePackage package, DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        outputDirectory.Create();
        File.WriteAllBytes(Path.Combine(outputDirectory.FullName, "payload.bin"), package.PackedBytes);
        File.WriteAllText(
            Path.Combine(outputDirectory.FullName, "index.json"),
            JsonSerializer.Serialize(
                new
                {
                    PackedBytesPath = "payload.bin",
                    package.Entries
                },
                JsonOptions));
    }

    private static string CombineSafe(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Packed file path '{relativePath}' must be relative.");
        }

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"Packed file path '{relativePath}' cannot traverse directories.");
        }

        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}

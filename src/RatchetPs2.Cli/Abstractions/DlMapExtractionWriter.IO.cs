using System.Text.Json;
using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static void WriteSectorBlock(
        string outputDirectory,
        string fileName,
        Stream isoStream,
        int baseSector,
        DlFileBlock block)
    {
        var bytes = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, baseSector, block);
        if (bytes.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), bytes);
        }
    }

    private static void WriteByteLengthBlock(
        string outputDirectory,
        string fileName,
        Stream isoStream,
        int baseSector,
        DlFileBlock block)
    {
        var bytes = DlLevelInfoReader.ReadByteLengthSectorRelativeBlock(isoStream, baseSector, block);
        if (bytes.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), bytes);
        }
    }

    private static bool TryGetCoreSegment(
        IReadOnlyDictionary<int, DlCoreLevelSegment> segments,
        int headerOffset,
        out DlCoreLevelSegment segment)
    {
        return segments.TryGetValue(headerOffset, out segment!);
    }

    private static MediaSource CreateMediaSource(DlLevelInfoSet levelInfo)
    {
        var isInherited = levelInfo.MediaLevelIndex != levelInfo.RequestedLevelIndex;
        return new MediaSource(
            levelInfo.RequestedLevelIndex,
            levelInfo.MediaLevelIndex,
            isInherited ? "inherited_media_level" : "requested_level",
            isInherited,
            isInherited ? $"inherited/level_{levelInfo.MediaLevelIndex:0000}" : null);
    }

    private static MediaPayloadSource CreateMediaPayloadSource(
        string payload,
        string directory,
        MediaSource source)
    {
        return new MediaPayloadSource(
            payload,
            source.Kind,
            source.RequestedLevelIndex,
            source.MediaLevelIndex,
            source.IsInherited,
            directory);
    }

    private static string GetMediaPayloadDirectory(string payloadDirectory, MediaSource source)
    {
        return source.InheritedRoot is null
            ? payloadDirectory
            : $"{source.InheritedRoot}/{payloadDirectory}";
    }

    private static string CreateDirectory(params string[] parts)
    {
        var path = Path.Combine(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateDirectoryForRelativePath(string root, string relativePath)
    {
        var path = CombineRelativePath(root, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CombineRelativePath(string root, string relativePath)
    {
        return Path.Combine(new[] { root }.Concat(relativePath.Split('/')).ToArray());
    }

    private static void WriteJson(string path, object value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void PrepareGltfOutput(FileInfo outputFile)
    {
        outputFile.Directory?.Create();
        var basePath = Path.Combine(
            outputFile.DirectoryName ?? string.Empty,
            Path.GetFileNameWithoutExtension(outputFile.Name));

        DeleteIfExists(outputFile.FullName);
        DeleteIfExists($"{basePath}.buffer.bin");
        DeleteIfExists($"{basePath}.diagnostics.json");
    }

    private static bool IsGltfExportFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException;
    }

    private static string ToRelativeAssetPath(string assetDirectory, string path)
    {
        return CliPathUtils.ToUriPath(Path.GetRelativePath(assetDirectory, path));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteMatchingFiles(string directory, params string[] patterns)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var pattern in patterns)
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
            }
        }
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static void AddOmittedRawPayload(
        IDictionary<string, object?> manifest,
        string path,
        string replacement,
        string reason)
    {
        if (!manifest.TryGetValue("OmittedRawPayloads", out var existing)
            || existing is not List<object> omittedRawPayloads)
        {
            omittedRawPayloads = [];
            manifest["OmittedRawPayloads"] = omittedRawPayloads;
        }

        omittedRawPayloads.Add(new
        {
            Path = path,
            Replacement = replacement,
            Reason = reason
        });
    }
}

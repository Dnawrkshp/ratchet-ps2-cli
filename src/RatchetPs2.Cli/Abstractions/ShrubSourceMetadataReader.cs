using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Abstractions;

internal sealed record ShrubSourceMetadata(
    GameId? GameId,
    IReadOnlyList<string> Labels,
    FileInfo? MetaFile);

internal static class ShrubSourceMetadataReader
{
    public static ShrubSourceMetadata ReadForSource(FileInfo sourceFile)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);

        var sourceDirectory = sourceFile.Directory;
        if (sourceDirectory is null)
        {
            return Empty();
        }

        var metaFile = FindPrimaryMetaFile(sourceDirectory);
        if (metaFile is null)
        {
            return Empty();
        }

        var labels = ReadLabels(metaFile).ToArray();
        return new ShrubSourceMetadata(InferGame(labels), labels, metaFile);
    }

    private static ShrubSourceMetadata Empty()
    {
        return new ShrubSourceMetadata(GameId: null, Labels: [], MetaFile: null);
    }

    private static FileInfo? FindPrimaryMetaFile(DirectoryInfo sourceDirectory)
    {
        var preferred = new FileInfo(Path.Combine(sourceDirectory.FullName, $"{sourceDirectory.Name}.fbx.meta"));
        if (preferred.Exists)
        {
            return preferred;
        }

        return sourceDirectory
            .EnumerateFiles("*.fbx.meta", SearchOption.TopDirectoryOnly)
            .Where(file => !file.Name.EndsWith("_col.fbx.meta", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? sourceDirectory
                .EnumerateFiles("*.fbx.meta", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .FirstOrDefault();
    }

    private static IEnumerable<string> ReadLabels(FileInfo metaFile)
    {
        var inLabels = false;
        foreach (var rawLine in File.ReadLines(metaFile.FullName))
        {
            var line = rawLine.TrimEnd();
            if (!inLabels)
            {
                if (line.Equals("labels:", StringComparison.Ordinal))
                {
                    inLabels = true;
                }

                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                var label = trimmed[2..].Trim();
                if (!string.IsNullOrWhiteSpace(label))
                {
                    yield return label;
                }

                continue;
            }

            if (line.Length == 0 || line[0] != ' ')
            {
                yield break;
            }
        }
    }

    private static GameId? InferGame(IEnumerable<string> labels)
    {
        foreach (var label in labels)
        {
            if (label.Equals("GC", StringComparison.OrdinalIgnoreCase))
            {
                return GameId.GC;
            }

            if (label.Equals("UYA", StringComparison.OrdinalIgnoreCase))
            {
                return GameId.UYA;
            }

            if (label.Equals("DL", StringComparison.OrdinalIgnoreCase))
            {
                return GameId.DL;
            }
        }

        return null;
    }
}

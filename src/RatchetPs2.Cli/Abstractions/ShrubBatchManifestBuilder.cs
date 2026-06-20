using RatchetPs2.Core.Games;

namespace RatchetPs2.Cli.Abstractions;

internal static class ShrubBatchManifestBuilder
{
    public static object BuildManifest(
        DirectoryInfo inputRoot,
        DirectoryInfo outputRoot,
        ShrubBatchExportResult exportResult)
    {
        return new
        {
            Format = "ratchet-ps2-shrub-viewer-manifest-v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            exportResult.Game,
            exportResult.SourceKind,
            SourceRoot = CliPathUtils.ToUriPath(inputRoot.FullName),
            OutputRoot = CliPathUtils.ToUriPath(outputRoot.FullName),
            Totals = new
            {
                exportResult.Found,
                exportResult.Succeeded,
                exportResult.Failed,
                TotalMs = exportResult.TotalElapsed.TotalMilliseconds,
                AverageSuccessMs = exportResult.SuccessfulDurations.Count == 0 ? 0 : exportResult.SuccessfulDurations.Average(),
                MedianSuccessMs = Median(exportResult.SuccessfulDurations),
                exportResult.TotalInputBytes,
                exportResult.TotalOutputBytes,
                MeshCount = exportResult.TotalMeshCount,
                PrimitiveCount = exportResult.TotalPrimitiveCount,
                VertexCount = exportResult.TotalVertexCount,
                TriangleCount = exportResult.TotalTriangleCount,
                MaterialCount = exportResult.TotalMaterialCount,
                TextureCount = exportResult.TotalTextureCount,
                exportResult.GameCounts
            },
            Entries = exportResult.Entries
        };
    }

    public static object BuildSuccessEntry(
        string id,
        GameId gameId,
        FileInfo sourceFile,
        ShrubFileExport fileExport,
        ShrubSourceMetadata metadata,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        double conversionMs)
    {
        var header = fileExport.PackedShrub?.Header;
        return new
        {
            Id = id,
            Label = fileExport.Id is { } shrubId ? $"{id} / {shrubId}" : id,
            Game = gameId.ToString(),
            Status = "ok",
            fileExport.SourceKind,
            Labels = metadata.Labels,
            Metadata = ToRelativeUri(manifestDirectory, metadata.MetaFile),
            SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
            Source = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, sourceFile.FullName)),
            Gltf = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, fileExport.GltfFile.FullName)),
            Buffer = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, fileExport.BufferFile.FullName)),
            Diagnostics = ToRelativeUri(manifestDirectory, fileExport.DiagnosticsFile),
            ConversionMs = conversionMs,
            InputBytes = fileExport.InputBytes,
            OutputBytes = fileExport.OutputBytes,
            Header = header is null ? null : new
            {
                OClass = $"0x{(ushort)header.OClass:X4}",
                SClass = $"0x{(ushort)header.SClass:X4}",
                ModeBits = $"0x{header.ModeBits:X4}",
                header.MipDistance,
                header.Scale,
                header.PacketCount,
                header.InstanceCount,
                header.BillboardCount,
                BoundingRadius = header.BoundingSphere.W
            },
            Geometry = new
            {
                fileExport.ModelInfo.MeshCount,
                fileExport.ModelInfo.PrimitiveCount,
                fileExport.ModelInfo.VertexCount,
                fileExport.ModelInfo.TriangleCount,
                fileExport.ModelInfo.MaterialCount,
                fileExport.ModelInfo.TextureCount,
                Bounds = fileExport.ModelInfo.Bounds
            },
            Textures = fileExport.Textures,
            Billboard = fileExport.Billboard is null ? null : new
            {
                fileExport.Billboard.FadeDistance,
                fileExport.Billboard.Width,
                fileExport.Billboard.Height,
                fileExport.Billboard.ZOffset,
                Texture = fileExport.BillboardTextureUri
            }
        };
    }

    public static object BuildFailureEntry(
        string id,
        GameId? gameId,
        FileInfo sourceFile,
        ShrubSourceMetadata metadata,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        double conversionMs,
        Exception exception)
    {
        return new
        {
            Id = id,
            Label = id,
            Game = gameId?.ToString() ?? "unknown",
            Labels = metadata.Labels,
            Metadata = ToRelativeUri(manifestDirectory, metadata.MetaFile),
            SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
            Source = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, sourceFile.FullName)),
            Status = "failed",
            ConversionMs = conversionMs,
            Error = exception.Message
        };
    }

    private static string? ToRelativeUri(DirectoryInfo manifestDirectory, FileInfo? file)
    {
        return file is null
            ? null
            : CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, file.FullName));
    }

    public static string NormalizeId(string relativeSourceDirectory, int fallbackIndex)
    {
        var normalized = CliPathUtils.ToUriPath(relativeSourceDirectory)
            .Trim('/')
            .Replace('/', ' ');
        return string.IsNullOrWhiteSpace(normalized)
            ? $"shrub {fallbackIndex:0000}"
            : normalized;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var midpoint = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[midpoint - 1] + sorted[midpoint]) / 2
            : sorted[midpoint];
    }
}

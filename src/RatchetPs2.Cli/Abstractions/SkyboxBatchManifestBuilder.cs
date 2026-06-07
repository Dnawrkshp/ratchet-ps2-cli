using RatchetPs2.Core.Games;
using System.Text.Json;

namespace RatchetPs2.Cli.Abstractions;

internal static class SkyboxBatchManifestBuilder
{
    public static object BuildManifest(
        GameId gameId,
        DirectoryInfo inputRoot,
        DirectoryInfo outputRoot,
        string skyFileName,
        int found,
        int succeeded,
        int failed,
        TimeSpan totalElapsed,
        IReadOnlyList<double> successfulDurations,
        long totalInputBytes,
        long totalOutputBytes,
        IReadOnlyList<object> entries)
    {
        return new
        {
            Format = "ratchet-ps2-skybox-viewer-manifest-v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Game = gameId.ToString(),
            SourceRoot = CliPathUtils.ToUriPath(inputRoot.FullName),
            OutputRoot = CliPathUtils.ToUriPath(outputRoot.FullName),
            SkyFileName = skyFileName,
            Totals = new
            {
                Found = found,
                Succeeded = succeeded,
                Failed = failed,
                TotalMs = totalElapsed.TotalMilliseconds,
                AverageSuccessMs = successfulDurations.Count == 0 ? 0 : successfulDurations.Average(),
                MedianSuccessMs = Median(successfulDurations),
                TotalInputBytes = totalInputBytes,
                TotalOutputBytes = totalOutputBytes
            },
            Entries = entries
        };
    }

    public static object BuildSuccessEntry(
        string id,
        GameId gameId,
        FileInfo skyFile,
        SkyboxFileExport fileExport,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        double conversionMs)
    {
        var skybox = fileExport.Skybox;
        var export = fileExport.Export;
        var clusterCount = skybox.Shells.Sum(shell => shell.Clusters.Count);
        var sourceVertexCount = skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Vertices.Count);
        var triangleCount = skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Triangles.Count);
        using var diagnosticsDocument = JsonDocument.Parse(export.DiagnosticsBytes);
        var diagnostics = diagnosticsDocument.RootElement;
        var exportedVertexCount = diagnostics.TryGetProperty("PositionCount", out var positionCountElement)
            ? positionCountElement.GetInt32()
            : triangleCount * 3;
        var colorCount = diagnostics.TryGetProperty("ColorCount", out var colorCountElement)
            ? colorCountElement.GetInt32()
            : 0;
        var primitiveCount = diagnostics.TryGetProperty("PrimitiveCount", out var primitiveCountElement)
            ? primitiveCountElement.GetInt32()
            : 0;
        var textureTriangleCounts = diagnostics.TryGetProperty("TextureTriangleCounts", out var textureCountsElement)
            ? textureCountsElement
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetInt32(), StringComparer.Ordinal)
            : skybox.Shells
                .SelectMany(shell => shell.Clusters)
                .SelectMany(cluster => cluster.Triangles)
                .GroupBy(triangle => triangle.TextureId)
                .OrderBy(group => group.Key == 0xFF ? int.MaxValue : group.Key)
                .ToDictionary(
                    group => group.Key == 0xFF ? "untextured" : group.Key.ToString(),
                    group => group.Count(),
                    StringComparer.Ordinal);

        return new
        {
            Id = id,
            Label = id,
            Game = gameId.ToString(),
            Status = "ok",
            SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
            SourceSky = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, skyFile.FullName)),
            Gltf = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, fileExport.GltfFile.FullName)),
            Buffer = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, fileExport.BufferFile.FullName)),
            Diagnostics = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, fileExport.DiagnosticsFile.FullName)),
            ConversionMs = conversionMs,
            InputBytes = skyFile.Length,
            OutputBytes = fileExport.OutputBytes,
            Header = new
            {
                skybox.Header.ClearScreen,
                skybox.Header.ShellCount,
                skybox.Header.SpriteCount,
                skybox.Header.SpriteMax,
                skybox.Header.TextureCount,
                skybox.Header.FxCount,
                TextureDefOffset = FormatOffset(skybox.Header.TextureDefOffset),
                TextureDataOffset = FormatOffset(skybox.Header.TextureDataOffset)
            },
            Geometry = new
            {
                ShellCount = skybox.Shells.Count,
                ClusterCount = clusterCount,
                SourceVertexCount = sourceVertexCount,
                ExportedVertexCount = exportedVertexCount,
                ColorCount = colorCount,
                TriangleCount = triangleCount,
                PrimitiveCount = primitiveCount,
                TexturedTriangleCount = textureTriangleCounts
                    .Where(pair => pair.Key != "untextured")
                    .Sum(pair => pair.Value),
                UntexturedTriangleCount = textureTriangleCounts.GetValueOrDefault("untextured"),
                TextureTriangleCounts = textureTriangleCounts
            },
            Textures = export.Textures.Select(texture => new
            {
                texture.Index,
                texture.Uri,
                texture.FileName,
                texture.Size.Width,
                texture.Size.Height,
                texture.Alpha.MinAlpha,
                texture.Alpha.MaxAlpha,
                texture.Alpha.UsesBinaryAlpha,
                AlphaMode = texture.Alpha.AlphaMode.ToString(),
                texture.Alpha.HasAlpha
            }).ToArray()
        };
    }

    public static object BuildFailureEntry(
        string id,
        GameId gameId,
        FileInfo skyFile,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        double conversionMs,
        Exception exception)
    {
        return new
        {
            Id = id,
            Label = id,
            Game = gameId.ToString(),
            SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
            SourceSky = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, skyFile.FullName)),
            Status = "failed",
            ConversionMs = conversionMs,
            Error = exception.Message
        };
    }

    public static string NormalizeId(string relativeSourceDirectory, int fallbackIndex)
    {
        var normalized = CliPathUtils.ToUriPath(relativeSourceDirectory)
            .Trim('/')
            .Replace('/', ' ');
        return string.IsNullOrWhiteSpace(normalized)
            ? $"skybox {fallbackIndex:0000}"
            : normalized;
    }

    private static string FormatOffset(long offset)
    {
        return $"0x{offset:X}";
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

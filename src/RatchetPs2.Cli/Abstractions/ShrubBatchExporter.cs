using RatchetPs2.Core.Games;
using System.Diagnostics;

namespace RatchetPs2.Cli.Abstractions;

internal sealed record ShrubBatchExportOptions(
    GameId? GameId,
    DirectoryInfo InputRoot,
    DirectoryInfo OutputRoot,
    DirectoryInfo ManifestDirectory,
    ShrubSourceKind SourceKind,
    string CoreFileName,
    int? Limit);

internal sealed record ShrubBatchExportResult(
    string Game,
    string SourceKind,
    int Found,
    int Succeeded,
    int Failed,
    TimeSpan TotalElapsed,
    IReadOnlyList<double> SuccessfulDurations,
    long TotalInputBytes,
    long TotalOutputBytes,
    long TotalMeshCount,
    long TotalPrimitiveCount,
    long TotalVertexCount,
    long TotalTriangleCount,
    long TotalMaterialCount,
    long TotalTextureCount,
    IReadOnlyDictionary<string, int> GameCounts,
    IReadOnlyList<object> Entries);

internal static class ShrubBatchExporter
{
    public static ShrubBatchExportResult Export(
        ShrubBatchExportOptions options,
        Action<string>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.OutputRoot.Create();
        var sourceSet = ShrubSourceFileResolver.Resolve(
            options.InputRoot,
            options.OutputRoot,
            options.SourceKind,
            options.CoreFileName);
        var sourceFiles = sourceSet.Files
            .Take(options.Limit ?? int.MaxValue)
            .ToArray();

        var entries = new List<object>(sourceFiles.Length);
        var totals = new ShrubBatchTotals();
        var totalStopwatch = Stopwatch.StartNew();

        for (var i = 0; i < sourceFiles.Length; i++)
        {
            var sourceFile = sourceFiles[i];
            ExportOne(options, sourceFile, i, entries, totals);

            if ((i + 1) % 50 == 0 || i + 1 == sourceFiles.Length)
            {
                reportProgress?.Invoke(
                    $"Processed {i + 1}/{sourceFiles.Length} shrubs ({totals.Succeeded} ok, {totals.Failed} failed) in {totalStopwatch.Elapsed.TotalSeconds:F1}s.");
            }
        }

        totalStopwatch.Stop();
        var gameLabel = options.GameId?.ToString() ?? totals.GameLabel;
        return totals.ToResult(gameLabel, sourceSet.SourceKind, sourceFiles.Length, totalStopwatch.Elapsed, entries);
    }

    private static void ExportOne(
        ShrubBatchExportOptions options,
        FileInfo sourceFile,
        int index,
        List<object> entries,
        ShrubBatchTotals totals)
    {
        var relativeSourceDirectory = Path.GetRelativePath(
            options.InputRoot.FullName,
            sourceFile.DirectoryName ?? options.InputRoot.FullName);
        var id = ShrubBatchManifestBuilder.NormalizeId(relativeSourceDirectory, index);
        var outputDirectory = new DirectoryInfo(Path.Combine(options.OutputRoot.FullName, relativeSourceDirectory));
        outputDirectory.Create();
        var gltfFile = new FileInfo(Path.Combine(outputDirectory.FullName, "shrub.gltf"));
        var itemStopwatch = Stopwatch.StartNew();
        var metadata = ShrubSourceMetadataReader.ReadForSource(sourceFile);
        var gameId = options.GameId ?? metadata.GameId;

        try
        {
            if (gameId is null)
            {
                throw new InvalidDataException(
                    $"Could not infer shrub game from '{sourceFile.FullName}'. Add a GC, UYA, or DL label to the sibling .fbx.meta file or pass --game explicitly.");
            }

            var fileExport = ShrubExportWriter.Export(sourceFile, gltfFile, gameId.Value);
            itemStopwatch.Stop();
            totals.AddSuccess(fileExport, gameId.Value, itemStopwatch.Elapsed);
            entries.Add(ShrubBatchManifestBuilder.BuildSuccessEntry(
                id,
                gameId.Value,
                sourceFile,
                fileExport,
                metadata,
                options.ManifestDirectory,
                relativeSourceDirectory,
                itemStopwatch.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex)
        {
            itemStopwatch.Stop();
            totals.AddFailure(gameId);
            entries.Add(ShrubBatchManifestBuilder.BuildFailureEntry(
                id,
                gameId,
                sourceFile,
                metadata,
                options.ManifestDirectory,
                relativeSourceDirectory,
                itemStopwatch.Elapsed.TotalMilliseconds,
                ex));
        }
    }

    private sealed class ShrubBatchTotals
    {
        private readonly List<double> successfulDurations = [];
        private readonly Dictionary<string, int> gameCounts = new(StringComparer.Ordinal);

        public int Succeeded { get; private set; }

        public int Failed { get; private set; }

        public long TotalInputBytes { get; private set; }

        public long TotalOutputBytes { get; private set; }

        public long TotalMeshCount { get; private set; }

        public long TotalPrimitiveCount { get; private set; }

        public long TotalVertexCount { get; private set; }

        public long TotalTriangleCount { get; private set; }

        public long TotalMaterialCount { get; private set; }

        public long TotalTextureCount { get; private set; }

        public string GameLabel => gameCounts.Count switch
        {
            0 => "auto",
            1 => gameCounts.Keys.Single(),
            _ => "mixed"
        };

        public void AddSuccess(ShrubFileExport fileExport, GameId gameId, TimeSpan elapsed)
        {
            Succeeded++;
            IncrementGameCount(gameId);
            successfulDurations.Add(elapsed.TotalMilliseconds);
            TotalInputBytes += fileExport.InputBytes;
            TotalOutputBytes += fileExport.OutputBytes;
            TotalMeshCount += fileExport.ModelInfo.MeshCount;
            TotalPrimitiveCount += fileExport.ModelInfo.PrimitiveCount;
            TotalVertexCount += fileExport.ModelInfo.VertexCount;
            TotalTriangleCount += fileExport.ModelInfo.TriangleCount;
            TotalMaterialCount += fileExport.ModelInfo.MaterialCount;
            TotalTextureCount += fileExport.ModelInfo.TextureCount;
        }

        public void AddFailure(GameId? gameId)
        {
            Failed++;
            if (gameId is { } resolvedGameId)
            {
                IncrementGameCount(resolvedGameId);
            }
        }

        public ShrubBatchExportResult ToResult(
            string gameLabel,
            string sourceKind,
            int found,
            TimeSpan elapsed,
            IReadOnlyList<object> entries)
        {
            return new ShrubBatchExportResult(
                gameLabel,
                sourceKind,
                found,
                Succeeded,
                Failed,
                elapsed,
                successfulDurations.ToArray(),
                TotalInputBytes,
                TotalOutputBytes,
                TotalMeshCount,
                TotalPrimitiveCount,
                TotalVertexCount,
                TotalTriangleCount,
                TotalMaterialCount,
                TotalTextureCount,
                new Dictionary<string, int>(gameCounts, StringComparer.Ordinal),
                entries);
        }

        private void IncrementGameCount(GameId gameId)
        {
            var key = gameId.ToString();
            gameCounts[key] = gameCounts.GetValueOrDefault(key) + 1;
        }
    }
}

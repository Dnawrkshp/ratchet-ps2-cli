using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Tfrags;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace RatchetPs2.Cli.Commands.Tfrag;

internal static class TfragExportGltfBatchCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputRootOption = new Option<DirectoryInfo>("--input-root")
        {
            Description = "Root directory to scan recursively for tfrag.bin files.",
            Required = true
        };
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write exported glTFs and the viewer manifest.",
            Required = true
        };
        var tfragFileNameOption = new Option<string>("--tfrag-file-name")
        {
            Description = "Tfrag binary file name to scan for. Defaults to tfrag.bin.",
            DefaultValueFactory = _ => "tfrag.bin"
        };
        tfragFileNameOption.Aliases.Add("--terrain-file-name");
        var manifestNameOption = new Option<string>("--manifest-name")
        {
            Description = "Viewer manifest file name. Defaults to manifest.json.",
            DefaultValueFactory = _ => "manifest.json"
        };
        var limitOption = new Option<int?>("--limit")
        {
            Description = "Optional maximum number of tfrag terrain files to export."
        };
        var minifyOption = CommonOptions.MinifyGltf();

        var command = CliCommandBuilder.Create(
            "export-gltf-batch",
            "Export a directory of tfrag files to glTF and write a viewer manifest.",
            gameOption,
            inputRootOption,
            outputRootOption,
            tfragFileNameOption,
            manifestNameOption,
            limitOption,
            minifyOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputRoot = parseResult.GetValue(inputRootOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            var tfragFileName = parseResult.GetValue(tfragFileNameOption);
            var manifestName = parseResult.GetValue(manifestNameOption);
            var limit = parseResult.GetValue(limitOption);
            var minify = parseResult.GetValue(minifyOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {TfragGameFormats.SupportedTfragGames} for tfrag glTF batch export.");
                return;
            }

            if (!TfragGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Tfrag glTF batch export currently supports only {TfragGameFormats.SupportedTfragGames}. Received {gameId}.");
                return;
            }

            if (inputRoot is null)
            {
                parseResult.GetResult(inputRootOption)?.AddError("Missing required --input-root option.");
                return;
            }

            if (outputRoot is null)
            {
                parseResult.GetResult(outputRootOption)?.AddError("Missing required --output-root option.");
                return;
            }

            if (!inputRoot.Exists)
            {
                parseResult.GetResult(inputRootOption)?.AddError(
                    $"Input root '{inputRoot.FullName}' does not exist.");
                return;
            }

            if (string.IsNullOrWhiteSpace(tfragFileName))
            {
                parseResult.GetResult(tfragFileNameOption)?.AddError("--tfrag-file-name cannot be empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(manifestName))
            {
                parseResult.GetResult(manifestNameOption)?.AddError("--manifest-name cannot be empty.");
                return;
            }

            if (limit is <= 0)
            {
                parseResult.GetResult(limitOption)?.AddError("--limit must be greater than zero when supplied.");
                return;
            }

            outputRoot.Create();
            var manifestFile = new FileInfo(Path.Combine(outputRoot.FullName, manifestName));
            var manifestDirectory = manifestFile.Directory ?? outputRoot;
            var tfragFiles = inputRoot
                .EnumerateFiles(tfragFileName, SearchOption.AllDirectories)
                .OrderBy(file => Path.GetRelativePath(inputRoot.FullName, file.FullName), StringComparer.Ordinal)
                .Take(limit ?? int.MaxValue)
                .ToArray();

            var entries = new List<object>(tfragFiles.Length);
            var totalStopwatch = Stopwatch.StartNew();
            var successfulDurations = new List<double>();
            var totalInputBytes = 0L;
            var totalOutputBytes = 0L;
            var succeeded = 0;
            var failed = 0;

            for (var i = 0; i < tfragFiles.Length; i++)
            {
                var tfragFile = tfragFiles[i];
                var relativeSourceDirectory = Path.GetRelativePath(inputRoot.FullName, tfragFile.DirectoryName ?? inputRoot.FullName);
                var sourceDirectoryName = NormalizeId(relativeSourceDirectory, i);
                var outputDirectory = new DirectoryInfo(Path.Combine(outputRoot.FullName, relativeSourceDirectory));
                outputDirectory.Create();

                var gltfFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tfrag.gltf"));
                var bufferFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tfrag.buffer.bin"));
                var diagnosticsFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tfrag.diagnostics.json"));
                var itemStopwatch = Stopwatch.StartNew();

                try
                {
                    using var input = tfragFile.OpenRead();
                    var terrain = TfragTerrainReader.Read(input);
                    var textureResources = TextureResourcePreparer.PrepareExternalTextures(
                        tfragFile.Directory,
                        outputDirectory,
                        normalizePs2FullOpacityAlpha: TfragTextureAlpha.FullOpacityAlpha);
                    var export = TfragGltfExporter.Export(
                        terrain,
                        gltfFile.Name,
                        new TfragGltfExportOptions
                        {
                            BufferFileName = bufferFile.Name,
                            GameLabel = gameId.ToString(),
                            ExternalTextureUris = textureResources?.Uris,
                            ExternalTextureSizes = textureResources?.Sizes,
                            ExternalTextureAlpha = textureResources?.Alpha,
                            IncludeDiagnostics = !minify,
                            Minify = minify,
                            MetadataMode = minify ? GltfExportMetadataMode.RuntimeOnly : GltfExportMetadataMode.Full
                        });

                    File.WriteAllBytes(gltfFile.FullName, export.GltfBytes);
                    File.WriteAllBytes(bufferFile.FullName, export.BinBytes);
                    if (export.DiagnosticsBytes.Length > 0)
                    {
                        File.WriteAllBytes(diagnosticsFile.FullName, export.DiagnosticsBytes);
                    }
                    itemStopwatch.Stop();

                    succeeded++;
                    successfulDurations.Add(itemStopwatch.Elapsed.TotalMilliseconds);
                    totalInputBytes += tfragFile.Length;
                    totalOutputBytes += export.GltfBytes.Length + export.BinBytes.Length + export.DiagnosticsBytes.Length;
                    entries.Add(BuildSuccessEntry(
                        sourceDirectoryName,
                        tfragFile,
                        gltfFile,
                        bufferFile,
                        diagnosticsFile,
                        manifestDirectory,
                        relativeSourceDirectory,
                        terrain,
                        gameId,
                        textureResources,
                        itemStopwatch.Elapsed.TotalMilliseconds,
                        export,
                        minify));
                }
                catch (Exception ex)
                {
                    itemStopwatch.Stop();
                    failed++;
                    entries.Add(new
                    {
                        Id = sourceDirectoryName,
                        Label = sourceDirectoryName,
                        Game = gameId.ToString(),
                        Status = "failed",
                        SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
                        SourceTfrag = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, tfragFile.FullName)),
                        ConversionMs = itemStopwatch.Elapsed.TotalMilliseconds,
                        Error = ex.Message
                    });
                }

                if ((i + 1) % 25 == 0 || i + 1 == tfragFiles.Length)
                {
                    Console.WriteLine(
                        $"Processed {i + 1}/{tfragFiles.Length} tfrag file(s) ({succeeded} ok, {failed} failed) in {totalStopwatch.Elapsed.TotalSeconds:F1}s.");
                }
            }

            totalStopwatch.Stop();
            var manifest = new
            {
                Format = "ratchet-ps2-tfrag-viewer-manifest-v1",
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Game = gameId.ToString(),
                SourceRoot = CliPathUtils.ToUriPath(inputRoot.FullName),
                OutputRoot = CliPathUtils.ToUriPath(outputRoot.FullName),
                TfragFileName = tfragFileName,
                Totals = new
                {
                    Found = tfragFiles.Length,
                    Succeeded = succeeded,
                    Failed = failed,
                    TotalMs = totalStopwatch.Elapsed.TotalMilliseconds,
                    AverageSuccessMs = successfulDurations.Count == 0 ? 0 : successfulDurations.Average(),
                    MedianSuccessMs = Median(successfulDurations),
                    TotalInputBytes = totalInputBytes,
                    TotalOutputBytes = totalOutputBytes
                },
                Entries = entries
            };

            File.WriteAllBytes(
                manifestFile.FullName,
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine(
                $"Wrote tfrag batch manifest '{manifestFile.FullName}' with {succeeded} successful export(s), {failed} failure(s), total {totalStopwatch.Elapsed.TotalSeconds:F2}s.");
        });

        return command;
    }

    private static object BuildSuccessEntry(
        string id,
        FileInfo tfragFile,
        FileInfo gltfFile,
        FileInfo bufferFile,
        FileInfo diagnosticsFile,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        TfragTerrain terrain,
        GameId gameId,
        TextureResources? textureResources,
        double conversionMs,
        TfragGltfExport export,
        bool minify)
    {
        using var gltfInput = new MemoryStream(export.GltfBytes);
        var modelInfo = GltfModelInspector.Inspect(gltfInput);
        var geometry = ReadGeometrySummary(export.DiagnosticsBytes);
        var usedTextureIds = terrain.Chunks
            .SelectMany(chunk => chunk.TextureEntries)
            .Select(entry => entry.TextureId)
            .Where(textureId => textureId >= 0)
            .Distinct()
            .Order()
            .ToArray();

        return new
        {
            Id = id,
            Label = $"{id} / {terrain.TfragCount} chunks",
            Game = gameId.ToString(),
            Status = "ok",
            SourceDirectory = CliPathUtils.ToUriPath(relativeSourceDirectory),
            SourceTfrag = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, tfragFile.FullName)),
            Gltf = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, gltfFile.FullName)),
            Buffer = CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, bufferFile.FullName)),
            Diagnostics = minify ? null : CliPathUtils.ToUriPath(Path.GetRelativePath(manifestDirectory.FullName, diagnosticsFile.FullName)),
            ConversionMs = conversionMs,
            InputBytes = tfragFile.Length,
            OutputBytes = export.GltfBytes.Length + export.BinBytes.Length + export.DiagnosticsBytes.Length,
            Header = new
            {
                terrain.TfragTableOffset,
                terrain.TfragCount,
                terrain.TotalTfragCount,
                terrain.TfragRadius
            },
            Geometry = new
            {
                modelInfo.MeshCount,
                modelInfo.PrimitiveCount,
                modelInfo.VertexCount,
                modelInfo.TriangleCount,
                TextureCount = modelInfo.TextureCount,
                geometry.LodChunkCounts,
                geometry.LodTriangleCounts
            },
            Chunks = new
            {
                Count = terrain.Chunks.Count,
                WithTextures = terrain.Chunks.Count(chunk => chunk.TextureEntries.Count > 0),
                BaseOnly = terrain.Chunks.Count(chunk => chunk.BaseOnly != 0),
                MaxTextureEntries = terrain.Chunks.Count == 0 ? 0 : terrain.Chunks.Max(chunk => chunk.TextureEntries.Count),
                TotalSourceTriangles = terrain.Chunks.Sum(chunk => chunk.TriangleCount),
                TotalSourceVertices = terrain.Chunks.Sum(chunk => chunk.VertexCount)
            },
            Textures = textureResources?.Entries ?? [],
            UsedTextureIds = usedTextureIds
        };
    }

    private static TfragBatchGeometrySummary ReadGeometrySummary(byte[] diagnosticsBytes)
    {
        if (diagnosticsBytes.Length == 0)
        {
            return new TfragBatchGeometrySummary(
                new SortedDictionary<string, int>(StringComparer.Ordinal),
                new SortedDictionary<string, int>(StringComparer.Ordinal));
        }

        using var document = JsonDocument.Parse(diagnosticsBytes);
        var geometry = document.RootElement.GetProperty("Geometry");
        return new TfragBatchGeometrySummary(
            ReadStringIntDictionary(geometry.GetProperty("LodChunkCounts")),
            ReadStringIntDictionary(geometry.GetProperty("LodTriangleCounts")));
    }

    private static SortedDictionary<string, int> ReadStringIntDictionary(JsonElement element)
    {
        var values = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = property.Value.GetInt32();
        }

        return values;
    }

    private static string NormalizeId(string relativeSourceDirectory, int index)
    {
        if (string.IsNullOrWhiteSpace(relativeSourceDirectory) || relativeSourceDirectory == ".")
        {
            return $"tfrag_{index:0000}";
        }

        return CliPathUtils.ToUriPath(relativeSourceDirectory);
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

    private sealed record TfragBatchGeometrySummary(
        IReadOnlyDictionary<string, int> LodChunkCounts,
        IReadOnlyDictionary<string, int> LodTriangleCounts);
}

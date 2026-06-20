using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;

namespace RatchetPs2.Cli.Commands.Skybox;

internal static class SkyboxExportGltfBatchCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputRootOption = new Option<DirectoryInfo>("--input-root")
        {
            Description = "Root directory to scan recursively for sky.bin files.",
            Required = true
        };
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write exported glTFs and the viewer manifest.",
            Required = true
        };
        var skyFileNameOption = new Option<string>("--sky-file-name")
        {
            Description = "Skybox binary file name to scan for. Defaults to sky.bin.",
            DefaultValueFactory = _ => "sky.bin"
        };
        var manifestNameOption = new Option<string>("--manifest-name")
        {
            Description = "Viewer manifest file name. Defaults to manifest.json.",
            DefaultValueFactory = _ => "manifest.json"
        };
        var limitOption = new Option<int?>("--limit")
        {
            Description = "Optional maximum number of skyboxes to export."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf-batch",
            "Export a directory of skybox binaries to glTF and write a viewer manifest.",
            gameOption,
            inputRootOption,
            outputRootOption,
            skyFileNameOption,
            manifestNameOption,
            limitOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputRoot = parseResult.GetValue(inputRootOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            var skyFileName = parseResult.GetValue(skyFileNameOption);
            var manifestName = parseResult.GetValue(manifestNameOption);
            var limit = parseResult.GetValue(limitOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {SkyboxGameFormats.SupportedSkyboxGames} for skybox glTF batch export.");
                return;
            }

            if (!SkyboxGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Skybox glTF batch export currently supports only {SkyboxGameFormats.SupportedSkyboxGames}. Received {gameId}.");
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

            if (string.IsNullOrWhiteSpace(skyFileName))
            {
                parseResult.GetResult(skyFileNameOption)?.AddError("--sky-file-name cannot be empty.");
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
            var skyFiles = inputRoot
                .EnumerateFiles(skyFileName, SearchOption.AllDirectories)
                .Where(file => !CliPathUtils.IsInsideDirectory(file, outputRoot))
                .OrderBy(file => Path.GetRelativePath(inputRoot.FullName, file.FullName), StringComparer.Ordinal)
                .Take(limit ?? int.MaxValue)
                .ToArray();

            var entries = new List<object>(skyFiles.Length);
            var totalStopwatch = Stopwatch.StartNew();
            var successfulDurations = new List<double>();
            var totalInputBytes = 0L;
            var totalOutputBytes = 0L;
            var succeeded = 0;
            var failed = 0;

            for (var i = 0; i < skyFiles.Length; i++)
            {
                var skyFile = skyFiles[i];
                var relativeSourceDirectory = Path.GetRelativePath(inputRoot.FullName, skyFile.DirectoryName ?? inputRoot.FullName);
                var id = SkyboxBatchManifestBuilder.NormalizeId(relativeSourceDirectory, i);
                var outputDirectory = new DirectoryInfo(Path.Combine(outputRoot.FullName, relativeSourceDirectory));
                outputDirectory.Create();

                var gltfFile = new FileInfo(Path.Combine(outputDirectory.FullName, "sky.gltf"));
                var itemStopwatch = Stopwatch.StartNew();

                try
                {
                    var levelNumber = SkyboxGameFormats.InferLevelNumber(relativeSourceDirectory);
                    var fileExport = SkyboxExportWriter.Export(skyFile, gltfFile, gameId, levelNumber);

                    itemStopwatch.Stop();
                    succeeded++;
                    successfulDurations.Add(itemStopwatch.Elapsed.TotalMilliseconds);
                    totalInputBytes += skyFile.Length;
                    totalOutputBytes += fileExport.OutputBytes;
                    entries.Add(SkyboxBatchManifestBuilder.BuildSuccessEntry(
                        id,
                        gameId,
                        skyFile,
                        fileExport,
                        manifestDirectory,
                        relativeSourceDirectory,
                        itemStopwatch.Elapsed.TotalMilliseconds));
                }
                catch (Exception ex)
                {
                    itemStopwatch.Stop();
                    failed++;
                    entries.Add(SkyboxBatchManifestBuilder.BuildFailureEntry(
                        id,
                        gameId,
                        skyFile,
                        manifestDirectory,
                        relativeSourceDirectory,
                        itemStopwatch.Elapsed.TotalMilliseconds,
                        ex));
                }

                if ((i + 1) % 25 == 0 || i + 1 == skyFiles.Length)
                {
                    Console.WriteLine(
                        $"Processed {i + 1}/{skyFiles.Length} skyboxes ({succeeded} ok, {failed} failed) in {totalStopwatch.Elapsed.TotalSeconds:F1}s.");
                }
            }

            totalStopwatch.Stop();
            var manifest = SkyboxBatchManifestBuilder.BuildManifest(
                gameId,
                inputRoot,
                outputRoot,
                skyFileName,
                skyFiles.Length,
                succeeded,
                failed,
                totalStopwatch.Elapsed,
                successfulDurations,
                totalInputBytes,
                totalOutputBytes,
                entries);

            File.WriteAllBytes(
                manifestFile.FullName,
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine(
                $"Wrote skybox batch manifest '{manifestFile.FullName}' with {succeeded} successful export(s), {failed} failure(s), total {totalStopwatch.Elapsed.TotalSeconds:F2}s.");
        });

        return command;
    }
}

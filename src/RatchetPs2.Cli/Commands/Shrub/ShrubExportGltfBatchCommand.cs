using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using System.CommandLine;
using System.Text.Json;

namespace RatchetPs2.Cli.Commands.Shrub;

internal static class ShrubExportGltfBatchCommand
{
    public static Command Build()
    {
        var gameOption = new Option<string>("--game")
        {
            Description = "Game id for every shrub, or auto to infer from sibling .fbx.meta labels. Defaults to auto.",
            DefaultValueFactory = _ => "auto"
        };
        var inputRootOption = new Option<DirectoryInfo>("--input-root")
        {
            Description = "Root directory to scan recursively for packed shrub binaries.",
            Required = true
        };
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write exported glTFs and the viewer manifest.",
            Required = true
        };
        var sourceKindOption = new Option<string>("--source-kind")
        {
            Description = "Shrub source kind: auto or packed. Defaults to auto.",
            DefaultValueFactory = _ => "auto"
        };
        var coreFileNameOption = new Option<string>("--core-file-name")
        {
            Description = "Packed shrub class binary file name to scan for. Defaults to core.bin.",
            DefaultValueFactory = _ => "core.bin"
        };
        var manifestNameOption = new Option<string>("--manifest-name")
        {
            Description = "Viewer manifest file name. Defaults to manifest.json.",
            DefaultValueFactory = _ => "manifest.json"
        };
        var limitOption = new Option<int?>("--limit")
        {
            Description = "Optional maximum number of shrubs to export."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf-batch",
            "Export a directory of shrubs to glTF and write a viewer manifest.",
            gameOption,
            inputRootOption,
            outputRootOption,
            sourceKindOption,
            coreFileNameOption,
            manifestNameOption,
            limitOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputRoot = parseResult.GetValue(inputRootOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            var sourceKindValue = parseResult.GetValue(sourceKindOption);
            var coreFileName = parseResult.GetValue(coreFileNameOption);
            var manifestName = parseResult.GetValue(manifestNameOption);
            var limit = parseResult.GetValue(limitOption);

            var inferGame = string.Equals(gameValue, "auto", StringComparison.OrdinalIgnoreCase);
            RatchetPs2.Core.Games.GameId? gameId = null;
            if (!inferGame)
            {
                if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var parsedGameId))
                {
                    parseResult.GetResult(gameOption)?.AddError(
                        $"Unsupported --game value '{gameValue}'. Expected auto or {ShrubGameFormats.SupportedShrubGames} for shrub glTF batch export.");
                    return;
                }

                gameId = parsedGameId;
            }

            if (gameId is { } explicitGameId && !ShrubGameFormats.IsSupported(explicitGameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Shrub glTF batch export currently supports only {ShrubGameFormats.SupportedShrubGames}. Received {explicitGameId}.");
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

            if (!ShrubSourceKindParser.TryParse(sourceKindValue, out var sourceKind))
            {
                parseResult.GetResult(sourceKindOption)?.AddError("--source-kind must be auto or packed.");
                return;
            }

            if (string.IsNullOrWhiteSpace(coreFileName))
            {
                parseResult.GetResult(coreFileNameOption)?.AddError("--core-file-name cannot be empty.");
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
            var exportResult = ShrubBatchExporter.Export(
                new ShrubBatchExportOptions(
                    gameId,
                    inputRoot,
                    outputRoot,
                    manifestDirectory,
                    sourceKind,
                    coreFileName!,
                    limit),
                Console.WriteLine);
            var manifest = ShrubBatchManifestBuilder.BuildManifest(
                inputRoot,
                outputRoot,
                exportResult);

            File.WriteAllBytes(
                manifestFile.FullName,
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine(
                $"Wrote shrub batch manifest '{manifestFile.FullName}' with {exportResult.Succeeded} successful export(s), {exportResult.Failed} failure(s), total {exportResult.TotalElapsed.TotalSeconds:F2}s.");
        });

        return command;
    }
}

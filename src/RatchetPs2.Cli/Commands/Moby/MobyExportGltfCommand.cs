using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the UYA moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export a UYA moby model to glTF geometry.",
            gameOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA for glTF export.");
                return;
            }

            if (gameId != GameId.UYA)
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby glTF export currently supports only UYA. Received {gameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            outputFile.Directory?.Create();
            using var input = inputFile.OpenRead();
            var export = UyaMobyGltfExporter.Export(input, outputFile.Name);

            var binFile = Path.ChangeExtension(outputFile.FullName, ".bin");
            var diagnosticsFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.diagnostics.json");

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(binFile, export.BinBytes);
            File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);

            Console.WriteLine(
                $"Exported UYA moby glTF '{inputFile.FullName}' to '{outputFile.FullName}'.");
        });

        return command;
    }
}

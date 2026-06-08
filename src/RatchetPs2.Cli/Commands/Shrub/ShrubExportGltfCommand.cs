using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Shrub;

internal static class ShrubExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to a packed shrub class binary, such as core.bin.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var textureDirectoryOption = new Option<DirectoryInfo?>("--texture-directory")
        {
            Description = "Directory containing external packed-shrub PNG textures. Defaults to the input shrub's directory."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export shrub geometry to a glTF model.",
            gameOption,
            inputOption,
            outputOption,
            textureDirectoryOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var textureDirectory = parseResult.GetValue(textureDirectoryOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {ShrubGameFormats.SupportedShrubGames} for shrub glTF export.");
                return;
            }

            if (!ShrubGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Shrub glTF export currently supports only {ShrubGameFormats.SupportedShrubGames}. Received {gameId}.");
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

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            var fileExport = ShrubExportWriter.Export(inputFile, outputFile, gameId, textureDirectory);
            Console.WriteLine(
                $"Exported {gameId} shrub glTF '{inputFile.FullName}' to '{outputFile.FullName}' ({fileExport.ModelInfo.TriangleCount} triangle(s), {fileExport.Textures.Count} texture(s)).");
        });

        return command;
    }
}

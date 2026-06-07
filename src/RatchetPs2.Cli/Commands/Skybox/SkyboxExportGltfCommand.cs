using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Skybox;

internal static class SkyboxExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the sky.bin binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var preserveAlphaOption = new Option<bool>("--preserve-alpha")
        {
            Description = "Preserve raw PS2 palette alpha and skip skybox RGB edge cleanup."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export skybox geometry to a glTF model.",
            gameOption,
            inputOption,
            outputOption,
            preserveAlphaOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var preserveAlpha = parseResult.GetValue(preserveAlphaOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {SkyboxGameFormats.SupportedSkyboxGames} for skybox glTF export.");
                return;
            }

            if (!SkyboxGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Skybox glTF export currently supports only {SkyboxGameFormats.SupportedSkyboxGames}. Received {gameId}.");
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

            var levelNumber = SkyboxGameFormats.InferLevelNumber(inputFile.FullName);
            var fileExport = SkyboxExportWriter.Export(inputFile, outputFile, gameId, preserveAlpha, levelNumber);

            Console.WriteLine(
                $"Exported {gameId} skybox glTF '{inputFile.FullName}' to '{outputFile.FullName}' with {fileExport.Export.Textures.Count} texture(s).");
        });

        return command;
    }
}

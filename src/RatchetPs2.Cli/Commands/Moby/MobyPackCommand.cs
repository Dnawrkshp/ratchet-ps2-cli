using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyPackCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = new Option<DirectoryInfo>("--input")
        {
            Description = "Path to the unpacked loose UYA moby model directory.",
            Required = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the packed moby model binary.");

        var command = CliCommandBuilder.Create(
            "pack",
            "Pack loose moby model definition files into a binary model.",
            gameOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputDirectory = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA for moby pack.");
                return;
            }

            if (gameId != GameId.UYA)
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby pack currently supports only UYA. Received {gameId}.");
                return;
            }

            if (inputDirectory is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            if (!inputDirectory.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input directory '{inputDirectory.FullName}' does not exist.");
                return;
            }

            var input = new FileSystemMobyModelInput(inputDirectory.FullName);
            var bytes = UyaMobyModelPacker.Pack(input);

            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);

            Console.WriteLine(
                $"Packed UYA moby '{inputDirectory.FullName}' to '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }
}

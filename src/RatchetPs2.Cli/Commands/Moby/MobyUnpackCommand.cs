using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyUnpackCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the UYA moby model binary.");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Path to the output directory for loose moby model files.",
            Required = true
        };

        var command = CliCommandBuilder.Create(
            "unpack",
            "Unpack a moby model into loose binary definition files.",
            gameOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputDirectory = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA for moby unpack.");
                return;
            }

            if (gameId != GameId.UYA)
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby unpack currently supports only UYA. Received {gameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputDirectory is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            outputDirectory.Create();

            using var input = inputFile.OpenRead();
            var output = new FileSystemMobyModelOutput(outputDirectory.FullName);
            var model = UyaMobyModelUnpacker.Unpack(input, output);

            Console.WriteLine(
                $"Unpacked UYA moby '{inputFile.FullName}' to '{outputDirectory.FullName}' " +
                $"({model.MeshTable?.Entries.Count ?? 0} meshes, {model.Sequences.Count} animations).");
        });

        return command;
    }
}

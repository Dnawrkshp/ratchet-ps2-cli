using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyRepackBinCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the source moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the repacked moby model binary.");

        var command = CliCommandBuilder.Create(
            "repack-bin",
            "Read a moby binary and write it back through the model packer.",
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
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL for moby repack-bin.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby repack-bin currently supports only UYA and DL. Received {gameId}.");
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

            var format = MobyGameFormats.Resolve(gameModuleResolver, gameId);

            using var input = inputFile.OpenRead();
            var model = MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    AnimationFormat = format
                });

            var bytes = MobyModelPacker.Build(model);
            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);

            Console.WriteLine(
                $"Repacked {gameId} moby '{inputFile.FullName}' to '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }
}

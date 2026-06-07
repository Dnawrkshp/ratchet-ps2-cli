using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tie;

internal static class TieInspectCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the tie.bin class binary.");
        var outputOption = new Option<FileInfo?>("--output")
        {
            Description = "Optional path to write the structural report instead of printing only to stdout."
        };

        var command = CliCommandBuilder.Create(
            "inspect",
            "Inspect a tie class binary and dump its currently understood structure.",
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
                    $"Unsupported --game value '{gameValue}'. Expected {TieGameFormats.SupportedTieGames} for tie inspect.");
                return;
            }

            if (!TieGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Tie inspect currently supports only {TieGameFormats.SupportedTieGames}. Received {gameId}.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            using var input = inputFile.OpenRead();
            var gameProfile = TieGameProfile.ForGame(gameId);
            var tie = TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(gameProfile));
            var report = TieClassDescriber.Describe(tie);

            if (outputFile is not null)
            {
                outputFile.Directory?.Create();
                File.WriteAllText(outputFile.FullName, report);
            }

            Console.WriteLine(report);
        });

        return command;
    }
}

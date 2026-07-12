using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExtractCustomCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the custom map zip.");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Directory root to write the extracted custom map package. A child folder is created from the zip file name.",
            Required = true
        };

        var command = CliCommandBuilder.Create(
            "extract-custom",
            "Extract a custom map zip into a rebuild-oriented package.",
            gameOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputRoot = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                Console.Error.WriteLine(
                    $"Unsupported --game value '{gameValue}'. Custom map extraction currently supports UYA.");
                return 1;
            }

            if (gameId != GameId.UYA)
            {
                Console.Error.WriteLine("Custom map extraction currently supports only --game UYA.");
                return 1;
            }

            if (inputFile is null)
            {
                Console.Error.WriteLine("Missing required --input option.");
                return 1;
            }

            if (outputRoot is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            try
            {
                var summary = UyaMapExtractionWriter.ExtractCustomZip(inputFile, outputRoot);
                Console.WriteLine(
                    $"Extracted UYA custom map '{inputFile.FullName}' to '{summary.OutputDirectory}' ({summary.FileCount} files).");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Custom map extraction failed: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}

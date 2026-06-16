using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExtractCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the DL game ISO.");
        var levelOption = new Option<int>("--level")
        {
            Description = "DL level index to extract.",
            Required = true
        };
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Directory to write the extracted rebuild package.",
            Required = true
        };

        var command = CliCommandBuilder.Create(
            "extract",
            "Extract a DL map from a game ISO into a rebuild-oriented package.",
            gameOption,
            inputOption,
            levelOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var level = parseResult.GetValue(levelOption);
            var outputDirectory = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                Console.Error.WriteLine(
                    $"Unsupported --game value '{gameValue}'. Map extraction currently supports DL only.");
                return 1;
            }

            if (gameId != GameId.DL)
            {
                Console.Error.WriteLine("Map extraction currently supports only --game DL.");
                return 1;
            }

            if (inputFile is null)
            {
                Console.Error.WriteLine("Missing required --input option.");
                return 1;
            }

            if (outputDirectory is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            try
            {
                var summary = DlMapExtractionWriter.Extract(inputFile, level, outputDirectory);
                Console.WriteLine(
                    $"Extracted DL level {level} to '{summary.OutputDirectory}' ({summary.CoreSegmentCount} core segments, {summary.TextureCount} textures).");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Map extraction failed: {ex.Message}");
                return 1;
            }
        });

        return command;
    }
}

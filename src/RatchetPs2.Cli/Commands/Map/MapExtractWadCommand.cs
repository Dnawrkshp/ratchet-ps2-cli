using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL.Level;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExtractWadCommand
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
        var outputOption = CommonOptions.OutputFile("Path to write the loose level WAD.");

        var command = CliCommandBuilder.Create(
            "extract-wad",
            "Extract a self-contained primary DL level WAD from a game ISO.",
            gameOption,
            inputOption,
            levelOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var level = parseResult.GetValue(levelOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (!TryValidateDlGame(gameValue, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            if (inputFile is null)
            {
                Console.Error.WriteLine("Missing required --input option.");
                return 1;
            }

            if (outputFile is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            try
            {
                outputFile.Directory?.Create();
                using var isoStream = inputFile.OpenRead();
                var looseWad = DlLooseLevelWadExtractor.ExtractPrimary(isoStream, level);
                File.WriteAllBytes(outputFile.FullName, looseWad.Bytes);
                Console.WriteLine(
                    $"Extracted DL level {level} WAD to '{outputFile.FullName}' ({looseWad.SectorCount} sectors, {looseWad.ByteLength} bytes, header sector 0x{looseWad.HeaderSector:X}, payload base 0x{looseWad.PayloadBaseSector:X}).");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Map WAD extraction failed: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static bool TryValidateDlGame(string? gameValue, out string error)
    {
        if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
        {
            error = $"Unsupported --game value '{gameValue}'. Map WAD extraction currently supports DL only.";
            return false;
        }

        if (gameId != GameId.DL)
        {
            error = "Map WAD extraction currently supports only --game DL.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

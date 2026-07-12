using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.UYA.Level;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapExtractWadCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the game ISO.");
        var levelOption = new Option<int>("--level")
        {
            Description = "Level index to extract.",
            Required = true
        };
        var outputOption = CommonOptions.OutputFile("Path to write the loose level WAD.");

        var command = CliCommandBuilder.Create(
            "extract-wad",
            "Extract a self-contained primary level WAD from a game ISO.",
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

            if (!TryValidateMapGame(gameValue, out var gameId, out var error))
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

                if (gameId == GameId.UYA)
                {
                    var uyaLooseWad = UyaLooseLevelWadExtractor.ExtractPrimary(isoStream, level);
                    File.WriteAllBytes(outputFile.FullName, uyaLooseWad.Bytes);
                    Console.WriteLine(
                        $"Extracted UYA level {level} WAD to '{outputFile.FullName}' ({uyaLooseWad.SectorCount} sectors, {uyaLooseWad.ByteLength} bytes, header sector 0x{uyaLooseWad.HeaderSector:X}, payload base 0x{uyaLooseWad.PayloadBaseSector:X}).");
                    return 0;
                }

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

    private static bool TryValidateMapGame(string? gameValue, out GameId gameId, out string error)
    {
        gameId = default;

        if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out gameId))
        {
            error = $"Unsupported --game value '{gameValue}'. Map WAD extraction currently supports UYA and DL.";
            return false;
        }

        if (gameId is not (GameId.UYA or GameId.DL))
        {
            error = "Map WAD extraction currently supports only --game UYA or --game DL.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

using System.CommandLine;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Games.DL.Armor;

namespace RatchetPs2.Cli.Commands.Armor;

internal static class ArmorExtractWadCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to the DL game ISO containing the global armor WAD.");
        var outputOption = CommonOptions.OutputFile("Path to write the self-contained armor WAD.");
        var command = CliCommandBuilder.Create(
            "extract-wad",
            "Extract the global player armor WAD from a DL ISO.",
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            if (inputFile is null || !inputFile.Exists)
            {
                Console.Error.WriteLine($"Input ISO '{inputFile?.FullName}' does not exist.");
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
                using var output = outputFile.Create();
                var extraction = DlArmorWadReader.ExtractWadFromIso(isoStream, output);
                Console.WriteLine(
                    $"Extracted DL armor WAD with {extraction.PayloadSectorCount} payload sectors from '{inputFile.FullName}' to '{outputFile.FullName}'.");
                return 0;
            }
            catch (Exception ex) when (IsExtractionFailure(ex))
            {
                Console.Error.WriteLine($"Failed to extract DL armor WAD: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static bool IsExtractionFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException
            or OverflowException
            or UnauthorizedAccessException;
    }
}

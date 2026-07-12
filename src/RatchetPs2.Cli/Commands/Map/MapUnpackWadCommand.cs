using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.UYA.Level;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Map;

internal static class MapUnpackWadCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the raw loose level WAD.");
        var outputOption = new Option<DirectoryInfo>("--output")
        {
            Description = "Directory to write unpacked files or indexed package output.",
            Required = true
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Output format: files or indexed.",
            DefaultValueFactory = _ => "files"
        };

        var command = CliCommandBuilder.Create(
            "unpack-wad",
            "Unpack a raw loose level WAD into files or an IndexedDB-friendly packed index.",
            gameOption,
            inputOption,
            outputOption,
            formatOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputDirectory = parseResult.GetValue(outputOption);
            var format = parseResult.GetValue(formatOption);

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

            if (outputDirectory is null)
            {
                Console.Error.WriteLine("Missing required --output option.");
                return 1;
            }

            var normalizedFormat = (format ?? "files").Trim().ToLowerInvariant();
            if (normalizedFormat is not "files" and not "indexed")
            {
                Console.Error.WriteLine($"Unsupported --format value '{format}'. Expected files or indexed.");
                return 1;
            }

            try
            {
                var bytes = File.ReadAllBytes(inputFile.FullName);

                if (gameId == GameId.UYA)
                {
                    var uyaPackage = UyaLevelWadUnpacker.Unpack(bytes);
                    if (normalizedFormat == "indexed")
                    {
                        PackedFilePackageWriter.WriteIndexed(uyaPackage.ToPackedPackage(), outputDirectory);
                        Console.WriteLine(
                            $"Unpacked UYA level WAD '{inputFile.FullName}' to indexed package '{outputDirectory.FullName}' ({uyaPackage.Files.Count} entries).");
                    }
                    else
                    {
                        PackedFilePackageWriter.WriteFiles(uyaPackage.Files, outputDirectory);
                        Console.WriteLine(
                            $"Unpacked UYA level WAD '{inputFile.FullName}' to '{outputDirectory.FullName}' ({uyaPackage.Files.Count} files).");
                    }

                    return 0;
                }

                var package = DlLevelWadUnpacker.Unpack(bytes);
                if (normalizedFormat == "indexed")
                {
                    PackedFilePackageWriter.WriteIndexed(package.ToPackedPackage(), outputDirectory);
                    Console.WriteLine(
                        $"Unpacked DL level WAD '{inputFile.FullName}' to indexed package '{outputDirectory.FullName}' ({package.Files.Count} entries).");
                }
                else
                {
                    PackedFilePackageWriter.WriteFiles(package, outputDirectory);
                    Console.WriteLine(
                        $"Unpacked DL level WAD '{inputFile.FullName}' to '{outputDirectory.FullName}' ({package.Files.Count} files).");
                }

                return 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                Console.Error.WriteLine($"Map WAD unpack failed: {ex.Message}");
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
            error = $"Unsupported --game value '{gameValue}'. Map WAD unpack currently supports UYA and DL.";
            return false;
        }

        if (gameId is not (GameId.UYA or GameId.DL))
        {
            error = "Map WAD unpack currently supports only --game UYA or --game DL.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

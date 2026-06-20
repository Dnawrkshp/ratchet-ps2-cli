using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Ties;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tie;

internal static class TieExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the tie.bin class binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var lodOption = new Option<int>("--lod")
        {
            Description = "Tie LOD packet group to export: 0, 1, or 2. Defaults to 0."
        };
        var textureDirectoryOption = new Option<DirectoryInfo?>("--texture-directory")
        {
            Description = "Directory containing external tie PNG textures to reference from the exported glTF. Supports Wrench numeric PNG names and tex.####.0.png names. Defaults to the input tie's directory when matching PNGs are present."
        };
        var minifyOption = CommonOptions.MinifyGltf();

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export tie geometry to a glTF model.",
            gameOption,
            inputOption,
            outputOption,
            lodOption,
            textureDirectoryOption,
            minifyOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var lodIndex = parseResult.GetValue(lodOption);
            var textureDirectory = parseResult.GetValue(textureDirectoryOption);
            var minify = parseResult.GetValue(minifyOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {TieGameFormats.SupportedTieGames} for tie glTF export.");
                return;
            }

            if (!TieGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Tie glTF export currently supports only {TieGameFormats.SupportedTieGames}. Received {gameId}.");
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

            if (lodIndex is < 0 or > 2)
            {
                parseResult.GetResult(lodOption)?.AddError(
                    $"Unsupported --lod value '{lodIndex}'. Expected 0, 1, or 2.");
                return;
            }

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            outputFile.Directory?.Create();
            var binFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.buffer.bin");
            var diagnosticsFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.diagnostics.json");
            var outputDirectory = outputFile.Directory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var textureResources = TieTextureResourcePreparer.PrepareExternalTextures(
                textureDirectory ?? inputFile.Directory,
                outputDirectory);

            using var input = inputFile.OpenRead();
            var export = TieGltfExporter.Export(
                input,
                outputFile.Name,
                new TieGltfExportOptions
                {
                    LodIndex = lodIndex,
                    BufferFileName = Path.GetFileName(binFile),
                    GameProfile = TieGameProfile.ForGame(gameId),
                    ExternalTextureUris = textureResources?.Uris,
                    ExternalTextureSizes = textureResources?.Sizes,
                    ExternalTextureAlpha = textureResources?.Alpha,
                    IncludeDiagnostics = !minify,
                    Minify = minify,
                    MetadataMode = minify ? GltfExportMetadataMode.RuntimeOnly : GltfExportMetadataMode.Full
                });

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(binFile, export.BinBytes);
            if (export.DiagnosticsBytes.Length > 0)
            {
                File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);
            }

            Console.WriteLine(
                $"Exported {gameId} tie LOD {lodIndex} glTF '{inputFile.FullName}' to '{outputFile.FullName}'.");
        });

        return command;
    }
}

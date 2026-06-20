using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Tfrags;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tfrag;

internal static class TfragExportGltfCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to a tfrag.bin binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var textureDirectoryOption = new Option<DirectoryInfo?>("--texture-directory")
        {
            Description = "Directory containing external tfrag PNG textures to reference from the exported glTF. Supports tex.####.0.png names. Defaults to the input tfrag directory when matching PNGs are present."
        };
        var minifyOption = CommonOptions.MinifyGltf();

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export tfrag terrain geometry to a glTF model grouped by chunk and LOD.",
            gameOption,
            inputOption,
            outputOption,
            textureDirectoryOption,
            minifyOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var textureDirectory = parseResult.GetValue(textureDirectoryOption);
            var minify = parseResult.GetValue(minifyOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {TfragGameFormats.SupportedTfragGames} for tfrag glTF export.");
                return;
            }

            if (!TfragGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Tfrag glTF export currently supports only {TfragGameFormats.SupportedTfragGames}. Received {gameId}.");
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

            outputFile.Directory?.Create();
            var binFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.buffer.bin");
            var diagnosticsFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.diagnostics.json");
            var outputDirectory = outputFile.Directory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
            var textureResources = TextureResourcePreparer.PrepareExternalTextures(
                textureDirectory ?? inputFile.Directory,
                outputDirectory,
                normalizePs2FullOpacityAlpha: TfragTextureAlpha.FullOpacityAlpha);

            using var input = inputFile.OpenRead();
            var export = TfragGltfExporter.Export(
                input,
                outputFile.Name,
                new TfragGltfExportOptions
                {
                    BufferFileName = Path.GetFileName(binFile),
                    GameLabel = gameId.ToString(),
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
                $"Exported {gameId} tfrag glTF '{inputFile.FullName}' to '{outputFile.FullName}'.");
        });

        return command;
    }
}

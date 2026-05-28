using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyExportGltfCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var inputOption = CommonOptions.InputFile("Path to the source moby model binary.");
        var outputOption = CommonOptions.OutputFile("Path to write the exported .gltf file.");
        var textureDirectoryOption = new Option<DirectoryInfo?>("--texture-directory")
        {
            Description = "Directory containing external tex.####.0.png files to reference from the exported glTF. Defaults to the input moby's directory when matching PNGs are present."
        };
        var debugUvColorsOption = new Option<bool>("--debug-uv-colors")
        {
            Description = "Add a vertex-color checker pattern derived from TEXCOORD_0 for offline UV inspection."
        };
        var skipAnimationsOption = new Option<bool>("--skip-animations")
        {
            Description = "Skip animation sequence parsing. Useful for DL skin cores where only mesh export is needed."
        };
        var lowLodTextureModeOption = new Option<string>("--low-lod-texture-mode")
        {
            Description = "Choose how low_lod glTF materials are assigned: rolling, explicit-only, high-lod-overlap, high-lod-nearest-center, or high-lod-nearest-triangle.",
            DefaultValueFactory = _ => "rolling"
        };
        var meshTextureOverridesOption = new Option<string?>("--mesh-texture-overrides")
        {
            Description = "Override exported glTF material texture IDs by moby mesh index, for example 30=0,34=1."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf",
            "Export a moby model to glTF geometry.",
            gameOption,
            inputOption,
            outputOption,
            textureDirectoryOption,
            debugUvColorsOption,
            skipAnimationsOption,
            lowLodTextureModeOption,
            meshTextureOverridesOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);
            var textureDirectory = parseResult.GetValue(textureDirectoryOption);
            var debugUvColors = parseResult.GetValue(debugUvColorsOption);
            var skipAnimations = parseResult.GetValue(skipAnimationsOption);
            var lowLodTextureModeValue = parseResult.GetValue(lowLodTextureModeOption);
            var meshTextureOverridesValue = parseResult.GetValue(meshTextureOverridesOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL for glTF export.");
                return;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby glTF export currently supports only UYA and DL. Received {gameId}.");
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

            if (!TryParseLowLodTextureMode(lowLodTextureModeValue, out var lowLodTextureMode))
            {
                parseResult.GetResult(lowLodTextureModeOption)?.AddError(
                    $"Unsupported low LOD texture mode '{lowLodTextureModeValue}'. Expected rolling, explicit-only, high-lod-overlap, high-lod-nearest-center, or high-lod-nearest-triangle.");
                return;
            }

            if (!TryParseMeshTextureOverrides(meshTextureOverridesValue, out var meshTextureOverrides, out var meshTextureOverridesError))
            {
                parseResult.GetResult(meshTextureOverridesOption)?.AddError(meshTextureOverridesError);
                return;
            }

            outputFile.Directory?.Create();
            var textureUris = PrepareExternalTextureUris(textureDirectory ?? inputFile.Directory, outputFile);
            using var input = inputFile.OpenRead();
            var binFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.buffer.bin");

            var export = MobyGltfExporter.Export(
                input,
                outputFile.Name,
                new MobyGltfExportOptions
                {
                    IncludeDebugUvColors = debugUvColors,
                    SkipAnimationSequences = skipAnimations,
                    AnimationFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId),
                    ExternalTextureUris = textureUris,
                    LowLodTextureMode = lowLodTextureMode,
                    MeshTextureOverrides = meshTextureOverrides,
                    BufferFileName = Path.GetFileName(binFile)
                });

            var diagnosticsFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.diagnostics.json");

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(binFile, export.BinBytes);
            File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);

            Console.WriteLine(
                $"Exported {gameId} moby glTF '{inputFile.FullName}' to '{outputFile.FullName}'.");
        });

        return command;
    }

    private static bool TryParseLowLodTextureMode(string? value, out MobyGltfLowLodTextureMode mode)
    {
        mode = MobyGltfLowLodTextureMode.Rolling;
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "rolling" => true,
            "explicit" or "explicit-only" => SetMode(MobyGltfLowLodTextureMode.ExplicitOnly, out mode),
            "high-lod-overlap" or "overlap" => SetMode(MobyGltfLowLodTextureMode.HighLodOverlap, out mode),
            "high-lod-nearest-center" or "nearest-center" or "center" => SetMode(MobyGltfLowLodTextureMode.HighLodNearestCenter, out mode),
            "high-lod-nearest-triangle" or "nearest-triangle" or "triangle" => SetMode(MobyGltfLowLodTextureMode.HighLodNearestTriangle, out mode),
            _ => false
        };
    }

    private static bool SetMode(MobyGltfLowLodTextureMode value, out MobyGltfLowLodTextureMode mode)
    {
        mode = value;
        return true;
    }

    private static bool TryParseMeshTextureOverrides(
        string? value,
        out IReadOnlyDictionary<int, int>? meshTextureOverrides,
        out string error)
    {
        meshTextureOverrides = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var overrides = new Dictionary<int, int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2)
            {
                error = $"Invalid mesh texture override '{part}'. Expected meshIndex=textureId.";
                return false;
            }

            if (!int.TryParse(pieces[0], out var meshIndex) || meshIndex < 0)
            {
                error = $"Invalid mesh index '{pieces[0]}' in override '{part}'.";
                return false;
            }

            if (!int.TryParse(pieces[1], out var textureId) || textureId < 0)
            {
                error = $"Invalid texture ID '{pieces[1]}' in override '{part}'.";
                return false;
            }

            overrides[meshIndex] = textureId;
        }

        meshTextureOverrides = overrides;
        return true;
    }

    private static IReadOnlyDictionary<int, string>? PrepareExternalTextureUris(DirectoryInfo? sourceDirectory, FileInfo outputFile)
    {
        if (sourceDirectory is null || !sourceDirectory.Exists)
        {
            return null;
        }

        var candidates = sourceDirectory
            .EnumerateFiles("tex.*.0.png", SearchOption.TopDirectoryOnly)
            .Select(file => (File: file, TextureId: TryParseTextureId(file.Name, out var textureId) ? textureId : (int?)null))
            .Where(item => item.TextureId.HasValue)
            .OrderBy(item => item.TextureId!.Value)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var outputDirectory = outputFile.Directory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        var textureOutputDirectory = new DirectoryInfo(Path.Combine(outputDirectory.FullName, "textures"));
        textureOutputDirectory.Create();

        var uris = new Dictionary<int, string>();
        foreach (var (sourceFile, textureId) in candidates)
        {
            var destinationFile = new FileInfo(Path.Combine(textureOutputDirectory.FullName, sourceFile.Name));
            if (!Path.GetFullPath(sourceFile.FullName).Equals(Path.GetFullPath(destinationFile.FullName), StringComparison.Ordinal))
            {
                sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
            }

            uris[textureId!.Value] = ToGltfUri(Path.GetRelativePath(outputDirectory.FullName, destinationFile.FullName));
        }

        return uris;
    }

    private static bool TryParseTextureId(string fileName, out int textureId)
    {
        textureId = 0;
        var parts = fileName.Split('.');
        return parts.Length == 4
            && parts[0] == "tex"
            && parts[2] == "0"
            && parts[3].Equals("png", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[1], out textureId)
            && textureId >= 0;
    }

    private static string ToGltfUri(string relativePath)
    {
        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}

using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static IReadOnlyList<GltfExportRoute> ExportAssetGltfs(
        string outputDirectory,
        int levelIndex,
        IReadOnlyList<DlAssetModelDefinition> mobyDefinitions,
        IReadOnlyList<DlAssetModelDefinition> tieDefinitions,
        IReadOnlyList<DlAssetShrubDefinition> shrubDefinitions)
    {
        var routes = new List<GltfExportRoute>();

        routes.Add(ExportSkyboxGltf(outputDirectory, levelIndex));
        routes.Add(ExportTfragGltf(outputDirectory));
        routes.AddRange(ExportChunkTfragGltfs(outputDirectory));
        routes.AddRange(ExportModelFamilyGltfs(outputDirectory, "moby", mobyDefinitions));
        routes.AddRange(ExportModelFamilyGltfs(outputDirectory, "tie", tieDefinitions));
        routes.AddRange(ExportShrubGltfs(outputDirectory, shrubDefinitions));

        return routes;
    }

    private static GltfExportRoute ExportSkyboxGltf(string outputDirectory, int levelIndex)
    {
        var inputFile = new FileInfo(CombineRelativePath(outputDirectory, SkyboxSourcePath));
        var outputFile = new FileInfo(CombineRelativePath(outputDirectory, SkyboxGltfPath));
        PrepareGltfOutput(outputFile);

        if (!inputFile.Exists || inputFile.Length == 0)
        {
            return GltfExportRoute.Empty("skybox", null, SkyboxSourcePath, SkyboxGltfPath);
        }

        try
        {
            var export = SkyboxExportWriter.Export(inputFile, outputFile, GameId.DL, levelIndex);
            return GltfExportRoute.Written(
                "skybox",
                null,
                SkyboxSourcePath,
                SkyboxGltfPath,
                ToRelativeAssetPath(outputDirectory, export.BufferFile.FullName),
                export.DiagnosticsFile is null
                    ? null
                    : ToRelativeAssetPath(outputDirectory, export.DiagnosticsFile.FullName));
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("skybox", null, SkyboxSourcePath, SkyboxGltfPath, ex.Message);
        }
    }

    private static GltfExportRoute ExportTfragGltf(string outputDirectory)
    {
        return ExportTfragGltf(
            outputDirectory,
            null,
            "tfrag/tfrag.bin",
            "tfrag/tfrag.gltf",
            new DirectoryInfo(Path.Combine(outputDirectory, "tfrag", "textures")));
    }

    private static GltfExportRoute ExportTfragGltf(
        string outputDirectory,
        int? modelId,
        string sourcePath,
        string gltfPath,
        DirectoryInfo textureDirectory)
    {
        var inputFile = new FileInfo(CombineRelativePath(outputDirectory, sourcePath));
        var outputFile = new FileInfo(CombineRelativePath(outputDirectory, gltfPath));
        PrepareGltfOutput(outputFile);

        if (!inputFile.Exists || inputFile.Length == 0)
        {
            return GltfExportRoute.Empty("tfrag", modelId, sourcePath, gltfPath);
        }

        try
        {
            var outputDirectoryInfo = outputFile.Directory ?? new DirectoryInfo(outputDirectory);
            var bufferFile = Path.Combine(outputDirectoryInfo.FullName, "tfrag.buffer.bin");
            var diagnosticsFile = Path.Combine(outputDirectoryInfo.FullName, "tfrag.diagnostics.json");
            var textureResources = TextureResourcePreparer.PrepareExternalTextures(
                textureDirectory,
                outputDirectoryInfo);

            using var input = inputFile.OpenRead();
            var export = TfragGltfExporter.Export(
                input,
                outputFile.Name,
                new TfragGltfExportOptions
                {
                    BufferFileName = Path.GetFileName(bufferFile),
                    GameLabel = GameId.DL.ToString(),
                    ExternalTextureUris = textureResources?.Uris,
                    ExternalTextureSizes = textureResources?.Sizes,
                    ExternalTextureAlpha = textureResources?.Alpha
                });

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(bufferFile, export.BinBytes);
            File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);

            return GltfExportRoute.Written(
                "tfrag",
                modelId,
                sourcePath,
                gltfPath,
                ToRelativeAssetPath(outputDirectory, bufferFile),
                ToRelativeAssetPath(outputDirectory, diagnosticsFile));
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("tfrag", modelId, sourcePath, gltfPath, ex.Message);
        }
    }

    private static IEnumerable<GltfExportRoute> ExportChunkTfragGltfs(string outputDirectory)
    {
        var chunksDirectory = new DirectoryInfo(Path.Combine(outputDirectory, "level_wad", "chunks"));
        if (!chunksDirectory.Exists)
        {
            yield break;
        }

        foreach (var chunkFile in chunksDirectory.EnumerateFiles("chunk*.wad").OrderBy(file => file.Name))
        {
            if (!TryGetChunkIndex(chunkFile.Name, out var chunkIndex) || chunkIndex == 0)
            {
                continue;
            }

            var relativeDirectory = $"tfrag/chunks/chunk{chunkIndex}";
            var sourcePath = $"{relativeDirectory}/tfrag.bin";
            var gltfPath = $"{relativeDirectory}/tfrag.gltf";
            GltfExportRoute? failedRoute = null;
            try
            {
                var tfragBytes = TfragChunkWadReader.ReadTerrainPayload(File.ReadAllBytes(chunkFile.FullName));
                var sourceFile = CombineRelativePath(outputDirectory, sourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                File.WriteAllBytes(sourceFile, tfragBytes);
            }
            catch (Exception ex) when (IsGltfExportFailure(ex) || ex is OverflowException)
            {
                failedRoute = GltfExportRoute.Failed("tfrag", chunkIndex, sourcePath, gltfPath, ex.Message);
            }

            if (failedRoute is not null)
            {
                yield return failedRoute;
                continue;
            }

            yield return ExportTfragGltf(
                outputDirectory,
                chunkIndex,
                sourcePath,
                gltfPath,
                new DirectoryInfo(Path.Combine(outputDirectory, "tfrag", "textures")));
        }
    }

    private static bool TryGetChunkIndex(string fileName, out int chunkIndex)
    {
        chunkIndex = 0;
        const string prefix = "chunk";
        const string suffix = ".wad";
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(fileName[prefix.Length..^suffix.Length], out chunkIndex);
    }

    private static IEnumerable<GltfExportRoute> ExportModelFamilyGltfs(
        string outputDirectory,
        string family,
        IReadOnlyList<DlAssetModelDefinition> modelDefinitions)
    {
        foreach (var definition in modelDefinitions)
        {
            var folderName = DlAssetReader.GetAssetFolderName(definition.ModelId);
            var relativeDirectory = $"{family}/{folderName}";
            var inputPath = $"{relativeDirectory}/{family}.bin";
            var gltfPath = $"{relativeDirectory}/{family}.gltf";
            var modelDirectory = CombineRelativePath(outputDirectory, relativeDirectory);
            var inputFile = new FileInfo(Path.Combine(modelDirectory, $"{family}.bin"));
            var outputFile = new FileInfo(Path.Combine(modelDirectory, $"{family}.gltf"));

            PrepareGltfOutput(outputFile);
            if (!inputFile.Exists || inputFile.Length == 0)
            {
                yield return GltfExportRoute.Empty(family, definition.ModelId, inputPath, gltfPath);
                continue;
            }

            yield return family == "moby"
                ? ExportMobyGltf(outputDirectory, definition.ModelId, inputFile, outputFile, inputPath, gltfPath)
                : ExportTieGltf(outputDirectory, definition.ModelId, inputFile, outputFile, inputPath, gltfPath);
        }
    }

    private static GltfExportRoute ExportMobyGltf(
        string outputDirectory,
        int modelId,
        FileInfo inputFile,
        FileInfo outputFile,
        string inputPath,
        string gltfPath)
    {
        try
        {
            var outputDirectoryInfo = outputFile.Directory ?? new DirectoryInfo(outputDirectory);
            var bufferFile = Path.Combine(outputDirectoryInfo.FullName, "moby.buffer.bin");
            var diagnosticsFile = Path.Combine(outputDirectoryInfo.FullName, "moby.diagnostics.json");
            var textureResources = TextureResourcePreparer.PrepareExternalTextures(
                new DirectoryInfo(Path.Combine(outputDirectoryInfo.FullName, "textures")),
                outputDirectoryInfo);

            using var input = inputFile.OpenRead();
            var export = DlMobyGltfExporter.Export(
                input,
                outputFile.Name,
                new MobyGltfExportOptions
                {
                    AnimationFormat = MobyAnimationFormat.Compact,
                    ExternalTextureUris = textureResources?.Uris,
                    ExternalTextureSizes = textureResources?.Sizes,
                    ExternalTextureAlpha = textureResources?.Alpha,
                    BufferFileName = Path.GetFileName(bufferFile)
                });

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(bufferFile, export.BinBytes);
            File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);

            return GltfExportRoute.Written(
                "moby",
                modelId,
                inputPath,
                gltfPath,
                ToRelativeAssetPath(outputDirectory, bufferFile),
                ToRelativeAssetPath(outputDirectory, diagnosticsFile));
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("moby", modelId, inputPath, gltfPath, ex.Message);
        }
    }

    private static GltfExportRoute ExportTieGltf(
        string outputDirectory,
        int modelId,
        FileInfo inputFile,
        FileInfo outputFile,
        string inputPath,
        string gltfPath)
    {
        try
        {
            var outputDirectoryInfo = outputFile.Directory ?? new DirectoryInfo(outputDirectory);
            var bufferFile = Path.Combine(outputDirectoryInfo.FullName, "tie.buffer.bin");
            var diagnosticsFile = Path.Combine(outputDirectoryInfo.FullName, "tie.diagnostics.json");
            var textureResources = TieTextureResourcePreparer.PrepareExternalTextures(
                new DirectoryInfo(Path.Combine(outputDirectoryInfo.FullName, "textures")),
                outputDirectoryInfo);

            using var input = inputFile.OpenRead();
            var export = TieGltfExporter.Export(
                input,
                outputFile.Name,
                new TieGltfExportOptions
                {
                    LodIndex = 0,
                    BufferFileName = Path.GetFileName(bufferFile),
                    GameProfile = TieGameProfile.ForGame(GameId.DL),
                    ExternalTextureUris = textureResources?.Uris,
                    ExternalTextureSizes = textureResources?.Sizes,
                    ExternalTextureAlpha = textureResources?.Alpha
                });

            File.WriteAllBytes(outputFile.FullName, export.GltfBytes);
            File.WriteAllBytes(bufferFile, export.BinBytes);
            File.WriteAllBytes(diagnosticsFile, export.DiagnosticsBytes);

            return GltfExportRoute.Written(
                "tie",
                modelId,
                inputPath,
                gltfPath,
                ToRelativeAssetPath(outputDirectory, bufferFile),
                ToRelativeAssetPath(outputDirectory, diagnosticsFile));
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("tie", modelId, inputPath, gltfPath, ex.Message);
        }
    }

    private static IEnumerable<GltfExportRoute> ExportShrubGltfs(
        string outputDirectory,
        IReadOnlyList<DlAssetShrubDefinition> shrubDefinitions)
    {
        var routes = new List<GltfExportRoute>();
        foreach (var definition in shrubDefinitions)
        {
            var folderName = DlAssetReader.GetAssetFolderName(definition.ModelId);
            var relativeDirectory = $"shrub/{folderName}";
            var inputPath = $"{relativeDirectory}/shrub.bin";
            var gltfPath = $"{relativeDirectory}/shrub.gltf";
            var shrubDirectory = CombineRelativePath(outputDirectory, relativeDirectory);
            var inputFile = new FileInfo(Path.Combine(shrubDirectory, "shrub.bin"));
            var outputFile = new FileInfo(Path.Combine(shrubDirectory, "shrub.gltf"));

            PrepareGltfOutput(outputFile);
            if (!inputFile.Exists || inputFile.Length == 0)
            {
                routes.Add(GltfExportRoute.Empty("shrub", definition.ModelId, inputPath, gltfPath));
                continue;
            }

            try
            {
                var export = ShrubExportWriter.Export(inputFile, outputFile, GameId.DL, new DirectoryInfo(shrubDirectory));
                routes.Add(GltfExportRoute.Written(
                    "shrub",
                    definition.ModelId,
                    inputPath,
                    gltfPath,
                    ToRelativeAssetPath(outputDirectory, export.BufferFile.FullName),
                    export.DiagnosticsFile is null
                        ? null
                        : ToRelativeAssetPath(outputDirectory, export.DiagnosticsFile.FullName)));
            }
            catch (Exception ex) when (IsGltfExportFailure(ex))
            {
                routes.Add(GltfExportRoute.Failed("shrub", definition.ModelId, inputPath, gltfPath, ex.Message));
            }
        }

        return routes;
    }
}

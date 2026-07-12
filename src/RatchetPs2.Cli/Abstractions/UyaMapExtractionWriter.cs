using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.UYA.Level;

namespace RatchetPs2.Cli.Abstractions;

internal static class UyaMapExtractionWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static UyaMapExtractionSummary Extract(FileInfo isoFile, int levelIndex, DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(isoFile);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        if (!isoFile.Exists)
        {
            throw new FileNotFoundException("Input ISO does not exist.", isoFile.FullName);
        }

        outputDirectory.Create();

        using var isoStream = isoFile.OpenRead();
        var levelInfo = UyaLevelInfoReader.ReadLevelSet(isoStream, levelIndex);
        var looseWad = UyaLooseLevelWadExtractor.ExtractPrimary(isoStream, levelIndex);
        var package = UyaLevelWadUnpacker.Unpack(looseWad.Bytes);

        PackedFilePackageWriter.WriteFiles(package.Files, outputDirectory);
        var assetFiles = BuildAssetFiles(levelIndex, package.Files);
        PackedFilePackageWriter.WriteFiles(assetFiles, outputDirectory);

        var optionalFileCount = 0;
        optionalFileCount += ExtractOptionalPart(
            outputDirectory.FullName,
            "level_audio/level_audio.wad",
            isoStream,
            levelInfo.RequestedLevel.LevelAudioWad);
        optionalFileCount += ExtractOptionalPart(
            outputDirectory.FullName,
            "level_scene/level_scene.wad",
            isoStream,
            levelInfo.RequestedLevel.LevelSceneWad);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "UYA",
            ["SourceIso"] = isoFile.FullName,
            ["RequestedLevelIndex"] = levelInfo.RequestedLevelIndex,
            ["LevelInfo"] = levelInfo,
            ["LevelWad"] = looseWad.LevelWad,
            ["LevelWadSectorCount"] = looseWad.SectorCount,
            ["LevelWadByteLength"] = looseWad.ByteLength,
            ["UnpackedFileCount"] = package.Files.Count,
            ["AssetUnpackedFileCount"] = assetFiles.Count
        };

        File.WriteAllText(
            Path.Combine(outputDirectory.FullName, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new UyaMapExtractionSummary(
            outputDirectory.FullName,
            package.Files.Count + assetFiles.Count + optionalFileCount + 1,
            looseWad.SectorCount);
    }

    public static UyaMapExtractionSummary ExtractCustomZip(FileInfo zipFile, DirectoryInfo outputRoot)
    {
        ArgumentNullException.ThrowIfNull(zipFile);
        ArgumentNullException.ThrowIfNull(outputRoot);

        if (!zipFile.Exists)
        {
            throw new FileNotFoundException("Input custom map zip does not exist.", zipFile.FullName);
        }

        outputRoot.Create();
        var outputDirectory = new DirectoryInfo(Path.Combine(
            outputRoot.FullName,
            Path.GetFileNameWithoutExtension(zipFile.Name)));
        outputDirectory.Create();

        using var zipStream = zipFile.OpenRead();
        var package = UyaCustomMapZipUnpacker.Unpack(zipStream);

        PackedFilePackageWriter.WriteFiles(package.LevelDataFiles, outputDirectory);
        PackedFilePackageWriter.WriteFiles(package.GameplayFiles, outputDirectory);
        var assetFiles = BuildAssetFiles(levelIndex: 0, package.LevelDataFiles);
        PackedFilePackageWriter.WriteFiles(assetFiles, outputDirectory);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "UYA",
            ["Source"] = "custom_map_zip",
            ["SourceCustomMapZip"] = zipFile.FullName,
            ["CustomMapName"] = Path.GetFileNameWithoutExtension(zipFile.Name),
            ["LevelDataWadEntry"] = package.LevelDataWadEntryName,
            ["WorldEntry"] = package.WorldEntryName,
            ["LevelDataWadByteLength"] = package.LevelDataWadByteLength,
            ["WorldByteLength"] = package.WorldByteLength,
            ["UnpackedFileCount"] = package.LevelDataFiles.Count,
            ["GameplayUnpackedFileCount"] = package.GameplayFiles.Count,
            ["AssetUnpackedFileCount"] = assetFiles.Count
        };

        File.WriteAllText(
            Path.Combine(outputDirectory.FullName, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new UyaMapExtractionSummary(
            outputDirectory.FullName,
            package.LevelDataFiles.Count + package.GameplayFiles.Count + assetFiles.Count + 1,
            0);
    }

    private static IReadOnlyList<PackedFile> BuildAssetFiles(int levelIndex, IReadOnlyList<PackedFile> files)
    {
        var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        if (!byPath.TryGetValue("assets/asset_header.bin", out var header)
            || !byPath.TryGetValue("assets/palette.bin", out var palette)
            || !byPath.TryGetValue("assets/asset_wad.bin", out var assetWad))
        {
            return [];
        }

        return DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.UYA,
            levelIndex,
            header.Bytes,
            palette.Bytes,
            assetWad.Bytes);
    }

    private static int ExtractOptionalPart(string outputRoot, string path, Stream isoStream, UyaFileBlock block)
    {
        if (block.IsEmpty)
        {
            return 0;
        }

        var bytes = UyaLevelInfoReader.ReadSectorBlock(isoStream, block);
        if (bytes.Length == 0)
        {
            return 0;
        }

        var outputPath = Path.Combine(
            new[] { outputRoot }.Concat(path.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, bytes);
        return 1;
    }
}

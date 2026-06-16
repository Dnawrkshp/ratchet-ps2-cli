using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private const int AssetHeaderFixedLength = 0xc0;
    private const int AssetModelDefinitionLength = 0x20;
    private const int AssetShrubDefinitionLength = 0x30;
    private const int AssetTextureDefinitionLength = 0x10;
    private const int AssetMipmapDefinitionLength = 0x10;
    private const int AssetParticleTextureDefinitionLength = 0x10;
    private const int AssetFxTextureDefinitionLength = 0x10;
    private const string SkyboxSourcePath = "skybox/sky.bin";
    private const string SkyboxGltfPath = "skybox/skybox.gltf";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static ExtractionSummary Extract(FileInfo isoFile, int levelIndex, DirectoryInfo outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(isoFile);
        ArgumentNullException.ThrowIfNull(outputDirectory);

        if (!isoFile.Exists)
        {
            throw new FileNotFoundException("Input ISO does not exist.", isoFile.FullName);
        }

        outputDirectory.Create();
        CleanLegacyCoreSegmentArtifacts(outputDirectory.FullName);

        using var isoStream = isoFile.OpenRead();
        var levelInfo = DlLevelInfoReader.ReadLevelSet(isoStream, levelIndex);
        var mediaSource = CreateMediaSource(levelInfo);
        var levelWadBytes = DlLevelInfoReader.ReadSectorHeader(
            isoStream,
            levelInfo.RequestedLevel.LevelWad,
            DlLevelConstants.LevelWadHeaderSectorCount);
        var levelAudioWadBytes = DlLevelInfoReader.ReadSectorHeader(
            isoStream,
            levelInfo.MediaLevel.LevelAudioWad,
            DlLevelConstants.LevelAudioWadHeaderSectorCount);
        var levelSceneWadBytes = DlLevelInfoReader.ReadSectorHeader(
            isoStream,
            levelInfo.MediaLevel.LevelSceneWad,
            DlLevelConstants.LevelSceneWadHeaderSectorCount);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "DL",
            ["SourceIso"] = isoFile.FullName,
            ["RequestedLevelIndex"] = levelInfo.RequestedLevelIndex,
            ["MediaLevelIndex"] = levelInfo.MediaLevelIndex,
            ["MediaSource"] = mediaSource,
            ["LevelInfo"] = levelInfo
        };

        var levelDirectory = CreateDirectory(outputDirectory.FullName, "level_wad");
        var levelWad = ExtractLevelWad(
            levelDirectory,
            outputDirectory.FullName,
            isoStream,
            levelWadBytes,
            manifest);

        if (levelAudioWadBytes.Length > 0)
        {
            var audioDirectory = GetMediaPayloadDirectory("level_audio", mediaSource);
            ExtractLevelAudioWad(
                CreateDirectoryForRelativePath(outputDirectory.FullName, audioDirectory),
                CreateMediaPayloadSource("level_audio", audioDirectory, mediaSource),
                isoStream,
                levelAudioWadBytes,
                manifest);
        }

        if (levelSceneWadBytes.Length > 0)
        {
            var sceneDirectory = GetMediaPayloadDirectory("level_scene", mediaSource);
            ExtractLevelSceneWad(
                outputDirectory.FullName,
                sceneDirectory,
                CreateMediaPayloadSource("level_scene", sceneDirectory, mediaSource),
                isoStream,
                levelSceneWadBytes,
                manifest);
        }

        IReadOnlyList<DlCoreLevelSegment> coreSegments = [];
        var coreLevelBytes = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, levelWad.Sector, levelWad.Data);
        if (coreLevelBytes.Length > 0)
        {
            coreSegments = ReadCoreSegments(coreLevelBytes, manifest);
        }

        var coreSegmentByHeaderOffset = coreSegments.ToDictionary(segment => segment.HeaderOffset);
        if (coreSegments.Count > 0)
        {
            manifest["CorePayloads"] = ExtractCorePayloads(outputDirectory.FullName, coreSegments);
        }

        if (TryGetCoreSegment(coreSegmentByHeaderOffset, 0x08, out var codeSegment))
        {
            ExtractCodeSegment(
                CreateDirectory(outputDirectory.FullName, "code"),
                codeSegment.PayloadBytes,
                manifest);
        }

        if (TryGetCoreSegment(coreSegmentByHeaderOffset, 0x20, out var hudHeader))
        {
            ExtractHudBanks(
                outputDirectory.FullName,
                CreateDirectory(outputDirectory.FullName, "hud"),
                hudHeader.PayloadBytes,
                GetHudBankPayloads(coreSegmentByHeaderOffset),
                manifest);
        }

        if (TryGetCoreSegment(coreSegmentByHeaderOffset, 0x10, out var assetHeader)
            && TryGetCoreSegment(coreSegmentByHeaderOffset, 0x18, out var palette)
            && TryGetCoreSegment(coreSegmentByHeaderOffset, 0x50, out var assetWad))
        {
            ExtractAssets(
                CreateDirectory(outputDirectory.FullName, "assets"),
                levelInfo.RequestedLevelIndex,
                assetHeader.PayloadBytes,
                palette.PayloadBytes,
                assetWad.PayloadBytes,
                manifest);
        }

        if (TryGetCoreSegment(coreSegmentByHeaderOffset, 0x58, out var worldInstances))
        {
            ExtractWorldInstances(
                CreateDirectory(outputDirectory.FullName, "world"),
                worldInstances.PayloadBytes,
                manifest);
        }

        WriteJson(Path.Combine(outputDirectory.FullName, "manifest.json"), manifest);

        return new ExtractionSummary(
            outputDirectory.FullName,
            coreSegments.Count,
            manifest.TryGetValue("TextureCount", out var textureCount) && textureCount is int count ? count : 0);
    }
}

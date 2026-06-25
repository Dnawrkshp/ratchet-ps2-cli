using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static DlLevelWad ExtractLevelWad(
        string outputDirectory,
        string mapOutputDirectory,
        Stream isoStream,
        byte[] levelWadBytes,
        IDictionary<string, object?> manifest)
    {
        CleanLegacyTableHeaderArtifacts(outputDirectory, "levelwad.bin", "header.bin");
        var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);

        WriteSectorBlock(outputDirectory, "core_sound.bnk", isoStream, levelWad.Sector, levelWad.CoreBank);

        var chunksDirectory = CreateDirectory(outputDirectory, "chunks");
        for (var i = 0; i < levelWad.Chunks.Count; i++)
        {
            WriteSectorBlock(chunksDirectory, $"chunk{i}.wad", isoStream, levelWad.Sector, levelWad.Chunks[i]);
            WriteSectorBlock(chunksDirectory, $"chunk{i}_bank.wad", isoStream, levelWad.Sector, levelWad.ChunkBanks[i]);
        }

        WriteSectorBlock(outputDirectory, "gameplay_unused.wad", isoStream, levelWad.Sector, levelWad.GameplayCore);

        var missionCount = 0;
        var skippedPlaceholderMissionCount = 0;
        string? missionsDirectory = null;
        for (var i = 0; i < levelWad.GameplayMissionData.Count; i++)
        {
            var missionData = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, levelWad.Sector, levelWad.GameplayMissionData[i]);
            var missionBank = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, levelWad.Sector, levelWad.MissionBanks[i]);
            var missionInstances = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, levelWad.Sector, levelWad.GameplayMissionInstances[i]);

            if (missionData.Length == 0 && missionBank.Length == 0 && missionInstances.Length == 0)
            {
                continue;
            }

            if (missionBank.Length == 0
                && missionInstances.Length == 0
                && DlMissionDataReader.IsPlaceholderMissionData(missionData))
            {
                skippedPlaceholderMissionCount++;
                continue;
            }

            missionsDirectory ??= CreateDirectory(mapOutputDirectory, "missions");
            missionCount++;
            ExtractMission(CreateDirectory(missionsDirectory, $"{i:0000}"), i, missionData, missionBank, missionInstances);
        }

        manifest["LevelWad"] = levelWad;
        AddOmittedRawPayload(
            manifest,
            "level_wad/levelwad.bin",
            "manifest LevelWad + level_wad payload files + missions",
            "The level WAD table is represented by parsed manifest metadata; sector padding beyond the parsed header is zero.");
        AddOmittedRawPayload(
            manifest,
            "level_wad/header.bin",
            "manifest LevelWad.HeaderBytes",
            "The parsed level WAD header bytes are already embedded in the manifest.");
        AddOmittedRawPayload(
            manifest,
            "level_wad/core_level.wad",
            "manifest CoreSegments/CorePayloads + named code/assets/hud/world/gameplay/core_pvars/global_nav outputs",
            "The sector-relative core level payload is represented by segment table metadata and named semantic payloads.");
        AddOmittedRawPayload(
            manifest,
            "level_wad/art_instances.wad",
            "world/lighting + world/tie + world/shrub + world/occlusion + world/unknown",
            "World instance data is represented by named semantic payloads and slot pointer metadata.");
        if (missionCount > 0)
        {
            manifest["MissionDirectory"] = "missions";
        }

        manifest["MissionCount"] = missionCount;
        if (skippedPlaceholderMissionCount > 0)
        {
            manifest["SkippedPlaceholderMissionCount"] = skippedPlaceholderMissionCount;
        }

        return levelWad;
    }

    private static void ExtractLevelAudioWad(
        string outputDirectory,
        MediaPayloadSource source,
        Stream isoStream,
        byte[] audioWadBytes,
        IDictionary<string, object?> manifest)
    {
        CleanLegacyTableHeaderArtifacts(outputDirectory, "levelaudio.bin", "header.bin");
        var audioWad = DlLevelWadReader.ReadLevelAudioWad(audioWadBytes);

        var instancesDirectory = CreateDirectory(outputDirectory, "audio_instances");
        for (var i = 0; i < audioWad.AudioInstances.Count; i++)
        {
            WriteByteLengthBlock(instancesDirectory, $"{i:0000}.bin", isoStream, audioWad.Sector, audioWad.AudioInstances[i]);
        }

        WriteByteLengthBlock(outputDirectory, "upgrade_sample.bin", isoStream, audioWad.Sector, audioWad.UpgradeSample);
        WriteByteLengthBlock(outputDirectory, "platinum_bolt.bin", isoStream, audioWad.Sector, audioWad.PlatinumBolt);
        WriteByteLengthBlock(outputDirectory, "spare.bin", isoStream, audioWad.Sector, audioWad.Spare);
        manifest["LevelAudioDirectory"] = source.Directory;
        manifest["LevelAudioSource"] = source;
        manifest["LevelAudioWad"] = audioWad;
        AddOmittedRawPayload(
            manifest,
            $"{source.Directory}/levelaudio.bin",
            "manifest LevelAudioWad + level_audio payload files",
            "The level audio WAD table is represented by parsed manifest metadata; sector padding beyond the parsed header is zero.");
        AddOmittedRawPayload(
            manifest,
            $"{source.Directory}/header.bin",
            "manifest LevelAudioWad.HeaderBytes",
            "The parsed level audio WAD header bytes are already embedded in the manifest.");
    }

    private static void ExtractLevelSceneWad(
        string mapOutputDirectory,
        string sceneRelativeDirectory,
        MediaPayloadSource source,
        Stream isoStream,
        byte[] sceneWadBytes,
        IDictionary<string, object?> manifest)
    {
        var sceneWad = DlLevelWadReader.ReadLevelSceneWad(sceneWadBytes);
        if (DlLevelWadReader.IsHeaderOnlyLevelSceneWad(sceneWadBytes, sceneWad))
        {
            var sceneOutputDirectory = CombineRelativePath(mapOutputDirectory, sceneRelativeDirectory);
            CleanLegacyTableHeaderArtifacts(sceneOutputDirectory, "levelscene.bin", "header.bin");
            TryDeleteEmptyDirectory(Path.Combine(sceneOutputDirectory, "scenes"));
            TryDeleteEmptyDirectory(sceneOutputDirectory);

            manifest["LevelSceneSource"] = source;
            manifest["LevelSceneWad"] = sceneWad;
            manifest["LevelSceneCount"] = 0;
            manifest["SkippedEmptySceneCount"] = sceneWad.Scenes.Count;
            manifest["SkippedLevelSceneWad"] = true;
            AddOmittedRawPayload(
                manifest,
                $"{source.Directory}/levelscene.bin",
                "manifest LevelSceneWad",
                "The level scene WAD only contains an empty scene table and zero padding.");
            AddOmittedRawPayload(
                manifest,
                $"{source.Directory}/header.bin",
                "manifest LevelSceneWad.HeaderBytes",
                "The parsed level scene WAD header bytes are already embedded in the manifest.");
            return;
        }

        var outputDirectory = CreateDirectoryForRelativePath(mapOutputDirectory, sceneRelativeDirectory);
        CleanLegacyTableHeaderArtifacts(outputDirectory, "levelscene.bin", "header.bin");
        WriteJson(Path.Combine(outputDirectory, "scenes.json"), sceneWad.Scenes);

        var sceneCount = 0;
        var skippedEmptySceneCount = 0;
        string? scenesDirectory = null;
        foreach (var scene in sceneWad.Scenes)
        {
            var subtitles = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, sceneWad.Sector, scene.Subtitles);
            var mobyLoad = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, sceneWad.Sector, scene.MobyLoad);
            if (subtitles.Length == 0 && mobyLoad.Length == 0)
            {
                skippedEmptySceneCount++;
                continue;
            }

            scenesDirectory ??= CreateDirectory(outputDirectory, "scenes");
            var sceneDirectory = CreateDirectory(scenesDirectory, $"{scene.Index:0000}");
            if (subtitles.Length > 0)
            {
                File.WriteAllBytes(Path.Combine(sceneDirectory, "subtitles.bin"), subtitles);
            }

            if (mobyLoad.Length > 0)
            {
                File.WriteAllBytes(Path.Combine(sceneDirectory, "mobyload.wad"), mobyLoad);
            }

            sceneCount++;
        }

        manifest["LevelSceneDirectory"] = source.Directory;
        manifest["LevelSceneSource"] = source;
        manifest["LevelSceneWad"] = sceneWad;
        manifest["LevelSceneCount"] = sceneCount;
        AddOmittedRawPayload(
            manifest,
            $"{source.Directory}/levelscene.bin",
            "manifest LevelSceneWad + level_scene/scenes payload files",
            "The level scene WAD table is represented by parsed manifest metadata; sector padding beyond the parsed header is zero.");
        AddOmittedRawPayload(
            manifest,
            $"{source.Directory}/header.bin",
            "manifest LevelSceneWad.HeaderBytes",
            "The parsed level scene WAD header bytes are already embedded in the manifest.");
        if (skippedEmptySceneCount > 0)
        {
            manifest["SkippedEmptySceneCount"] = skippedEmptySceneCount;
        }
    }

    private static void ExtractMission(
        string outputDirectory,
        int missionIndex,
        byte[] missionData,
        byte[] missionBank,
        byte[] missionInstances)
    {
        if (missionData.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, "mission.wad"), missionData);
            TryExtractMissionPayloads(outputDirectory, missionData);
        }

        if (missionBank.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, "sound.bnk"), missionBank);
        }

        if (missionInstances.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, "gameplay_instances.bin"), missionInstances);
        }

        WriteJson(
            Path.Combine(outputDirectory, "manifest.json"),
            new
            {
                MissionIndex = missionIndex,
                MissionWadLength = missionData.Length,
                SoundBankLength = missionBank.Length,
                GameplayInstancesLength = missionInstances.Length
            });
    }

    private static void TryExtractMissionPayloads(string outputDirectory, byte[] missionData)
    {
        if (missionData.Length < 0x10)
        {
            return;
        }

        var gameplayOffset = BitConverter.ToInt32(missionData, 0);
        var gameplayLength = BitConverter.ToInt32(missionData, 4);
        var classesOffset = BitConverter.ToInt32(missionData, 8);
        var classesLength = BitConverter.ToInt32(missionData, 12);
        var localOffsetDelta = gameplayOffset - 0x40;
        var gameplayBytes = WritePossiblyCompressedMissionBlock(outputDirectory, "gameplay.bin", missionData, 0x40, gameplayLength);
        if (gameplayBytes.Length >= DlGameplayBlockReader.MissionHeaderSize)
        {
            WriteGameplayBlocks(
                CreateDirectory(outputDirectory, "gameplay"),
                DlGameplayBlockReader.ReadMission(gameplayBytes));
        }

        if (classesOffset > 0 && classesLength > 0)
        {
            WritePossiblyCompressedMissionBlock(
                outputDirectory,
                "classes.bin",
                missionData,
                classesOffset - localOffsetDelta,
                classesLength);
        }
    }

    private static byte[] WritePossiblyCompressedMissionBlock(
        string outputDirectory,
        string fileName,
        byte[] source,
        int offset,
        int length)
    {
        if (offset < 0 || length <= 0 || (long)offset + length > source.Length)
        {
            return [];
        }

        var data = source.AsSpan(offset, length).ToArray();
        try
        {
            data = WadCompression.Decompress(data);
        }
        catch (InvalidDataException)
        {
        }

        File.WriteAllBytes(Path.Combine(outputDirectory, fileName), data);
        return data;
    }
}

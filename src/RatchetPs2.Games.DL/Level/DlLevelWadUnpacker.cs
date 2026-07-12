using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Gameplay;

namespace RatchetPs2.Games.DL.Level;

public static class DlLevelWadUnpacker
{
    public static DlLevelWadPackage Unpack(ReadOnlySpan<byte> levelWadBytes)
    {
        var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
        var files = new List<PackedFile>();

        AddFile(files, "level_wad/header.bin", levelWad.HeaderBytes);

        var coreLevelBytes = AddSectorFile(files, "level_wad/core_level.wad", levelWadBytes, levelWad.Data);
        AddSectorFile(files, "level_wad/core_sound.bnk", levelWadBytes, levelWad.CoreBank);

        for (var i = 0; i < levelWad.Chunks.Count; i++)
        {
            AddSectorFile(files, $"level_wad/chunks/chunk{i}.wad", levelWadBytes, levelWad.Chunks[i]);
            AddSectorFile(files, $"level_wad/chunks/chunk{i}_bank.wad", levelWadBytes, levelWad.ChunkBanks[i]);
        }

        AddSectorFile(files, "level_wad/gameplay_unused.wad", levelWadBytes, levelWad.GameplayCore);
        AddSectorFile(files, "level_wad/art_instances.wad", levelWadBytes, levelWad.ArtInstances);
        AddMissions(files, levelWadBytes, levelWad);

        if (coreLevelBytes.Length > 0)
        {
            AddCorePayloads(files, coreLevelBytes);
        }

        return new DlLevelWadPackage(levelWad, files);
    }

    public static PackedFilePackage UnpackPacked(ReadOnlySpan<byte> levelWadBytes)
    {
        return Unpack(levelWadBytes).ToPackedPackage();
    }

    private static void AddMissions(List<PackedFile> files, ReadOnlySpan<byte> levelWadBytes, DlLevelWad levelWad)
    {
        for (var i = 0; i < levelWad.GameplayMissionData.Count; i++)
        {
            var missionData = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.GameplayMissionData[i]);
            var missionBank = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.MissionBanks[i]);
            var missionInstances = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.GameplayMissionInstances[i]);

            if (missionData.Length == 0 && missionBank.Length == 0 && missionInstances.Length == 0)
            {
                continue;
            }

            if (missionBank.Length == 0
                && missionInstances.Length == 0
                && DlMissionDataReader.IsPlaceholderMissionData(missionData))
            {
                continue;
            }

            var missionRoot = $"missions/{i:0000}";
            AddFile(files, $"{missionRoot}/mission.wad", missionData);
            AddFile(files, $"{missionRoot}/sound.bnk", missionBank);
            AddFile(files, $"{missionRoot}/gameplay_instances.bin", missionInstances);
            AddMissionPayloads(files, missionRoot, missionData);
        }
    }

    private static void AddMissionPayloads(List<PackedFile> files, string missionRoot, byte[] missionData)
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

        var gameplayBytes = AddPossiblyCompressedMissionBlock(files, $"{missionRoot}/gameplay.bin", missionData, 0x40, gameplayLength);
        if (gameplayBytes.Length >= DlGameplayBlockReader.MissionHeaderSize)
        {
            AddGameplayBlocks(files, $"{missionRoot}/gameplay", DlGameplayBlockReader.ReadMission(gameplayBytes));
        }

        if (classesOffset > 0 && classesLength > 0)
        {
            AddPossiblyCompressedMissionBlock(
                files,
                $"{missionRoot}/classes.bin",
                missionData,
                classesOffset - localOffsetDelta,
                classesLength);
        }
    }

    private static byte[] AddPossiblyCompressedMissionBlock(
        List<PackedFile> files,
        string path,
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

        AddFile(files, path, data);
        return data;
    }

    private static void AddCorePayloads(List<PackedFile> files, byte[] coreLevelBytes)
    {
        var segments = DlCoreLevelSegmentReader.Read(coreLevelBytes);
        foreach (var segment in segments)
        {
            AddFile(files, GetCorePayloadPath(segment), segment.PayloadBytes);

            if (segment.HeaderOffset == 0x58 && segment.PayloadBytes.Length > 0)
            {
                AddWorldPayloads(files, segment.PayloadBytes);
            }

            if (segment.HeaderOffset == 0x60 && segment.PayloadBytes.Length > 0)
            {
                AddGameplayBlocks(files, "gameplay/core", DlGameplayBlockReader.ReadCore(segment.PayloadBytes));
            }
        }
    }

    private static void AddGameplayBlocks(List<PackedFile> files, string root, DlGameplayBlocks gameplay)
    {
        AddFile(files, $"{root}/header.bin", gameplay.HeaderBytes);
        foreach (var block in gameplay.Blocks)
        {
            AddFile(files, $"{root}/{block.SemanticName}.bin", block.PayloadBytes);
        }
    }

    private static void AddWorldPayloads(List<PackedFile> files, byte[] worldBytes)
    {
        var world = DlWorldInstanceReader.Read(worldBytes);
        foreach (var slot in world.Slots)
        {
            if (slot.PayloadBytes.Length == 0)
            {
                continue;
            }

            AddFile(files, $"world/{GetWorldSlotPath(slot)}", slot.PayloadBytes);
        }
    }

    private static string GetCorePayloadPath(DlCoreLevelSegment segment)
    {
        return segment.HeaderOffset switch
        {
            0x00 => "core_pvars/moby8355_pvars.bin",
            0x08 => "code/code.bin",
            0x10 => "assets/asset_header.bin",
            0x18 => "assets/palette.bin",
            0x20 => "hud/header.bin",
            0x28 => "hud/bank0.bin",
            0x30 => "hud/bank1.bin",
            0x38 => "hud/bank2.bin",
            0x40 => "hud/bank3.bin",
            0x48 => "hud/bank4.bin",
            0x50 => "assets/asset_wad.bin",
            0x58 => "world/art_instances.wad",
            0x60 => "gameplay/gameplay_core.bin",
            0x68 => "global_nav/global_nav_data.bin",
            _ => $"core_unknown/{segment.Name}{segment.OutputExtension}"
        };
    }

    private static string GetWorldSlotPath(DlWorldInstanceSlot slot)
    {
        return slot.HeaderOffset switch
        {
            0x00 => "lighting/directional_lights.bin",
            0x04 => "tie/class_ids.bin",
            0x08 => "tie/instances.bin",
            0x0c => "tie/groups.bin",
            0x10 => "shrub/class_ids.bin",
            0x14 => "shrub/instances.bin",
            0x18 => "shrub/groups.bin",
            0x1c => "occlusion/instance_mapping.bin",
            0x20 => "tie/colors.bin",
            _ => $"unknown/slot_{slot.HeaderOffset:X2}.bin"
        };
    }

    private static byte[] AddSectorFile(
        List<PackedFile> files,
        string path,
        ReadOnlySpan<byte> levelWadBytes,
        DlFileBlock block)
    {
        var bytes = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, block);
        AddFile(files, path, bytes);
        return bytes;
    }

    private static void AddFile(List<PackedFile> files, string path, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        files.Add(new PackedFile(path, bytes, GetContentType(path)));
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".gltf" => "model/gltf+json",
            ".png" => "image/png",
            ".pif" or ".wad" or ".bnk" or ".bin" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }
}

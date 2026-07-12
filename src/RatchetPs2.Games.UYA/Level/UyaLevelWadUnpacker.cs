using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.UYA.Gameplay;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadUnpacker
{
    public static UyaLevelWadPackage Unpack(ReadOnlySpan<byte> levelWadBytes)
    {
        var levelWad = UyaLevelWadReader.ReadLevelWad(levelWadBytes);
        var files = new List<PackedFile>();

        AddFile(files, "level_wad/header.bin", levelWad.HeaderBytes);

        var levelDataBytes = AddSectorFile(files, "level_wad/level_data.wad", levelWadBytes, levelWad.Data);
        AddSectorFile(files, "level_wad/sound.bnk", levelWadBytes, levelWad.SoundBank);
        var gameplayBytes = AddSectorFile(files, "gameplay/gameplay.bin", levelWadBytes, levelWad.Gameplay);
        AddSectorFile(files, "occlusion/occlusion.bin", levelWadBytes, levelWad.Occlusion);

        for (var i = 0; i < levelWad.Chunks.Count; i++)
        {
            AddSectorFile(files, $"level_wad/chunks/chunk{i}.wad", levelWadBytes, levelWad.Chunks[i]);
            AddSectorFile(files, $"level_wad/chunks/chunk{i}_bank.wad", levelWadBytes, levelWad.ChunkBanks[i]);
        }

        if (levelDataBytes.Length > 0)
        {
            AddLevelDataPayloads(files, levelDataBytes);
        }

        if (gameplayBytes.Length > 0)
        {
            AddGameplayPayloads(files, gameplayBytes);
        }

        return new UyaLevelWadPackage(levelWad, files);
    }

    public static PackedFilePackage UnpackPacked(ReadOnlySpan<byte> levelWadBytes)
    {
        return Unpack(levelWadBytes).ToPackedPackage();
    }

    public static IReadOnlyList<PackedFile> UnpackLevelData(ReadOnlySpan<byte> levelDataBytes)
    {
        var files = new List<PackedFile>();
        var rawBytes = levelDataBytes.ToArray();

        AddFile(files, "level_wad/level_data.wad", rawBytes);
        AddLevelDataPayloads(files, rawBytes);

        return files;
    }

    public static IReadOnlyList<PackedFile> UnpackGameplay(ReadOnlySpan<byte> gameplayBytes)
    {
        var files = new List<PackedFile>();
        var rawBytes = gameplayBytes.ToArray();

        AddFile(files, "gameplay/gameplay.bin", rawBytes);
        AddGameplayPayloads(files, rawBytes);

        return files;
    }

    private static void AddLevelDataPayloads(List<PackedFile> files, byte[] levelDataBytes)
    {
        var levelData = UyaLevelWadReader.ReadLevelDataWad(levelDataBytes);

        AddFile(files, "level_data/header.bin", levelData.HeaderBytes);
        AddByteFile(files, "code/code.bin", levelDataBytes, levelData.Overlay);
        AddByteFile(files, "assets/asset_header.bin", levelDataBytes, levelData.CoreIndex);
        AddByteFile(files, "assets/palette.bin", levelDataBytes, levelData.GsRam);
        AddByteFile(files, "hud/header.bin", levelDataBytes, levelData.HudHeader);

        for (var i = 0; i < levelData.HudBanks.Count; i++)
        {
            AddByteFile(files, $"hud/bank{i}.bin", levelDataBytes, levelData.HudBanks[i]);
        }

        AddByteFile(files, "assets/asset_wad.bin", levelDataBytes, levelData.CoreData);
        AddByteFile(files, "transition_textures/transition_textures.bin", levelDataBytes, levelData.TransitionTextures);
    }

    private static void AddGameplayPayloads(List<PackedFile> files, byte[] gameplayBytes)
    {
        var payloadBytes = BinaryMagic.IsWad(gameplayBytes)
            ? WadCompression.Decompress(gameplayBytes)
            : gameplayBytes;

        AddFile(files, "gameplay/gameplay_core.bin", payloadBytes);
        if (payloadBytes.Length >= UyaGameplayBlockReader.CoreHeaderSize)
        {
            AddGameplayBlocks(files, "gameplay/core", UyaGameplayBlockReader.ReadCore(payloadBytes));
        }
    }

    private static void AddGameplayBlocks(List<PackedFile> files, string root, UyaGameplayBlocks gameplay)
    {
        AddFile(files, $"{root}/header.bin", gameplay.HeaderBytes);
        foreach (var block in gameplay.Blocks)
        {
            AddFile(files, $"{root}/{block.SemanticName}.bin", block.PayloadBytes);
        }
    }

    private static byte[] AddSectorFile(
        List<PackedFile> files,
        string path,
        ReadOnlySpan<byte> levelWadBytes,
        UyaFileBlock block)
    {
        var bytes = UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, block);
        AddFile(files, path, bytes);
        return bytes;
    }

    private static byte[] AddByteFile(
        List<PackedFile> files,
        string path,
        ReadOnlySpan<byte> bytes,
        UyaByteBlock block)
    {
        var fileBytes = UyaLevelWadReader.ReadByteFileBlock(bytes, block);
        AddFile(files, path, fileBytes);
        return fileBytes;
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

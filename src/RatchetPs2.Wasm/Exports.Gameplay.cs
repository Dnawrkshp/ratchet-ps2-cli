using Microsoft.JSInterop;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.UYA.Gameplay;
using System.Buffers.Binary;
using System.Runtime.Versioning;

namespace RatchetPs2.Wasm;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    [JSInvokable("ParseDlGameplayCore")]
    public static WasmDlGameplayBlocks ParseDlGameplayCore(byte[] gameplayBytes)
    {
        ArgumentNullException.ThrowIfNull(gameplayBytes);

        return ToWasmGameplayBlocks(DlGameplayBlockReader.ReadCore(gameplayBytes));
    }

    [JSInvokable("ParseDlGameplayMission")]
    public static WasmDlGameplayBlocks ParseDlGameplayMission(byte[] gameplayBytes)
    {
        ArgumentNullException.ThrowIfNull(gameplayBytes);

        return ToWasmGameplayBlocks(DlGameplayBlockReader.ReadMission(gameplayBytes));
    }

    [JSInvokable("ParseUyaGameplayCore")]
    public static WasmUyaGameplayBlocks ParseUyaGameplayCore(byte[] gameplayBytes)
    {
        ArgumentNullException.ThrowIfNull(gameplayBytes);

        var payloadBytes = BinaryMagic.IsWad(gameplayBytes)
            ? WadCompression.Decompress(gameplayBytes)
            : gameplayBytes;

        return ToWasmGameplayBlocks(UyaGameplayBlockReader.ReadCore(payloadBytes));
    }

    private static WasmDlGameplayBlocks ToWasmGameplayBlocks(DlGameplayBlocks gameplay)
    {
        return new WasmDlGameplayBlocks(
            gameplay.Kind,
            gameplay.HeaderSize,
            ToWasmPvarTables(gameplay.Blocks, "DL"),
            gameplay.Blocks
                .Select(block => new WasmDlGameplayBlock(
                    block.Index,
                    block.HeaderOffset,
                    block.Pointer,
                    block.SemanticName,
                    block.PayloadBytes.Length,
                    ToWasmLevelSettings(block.LevelSettings),
                    ToWasmMobyInstances(block.MobyInstances)))
                .ToArray());
    }

    private static WasmUyaGameplayBlocks ToWasmGameplayBlocks(UyaGameplayBlocks gameplay)
    {
        return new WasmUyaGameplayBlocks(
            gameplay.Kind,
            gameplay.HeaderSize,
            gameplay.HeaderBytes,
            ToWasmPvarTables(gameplay.Blocks, "UYA"),
            gameplay.Blocks
                .Select(block => new WasmUyaGameplayBlock(
                    block.Index,
                    block.HeaderOffset,
                    block.Pointer,
                    block.SemanticName,
                    block.PayloadBytes.Length,
                    block.PayloadBytes,
                    ToWasmLevelSettings(block.LevelSettings),
                    ToWasmMobyInstances(block.MobyInstances)))
                .ToArray());
    }

    private static WasmDlLevelSettings? ToWasmLevelSettings(DlLevelSettings? settings)
    {
        return settings is null
            ? null
            : new WasmDlLevelSettings(
                settings.BackgroundColor,
                settings.FogColor,
                settings.FogNearDistance,
                settings.FogFarDistance,
                settings.FogNearIntensity,
                settings.FogFarIntensity,
                settings.DeathHeight,
                settings.IsSphericalWorld,
                settings.SphereCenter,
                settings.ShipPosition,
                settings.ShipRotationZ,
                settings.ShipPath,
                settings.ShipCameraCuboidStart,
                settings.ShipCameraCuboidEnd,
                settings.Padding58,
                settings.ChunkPlanes.ToArray(),
                settings.CoreSoundsCount,
                settings.ThirdPartCount,
                settings.ThirdPart.ToArray(),
                settings.RewardStats,
                settings.FifthPart,
                settings.DebugAttackDamage.Length,
                settings.TrailingBytes.Length);
    }

    private static WasmUyaLevelSettings? ToWasmLevelSettings(UyaLevelSettings? settings)
    {
        return settings is null
            ? null
            : new WasmUyaLevelSettings(
                settings.BackgroundColor,
                settings.FogColor,
                settings.FogNearDistance,
                settings.FogFarDistance,
                settings.FogNearIntensity,
                settings.FogFarIntensity,
                settings.DeathHeight,
                settings.IsSphericalWorld,
                settings.SphereCenter,
                settings.ShipPosition,
                settings.ShipRotationZ,
                settings.ShipPath,
                settings.ShipCameraCuboidStart,
                settings.ShipCameraCuboidEnd,
                settings.Padding58,
                settings.ChunkPlanes.ToArray(),
                settings.CoreSoundsCount,
                settings.Rac3ThirdPart,
                settings.TrailingBytes.Length);
    }

    private static WasmDlMobyInstances? ToWasmMobyInstances(DlMobyInstances? mobyInstances)
    {
        return mobyInstances is null
            ? null
            : new WasmDlMobyInstances(
                mobyInstances.StaticCount,
                mobyInstances.SpawnableMobyCount,
                mobyInstances.Pad8,
                mobyInstances.PadC,
                mobyInstances.Instances.ToArray(),
                mobyInstances.TrailingBytes.Length);
    }

    private static WasmUyaMobyInstances? ToWasmMobyInstances(UyaMobyInstances? mobyInstances)
    {
        return mobyInstances is null
            ? null
            : new WasmUyaMobyInstances(
                mobyInstances.StaticCount,
                mobyInstances.SpawnableMobyCount,
                mobyInstances.Pad8,
                mobyInstances.PadC,
                mobyInstances.Instances.ToArray(),
                mobyInstances.TrailingBytes.Length);
    }

    private static WasmDlPvarTables? ToWasmPvarTables(IReadOnlyList<DlGameplayBlock> blocks, string gameName)
    {
        return ToWasmPvarTables(
            FindPayload(blocks, "pvar_moby_links"),
            FindPayload(blocks, "pvar_table"),
            FindPayload(blocks, "pvar_data"),
            FindPayload(blocks, "pvar_relative_pointers"),
            gameName);
    }

    private static WasmDlPvarTables? ToWasmPvarTables(IReadOnlyList<UyaGameplayBlock> blocks, string gameName)
    {
        return ToWasmPvarTables(
            FindPayload(blocks, "pvar_moby_links"),
            FindPayload(blocks, "pvar_table"),
            FindPayload(blocks, "pvar_data"),
            FindPayload(blocks, "pvar_relative_pointers"),
            gameName);
    }

    private static WasmDlPvarTables? ToWasmPvarTables(
        byte[] mobyLinksBytes,
        byte[] tableBytes,
        byte[] dataBytes,
        byte[] relativePointerBytes,
        string gameName)
    {
        if (mobyLinksBytes.Length == 0 &&
            tableBytes.Length == 0 &&
            dataBytes.Length == 0 &&
            relativePointerBytes.Length == 0)
        {
            return null;
        }

        return new WasmDlPvarTables(
            mobyLinksBytes,
            tableBytes,
            dataBytes,
            relativePointerBytes,
            ReadPvarTableEntries(tableBytes, dataBytes, gameName),
            ReadPvarRelativePointers(relativePointerBytes, gameName));
    }

    private static byte[] FindPayload(IReadOnlyList<DlGameplayBlock> blocks, string semanticName)
    {
        return blocks.FirstOrDefault(block => block.SemanticName == semanticName)?.PayloadBytes ?? [];
    }

    private static byte[] FindPayload(IReadOnlyList<UyaGameplayBlock> blocks, string semanticName)
    {
        return blocks.FirstOrDefault(block => block.SemanticName == semanticName)?.PayloadBytes ?? [];
    }

    private static WasmDlPvarTableEntry[] ReadPvarTableEntries(byte[] tableBytes, byte[] dataBytes, string gameName)
    {
        const int entrySize = 8;
        if (tableBytes.Length % entrySize != 0)
        {
            throw new InvalidDataException($"{gameName} pvar table length must be divisible by 8.");
        }

        var entries = new WasmDlPvarTableEntry[tableBytes.Length / entrySize];
        for (int index = 0; index < entries.Length; index++)
        {
            ReadOnlySpan<byte> entryBytes = tableBytes.AsSpan(index * entrySize, entrySize);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[..4]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(entryBytes[4..]);
            if (offset < 0 || length < 0 || offset > dataBytes.Length - length)
            {
                throw new InvalidDataException($"{gameName} pvar table entry {index} points outside pvar_data.");
            }

            entries[index] = new WasmDlPvarTableEntry(
                index,
                offset,
                length,
                length == 0 ? [] : dataBytes.AsSpan(offset, length).ToArray());
        }

        return entries;
    }

    private static WasmDlPvarRelativePointer[] ReadPvarRelativePointers(byte[] relativePointerBytes, string gameName)
    {
        const int entrySize = 8;
        if (relativePointerBytes.Length % entrySize != 0)
        {
            throw new InvalidDataException($"{gameName} pvar relative pointer table length must be divisible by 8.");
        }

        var pointers = new WasmDlPvarRelativePointer[relativePointerBytes.Length / entrySize];
        for (int index = 0; index < pointers.Length; index++)
        {
            ReadOnlySpan<byte> entryBytes = relativePointerBytes.AsSpan(index * entrySize, entrySize);
            pointers[index] = new WasmDlPvarRelativePointer(
                BinaryPrimitives.ReadInt32LittleEndian(entryBytes[..4]),
                BinaryPrimitives.ReadInt32LittleEndian(entryBytes[4..]));
        }

        return pointers;
    }
}

public sealed record WasmDlGameplayBlocks(
    string Kind,
    int HeaderSize,
    WasmDlPvarTables? PvarTables,
    WasmDlGameplayBlock[] Blocks);

public sealed record WasmUyaGameplayBlocks(
    string Kind,
    int HeaderSize,
    byte[] HeaderBytes,
    WasmDlPvarTables? PvarTables,
    WasmUyaGameplayBlock[] Blocks);

public sealed record WasmDlPvarTables(
    byte[] MobyLinksBytes,
    byte[] TableBytes,
    byte[] DataBytes,
    byte[] RelativePointerBytes,
    WasmDlPvarTableEntry[] Entries,
    WasmDlPvarRelativePointer[] RelativePointers);

public sealed record WasmDlPvarTableEntry(
    int Index,
    int Offset,
    int Length,
    byte[] Data);

public sealed record WasmDlPvarRelativePointer(
    int PvarIndex,
    int Offset);

public sealed record WasmDlGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    int PayloadLength,
    WasmDlLevelSettings? LevelSettings,
    WasmDlMobyInstances? MobyInstances);

public sealed record WasmUyaGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    int PayloadLength,
    byte[] PayloadBytes,
    WasmUyaLevelSettings? LevelSettings,
    WasmUyaMobyInstances? MobyInstances);

public sealed record WasmDlLevelSettings(
    DlRgb96 BackgroundColor,
    DlRgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity,
    float DeathHeight,
    bool IsSphericalWorld,
    DlVector3 SphereCenter,
    DlVector3 ShipPosition,
    float ShipRotationZ,
    int ShipPath,
    int ShipCameraCuboidStart,
    int ShipCameraCuboidEnd,
    uint Padding58,
    DlLevelSettingsChunkPlane[] ChunkPlanes,
    int CoreSoundsCount,
    int? ThirdPartCount,
    DlLevelSettingsThirdPart[] ThirdPart,
    DlLevelSettingsRewardStats? RewardStats,
    DlLevelSettingsFifthPart? FifthPart,
    int DebugAttackDamageLength,
    int TrailingByteLength);

public sealed record WasmDlMobyInstances(
    int StaticCount,
    int SpawnableMobyCount,
    int Pad8,
    int PadC,
    DlMobyInstance[] Instances,
    int TrailingByteLength);

public sealed record WasmUyaLevelSettings(
    UyaRgb96 BackgroundColor,
    UyaRgb96 FogColor,
    float FogNearDistance,
    float FogFarDistance,
    float FogNearIntensity,
    float FogFarIntensity,
    float DeathHeight,
    bool IsSphericalWorld,
    UyaVector3 SphereCenter,
    UyaVector3 ShipPosition,
    float ShipRotationZ,
    int ShipPath,
    int ShipCameraCuboidStart,
    int ShipCameraCuboidEnd,
    uint Padding58,
    UyaLevelSettingsChunkPlane[] ChunkPlanes,
    int CoreSoundsCount,
    int Rac3ThirdPart,
    int TrailingByteLength);

public sealed record WasmUyaMobyInstances(
    int StaticCount,
    int SpawnableMobyCount,
    int Pad8,
    int PadC,
    UyaMobyInstance[] Instances,
    int TrailingByteLength);

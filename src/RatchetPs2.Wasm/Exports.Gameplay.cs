using Microsoft.JSInterop;
using RatchetPs2.Games.DL.Gameplay;
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

    private static WasmDlGameplayBlocks ToWasmGameplayBlocks(DlGameplayBlocks gameplay)
    {
        return new WasmDlGameplayBlocks(
            gameplay.Kind,
            gameplay.HeaderSize,
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
}

public sealed record WasmDlGameplayBlocks(
    string Kind,
    int HeaderSize,
    WasmDlGameplayBlock[] Blocks);

public sealed record WasmDlGameplayBlock(
    int Index,
    int HeaderOffset,
    int Pointer,
    string SemanticName,
    int PayloadLength,
    WasmDlLevelSettings? LevelSettings,
    WasmDlMobyInstances? MobyInstances);

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

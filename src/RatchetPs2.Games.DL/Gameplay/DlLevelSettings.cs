using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.DL.Gameplay;

public static class DlLevelSettingsReader
{
    public const int MinimumSize = 0x80;

    private const int FirstPartSize = 0x5c;
    private const int ChunkPlaneSize = 0x20;
    private const int ThirdPartSize = 0x10;
    private const int RewardStatsSize = 0x18;
    private const int FifthPartSize = 0x18;

    public static bool TryRead(ReadOnlySpan<byte> data, out DlLevelSettings? settings)
    {
        if (data.Length < MinimumSize)
        {
            settings = null;
            return false;
        }

        settings = Read(data);
        return true;
    }

    public static DlLevelSettings Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, MinimumSize, "DL level settings");

        var chunkPlaneCount = ReadInt32LittleEndian(data, FirstPartSize + 0x0c);
        var chunkPlaneCursor = FirstPartSize;
        var chunkPlanes = new List<DlLevelSettingsChunkPlane>();
        if (chunkPlaneCount > 0)
        {
            EnsureRange(data, chunkPlaneCursor, checked(chunkPlaneCount * ChunkPlaneSize), "DL level settings chunk planes");
            for (var i = 0; i < chunkPlaneCount; i++)
            {
                chunkPlanes.Add(ReadChunkPlane(data, chunkPlaneCursor));
                chunkPlaneCursor += ChunkPlaneSize;
            }
        }
        else
        {
            chunkPlaneCursor += ChunkPlaneSize;
        }

        var cursor = chunkPlaneCursor;
        var coreSoundsCount = ReadInt32LittleEndian(data, cursor);
        cursor += sizeof(int);

        int? thirdPartCount = null;
        var thirdPart = new List<DlLevelSettingsThirdPart>();
        DlLevelSettingsRewardStats? rewardStats = null;
        DlLevelSettingsFifthPart? fifthPart = null;
        byte[] debugAttackDamage = [];
        byte[] trailingBytes = [];

        if (cursor < data.Length)
        {
            thirdPartCount = ReadInt32LittleEndian(data, cursor);
            cursor += sizeof(int);

            if (thirdPartCount.Value >= 0)
            {
                EnsureRange(data, cursor, checked(thirdPartCount.Value * ThirdPartSize), "DL level settings third part");
                for (var i = 0; i < thirdPartCount.Value; i++)
                {
                    thirdPart.Add(ReadThirdPart(data, cursor));
                    cursor += ThirdPartSize;
                }

                rewardStats = ReadRewardStats(data, cursor);
                cursor += RewardStatsSize;
            }
            else
            {
                cursor += ThirdPartSize;
            }

            fifthPart = ReadFifthPart(data, cursor);
            cursor += FifthPartSize;

            var debugAttackDamageCount = ReadInt32LittleEndian(data, cursor);
            cursor += sizeof(int);
            debugAttackDamage = SliceToArray(
                data,
                cursor,
                debugAttackDamageCount,
                "DL level settings debug attack damage");
            cursor += debugAttackDamageCount;
            trailingBytes = data[cursor..].ToArray();
        }

        return new DlLevelSettings(
            ReadRgb(data, 0x00),
            ReadRgb(data, 0x0c),
            ReadSingleLittleEndian(data, 0x18),
            ReadSingleLittleEndian(data, 0x1c),
            ReadSingleLittleEndian(data, 0x20),
            ReadSingleLittleEndian(data, 0x24),
            ReadSingleLittleEndian(data, 0x28),
            ReadInt32LittleEndian(data, 0x2c) != 0,
            ReadVector3(data, 0x30),
            ReadVector3(data, 0x3c),
            ReadSingleLittleEndian(data, 0x48),
            ReadInt32LittleEndian(data, 0x4c),
            ReadInt32LittleEndian(data, 0x50),
            ReadInt32LittleEndian(data, 0x54),
            ReadUInt32LittleEndian(data, 0x58),
            chunkPlanes,
            coreSoundsCount,
            thirdPartCount,
            thirdPart,
            rewardStats,
            fifthPart,
            debugAttackDamage,
            trailingBytes);
    }

    private static DlRgb96 ReadRgb(ReadOnlySpan<byte> data, int offset)
    {
        return new DlRgb96(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8));
    }

    private static DlVector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new DlVector3(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8));
    }

    private static DlLevelSettingsChunkPlane ReadChunkPlane(ReadOnlySpan<byte> data, int offset)
    {
        return new DlLevelSettingsChunkPlane(
            ReadVector3(data, offset),
            ReadInt32LittleEndian(data, offset + 0x0c),
            ReadVector3(data, offset + 0x10),
            ReadUInt32LittleEndian(data, offset + 0x1c));
    }

    private static DlLevelSettingsThirdPart ReadThirdPart(ReadOnlySpan<byte> data, int offset)
    {
        return new DlLevelSettingsThirdPart(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8),
            ReadInt32LittleEndian(data, offset + 0x0c));
    }

    private static DlLevelSettingsRewardStats ReadRewardStats(ReadOnlySpan<byte> data, int offset)
    {
        return new DlLevelSettingsRewardStats(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8),
            ReadSingleLittleEndian(data, offset + 0x0c),
            ReadInt32LittleEndian(data, offset + 0x10),
            ReadInt32LittleEndian(data, offset + 0x14));
    }

    private static DlLevelSettingsFifthPart ReadFifthPart(ReadOnlySpan<byte> data, int offset)
    {
        return new DlLevelSettingsFifthPart(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8),
            ReadInt32LittleEndian(data, offset + 0x0c),
            ReadInt32LittleEndian(data, offset + 0x10),
            ReadInt32LittleEndian(data, offset + 0x14));
    }
}

public sealed record DlLevelSettings(
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
    IReadOnlyList<DlLevelSettingsChunkPlane> ChunkPlanes,
    int CoreSoundsCount,
    int? ThirdPartCount,
    IReadOnlyList<DlLevelSettingsThirdPart> ThirdPart,
    DlLevelSettingsRewardStats? RewardStats,
    DlLevelSettingsFifthPart? FifthPart,
    byte[] DebugAttackDamage,
    byte[] TrailingBytes);

public readonly record struct DlRgb96(int Red, int Green, int Blue);

public readonly record struct DlVector3(float X, float Y, float Z);

public readonly record struct DlLevelSettingsChunkPlane(
    DlVector3 Point,
    int PlaneCount,
    DlVector3 Normal,
    uint Padding);

public readonly record struct DlLevelSettingsThirdPart(
    int Unknown0,
    int Unknown4,
    int Unknown8,
    int UnknownC);

public readonly record struct DlLevelSettingsRewardStats(
    float XpDecayRate,
    float XpDecayMin,
    float BoltDecayRate,
    float BoltDecayMin,
    int Unknown10,
    int Unknown14);

public readonly record struct DlLevelSettingsFifthPart(
    int Unknown0,
    int MobyInstanceCount,
    int Unknown8,
    int UnknownC,
    int Unknown10,
    int DebugHitPoints);

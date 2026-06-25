using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.DL.Gameplay;

public static class DlMobyInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x70;

    public static bool TryRead(ReadOnlySpan<byte> data, out DlMobyInstances? mobyInstances)
    {
        if (data.Length < HeaderSize)
        {
            mobyInstances = null;
            return false;
        }

        mobyInstances = Read(data);
        return true;
    }

    public static DlMobyInstances Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "DL moby instances header");

        var staticCount = ReadInt32LittleEndian(data, 0x00);
        if (staticCount < 0)
        {
            throw new InvalidDataException("DL moby instances static count cannot be negative.");
        }

        var recordsLength = checked(staticCount * RecordSize);
        EnsureRange(data, HeaderSize, recordsLength, "DL moby instance records");

        var instances = new List<DlMobyInstance>(staticCount);
        for (var i = 0; i < staticCount; i++)
        {
            instances.Add(ReadInstance(data, HeaderSize + (i * RecordSize)));
        }

        var tailOffset = HeaderSize + recordsLength;
        return new DlMobyInstances(
            staticCount,
            ReadInt32LittleEndian(data, 0x04),
            ReadInt32LittleEndian(data, 0x08),
            ReadInt32LittleEndian(data, 0x0c),
            instances,
            data[tailOffset..].ToArray());
    }

    private static DlMobyInstance ReadInstance(ReadOnlySpan<byte> data, int offset)
    {
        return new DlMobyInstance(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 0x04),
            ReadInt32LittleEndian(data, offset + 0x08),
            ReadInt32LittleEndian(data, offset + 0x0c),
            ReadInt32LittleEndian(data, offset + 0x10),
            ReadSingleLittleEndian(data, offset + 0x14),
            ReadInt32LittleEndian(data, offset + 0x18),
            ReadInt32LittleEndian(data, offset + 0x1c),
            ReadInt32LittleEndian(data, offset + 0x20),
            ReadInt32LittleEndian(data, offset + 0x24),
            ReadVector3(data, offset + 0x28),
            ReadVector3(data, offset + 0x34),
            ReadInt32LittleEndian(data, offset + 0x40),
            ReadInt32LittleEndian(data, offset + 0x44),
            ReadSingleLittleEndian(data, offset + 0x48),
            ReadInt32LittleEndian(data, offset + 0x4c),
            ReadInt32LittleEndian(data, offset + 0x50),
            ReadInt32LittleEndian(data, offset + 0x54),
            ReadInt32LittleEndian(data, offset + 0x58),
            ReadRgb(data, offset + 0x5c),
            ReadInt32LittleEndian(data, offset + 0x68),
            ReadInt32LittleEndian(data, offset + 0x6c));
    }

    private static DlVector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new DlVector3(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8));
    }

    private static DlRgb96 ReadRgb(ReadOnlySpan<byte> data, int offset)
    {
        return new DlRgb96(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8));
    }
}

public sealed record DlMobyInstances(
    int StaticCount,
    int SpawnableMobyCount,
    int Pad8,
    int PadC,
    IReadOnlyList<DlMobyInstance> Instances,
    byte[] TrailingBytes);

public sealed record DlMobyInstance(
    int Size,
    int Mission,
    int Uid,
    int Bolts,
    int ClassId,
    float Scale,
    int DrawDistance,
    int UpdateDistance,
    int Unused20,
    int Unused24,
    DlVector3 Position,
    DlVector3 Rotation,
    int Group,
    int IsRooted,
    float RootedDistance,
    int Unused4C,
    int PvarIndex,
    int Occlusion,
    int ModeBits,
    DlRgb96 Color,
    int Light,
    int Unused6C);

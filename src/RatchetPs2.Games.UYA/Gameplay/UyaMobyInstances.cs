using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaMobyInstancesReader
{
    public const int HeaderSize = 0x10;
    public const int RecordSize = 0x88;

    public static bool TryRead(ReadOnlySpan<byte> data, out UyaMobyInstances? mobyInstances)
    {
        if (data.Length < HeaderSize)
        {
            mobyInstances = null;
            return false;
        }

        mobyInstances = Read(data);
        return true;
    }

    public static UyaMobyInstances Read(ReadOnlySpan<byte> data)
    {
        EnsureRange(data, 0, HeaderSize, "UYA moby instances header");

        var staticCount = ReadInt32LittleEndian(data, 0x00);
        if (staticCount < 0)
        {
            throw new InvalidDataException("UYA moby instances static count cannot be negative.");
        }

        var recordsLength = checked(staticCount * RecordSize);
        EnsureRange(data, HeaderSize, recordsLength, "UYA moby instance records");

        var instances = new List<UyaMobyInstance>(staticCount);
        for (var i = 0; i < staticCount; i++)
        {
            instances.Add(ReadInstance(data, HeaderSize + (i * RecordSize)));
        }

        var tailOffset = HeaderSize + recordsLength;
        return new UyaMobyInstances(
            staticCount,
            ReadInt32LittleEndian(data, 0x04),
            ReadInt32LittleEndian(data, 0x08),
            ReadInt32LittleEndian(data, 0x0c),
            instances,
            data[tailOffset..].ToArray());
    }

    private static UyaMobyInstance ReadInstance(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaMobyInstance(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 0x04),
            ReadInt32LittleEndian(data, offset + 0x08),
            ReadInt32LittleEndian(data, offset + 0x0c),
            ReadInt32LittleEndian(data, offset + 0x10),
            ReadInt32LittleEndian(data, offset + 0x14),
            ReadInt32LittleEndian(data, offset + 0x18),
            ReadInt32LittleEndian(data, offset + 0x1c),
            ReadInt32LittleEndian(data, offset + 0x20),
            ReadInt32LittleEndian(data, offset + 0x24),
            ReadInt32LittleEndian(data, offset + 0x28),
            ReadSingleLittleEndian(data, offset + 0x2c),
            ReadInt32LittleEndian(data, offset + 0x30),
            ReadInt32LittleEndian(data, offset + 0x34),
            ReadInt32LittleEndian(data, offset + 0x38),
            ReadInt32LittleEndian(data, offset + 0x3c),
            ReadVector3(data, offset + 0x40),
            ReadVector3(data, offset + 0x4c),
            ReadInt32LittleEndian(data, offset + 0x58),
            ReadInt32LittleEndian(data, offset + 0x5c),
            ReadSingleLittleEndian(data, offset + 0x60),
            ReadInt32LittleEndian(data, offset + 0x64),
            ReadInt32LittleEndian(data, offset + 0x68),
            ReadInt32LittleEndian(data, offset + 0x6c),
            ReadInt32LittleEndian(data, offset + 0x70),
            ReadRgb(data, offset + 0x74),
            ReadInt32LittleEndian(data, offset + 0x80),
            ReadInt32LittleEndian(data, offset + 0x84));
    }

    private static UyaVector3 ReadVector3(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaVector3(
            ReadSingleLittleEndian(data, offset),
            ReadSingleLittleEndian(data, offset + 4),
            ReadSingleLittleEndian(data, offset + 8));
    }

    private static UyaRgb96 ReadRgb(ReadOnlySpan<byte> data, int offset)
    {
        return new UyaRgb96(
            ReadInt32LittleEndian(data, offset),
            ReadInt32LittleEndian(data, offset + 4),
            ReadInt32LittleEndian(data, offset + 8));
    }
}

public sealed record UyaMobyInstances(
    int StaticCount,
    int SpawnableMobyCount,
    int Pad8,
    int PadC,
    IReadOnlyList<UyaMobyInstance> Instances,
    byte[] TrailingBytes);

public sealed record UyaMobyInstance(
    int Size,
    int Mission,
    int Unknown8,
    int UnknownC,
    int Uid,
    int Bolts,
    int Unknown18,
    int Unknown1C,
    int Unknown20,
    int Unknown24,
    int ClassId,
    float Scale,
    int DrawDistance,
    int UpdateDistance,
    int Unused38,
    int Unused3C,
    UyaVector3 Position,
    UyaVector3 Rotation,
    int Group,
    int IsRooted,
    float RootedDistance,
    int Unknown64,
    int PvarIndex,
    int Occlusion,
    int ModeBits,
    UyaRgb96 Color,
    int Light,
    int Unknown84);

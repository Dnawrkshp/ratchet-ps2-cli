using System.Buffers.Binary;

namespace RatchetPs2.Core.IO;

public static class BinarySpanReader
{
    public static int CheckedOffset(uint offset, string name)
    {
        if (offset > int.MaxValue)
        {
            throw new InvalidDataException($"{name} offset 0x{offset:X8} exceeds supported stream size.");
        }

        return (int)offset;
    }

    public static void EnsureRange(ReadOnlySpan<byte> bytes, int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length || length > bytes.Length - offset)
        {
            throw new InvalidDataException(
                $"{context} range 0x{offset:X}+0x{length:X} exceeds available length 0x{bytes.Length:X}.");
        }
    }

    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> bytes, int offset, int length, string context)
    {
        EnsureRange(bytes, offset, length, context);
        return bytes.Slice(offset, length);
    }

    public static byte[] SliceToArray(ReadOnlySpan<byte> bytes, int offset, int length, string context)
    {
        return Slice(bytes, offset, length, context).ToArray();
    }

    public static short ReadInt16LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(Slice(bytes, offset, sizeof(short), "Int16"));
    }

    public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(Slice(bytes, offset, sizeof(ushort), "UInt16"));
    }

    public static int ReadInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(Slice(bytes, offset, sizeof(int), "Int32"));
    }

    public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, sizeof(uint), "UInt32"));
    }

    public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(Slice(bytes, offset, sizeof(ulong), "UInt64"));
    }

    public static float ReadSingleLittleEndian(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadSingleLittleEndian(Slice(bytes, offset, sizeof(float), "Single"));
    }
}

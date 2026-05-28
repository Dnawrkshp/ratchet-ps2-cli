using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static ushort ReadLowHalfword(byte[] block)
    {
        return BitConverter.ToUInt16(block, 0x00);
    }

    private static void WriteLow9Bits(byte[] block, ushort value)
    {
        var current = BitConverter.ToUInt16(block, 0x00);
        var next = (ushort)((current & ~0x01FF) | (value & 0x01FF));
        var bytes = BitConverter.GetBytes(next);
        block[0] = bytes[0];
        block[1] = bytes[1];
    }

    private static void WriteLow9Bits(byte[] data, int offset, ushort value)
    {
        var current = BitConverter.ToUInt16(data, offset);
        var next = (ushort)((current & ~0x01FF) | (value & 0x01FF));
        WriteUInt16(data, offset, next);
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        data[offset] = bytes[0];
        data[offset + 1] = bytes[1];
    }

    private static void WriteInt16(byte[] data, int offset, short value)
    {
        var bytes = BitConverter.GetBytes(value);
        data[offset] = bytes[0];
        data[offset + 1] = bytes[1];
    }

    private static byte[] Combine(byte[] first, byte[]? second)
    {
        if (second is null || second.Length == 0)
        {
            return (byte[])first.Clone();
        }

        var combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static void SplitCombinedVifData(byte[] combined, int vifDataLength, out byte[] vifData, out byte[]? vifTextureData)
    {
        vifData = combined[..vifDataLength];
        vifTextureData = combined.Length > vifDataLength
            ? combined[vifDataLength..]
            : null;
    }

    private static void Align(BinaryWriter writer, int alignment)
    {
        var remainder = writer.BaseStream.Position % alignment;
        if (remainder != 0)
        {
            writer.Write(new byte[alignment - remainder]);
        }
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] ToArray(Vector2 value) => [value.X, value.Y];
}

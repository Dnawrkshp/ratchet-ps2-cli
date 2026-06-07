namespace RatchetPs2.Core.Ties;

internal static class TieBinaryReaderUtils
{
    public static int CheckedOffset(uint offset, string name)
    {
        if (offset > int.MaxValue)
        {
            throw new InvalidDataException($"{name} offset 0x{offset:X8} exceeds supported stream size.");
        }

        return (int)offset;
    }

    public static void EnsureRange(byte[] bytes, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length || offset + length > bytes.Length)
        {
            throw new InvalidDataException(
                $"{name} range 0x{offset:X}..0x{offset + length:X} is outside tie class size 0x{bytes.Length:X}.");
        }
    }

    public static byte[] Slice(byte[] bytes, int offset, int length)
    {
        EnsureRange(bytes, offset, length, "byte slice");
        var result = new byte[length];
        Array.Copy(bytes, offset, result, 0, length);
        return result;
    }
}

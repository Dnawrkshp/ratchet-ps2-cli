using System.Buffers.Binary;

namespace RatchetPs2.Core.IO;

public static class StreamExtensions
{
    public static uint ReadUInt32LittleEndian(this Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> buffer = stackalloc byte[sizeof(uint)];
        ReadExactly(stream, buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public static int ReadInt32LittleEndian(this Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> buffer = stackalloc byte[sizeof(int)];
        ReadExactly(stream, buffer);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    public static byte[] ReadBytesExactly(this Stream stream, int length)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var buffer = new byte[length];
        ReadExactly(stream, buffer);
        return buffer;
    }

    public static void ReadExactly(this Stream stream, Span<byte> buffer)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream while reading binary data.");
            }

            totalRead += read;
        }
    }
}
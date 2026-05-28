namespace RatchetPs2.Core.IO.Vif;

public sealed record Ps2VifPacketSpan(
    int Offset,
    ushort Immediate,
    byte Num,
    byte CommandByte,
    int Command,
    bool IsUnpack,
    int PayloadLength,
    int TotalLength);

public static class Ps2VifPacket
{
    public static void WriteHeader(Stream stream, ushort immediate, byte num, byte commandByte)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.WriteByte((byte)(immediate & 0xFF));
        stream.WriteByte((byte)(immediate >> 8));
        stream.WriteByte(num);
        stream.WriteByte(commandByte);
    }

    public static List<Ps2VifPacketSpan> ReadSpans(ReadOnlySpan<byte> vifData)
    {
        var packets = new List<Ps2VifPacketSpan>();
        var offset = 0;
        while (offset + 4 <= vifData.Length)
        {
            var immediate = BitConverter.ToUInt16(vifData[offset..]);
            var num = vifData[offset + 2];
            var commandByte = vifData[offset + 3];
            var command = commandByte & 0x7F;
            var payloadLength = GetPayloadLength(command, num);
            var alignedPayloadLength = Align(payloadLength, 4);
            var availablePayloadLength = vifData.Length - offset - 4;
            if (alignedPayloadLength > availablePayloadLength)
            {
                if (command < 0x60)
                {
                    break;
                }

                alignedPayloadLength = Math.Max(availablePayloadLength, 0);
            }

            var totalLength = 4 + alignedPayloadLength;

            packets.Add(new Ps2VifPacketSpan(
                offset,
                checked((ushort)immediate),
                num,
                commandByte,
                command,
                command >= 0x60,
                alignedPayloadLength,
                totalLength));

            offset += totalLength;
        }

        return packets;
    }

    public static int GetPayloadLength(int command, int num)
    {
        if (command < 0x60)
        {
            return command switch
            {
                0x01 => 4,
                0x02 => 8,
                0x03 => 12,
                0x04 => 16,
                0x05 => num * 4,
                _ => 0
            };
        }

        var unpackNum = num == 0 ? 256 : num;
        var vn = (command >> 2) & 0x03;
        var vl = command & 0x03;
        var componentCount = vn switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            _ => 4
        };
        var bytesPerComponent = vl switch
        {
            0 => 4,
            1 => 2,
            2 => 1,
            _ => 0
        };

        return unpackNum * componentCount * bytesPerComponent;
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }
}

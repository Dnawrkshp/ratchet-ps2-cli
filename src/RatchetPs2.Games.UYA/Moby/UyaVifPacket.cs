namespace RatchetPs2.Games.UYA.Moby;

internal sealed class UyaVifPacket
{
    public int Offset { get; init; }
    public int Command { get; init; }
    public int Immediate { get; init; }
    public int Num { get; init; }
    public int Irq { get; init; }
    public bool IsUnpack { get; init; }
    public int VnVl { get; init; }
    public int ComponentCount { get; init; }
    public int ComponentByteSize { get; init; }
    public int RawPayloadSize { get; init; }
    public int AlignedPayloadSize { get; init; }
    public int DestinationAddr { get; init; }
    public string Kind { get; init; } = "UNKNOWN";
    public byte[] Payload { get; init; } = [];
}

internal static class UyaVifPacketReader
{
    private static readonly IReadOnlyDictionary<int, (string Name, int ComponentCount, int ComponentByteSize, int BytesPerElement)> UnpackFormats =
        new Dictionary<int, (string, int, int, int)>
        {
            [0x0] = ("S_32", 1, 4, 4),
            [0x1] = ("S_16", 1, 2, 2),
            [0x2] = ("S_8", 1, 1, 1),
            [0x3] = ("V2_5", 2, 2, 4),
            [0x4] = ("V2_32", 2, 4, 8),
            [0x5] = ("V2_16", 2, 2, 4),
            [0x6] = ("V2_8", 2, 1, 2),
            [0x7] = ("V3_5", 3, 2, 6),
            [0x8] = ("V3_32", 3, 4, 12),
            [0x9] = ("V3_16", 3, 2, 6),
            [0xA] = ("V3_8", 3, 1, 3),
            [0xB] = ("V4_5", 4, 2, 8),
            [0xC] = ("V4_32", 4, 4, 16),
            [0xD] = ("V4_16", 4, 2, 8),
            [0xE] = ("V4_8", 4, 1, 4),
            [0xF] = ("V4_5_PACKED", 4, 2, 2)
        };

    public static List<UyaVifPacket> Read(byte[] data)
    {
        var packets = new List<UyaVifPacket>();
        if (data.Length < 4)
        {
            return packets;
        }

        var offset = 0;
        while (offset + 4 <= data.Length)
        {
            var packetOffset = offset;
            var imm = BitConverter.ToUInt16(data, offset);
            var numRaw = data[offset + 2];
            var cmdIrq = data[offset + 3];
            offset += 4;

            var command = cmdIrq & 0x7F;
            var irq = (cmdIrq >> 7) & 1;
            var num = numRaw == 0 ? 256 : numRaw;
            var isUnpack = command is >= 0x60 and <= 0x7F;
            var vnvl = isUnpack ? command & 0x0F : 0;
            var format = isUnpack
                ? GetUnpackFormat(vnvl)
                : (Name: "UNKNOWN", ComponentCount: 0, ComponentByteSize: 0, BytesPerElement: 0);
            var rawPayloadSize = isUnpack
                ? format.BytesPerElement * num
                : GetNonUnpackPayloadSize(command, imm, num);
            var alignedPayloadSize = isUnpack
                ? (rawPayloadSize + 3) & ~3
                : rawPayloadSize;
            var safePayloadSize = Math.Max(0, Math.Min(rawPayloadSize, data.Length - offset));
            var payload = safePayloadSize == 0 ? [] : data[offset..(offset + safePayloadSize)];

            packets.Add(new UyaVifPacket
            {
                Offset = packetOffset,
                Command = command,
                Immediate = imm,
                Num = num,
                Irq = irq,
                IsUnpack = isUnpack,
                VnVl = vnvl,
                ComponentCount = format.ComponentCount,
                ComponentByteSize = format.ComponentByteSize,
                RawPayloadSize = rawPayloadSize,
                AlignedPayloadSize = alignedPayloadSize,
                DestinationAddr = imm & 0x03FF,
                Kind = isUnpack ? $"UNPACK_{format.Name}" : GetCommandName(command),
                Payload = payload
            });

            offset += Math.Min(alignedPayloadSize, data.Length - offset);
        }

        return packets;
    }

    private static (string Name, int ComponentCount, int ComponentByteSize, int BytesPerElement) GetUnpackFormat(int vnvl)
    {
        return UnpackFormats.TryGetValue(vnvl, out var format)
            ? format
            : ("UNKNOWN", 0, 0, 0);
    }

    private static int GetNonUnpackPayloadSize(int command, int imm, int num)
    {
        return command switch
        {
            0x20 => 4,
            0x30 => 16,
            0x31 => 16,
            0x4A => num * 8,
            0x50 => imm * 16,
            0x51 => imm * 16,
            _ => 0
        };
    }

    private static string GetCommandName(int command)
    {
        return command switch
        {
            0x00 => "NOP",
            0x01 => "STCYCL",
            0x02 => "OFFSET",
            0x03 => "BASE",
            0x04 => "ITOP",
            0x05 => "STMOD",
            0x06 => "MSKPATH3",
            0x07 => "MARK",
            0x10 => "FLUSHE",
            0x11 => "FLUSH",
            0x13 => "FLUSHA",
            0x14 => "MSCAL",
            0x15 => "MSCALF",
            0x17 => "MSCNT",
            0x20 => "STMASK",
            0x30 => "STROW",
            0x31 => "STCOL",
            0x4A => "MPG",
            0x50 => "DIRECT",
            0x51 => "DIRECTHL",
            _ => "UNKNOWN"
        };
    }
}

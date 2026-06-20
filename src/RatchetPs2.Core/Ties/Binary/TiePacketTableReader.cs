using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TiePacketTableReader
{
    public const int PacketSize = 0x10;

    public static List<TiePacketTable> Read(
        BinaryReader reader,
        byte[] bytes,
        TieClassHeader header)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(header);

        var tables = new List<TiePacketTable>(3);
        for (var lodIndex = 0; lodIndex < 3; lodIndex++)
        {
            var offset = header.PacketTableOffsets[lodIndex];
            var count = header.PacketCounts[lodIndex];
            var packets = new List<TiePacket>(count);

            if (count > 0)
            {
                var tableOffset = CheckedOffset(offset, $"packet table {lodIndex}");
                EnsureRange(bytes, tableOffset, count * PacketSize, $"packet table {lodIndex}");
                reader.BaseStream.Position = tableOffset;

                for (var packetIndex = 0; packetIndex < count; packetIndex++)
                {
                    var dataOffset = reader.ReadUInt32();
                    var absoluteDataOffset = checked((int)(offset + dataOffset));
                    var shaderCount = reader.ReadByte();
                    packets.Add(new TiePacket
                    {
                        LodIndex = lodIndex,
                        PacketIndex = packetIndex,
                        DataOffset = dataOffset,
                        AbsoluteDataOffset = absoluteDataOffset,
                        ShaderCount = shaderCount,
                        BfcDistance = reader.ReadByte(),
                        ControlCount = reader.ReadByte(),
                        ControlSize = reader.ReadByte(),
                        VertexOffset = reader.ReadByte(),
                        VertexSize = reader.ReadByte(),
                        RgbaCount = reader.ReadByte(),
                        MultipassOffset = reader.ReadByte(),
                        ScissorOffset = reader.ReadByte(),
                        ScissorSize = reader.ReadByte(),
                        // DL retail treats packet byte +0x0e as pass flags. See TiePassFlags for the
                        // FUN_00593d90/FUN_00595168 assembly branch citations.
                        PassFlags = reader.ReadByte(),
                        MultipassUvSize = reader.ReadByte(),
                        ShaderSwitchVuAddresses = ReadPacketShaderSwitchVuAddresses(
                            bytes,
                            absoluteDataOffset,
                            shaderCount,
                            $"packet shader switches LOD{lodIndex}[{packetIndex}]"),
                        ShaderReferences = ReadPacketShaderReferences(
                            bytes,
                            absoluteDataOffset,
                            shaderCount,
                            header.TextureCount,
                            $"packet shaders LOD{lodIndex}[{packetIndex}]")
                    });
                }
            }

            tables.Add(new TiePacketTable
            {
                LodIndex = lodIndex,
                Offset = offset,
                Count = count,
                Packets = packets
            });
        }

        return tables;
    }

    private static List<TiePacketShaderReference> ReadPacketShaderReferences(
        byte[] bytes,
        int packetDataOffset,
        int shaderCount,
        int shaderTableCount,
        string rangeDescription)
    {
        if (shaderCount <= 0)
        {
            return [];
        }

        var offset = packetDataOffset + 0x10;
        var length = shaderCount * sizeof(int);
        EnsureRange(bytes, offset, length, rangeDescription);

        var references = new List<TiePacketShaderReference>(shaderCount);
        for (var i = 0; i < shaderCount; i++)
        {
            var shaderByteOffset = BitConverter.ToInt32(bytes, offset + i * sizeof(int));
            var shaderIndex = shaderByteOffset >= 0
                && shaderByteOffset % TieShader.Size == 0
                && shaderByteOffset / TieShader.Size < shaderTableCount
                    ? shaderByteOffset / TieShader.Size
                    : -1;

            references.Add(new TiePacketShaderReference
            {
                Index = i,
                ShaderByteOffset = shaderByteOffset,
                ShaderIndex = shaderIndex
            });
        }

        return references;
    }

    private static List<int> ReadPacketShaderSwitchVuAddresses(
        byte[] bytes,
        int packetDataOffset,
        int shaderCount,
        string rangeDescription)
    {
        if (shaderCount <= 1)
        {
            return [];
        }

        var length = (shaderCount - 1) * sizeof(int);
        EnsureRange(bytes, packetDataOffset, length, rangeDescription);

        var addresses = new List<int>(shaderCount - 1);
        for (var i = 0; i < shaderCount - 1; i++)
        {
            addresses.Add(BitConverter.ToInt32(bytes, packetDataOffset + i * sizeof(int)));
        }

        return addresses;
    }
}

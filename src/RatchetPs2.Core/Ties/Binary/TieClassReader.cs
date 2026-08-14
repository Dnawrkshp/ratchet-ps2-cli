using System.Text;

namespace RatchetPs2.Core.Ties;

public static class TieClassReader
{
    public static TieClass Read(Stream input, TieClassReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var memory = new MemoryStream();
        input.CopyTo(memory);
        return Read(memory.ToArray(), options);
    }

    public static TieClass Read(ReadOnlySpan<byte> data, TieClassReadOptions? options = null)
    {
        options ??= TieClassReadOptions.Default;
        if (data.Length < TieClassHeader.Size)
        {
            throw new InvalidDataException(
                $"Tie class binary is too short for a 0x{TieClassHeader.Size:X} byte header.");
        }

        var bytes = data.ToArray();
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        var header = TieClassHeaderReader.Read(reader);
        var packetTables = TiePacketTableReader.Read(reader, bytes, header);
        var packetDataBlocks = TiePacketDataBlockReader.Read(bytes, header, packetTables);
        var lodTopologies = TieLodTopologyBuilder.Build(packetDataBlocks);
        var vertexNormals = TieVertexNormalReader.Read(bytes, header, options);
        var vertexNormalRemaps = TieVertexNormalReader.ReadRemaps(
            bytes,
            header,
            lodTopologies,
            vertexNormals.Count);
        var rgbaRemapOperations = TieRgbaRemapOperationReader.Read(bytes, header);
        var (glowRgbaRemaps, glowRgbaVertices) = TieGlowRgbaReader.Read(
            bytes,
            header,
            packetTables,
            packetDataBlocks,
            lodTopologies);
        var shaders = TieShaderReader.Read(bytes, header);
        var fileSections = TieRawSectionBuilder.Build(bytes, header, packetTables, shaders);

        return new TieClass
        {
            Header = header,
            ByteLength = bytes.Length,
            PacketTables = packetTables,
            PacketDataBlocks = packetDataBlocks,
            LodTopologies = lodTopologies,
            VertexNormals = vertexNormals,
            VertexNormalRemaps = vertexNormalRemaps,
            RgbaRemapOperations = rgbaRemapOperations,
            GlowRgbaRemaps = glowRgbaRemaps,
            GlowRgbaVertices = glowRgbaVertices,
            Shaders = shaders,
            FileSections = fileSections
        };
    }

    public static string Describe(TieClass tie)
    {
        return TieClassDescriber.Describe(tie);
    }
}

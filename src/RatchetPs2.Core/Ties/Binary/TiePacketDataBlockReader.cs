using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TiePacketDataBlockReader
{
    private const int PacketControlStartQword = 2;

    public static List<TiePacketDataBlock> Read(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketTable> tables,
        TieClassReadOptions options)
    {
        var packets = tables
            .SelectMany(table => table.Packets)
            .Where(packet => packet.DataOffset > 0)
            .OrderBy(packet => packet.AbsoluteDataOffset)
            .ToArray();

        var blocks = new List<TiePacketDataBlock>(packets.Length);
        foreach (var packet in packets)
        {
            var offset = packet.AbsoluteDataOffset;
            var qwordCount = GetPacketQwordCount(packet);
            var length = qwordCount * 0x10;
            EnsureRange(bytes, offset, length, $"packet data LOD{packet.LodIndex}[{packet.PacketIndex}]");
            var blockBytes = Slice(bytes, offset, length);
            var controlRows = TiePacketControlDecoder.DecodeControlRows(bytes, packet);
            var unpackHeader = TiePacketControlDecoder.DecodeUnpackHeader(controlRows);
            var stripControls = TiePacketControlDecoder.DecodeStripControls(bytes, packet, controlRows);
            var setupRows = TiePacketControlDecoder.DecodeSetupRows(bytes, packet);
            var vertexRows = TiePacketVertexDecoder.DecodeVertexRows(bytes, header, packet, unpackHeader);
            var decodedVertices = TiePacketVertexDecoder.DecodePacketVertices(bytes, packet, unpackHeader, vertexRows);
            var physicalPrimitives = TiePacketPrimitiveDecoder.DecodePacketPrimitives(
                setupRows,
                stripControls,
                decodedVertices,
                useStripTokenReferences: false);
            var tokenReferencePrimitives = TiePacketPrimitiveDecoder.DecodePacketPrimitives(
                setupRows,
                stripControls,
                decodedVertices,
                useStripTokenReferences: true);
            var primitives = options.UseStripTokenReferencesForTopology
                ? tokenReferencePrimitives
                : physicalPrimitives;
            blocks.Add(new TiePacketDataBlock
            {
                LodIndex = packet.LodIndex,
                PacketIndex = packet.PacketIndex,
                Offset = offset,
                Length = length,
                QwordCount = qwordCount,
                Bytes = blockBytes,
                Regions = BuildPacketDataRegions(bytes, packet, qwordCount),
                SetupRows = setupRows,
                UnpackHeader = unpackHeader,
                ControlRows = controlRows,
                StripControls = stripControls,
                StripTokens = stripControls.SelectMany(strip => strip.DecodedTokens).ToArray(),
                ScissorTokens = TiePacketControlDecoder.DecodeScissorTokens(bytes, packet, stripControls),
                VertexRows = vertexRows,
                DecodedVertices = decodedVertices,
                PhysicalPrimitives = physicalPrimitives,
                TokenReferencePrimitives = tokenReferencePrimitives,
                Primitives = primitives
            });
        }

        return blocks;
    }

    public static int GetPacketQwordCount(TiePacket packet)
    {
        var qwords = 0;
        Consider(packet.VertexOffset, packet.VertexSize);
        Consider(packet.ScissorOffset, packet.ScissorSize);

        if (packet.MultipassOffset > 0 && packet.MultipassUvSize > 0)
        {
            Consider(
                packet.MultipassOffset,
                TiePassFlags.GeneratedEnvPassHeaderQwords + packet.MultipassUvSize);
        }

        return qwords;

        void Consider(byte offset, int count)
        {
            if (count == 0)
            {
                return;
            }

            qwords = Math.Max(qwords, offset + count);
        }
    }

    private static List<TiePacketDataRegion> BuildPacketDataRegions(
        byte[] bytes,
        TiePacket packet,
        int qwordCount)
    {
        var regions = new List<TiePacketDataRegion>();
        AddRegion("setup-rows", 0, TiePacketControlDecoder.PacketSetupQwordCount);
        AddRegion("control-region", PacketControlStartQword, packet.VertexOffset - PacketControlStartQword);
        AddRegion("vertex-rows", packet.VertexOffset, packet.VertexSize);
        AddRegion("scissor-rows", packet.ScissorOffset, packet.ScissorSize);

        if (packet.MultipassOffset > 0 && packet.MultipassUvSize > 0)
        {
            AddRegion(
                "multipass-uv",
                packet.MultipassOffset,
                TiePassFlags.GeneratedEnvPassHeaderQwords + packet.MultipassUvSize);
        }

        return regions
            .OrderBy(region => region.QwordOffset)
            .ThenBy(region => region.Name, StringComparer.Ordinal)
            .ToList();

        void AddRegion(string name, int qwordOffset, int regionQwordCount)
        {
            if (regionQwordCount <= 0 || qwordOffset >= qwordCount)
            {
                return;
            }

            var clampedQwordCount = Math.Min(regionQwordCount, qwordCount - qwordOffset);
            var offset = packet.AbsoluteDataOffset + qwordOffset * 0x10;
            var length = clampedQwordCount * 0x10;
            regions.Add(new TiePacketDataRegion
            {
                Name = name,
                QwordOffset = qwordOffset,
                QwordCount = clampedQwordCount,
                Offset = offset,
                Length = length,
                Bytes = Slice(bytes, offset, length)
            });
        }
    }
}

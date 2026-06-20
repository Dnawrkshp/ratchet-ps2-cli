using System.Buffers.Binary;
using System.Numerics;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfTexCoordBuilder
{
    private const float PacketTextureCoordinateWrapPeriod = 16f;
    private const float MultipassTextureCoordinateWrapPeriod = 1f;
    private const int GsUvFixedPointMask = 0x3FFF;
    private const float GsUvFixedPointScale = 16384f;

    public static List<Vector2> BuildTexCoords(TieClass tie, TieLodTopology topology)
    {
        var texCoords = new List<Vector2>(topology.LogicalVertices.Count);
        var rowsByPacketIndex = tie.PacketDataBlocks
            .Where(block => block.LodIndex == topology.LodIndex)
            .ToDictionary(block => block.PacketIndex, block => block.VertexRows);

        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (vertex.DecodedVertex is null && vertex.VertexRow is null && vertex.AddressRow is null)
            {
                throw new InvalidDataException(
                    $"Tie LOD {topology.LodIndex} logical vertex {vertex.LogicalVertexIndex} has no decoded vertex.");
            }

            rowsByPacketIndex.TryGetValue(vertex.PacketIndex, out var packetRows);
            texCoords.Add(ToGltfTexCoord(vertex, packetRows));
        }

        UnwrapPacketTexCoords(topology, texCoords);
        return texCoords;
    }

    public static List<Vector2> BuildMultipassTexCoords(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector2> fallbackTexCoords)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(fallbackTexCoords);

        var texCoords = fallbackTexCoords.ToList();
        if (texCoords.Count != topology.LogicalVertices.Count)
        {
            return [];
        }

        var assignedTexCoords = new bool[texCoords.Count];
        var blocksByPacketIndex = tie.PacketDataBlocks
            .Where(block => block.LodIndex == topology.LodIndex)
            .ToDictionary(block => block.PacketIndex);
        var packetsByIndex = tie.PacketTables
            .FirstOrDefault(table => table.LodIndex == topology.LodIndex)
            ?.Packets
            .ToDictionary(packet => packet.PacketIndex)
            ?? [];
        var assignedAny = false;

        foreach (var packetGroup in topology.Strips.GroupBy(strip => strip.PacketIndex).OrderBy(group => group.Key))
        {
            if (!packetsByIndex.TryGetValue(packetGroup.Key, out var packet)
                || packet.PassFlags == 0
                || !blocksByPacketIndex.TryGetValue(packetGroup.Key, out var block))
            {
                continue;
            }

            if (TiePassFlags.UsesEnvironmentPass(packet.PassFlags))
            {
                // DL envpass packets do not store ready-made UV1 values here. Retail
                // FUN_00595168 branches on pass bits 1-2 at 0x00595618/0x0059561c,
                // sets s5 to multipass+0x30 at 0x005959c0, writes uvSize-derived DMA
                // tags at 0x00595a40-0x00595a5c, then calls the generated-UV helper
                // at 0x00595a64. Treating that payload as fixed-point GS UV words
                // creates the tall barcode/window reflection artifacts in the viewer.
                continue;
            }

            var uvWords = DecodeMultipassUvWords(block);
            if (uvWords.Count == 0)
            {
                continue;
            }

            var uvIndex = 0;
            foreach (var strip in packetGroup.OrderBy(strip => strip.PacketStripIndex))
            {
                foreach (var vertex in strip.LogicalVertices)
                {
                    texCoords[vertex.LogicalVertexIndex] = uvWords[uvIndex % uvWords.Count];
                    uvIndex++;
                    assignedTexCoords[vertex.LogicalVertexIndex] = true;
                    assignedAny = true;
                }
            }
        }

        if (assignedAny)
        {
            UnwrapPacketTexCoords(
                topology,
                texCoords,
                MultipassTextureCoordinateWrapPeriod,
                assignedTexCoords);
        }

        return assignedAny ? texCoords : [];
    }

    private static void UnwrapPacketTexCoords(
        TieLodTopology topology,
        List<Vector2> texCoords,
        float wrapPeriod = PacketTextureCoordinateWrapPeriod,
        IReadOnlyList<bool>? assignedTexCoords = null)
    {
        foreach (var strip in topology.Strips)
        {
            if (strip.LogicalVertices.Count < 2)
            {
                continue;
            }

            var previousIndex = strip.LogicalVertices[0].LogicalVertexIndex;
            if (!IsAssignedTexCoord(previousIndex))
            {
                continue;
            }

            var previous = texCoords[previousIndex];
            for (var i = 1; i < strip.LogicalVertices.Count; i++)
            {
                var index = strip.LogicalVertices[i].LogicalVertexIndex;
                if (!IsAssignedTexCoord(index))
                {
                    break;
                }

                var current = texCoords[index];
                current = new Vector2(
                    UnwrapPacketTexCoordComponent(current.X, previous.X, wrapPeriod),
                    UnwrapPacketTexCoordComponent(current.Y, previous.Y, wrapPeriod));
                texCoords[index] = current;
                previous = current;
            }
        }

        bool IsAssignedTexCoord(int index)
        {
            return assignedTexCoords is null
                || (index >= 0 && index < assignedTexCoords.Count && assignedTexCoords[index]);
        }
    }

    private static float UnwrapPacketTexCoordComponent(float value, float previous, float wrapPeriod)
    {
        var delta = value - previous;
        while (delta > wrapPeriod / 2f)
        {
            value -= wrapPeriod;
            delta = value - previous;
        }

        while (delta < -wrapPeriod / 2f)
        {
            value += wrapPeriod;
            delta = value - previous;
        }

        return value;
    }

    private static Vector2 ToGltfTexCoord(TieLogicalVertex vertex, IReadOnlyList<TiePacketVertexRow>? packetRows)
    {
        if (vertex.DecodedVertex is { } decodedVertex)
        {
            return new Vector2(decodedVertex.S / 4096f, decodedVertex.T / 4096f);
        }

        if (!TrySelectTextureCoordinate(vertex.VertexRow, packetRows, out var u, out var v)
            && !TrySelectTextureCoordinate(vertex.AddressRow, packetRows, out u, out v))
        {
            u = 0;
            v = 0;
        }

        return new Vector2(u / 4096f, v / 4096f);
    }

    private static bool TrySelectTextureCoordinate(
        TiePacketVertexRow? row,
        IReadOnlyList<TiePacketVertexRow>? packetRows,
        out short u,
        out short v)
    {
        if (row is not null
            && TiePacketVertexRowClassifier.UsesSecondPositionSlot(row)
            && TrySelectAdjacentTextureCoordinate(row, packetRows, out u, out v))
        {
            return true;
        }

        return TrySelectTextureCoordinate(row, out u, out v);
    }

    private static bool TrySelectTextureCoordinate(TiePacketVertexRow? row, out short u, out short v)
    {
        if (row is null)
        {
            u = 0;
            v = 0;
            return false;
        }

        if (row.Data2 == 4096)
        {
            u = row.Data0;
            v = row.Data1;
            return true;
        }

        if (IsLikelyInlineTextureCoordinate(row))
        {
            u = row.X;
            v = row.Y;
            return true;
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool TrySelectAdjacentTextureCoordinate(
        TiePacketVertexRow row,
        IReadOnlyList<TiePacketVertexRow>? packetRows,
        out short u,
        out short v)
    {
        if (packetRows is not null)
        {
            if (TrySelectTextureCoordinateAt(packetRows, row.Index + 1, out u, out v)
                || TrySelectTextureCoordinateAt(packetRows, row.Index - 1, out u, out v))
            {
                return true;
            }
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool TrySelectTextureCoordinateAt(
        IReadOnlyList<TiePacketVertexRow> packetRows,
        int index,
        out short u,
        out short v)
    {
        if (index >= 0 && index < packetRows.Count)
        {
            return TrySelectTextureCoordinate(packetRows[index], out u, out v);
        }

        u = 0;
        v = 0;
        return false;
    }

    private static bool IsLikelyInlineTextureCoordinate(TiePacketVertexRow row)
    {
        return row.Z == 4096
            || (Math.Abs((int)row.X) <= 8192
                && Math.Abs((int)row.Y) <= 8192
                && row.Z is 0 or 14);
    }

    private static List<Vector2> DecodeMultipassUvWords(TiePacketDataBlock block)
    {
        var region = block.Regions.FirstOrDefault(region => region.Name == "multipass-uv");
        if (region is null || region.QwordCount <= TiePassFlags.GeneratedEnvPassHeaderQwords)
        {
            return [];
        }

        var offset = TiePassFlags.GeneratedEnvPassHeaderQwords * TiePassFlags.QwordSize;
        var wordCount = (region.Bytes.Length - offset) / sizeof(uint);
        var values = new List<Vector2>(wordCount);
        for (var i = 0; i < wordCount; i++)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(region.Bytes.AsSpan(offset + i * sizeof(uint)));
            values.Add(new Vector2(
                (word & GsUvFixedPointMask) / GsUvFixedPointScale,
                ((word >> 16) & GsUvFixedPointMask) / GsUvFixedPointScale));
        }

        return values;
    }
}

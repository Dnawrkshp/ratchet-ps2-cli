using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TieRawSectionBuilder
{
    public static List<TieRawSection> Build(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketTable> tables,
        IReadOnlyList<TieShader> shaders)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(shaders);

        var labelsByOffset = new SortedDictionary<int, List<string>>();

        Mark(labelsByOffset, 0, "header");
        Mark(labelsByOffset, TieClassHeader.Size, "post-header");
        Mark(labelsByOffset, bytes.Length, "eof");
        MarkOffset(labelsByOffset, bytes, header.ShadersOffset, "shaders");
        if (shaders.Count > 0)
        {
            Mark(labelsByOffset, shaders[^1].Offset + TieShader.Size, "post-shaders");
        }

        MarkOffset(labelsByOffset, bytes, header.AmbientRgbaOffset, "ambient-rgbas");
        if (header.AmbientRgbaOffset > 0 && header.AmbientSize > 0)
        {
            Mark(labelsByOffset, CheckedOffset(header.AmbientRgbaOffset, "ambient RGBA") + header.AmbientSize, "post-ambient-rgbas");
        }

        MarkOffset(labelsByOffset, bytes, header.VertexNormalsOffset, "vertex-normals");
        for (var i = 0; i < header.RgbaRemapOffsets.Length; i++)
        {
            MarkOffset(labelsByOffset, bytes, header.RgbaRemapOffsets[i], $"rgba-remap-{i}");
        }

        for (var i = 0; i < header.GlowRemapOffsets.Length; i++)
        {
            MarkOffset(labelsByOffset, bytes, header.GlowRemapOffsets[i], $"glow-remap-{i}");
        }

        foreach (var table in tables)
        {
            if (table.Offset > 0 || table.Count > 0)
            {
                var tableOffset = CheckedOffset(table.Offset, $"packet table {table.LodIndex}");
                Mark(labelsByOffset, tableOffset, $"packet-table-lod{table.LodIndex}");
                Mark(labelsByOffset, tableOffset + table.Count * TiePacketTableReader.PacketSize, $"post-packet-table-lod{table.LodIndex}");
            }

            foreach (var packet in table.Packets)
            {
                var qwordCount = TiePacketDataBlockReader.GetPacketQwordCount(packet);
                Mark(labelsByOffset, packet.AbsoluteDataOffset, $"packet-data-lod{packet.LodIndex}-{packet.PacketIndex}");
                Mark(
                    labelsByOffset,
                    packet.AbsoluteDataOffset + qwordCount * 0x10,
                    $"post-packet-data-lod{packet.LodIndex}-{packet.PacketIndex}");
            }
        }

        return BuildSections(bytes, labelsByOffset);
    }

    private static List<TieRawSection> BuildSections(
        byte[] bytes,
        SortedDictionary<int, List<string>> labelsByOffset)
    {
        var boundaries = labelsByOffset.Keys
            .Where(offset => offset >= 0 && offset <= bytes.Length)
            .Distinct()
            .Order()
            .ToArray();
        var sections = new List<TieRawSection>();
        for (var i = 0; i < boundaries.Length - 1; i++)
        {
            var start = boundaries[i];
            var end = boundaries[i + 1];
            if (end <= start)
            {
                continue;
            }

            var name = labelsByOffset.TryGetValue(start, out var labels)
                ? string.Join(", ", labels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                : "unlabeled";
            sections.Add(new TieRawSection
            {
                Name = name,
                Offset = start,
                Length = end - start,
                Bytes = Slice(bytes, start, end - start)
            });
        }

        return sections;
    }

    private static void MarkOffset(
        SortedDictionary<int, List<string>> labelsByOffset,
        byte[] bytes,
        uint offset,
        string label)
    {
        if (offset == 0)
        {
            return;
        }

        var checkedOffset = CheckedOffset(offset, label);
        if (checkedOffset <= bytes.Length)
        {
            Mark(labelsByOffset, checkedOffset, label);
        }
    }

    private static void Mark(SortedDictionary<int, List<string>> labelsByOffset, int offset, string label)
    {
        if (!labelsByOffset.TryGetValue(offset, out var labels))
        {
            labels = [];
            labelsByOffset[offset] = labels;
        }

        labels.Add(label);
    }
}

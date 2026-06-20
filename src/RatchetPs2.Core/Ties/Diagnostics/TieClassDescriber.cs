using System.Text;

namespace RatchetPs2.Core.Ties;

public static class TieClassDescriber
{
    public static string Describe(TieClass tie)
    {
        ArgumentNullException.ThrowIfNull(tie);

        var header = tie.Header;
        var builder = new StringBuilder();
        builder.AppendLine("Tie class");
        builder.AppendLine($"  Size: {tie.ByteLength} bytes");
        builder.AppendLine($"  OClass: 0x{(ushort)header.OClass:X4}");
        builder.AppendLine($"  TClass: 0x{(ushort)header.TClass:X4}");
        builder.AppendLine($"  Texture count: {header.TextureCount}");
        builder.AppendLine($"  Instance index/count: {header.InstanceIndex}/{header.InstanceCount}");
        builder.AppendLine($"  Distances: near={header.NearDistance:G9}, medium={header.MediumDistance:G9}, far={header.FarDistance:G9}");
        builder.AppendLine($"  Scale: {header.Scale:G9}");
        builder.AppendLine($"  Mipmap distance: {header.MipmapDistance:G9}");
        builder.AppendLine($"  Mode bits: 0x{(ushort)header.ModeBits:X4}");
        builder.AppendLine($"  Glow RGBA: 0x{header.GlowRgba:X8}");
        builder.AppendLine(
            $"  Bounding sphere: ({header.BoundingSphere.X:G9}, {header.BoundingSphere.Y:G9}, {header.BoundingSphere.Z:G9}), r={header.BoundingSphere.Radius:G9}");
        builder.AppendLine($"  Shader offset: {FormatOffset(header.ShadersOffset)}");
        builder.AppendLine($"  Ambient RGBA offset/size: {FormatOffset(header.AmbientRgbaOffset)} / {header.AmbientSize}");
        builder.AppendLine($"  Vertex normals offset/count: {FormatOffset(header.VertexNormalsOffset)} / {header.VertexNormalsCount}");
        builder.AppendLine($"  Decoded vertex normals/remaps: {tie.VertexNormals.Count} / {tie.VertexNormalRemaps.Count}");
        builder.AppendLine($"  Decoded glow RGBA remaps/vertices: {tie.GlowRgbaRemaps.Count} / {tie.GlowRgbaVertices.Count}");
        builder.AppendLine($"  RGBA remap offsets: {FormatOffsets(header.RgbaRemapOffsets)}");
        builder.AppendLine($"  Glow remap offsets: {FormatOffsets(header.GlowRemapOffsets)}");
        builder.AppendLine();

        builder.AppendLine("LODs");
        for (var i = 0; i < header.Lods.Length; i++)
        {
            var lod = header.Lods[i];
            builder.AppendLine(
                $"  LOD {i}: vertices={lod.VertexCount}, triangles={lod.TriangleCount}, strips={lod.StripCount}, packetTable={FormatOffset(header.PacketTableOffsets[i])}, packets={header.PacketCounts[i]}, cacheSize={header.CacheSizes[i]}");
        }
        builder.AppendLine();

        builder.AppendLine("Topology");
        foreach (var topology in tie.LodTopologies)
        {
            if (topology.StripCount == 0 && topology.TriangleCount == 0 && topology.LogicalVertexCount == 0)
            {
                continue;
            }

            builder.AppendLine(
                $"  LOD {topology.LodIndex}: logical vertices={topology.LogicalVertexCount}, mapped={topology.PrimaryAddressMappedLogicalVertexCount + topology.SecondaryAddressMappedLogicalVertexCount} (W={topology.PrimaryAddressMappedLogicalVertexCount}, Data3={topology.SecondaryAddressMappedLogicalVertexCount}, unresolved={topology.UnresolvedLogicalVertexCount}), strips={topology.StripCount}, triangles={topology.TriangleCount}, vertex rows={topology.PacketVertexRowCount}");
        }
        builder.AppendLine();

        builder.AppendLine("Packet tables");
        foreach (var table in tie.PacketTables)
        {
            builder.AppendLine($"  LOD {table.LodIndex}: offset={FormatOffset(table.Offset)}, count={table.Packets.Count}");
            foreach (var packet in table.Packets)
            {
                var shaderText = packet.ShaderReferences.Count == 0
                    ? packet.ShaderCount.ToString()
                    : $"{packet.ShaderCount} [{string.Join(", ", packet.ShaderReferences.Select(reference => reference.ShaderIndex >= 0 ? reference.ShaderIndex.ToString() : $"0x{reference.ShaderByteOffset:X}"))}]";
                var shaderSwitchText = packet.ShaderSwitchVuAddresses.Count == 0
                    ? string.Empty
                    : $", shaderSwitchVu=[{string.Join(", ", packet.ShaderSwitchVuAddresses)}]";
                builder.AppendLine(
                    $"    [{packet.PacketIndex}] data={FormatOffset(packet.AbsoluteDataOffset)} (relative {FormatOffset(packet.DataOffset)}), shaders={shaderText}{shaderSwitchText}, controls={packet.ControlCount} rows/{packet.ControlSize} qw, vertex={packet.VertexOffset}+{packet.VertexSize}, rgba={packet.RgbaCount}, scissor={packet.ScissorOffset}+{packet.ScissorSize}, multipass={packet.MultipassOffset}/flags={TiePassFlags.FormatByteBits(packet.PassFlags)}/uv={packet.MultipassUvSize}");
            }
        }
        builder.AppendLine();

        builder.AppendLine("Packet data spans");
        builder.AppendLine("  TiePacket.data is relative to its packet table; file sections below split additional known markers.");
        foreach (var block in tie.PacketDataBlocks)
        {
            builder.AppendLine($"  LOD{block.LodIndex}[{block.PacketIndex}] {FormatOffset(block.Offset)}..0x{block.Offset + block.Length:X}: {block.Length} bytes, {block.QwordCount} qw");
            foreach (var region in block.Regions)
            {
                builder.AppendLine(
                    $"    {region.Name}: qword {region.QwordOffset}+{region.QwordCount}, {FormatOffset(region.Offset)}..0x{region.Offset + region.Length:X} ({region.Length} bytes)");
            }

            foreach (var row in block.SetupRows)
            {
                builder.AppendLine(
                    $"    setup row {row.Index}: {string.Join(", ", row.Words.Select(word => $"{word.Role}:{FormatWord(word.Raw)}"))}");
            }

            if (block.UnpackHeader is { } unpackHeader)
            {
                builder.AppendLine(
                    $"    unpack header: strips={unpackHeader.StripCount}, dinky={unpackHeader.DinkyVertexCount}, raw=[{unpackHeader.Unknown0:X2} {unpackHeader.Unknown1:X2} {unpackHeader.Unknown2:X2} {unpackHeader.StripCount:X2} {unpackHeader.Unknown4:X2} {unpackHeader.Unknown5:X2} {unpackHeader.Unknown6:X2} {unpackHeader.Unknown7:X2} {unpackHeader.DinkyVerticesSizePlusFour:X2} {unpackHeader.FatVerticesSize:X2} {unpackHeader.Unknown10:X2} {unpackHeader.Unknown11:X2}]");
            }

            if (block.ControlRows.Count > 0)
            {
                var tokenCount = block.StripControls.Sum(strip => strip.TokenCount);
                var endToken = block.ScissorTokens.FirstOrDefault(token => token.IsEndToken);
                var endTokenText = endToken is null
                    ? "none"
                    : $"{FormatOffset(endToken.Offset)}";
                var topology = tie.LodTopologies.FirstOrDefault(topology => topology.LodIndex == block.LodIndex);
                var blockTriangleCount = topology?.Strips
                    .Where(strip => strip.PacketIndex == block.PacketIndex)
                    .Sum(strip => strip.TriangleCount) ?? 0;
                builder.AppendLine(
                    $"    control rows: {block.ControlRows.Count}, strip controls: {block.StripControls.Count}, scissor tokens: {tokenCount} + end@{endTokenText}, triangles={blockTriangleCount}");
            }

            var blockTopology = tie.LodTopologies.FirstOrDefault(topology => topology.LodIndex == block.LodIndex);
            if (blockTopology is not null)
            {
                var blockLogicalVertices = blockTopology.LogicalVertices
                    .Where(vertex => vertex.PacketIndex == block.PacketIndex)
                    .ToArray();
                if (blockLogicalVertices.Length > 0)
                {
                    var primaryMappedCount = blockLogicalVertices.Count(
                        vertex => vertex.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress);
                    var secondaryMappedCount = blockLogicalVertices.Count(
                        vertex => vertex.MappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress);
                    var unresolvedCount = blockLogicalVertices.Count(
                        vertex => vertex.MappingKind == TieLogicalVertexMappingKind.Unresolved);
                    builder.AppendLine(
                        $"    vertex mapping: logical={blockLogicalVertices.Length}, mapped={primaryMappedCount + secondaryMappedCount} (W={primaryMappedCount}, Data3={secondaryMappedCount}, unresolved={unresolvedCount})");
                }
            }

            if (block.DecodedVertices.Count > 0)
            {
                var decodedKindText = string.Join(
                    ", ",
                    block.DecodedVertices
                        .GroupBy(vertex => vertex.Kind)
                        .OrderBy(group => group.Key)
                        .Select(group => $"{group.Key}={group.Count()}"));
                builder.AppendLine(
                    $"    packet stream: decoded vertices={block.DecodedVertices.Count} ({decodedKindText}), primitives={block.Primitives.Count}");
            }

            if (block.VertexRows.Count > 0)
            {
                var minX = block.VertexRows.Min(row => row.ModelX);
                var minY = block.VertexRows.Min(row => row.ModelY);
                var minZ = block.VertexRows.Min(row => row.ModelZ);
                var maxX = block.VertexRows.Max(row => row.ModelX);
                var maxY = block.VertexRows.Max(row => row.ModelY);
                var maxZ = block.VertexRows.Max(row => row.ModelZ);
                var rowKindText = string.Join(
                    ", ",
                    block.VertexRows
                        .GroupBy(row => row.Kind)
                        .OrderBy(group => group.Key)
                        .Select(group => $"{group.Key}={group.Count()}"));
                builder.AppendLine(
                    $"    raw vertex qwords: {block.VertexRows.Count} ({rowKindText}), first-vector bounds=({minX:G7}, {minY:G7}, {minZ:G7})..({maxX:G7}, {maxY:G7}, {maxZ:G7})");
            }

            var blockGlowVertexCount = tie.GlowRgbaVertices.Count(
                vertex => vertex.LodIndex == block.LodIndex && vertex.PacketIndex == block.PacketIndex);
            if (blockGlowVertexCount > 0)
            {
                builder.AppendLine($"    resolved glow RGBA vertices: {blockGlowVertexCount}");
            }
        }
        builder.AppendLine();

        if (tie.GlowRgbaRemaps.Count > 0)
        {
            builder.AppendLine("Glow RGBA remaps");
            foreach (var remap in tie.GlowRgbaRemaps)
            {
                var location = remap.LodIndex.HasValue && remap.PacketIndex.HasValue
                    ? $"LOD{remap.LodIndex.Value}[{remap.PacketIndex.Value}]"
                    : "unresolved packet";
                var rowRange = remap.StartVertexRowIndex.HasValue && remap.EndVertexRowIndexExclusive.HasValue
                    ? $", vertexRows={remap.StartVertexRowIndex.Value}..{remap.EndVertexRowIndexExclusive.Value - 1}"
                    : string.Empty;
                var offsetRange = remap.ResolvedStartOffset.HasValue && remap.EndOffset.HasValue
                    ? $", resolvedRange={FormatOffset(remap.ResolvedStartOffset.Value)}..{FormatOffset(remap.EndOffset.Value)}"
                    : string.Empty;
                var resolvedPacket = remap.ResolvedPacketIndex.HasValue
                    ? $", resolvedPacket={remap.ResolvedPacketIndex.Value}"
                    : string.Empty;
                var resolvedPackets = remap.ResolvedPacketIndices.Count > 0
                    ? $", resolvedPackets=[{string.Join(", ", remap.ResolvedPacketIndices)}]"
                    : string.Empty;
                var resolvedShader = remap.ResolvedShaderIndex.HasValue
                    ? $", resolvedShader={remap.ResolvedShaderIndex.Value}"
                    : string.Empty;
                builder.AppendLine(
                    $"  [{remap.RemapIndex}] offset={FormatOffset(remap.Offset)}, color={remap.Rgba.ToRgbaHex()}, {location}, {remap.ResolutionKind}{offsetRange}{resolvedPacket}{resolvedPackets}{resolvedShader}{rowRange}, packets={remap.ResolvedPacketCount}, rows={remap.ResolvedVertexRowCount}, logicalVertices={remap.ResolvedLogicalVertexCount}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Shaders");
        foreach (var shader in tie.Shaders)
        {
            builder.AppendLine($"  [{shader.Index}] offset={FormatOffset(shader.Offset)}, size={shader.Bytes.Length}, clampU={shader.ClampU}, clampV={shader.ClampV}");
        }
        builder.AppendLine();

        builder.AppendLine("File sections");
        foreach (var section in tie.FileSections)
        {
            builder.AppendLine($"  {FormatOffset(section.Offset)}..0x{section.Offset + section.Length:X}: {section.Name} ({section.Length} bytes)");
        }

        return builder.ToString();
    }

    private static string FormatOffset(uint offset) => offset == 0 ? "none" : $"0x{offset:X}";

    private static string FormatOffset(int offset) => $"0x{offset:X}";

    private static string FormatWord(int value) => $"0x{unchecked((uint)value):X8}";

    private static string FormatOffsets(IEnumerable<ushort> offsets)
    {
        return string.Join(", ", offsets.Select(offset => offset == 0 ? "none" : $"0x{offset:X}"));
    }
}

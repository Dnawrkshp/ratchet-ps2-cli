namespace RatchetPs2.Core.Ties;

internal static class TieGlowRgbaReader
{
    private const int MaxBoundedPrimaryShaderRangeByteLength = 0x60;
    private const int MaxTailBridgeShaderPromotionBlockCount = 6;

    public static (List<TieGlowRgbaRemap> Remaps, List<TieGlowRgbaVertex> Vertices) Read(
        byte[] bytes,
        TieClassHeader header,
        IReadOnlyList<TiePacketTable> packetTables,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        IReadOnlyList<TieLodTopology> lodTopologies)
    {
        var rgbaRemapOffsets = header.RgbaRemapOffsets
            .Where(offset => offset > 0)
            .Select(offset => (int)offset)
            .ToHashSet();
        var remapOffsets = header.GlowRemapOffsets
            .Select((offset, index) => (Offset: (int)offset, Index: index))
            .Where(item => item.Offset > 0)
            .ToArray();
        if (remapOffsets.Length == 0)
        {
            return ([], []);
        }

        var rgba = TieRgba32.FromRaw(header.GlowRgba);
        var ranges = remapOffsets
            .Select(item => BuildGlowRemapRange(
                bytes,
                packetDataBlocks,
                item.Index,
                item.Offset,
                header.GlowRgba,
                rgba,
                rgbaRemapOffsets.Contains(item.Offset)))
            .ToArray();
        var packetsByBlockKey = packetTables
            .SelectMany(table => table.Packets)
            .ToDictionary(packet => (packet.LodIndex, packet.PacketIndex));

        foreach (var lodGroup in ranges
                     .Where(range => range.Block is not null)
                     .GroupBy(range => range.Block!.LodIndex))
        {
            var lodBlocks = packetDataBlocks
                .Where(block => block.LodIndex == lodGroup.Key)
                .OrderBy(block => block.Offset)
                .ThenBy(block => block.PacketIndex)
                .ToArray();
            if (lodBlocks.Length == 0)
            {
                continue;
            }

            var lodDataEndOffset = lodBlocks.Max(block => block.Offset + block.Length);
            var orderedRanges = lodGroup
                .OrderBy(range => range.Offset)
                .ThenBy(range => range.RemapIndex)
                .ToArray();
            var multipassBlocks = lodBlocks
                .Where(block => packetsByBlockKey.TryGetValue((block.LodIndex, block.PacketIndex), out var packet)
                    && IsGlowPassFlagsPacket(packet))
                .ToArray();
            if (multipassBlocks.Length > 0)
            {
                foreach (var range in orderedRanges)
                {
                    ResolveGlowRemapMultipassBlocks(range, multipassBlocks);
                }

                continue;
            }

            var primaryRanges = orderedRanges
                .Where(range => range.IsRgbaRemapOffset)
                .ToArray();
            if (primaryRanges.Length > 0)
            {
                foreach (var range in primaryRanges)
                {
                    ResolveGlowRemapOffsetRange(
                        range,
                        lodBlocks,
                        packetsByBlockKey,
                        GetNextRangeBoundary(orderedRanges, range) ?? lodDataEndOffset);
                }

                if (primaryRanges.Any(range => range.ResolutionKind != TieGlowRgbaRemapResolutionKind.Unresolved))
                {
                    continue;
                }
            }

            foreach (var range in orderedRanges)
            {
                ResolveGlowRemapOffsetRange(
                    range,
                    lodBlocks,
                    packetsByBlockKey,
                    GetNextRangeBoundary(orderedRanges, range) ?? lodDataEndOffset);
            }

            // GC glow remaps are still a heuristic WIP. These tail/local shader
            // promotions cover the known GC fixtures, but additional tuning is
            // expected as more edge cases are compared against in-game output.
            SuppressLocalTailRangesWithTailShaderBridge(orderedRanges, packetsByBlockKey);
            PromoteRepeatedTailBridgeShaderRanges(orderedRanges, lodBlocks, packetsByBlockKey);
            PromoteRepeatedLocalShaderRanges(orderedRanges, lodBlocks, packetsByBlockKey);
        }

        var verticesByLogicalIndex = new Dictionary<(int LodIndex, int LogicalVertexIndex), TieGlowRgbaVertex>();
        foreach (var range in ranges.Where(range => range.ResolutionKind != TieGlowRgbaRemapResolutionKind.Unresolved))
        {
            var block = range.Block;
            var topology = block is null
                ? null
                : lodTopologies.FirstOrDefault(topology => topology.LodIndex == block.LodIndex);
            if (block is null
                || topology is null
                || range.EndOffset is null)
            {
                continue;
            }

            var resolvedLogicalVertexCount = 0;
            foreach (var vertex in topology.LogicalVertices)
            {
                var row = vertex.VertexRow ?? vertex.AddressRow;
                if (row is null
                    || !GlowRangeContainsVertex(range, topology, vertex, row))
                {
                    continue;
                }

                resolvedLogicalVertexCount++;
                var glowVertex = new TieGlowRgbaVertex
                {
                    RemapIndex = range.RemapIndex,
                    RemapOffset = range.Offset,
                    LodIndex = vertex.LodIndex,
                    PacketIndex = vertex.PacketIndex,
                    StripIndex = vertex.StripIndex,
                    PacketStripIndex = vertex.PacketStripIndex,
                    IndexInStrip = vertex.IndexInStrip,
                    LogicalVertexIndex = vertex.LogicalVertexIndex,
                    VertexRowIndex = row.Index,
                    VertexRowOffset = row.Offset,
                    RawRgba = range.RawRgba,
                    Rgba = range.Rgba
                };
                var key = (vertex.LodIndex, vertex.LogicalVertexIndex);
                if (!verticesByLogicalIndex.TryGetValue(key, out var existing)
                    || glowVertex.RemapOffset >= existing.RemapOffset)
                {
                    verticesByLogicalIndex[key] = glowVertex;
                }
            }

            range.ResolvedLogicalVertexCount = resolvedLogicalVertexCount;
        }

        return (
            ranges.Select(range => range.ToRemap()).ToList(),
            verticesByLogicalIndex.Values
                .OrderBy(vertex => vertex.LodIndex)
                .ThenBy(vertex => vertex.LogicalVertexIndex)
                .ToList());
    }

    private static GlowRgbaRemapRange BuildGlowRemapRange(
        byte[] bytes,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        int remapIndex,
        int offset,
        int rawRgba,
        TieRgba32 rgba,
        bool isRgbaRemapOffset)
    {
        var block = offset <= bytes.Length
            ? packetDataBlocks.FirstOrDefault(block => offset >= block.Offset && offset < block.Offset + block.Length)
                ?? packetDataBlocks
                    .Where(block => offset < block.Offset)
                    .OrderBy(block => block.Offset)
                    .FirstOrDefault()
            : null;
        return new GlowRgbaRemapRange(remapIndex, offset, rawRgba, rgba, block, isRgbaRemapOffset);
    }

    private static int? GetNextRangeBoundary(
        IReadOnlyList<GlowRgbaRemapRange> orderedRanges,
        GlowRgbaRemapRange range)
    {
        return orderedRanges
            .Where(candidate => candidate.Offset > range.Offset)
            .OrderBy(candidate => candidate.Offset)
            .ThenBy(candidate => candidate.RemapIndex)
            .Select(candidate => (int?)candidate.Offset)
            .FirstOrDefault();
    }

    private static void ResolveGlowRemapOffsetRange(
        GlowRgbaRemapRange range,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey,
        int endOffset)
    {
        var block = range.Block;
        if (block is null || endOffset <= range.Offset)
        {
            return;
        }

        if (packetsByBlockKey.TryGetValue((block.LodIndex, block.PacketIndex), out var sourcePacket)
            && IsGlowPassFlagsPacket(sourcePacket)
            && TryResolveGlowRemapMultipassPacketRange(range, block))
        {
            return;
        }

        if (sourcePacket is not null
            && TryResolveGlowRemapShaderRange(range, sourcePacket, lodBlocks, packetsByBlockKey, endOffset))
        {
            return;
        }

        if (sourcePacket is not null
            && TryResolveGlowRemapLocalFirstShaderRange(range, sourcePacket, block))
        {
            return;
        }

        if (TryResolveGlowRemapVertexRowRange(range, sourcePacket, endOffset))
        {
            return;
        }

        if (TryResolveGlowRemapPacketMarkerRange(range, sourcePacket, block))
        {
            return;
        }

        if (TryResolveGlowRemapNonRgbaTailBridge(range, lodBlocks, packetsByBlockKey))
        {
            return;
        }

        if (TryResolveGlowRemapOverlappingVertexRowRange(range, lodBlocks, packetsByBlockKey, endOffset))
        {
            return;
        }

        var targetBlock = lodBlocks
            .Where(candidate => candidate.Offset + candidate.Length > range.Offset && candidate.Offset < endOffset)
            .FirstOrDefault(candidate =>
                packetsByBlockKey.TryGetValue((candidate.LodIndex, candidate.PacketIndex), out var packet)
                && IsGlowPassFlagsPacket(packet));
        if (targetBlock is null || targetBlock.VertexRows.Count == 0)
        {
            return;
        }

        TryResolveGlowRemapMultipassPacketRange(range, targetBlock);
    }

    private static void ResolveGlowRemapMultipassBlocks(
        GlowRgbaRemapRange range,
        IReadOnlyList<TiePacketDataBlock> blocks)
    {
        var resolvedBlocks = blocks
            .Where(block => block.VertexRows.Count > 0)
            .OrderBy(block => block.Offset)
            .ThenBy(block => block.PacketIndex)
            .ToArray();
        if (resolvedBlocks.Length == 0)
        {
            return;
        }

        foreach (var block in resolvedBlocks)
        {
            range.ResolvedBlocks.Add(block);
            range.AddResolvedRows(block, block.VertexRows[0].Offset, block.VertexRows[^1].Offset + 0x10, block.VertexRows);
        }

        var firstBlock = resolvedBlocks[0];
        var lastBlock = resolvedBlocks[^1];
        range.ResolvedStartOffset = firstBlock.VertexRows[0].Offset;
        range.EndOffset = lastBlock.VertexRows[^1].Offset + 0x10;
        range.ResolutionKind = resolvedBlocks.Length == 1
            ? TieGlowRgbaRemapResolutionKind.PacketMultipassRange
            : TieGlowRgbaRemapResolutionKind.PacketMultipassSet;
        range.ResolvedPacketIndex = firstBlock.PacketIndex;
        range.ResolvedPacketCount = resolvedBlocks.Length;
        range.ResolvedVertexRowCount = resolvedBlocks.Sum(block => block.VertexRows.Count);
        if (resolvedBlocks.Length == 1)
        {
            range.StartVertexRowIndex = firstBlock.VertexRows[0].Index;
            range.EndVertexRowIndexExclusive = firstBlock.VertexRows[^1].Index + 1;
        }
    }

    private static bool TryResolveGlowRemapMultipassPacketRange(
        GlowRgbaRemapRange range,
        TiePacketDataBlock block)
    {
        if (block.VertexRows.Count == 0)
        {
            return false;
        }

        range.ResolvedStartOffset = block.VertexRows[0].Offset;
        range.EndOffset = block.VertexRows[^1].Offset + 0x10;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketMultipassRange;
        range.ResolvedPacketIndex = block.PacketIndex;
        range.StartVertexRowIndex = block.VertexRows[0].Index;
        range.EndVertexRowIndexExclusive = block.VertexRows[^1].Index + 1;
        range.ResolvedPacketCount = 1;
        range.ResolvedVertexRowCount = block.VertexRows.Count;
        range.AddResolvedRows(block, range.ResolvedStartOffset, range.EndOffset.Value, block.VertexRows);
        return true;
    }

    private static bool TryResolveGlowRemapShaderRange(
        GlowRgbaRemapRange range,
        TiePacket sourcePacket,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey,
        int endOffset)
    {
        var block = range.Block;
        if (!range.IsRgbaRemapOffset
            || block is null
            || sourcePacket.ShaderReferences.Count <= 1
            || block.VertexRows.Count == 0)
        {
            return false;
        }

        var sourceRow = block.VertexRows.FirstOrDefault(row =>
            range.Offset >= row.Offset && range.Offset < row.Offset + 0x10);
        if (sourceRow is null
            || !TrySelectShaderIndex(sourcePacket, sourceRow.PrimaryVuAddress, out var shaderIndex))
        {
            return false;
        }

        var resolvedBlocks = GetForwardContiguousShaderBlocks(lodBlocks, packetsByBlockKey, block, shaderIndex);
        if (resolvedBlocks.Length == 0)
        {
            return false;
        }

        if (TryResolveBoundedPrimaryShaderRowRange(range, sourcePacket, block, shaderIndex, endOffset))
        {
            return true;
        }

        range.ResolvedStartOffset = range.Offset;
        range.EndOffset = endOffset;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketShaderRange;
        range.ResolvedShaderIndex = shaderIndex;
        range.ResolvedPacketIndex = resolvedBlocks[0].PacketIndex;
        range.ResolvedPacketCount = resolvedBlocks.Length;
        range.ResolvedVertexRowCount = resolvedBlocks.Sum(resolvedBlock => resolvedBlock.VertexRows.Count);
        range.ResolvedBlocks.AddRange(resolvedBlocks);
        return true;
    }

    private static bool TryResolveBoundedPrimaryShaderRowRange(
        GlowRgbaRemapRange range,
        TiePacket sourcePacket,
        TiePacketDataBlock block,
        int shaderIndex,
        int endOffset)
    {
        if (endOffset - range.Offset > MaxBoundedPrimaryShaderRangeByteLength
            || !RangeStartsInRegion(range, "vertex-rows"))
        {
            return false;
        }

        var eligibleRows = GetGlowEligibleRows(block, sourcePacket);
        if (eligibleRows.Length == 0)
        {
            return false;
        }

        var vertexRowsStart = eligibleRows[0].Offset;
        var vertexRowsEnd = eligibleRows[^1].Offset + 0x10;
        var rowStartOffset = Math.Max(range.Offset, vertexRowsStart);
        var rowEndOffset = Math.Min(endOffset, vertexRowsEnd);
        if (rowEndOffset <= rowStartOffset)
        {
            return false;
        }

        var rows = eligibleRows
            .Where(row => row.Offset >= rowStartOffset && row.Offset < rowEndOffset)
            .ToArray();
        if (rows.Length == 0)
        {
            return false;
        }

        range.ResolvedStartOffset = rowStartOffset;
        range.EndOffset = rowEndOffset;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketShaderRange;
        range.ResolvedShaderIndex = shaderIndex;
        range.ResolvedPacketIndex = block.PacketIndex;
        range.ResolvedPacketCount = 1;
        range.StartVertexRowIndex = rows[0].Index;
        range.EndVertexRowIndexExclusive = rows[^1].Index + 1;
        range.ResolvedVertexRowCount = rows.Length;
        range.ResolvedBlocks.Add(block);
        range.AddResolvedRows(block, rowStartOffset, rowEndOffset, rows);
        return true;
    }

    private static bool TryResolveGlowRemapLocalFirstShaderRange(
        GlowRgbaRemapRange range,
        TiePacket sourcePacket,
        TiePacketDataBlock block)
    {
        if (range.IsRgbaRemapOffset
            || sourcePacket.ShaderReferences.Count <= 1
            || block.VertexRows.Count == 0)
        {
            return false;
        }

        var eligibleRows = GetGlowEligibleRows(block, sourcePacket);
        if (eligibleRows.Length == 0)
        {
            return false;
        }

        var vertexRowsStart = eligibleRows[0].Offset;
        var vertexRowsEnd = eligibleRows[^1].Offset + 0x10;
        if (range.Offset < vertexRowsStart || range.Offset >= vertexRowsEnd)
        {
            return false;
        }

        var shaderIndex = sourcePacket.ShaderReferences[0].ShaderIndex;
        if (shaderIndex < 0)
        {
            return false;
        }

        range.ResolvedStartOffset = vertexRowsStart;
        range.EndOffset = vertexRowsEnd;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketShaderRange;
        range.ResolvedShaderIndex = shaderIndex;
        range.ResolvedPacketIndex = block.PacketIndex;
        range.ResolvedPacketCount = 1;
        range.ResolvedVertexRowCount = eligibleRows.Length;
        range.ResolvedBlocks.Add(block);
        range.ResolvedByLocalFirstShaderRange = true;
        return true;
    }

    private static bool TryResolveGlowRemapVertexRowRange(
        GlowRgbaRemapRange range,
        TiePacket? packet,
        int endOffset)
    {
        var block = range.Block;
        if (block is null || block.VertexRows.Count == 0)
        {
            return false;
        }

        var eligibleRows = GetGlowEligibleRows(block, packet);
        if (eligibleRows.Length == 0)
        {
            return false;
        }

        var vertexRowsStart = eligibleRows[0].Offset;
        var vertexRowsEnd = eligibleRows[^1].Offset + 0x10;
        if (range.Offset >= vertexRowsEnd || endOffset <= vertexRowsStart)
        {
            return false;
        }

        var rowStartOffset = Math.Max(range.Offset, vertexRowsStart);
        var rowEndOffset = Math.Min(endOffset, vertexRowsEnd);
        var rows = eligibleRows
            .Where(row => row.Offset >= rowStartOffset && row.Offset < rowEndOffset)
            .ToArray();
        if (rows.Length == 0)
        {
            return false;
        }

        range.EndOffset = rowEndOffset;
        range.ResolutionKind = range.Offset < vertexRowsStart
            ? TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange
            : TieGlowRgbaRemapResolutionKind.PacketVertexRowRange;
        range.ResolvedStartOffset = rowStartOffset;
        range.ResolvedPacketIndex = block.PacketIndex;
        range.StartVertexRowIndex = rows[0].Index;
        range.EndVertexRowIndexExclusive = rows[^1].Index + 1;
        range.ResolvedPacketCount = 1;
        range.ResolvedVertexRowCount = rows.Length;
        range.AddResolvedRows(block, rowStartOffset, rowEndOffset, rows);
        return true;
    }

    private static bool TryResolveGlowRemapPacketMarkerRange(
        GlowRgbaRemapRange range,
        TiePacket? packet,
        TiePacketDataBlock block)
    {
        if (block.VertexRows.Count == 0)
        {
            return false;
        }

        var markerRegion = block.Regions.FirstOrDefault(region =>
            range.Offset >= region.Offset
            && range.Offset < region.Offset + region.Length);
        if (markerRegion is null
            || markerRegion.Name is not ("setup-rows" or "control-region" or "multipass-uv"))
        {
            return false;
        }

        var eligibleRows = GetGlowEligibleRows(block, packet);
        if (eligibleRows.Length == 0)
        {
            return false;
        }

        var vertexRowsStart = eligibleRows[0].Offset;
        var vertexRowsEnd = eligibleRows[^1].Offset + 0x10;
        range.ResolvedStartOffset = vertexRowsStart;
        range.EndOffset = vertexRowsEnd;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange;
        range.ResolvedPacketIndex = block.PacketIndex;
        range.StartVertexRowIndex = eligibleRows[0].Index;
        range.EndVertexRowIndexExclusive = eligibleRows[^1].Index + 1;
        range.ResolvedPacketCount = 1;
        range.ResolvedVertexRowCount = eligibleRows.Length;
        range.AddResolvedRows(block, vertexRowsStart, vertexRowsEnd, eligibleRows);
        return true;
    }

    private static bool TryResolveGlowRemapNonRgbaTailBridge(
        GlowRgbaRemapRange range,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey)
    {
        var block = range.Block;
        if (block is null
            || block.VertexRows.Count == 0
            || !packetsByBlockKey.TryGetValue((block.LodIndex, block.PacketIndex), out var sourcePacket))
        {
            return false;
        }

        var vertexRowsStart = block.VertexRows[0].Offset;
        var vertexRowsEnd = block.VertexRows[^1].Offset + 0x10;
        if (range.Offset < vertexRowsStart || range.Offset >= vertexRowsEnd)
        {
            return false;
        }

        var eligibleRows = GetGlowEligibleRows(block, sourcePacket);
        if (eligibleRows.Length == 0 || range.Offset < eligibleRows[^1].Offset + 0x10)
        {
            return false;
        }

        var sourceShaderIndices = sourcePacket.ShaderReferences
            .Select(reference => reference.ShaderIndex)
            .Where(shaderIndex => shaderIndex >= 0)
            .ToHashSet();
        if (TryResolveGlowRemapCarriedTailShaderRange(
            range,
            sourcePacket,
            lodBlocks,
            packetsByBlockKey))
        {
            return true;
        }

        // Some GC glow markers sit in the non-RGBA tail of the previous packet.
        // The target can be the first later packet introducing the lamp shader, not the immediate same-shader continuation.
        foreach (var targetBlock in lodBlocks
                     .Where(candidate => candidate.LodIndex == block.LodIndex
                         && candidate.Offset > block.Offset
                         && candidate.VertexRows.Count > 0)
                     .OrderBy(candidate => candidate.Offset)
                     .ThenBy(candidate => candidate.PacketIndex))
        {
            if (!packetsByBlockKey.TryGetValue((targetBlock.LodIndex, targetBlock.PacketIndex), out var targetPacket))
            {
                continue;
            }

            var targetShaderIndex = targetPacket.ShaderReferences
                .Select(reference => reference.ShaderIndex)
                .FirstOrDefault(shaderIndex => shaderIndex >= 0 && !sourceShaderIndices.Contains(shaderIndex), -1);
            if (targetShaderIndex < 0)
            {
                continue;
            }

            var targetRows = GetGlowEligibleRows(targetBlock, targetPacket);
            if (targetRows.Length == 0)
            {
                return false;
            }

            range.ResolvedStartOffset = targetRows[0].Offset;
            range.EndOffset = targetRows[^1].Offset + 0x10;
            range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketShaderRange;
            range.ResolvedPacketIndex = targetBlock.PacketIndex;
            range.ResolvedShaderIndex = targetShaderIndex;
            range.ResolvedPacketCount = 1;
            range.ResolvedVertexRowCount = targetRows.Length;
            range.ResolvedBlocks.Add(targetBlock);
            range.ResolvedByNonRgbaTailBridge = true;
            return true;
        }

        return false;
    }

    private static bool TryResolveGlowRemapCarriedTailShaderRange(
        GlowRgbaRemapRange range,
        TiePacket sourcePacket,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey)
    {
        var block = range.Block;
        if (block is null || sourcePacket.ShaderReferences.Count <= 1)
        {
            return false;
        }

        foreach (var shaderIndex in sourcePacket.ShaderReferences
                     .Skip(1)
                     .Select(reference => reference.ShaderIndex)
                     .Where(shaderIndex => shaderIndex >= 0)
                     .Distinct())
        {
            var carriedBlocks = GetForwardContiguousShaderBlocks(
                lodBlocks,
                packetsByBlockKey,
                block,
                shaderIndex);
            if (carriedBlocks.Length <= 1)
            {
                continue;
            }

            ApplyShaderRangeResolution(range, shaderIndex, carriedBlocks);
            range.ResolvedByNonRgbaTailBridge = true;
            return true;
        }

        return false;
    }

    private static void SuppressLocalTailRangesWithTailShaderBridge(
        IReadOnlyList<GlowRgbaRemapRange> ranges,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey)
    {
        var tailBridgeRanges = ranges
            .Where(range => range.ResolvedByNonRgbaTailBridge && range.Block is not null)
            .OrderBy(range => range.Offset)
            .ToArray();
        if (tailBridgeRanges.Length == 0)
        {
            return;
        }

        foreach (var bridgeGroup in tailBridgeRanges.GroupBy(range => (range.Block!.LodIndex, range.Block.PacketIndex)))
        {
            var bridge = bridgeGroup.First();
            var block = bridge.Block!;
            if (!packetsByBlockKey.TryGetValue((block.LodIndex, block.PacketIndex), out var sourcePacket))
            {
                continue;
            }

            var eligibleRows = GetGlowEligibleRows(block, sourcePacket);
            if (eligibleRows.Length == 0)
            {
                continue;
            }

            var eligibleRowsStart = eligibleRows[0].Offset;
            var eligibleRowsEnd = eligibleRows[^1].Offset + 0x10;
            foreach (var range in ranges)
            {
                if (range.Block == block
                    && range.Offset < bridge.Offset
                    && range.ResolvedByLocalFirstShaderRange
                    && range.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
                    && range.ResolvedPacketIndex == block.PacketIndex
                    && range.ResolvedStartOffset == eligibleRowsStart
                    && range.EndOffset == eligibleRowsEnd)
                {
                    range.ClearResolution();
                    continue;
                }

                if (range.Block != block
                    || range.Offset >= bridge.Offset
                    || range.ResolutionKind != TieGlowRgbaRemapResolutionKind.PacketVertexRowRange
                    || range.ResolvedPacketIndex != block.PacketIndex
                    || range.EndOffset != eligibleRowsEnd
                    || range.ResolvedStartOffset <= eligibleRowsStart)
                {
                    continue;
                }

                range.ClearResolution();
            }
        }
    }

    private static void PromoteRepeatedLocalShaderRanges(
        IReadOnlyList<GlowRgbaRemapRange> ranges,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey)
    {
        var localShaderRanges = ranges
            .Where(range => range.ResolvedByLocalFirstShaderRange
                && range.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
                && range.ResolvedShaderIndex is >= 0)
            .ToArray();
        if (localShaderRanges.Length < 2)
        {
            return;
        }

        if (localShaderRanges
                .Select(range => range.ResolvedShaderIndex!.Value)
                .Distinct()
                .Count() < 2)
        {
            return;
        }

        var candidates = localShaderRanges
            .GroupBy(range => range.ResolvedShaderIndex!.Value)
            .Select(group => new
            {
                ShaderIndex = group.Key,
                RangeCount = group.Count(),
                Blocks = GetShaderBlocks(lodBlocks, packetsByBlockKey, group.Key)
            })
            .Where(candidate => candidate.RangeCount > 1 && candidate.Blocks.Length > 1)
            .OrderByDescending(candidate => candidate.RangeCount)
            .ThenByDescending(candidate => candidate.Blocks.Length)
            .ToArray();
        if (candidates.Length == 0)
        {
            return;
        }

        var selected = candidates[0];
        if (candidates.Length > 1
            && candidates[1].RangeCount == selected.RangeCount
            && candidates[1].Blocks.Length == selected.Blocks.Length)
        {
            return;
        }

        // GC lamp glow can use multiple local markers for the same material shader.
        // Treat the repeated shader as the target and discard one-off local shader hits.
        foreach (var range in localShaderRanges)
        {
            ApplyShaderRangeResolution(range, selected.ShaderIndex, selected.Blocks);
            range.ResolvedByLocalFirstShaderRange = true;
        }
    }

    private static void PromoteRepeatedTailBridgeShaderRanges(
        IReadOnlyList<GlowRgbaRemapRange> ranges,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey)
    {
        var bridgeGroups = ranges
            .Where(range => range.ResolvedByNonRgbaTailBridge
                && range.Block is not null
                && range.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
                && range.ResolvedShaderIndex is >= 0)
            .GroupBy(range => (range.Block!.LodIndex, range.Block.PacketIndex, ShaderIndex: range.ResolvedShaderIndex!.Value))
            .Where(group => group.Count() > 1)
            .ToArray();
        foreach (var group in bridgeGroups)
        {
            var shaderIndex = group.Key.ShaderIndex;
            var blocks = GetShaderBlocks(lodBlocks, packetsByBlockKey, shaderIndex);
            if (blocks.Length <= 1 || blocks.Length > MaxTailBridgeShaderPromotionBlockCount)
            {
                continue;
            }

            foreach (var range in group)
            {
                ApplyShaderRangeResolution(range, shaderIndex, blocks);
                range.ResolvedByNonRgbaTailBridge = true;
            }
        }
    }

    private static TiePacketDataBlock[] GetShaderBlocks(
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey,
        int shaderIndex)
    {
        return lodBlocks
            .Where(candidate => packetsByBlockKey.TryGetValue((candidate.LodIndex, candidate.PacketIndex), out var packet)
                && packet.ShaderReferences.Any(reference => reference.ShaderIndex == shaderIndex))
            .OrderBy(candidate => candidate.Offset)
            .ThenBy(candidate => candidate.PacketIndex)
            .ToArray();
    }

    private static TiePacketDataBlock[] GetForwardContiguousShaderBlocks(
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey,
        TiePacketDataBlock sourceBlock,
        int shaderIndex)
    {
        var blocks = GetShaderBlocks(lodBlocks, packetsByBlockKey, shaderIndex);
        var sourceIndex = Array.FindIndex(blocks, block =>
            block.LodIndex == sourceBlock.LodIndex
            && block.PacketIndex == sourceBlock.PacketIndex);
        if (sourceIndex < 0)
        {
            return [];
        }

        var result = new List<TiePacketDataBlock> { blocks[sourceIndex] };
        for (var index = sourceIndex + 1; index < blocks.Length; index++)
        {
            if (blocks[index].PacketIndex != result[^1].PacketIndex + 1)
            {
                break;
            }

            result.Add(blocks[index]);
        }

        return result.ToArray();
    }

    private static bool RangeStartsInRegion(GlowRgbaRemapRange range, string regionName)
    {
        var block = range.Block;
        return block is not null
            && block.Regions.Any(region =>
                region.Name == regionName
                && range.Offset >= region.Offset
                && range.Offset < region.Offset + region.Length);
    }

    private static void ApplyShaderRangeResolution(
        GlowRgbaRemapRange range,
        int shaderIndex,
        IReadOnlyList<TiePacketDataBlock> resolvedBlocks)
    {
        range.ClearResolution();
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketShaderRange;
        range.ResolvedShaderIndex = shaderIndex;
        range.ResolvedPacketIndex = resolvedBlocks[0].PacketIndex;
        range.ResolvedPacketCount = resolvedBlocks.Count;
        range.ResolvedVertexRowCount = resolvedBlocks.Sum(resolvedBlock => resolvedBlock.VertexRows.Count);
        range.ResolvedBlocks.AddRange(resolvedBlocks);
        var firstBlock = resolvedBlocks[0];
        var lastBlock = resolvedBlocks[^1];
        if (firstBlock.VertexRows.Count > 0)
        {
            range.ResolvedStartOffset = firstBlock.VertexRows[0].Offset;
        }

        if (lastBlock.VertexRows.Count > 0)
        {
            range.EndOffset = lastBlock.VertexRows[^1].Offset + 0x10;
        }
    }

    private static bool TryResolveGlowRemapOverlappingVertexRowRange(
        GlowRgbaRemapRange range,
        IReadOnlyList<TiePacketDataBlock> lodBlocks,
        IReadOnlyDictionary<(int LodIndex, int PacketIndex), TiePacket> packetsByBlockKey,
        int endOffset)
    {
        if (endOffset <= range.Offset)
        {
            return false;
        }

        var spans = lodBlocks
            .Where(block => block.VertexRows.Count > 0)
            .Select(block =>
            {
                packetsByBlockKey.TryGetValue((block.LodIndex, block.PacketIndex), out var packet);
                var eligibleRows = GetGlowEligibleRows(block, packet);
                if (eligibleRows.Length == 0)
                {
                    return new GlowRgbaResolvedRowSpan(block, 0, 0, []);
                }

                var vertexRowsStart = eligibleRows[0].Offset;
                var vertexRowsEnd = eligibleRows[^1].Offset + 0x10;
                var rowStartOffset = Math.Max(range.Offset, vertexRowsStart);
                var rowEndOffset = Math.Min(endOffset, vertexRowsEnd);
                var rows = rowStartOffset < rowEndOffset
                    ? eligibleRows
                        .Where(row => row.Offset >= rowStartOffset && row.Offset < rowEndOffset)
                        .ToArray()
                    : [];
                return new GlowRgbaResolvedRowSpan(block, rowStartOffset, rowEndOffset, rows);
            })
            .Where(span => span.Rows.Count > 0)
            .OrderBy(span => span.Block.Offset)
            .ThenBy(span => span.Block.PacketIndex)
            .ToArray();
        if (spans.Length == 0)
        {
            return false;
        }

        if (RangeStartsInRegion(range, "scissor-rows") && spans.Length >= 5)
        {
            return false;
        }

        foreach (var span in spans)
        {
            range.AddResolvedRows(span.Block, span.StartOffset, span.EndOffset, span.Rows);
        }

        range.ResolvedStartOffset = spans[0].StartOffset;
        range.EndOffset = spans[^1].EndOffset;
        range.ResolutionKind = TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange;
        range.ResolvedPacketIndex = spans[0].Block.PacketIndex;
        range.ResolvedPacketCount = spans.Length;
        range.ResolvedVertexRowCount = spans.Sum(span => span.Rows.Count);
        if (spans.Length == 1)
        {
            range.StartVertexRowIndex = spans[0].Rows[0].Index;
            range.EndVertexRowIndexExclusive = spans[0].Rows[^1].Index + 1;
        }
        return true;
    }

    private static TiePacketVertexRow[] GetGlowEligibleRows(TiePacketDataBlock block, TiePacket? packet)
    {
        if (block.VertexRows.Count == 0)
        {
            return [];
        }

        var rowCount = packet is null || packet.RgbaCount <= 0
            ? block.VertexRows.Count
            : Math.Min(packet.RgbaCount, block.VertexRows.Count);
        return block.VertexRows
            .Take(rowCount)
            .ToArray();
    }

    private static bool IsGlowPassFlagsPacket(TiePacket packet)
    {
        return packet.PassFlags == TiePassFlags.GlowEmissionPassFlags;
    }

    private static bool TrySelectShaderIndex(TiePacket packet, int vuAddress, out int shaderIndex)
    {
        shaderIndex = -1;
        if (vuAddress <= 0 || packet.ShaderReferences.Count == 0)
        {
            return false;
        }

        var referenceIndex = 0;
        for (var i = 0; i < packet.ShaderSwitchVuAddresses.Count; i++)
        {
            if (vuAddress < packet.ShaderSwitchVuAddresses[i])
            {
                break;
            }

            referenceIndex = Math.Min(i + 1, packet.ShaderReferences.Count - 1);
        }

        shaderIndex = packet.ShaderReferences[referenceIndex].ShaderIndex;
        return shaderIndex >= 0;
    }

    private static bool GlowRangeContainsVertex(
        GlowRgbaRemapRange range,
        TieLodTopology topology,
        TieLogicalVertex vertex,
        TiePacketVertexRow row)
    {
        if (range.ResolvedShaderIndex is { } shaderIndex)
        {
            if (range.Block is null
                || vertex.LodIndex != range.Block.LodIndex
                || vertex.StripIndex < 0
                || vertex.StripIndex >= topology.Strips.Count)
            {
                return false;
            }

            var strip = topology.Strips[vertex.StripIndex];
            if (strip.ShaderIndex != shaderIndex
                || !range.ResolvedBlocks.Any(block =>
                    block.LodIndex == vertex.LodIndex
                    && block.PacketIndex == vertex.PacketIndex))
            {
                return false;
            }

            return range.ResolvedRowSpans.Count == 0
                || range.ResolvedRowSpans.Any(span =>
                    span.Block.LodIndex == vertex.LodIndex
                    && span.Block.PacketIndex == vertex.PacketIndex
                    && row.Offset >= span.StartOffset
                    && row.Offset < span.EndOffset);
        }

        if (range.ResolvedRowSpans.Count > 0)
        {
            return range.ResolvedRowSpans.Any(span =>
                span.Block.LodIndex == vertex.LodIndex
                && span.Block.PacketIndex == vertex.PacketIndex
                && row.Offset >= span.StartOffset
                && row.Offset < span.EndOffset);
        }

        if (range.ResolvedBlocks.Count > 0)
        {
            return range.ResolvedBlocks.Any(block =>
                block.LodIndex == vertex.LodIndex
                && block.PacketIndex == vertex.PacketIndex);
        }

        return range.EndOffset.HasValue
            && row.Offset >= range.ResolvedStartOffset
            && row.Offset < range.EndOffset.Value;
    }

    private sealed class GlowRgbaRemapRange(
        int remapIndex,
        int offset,
        int rawRgba,
        TieRgba32 rgba,
        TiePacketDataBlock? block,
        bool isRgbaRemapOffset)
    {
        public int RemapIndex { get; } = remapIndex;
        public int Offset { get; } = offset;
        public int RawRgba { get; } = rawRgba;
        public TieRgba32 Rgba { get; } = rgba;
        public TiePacketDataBlock? Block { get; } = block;
        public bool IsRgbaRemapOffset { get; } = isRgbaRemapOffset;
        public TieGlowRgbaRemapResolutionKind ResolutionKind { get; set; } = TieGlowRgbaRemapResolutionKind.Unresolved;
        public int ResolvedStartOffset { get; set; } = offset;
        public int? EndOffset { get; set; }
        public int? ResolvedPacketIndex { get; set; }
        public int? ResolvedShaderIndex { get; set; }
        public int? StartVertexRowIndex { get; set; }
        public int? EndVertexRowIndexExclusive { get; set; }
        public int ResolvedPacketCount { get; set; }
        public int ResolvedVertexRowCount { get; set; }
        public int ResolvedLogicalVertexCount { get; set; }
        public bool ResolvedByNonRgbaTailBridge { get; set; }
        public bool ResolvedByLocalFirstShaderRange { get; set; }
        public List<TiePacketDataBlock> ResolvedBlocks { get; } = [];
        public List<GlowRgbaResolvedRowSpan> ResolvedRowSpans { get; } = [];

        public void AddResolvedRows(
            TiePacketDataBlock block,
            int startOffset,
            int endOffset,
            IReadOnlyList<TiePacketVertexRow> rows)
        {
            ResolvedRowSpans.Add(new GlowRgbaResolvedRowSpan(block, startOffset, endOffset, rows));
        }

        public void ClearResolution()
        {
            ResolutionKind = TieGlowRgbaRemapResolutionKind.Unresolved;
            ResolvedStartOffset = Offset;
            EndOffset = null;
            ResolvedPacketIndex = null;
            ResolvedShaderIndex = null;
            StartVertexRowIndex = null;
            EndVertexRowIndexExclusive = null;
            ResolvedPacketCount = 0;
            ResolvedVertexRowCount = 0;
            ResolvedLogicalVertexCount = 0;
            ResolvedByNonRgbaTailBridge = false;
            ResolvedByLocalFirstShaderRange = false;
            ResolvedBlocks.Clear();
            ResolvedRowSpans.Clear();
        }

        public TieGlowRgbaRemap ToRemap()
        {
            var resolvedPacketIndices = ResolvedBlocks
                .Select(block => block.PacketIndex)
                .Concat(ResolvedRowSpans.Select(span => span.Block.PacketIndex))
                .Distinct()
                .OrderBy(index => index)
                .ToArray();

            return new TieGlowRgbaRemap
            {
                RemapIndex = RemapIndex,
                Offset = Offset,
                RawRgba = RawRgba,
                Rgba = Rgba,
                ResolutionKind = ResolutionKind,
                ResolvedStartOffset = ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved ? null : ResolvedStartOffset,
                EndOffset = EndOffset,
                LodIndex = Block?.LodIndex,
                PacketIndex = Block?.PacketIndex,
                ResolvedPacketIndex = ResolvedPacketIndex,
                ResolvedPacketIndices = resolvedPacketIndices,
                ResolvedShaderIndex = ResolvedShaderIndex,
                StartVertexRowIndex = StartVertexRowIndex,
                EndVertexRowIndexExclusive = EndVertexRowIndexExclusive,
                ResolvedPacketCount = ResolvedPacketCount,
                ResolvedVertexRowCount = ResolvedVertexRowCount,
                ResolvedLogicalVertexCount = ResolvedLogicalVertexCount
            };
        }
    }

    private sealed record GlowRgbaResolvedRowSpan(
        TiePacketDataBlock Block,
        int StartOffset,
        int EndOffset,
        IReadOnlyList<TiePacketVertexRow> Rows);
}

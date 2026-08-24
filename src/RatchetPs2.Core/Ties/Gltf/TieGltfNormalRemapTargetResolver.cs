namespace RatchetPs2.Core.Ties;

internal static class TieGltfNormalRemapTargetResolver
{
    public static IReadOnlyDictionary<int, int> BuildPacketDinkyUploadBases(
        TieClass tie,
        TieLodTopology topology)
    {
        var bases = new Dictionary<int, int>();
        var nextBase = 0;
        foreach (var block in tie.PacketDataBlocks
                     .Where(block => block.LodIndex == topology.LodIndex)
                     .OrderBy(block => block.PacketIndex))
        {
            bases[block.PacketIndex] = nextBase;
            nextBase += Align(GetPacketUploadTargetSpan(block), 4);
        }

        return bases;
    }

    public static IReadOnlyDictionary<int, TieGltfPacketUploadLayout> BuildPacketUploadLayouts(
        TieClass tie,
        TieLodTopology topology)
    {
        return BuildPacketUploadLayouts(tie.PacketDataBlocks, topology);
    }

    public static IReadOnlyDictionary<int, TieGltfPacketUploadLayout> BuildPacketUploadLayouts(
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        TieLodTopology topology)
    {
        var layouts = new Dictionary<int, TieGltfPacketUploadLayout>();
        var nextBase = 0;
        foreach (var block in packetDataBlocks
                     .Where(block => block.LodIndex == topology.LodIndex)
                     .OrderBy(block => block.PacketIndex))
        {
            var dinkyCount = block.UnpackHeader?.DinkyVertexCount ?? 0;
            var fatCount = block.DecodedVertices.Count(vertex => vertex.Kind == TiePacketDecodedVertexKind.Fat);
            layouts[block.PacketIndex] = new TieGltfPacketUploadLayout(nextBase, dinkyCount, fatCount);
            nextBase += Align(dinkyCount + fatCount, 4);
        }

        return layouts;
    }

    public static bool TryGetPacketDinkyUploadTarget(
        TieLogicalVertex vertex,
        IReadOnlyDictionary<int, int> packetDinkyUploadBases,
        out int target)
    {
        target = 0;
        if (vertex.DecodedVertex?.Kind != TiePacketDecodedVertexKind.Dinky
            || !packetDinkyUploadBases.TryGetValue(vertex.PacketIndex, out var packetBase))
        {
            return false;
        }

        target = packetBase + vertex.DecodedVertex.SourceIndex;
        return true;
    }

    public static bool TryGetPacketUploadTarget(
        TieLogicalVertex vertex,
        IReadOnlyDictionary<int, TieGltfPacketUploadLayout> packetUploadLayouts,
        out int target)
    {
        target = 0;
        if (vertex.DecodedVertex is null
            || !packetUploadLayouts.TryGetValue(vertex.PacketIndex, out var layout))
        {
            return false;
        }

        target = vertex.DecodedVertex.Kind == TiePacketDecodedVertexKind.Fat
            ? layout.Base + layout.DinkyCount + vertex.DecodedVertex.SourceIndex
            : layout.Base + vertex.DecodedVertex.SourceIndex;
        return target >= layout.Base && target < layout.Base + layout.DinkyCount + layout.FatCount;
    }

    private static int GetPacketUploadTargetSpan(TiePacketDataBlock block)
    {
        var dinkyCount = block.UnpackHeader?.DinkyVertexCount ?? 0;
        var fatCount = block.DecodedVertices.Count(vertex => vertex.Kind == TiePacketDecodedVertexKind.Fat);
        return dinkyCount + fatCount;
    }

    private static int Align(int value, int alignment)
    {
        return (value + alignment - 1) / alignment * alignment;
    }
}

internal readonly record struct TieGltfPacketUploadLayout(int Base, int DinkyCount, int FatCount);

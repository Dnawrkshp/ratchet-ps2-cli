namespace RatchetPs2.Core.Ties;

internal static class TieGlowRgbaReader
{
    public static (List<TieGlowRgbaRemap> Remaps, List<TieGlowRgbaVertex> Vertices) Read(
        TieClassHeader header,
        IReadOnlyList<TiePacketDataBlock> packetDataBlocks,
        IReadOnlyList<TieLodTopology> lodTopologies,
        IReadOnlyList<TieRgbaRemapOperation> rgbaRemapOperations)
    {
        var rgba = TieRgba32.FromRaw(header.GlowRgba);
        var recipes = rgbaRemapOperations
            .GroupBy(operation => (operation.LodIndex, operation.TargetCacheSlot))
            .Select(group => group
                .OrderBy(operation => operation.GroupIndex)
                .ThenBy(operation => operation.Offset)
                .ThenBy(operation => operation.OperationIndex)
                .Last())
            .Where(operation => operation.SourceSlots.Contains(TieRgbaRemapOperation.ConstantColorSourceSlot))
            .OrderBy(operation => operation.LodIndex)
            .ThenBy(operation => operation.GroupIndex)
            .ThenBy(operation => operation.Offset)
            .ThenBy(operation => operation.OperationIndex)
            .Select((operation, remapIndex) => (Operation: operation, RemapIndex: remapIndex))
            .ToArray();
        if (recipes.Length == 0)
        {
            return ([], []);
        }

        var vertices = new List<TieGlowRgbaVertex>();
        foreach (var topology in lodTopologies)
        {
            var recipesByTarget = recipes
                .Where(recipe => recipe.Operation.LodIndex == topology.LodIndex)
                .ToDictionary(recipe => recipe.Operation.TargetCacheSlot);
            if (recipesByTarget.Count == 0)
            {
                continue;
            }

            var packetUploadLayouts = TieGltfNormalRemapTargetResolver.BuildPacketUploadLayouts(
                packetDataBlocks,
                topology);
            foreach (var vertex in topology.LogicalVertices)
            {
                var row = vertex.VertexRow ?? vertex.AddressRow;
                if (row is null
                    || !TieGltfNormalRemapTargetResolver.TryGetPacketUploadTarget(
                        vertex,
                        packetUploadLayouts,
                        out var target)
                    || !recipesByTarget.TryGetValue(target, out var recipe))
                {
                    continue;
                }

                vertices.Add(new TieGlowRgbaVertex
                {
                    RemapIndex = recipe.RemapIndex,
                    RemapOffset = recipe.Operation.Offset,
                    LodIndex = vertex.LodIndex,
                    PacketIndex = vertex.PacketIndex,
                    StripIndex = vertex.StripIndex,
                    PacketStripIndex = vertex.PacketStripIndex,
                    IndexInStrip = vertex.IndexInStrip,
                    LogicalVertexIndex = vertex.LogicalVertexIndex,
                    VertexRowIndex = row.Index,
                    VertexRowOffset = row.Offset,
                    RawRgba = header.GlowRgba,
                    Rgba = rgba,
                    GlowWeight = recipe.Operation.SourceSlots.Count(
                        source => source == TieRgbaRemapOperation.ConstantColorSourceSlot)
                        / (float)recipe.Operation.SourceSlots.Length
                });
            }
        }

        return (
            recipes.Select(recipe => BuildRemap(recipe.RemapIndex, recipe.Operation, vertices, header.GlowRgba, rgba)).ToList(),
            vertices.OrderBy(vertex => vertex.LodIndex).ThenBy(vertex => vertex.LogicalVertexIndex).ToList());
    }

    private static TieGlowRgbaRemap BuildRemap(
        int remapIndex,
        TieRgbaRemapOperation operation,
        IReadOnlyList<TieGlowRgbaVertex> vertices,
        int rawRgba,
        TieRgba32 rgba)
    {
        var resolved = vertices.Where(vertex => vertex.RemapIndex == remapIndex).ToArray();
        var packetIndices = resolved.Select(vertex => vertex.PacketIndex).Distinct().Order().ToArray();
        var rowCount = resolved.Select(vertex => (vertex.PacketIndex, vertex.VertexRowIndex)).Distinct().Count();
        var packetIndex = packetIndices.Length > 0 ? packetIndices[0] : (int?)null;
        return new TieGlowRgbaRemap
        {
            RemapIndex = remapIndex,
            Offset = operation.Offset,
            RawRgba = rawRgba,
            Rgba = rgba,
            ResolutionKind = resolved.Length > 0
                ? TieGlowRgbaRemapResolutionKind.PacketVertexRowRange
                : TieGlowRgbaRemapResolutionKind.Unresolved,
            ResolvedStartOffset = resolved.Length > 0 ? resolved.Min(vertex => vertex.VertexRowOffset) : null,
            EndOffset = resolved.Length > 0 ? resolved.Max(vertex => vertex.VertexRowOffset) + 0x10 : null,
            LodIndex = operation.LodIndex,
            PacketIndex = packetIndex,
            ResolvedPacketIndex = packetIndex,
            ResolvedPacketIndices = packetIndices,
            StartVertexRowIndex = packetIndices.Length == 1 ? resolved.Min(vertex => (int?)vertex.VertexRowIndex) : null,
            EndVertexRowIndexExclusive = packetIndices.Length == 1 ? resolved.Max(vertex => (int?)vertex.VertexRowIndex) + 1 : null,
            ResolvedPacketCount = packetIndices.Length,
            ResolvedVertexRowCount = rowCount,
            ResolvedLogicalVertexCount = resolved.Length
        };
    }
}

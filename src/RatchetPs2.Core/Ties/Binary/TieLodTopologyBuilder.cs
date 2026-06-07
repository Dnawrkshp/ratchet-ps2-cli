namespace RatchetPs2.Core.Ties;

internal static class TieLodTopologyBuilder
{
    public static List<TieLodTopology> Build(
        IReadOnlyList<TiePacketDataBlock> blocks,
        TieClassReadOptions options)
    {
        var topologies = new List<TieLodTopology>(3);
        for (var lodIndex = 0; lodIndex < 3; lodIndex++)
        {
            var logicalVertices = new List<TieLogicalVertex>();
            var strips = new List<TieTriangleStrip>();
            var triangles = new List<TieTriangle>();
            var logicalVertexIndex = 0;
            var packetVertexRowCount = 0;

            foreach (var block in blocks
                         .Where(block => block.LodIndex == lodIndex)
                         .OrderBy(block => block.PacketIndex))
            {
                packetVertexRowCount += block.VertexRows.Count;
                if (CanUseDecodedPacketPrimitives(block))
                {
                    TiePacketPrimitive? previousPrimitive = null;
                    var logicalVertexIndexByGsPacketWriteOffset = new Dictionary<int, int>();
                    foreach (var primitive in block.Primitives)
                    {
                        var stripControl = block.StripControls[primitive.PacketStripIndex];
                        var tokenReferencePrimitive = primitive.Index >= 0
                            && primitive.Index < block.TokenReferencePrimitives.Count
                                ? block.TokenReferencePrimitives[primitive.Index]
                                : null;
                        var usesPreviousStripReferencePhase = UsesPreviousStripReferencePhase(
                            stripControl,
                            primitive,
                            previousPrimitive,
                            options);
                        var usesTokenReferenceTriangleSequence = UsesTokenReferenceTriangleSequence(
                            stripControl,
                            primitive,
                            tokenReferencePrimitive,
                            options);
                        var stripIndex = strips.Count;
                        var logicalVertexStartIndex = logicalVertexIndex;
                        var triangleStartIndex = triangles.Count;
                        var stripLogicalVertices = new List<TieLogicalVertex>(primitive.Vertices.Count);

                        for (var i = 0; i < primitive.Vertices.Count; i++)
                        {
                            var reference = primitive.Vertices[i];
                            var token = i < stripControl.Tokens.Length ? stripControl.Tokens[i] : (byte)0;
                            var sourceRow = reference.Vertex.SourceRow;
                            var logicalVertex = new TieLogicalVertex
                            {
                                LodIndex = lodIndex,
                                PacketIndex = block.PacketIndex,
                                PacketStripIndex = stripControl.Index,
                                StripIndex = stripIndex,
                                IndexInStrip = i,
                                LogicalVertexIndex = logicalVertexIndex + i,
                                VuAddress = reference.GsPacketWriteOffset,
                                Token = token,
                                GsPacketWriteOffset = reference.GsPacketWriteOffset,
                                MappingKind = reference.IsSecondaryWriteOffset
                                    ? TieLogicalVertexMappingKind.SecondaryRowAddress
                                    : TieLogicalVertexMappingKind.PrimaryRowAddress,
                                DecodedVertex = reference.Vertex,
                                AddressRow = sourceRow,
                                VertexRow = sourceRow
                            };
                            stripLogicalVertices.Add(logicalVertex);
                            logicalVertices.Add(logicalVertex);
                            logicalVertexIndexByGsPacketWriteOffset[reference.GsPacketWriteOffset] =
                                logicalVertex.LogicalVertexIndex;
                        }

                        var stripTriangles = BuildDecodedPrimitiveTriangles(
                            lodIndex,
                            stripIndex,
                            primitive,
                            tokenReferencePrimitive,
                            usesTokenReferenceTriangleSequence,
                            usesPreviousStripReferencePhase,
                            logicalVertexStartIndex,
                            logicalVertexIndexByGsPacketWriteOffset);
                        strips.Add(new TieTriangleStrip
                        {
                            LodIndex = lodIndex,
                            PacketIndex = block.PacketIndex,
                            PacketStripIndex = stripControl.Index,
                            StripIndex = stripIndex,
                            LogicalVertexStartIndex = logicalVertexStartIndex,
                            LogicalVertexCount = primitive.Vertices.Count,
                            TriangleStartIndex = triangleStartIndex,
                            TriangleCount = stripTriangles.Count,
                            VuAddress = stripControl.VuAddress,
                            Flags = stripControl.Flags,
                            UsesPreviousStripReferencePhase = usesPreviousStripReferencePhase,
                            UsesTokenReferenceTriangleSequence = usesTokenReferenceTriangleSequence,
                            ShaderIndex = primitive.MaterialIndex >= 0 ? primitive.MaterialIndex : null,
                            Tokens = (byte[])stripControl.Tokens.Clone(),
                            LogicalVertices = stripLogicalVertices
                        });

                        triangles.AddRange(stripTriangles);

                        logicalVertexIndex += primitive.Vertices.Count;
                        previousPrimitive = primitive;
                    }

                    continue;
                }

                var vertexRowsByVuAddress = BuildVertexAddressLookup(block.VertexRows);

                foreach (var stripControl in block.StripControls)
                {
                    var usesPreviousStripReferencePhase = UsesPreviousStripReferencePhase(
                        stripControl,
                        options);
                    var stripIndex = strips.Count;
                    var logicalVertexStartIndex = logicalVertexIndex;
                    var triangleStartIndex = triangles.Count;
                    var triangleCount = Math.Max(0, stripControl.TokenCount - 2);
                    var stripLogicalVertices = new List<TieLogicalVertex>(stripControl.TokenCount);

                    for (var i = 0; i < stripControl.TokenCount; i++)
                    {
                        var decodedToken = i < stripControl.DecodedTokens.Count ? stripControl.DecodedTokens[i] : null;
                        var vuAddress = options.UseStripTokenReferencesForTopology
                            ? decodedToken?.ReferencedGsPacketWriteOffset ?? stripControl.VuAddress + 1 + i * 3
                            : stripControl.VuAddress + 1 + i * 3;
                        var token = decodedToken?.Value ?? (i < stripControl.Tokens.Length ? stripControl.Tokens[i] : (byte)0);
                        vertexRowsByVuAddress.TryGetValue(vuAddress, out var mappedVertexRow);
                        var mappingKind = mappedVertexRow is null
                            ? TieLogicalVertexMappingKind.Unresolved
                            : mappedVertexRow.MappingKind;
                        var logicalVertex = new TieLogicalVertex
                        {
                            LodIndex = lodIndex,
                            PacketIndex = block.PacketIndex,
                            PacketStripIndex = stripControl.Index,
                            StripIndex = stripIndex,
                            IndexInStrip = i,
                            LogicalVertexIndex = logicalVertexIndex + i,
                            VuAddress = vuAddress,
                            Token = token,
                            GsPacketWriteOffset = vuAddress,
                            MappingKind = mappingKind,
                            DecodedVertex = null,
                            AddressRow = mappedVertexRow?.AddressRow,
                            VertexRow = mappedVertexRow?.VertexRow
                        };
                        stripLogicalVertices.Add(logicalVertex);
                        logicalVertices.Add(logicalVertex);
                    }

                    strips.Add(new TieTriangleStrip
                    {
                        LodIndex = lodIndex,
                        PacketIndex = block.PacketIndex,
                        PacketStripIndex = stripControl.Index,
                        StripIndex = stripIndex,
                        LogicalVertexStartIndex = logicalVertexStartIndex,
                        LogicalVertexCount = stripControl.TokenCount,
                        TriangleStartIndex = triangleStartIndex,
                        TriangleCount = triangleCount,
                        VuAddress = stripControl.VuAddress,
                        Flags = stripControl.Flags,
                        UsesPreviousStripReferencePhase = usesPreviousStripReferencePhase,
                        UsesTokenReferenceTriangleSequence = false,
                        ShaderIndex = null,
                        Tokens = (byte[])stripControl.Tokens.Clone(),
                        LogicalVertices = stripLogicalVertices
                    });

                    var flip = ((stripControl.Flags & 0x20) != 0) ^ usesPreviousStripReferencePhase;
                    for (var i = 2; i < stripControl.TokenCount; i++)
                    {
                        var a = logicalVertexStartIndex + i - 2;
                        var b = logicalVertexStartIndex + i - 1;
                        var c = logicalVertexStartIndex + i;
                        triangles.Add(flip
                            ? new TieTriangle(lodIndex, stripIndex, i - 2, a, c, b)
                            : new TieTriangle(lodIndex, stripIndex, i - 2, a, b, c));
                        flip = !flip;
                    }

                    logicalVertexIndex += stripControl.TokenCount;
                }
            }

            topologies.Add(new TieLodTopology
            {
                LodIndex = lodIndex,
                LogicalVertexCount = logicalVertexIndex,
                PacketVertexRowCount = packetVertexRowCount,
                PrimaryAddressMappedLogicalVertexCount = logicalVertices.Count(
                    vertex => vertex.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress),
                SecondaryAddressMappedLogicalVertexCount = logicalVertices.Count(
                    vertex => vertex.MappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress),
                UnresolvedLogicalVertexCount = logicalVertices.Count(
                    vertex => vertex.MappingKind == TieLogicalVertexMappingKind.Unresolved),
                StripCount = strips.Count,
                TriangleCount = triangles.Count,
                LogicalVertices = logicalVertices,
                Strips = strips,
                Triangles = triangles
            });
        }

        return topologies;
    }

    private static bool CanUseDecodedPacketPrimitives(TiePacketDataBlock block)
    {
        if (block.Primitives.Count == 0 || block.Primitives.Count != block.StripControls.Count)
        {
            return false;
        }

        foreach (var primitive in block.Primitives)
        {
            if (primitive.PacketStripIndex < 0 || primitive.PacketStripIndex >= block.StripControls.Count)
            {
                return false;
            }

            if (primitive.Vertices.Count != block.StripControls[primitive.PacketStripIndex].TokenCount)
            {
                return false;
            }
        }

        return true;
    }

    private static bool UsesPreviousStripReferencePhase(
        TiePacketStripControl stripControl,
        TiePacketPrimitive primitive,
        TiePacketPrimitive? previousPrimitive,
        TieClassReadOptions options)
    {
        return UsesPreviousStripReferenceToken(stripControl, options)
            && previousPrimitive is not null
            && primitive.MaterialIndex == previousPrimitive.MaterialIndex
            && primitive.Vertices.Count > 0
            && primitive.Vertices[0].IsSecondaryWriteOffset;
    }

    private static bool UsesPreviousStripReferencePhase(
        TiePacketStripControl stripControl,
        TieClassReadOptions options)
    {
        return UsesPreviousStripReferenceToken(stripControl, options);
    }

    private static bool UsesTokenReferenceTriangleSequence(
        TiePacketStripControl stripControl,
        TiePacketPrimitive primitive,
        TiePacketPrimitive? tokenReferencePrimitive,
        TieClassReadOptions options)
    {
        // GC previous-strip tokens are decoded and surfaced in diagnostics, but direct
        // token-reference triangles are not yet safe to emit. Model 336 proves that
        // some first-token references seed state only: drawing them creates stretched
        // bridge faces or flips already-correct packet 4 tex_0005 triangles.
        return false
            && options.UsePreviousStripReferencePhaseForTopology
            && UsesPreviousStripReferenceToken(stripControl, options)
            && tokenReferencePrimitive is not null
            && tokenReferencePrimitive.PacketStripIndex == primitive.PacketStripIndex
            && tokenReferencePrimitive.Vertices.Count == primitive.Vertices.Count
            && tokenReferencePrimitive.Vertices.Count >= 3
            && !PrimitiveVertexReferencesMatch(primitive, tokenReferencePrimitive);
    }

    private static bool UsesPreviousStripReferenceToken(
        TiePacketStripControl stripControl,
        TieClassReadOptions options)
    {
        return options.UsePreviousStripReferencePhaseForTopology
            && stripControl.DecodedTokens.Count > 0
            && stripControl.DecodedTokens[0].AddressMode
                == TiePacketStripTokenAddressMode.PreviousStripVertexReference;
    }

    private static List<TieTriangle> BuildDecodedPrimitiveTriangles(
        int lodIndex,
        int stripIndex,
        TiePacketPrimitive primitive,
        TiePacketPrimitive? tokenReferencePrimitive,
        bool useTokenReferenceTriangleSequence,
        bool usesPreviousStripReferencePhase,
        int logicalVertexStartIndex,
        IReadOnlyDictionary<int, int> logicalVertexIndexByGsPacketWriteOffset)
    {
        var references = useTokenReferenceTriangleSequence && tokenReferencePrimitive is not null
            ? tokenReferencePrimitive.Vertices
            : primitive.Vertices;
        var logicalIndices = new int[references.Count];
        for (var i = 0; i < references.Count; i++)
        {
            if (useTokenReferenceTriangleSequence)
            {
                if (!logicalVertexIndexByGsPacketWriteOffset.TryGetValue(
                        references[i].GsPacketWriteOffset,
                        out logicalIndices[i]))
                {
                    return BuildPhysicalPrimitiveTriangles(
                        lodIndex,
                        stripIndex,
                        primitive,
                        usesPreviousStripReferencePhase,
                        logicalVertexStartIndex);
                }
            }
            else
            {
                logicalIndices[i] = logicalVertexStartIndex + i;
            }
        }

        var triangles = new List<TieTriangle>(Math.Max(0, logicalIndices.Length - 2));
        var flip = primitive.WindingOrder;
        if (!useTokenReferenceTriangleSequence)
        {
            flip ^= usesPreviousStripReferencePhase;
        }

        var firstTriangleIndex = useTokenReferenceTriangleSequence ? 3 : 2;
        if (useTokenReferenceTriangleSequence)
        {
            flip = !flip;
        }

        for (var i = firstTriangleIndex; i < logicalIndices.Length; i++)
        {
            var a = logicalIndices[i - 2];
            var b = logicalIndices[i - 1];
            var c = logicalIndices[i];
            triangles.Add(flip
                ? new TieTriangle(lodIndex, stripIndex, triangles.Count, a, c, b)
                : new TieTriangle(lodIndex, stripIndex, triangles.Count, a, b, c));
            flip = !flip;
        }

        return triangles;
    }

    private static List<TieTriangle> BuildPhysicalPrimitiveTriangles(
        int lodIndex,
        int stripIndex,
        TiePacketPrimitive primitive,
        bool usesPreviousStripReferencePhase,
        int logicalVertexStartIndex)
    {
        var triangles = new List<TieTriangle>(Math.Max(0, primitive.Vertices.Count - 2));
        var flip = primitive.WindingOrder ^ usesPreviousStripReferencePhase;
        for (var i = 2; i < primitive.Vertices.Count; i++)
        {
            var a = logicalVertexStartIndex + i - 2;
            var b = logicalVertexStartIndex + i - 1;
            var c = logicalVertexStartIndex + i;
            triangles.Add(flip
                ? new TieTriangle(lodIndex, stripIndex, triangles.Count, a, c, b)
                : new TieTriangle(lodIndex, stripIndex, triangles.Count, a, b, c));
            flip = !flip;
        }

        return triangles;
    }

    private static bool PrimitiveVertexReferencesMatch(
        TiePacketPrimitive left,
        TiePacketPrimitive right)
    {
        if (left.Vertices.Count != right.Vertices.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Vertices.Count; i++)
        {
            if (left.Vertices[i].GsPacketWriteOffset != right.Vertices[i].GsPacketWriteOffset
                || left.Vertices[i].Vertex.Index != right.Vertices[i].Vertex.Index
                || left.Vertices[i].IsSecondaryWriteOffset != right.Vertices[i].IsSecondaryWriteOffset)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<int, VertexAddressMapping> BuildVertexAddressLookup(IReadOnlyList<TiePacketVertexRow> rows)
    {
        var lookup = new Dictionary<int, VertexAddressMapping>();
        foreach (var row in rows)
        {
            if (row.HasPrimaryVuAddress)
            {
                AddVertexAddressMapping(
                    lookup,
                    (int)row.PrimaryVuAddress,
                    TieLogicalVertexMappingKind.PrimaryRowAddress,
                    row,
                    ResolveAddressVertexRow(rows, row, TieLogicalVertexMappingKind.PrimaryRowAddress));
            }
        }

        foreach (var row in rows)
        {
            if (!row.HasSecondaryVuAddress)
            {
                continue;
            }

            AddVertexAddressMapping(
                lookup,
                (int)row.SecondaryVuAddress,
                TieLogicalVertexMappingKind.SecondaryRowAddress,
                row,
                ResolveAddressVertexRow(rows, row, TieLogicalVertexMappingKind.SecondaryRowAddress));
        }

        return lookup;
    }

    private static void AddVertexAddressMapping(
        Dictionary<int, VertexAddressMapping> lookup,
        int vuAddress,
        TieLogicalVertexMappingKind mappingKind,
        TiePacketVertexRow addressRow,
        TiePacketVertexRow vertexRow)
    {
        var mapping = new VertexAddressMapping(mappingKind, addressRow, vertexRow);
        if (!lookup.TryGetValue(vuAddress, out var existing) || PreferAddressMapping(mapping, existing))
        {
            lookup[vuAddress] = mapping;
        }
    }

    private static bool PreferAddressMapping(
        VertexAddressMapping candidate,
        VertexAddressMapping existing)
    {
        if (existing.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress)
        {
            return false;
        }

        if (candidate.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress)
        {
            return true;
        }

        var candidateUsesAddressRowAsVertex = ReferenceEquals(candidate.AddressRow, candidate.VertexRow);
        var existingUsesAddressRowAsVertex = ReferenceEquals(existing.AddressRow, existing.VertexRow);
        if (candidateUsesAddressRowAsVertex != existingUsesAddressRowAsVertex)
        {
            return candidateUsesAddressRowAsVertex;
        }

        var candidateLooksLikePosition = HasLikelyPositionVector(candidate.VertexRow);
        var existingLooksLikePosition = HasLikelyPositionVector(existing.VertexRow);
        if (candidateLooksLikePosition != existingLooksLikePosition)
        {
            return candidateLooksLikePosition;
        }

        return candidate.AddressRow.Index > existing.AddressRow.Index;
    }

    private static TiePacketVertexRow ResolveAddressVertexRow(
        IReadOnlyList<TiePacketVertexRow> rows,
        TiePacketVertexRow addressRow,
        TieLogicalVertexMappingKind mappingKind)
    {
        if (IsLikelyAddressMarkerRow(addressRow))
        {
            var adjacentOffset = mappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress ? 1 : -1;
            var adjacentIndex = addressRow.Index + adjacentOffset;
            if (adjacentIndex >= 0
                && adjacentIndex < rows.Count
                && HasLikelyPositionVector(rows[adjacentIndex]))
            {
                return rows[adjacentIndex];
            }
        }

        // Some address tags sit on marker/attribute qwords; nearby qwords carry
        // the coordinate-like vertex data in known tie fixtures.
        if (HasLikelyPositionVector(addressRow))
        {
            return addressRow;
        }

        var nextIndex = addressRow.Index + 1;
        if (nextIndex < rows.Count && HasLikelyPositionVector(rows[nextIndex]))
        {
            return rows[nextIndex];
        }

        var previousIndex = addressRow.Index - 1;
        if (previousIndex >= 0 && HasLikelyPositionVector(rows[previousIndex]))
        {
            return rows[previousIndex];
        }

        return addressRow;
    }

    private static bool IsLikelyAddressMarkerRow(TiePacketVertexRow row)
    {
        return row.Z == 4096 && (row.HasPrimaryVuAddress || row.HasSecondaryVuAddress);
    }

    private static bool HasLikelyPositionVector(TiePacketVertexRow row)
    {
        return TiePacketVertexRowClassifier.TrySelectPositionSlot(row, out _);
    }

    private sealed record VertexAddressMapping(
        TieLogicalVertexMappingKind MappingKind,
        TiePacketVertexRow AddressRow,
        TiePacketVertexRow VertexRow);
}

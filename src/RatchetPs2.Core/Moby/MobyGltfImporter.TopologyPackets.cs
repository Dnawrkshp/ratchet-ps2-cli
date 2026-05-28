using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static VifBuildResult BuildVifData(
        byte[] templateVifData,
        byte[]? templateVifTextureData,
        IReadOnlyList<uint> triangleIndices,
        bool preserveTemplateTopologyTail = false,
        bool compactTopologyPacket = false,
        bool forceZeroMarkerTopology = false,
        bool forceIsolatedTriangleTopology = false,
        bool generateMinimalVifContainer = false,
        byte? generatedVifDomainCapacity = null)
    {
        if (triangleIndices.Count < 3 || triangleIndices.Count % 3 != 0)
        {
            throw new InvalidDataException("glTF mesh indices must contain triangles.");
        }

        var adjustedTemplateVifTextureData = templateVifTextureData is null
            ? null
            : (byte[])templateVifTextureData.Clone();
        var templateCombinedVifData = Combine(templateVifData, adjustedTemplateVifTextureData);
        var topologyTokens = forceIsolatedTriangleTopology
            ? Ps2VifTopology.BuildIsolatedTriangleTokens(triangleIndices)
            : Ps2VifTopology.BuildRestartStripTokens(triangleIndices);
        var packet = TryFindTopologyPacket(templateCombinedVifData);
        var prefixBytes = packet is not null && packet.PayloadLength >= 4
            ? templateCombinedVifData.AsSpan(packet.Offset + 4, 4).ToArray()
            : [0xFE, 0x1F, 0x01, 0x00];
        var useZeroMarkerTopology = forceZeroMarkerTopology
            || TemplateUsesZeroMarkerTopology(packet, templateCombinedVifData, templateVifTextureData);
        if (useZeroMarkerTopology)
        {
            topologyTokens = BuildZeroMarkerTopologyTokens(topologyTokens, prefixBytes);
        }
        var payload = Ps2VifTopology.BuildPayload(topologyTokens, prefixBytes);
        var generatedRowUsage = SummarizeGeneratedTopologyRowUsage(
            payload,
            adjustedTemplateVifTextureData,
            Math.Max(127, GetMaxIndex(triangleIndices) + 1));
        if (packet is null)
        {
            var standaloneVertexDomainCount = generatedVifDomainCapacity
                ?? (byte)Math.Min(127, Math.Max(1, GetMaxIndex(triangleIndices) + 1));
            return new VifBuildResult(
                generateMinimalVifContainer
                    ? BuildGeneratedMinimalTopologyVifData(
                        payload,
                        standaloneVertexDomainCount,
                        compactTopologyPacket,
                        adjustedTemplateVifTextureData is not null)
                    : BuildStandaloneTopologyVifData(payload),
                adjustedTemplateVifTextureData,
                topologyTokens.Count - (triangleIndices.Count / 3),
                PreservedTemplateLayout: false,
                ExpandedTopologyPacket: false,
                OriginalTopologyPayloadBytes: 0,
                NewTopologyPayloadBytes: payload.Length,
                ReusedTemplateTopology: false,
                RemappedTemplateTopology: false,
                UsedMetadataTopologyLayout: false,
                GeneratedTopologyFromGltf: true,
                GeneratedTopologyTokenCount: topologyTokens.Count,
                GeneratedTopologySourceTriangleCount: triangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata: true,
                GeneratedTopologyMatchesSourceTriangles: true,
                GeneratedTopologyPreservesTemplateControlMarkers: true,
                GeneratedTopologyMatchesTemplateControlShape: true,
                TemplateTopologyRestartCount: 0,
                GeneratedTopologyRestartCount: 0,
                TemplateTopologyNegativeTokenCount: 0,
                GeneratedTopologyNegativeTokenCount: 0,
                TemplateTopologyShape: null,
                GeneratedTopologyShape: null,
                GeneratedTopologyRowUsage: generatedRowUsage);
        }

        var vertexDomainCount = generatedVifDomainCapacity
            ?? (TryReadLeadingVertexDomainUnpackCount(templateVifData, out var domainCount)
                ? domainCount
                : (byte)Math.Min(127, Math.Max(1, GetMaxIndex(triangleIndices) + 1)));
        var topologyPacketOffset = generateMinimalVifContainer
            ? 4 + vertexDomainCount * 4
            : packet.Offset;
        var compactLayout = compactTopologyPacket
            ? ResolveCompactTopologyLayout(payload.Length, topologyPacketOffset, packet, templateVifData.Length, adjustedTemplateVifTextureData)
            : null;
        var targetPayloadLength = compactLayout?.PayloadLength ?? Math.Max(payload.Length, packet.PayloadLength);
        targetPayloadLength = Align(targetPayloadLength, 4);
        if (targetPayloadLength / 4 > 0xFF)
        {
            throw new InvalidDataException(
                $"Regenerated topology VIF payload is {targetPayloadLength} bytes. v1 importer supports at most 1020 bytes per topology packet.");
        }

        var replacementPayload = new byte[targetPayloadLength];
        Array.Fill<byte>(replacementPayload, 0x80);
        if (preserveTemplateTopologyTail)
        {
            var templatePayloadLength = Math.Min(packet.PayloadLength, templateCombinedVifData.Length - packet.Offset - 4);
            if (templatePayloadLength > 0)
            {
                var copyLength = Math.Min(templatePayloadLength, replacementPayload.Length);
                templateCombinedVifData.AsSpan(packet.Offset + 4, copyLength)
                    .CopyTo(replacementPayload.AsSpan(0, copyLength));
            }
        }
        payload.CopyTo(replacementPayload, 0);

        using var stream = generateMinimalVifContainer
            ? new MemoryStream(4 + vertexDomainCount * 4 + 4 + targetPayloadLength)
            : new MemoryStream(templateCombinedVifData.Length - packet.TotalLength + 4 + targetPayloadLength);
        if (generateMinimalVifContainer)
        {
            WriteGeneratedVertexDomainUnpack(stream, vertexDomainCount);
        }
        else
        {
            stream.Write(templateCombinedVifData, 0, packet.Offset);
        }
        Ps2VifPacket.WriteHeader(stream, packet.Immediate, checked((byte)(targetPayloadLength / 4)), packet.CommandByte);
        stream.Write(replacementPayload, 0, replacementPayload.Length);

        if (!compactTopologyPacket && !generateMinimalVifContainer)
        {
            var suffixOffset = packet.Offset + packet.TotalLength;
            stream.Write(templateCombinedVifData, suffixOffset, templateCombinedVifData.Length - suffixOffset);
        }
        var combinedResult = stream.ToArray();
        Array.Resize(ref combinedResult, Align(combinedResult.Length, 0x10));
        byte[] resultVifData;
        byte[]? resultVifTextureData;
        if (compactTopologyPacket)
        {
            var compactVifDataLength = topologyPacketOffset + 4 + targetPayloadLength - (compactLayout?.TextureOverlapBytes ?? 0);
            if (compactVifDataLength <= 0 || compactVifDataLength % 0x10 != 0)
            {
                compactVifDataLength = Align(packet.Offset + 4 + targetPayloadLength, 0x10);
            }

            resultVifData = new byte[compactVifDataLength];
            Array.Copy(combinedResult, resultVifData, Math.Min(compactVifDataLength, combinedResult.Length));
            resultVifTextureData = adjustedTemplateVifTextureData;
        }
        else
        {
            SplitCombinedVifData(combinedResult, templateVifData.Length, out resultVifData, out resultVifTextureData);
        }

        return new VifBuildResult(
            resultVifData,
            resultVifTextureData,
            topologyTokens.Count - (triangleIndices.Count / 3),
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: targetPayloadLength > packet.PayloadLength,
            OriginalTopologyPayloadBytes: packet.PayloadLength,
            NewTopologyPayloadBytes: payload.Length,
            ReusedTemplateTopology: false,
            RemappedTemplateTopology: false,
            UsedMetadataTopologyLayout: false,
            GeneratedTopologyFromGltf: true,
            GeneratedTopologyTokenCount: topologyTokens.Count,
            GeneratedTopologySourceTriangleCount: triangleIndices.Count / 3,
            GeneratedTopologyPayloadFitsMetadata: targetPayloadLength <= packet.PayloadLength,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null,
            TemplateTopologyTrace: null,
            GeneratedTopologyTrace: null,
            GeneratedTopologyRowUsage: generatedRowUsage,
            TemplateTopologyZeroMarkers: null,
            GeneratedTopologyZeroMarkers: null,
            CompactTopologyTextureOverlapBytes: compactLayout?.TextureOverlapBytes);
    }

    private static void WriteGeneratedVertexDomainUnpack(Stream stream, byte vertexDomainCount)
    {
        const ushort vertexDomainImmediate = 0x80C2;
        const byte vertexDomainCommand = 0x75;
        Ps2VifPacket.WriteHeader(stream, vertexDomainImmediate, vertexDomainCount, vertexDomainCommand);
        stream.Write(new byte[vertexDomainCount * 4]);
    }

    private static CompactTopologyLayout ResolveCompactTopologyLayout(
        int payloadLength,
        int topologyPacketOffset,
        Ps2VifPacketSpan packet,
        int templateVifDataLength,
        byte[]? templateVifTextureData)
    {
        const int guardPaddingBytes = 12;
        var textureOverlapBytes = ResolveTemplateTextureOverlapBytes(packet, templateVifDataLength, templateVifTextureData);
        var aligned = Align(payloadLength + guardPaddingBytes + textureOverlapBytes, 4);
        while ((topologyPacketOffset + 4 + aligned - textureOverlapBytes) % 0x10 != 0)
        {
            aligned += 4;
        }

        return new CompactTopologyLayout(aligned, textureOverlapBytes);
    }

    private static int ResolveTemplateTextureOverlapBytes(
        Ps2VifPacketSpan packet,
        int templateVifDataLength,
        byte[]? templateVifTextureData)
    {
        if (templateVifTextureData is null || templateVifTextureData.Length == 0)
        {
            return 0;
        }

        var topologyEnd = packet.Offset + 4 + packet.PayloadLength;
        var overlapBytes = topologyEnd - templateVifDataLength;
        return overlapBytes is >= 0 and <= 12 && overlapBytes % 4 == 0
            ? overlapBytes
            : 0;
    }

    private static List<uint> BuildConservativeStripIndices(IReadOnlyList<uint> triangleIndices)
    {
        if (triangleIndices.Count < 3 || triangleIndices.Count % 3 != 0)
        {
            throw new InvalidDataException("glTF mesh indices must contain triangles.");
        }

        var stripIndices = new List<uint>();
        var currentStrip = new List<uint>();
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            if (a > 126 || b > 126 || c > 126)
            {
                throw new InvalidDataException("v1 importer supports mesh-local vertex indices up to 126.");
            }

            if (currentStrip.Count == 0)
            {
                stripIndices.Add(a);
                stripIndices.Add(b);
                stripIndices.Add(c);
                currentStrip.Add(a);
                currentStrip.Add(b);
                currentStrip.Add(c);
                continue;
            }

            if (TryAppendToCurrentStrip(currentStrip, a, b, c, out var nextVertex))
            {
                stripIndices.Add(nextVertex);
                currentStrip.Add(nextVertex);
                continue;
            }

            var last = currentStrip[^1];
            stripIndices.Add(last);
            stripIndices.Add(a);
            stripIndices.Add(a);
            stripIndices.Add(a);
            stripIndices.Add(b);
            stripIndices.Add(c);
            currentStrip.Clear();
            currentStrip.Add(a);
            currentStrip.Add(b);
            currentStrip.Add(c);
        }

        return stripIndices;
    }

    private static bool TryAppendToCurrentStrip(
        IReadOnlyList<uint> currentStrip,
        uint triangleA,
        uint triangleB,
        uint triangleC,
        out uint nextVertex)
    {
        nextVertex = 0;
        if (currentStrip.Count < 3)
        {
            return false;
        }

        var previousA = currentStrip[^2];
        var previousB = currentStrip[^1];
        var nextFlip = ((currentStrip.Count - 2) & 1) == 1;
        if (nextFlip)
        {
            if (triangleA != previousA || triangleC != previousB)
            {
                return false;
            }

            nextVertex = triangleB;
            return true;
        }

        if (triangleA != previousA || triangleB != previousB)
        {
            return false;
        }

        nextVertex = triangleC;
        return true;
    }

    private static bool TryBuildTemplateSegmentedTopologyTokens(
        ReadOnlySpan<byte> templatePayload,
        IReadOnlyList<ImportedTopologyPayloadToken> templateTokens,
        byte[]? texturePayload,
        int positionCount,
        IReadOnlyList<uint> triangleIndices,
        out List<byte> topologyTokens)
    {
        topologyTokens = [];
        if (!TryDecodeTopologyPayloadStrips(templatePayload, texturePayload, positionCount, out var templateStrips))
        {
            return false;
        }

        var templateTokenPatterns = BuildResolvedTopologyTokenPatterns(templatePayload, texturePayload, positionCount);
        if (templateTokenPatterns.Count != templateStrips.Count)
        {
            return false;
        }

        var sourceTriangles = new List<TopologyTriangle>(triangleIndices.Count / 3);
        for (var i = 0; i + 2 < triangleIndices.Count; i += 3)
        {
            sourceTriangles.Add(new TopologyTriangle(triangleIndices[i], triangleIndices[i + 1], triangleIndices[i + 2]));
        }

        var allSourceTriangleKeys = sourceTriangles
            .Select(triangle => BuildIndexTriangleKey(triangle.A, triangle.B, triangle.C))
            .ToHashSet(StringComparer.Ordinal);
        var allSourceVertices = sourceTriangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var allTraceVertices = templateTokenPatterns
            .SelectMany(pattern => pattern.Select(token => (uint)token.VertexIndex))
            .Where(vertexIndex => vertexIndex <= 126)
            .Distinct()
            .ToArray();
        var allCandidateVertices = allSourceVertices
            .Concat(allTraceVertices)
            .Distinct()
            .ToArray();
        var templateTrace = TraceTopology(templateTokenPatterns.SelectMany(pattern => pattern).ToList());
        var templateZeroMarkerCount = templateTokenPatterns.Sum(pattern => pattern.Count(token => token.IsZeroMarker));
        if (templateZeroMarkerCount > 1
            && templateTrace.Segments.Length <= 20
            && TryBuildTemplateTraceSegmentedTopologyTokens(
            templateTokenPatterns,
            templateTrace,
            sourceTriangles,
            allCandidateVertices,
            out var traceSegmentedTokens))
        {
            topologyTokens = traceSegmentedTokens;
            return true;
        }

        var sourceTriangleOffset = 0;
        for (var stripIndex = 0; stripIndex < templateStrips.Count; stripIndex++)
        {
            var templateStripTriangleCount = stripIndex < templateTrace.Segments.Length
                ? templateTrace.Segments[stripIndex].UniqueTriangleCount
                : templateStrips[stripIndex].Count / 3;
            var sourceStripTriangleCount = Math.Min(templateStripTriangleCount, sourceTriangles.Count - sourceTriangleOffset);
            if (sourceStripTriangleCount < 0)
            {
                return false;
            }

            var sourceStrip = sourceTriangles.GetRange(sourceTriangleOffset, sourceStripTriangleCount);
            sourceTriangleOffset += sourceStripTriangleCount;
            if (sourceStrip.Count == 0)
            {
                continue;
            }

            var tokenPattern = templateTokenPatterns[stripIndex];
            var boolTokenPattern = tokenPattern.Select(token => token.IsNonPositive).ToArray();
            if (!(stripIndex < templateTrace.Segments.Length
                    && TryBuildTracePatternedStripTokens(
                        sourceStrip,
                        tokenPattern,
                        templateTrace.Segments[stripIndex],
                        allCandidateVertices,
                        out var stripTokens,
                        out _))
                && !TryBuildPatternedStripTokens(sourceStrip, boolTokenPattern, allSourceTriangleKeys, out stripTokens)
                && !TryBuildSequentialStripTokens(sourceStrip, out stripTokens))
            {
                return false;
            }

            topologyTokens.AddRange(stripTokens);
        }

        return sourceTriangleOffset == sourceTriangles.Count;
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokens(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        TopologyTraceSummary templateTrace,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        out List<byte> topologyTokens)
    {
        topologyTokens = [];
        if (templateTokenPatterns.Count != templateTrace.Segments.Length)
        {
            return false;
        }

        var futureCapacity = new int[templateTrace.Segments.Length + 1];
        for (var i = templateTrace.Segments.Length - 1; i >= 0; i--)
        {
            futureCapacity[i] = futureCapacity[i + 1] + templateTrace.Segments[i].UniqueTriangleCount;
        }

        var memo = new HashSet<string>(StringComparer.Ordinal);
        return TryBuildTemplateTraceSegmentedTopologyTokens(
            templateTokenPatterns,
            templateTrace.Segments,
            sourceTriangles,
            sourceVertices,
            futureCapacity,
            segmentIndex: 0,
            sourceTriangleOffset: 0,
            topologyTokens,
            memo);
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokensUnordered(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        IReadOnlyList<TopologyTraceSegment> templateSegments,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlyList<int> futureCapacity,
        int segmentIndex,
        bool[] usedSourceTriangles,
        int usedSourceTriangleCount,
        List<byte> topologyTokens,
        HashSet<string> memo)
    {
        if (segmentIndex == templateSegments.Count)
        {
            return usedSourceTriangleCount == sourceTriangles.Count;
        }

        var memoKey = $"u|{segmentIndex}|{usedSourceTriangleCount}|{BuildUsedTriangleKey(usedSourceTriangles)}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var segment = templateSegments[segmentIndex];
        var tokenPattern = templateTokenPatterns[segmentIndex];
        var remainingSourceTriangles = sourceTriangles.Count - usedSourceTriangleCount;
        var minCount = Math.Max(0, remainingSourceTriangles - futureCapacity[segmentIndex + 1]);
        var maxCount = Math.Min(segment.UniqueTriangleCount, remainingSourceTriangles);
        var availableTriangles = new List<TopologyTriangle>();
        var availableIndices = new List<int>();
        for (var i = 0; i < sourceTriangles.Count; i++)
        {
            if (usedSourceTriangles[i])
            {
                continue;
            }

            availableTriangles.Add(sourceTriangles[i]);
            availableIndices.Add(i);
        }

        foreach (var count in BuildSegmentSourceCountCandidates(minCount, maxCount))
        {
            if (count > availableTriangles.Count)
            {
                continue;
            }

            if (!TryBuildTracePatternedStripTokens(
                    availableTriangles,
                    tokenPattern,
                    segment,
                    sourceVertices,
                    out var segmentTokens,
                    out var segmentUsedTriangles,
                    requiredUsedTriangleCount: count))
            {
                continue;
            }

            var nextUsedSourceTriangles = (bool[])usedSourceTriangles.Clone();
            var nextUsedSourceTriangleCount = usedSourceTriangleCount;
            for (var i = 0; i < segmentUsedTriangles.Length; i++)
            {
                if (!segmentUsedTriangles[i])
                {
                    continue;
                }

                var originalIndex = availableIndices[i];
                if (!nextUsedSourceTriangles[originalIndex])
                {
                    nextUsedSourceTriangles[originalIndex] = true;
                    nextUsedSourceTriangleCount++;
                }
            }

            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(segmentTokens);
            if (TryBuildTemplateTraceSegmentedTopologyTokensUnordered(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                nextUsedSourceTriangles,
                nextUsedSourceTriangleCount,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        return false;
    }

    private static bool TryBuildTemplateTraceSegmentedTopologyTokens(
        IReadOnlyList<List<TopologyTraceToken>> templateTokenPatterns,
        IReadOnlyList<TopologyTraceSegment> templateSegments,
        IReadOnlyList<TopologyTriangle> sourceTriangles,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlyList<int> futureCapacity,
        int segmentIndex,
        int sourceTriangleOffset,
        List<byte> topologyTokens,
        HashSet<string> memo)
    {
        if (segmentIndex == templateSegments.Count)
        {
            return sourceTriangleOffset == sourceTriangles.Count;
        }

        var memoKey = $"{segmentIndex}|{sourceTriangleOffset}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var segment = templateSegments[segmentIndex];
        var tokenPattern = templateTokenPatterns[segmentIndex];
        var remainingSourceTriangles = sourceTriangles.Count - sourceTriangleOffset;
        var minCount = Math.Max(0, remainingSourceTriangles - futureCapacity[segmentIndex + 1]);
        var maxCount = Math.Min(segment.UniqueTriangleCount, remainingSourceTriangles);
        foreach (var count in BuildSegmentSourceCountCandidates(minCount, maxCount))
        {
            var sourceStrip = count == 0
                ? []
                : sourceTriangles.Skip(sourceTriangleOffset).Take(count).ToList();
            if (!TryBuildTracePatternedStripTokens(sourceStrip, tokenPattern, segment, sourceVertices, out var segmentTokens, out _))
            {
                continue;
            }

            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(segmentTokens);
            if (TryBuildTemplateTraceSegmentedTopologyTokens(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                sourceTriangleOffset + count,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        if (minCount == 0 && tokenPattern.Any(token => token.IsZeroMarker))
        {
            var tokenStart = topologyTokens.Count;
            topologyTokens.AddRange(BuildTemplateTraceTokens(tokenPattern));
            if (TryBuildTemplateTraceSegmentedTopologyTokens(
                templateTokenPatterns,
                templateSegments,
                sourceTriangles,
                sourceVertices,
                futureCapacity,
                segmentIndex + 1,
                sourceTriangleOffset,
                topologyTokens,
                memo))
            {
                return true;
            }

            topologyTokens.RemoveRange(tokenStart, topologyTokens.Count - tokenStart);
        }

        return false;
    }

    private static IEnumerable<int> BuildSegmentSourceCountCandidates(int minCount, int maxCount)
    {
        if (maxCount < minCount)
        {
            yield break;
        }

        yield return maxCount;

        if (maxCount - 1 >= minCount)
        {
            yield return maxCount - 1;
        }

        if (minCount != maxCount && minCount != maxCount - 1)
        {
            yield return minCount;
        }
    }

    private static List<byte> BuildTemplateTraceTokens(IReadOnlyList<TopologyTraceToken> tokenPattern)
    {
        var tokens = new List<byte>(tokenPattern.Count);
        foreach (var token in tokenPattern)
        {
            tokens.Add(token.IsZeroMarker
                ? (byte)0
                : Ps2VifTopology.EncodeIndexToken((uint)token.VertexIndex, token.IsNonPositive));
        }

        return tokens;
    }

    private static bool TryBuildTracePatternedStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<TopologyTraceToken> tokenPattern,
        TopologyTraceSegment traceSegment,
        IReadOnlyList<uint> sourceVertices,
        out List<byte> tokens,
        out bool[] usedTriangleMask,
        int? requiredUsedTriangleCount = null)
    {
        tokens = [];
        usedTriangleMask = [];
        var expectedUsedTriangleCount = requiredUsedTriangleCount ?? triangles.Count;
        if (expectedUsedTriangleCount > triangles.Count
            || expectedUsedTriangleCount > traceSegment.UniqueTriangleCount
            || tokenPattern.Count != traceSegment.TokenCount)
        {
            return false;
        }

        var strictTrace = triangles.Count == traceSegment.UniqueTriangleCount;
        var templateOnlyTriangleCount = traceSegment.UniqueTriangleCount - expectedUsedTriangleCount;
        var expectedDegenerateCounts = new int[tokenPattern.Count];
        foreach (var controlEvent in traceSegment.ControlEvents)
        {
            if (controlEvent.SegmentTokenIndex < 0 || controlEvent.SegmentTokenIndex >= expectedDegenerateCounts.Length)
            {
                return false;
            }

            expectedDegenerateCounts[controlEvent.SegmentTokenIndex] += controlEvent.EmittedTriangles.Count(triangle => triangle.IsDegenerate);
        }

        var memo = new HashSet<string>(StringComparer.Ordinal);
        var searchBudget = 1000000;
        bool[]? solvedUsedTriangles = null;
        var solved = TrySolveTracePatternedStrip(
            triangles,
            tokenPattern,
            expectedDegenerateCounts,
            traceSegment.DegenerateTriangleCount,
            templateOnlyTriangleCount,
            expectedUsedTriangleCount,
            sourceVertices,
            tokenIndex: 0,
            strip: [],
            usedTriangles: new bool[triangles.Count],
            degenerateTriangleCount: 0,
            templateOnlyTriangleCount: 0,
            strictTrace,
            tokens,
            memo,
            ref solvedUsedTriangles,
            ref searchBudget);
        usedTriangleMask = solvedUsedTriangles ?? [];
        return solved;
    }

    private static bool TrySolveTracePatternedStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<TopologyTraceToken> tokenPattern,
        IReadOnlyList<int> expectedDegenerateCounts,
        int expectedTotalDegenerateCount,
        int expectedTemplateOnlyTriangleCount,
        int expectedUsedTriangleCount,
        IReadOnlyList<uint> sourceVertices,
        int tokenIndex,
        List<uint> strip,
        bool[] usedTriangles,
        int degenerateTriangleCount,
        int templateOnlyTriangleCount,
        bool strictTrace,
        List<byte> tokens,
        HashSet<string> memo,
        ref bool[]? solvedUsedTriangles,
        ref int searchBudget)
    {
        if (searchBudget-- <= 0)
        {
            return false;
        }

        if (tokenIndex == tokenPattern.Count)
        {
            if (usedTriangles.Count(used => used) == expectedUsedTriangleCount
                && degenerateTriangleCount == expectedTotalDegenerateCount
                && templateOnlyTriangleCount == expectedTemplateOnlyTriangleCount)
            {
                solvedUsedTriangles = (bool[])usedTriangles.Clone();
                return true;
            }

            return false;
        }

        var lastA = strip.Count >= 2 ? strip[^2] : 127u;
        var lastB = strip.Count >= 1 ? strip[^1] : 127u;
        var usedTriangleKey = BuildUsedTriangleKey(usedTriangles);
        var memoKey = $"{tokenIndex}|{lastA}|{lastB}|{usedTriangleKey}|{degenerateTriangleCount}|{templateOnlyTriangleCount}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var token = tokenPattern[tokenIndex];
        var isNegative = token.IsNonPositive;
        var nextIsRestart = isNegative
            && tokenIndex + 1 < tokenPattern.Count
            && tokenPattern[tokenIndex + 1].IsNonPositive;
        var candidates = token.IsZeroMarker
            ? new[] { (uint)token.VertexIndex }
            : GetTracePatternCandidates(
                triangles,
                FirstUnusedTriangleIndex(usedTriangles),
                sourceVertices,
                token.VertexIndex is >= 0 and <= 126 ? (uint)token.VertexIndex : null);
        foreach (var candidate in candidates)
        {
            var nextStrip = new List<uint>(strip);
            var nextUsedTriangles = (bool[])usedTriangles.Clone();
            var tokenDegenerateCount = 0;
            var nextDegenerateTriangleCount = degenerateTriangleCount;
            var nextTemplateOnlyTriangleCount = templateOnlyTriangleCount;

            if (isNegative && !nextIsRestart)
            {
                if (nextStrip.Count == 0)
                {
                    continue;
                }

                nextStrip.Add(nextStrip[^1]);
                if (!TryValidateTraceTriangle(
                    nextStrip,
                    triangles,
                    nextUsedTriangles,
                    ref tokenDegenerateCount,
                    ref nextDegenerateTriangleCount,
                    ref nextTemplateOnlyTriangleCount,
                    expectedTemplateOnlyTriangleCount))
                {
                    continue;
                }
            }

            nextStrip.Add(candidate);
            if (!TryValidateTraceTriangle(
                nextStrip,
                triangles,
                nextUsedTriangles,
                ref tokenDegenerateCount,
                ref nextDegenerateTriangleCount,
                ref nextTemplateOnlyTriangleCount,
                expectedTemplateOnlyTriangleCount))
            {
                continue;
            }

            if (tokenDegenerateCount != expectedDegenerateCounts[tokenIndex])
            {
                continue;
            }

            tokens.Add(token.IsZeroMarker ? (byte)0 : Ps2VifTopology.EncodeIndexToken(candidate, isNegative));
            if (TrySolveTracePatternedStrip(
                triangles,
                tokenPattern,
                expectedDegenerateCounts,
                expectedTotalDegenerateCount,
                expectedTemplateOnlyTriangleCount,
                expectedUsedTriangleCount,
                sourceVertices,
                tokenIndex + 1,
                nextStrip,
                nextUsedTriangles,
                nextDegenerateTriangleCount,
                nextTemplateOnlyTriangleCount,
                strictTrace,
                tokens,
                memo,
                ref solvedUsedTriangles,
                ref searchBudget))
            {
                return true;
            }

            tokens.RemoveAt(tokens.Count - 1);
        }

        return false;
    }

    private static int FirstUnusedTriangleIndex(IReadOnlyList<bool> usedTriangles)
    {
        for (var i = 0; i < usedTriangles.Count; i++)
        {
            if (!usedTriangles[i])
            {
                return i;
            }
        }

        return usedTriangles.Count;
    }

    private static string BuildUsedTriangleKey(IReadOnlyList<bool> usedTriangles)
    {
        if (usedTriangles.Count == 0)
        {
            return string.Empty;
        }

        var chars = new char[(usedTriangles.Count + 3) / 4];
        for (var i = 0; i < usedTriangles.Count; i++)
        {
            if (!usedTriangles[i])
            {
                continue;
            }

            chars[i / 4] = (char)(chars[i / 4] | (char)(1 << (i % 4)));
        }

        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)('A' + chars[i]);
        }

        return new string(chars);
    }

    private static IEnumerable<uint> GetTracePatternCandidates(
        IReadOnlyList<TopologyTriangle> triangles,
        int triangleIndex,
        IReadOnlyList<uint> sourceVertices,
        uint? preferredVertex)
    {
        var yielded = new HashSet<uint>();
        if (preferredVertex is { } preferred && yielded.Add(preferred))
        {
            yield return preferred;
        }

        if (triangleIndex < triangles.Count)
        {
            var triangle = triangles[triangleIndex];
            if (yielded.Add(triangle.A))
            {
                yield return triangle.A;
            }

            if (yielded.Add(triangle.B))
            {
                yield return triangle.B;
            }

            if (yielded.Add(triangle.C))
            {
                yield return triangle.C;
            }
        }

        if (triangleIndex + 1 < triangles.Count)
        {
            var triangle = triangles[triangleIndex + 1];
            if (yielded.Add(triangle.A))
            {
                yield return triangle.A;
            }

            if (yielded.Add(triangle.B))
            {
                yield return triangle.B;
            }

            if (yielded.Add(triangle.C))
            {
                yield return triangle.C;
            }
        }

        foreach (var vertex in sourceVertices)
        {
            if (yielded.Add(vertex))
            {
                yield return vertex;
            }
        }
    }

    private static bool TryValidateTraceTriangle(
        IReadOnlyList<uint> strip,
        IReadOnlyList<TopologyTriangle> triangles,
        bool[] usedTriangles,
        ref int tokenDegenerateCount,
        ref int totalDegenerateCount,
        ref int templateOnlyTriangleCount,
        int expectedTemplateOnlyTriangleCount)
    {
        var k = strip.Count - 1;
        if (k < 2)
        {
            return true;
        }

        var a = strip[k - 2];
        var b = strip[k - 1];
        var c = strip[k];
        var flip = ((k - 2) & 1) == 1;
        var i0 = a;
        var i1 = flip ? c : b;
        var i2 = flip ? b : c;
        if (i0 == i1 || i1 == i2 || i0 == i2)
        {
            tokenDegenerateCount++;
            totalDegenerateCount++;
            return true;
        }

        var matchingTriangleIndex = FindUnusedTriangleIndex(triangles, usedTriangles, i0, i1, i2);
        if (matchingTriangleIndex < 0)
        {
            if (templateOnlyTriangleCount >= expectedTemplateOnlyTriangleCount)
            {
                return false;
            }

            templateOnlyTriangleCount++;
            return true;
        }

        usedTriangles[matchingTriangleIndex] = true;
        return true;
    }

    private static int FindUnusedTriangleIndex(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> usedTriangles,
        uint a,
        uint b,
        uint c)
    {
        for (var i = 0; i < triangles.Count; i++)
        {
            if (usedTriangles[i])
            {
                continue;
            }

            var triangle = triangles[i];
            if (triangle.A == a && triangle.B == b && triangle.C == c)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<List<bool>> BuildTemplateTopologyTokenPatterns(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var effectiveTokenCount = tokens.Count;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.Equals(tokens[i].Kind, "zero", StringComparison.Ordinal))
            {
                continue;
            }

            effectiveTokenCount = Math.Max(0, i - 3);
            break;
        }

        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < effectiveTokenCount; i++)
        {
            var isNegative = tokens[i].Negative;
            var nextIsNonPositive = i + 1 < effectiveTokenCount && IsNonPositiveTopologyToken(tokens[i + 1]);
            if (IsNonPositiveTopologyToken(tokens[i]) && nextIsNonPositive)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(isNegative);
        }

        return patterns.Where(pattern => pattern.Count > 0).ToList();
    }

    private static List<List<TopologyTraceToken>> BuildResolvedTopologyTokenPatterns(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = BuildResolvedTopologyTraceTokens(indexPayload, texturePayload, positionCount);
        var patterns = new List<List<TopologyTraceToken>>();
        List<TopologyTraceToken>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var nextIsNonPositive = i + 1 < tokens.Count && tokens[i + 1].IsNonPositive;
            if (tokens[i].IsNonPositive && nextIsNonPositive)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(tokens[i]);
        }

        return patterns.Where(pattern => pattern.Count > 0).ToList();
    }

    private static List<TopologyTraceToken> BuildResolvedTopologyTraceTokens(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = new List<TopologyTraceToken>();
        if (indexPayload.Length < 8 || positionCount < 1)
        {
            return tokens;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var raw = unchecked((sbyte)indexPayload[j]);
            var idx = raw;
            var isZeroMarker = false;
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    if (tokens.Count >= 3)
                    {
                        tokens.RemoveRange(tokens.Count - 3, 3);
                    }

                    break;
                }

                idx = (sbyte)(secret - 0x80);
                isZeroMarker = true;
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            var vertexIndex = (idx & 0x7F) - 1;
            if (vertexIndex < 0 || vertexIndex >= positionCount)
            {
                break;
            }

            tokens.Add(new TopologyTraceToken(idx <= 0, vertexIndex, isZeroMarker));
        }

        return tokens;
    }

    private static bool TryBuildPatternedStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> tokenPattern,
        IReadOnlySet<string> allowedTriangleKeys,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return tokenPattern.Count == 0;
        }

        var sourceVertices = triangles
            .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C })
            .Distinct()
            .ToArray();
        var memo = new HashSet<string>(StringComparer.Ordinal);
        var searchBudget = 250000;
        return TrySolvePatternedStrip(
            triangles,
            tokenPattern,
            sourceVertices,
            allowedTriangleKeys,
            tokenIndex: 0,
            strip: [],
            triangleIndex: 0,
            tokens,
            memo,
            ref searchBudget);
    }

    private static bool TrySolvePatternedStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> tokenPattern,
        IReadOnlyList<uint> sourceVertices,
        IReadOnlySet<string> allowedTriangleKeys,
        int tokenIndex,
        List<uint> strip,
        int triangleIndex,
        List<byte> tokens,
        HashSet<string> memo,
        ref int searchBudget)
    {
        if (searchBudget-- <= 0)
        {
            return false;
        }

        if (tokenIndex == tokenPattern.Count)
        {
            return triangleIndex == triangles.Count;
        }

        var lastA = strip.Count >= 2 ? strip[^2] : 127u;
        var lastB = strip.Count >= 1 ? strip[^1] : 127u;
        var memoKey = $"{tokenIndex}|{strip.Count}|{lastA}|{lastB}|{triangleIndex}";
        if (!memo.Add(memoKey))
        {
            return false;
        }

        var isNegative = tokenPattern[tokenIndex];
        var nextIsRestart = isNegative
            && tokenIndex + 1 < tokenPattern.Count
            && tokenPattern[tokenIndex + 1];
        foreach (var candidate in GetPrioritizedPatternCandidates(triangles, triangleIndex, sourceVertices))
        {
            var nextStrip = new List<uint>(strip);
            var nextTriangleIndex = triangleIndex;
            if (isNegative && !nextIsRestart)
            {
                if (nextStrip.Count == 0)
                {
                    continue;
                }

                nextStrip.Add(nextStrip[^1]);
                if (!TryValidateNewStripTriangle(nextStrip, triangles, allowedTriangleKeys, ref nextTriangleIndex))
                {
                    continue;
                }
            }

            nextStrip.Add(candidate);
            if (!TryValidateNewStripTriangle(nextStrip, triangles, allowedTriangleKeys, ref nextTriangleIndex))
            {
                continue;
            }

            tokens.Add(Ps2VifTopology.EncodeIndexToken(candidate, isNegative));
            if (TrySolvePatternedStrip(
                triangles,
                tokenPattern,
                sourceVertices,
                allowedTriangleKeys,
                tokenIndex + 1,
                nextStrip,
                nextTriangleIndex,
                tokens,
                memo,
                ref searchBudget))
            {
                return true;
            }

            tokens.RemoveAt(tokens.Count - 1);
        }

        return false;
    }

    private static IEnumerable<uint> GetPrioritizedPatternCandidates(
        IReadOnlyList<TopologyTriangle> triangles,
        int triangleIndex,
        IReadOnlyList<uint> sourceVertices)
    {
        var yielded = new HashSet<uint>();
        if (triangleIndex < triangles.Count)
        {
            var triangle = triangles[triangleIndex];
            if (yielded.Add(triangle.A))
            {
                yield return triangle.A;
            }

            if (yielded.Add(triangle.B))
            {
                yield return triangle.B;
            }

            if (yielded.Add(triangle.C))
            {
                yield return triangle.C;
            }
        }

        foreach (var vertex in sourceVertices)
        {
            if (yielded.Add(vertex))
            {
                yield return vertex;
            }
        }
    }

    private static bool TryValidateNewStripTriangle(
        IReadOnlyList<uint> strip,
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlySet<string> allowedTriangleKeys,
        ref int triangleIndex)
    {
        var k = strip.Count - 1;
        if (k < 2)
        {
            return true;
        }

        var a = strip[k - 2];
        var b = strip[k - 1];
        var c = strip[k];
        var flip = ((k - 2) & 1) == 1;
        var i0 = a;
        var i1 = flip ? c : b;
        var i2 = flip ? b : c;
        if (i0 == i1 || i1 == i2 || i0 == i2)
        {
            return true;
        }

        if (triangleIndex >= triangles.Count)
        {
            return allowedTriangleKeys.Contains(BuildIndexTriangleKey(i0, i1, i2));
        }

        var expected = triangles[triangleIndex];
        if (expected.A == i0 && expected.B == i1 && expected.C == i2)
        {
            triangleIndex++;
            return true;
        }

        return allowedTriangleKeys.Contains(BuildIndexTriangleKey(i0, i1, i2));
    }

    private static bool TryBuildSequentialStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return true;
        }

        for (var seedRotation = 0; seedRotation < 3; seedRotation++)
        {
            var seed = Ps2VifTopology.RotateTriangle(triangles[0], seedRotation);
            var currentStrip = new List<uint> { seed.A, seed.A, seed.C, seed.B };
            var candidateTokens = new List<byte>
            {
                Ps2VifTopology.EncodeIndexToken(seed.A, negative: true),
                Ps2VifTopology.EncodeIndexToken(seed.C, negative: true),
                Ps2VifTopology.EncodeIndexToken(seed.B, negative: false)
            };

            var matched = true;
            for (var i = 1; i < triangles.Count; i++)
            {
                if (!Ps2VifTopology.TryFindAppendableTriangleRotation(currentStrip, triangles[i], out var rotated, out var nextVertex))
                {
                    matched = false;
                    break;
                }

                candidateTokens.Add(Ps2VifTopology.EncodeIndexToken(nextVertex, negative: false));
                currentStrip.Add(nextVertex);
            }

            if (!matched)
            {
                continue;
            }

            tokens = candidateTokens;
            return true;
        }

        return TryBuildSingleGreedyStripTokens(triangles, out tokens);
    }

    private static bool TryBuildSingleGreedyStripTokens(
        IReadOnlyList<TopologyTriangle> triangles,
        out List<byte> tokens)
    {
        tokens = [];
        if (triangles.Count == 0)
        {
            return true;
        }

        var available = Enumerable.Repeat(true, triangles.Count).ToArray();
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            for (var rotation = 0; rotation < 3; rotation++)
            {
                var candidate = Ps2VifTopology.BuildGreedyRestartStrip(triangles, available, triangleIndex, rotation);
                if (candidate.TriangleIndices.Count != triangles.Count)
                {
                    continue;
                }

                tokens = candidate.Tokens;
                return true;
            }
        }

        return false;
    }

    private static VifBuildResult BuildMetadataVifData(MobyMeshTableEntry templateEntry, ImportedMesh mesh)
    {
        var topology = mesh.Metadata?.Topology;
        if (topology is null)
        {
            return BuildTemplateVifPassthrough(templateEntry);
        }

        var alignedPayloadBytes = BuildMetadataAlignedTopologyPayload(topology);
        if (alignedPayloadBytes.Length == 0)
        {
            return BuildTemplateVifPassthrough(templateEntry);
        }

        using var stream = new MemoryStream(
            topology.BeforePacketBytes.Length
            + 4
            + alignedPayloadBytes.Length
            + topology.AfterPacketBytes.Length);
        stream.Write(topology.BeforePacketBytes);
        Ps2VifPacket.WriteHeader(
            stream,
            checked((ushort)topology.Immediate),
            checked((byte)(alignedPayloadBytes.Length / 4)),
            checked((byte)topology.CommandByte));
        stream.Write(alignedPayloadBytes);
        stream.Write(topology.AfterPacketBytes);

        var combinedVifData = stream.ToArray();
        SplitCombinedVifData(combinedVifData, topology.VifDataSplitOffset, out var vifData, out var vifTextureData);

        return new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: alignedPayloadBytes.Length,
            NewTopologyPayloadBytes: alignedPayloadBytes.Length,
            ReusedTemplateTopology: true,
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: true,
            GeneratedTopologyFromGltf: false,
            GeneratedTopologyTokenCount: 0,
            GeneratedTopologySourceTriangleCount: 0,
            GeneratedTopologyPayloadFitsMetadata: true,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null);
    }

    private static VifBuildResult BuildMetadataShapeTopologyVifData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        IReadOnlyList<uint> remappedTriangleIndices,
        bool allowExactSourceShapeMismatch = false,
        bool preferExactSourceTopology = false)
    {
        var topology = mesh.Metadata?.Topology ?? TryBuildTemplateTopologyMetadata(templateEntry);
        if (topology is null)
        {
            return BuildVifData(templateEntry.VifData, templateEntry.VifTextureData, remappedTriangleIndices);
        }

        var templateTraceForSourceFilter = TraceTopology(BuildResolvedTopologyTraceTokens(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount));
        var topologySourceTriangleIndices = TryBuildTemplateCompatibleTopologySource(
            templateTraceForSourceFilter,
            remappedTriangleIndices,
            out var templateCompatibleTriangleIndices)
            ? templateCompatibleTriangleIndices
            : remappedTriangleIndices;
        var topologyTokens = !preferExactSourceTopology
            && TryBuildTemplateSegmentedTopologyTokens(
                topology.AlignedPayloadBytes,
                topology.PayloadTokens,
                templateEntry.VifTextureData,
                templateEntry.VertexCount,
                topologySourceTriangleIndices,
                out var templateSegmentedTokens)
                    ? templateSegmentedTokens
                    : Ps2VifTopology.BuildRestartStripTokens(topologySourceTriangleIndices);
        var prefixBytes = topology.PayloadPrefixBytes.Count >= 4
            ? topology.PayloadPrefixBytes.Take(4).Select(value => checked((byte)value)).ToArray()
            : [0, 0, 0x80, 0];
        var adjustedVifTextureData = templateEntry.VifTextureData is null
            ? null
            : (byte[])templateEntry.VifTextureData.Clone();
        ApplyTemplateZeroMarkers(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount,
            topologyTokens,
            prefixBytes,
            adjustedVifTextureData,
            rewriteSecrets: true);
        var payload = Ps2VifTopology.BuildPayload(topologyTokens, prefixBytes);
        var unpaddedPayloadLength = 4 + topologyTokens.Count;
        for (var i = unpaddedPayloadLength; i < payload.Length && i < topology.AlignedPayloadBytes.Length; i++)
        {
            payload[i] = topology.AlignedPayloadBytes[i];
        }
        var payloadFitsMetadata = payload.Length <= topology.AlignedPayloadBytes.Length;
        var analysisPayload = payloadFitsMetadata
            ? preferExactSourceTopology
                ? Enumerable.Repeat((byte)0x80, topology.AlignedPayloadBytes.Length).ToArray()
                : (byte[])topology.AlignedPayloadBytes.Clone()
            : payload;
        if (payloadFitsMetadata)
        {
            payload.CopyTo(analysisPayload, 0);
            if (payload.Length < topology.AlignedPayloadBytes.Length)
            {
                var templateTailPayload = (byte[])topology.AlignedPayloadBytes.Clone();
                payload.CopyTo(templateTailPayload, 0);
                if (TopologyZeroMarkerPositionsMatch(
                        topology.AlignedPayloadBytes,
                        templateTailPayload,
                        templateEntry.VifTextureData,
                        adjustedVifTextureData,
                        templateEntry.VertexCount)
                    && TopologyShapesMatch(
                        SummarizeResolvedTopologyShape(BuildResolvedTopologyTraceTokens(topology.AlignedPayloadBytes, templateEntry.VifTextureData, templateEntry.VertexCount)),
                        SummarizeResolvedTopologyShape(BuildResolvedTopologyTraceTokens(templateTailPayload, adjustedVifTextureData, templateEntry.VertexCount))))
                {
                    analysisPayload = templateTailPayload;
                }
            }
        }

        var preservesTemplateControlMarkers = TopologyZeroMarkerPositionsMatch(
            topology.AlignedPayloadBytes,
            analysisPayload,
            templateEntry.VifTextureData,
            adjustedVifTextureData,
            templateEntry.VertexCount);
        var templateResolvedTokens = BuildResolvedTopologyTraceTokens(
            topology.AlignedPayloadBytes,
            templateEntry.VifTextureData,
            templateEntry.VertexCount);
        var generatedResolvedTokens = BuildResolvedTopologyTraceTokens(
            analysisPayload,
            adjustedVifTextureData,
            templateEntry.VertexCount);
        var templateRestartCount = CountResolvedTopologyRestarts(templateResolvedTokens);
        var generatedRestartCount = CountResolvedTopologyRestarts(generatedResolvedTokens);
        var templateNegativeTokenCount = templateResolvedTokens.Count(token => token.IsNonPositive);
        var generatedNegativeTokenCount = generatedResolvedTokens.Count(token => token.IsNonPositive);
        var templateTopologyShape = SummarizeResolvedTopologyShape(templateResolvedTokens);
        var generatedTopologyShape = SummarizeResolvedTopologyShape(generatedResolvedTokens);
        var templateTopologyTrace = TraceTopology(templateResolvedTokens);
        var generatedTopologyTrace = TraceTopology(generatedResolvedTokens);
        var templateZeroMarkers = SummarizeTopologyZeroMarkers(templateResolvedTokens);
        var generatedZeroMarkers = SummarizeTopologyZeroMarkers(generatedResolvedTokens);
        var matchesTemplateControlShape = TopologyShapesMatch(templateTopologyShape, generatedTopologyShape);
        var topologyPayloadDiff = BuildTopologyPayloadDiff(topology.AlignedPayloadBytes, analysisPayload);
        var decodedTriangleIndices = new List<uint>();
        var payloadDecoded = TryDecodeTopologyPayload(
            analysisPayload,
            adjustedVifTextureData,
            templateEntry.VertexCount,
            out decodedTriangleIndices);
        var payloadMatchesSourceTriangles = payloadDecoded
            && TriangleListsMatch(topologySourceTriangleIndices, decodedTriangleIndices);
        var payloadContainsSourceTriangles = payloadDecoded
            && TriangleSetContainsAll(decodedTriangleIndices, topologySourceTriangleIndices);
        var topologySourceDiff = BuildTopologySourceDiff(
            templateTopologyTrace,
            remappedTriangleIndices,
            payloadDecoded ? decodedTriangleIndices : null);
        if (!preservesTemplateControlMarkers)
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = payloadFitsMetadata,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = false,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var allowRelaxedShapeMismatch = allowExactSourceShapeMismatch
            && payloadFitsMetadata
            && preservesTemplateControlMarkers
            && payloadMatchesSourceTriangles;
        if (!matchesTemplateControlShape && !allowRelaxedShapeMismatch)
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = payloadFitsMetadata,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = false,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var smallExpandedPayload = payload.Length <= topology.AlignedPayloadBytes.Length + 0x10;
        if (!payloadFitsMetadata
            && !(smallExpandedPayload && preservesTemplateControlMarkers && payloadMatchesSourceTriangles))
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = false,
                GeneratedTopologyMatchesSourceTriangles = payloadMatchesSourceTriangles,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var preservedZeroMarkerControlFlow = payloadFitsMetadata
            && preservesTemplateControlMarkers
            && matchesTemplateControlShape
            && templateZeroMarkers.Count > 0;
        if (!payloadMatchesSourceTriangles
            && !(preservedZeroMarkerControlFlow
                || (preservesTemplateControlMarkers && matchesTemplateControlShape && payloadContainsSourceTriangles)
                || (allowExactSourceShapeMismatch && preservesTemplateControlMarkers && payloadMatchesSourceTriangles)))
        {
            return BuildMetadataVifData(templateEntry, mesh) with
            {
                GeneratedTopologyTokenCount = topologyTokens.Count,
                GeneratedTopologySourceTriangleCount = topologySourceTriangleIndices.Count / 3,
                GeneratedTopologyPayloadFitsMetadata = true,
                GeneratedTopologyMatchesSourceTriangles = false,
                GeneratedTopologyPreservesTemplateControlMarkers = preservesTemplateControlMarkers,
                GeneratedTopologyMatchesTemplateControlShape = matchesTemplateControlShape,
                TemplateTopologyRestartCount = templateRestartCount,
                GeneratedTopologyRestartCount = generatedRestartCount,
                TemplateTopologyNegativeTokenCount = templateNegativeTokenCount,
                GeneratedTopologyNegativeTokenCount = generatedNegativeTokenCount,
                TemplateTopologyShape = templateTopologyShape,
                GeneratedTopologyShape = generatedTopologyShape,
                TemplateTopologyTrace = templateTopologyTrace,
                GeneratedTopologyTrace = generatedTopologyTrace,
                TemplateTopologyZeroMarkers = templateZeroMarkers,
                GeneratedTopologyZeroMarkers = generatedZeroMarkers,
                TopologySourceDiff = topologySourceDiff,
                TopologyPayloadDiff = topologyPayloadDiff
            };
        }

        var alignedPayloadBytes = analysisPayload;

        using var stream = new MemoryStream(
            topology.BeforePacketBytes.Length
            + 4
            + alignedPayloadBytes.Length
            + topology.AfterPacketBytes.Length);
        stream.Write(topology.BeforePacketBytes);
        Ps2VifPacket.WriteHeader(
            stream,
            checked((ushort)topology.Immediate),
            checked((byte)(alignedPayloadBytes.Length / 4)),
            checked((byte)topology.CommandByte));
        stream.Write(alignedPayloadBytes);
        stream.Write(topology.AfterPacketBytes);

        var combinedVifData = stream.ToArray();
        var splitOffset = topology.VifDataSplitOffset;
        if (!payloadFitsMetadata && topology.Offset < topology.VifDataSplitOffset)
        {
            splitOffset += payload.Length - topology.AlignedPayloadBytes.Length;
        }

        SplitCombinedVifData(combinedVifData, splitOffset, out var vifData, out var vifTextureData);
        if (vifData.Length % 0x10 != 0)
        {
            Array.Resize(ref vifData, Align(vifData.Length, 0x10));
        }

        if (adjustedVifTextureData is not null)
        {
            vifTextureData = adjustedVifTextureData;
        }

        return new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: topologyTokens.Count - topologySourceTriangleIndices.Count,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: !payloadFitsMetadata,
            OriginalTopologyPayloadBytes: topology.AlignedPayloadBytes.Length,
            NewTopologyPayloadBytes: payload.Length,
            ReusedTemplateTopology: false,
            RemappedTemplateTopology: false,
            UsedMetadataTopologyLayout: true,
            GeneratedTopologyFromGltf: true,
            GeneratedTopologyTokenCount: topologyTokens.Count,
            GeneratedTopologySourceTriangleCount: topologySourceTriangleIndices.Count / 3,
            GeneratedTopologyPayloadFitsMetadata: payloadFitsMetadata,
            GeneratedTopologyMatchesSourceTriangles: payloadMatchesSourceTriangles,
            GeneratedTopologyPreservesTemplateControlMarkers: preservesTemplateControlMarkers,
            GeneratedTopologyMatchesTemplateControlShape: matchesTemplateControlShape,
            TemplateTopologyRestartCount: templateRestartCount,
            GeneratedTopologyRestartCount: generatedRestartCount,
            TemplateTopologyNegativeTokenCount: templateNegativeTokenCount,
            GeneratedTopologyNegativeTokenCount: generatedNegativeTokenCount,
            TemplateTopologyShape: templateTopologyShape,
            GeneratedTopologyShape: generatedTopologyShape,
            TemplateTopologyTrace: templateTopologyTrace,
            GeneratedTopologyTrace: generatedTopologyTrace,
            TemplateTopologyZeroMarkers: templateZeroMarkers,
            GeneratedTopologyZeroMarkers: generatedZeroMarkers,
            TopologySourceDiff: topologySourceDiff,
            TopologyPayloadDiff: topologyPayloadDiff);
    }

    private static ImportedTopologyMetadata? TryBuildTemplateTopologyMetadata(MobyMeshTableEntry templateEntry)
    {
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.PayloadLength <= 0 || packet.Offset + packet.TotalLength > combinedVifData.Length)
        {
            return null;
        }

        var payloadOffset = packet.Offset + 4;
        var payloadBytes = combinedVifData.AsSpan(payloadOffset, packet.PayloadLength).ToArray();
        var prefixBytes = payloadBytes.Take(4).Select(value => (int)value).ToList();
        var payloadTokens = new List<ImportedTopologyPayloadToken>();
        for (var i = 4; i < payloadBytes.Length; i++)
        {
            var value = payloadBytes[i];
            if (value == 0)
            {
                payloadTokens.Add(new ImportedTopologyPayloadToken("zero", Negative: false, VertexIndex: -1));
                continue;
            }

            var negative = (value & 0x80) != 0;
            var vertexIndex = (value & 0x7F) - 1;
            payloadTokens.Add(new ImportedTopologyPayloadToken(
                negative ? "negative_index" : "index",
                negative,
                vertexIndex));
        }

        return new ImportedTopologyMetadata(
            packet.Offset,
            packet.Immediate,
            packet.CommandByte,
            templateEntry.VifData.Length,
            Convert.ToBase64String(payloadBytes),
            payloadBytes,
            PayloadPaddingBytes: [],
            payloadBytes.Select(value => (int)value).ToList(),
            prefixBytes,
            payloadTokens,
            combinedVifData.AsSpan(0, packet.Offset).ToArray(),
            combinedVifData.AsSpan(packet.Offset + packet.TotalLength).ToArray());
    }

    private static byte[] BuildMetadataAlignedTopologyPayload(ImportedTopologyMetadata topology)
    {
        var payloadBytes = BuildMetadataTopologyPayload(topology);
        if (payloadBytes.Length == 0 && topology.PayloadBytes.Count == 0)
        {
            return topology.AlignedPayloadBytes;
        }

        var payloadLength = payloadBytes.Length + topology.PayloadPaddingBytes.Length;
        if (payloadLength == 0 || payloadLength % 4 != 0)
        {
            return [];
        }

        var result = new byte[payloadLength];
        Buffer.BlockCopy(payloadBytes, 0, result, 0, payloadBytes.Length);
        topology.PayloadPaddingBytes.CopyTo(result.AsSpan(payloadBytes.Length));
        return result;
    }

    private static byte[] BuildMetadataTopologyPayload(ImportedTopologyMetadata topology)
    {
        if (topology.PayloadPrefixBytes.Count >= 4 && topology.PayloadTokens.Count > 0)
        {
            var result = new byte[4 + topology.PayloadTokens.Count];
            for (var i = 0; i < 4; i++)
            {
                result[i] = checked((byte)topology.PayloadPrefixBytes[i]);
            }

            for (var i = 0; i < topology.PayloadTokens.Count; i++)
            {
                var token = topology.PayloadTokens[i];
                result[4 + i] = token.Kind == "zero"
                    ? (byte)0
                    : checked((byte)((token.Negative ? 0x80 : 0x00) | ((token.VertexIndex + 1) & 0x7F)));
            }

            return result;
        }

        if (topology.PayloadBytes.Count == 0)
        {
            return [];
        }

        var payload = new byte[topology.PayloadBytes.Count];
        for (var i = 0; i < topology.PayloadBytes.Count; i++)
        {
            payload[i] = checked((byte)topology.PayloadBytes[i]);
        }

        return payload;
    }

    private static void ApplyTemplateZeroMarkers(
        ReadOnlySpan<byte> templatePayload,
        byte[]? templateTexturePayload,
        int positionCount,
        List<byte> topologyTokens,
        byte[] prefixBytes,
        byte[]? adjustedTexturePayload,
        bool rewriteSecrets)
    {
        var templateTokens = BuildResolvedTopologyTraceTokens(templatePayload, templateTexturePayload, positionCount);
        var zeroMarkerOrdinal = 0;
        for (var tokenIndex = 0; tokenIndex < templateTokens.Count && tokenIndex < topologyTokens.Count; tokenIndex++)
        {
            if (!templateTokens[tokenIndex].IsZeroMarker)
            {
                continue;
            }

            var rawToken = unchecked((sbyte)topologyTokens[tokenIndex]);
            if (rawToken == 0)
            {
                zeroMarkerOrdinal++;
                continue;
            }

            topologyTokens[tokenIndex] = 0;
            if (!rewriteSecrets)
            {
                zeroMarkerOrdinal++;
                continue;
            }

            var secret = unchecked((byte)(rawToken + 0x80));
            if (!TryWriteZeroMarkerSecret(zeroMarkerOrdinal, secret, prefixBytes, adjustedTexturePayload))
            {
                zeroMarkerOrdinal++;
                continue;
            }

            zeroMarkerOrdinal++;
        }
    }

    private static bool TryWriteZeroMarkerSecret(
        int zeroMarkerOrdinal,
        byte secret,
        byte[] prefixBytes,
        byte[]? adjustedTexturePayload)
    {
        if (zeroMarkerOrdinal == 0)
        {
            if (prefixBytes.Length < 3)
            {
                return false;
            }

            prefixBytes[2] = secret;
            return true;
        }

        var textureSecretOffset = (zeroMarkerOrdinal - 1) * 0x10 + 0x0C;
        if (adjustedTexturePayload is null || textureSecretOffset >= adjustedTexturePayload.Length)
        {
            return false;
        }

        adjustedTexturePayload[textureSecretOffset] = secret;
        return true;
    }

    private static bool TryBuildTemplateTopologyReplacement(
        MobyMeshTableEntry templateEntry,
        IReadOnlyList<int> indexByOriginalIndex,
        IReadOnlyList<uint> expectedRemappedIndices,
        out VifBuildResult result)
    {
        result = default!;
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.Offset + 4 > combinedVifData.Length)
        {
            return false;
        }

        if (indexByOriginalIndex.Count != templateEntry.VertexCount)
        {
            return false;
        }

        var remappedCombined = (byte[])combinedVifData.Clone();
        var payloadOffset = packet.Offset + 4;
        var payloadLength = Math.Min(packet.PayloadLength, remappedCombined.Length - payloadOffset);
        if (payloadLength <= 0)
        {
            return false;
        }

        if (!TryRemapTopologyPayload(
            remappedCombined,
            payloadOffset,
            payloadLength,
            templateEntry.VifData.Length,
            templateEntry.VifTextureData?.Length ?? 0,
            indexByOriginalIndex))
        {
            return false;
        }

        SplitCombinedVifData(remappedCombined, templateEntry.VifData.Length, out var vifData, out var vifTextureData);

        result = new VifBuildResult(
            vifData,
            vifTextureData,
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: packet.PayloadLength,
            NewTopologyPayloadBytes: packet.PayloadLength,
            ReusedTemplateTopology: indexByOriginalIndex.Select((value, index) => value == index).All(match => match),
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: false,
            GeneratedTopologyFromGltf: false,
            GeneratedTopologyTokenCount: 0,
            GeneratedTopologySourceTriangleCount: 0,
            GeneratedTopologyPayloadFitsMetadata: true,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null);
        return true;
    }

    private static VifBuildResult BuildTemplateVifPassthrough(MobyMeshTableEntry templateEntry)
    {
        var packet = TryFindTopologyPacket(Combine(templateEntry.VifData, templateEntry.VifTextureData));
        return new VifBuildResult(
            (byte[])templateEntry.VifData.Clone(),
            templateEntry.VifTextureData is null ? null : (byte[])templateEntry.VifTextureData.Clone(),
            ConnectorIndexCount: 0,
            PreservedTemplateLayout: true,
            ExpandedTopologyPacket: false,
            OriginalTopologyPayloadBytes: packet?.PayloadLength ?? 0,
            NewTopologyPayloadBytes: packet?.PayloadLength ?? 0,
            ReusedTemplateTopology: true,
            RemappedTemplateTopology: true,
            UsedMetadataTopologyLayout: false,
            GeneratedTopologyFromGltf: false,
            GeneratedTopologyTokenCount: 0,
            GeneratedTopologySourceTriangleCount: 0,
            GeneratedTopologyPayloadFitsMetadata: true,
            GeneratedTopologyMatchesSourceTriangles: true,
            GeneratedTopologyPreservesTemplateControlMarkers: true,
            GeneratedTopologyMatchesTemplateControlShape: true,
            TemplateTopologyRestartCount: 0,
            GeneratedTopologyRestartCount: 0,
            TemplateTopologyNegativeTokenCount: 0,
            GeneratedTopologyNegativeTokenCount: 0,
            TemplateTopologyShape: null,
            GeneratedTopologyShape: null);
    }

    private static bool TryBuildRemappedTemplateTopology(
        MobyMeshTableEntry templateEntry,
        IReadOnlyList<int> indexByOriginalIndex,
        out List<uint> remappedIndices)
    {
        remappedIndices = [];
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null || packet.Offset + 4 > combinedVifData.Length)
        {
            return false;
        }

        var payloadOffset = packet.Offset + 4;
        var payloadLength = Math.Min(packet.PayloadLength, combinedVifData.Length - payloadOffset);
        if (payloadLength <= 0)
        {
            return false;
        }

        if (!TryDecodeTopologyPayload(
            combinedVifData.AsSpan(payloadOffset, payloadLength),
            templateEntry.VifTextureData,
            templateEntry.VertexCount,
            out var templateIndices))
        {
            return false;
        }

        remappedIndices = new List<uint>(templateIndices.Count);
        foreach (var index in templateIndices)
        {
            if (index >= indexByOriginalIndex.Count)
            {
                return false;
            }

            remappedIndices.Add(checked((uint)indexByOriginalIndex[(int)index]));
        }

        return true;
    }

    private static bool TryRemapTopologyPayload(
        byte[] combinedVifData,
        int indexPayloadOffset,
        int indexPayloadLength,
        int vifDataLength,
        int vifTextureDataLength,
        IReadOnlyList<int> indexByOriginalIndex)
    {
        var indexPayload = combinedVifData.AsSpan(indexPayloadOffset, indexPayloadLength);
        if (indexPayload.Length >= 3 && !TryRemapTopologyIndexByte(ref indexPayload[2], indexByOriginalIndex))
        {
            return false;
        }

        for (var i = 4; i < indexPayload.Length; i++)
        {
            if (!TryRemapTopologyIndexByte(ref indexPayload[i], indexByOriginalIndex))
            {
                return false;
            }
        }

        for (var textureOffset = 0x0C; textureOffset < vifTextureDataLength; textureOffset += 0x10)
        {
            var combinedOffset = vifDataLength + textureOffset;
            if (combinedOffset >= indexPayloadOffset && combinedOffset < indexPayloadOffset + indexPayloadLength)
            {
                continue;
            }

            if (!TryRemapTopologyIndexByte(ref combinedVifData[combinedOffset], indexByOriginalIndex))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryRemapTopologyIndexByte(ref byte value, IReadOnlyList<int> indexByOriginalIndex)
    {
        if (value == 0)
        {
            return true;
        }

        var oldIndex = (value & 0x7F) - 1;
        if (oldIndex < 0)
        {
            return true;
        }

        if (oldIndex >= indexByOriginalIndex.Count)
        {
            return false;
        }

        var newIndex = indexByOriginalIndex[oldIndex];
        if (newIndex < 0 || newIndex > 126)
        {
            return false;
        }

        value = checked((byte)((value & 0x80) | (newIndex + 1)));
        return true;
    }

    private static bool TryDecodeTopologyPayload(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount,
        out List<uint> indices)
    {
        indices = [];
        if (indexPayload.Length < 8 || positionCount < 3)
        {
            return false;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        List<uint>? currentStrip = null;
        var strips = new List<List<uint>>();
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var idx = unchecked((sbyte)indexPayload[j]);
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    if (currentStrip is null || currentStrip.Count < 3)
                    {
                        break;
                    }

                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    currentStrip.RemoveAt(currentStrip.Count - 1);
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = [];
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Add(currentStrip[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = [];
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Count >= 3))
        {
            var flip = false;
            for (var k = 2; k < strip.Count; k++)
            {
                var a = strip[k - 2];
                var b = strip[k - 1];
                var c = strip[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;

                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    continue;
                }

                var key = BuildIndexTriangleKey(i0, i1, i2);
                if (!seenTriangles.Add(key))
                {
                    continue;
                }

                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
            }
        }

        return indices.Count >= 3;
    }

    private static bool TryDecodeTopologyPayloadStrips(
        ReadOnlySpan<byte> indexPayload,
        byte[]? texturePayload,
        int positionCount,
        out List<List<uint>> stripTriangles)
    {
        stripTriangles = [];
        if (indexPayload.Length < 8 || positionCount < 3)
        {
            return false;
        }

        var secretIndices = new List<sbyte> { unchecked((sbyte)indexPayload[2]) };
        var texturePrimitiveCount = 0;
        if (texturePayload is not null && texturePayload.Length >= 0x40)
        {
            texturePrimitiveCount = texturePayload.Length / 0x40;
            for (var i = 0; i < texturePrimitiveCount; i++)
            {
                var secretOffset = i * 0x10 + 0x0C;
                if (secretOffset >= texturePayload.Length)
                {
                    break;
                }

                secretIndices.Add(unchecked((sbyte)texturePayload[secretOffset]));
            }
        }

        var nextSecretIndex = 0;
        var adGifIndex = 0;
        List<uint>? currentStrip = null;
        var strips = new List<List<uint>>();
        for (var j = 4; j < indexPayload.Length; j++)
        {
            var idx = unchecked((sbyte)indexPayload[j]);
            if (idx == 0)
            {
                if (nextSecretIndex >= secretIndices.Count)
                {
                    break;
                }

                var secret = secretIndices[nextSecretIndex++];
                if (secret == 0)
                {
                    break;
                }

                idx = (sbyte)(secret - 0x80);
                if (texturePrimitiveCount > 0)
                {
                    if (adGifIndex >= texturePrimitiveCount)
                    {
                        break;
                    }

                    adGifIndex++;
                }
            }

            if (idx <= 0)
            {
                var nextIsRestart = j + 1 < indexPayload.Length && unchecked((sbyte)indexPayload[j + 1]) <= 0;
                if (nextIsRestart)
                {
                    currentStrip = [];
                    strips.Add(currentStrip);
                }
                else
                {
                    if (currentStrip is null || currentStrip.Count < 1)
                    {
                        break;
                    }

                    currentStrip.Add(currentStrip[^1]);
                }
            }

            if (currentStrip is null)
            {
                currentStrip = [];
                strips.Add(currentStrip);
            }

            var decoded = (idx & 0x7F) - 1;
            if (decoded < 0 || decoded >= positionCount)
            {
                break;
            }

            currentStrip.Add((uint)decoded);
        }

        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strip in strips.Where(strip => strip.Count >= 3))
        {
            var triangles = new List<uint>();
            var flip = false;
            for (var k = 2; k < strip.Count; k++)
            {
                var a = strip[k - 2];
                var b = strip[k - 1];
                var c = strip[k];
                var i0 = a;
                var i1 = flip ? c : b;
                var i2 = flip ? b : c;
                flip = !flip;

                if (i0 == i1 || i1 == i2 || i0 == i2)
                {
                    continue;
                }

                var key = BuildIndexTriangleKey(i0, i1, i2);
                if (!seenTriangles.Add(key))
                {
                    continue;
                }

                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);
            }

            if (triangles.Count > 0)
            {
                stripTriangles.Add(triangles);
            }
        }

        return stripTriangles.Count > 0;
    }

    private static string BuildIndexTriangleKey(uint a, uint b, uint c)
    {
        Span<uint> values = [a, b, c];
        values.Sort();
        return $"{values[0]}|{values[1]}|{values[2]}";
    }

    private static bool TriangleListsMatch(IReadOnlyList<uint> expected, IReadOnlyList<uint> actual)
    {
        if (expected.Count != actual.Count || expected.Count % 3 != 0)
        {
            return false;
        }

        var expectedCounts = CountTriangleSets(expected);
        var actualCounts = CountTriangleSets(actual);
        if (expectedCounts.Count != actualCounts.Count)
        {
            return false;
        }

        foreach (var (key, count) in expectedCounts)
        {
            if (!actualCounts.TryGetValue(key, out var actualCount) || actualCount != count)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TriangleSetContainsAll(IReadOnlyList<uint> actual, IReadOnlyList<uint> expected)
    {
        if (actual.Count % 3 != 0 || expected.Count % 3 != 0)
        {
            return false;
        }

        var actualCounts = CountTriangleSets(actual);
        var expectedCounts = CountTriangleSets(expected);
        foreach (var (key, count) in expectedCounts)
        {
            if (!actualCounts.TryGetValue(key, out var actualCount) || actualCount < count)
            {
                return false;
            }
        }

        return true;
    }

    private static TopologySourceDiff BuildTopologySourceDiff(
        TopologyTraceSummary? templateTrace,
        IReadOnlyList<uint> sourceIndices,
        IReadOnlyList<uint>? generatedIndices)
    {
        var templateTriangleKeys = templateTrace is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : CountTriangleSets(templateTrace.UniqueTriangleIndices);
        var sourceTriangleKeys = CountTriangleSets(sourceIndices);
        var generatedTriangleKeys = generatedIndices is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : CountTriangleSets(generatedIndices);

        return new TopologySourceDiff(
            TemplateUniqueTriangleCount: templateTriangleKeys.Values.Sum(),
            SourceTriangleCount: sourceIndices.Count / 3,
            GeneratedDecodedTriangleCount: generatedIndices?.Count / 3 ?? 0,
            TemplateOnlyTriangleCount: CountOnly(templateTriangleKeys, sourceTriangleKeys),
            SourceOnlyTriangleCount: CountOnly(sourceTriangleKeys, templateTriangleKeys),
            GeneratedOnlyTriangleCount: CountOnly(generatedTriangleKeys, sourceTriangleKeys),
            SourceMissingFromGeneratedTriangleCount: CountOnly(sourceTriangleKeys, generatedTriangleKeys),
            TemplateVertexRange: BuildTriangleVertexRange(templateTrace is null ? [] : templateTrace.UniqueTriangleIndices),
            SourceVertexRange: BuildTriangleVertexRange(sourceIndices),
            GeneratedVertexRange: generatedIndices is null ? null : BuildTriangleVertexRange(generatedIndices),
            TemplateOnlyTriangleSamples: BuildOnlySamples(templateTriangleKeys, sourceTriangleKeys),
            SourceOnlyTriangleSamples: BuildOnlySamples(sourceTriangleKeys, templateTriangleKeys),
            GeneratedOnlyTriangleSamples: BuildOnlySamples(generatedTriangleKeys, sourceTriangleKeys),
            SourceMissingFromGeneratedTriangleSamples: BuildOnlySamples(sourceTriangleKeys, generatedTriangleKeys));
    }

    private static TopologyPayloadDiff BuildTopologyPayloadDiff(
        IReadOnlyList<byte> templatePayload,
        IReadOnlyList<byte> generatedPayload,
        int maxSamples = 16)
    {
        var sharedLength = Math.Min(templatePayload.Count, generatedPayload.Count);
        var differingByteCount = Math.Abs(templatePayload.Count - generatedPayload.Count);
        var samples = new List<TopologyPayloadByteDiff>(maxSamples);
        for (var i = 0; i < sharedLength; i++)
        {
            if (templatePayload[i] == generatedPayload[i])
            {
                continue;
            }

            differingByteCount++;
            if (samples.Count < maxSamples)
            {
                samples.Add(new TopologyPayloadByteDiff(i, templatePayload[i], generatedPayload[i]));
            }
        }

        for (var i = sharedLength; i < templatePayload.Count && samples.Count < maxSamples; i++)
        {
            samples.Add(new TopologyPayloadByteDiff(i, templatePayload[i], null));
        }

        for (var i = sharedLength; i < generatedPayload.Count && samples.Count < maxSamples; i++)
        {
            samples.Add(new TopologyPayloadByteDiff(i, null, generatedPayload[i]));
        }

        return new TopologyPayloadDiff(
            TemplatePayloadBytes: templatePayload.Count,
            GeneratedPayloadBytes: generatedPayload.Count,
            DifferingByteCount: differingByteCount,
            FirstDiffs: samples.ToArray());
    }

    private static bool TryBuildTemplateCompatibleTopologySource(
        TopologyTraceSummary templateTrace,
        IReadOnlyList<uint> sourceIndices,
        out IReadOnlyList<uint> templateCompatibleIndices)
    {
        templateCompatibleIndices = [];
        if (templateTrace.UniqueTriangleIndices.Length == 0)
        {
            return false;
        }

        var templateCounts = CountTriangleSets(templateTrace.UniqueTriangleIndices);
        var sourceCounts = CountTriangleSets(sourceIndices);
        var templateVertexRange = BuildTriangleVertexRange(templateTrace.UniqueTriangleIndices);
        var sourceVertexRange = BuildTriangleVertexRange(sourceIndices);
        if (IsNearTemplateTriangleSet(templateCounts, sourceCounts))
        {
            templateCompatibleIndices = templateTrace.UniqueTriangleIndices;
            return true;
        }

        if (sourceIndices.Count <= templateTrace.UniqueTriangleIndices.Length)
        {
            return false;
        }

        if (templateVertexRange is null
            || sourceVertexRange is null
            || sourceVertexRange.UniqueCount <= templateVertexRange.UniqueCount * 4)
        {
            return false;
        }

        foreach (var (key, templateCount) in templateCounts)
        {
            if (!sourceCounts.TryGetValue(key, out var sourceCount) || sourceCount < templateCount)
            {
                return false;
            }
        }

        templateCompatibleIndices = templateTrace.UniqueTriangleIndices;
        return true;
    }

    private static bool IsNearTemplateTriangleSet(
        IReadOnlyDictionary<string, int> templateCounts,
        IReadOnlyDictionary<string, int> sourceCounts)
    {
        var templateOnly = CountOnly(templateCounts, sourceCounts);
        var sourceOnly = CountOnly(sourceCounts, templateCounts);
        var templateTotal = templateCounts.Values.Sum();
        var sourceTotal = sourceCounts.Values.Sum();

        return templateTotal > 0
            && Math.Abs(templateTotal - sourceTotal) <= 2
            && templateOnly <= 2
            && sourceOnly <= 1;
    }

    private static int CountOnly(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right)
    {
        var count = 0;
        foreach (var (key, leftCount) in left)
        {
            right.TryGetValue(key, out var rightCount);
            if (leftCount > rightCount)
            {
                count += leftCount - rightCount;
            }
        }

        return count;
    }

    private static string[] BuildOnlySamples(
        IReadOnlyDictionary<string, int> left,
        IReadOnlyDictionary<string, int> right,
        int maxSamples = 16)
    {
        var samples = new List<string>(maxSamples);
        foreach (var (key, leftCount) in left.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            right.TryGetValue(key, out var rightCount);
            for (var i = rightCount; i < leftCount && samples.Count < maxSamples; i++)
            {
                samples.Add(key);
            }

            if (samples.Count >= maxSamples)
            {
                break;
            }
        }

        return samples.ToArray();
    }

    private static TopologyVertexRange? BuildTriangleVertexRange(IReadOnlyList<uint> indices)
    {
        if (indices.Count == 0)
        {
            return null;
        }

        return new TopologyVertexRange(indices.Min(), indices.Max(), indices.Distinct().Count());
    }

    private static bool HasSignificantZeroTopologyMarker(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.Equals(tokens[i].Kind, "zero", StringComparison.Ordinal))
            {
                continue;
            }

            for (var j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].VertexIndex > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountTemplateTopologyRestarts(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (IsNonPositiveTopologyToken(tokens[i]) && IsNonPositiveTopologyToken(tokens[i + 1]))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountGeneratedTopologyRestarts(IReadOnlyList<byte> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (unchecked((sbyte)tokens[i]) <= 0 && unchecked((sbyte)tokens[i + 1]) <= 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountResolvedTopologyRestarts(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var count = 0;
        for (var i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].IsNonPositive && tokens[i + 1].IsNonPositive)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetMaxIndex(IReadOnlyList<uint> indices)
    {
        var max = 0;
        foreach (var index in indices)
        {
            if (index > int.MaxValue)
            {
                throw new InvalidDataException("v1 importer supports mesh-local vertex indices up to 126.");
            }

            max = Math.Max(max, (int)index);
        }

        return max;
    }

    private static bool TopologyZeroMarkerPositionsMatch(
        ReadOnlySpan<byte> expectedPayload,
        ReadOnlySpan<byte> actualPayload,
        byte[]? expectedTexturePayload,
        byte[]? actualTexturePayload,
        int positionCount)
    {
        var expected = BuildResolvedTopologyTraceTokens(expectedPayload, expectedTexturePayload, positionCount);
        var actual = BuildResolvedTopologyTraceTokens(actualPayload, actualTexturePayload, positionCount);
        for (var i = 0; i < expected.Count; i++)
        {
            if (!expected[i].IsZeroMarker)
            {
                continue;
            }

            if (i >= actual.Count || !actual[i].IsZeroMarker)
            {
                return false;
            }
        }

        return true;
    }

    private static TopologyZeroMarkerSummary SummarizeTopologyZeroMarkers(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var indices = tokens
            .Select((token, index) => new { token, index })
            .Where(item => item.token.IsZeroMarker)
            .Select(item => item.index)
            .ToArray();

        return new TopologyZeroMarkerSummary(indices.Length, indices);
    }

    private static TopologyRowUsageSummary SummarizeGeneratedTopologyRowUsage(
        ReadOnlySpan<byte> payload,
        byte[]? texturePayload,
        int positionCount)
    {
        var tokens = BuildResolvedTopologyTraceTokens(payload, texturePayload, positionCount);
        var rows = tokens
            .Where(token => token.VertexIndex >= 0)
            .Select(token => token.VertexIndex)
            .ToArray();
        var uniqueRows = rows
            .Distinct()
            .Order()
            .ToArray();

        return new TopologyRowUsageSummary(
            tokens.Count,
            uniqueRows.Length == 0 ? null : uniqueRows[0],
            uniqueRows.Length == 0 ? null : uniqueRows[^1],
            uniqueRows.Length,
            tokens.Count(token => token.IsNonPositive),
            tokens.Count(token => token.IsZeroMarker),
            rows.Take(96).ToArray(),
            uniqueRows.Take(96).ToArray());
    }

    private static bool IsNonPositiveTopologyToken(ImportedTopologyPayloadToken token)
    {
        return token.Negative || string.Equals(token.Kind, "zero", StringComparison.Ordinal);
    }

    private static TopologyShapeSummary SummarizeTemplateTopologyShape(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var patterns = BuildTemplateTopologyTokenPatterns(tokens);
        return SummarizeTopologyShape(patterns);
    }

    private static TopologyShapeSummary SummarizeGeneratedTopologyShape(IReadOnlyList<byte> tokens)
    {
        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var isNegative = unchecked((sbyte)tokens[i]) < 0;
            var nextIsRestart = i + 1 < tokens.Count && unchecked((sbyte)tokens[i + 1]) <= 0;
            if (unchecked((sbyte)tokens[i]) <= 0 && nextIsRestart)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(isNegative);
        }

        return SummarizeTopologyShape(patterns.Where(pattern => pattern.Count > 0).ToList());
    }

    private static TopologyShapeSummary SummarizeResolvedTopologyShape(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var patterns = new List<List<bool>>();
        List<bool>? currentPattern = null;
        for (var i = 0; i < tokens.Count; i++)
        {
            var nextIsRestart = i + 1 < tokens.Count && tokens[i + 1].IsNonPositive;
            if (tokens[i].IsNonPositive && nextIsRestart)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            if (currentPattern is null)
            {
                currentPattern = [];
                patterns.Add(currentPattern);
            }

            currentPattern.Add(tokens[i].IsNonPositive);
        }

        return SummarizeTopologyShape(patterns.Where(pattern => pattern.Count > 0).ToList());
    }

    private static TopologyShapeSummary SummarizeTopologyShape(IReadOnlyList<List<bool>> patterns)
    {
        return new TopologyShapeSummary(
            patterns.Count,
            patterns.Sum(pattern => pattern.Count),
            patterns.Select(pattern => pattern.Count).ToArray(),
            patterns.Select(pattern => pattern.Count(value => value)).ToArray(),
            patterns.Select(pattern => pattern.Skip(2).Count(value => value)).ToArray());
    }

    private static bool TopologyShapesMatch(TopologyShapeSummary expected, TopologyShapeSummary actual)
    {
        return expected.SegmentCount == actual.SegmentCount
            && expected.EffectiveTokenCount == actual.EffectiveTokenCount
            && expected.SegmentTokenLengths.SequenceEqual(actual.SegmentTokenLengths)
            && expected.SegmentNegativeTokenCounts.SequenceEqual(actual.SegmentNegativeTokenCounts)
            && expected.SegmentMidStripNegativeTokenCounts.SequenceEqual(actual.SegmentMidStripNegativeTokenCounts);
    }

    private static TopologyTraceSummary TraceTemplateTopology(IReadOnlyList<ImportedTopologyPayloadToken> tokens)
    {
        var effectiveTokens = new List<TopologyTraceToken>();
        var effectiveTokenCount = tokens.Count;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!string.Equals(tokens[i].Kind, "zero", StringComparison.Ordinal))
            {
                continue;
            }

            effectiveTokenCount = Math.Max(0, i - 3);
            break;
        }

        for (var i = 0; i < effectiveTokenCount; i++)
        {
            var token = tokens[i];
            effectiveTokens.Add(new TopologyTraceToken(
                token.Negative || string.Equals(token.Kind, "zero", StringComparison.Ordinal),
                token.VertexIndex,
                IsZeroMarker: false));
        }

        return TraceTopology(effectiveTokens);
    }

    private static TopologyTraceSummary TraceGeneratedTopology(IReadOnlyList<byte> tokens)
    {
        return TraceTopology(tokens
            .Select(token => new TopologyTraceToken(unchecked((sbyte)token) <= 0, (token & 0x7F) - 1, IsZeroMarker: false))
            .ToList());
    }

    private static TopologyTraceSummary TraceTopology(IReadOnlyList<TopologyTraceToken> tokens)
    {
        var segments = new List<TopologyTraceSegment>();
        var allSeenTriangleKeys = new HashSet<string>(StringComparer.Ordinal);
        var allSeenTriangleIndices = new List<uint>();
        var currentStrip = new List<uint>();
        var currentEvents = new List<TopologyTraceControlEvent>();
        var segmentIndex = -1;
        var segmentTokenStart = 0;
        var segmentTokenCount = 0;
        var rawTriangleCount = 0;
        var uniqueTriangleCount = 0;
        var degenerateTriangleCount = 0;
        var duplicateTriangleCount = 0;
        var midStripControlCount = 0;

        void FlushSegment()
        {
            if (segmentIndex < 0)
            {
                return;
            }

            segments.Add(new TopologyTraceSegment(
                segmentIndex,
                segmentTokenStart,
                segmentTokenCount,
                rawTriangleCount,
                uniqueTriangleCount,
                degenerateTriangleCount,
                duplicateTriangleCount,
                midStripControlCount,
                currentEvents.ToArray()));
        }

        void StartSegment(int tokenIndex)
        {
            FlushSegment();
            segmentIndex++;
            segmentTokenStart = tokenIndex;
            segmentTokenCount = 0;
            rawTriangleCount = 0;
            uniqueTriangleCount = 0;
            degenerateTriangleCount = 0;
            duplicateTriangleCount = 0;
            midStripControlCount = 0;
            currentEvents = [];
            currentStrip = [];
        }

        TopologyTraceEmittedTriangle? TryEmitTriangle()
        {
            if (currentStrip.Count < 3)
            {
                return null;
            }

            var k = currentStrip.Count - 1;
            var a = currentStrip[k - 2];
            var b = currentStrip[k - 1];
            var c = currentStrip[k];
            var flip = ((k - 2) & 1) != 0;
            var i0 = a;
            var i1 = flip ? c : b;
            var i2 = flip ? b : c;
            var isDegenerate = i0 == i1 || i1 == i2 || i0 == i2;
            var key = BuildIndexTriangleKey(i0, i1, i2);
            var isDuplicate = !isDegenerate && !allSeenTriangleKeys.Add(key);

            rawTriangleCount++;
            if (isDegenerate)
            {
                degenerateTriangleCount++;
            }
            else if (isDuplicate)
            {
                duplicateTriangleCount++;
            }
            else
            {
                uniqueTriangleCount++;
                allSeenTriangleIndices.Add(i0);
                allSeenTriangleIndices.Add(i1);
                allSeenTriangleIndices.Add(i2);
            }

            return new TopologyTraceEmittedTriangle(
                new[] { i0, i1, i2 },
                flip,
                isDegenerate,
                isDuplicate,
                isDegenerate || isDuplicate ? null : uniqueTriangleCount - 1);
        }

        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var nextIsRestart = tokenIndex + 1 < tokens.Count && tokens[tokenIndex + 1].IsNonPositive;
            if (segmentIndex < 0 || (token.IsNonPositive && nextIsRestart))
            {
                StartSegment(tokenIndex);
            }

            segmentTokenCount++;
            var emitted = new List<TopologyTraceEmittedTriangle>();
            var action = "append";
            if (token.IsNonPositive && !nextIsRestart)
            {
                action = "duplicate-previous";
                if (segmentTokenCount > 2)
                {
                    midStripControlCount++;
                }

                if (currentStrip.Count > 0)
                {
                    currentStrip.Add(currentStrip[^1]);
                    var duplicateTriangle = TryEmitTriangle();
                    if (duplicateTriangle is not null)
                    {
                        emitted.Add(duplicateTriangle);
                    }
                }
            }
            else if (token.IsNonPositive)
            {
                action = "restart";
            }

            if (token.VertexIndex >= 0)
            {
                currentStrip.Add((uint)token.VertexIndex);
                var triangle = TryEmitTriangle();
                if (triangle is not null)
                {
                    emitted.Add(triangle);
                }
            }

            if (action != "append" || emitted.Any(triangle => triangle.IsDegenerate || triangle.IsDuplicate))
            {
                currentEvents.Add(new TopologyTraceControlEvent(
                    tokenIndex,
                    segmentIndex,
                    segmentTokenCount - 1,
                    token.IsNonPositive,
                    token.VertexIndex,
                    action,
                    currentStrip.Count,
                    emitted.ToArray()));
            }
        }

        FlushSegment();
        return new TopologyTraceSummary(
            segments.Count,
            segments.Sum(segment => segment.RawTriangleCount),
            segments.Sum(segment => segment.UniqueTriangleCount),
            segments.Sum(segment => segment.DegenerateTriangleCount),
            segments.Sum(segment => segment.DuplicateTriangleCount),
            segments.Sum(segment => segment.MidStripControlCount),
            segments.ToArray(),
            allSeenTriangleIndices.ToArray());
    }

    private static Dictionary<string, int> CountTriangleSets(IReadOnlyList<uint> indices)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var key = BuildIndexTriangleKey(indices[i], indices[i + 1], indices[i + 2]);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        return counts;
    }

    private static List<byte> BuildZeroMarkerTopologyTokens(IReadOnlyList<byte> sourceTokens, byte[] prefixBytes)
    {
        if (sourceTokens.Count == 0)
        {
            return [];
        }

        var first = unchecked((sbyte)sourceTokens[0]);
        prefixBytes[2] = checked((byte)(first + 0x80));

        var tokens = new List<byte>(sourceTokens.Count + 4) { 0 };
        for (var i = 1; i < sourceTokens.Count; i++)
        {
            tokens.Add(sourceTokens[i]);
        }

        var trailerStart = Math.Max(0, sourceTokens.Count - 3);
        for (var i = trailerStart; i < sourceTokens.Count; i++)
        {
            tokens.Add(sourceTokens[i]);
        }

        tokens.Add(0);
        return tokens;
    }

    private static bool TemplateUsesZeroMarkerTopology(
        Ps2VifPacketSpan? topologyPacket,
        byte[] combinedVifData,
        byte[]? textureData)
    {
        if (topologyPacket is null || topologyPacket.PayloadLength < 8)
        {
            return false;
        }

        var payloadOffset = topologyPacket.Offset + 4;
        var payloadLength = Math.Min(topologyPacket.PayloadLength, combinedVifData.Length - payloadOffset);
        if (payloadLength < 8)
        {
            return false;
        }

        var payload = combinedVifData.AsSpan(payloadOffset, payloadLength);
        if (payload[4..].IndexOf((byte)0) >= 0)
        {
            return true;
        }

        return textureData is not null
            && Ps2VifPacket.ReadSpans(textureData).Any(packet => packet.IsUnpack && (packet.Command & 0x0F) == 0x0C);
    }

    private static byte[] BuildStandaloneTopologyVifData(byte[] payload)
    {
        var num = checked((byte)(payload.Length / 4));
        using var stream = new MemoryStream();
        Ps2VifPacket.WriteHeader(stream, immediate: 0, num, commandByte: 0x6E); // UNPACK_V4_8
        stream.Write(payload, 0, payload.Length);
        using var writer = new BinaryWriter(stream);
        Align(writer, 0x10);
        return stream.ToArray();
    }

    private static byte[] BuildGeneratedMinimalTopologyVifData(
        byte[] payload,
        byte vertexDomainCount,
        bool compactTopologyPacket,
        bool hasTextureMetadata)
    {
        const ushort generatedTopologyImmediate = 0x812D;
        const int guardPaddingBytes = 12;
        const int generatedTextureOverlapBytes = 8;

        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = compactTopologyPacket && hasTextureMetadata
            ? generatedTextureOverlapBytes
            : 0;
        var targetPayloadLength = Align(payload.Length + guardPaddingBytes + textureOverlapBytes, 4);
        while (compactTopologyPacket
            && (topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        if (targetPayloadLength / 4 > 0xFF)
        {
            throw new InvalidDataException(
                $"Regenerated topology VIF payload is {targetPayloadLength} bytes. v1 importer supports at most 1020 bytes per topology packet.");
        }

        var replacementPayload = new byte[targetPayloadLength];
        Array.Fill<byte>(replacementPayload, 0x80);
        payload.CopyTo(replacementPayload, 0);

        using var stream = new MemoryStream();
        WriteGeneratedVertexDomainUnpack(stream, vertexDomainCount);
        Ps2VifPacket.WriteHeader(stream, generatedTopologyImmediate, checked((byte)(targetPayloadLength / 4)), commandByte: 0x6E); // UNPACK_V4_8
        stream.Write(replacementPayload, 0, replacementPayload.Length);
        if (textureOverlapBytes > 0)
        {
            stream.SetLength(stream.Length - textureOverlapBytes);
        }

        using var writer = new BinaryWriter(stream);
        Align(writer, 0x10);
        return stream.ToArray();
    }

    private static short Quantize(float value, ref int clipCount)
    {
        var rounded = MathF.Round(value);
        if (rounded < short.MinValue)
        {
            clipCount++;
            return short.MinValue;
        }

        if (rounded > short.MaxValue)
        {
            clipCount++;
            return short.MaxValue;
        }

        return checked((short)rounded);
    }


    private static int EstimateGeneratedTopologyTokenCount(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry)
    {
        return BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology: false).Count;
    }

    private static int EstimateIsolatedTriangleTopologyTokenCount(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry)
    {
        return BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology: true).Count;
    }

    private static bool GeneratedTopologyFitsCompactPacketBudget(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        int vertexDomainCount,
        bool isolatedTriangleTopology)
    {
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (packet is null)
        {
            return true;
        }

        const int topologyPrefixBytes = 4;
        const int guardPaddingBytes = 12;
        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = ResolveTemplateTextureOverlapBytes(
            packet,
            templateEntry.VifData.Length,
            templateEntry.VifTextureData);
        var tokenCount = BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology).Count;
        var targetPayloadLength = Align(topologyPrefixBytes + tokenCount + guardPaddingBytes + textureOverlapBytes, 4);
        while ((topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        const int safetyMarginBytes = 0x10;
        return targetPayloadLength + safetyMarginBytes <= packet.PayloadLength;
    }

    private static bool GeneratedTopologyFitsGeneratedMinimalPacketBudget(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        int vertexDomainCount,
        bool isolatedTriangleTopology,
        bool compactTopologyPacket)
    {
        const int topologyPrefixBytes = 4;
        const int guardPaddingBytes = 12;
        const int generatedTextureOverlapBytes = 8;
        const int maxPacketPayloadBytes = 0xFF * 4;

        var topologyPacketOffset = 4 + vertexDomainCount * 4;
        var textureOverlapBytes = compactTopologyPacket && templateEntry.VifTextureData is { Length: > 0 }
            ? generatedTextureOverlapBytes
            : 0;
        var tokenCount = BuildEstimatedTopologyTokens(triangleIndices, templateEntry, isolatedTriangleTopology).Count;
        var targetPayloadLength = Align(topologyPrefixBytes + tokenCount + guardPaddingBytes + textureOverlapBytes, 4);
        while (compactTopologyPacket
            && (topologyPacketOffset + 4 + targetPayloadLength - textureOverlapBytes) % 0x10 != 0)
        {
            targetPayloadLength += 4;
        }

        return targetPayloadLength <= maxPacketPayloadBytes;
    }

    private static List<byte> BuildEstimatedTopologyTokens(
        IReadOnlyList<uint> triangleIndices,
        MobyMeshTableEntry templateEntry,
        bool isolatedTriangleTopology)
    {
        var topologyTokens = isolatedTriangleTopology
            ? Ps2VifTopology.BuildIsolatedTriangleTokens(triangleIndices)
            : Ps2VifTopology.BuildRestartStripTokens(triangleIndices);
        var combinedVifData = Combine(templateEntry.VifData, templateEntry.VifTextureData);
        var packet = TryFindTopologyPacket(combinedVifData);
        if (TemplateUsesZeroMarkerTopology(packet, combinedVifData, templateEntry.VifTextureData))
        {
            var prefixBytes = packet is not null && packet.PayloadLength >= 4
                ? combinedVifData.AsSpan(packet.Offset + 4, 4).ToArray()
                : [0, 0, 0x80, 0];
            topologyTokens = BuildZeroMarkerTopologyTokens(topologyTokens, prefixBytes);
        }

        return topologyTokens;
    }

}

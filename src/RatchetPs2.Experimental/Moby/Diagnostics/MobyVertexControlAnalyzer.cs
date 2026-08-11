using System.Text.Json;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Experimental.Moby.Diagnostics;

public static class MobyVertexControlAnalyzer
{
    public static MobyVertexControlAnalysis Analyze(
        IEnumerable<MobyVertexControlAnalysisInput> inputs,
        MobyModelReadOptions? readOptions = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        readOptions ??= new MobyModelReadOptions();

        var models = new List<MobyVertexControlModelAnalysis>();
        foreach (var input in inputs)
        {
            var model = MobyModelReader.Read(input.Stream, readOptions);
            models.Add(AnalyzeModel(input.Name, model));
        }

        return new MobyVertexControlAnalysis(models);
    }

    public static byte[] WriteJson(MobyVertexControlAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return JsonSerializer.SerializeToUtf8Bytes(
            analysis,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static MobyVertexControlModelAnalysis AnalyzeModel(string name, MobyModel model)
    {
        var meshes = new List<MobyVertexControlMeshAnalysis>();
        var entries = model.MeshTable?.Entries ?? [];
        for (var i = 0; i < entries.Count; i++)
        {
            meshes.Add(AnalyzeMesh(i, entries[i], model));
        }

        var distinctControlWords = meshes
            .SelectMany(mesh => mesh.ControlWords)
            .GroupBy(word => word.Value)
            .Select(group => new MobyVertexControlWordSummary(
                group.Key,
                group.Sum(word => word.Count),
                group.SelectMany(word => word.High7Values).Distinct().Order().ToArray(),
                group.Select(word => word.Low9Min).Min(),
                group.Select(word => word.Low9Max).Max()))
            .OrderByDescending(word => word.Count)
            .ThenBy(word => word.Value)
            .ToArray();

        return new MobyVertexControlModelAnalysis(
            name,
            entries.Count,
            model.HighLodMeshCount,
            model.LowLodMeshCount,
            model.FarLodMeshCount,
            model.MetalCount,
            model.JointCount,
            model.Skeleton?.Bones.Count ?? 0,
            model.CommonTransforms?.Length ?? 0,
            (model.CommonTransforms?.Length ?? 0) / 0x10,
            model.AnimationJoints?.Count ?? 0,
            meshes,
            distinctControlWords);
    }

    private static MobyVertexControlMeshAnalysis AnalyzeMesh(int meshIndex, MobyMeshTableEntry entry, MobyModel model)
    {
        var vertexData = entry.VertexData;
        var vifDataQw = entry.VifData.Length / 0x10;
        var vifTextureDataQw = (entry.VifTextureData?.Length ?? 0) / 0x10;
        var totalVifQw = vifDataQw + vifTextureDataQw;
        var vertexDataQw = vertexData.Length / 0x10;
        var vertexHeaderDomainCapacity = ReadUInt16(vertexData, 0x0A);
        var leadingVifDomainCapacity = TryReadLeadingVertexDomainUnpackCount(entry.VifData, out var domainCount)
            ? domainCount
            : -1;
        var commonTransformCount = (model.CommonTransforms?.Length ?? 0) / 0x10;
        var hasCommonTransform = entry.CommonTransformJointIndex >= 0
            && entry.CommonTransformJointIndex < commonTransformCount;
        var duplicateVertexCount = ReadUInt16(vertexData, 0x08);
        var twoWayBlendVertexCount = ReadUInt16(vertexData, 0x02);
        var threeWayBlendVertexCount = ReadUInt16(vertexData, 0x04);
        var mainVertexCount = ReadUInt16(vertexData, 0x06);
        var vertexTableOffset = ReadUInt16(vertexData, 0x0C);
        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        var supported = vertexTableOffset > 0
            && vertexTableOffset % 0x10 == 0
            && vertexTableOffset + inFileVertexCount * 0x10 <= vertexData.Length;

        if (!supported)
        {
            return new MobyVertexControlMeshAnalysis(
                meshIndex,
                entry.MeshType.ToString(),
                entry.Unknown0A,
                entry.CommonTransformJointIndex,
                hasCommonTransform,
                entry.VertexCount,
                entry.VifListSize,
                entry.VifListTextureSize,
                entry.VifData.Length,
                entry.VifTextureData?.Length ?? 0,
                vifDataQw,
                vifTextureDataQw,
                totalVifQw,
                entry.VertexDataSize,
                vertexData.Length,
                vertexDataQw,
                vertexHeaderDomainCapacity,
                leadingVifDomainCapacity,
                twoWayBlendVertexCount,
                threeWayBlendVertexCount,
                mainVertexCount,
                duplicateVertexCount,
                vertexTableOffset,
                false,
                [],
                [],
                new MobyVertexControlLow9Shape(0, 0, 0, 0, 0, 0, 0, 0, 0),
                [],
                new MobyVertexControlLow9Shape(0, 0, 0, 0, 0, 0, 0, 0, 0),
                [],
                [],
                AnalyzeTopology(entry, 0),
                [],
                []);
        }

        var rows = new List<MobyVertexControlRowSample>();
        var epilogueRows = new List<MobyVertexControlRowSample>();
        var low9Sequence = new List<int>();
        var controlWords = new Dictionary<ushort, List<int>>();
        var prefixes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            var controlWord = ReadUInt16(vertexData, offset);
            var low9 = controlWord & 0x01FF;
            low9Sequence.Add(low9);
            if (!controlWords.TryGetValue(controlWord, out var lowValues))
            {
                lowValues = [];
                controlWords.Add(controlWord, lowValues);
            }

            lowValues.Add(low9);

            var prefix = Convert.ToHexString(vertexData.AsSpan(offset + 0x02, 0x08));
            prefixes[prefix] = prefixes.GetValueOrDefault(prefix) + 1;

            if (rows.Count < 16)
            {
                rows.Add(new MobyVertexControlRowSample(
                    i,
                    controlWord,
                    controlWord >> 9,
                    low9,
                    -1,
                    prefix,
                    ReadInt16(vertexData, offset + 0x0A),
                    ReadInt16(vertexData, offset + 0x0C),
                    ReadInt16(vertexData, offset + 0x0E)));
            }
        }

        var wordSummaries = controlWords
            .Select(pair => new MobyVertexControlWordSummary(
                pair.Key,
                pair.Value.Count,
                [(int)(pair.Key >> 9)],
                pair.Value.Min(),
                pair.Value.Max()))
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => summary.Value)
            .ToArray();

        var prefixSummaries = prefixes
            .Select(pair => new MobyVertexControlPrefixSummary(pair.Key, pair.Value))
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => summary.Hex)
            .Take(16)
            .ToArray();
        var low9Runs = BuildLow9Runs(low9Sequence);
        var low9Shape = SummarizeLow9Shape(low9Sequence, low9Runs);
        var resolvedLow9Sequence = ResolveDelayedLow9Storage(vertexData, vertexTableOffset, inFileVertexCount);
        var resolvedLow9Runs = BuildLow9Runs(resolvedLow9Sequence);
        var resolvedLow9Shape = SummarizeLow9Shape(resolvedLow9Sequence, resolvedLow9Runs);
        var duplicateIndices = ReadDuplicateIndices(vertexData, matrixTransferCount: ReadUInt16(vertexData, 0x00), duplicateVertexCount);
        var topology = AnalyzeTopology(entry, inFileVertexCount + duplicateIndices.Length);
        var totalVertexRowCount = (vertexData.Length - vertexTableOffset) / 0x10;
        for (var i = inFileVertexCount; i < totalVertexRowCount && epilogueRows.Count < 16; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            var controlWord = ReadUInt16(vertexData, offset);
            var low9 = controlWord & 0x01FF;
            epilogueRows.Add(new MobyVertexControlRowSample(
                i,
                controlWord,
                controlWord >> 9,
                low9,
                -1,
                Convert.ToHexString(vertexData.AsSpan(offset + 0x02, 0x08)),
                ReadInt16(vertexData, offset + 0x0A),
                ReadInt16(vertexData, offset + 0x0C),
                ReadInt16(vertexData, offset + 0x0E)));
        }

        for (var i = 0; i < rows.Count && i < resolvedLow9Sequence.Length; i++)
        {
            rows[i] = rows[i] with { ResolvedLow9 = resolvedLow9Sequence[i] };
        }

        return new MobyVertexControlMeshAnalysis(
            meshIndex,
            entry.MeshType.ToString(),
            entry.Unknown0A,
            entry.CommonTransformJointIndex,
            hasCommonTransform,
            entry.VertexCount,
            entry.VifListSize,
            entry.VifListTextureSize,
            entry.VifData.Length,
            entry.VifTextureData?.Length ?? 0,
            vifDataQw,
            vifTextureDataQw,
            totalVifQw,
            entry.VertexDataSize,
            vertexData.Length,
            vertexDataQw,
            vertexHeaderDomainCapacity,
            leadingVifDomainCapacity,
            twoWayBlendVertexCount,
            threeWayBlendVertexCount,
            mainVertexCount,
            duplicateVertexCount,
            vertexTableOffset,
            true,
            wordSummaries,
            prefixSummaries,
            low9Shape,
            low9Runs.Take(32).ToArray(),
            resolvedLow9Shape,
            resolvedLow9Runs.Take(32).ToArray(),
            duplicateIndices,
            topology,
            epilogueRows,
            rows);
    }

    private static int[] ResolveDelayedLow9Storage(byte[] vertexData, int vertexTableOffset, int inFileVertexCount)
    {
        var resolved = new int[inFileVertexCount];
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            resolved[i] = ReadUInt16(vertexData, offset) & 0x01FF;
        }

        for (var i = 7; i < inFileVertexCount; i++)
        {
            var sourceOffset = vertexTableOffset + i * 0x10;
            resolved[i - 7] = ReadUInt16(vertexData, sourceOffset) & 0x01FF;
        }

        var vertexDataSizeQw = vertexData.Length / 0x10;
        var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
        if (epilogueVertexCount < 0 || epilogueVertexCount > 64)
        {
            return resolved;
        }

        var epilogueReadOffset = vertexTableOffset + inFileVertexCount * 0x10;
        epilogueReadOffset += Math.Max(7 - inFileVertexCount, 0) * 0x10;
        for (var i = Math.Max(7 - inFileVertexCount, 0); i < epilogueVertexCount; i++)
        {
            if (epilogueReadOffset + 0x10 > vertexData.Length)
            {
                break;
            }

            var destinationIndex = inFileVertexCount + i - 7;
            if (destinationIndex >= 0 && destinationIndex < resolved.Length)
            {
                resolved[destinationIndex] = ReadUInt16(vertexData, epilogueReadOffset) & 0x01FF;
            }

            epilogueReadOffset += 0x10;
        }

        var lastVertexOffset = epilogueReadOffset - 0x10;
        if (lastVertexOffset < 0 || lastVertexOffset + 0x10 > vertexData.Length)
        {
            lastVertexOffset = Math.Max(
                vertexTableOffset,
                Math.Min(vertexData.Length - 0x10, vertexTableOffset + (inFileVertexCount - 1) * 0x10));
        }

        for (var i = Math.Max(7 - inFileVertexCount - epilogueVertexCount, 0); i < 6; i++)
        {
            var destinationIndex = inFileVertexCount + epilogueVertexCount + i - 7;
            if (destinationIndex >= 0 && destinationIndex < resolved.Length)
            {
                resolved[destinationIndex] = ReadUInt16(vertexData, lastVertexOffset + 0x04 + i * 2) & 0x01FF;
            }
        }

        return resolved;
    }

    private static int[] ReadDuplicateIndices(byte[] vertexData, int matrixTransferCount, int duplicateVertexCount)
    {
        var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
        if (duplicateIndicesOffset % 4 != 0)
        {
            duplicateIndicesOffset += 2;
        }
        if (duplicateIndicesOffset % 8 != 0)
        {
            duplicateIndicesOffset += 4;
        }

        var duplicateIndices = new List<int>(duplicateVertexCount);
        for (var i = 0; i < duplicateVertexCount; i++)
        {
            var offset = duplicateIndicesOffset + i * 2;
            if (offset + 2 > vertexData.Length)
            {
                break;
            }

            duplicateIndices.Add((ReadUInt16(vertexData, offset) >> 7) & 0x01FF);
        }

        return duplicateIndices.ToArray();
    }

    private static MobyVertexControlTopologySummary AnalyzeTopology(MobyMeshTableEntry entry, int positionCount)
    {
        var combined = entry.VifTextureData is null
            ? entry.VifData
            : [.. entry.VifData, .. entry.VifTextureData];
        var packet = MobyVifPacketReader.Read(combined)
            .FirstOrDefault(packet => packet.Kind == "UNPACK_V4_8" && packet.Payload.Length >= 4);
        if (packet is null)
        {
            return new MobyVertexControlTopologySummary(false, 0, 0, 0, 0, 0, 0, 0, 0, []);
        }

        var tokenValues = new List<int>();
        var decodedIndices = new List<int>();
        var restartCount = 0;
        var negativeCount = 0;
        var zeroMarkerCount = 0;
        for (var i = 4; i < packet.Payload.Length; i++)
        {
            var signed = unchecked((sbyte)packet.Payload[i]);
            tokenValues.Add(signed);
            if (signed < 0)
            {
                negativeCount++;
            }
            else if (signed == 0)
            {
                zeroMarkerCount++;
            }

            if (signed <= 0 && i + 1 < packet.Payload.Length && unchecked((sbyte)packet.Payload[i + 1]) <= 0)
            {
                restartCount++;
            }

            var decoded = (signed & 0x7F) - 1;
            if (decoded >= 0)
            {
                decodedIndices.Add(decoded);
            }
        }

        var outOfRangeCount = positionCount <= 0
            ? 0
            : decodedIndices.Count(index => index >= positionCount);
        var runs = BuildTopologyRuns(tokenValues);
        return new MobyVertexControlTopologySummary(
            true,
            tokenValues.Count,
            decodedIndices.Count == 0 ? -1 : decodedIndices.Min(),
            decodedIndices.Count == 0 ? -1 : decodedIndices.Max(),
            decodedIndices.Distinct().Count(),
            restartCount,
            negativeCount,
            zeroMarkerCount,
            outOfRangeCount,
            runs.Take(32).ToArray());
    }

    private static bool TryReadLeadingVertexDomainUnpackCount(byte[] vifData, out int count)
    {
        count = 0;
        var packet = MobyVifPacketReader.Read(vifData)
            .FirstOrDefault(packet => packet.IsUnpack && packet.DestinationAddr == 0);
        if (packet is null)
        {
            return false;
        }

        count = packet.Num;
        return true;
    }

    private static MobyVertexControlTopologyRun[] BuildTopologyRuns(IReadOnlyList<int> tokenValues)
    {
        if (tokenValues.Count == 0)
        {
            return [];
        }

        var runs = new List<MobyVertexControlTopologyRun>();
        var start = 0;
        var previousNonPositive = tokenValues[0] <= 0;
        for (var i = 1; i < tokenValues.Count; i++)
        {
            var nonPositive = tokenValues[i] <= 0;
            if (nonPositive != previousNonPositive)
            {
                runs.Add(new MobyVertexControlTopologyRun(start, i - start, previousNonPositive));
                start = i;
                previousNonPositive = nonPositive;
            }
        }

        runs.Add(new MobyVertexControlTopologyRun(start, tokenValues.Count - start, previousNonPositive));
        return runs.ToArray();
    }

    private static MobyVertexControlLow9Shape SummarizeLow9Shape(
        IReadOnlyList<int> low9Sequence,
        IReadOnlyList<MobyVertexControlLow9Run> runs)
    {
        var zeroCount = 0;
        var ffCount = 0;
        var sequentialCount = 0;
        var otherCount = 0;

        for (var i = 0; i < low9Sequence.Count; i++)
        {
            var value = low9Sequence[i];
            if (value == 0)
            {
                zeroCount++;
            }
            else if (value == 0xFF)
            {
                ffCount++;
            }
            else if (value == i)
            {
                sequentialCount++;
            }
            else
            {
                otherCount++;
            }
        }

        return new MobyVertexControlLow9Shape(
            low9Sequence.Count,
            zeroCount,
            ffCount,
            sequentialCount,
            otherCount,
            runs.Count,
            runs.Count(run => run.ValueStart == 0 && run.ValueEnd == 0),
            runs.Count(run => run.ValueStart == 0xFF && run.ValueEnd == 0xFF),
            runs.Count(run => run.IsSequential));
    }

    private static MobyVertexControlLow9Run[] BuildLow9Runs(IReadOnlyList<int> low9Sequence)
    {
        if (low9Sequence.Count == 0)
        {
            return [];
        }

        var runs = new List<MobyVertexControlLow9Run>();
        var start = 0;
        var previous = low9Sequence[0];
        var sequential = false;
        for (var i = 1; i < low9Sequence.Count; i++)
        {
            var value = low9Sequence[i];
            var continuesConstant = value == previous && !sequential;
            var continuesSequential = value == previous + 1 && previous != 0xFF;
            if (!continuesConstant && !continuesSequential)
            {
                runs.Add(new MobyVertexControlLow9Run(
                    start,
                    i - start,
                    low9Sequence[start],
                    previous,
                    sequential));
                start = i;
                sequential = false;
            }
            else if (continuesSequential)
            {
                sequential = true;
            }

            previous = value;
        }

        runs.Add(new MobyVertexControlLow9Run(
            start,
            low9Sequence.Count - start,
            low9Sequence[start],
            previous,
            sequential));

        return runs.ToArray();
    }

    private static ushort ReadUInt16(byte[] data, int offset)
    {
        return offset >= 0 && offset + 2 <= data.Length
            ? BitConverter.ToUInt16(data, offset)
            : (ushort)0;
    }

    private static short ReadInt16(byte[] data, int offset)
    {
        return offset >= 0 && offset + 2 <= data.Length
            ? BitConverter.ToInt16(data, offset)
            : (short)0;
    }
}

public sealed record MobyVertexControlAnalysisInput(string Name, Stream Stream);

public sealed record MobyVertexControlAnalysis(
    IReadOnlyList<MobyVertexControlModelAnalysis> Models);

public sealed record MobyVertexControlModelAnalysis(
    string Name,
    int MeshCount,
    int HighLodMeshCount,
    int LowLodMeshCount,
    int FarLodMeshCount,
    int MetalCount,
    int JointCount,
    int SkeletonBoneCount,
    int CommonTransformLength,
    int CommonTransformCount,
    int AnimationJointCount,
    IReadOnlyList<MobyVertexControlMeshAnalysis> Meshes,
    IReadOnlyList<MobyVertexControlWordSummary> ControlWords);

public sealed record MobyVertexControlMeshAnalysis(
    int MeshIndex,
    string MeshType,
    int MeshTableUnknown0A,
    int CommonTransformJointIndex,
    bool HasCommonTransform,
    int HeaderVertexCount,
    int HeaderVifListSize,
    int HeaderVifListTextureSize,
    int VifDataLength,
    int VifTextureDataLength,
    int VifDataQw,
    int VifTextureDataQw,
    int TotalVifQw,
    int HeaderVertexDataSize,
    int VertexDataLength,
    int VertexDataQw,
    int VertexHeaderDomainCapacity,
    int LeadingVifDomainCapacity,
    int TwoWayBlendVertexCount,
    int ThreeWayBlendVertexCount,
    int MainVertexCount,
    int DuplicateVertexCount,
    int VertexTableOffset,
    bool SupportedRowLayout,
    IReadOnlyList<MobyVertexControlWordSummary> ControlWords,
    IReadOnlyList<MobyVertexControlPrefixSummary> Prefixes,
    MobyVertexControlLow9Shape Low9Shape,
    IReadOnlyList<MobyVertexControlLow9Run> Low9Runs,
    MobyVertexControlLow9Shape ResolvedLow9Shape,
    IReadOnlyList<MobyVertexControlLow9Run> ResolvedLow9Runs,
    IReadOnlyList<int> DuplicateIndices,
    MobyVertexControlTopologySummary Topology,
    IReadOnlyList<MobyVertexControlRowSample> EpilogueRowSamples,
    IReadOnlyList<MobyVertexControlRowSample> RowSamples);

public sealed record MobyVertexControlWordSummary(
    int Value,
    int Count,
    IReadOnlyList<int> High7Values,
    int Low9Min,
    int Low9Max);

public sealed record MobyVertexControlPrefixSummary(string Hex, int Count);

public sealed record MobyVertexControlLow9Shape(
    int RowCount,
    int ZeroCount,
    int FFCount,
    int SequentialRowIndexCount,
    int OtherCount,
    int RunCount,
    int ZeroRunCount,
    int FFRunCount,
    int SequentialRunCount);

public sealed record MobyVertexControlLow9Run(
    int RowStart,
    int Count,
    int ValueStart,
    int ValueEnd,
    bool IsSequential);

public sealed record MobyVertexControlTopologySummary(
    bool HasTopology,
    int TokenCount,
    int DecodedIndexMin,
    int DecodedIndexMax,
    int UniqueDecodedIndexCount,
    int RestartCount,
    int NegativeTokenCount,
    int ZeroMarkerCount,
    int OutOfRangeDecodedIndexCount,
    IReadOnlyList<MobyVertexControlTopologyRun> Runs);

public sealed record MobyVertexControlTopologyRun(
    int TokenStart,
    int Count,
    bool NonPositive);

public sealed record MobyVertexControlRowSample(
    int RowIndex,
    int ControlWord,
    int High7,
    int Low9,
    int ResolvedLow9,
    string PrefixHex,
    int X,
    int Y,
    int Z);

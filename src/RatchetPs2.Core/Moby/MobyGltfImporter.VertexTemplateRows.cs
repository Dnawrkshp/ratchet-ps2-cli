using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static VertexBuildResult RequireTemplateVertexData(
        ImportedMesh mesh,
        bool hasTemplateVertexData,
        VertexBuildResult templateVertexBuild,
        MobyGltfImportPacketMode mode)
    {
        if (!hasTemplateVertexData)
        {
            throw new InvalidDataException(
                $"Packet mode '{mode}' requires mesh {mesh.TemplateMeshIndex:0000} to match the template vertex positions/skinning.");
        }

        return templateVertexBuild;
    }

    private static bool TryBuildTemplateVertexData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        TemplateDecodedMesh? templateMesh,
        MobyGltfImportOptions options,
        out VertexBuildResult result)
    {
        result = default!;
        if (templateMesh is null || templateMesh.Positions.Count != mesh.Positions.Count)
        {
            return false;
        }

        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            if (Vector3.Distance(templateMesh.Positions[i], mesh.Positions[i]) > options.ScaleTolerance)
            {
                return false;
            }
        }

        var rewriteSkinRows = options.CustomStaticTransferReferenceSkinning
            && HasUsableSkinRows(mesh);
        if (mesh.Joints is not null
            && mesh.Weights is not null
            && !rewriteSkinRows
            && !options.CustomStaticTransferReferenceSkinning)
        {
            if (templateMesh.Joints.Count != mesh.Joints.Count || templateMesh.Weights.Count != mesh.Weights.Count)
            {
                return false;
            }

            for (var i = 0; i < mesh.Joints.Count; i++)
            {
                if (!SkinRowsMatch(templateMesh.Joints[i], templateMesh.Weights[i], mesh.Joints[i], mesh.Weights[i]))
                {
                    return false;
                }
            }
        }

        var indexByOriginalIndex = new int[mesh.Positions.Count];
        for (var i = 0; i < indexByOriginalIndex.Length; i++)
        {
            indexByOriginalIndex[i] = i;
        }

        result = new VertexBuildResult(
            (byte[])templateEntry.VertexData.Clone(),
            indexByOriginalIndex,
            UsedTemplateVertexData: true,
            UsedMetadataVertexLayout: false,
            UsedMetadataRowPrefixes: false,
            UsedMetadataLowVertexBits: false);
        return true;
    }

    private static VertexBuildResult BuildTemplatePositionVertexData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        TemplateDecodedMesh? templateMesh,
        MobyGltfImportOptions options,
        ref int quantizationClipCount)
    {
        if (templateMesh is null || templateMesh.Positions.Count != mesh.Positions.Count)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} to have the same vertex count as the template.");
        }

        var rewriteSkinRows = options.CustomStaticTransferReferenceSkinning
            && HasUsableSkinRows(mesh);
        if (mesh.Joints is not null
            && mesh.Weights is not null
            && !rewriteSkinRows
            && !options.CustomStaticTransferReferenceSkinning)
        {
            if (templateMesh.Joints.Count != mesh.Joints.Count || templateMesh.Weights.Count != mesh.Weights.Count)
            {
                throw new InvalidDataException(
                    $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} skin rows to match the template.");
            }

            for (var i = 0; i < mesh.Joints.Count; i++)
            {
                if (!SkinRowsMatch(templateMesh.Joints[i], templateMesh.Weights[i], mesh.Joints[i], mesh.Weights[i]))
                {
                    throw new InvalidDataException(
                        $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} skin rows to match the template.");
                }
            }
        }

        var data = (byte[])templateEntry.VertexData.Clone();
        var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
        var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
        var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
        var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        if (vertexTableOffset <= 0 || vertexTableOffset % 0x10 != 0 || vertexTableOffset + inFileVertexCount * 0x10 > data.Length)
        {
            throw new InvalidDataException($"Template vertex data for mesh {mesh.TemplateMeshIndex:0000} has an unsupported layout.");
        }

        if (options.CustomStaticNeutralizeTemplateSkinning && !mesh.CustomStaticHideMesh)
        {
            NeutralizeTemplateSkinRows(data, vertexTableOffset, inFileVertexCount, templateEntry.CommonTransformJointIndex);
        }

        for (var i = 0; i < inFileVertexCount; i++)
        {
            var source = mesh.Positions[i];
            var x = Quantize(source.X / scale, ref quantizationClipCount);
            var sourceY = Quantize(-source.Z / scale, ref quantizationClipCount);
            var sourceZ = Quantize(source.Y / scale, ref quantizationClipCount);
            var offset = vertexTableOffset + i * 0x10;
            WriteInt16(data, offset + 0x0A, x);
            WriteInt16(data, offset + 0x0C, sourceY);
            WriteInt16(data, offset + 0x0E, sourceZ);
        }

        if (options.CustomStaticFlattenVertexPrefixes && !mesh.CustomStaticHideMesh)
        {
            FlattenVertexRowPrefixes(data, vertexTableOffset, inFileVertexCount);
        }

        var indexByOriginalIndex = new int[mesh.Positions.Count];
        for (var i = 0; i < indexByOriginalIndex.Length; i++)
        {
            indexByOriginalIndex[i] = i;
        }

        return new VertexBuildResult(
            data,
            indexByOriginalIndex,
            UsedTemplateVertexData: false,
            UsedMetadataVertexLayout: false,
            UsedMetadataRowPrefixes: false,
            UsedMetadataLowVertexBits: false);
    }

    private static void FlattenVertexRowPrefixes(byte[] data, int vertexTableOffset, int inFileVertexCount)
    {
        if (inFileVertexCount <= 1 || vertexTableOffset + 0x10 > data.Length)
        {
            return;
        }

        var prefix = data.AsSpan(vertexTableOffset + 0x02, 0x08).ToArray();
        for (var i = 1; i < inFileVertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            if (offset + 0x0A > data.Length)
            {
                break;
            }

            prefix.CopyTo(data.AsSpan(offset + 0x02, 0x08));
        }
    }

    private static void NeutralizeTemplateSkinRows(byte[] data, int vertexTableOffset, int inFileVertexCount, byte jointIndex)
    {
        var safeJoint = (ushort)(jointIndex & 0x7F);
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            var low9 = (ushort)(i & 0x01FF);
            WriteUInt16(data, offset, checked((ushort)((safeJoint << 9) | low9)));
            data[offset + 0x02] = 0;
            data[offset + 0x03] = 4;
            data[offset + 0x04] = 255;
            data[offset + 0x05] = 0;
            data[offset + 0x06] = 0;
            data[offset + 0x07] = 4;
            data[offset + 0x08] = 0;
            data[offset + 0x09] = 0;
        }
    }

    private static VertexBuildResult BuildCompactRigidRows(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        MobyGltfImportOptions options,
        ref int quantizationClipCount)
    {
        var templateData = (byte[])templateEntry.VertexData.Clone();
        var vertexTableOffset = BitConverter.ToUInt16(templateData, 0x0C);
        var templateTwoWayBlendVertexCount = BitConverter.ToUInt16(templateData, 0x02);
        var templateThreeWayBlendVertexCount = BitConverter.ToUInt16(templateData, 0x04);
        var templateMainVertexCount = BitConverter.ToUInt16(templateData, 0x06);
        var templateInFileVertexCount = templateTwoWayBlendVertexCount + templateThreeWayBlendVertexCount + templateMainVertexCount;
        if (vertexTableOffset <= 0
            || vertexTableOffset % 0x10 != 0
            || vertexTableOffset > templateData.Length
            || templateInFileVertexCount <= 0)
        {
            throw new InvalidDataException(
                $"Custom static compact rigid-row mode found unsupported vertex data layout for mesh {mesh.TemplateMeshIndex:0000}.");
        }

        var preservesTemplateRowContract = options.CustomStaticPreserveTemplateRowContract;
        var vertexCount = preservesTemplateRowContract || options.CustomStaticPreserveTemplateVertexHeaderCounts
            ? templateInFileVertexCount
            : mesh.Positions.Count;
        var epilogueCount = 7;
        var templateTotalVertexRowCount = (templateData.Length - vertexTableOffset) / 0x10;
        var compactLength = vertexTableOffset + (vertexCount + epilogueCount) * 0x10;
        var data = preservesTemplateRowContract || options.CustomStaticPadCompactRigidRowsToTemplateSize
            ? (byte[])templateData.Clone()
            : new byte[compactLength];
        var totalVertexRowCount = (data.Length - vertexTableOffset) / 0x10;
        if (options.CustomStaticGenerateCompactVertexHeader && !preservesTemplateRowContract)
        {
            var headerDomainCapacity = options.CustomStaticGenerateVertexHeaderDomainCapacity
                ? ResolveGeneratedDomainCapacity(mesh)
                : (byte)0x61;
            WriteGeneratedCompactVertexHeader(data, vertexTableOffset, vertexCount, headerDomainCapacity);
        }
        else
        {
            templateData.AsSpan(0, Math.Min(vertexTableOffset, data.Length)).CopyTo(data);
            WriteUInt16(data, 0x02, preservesTemplateRowContract || options.CustomStaticPreserveTemplateVertexHeaderCounts ? templateTwoWayBlendVertexCount : (ushort)0);
            WriteUInt16(data, 0x04, preservesTemplateRowContract || options.CustomStaticPreserveTemplateVertexHeaderCounts ? templateThreeWayBlendVertexCount : (ushort)0);
            WriteUInt16(data, 0x06, checked((ushort)(preservesTemplateRowContract || options.CustomStaticPreserveTemplateVertexHeaderCounts ? templateMainVertexCount : vertexCount)));
            WriteUInt16(data, 0x08, 0);
        }

        var forcedLow9Value = ResolveCustomStaticVertexControlLow9Value(options);
        var preserveTemplateLow9MaxValue = options.CustomStaticPreserveTemplateLow9MaxValue
            ?? (options.CustomStaticAutoPreserveTemplateLow9MaxValue
                ? DeriveTemplateLow9ActiveWindowMaxValue(templateData, vertexTableOffset, templateInFileVertexCount)
                : null);
        var templateRowPrefix = ResolveCustomStaticVertexRowPrefix(templateData, vertexTableOffset, templateInFileVertexCount, options);
        NeutralizeTemplateSkinRows(data, vertexTableOffset, vertexCount, templateEntry.CommonTransformJointIndex);

        for (var i = 0; i < vertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            var templateOffset = vertexTableOffset + Math.Min(i, templateInFileVertexCount - 1) * 0x10;
            var low9 = forcedLow9Value is { } low9Value
                ? ShouldPreserveTemplateLow9UpToValue(templateData, templateOffset, preserveTemplateLow9MaxValue)
                    ? BitConverter.ToUInt16(templateData, templateOffset) & 0x01FF
                    : low9Value & 0x01FF
                : i & 0x01FF;
            WriteUInt16(data, offset, checked((ushort)low9));
            templateRowPrefix?.CopyTo(data.AsSpan(offset + 0x02, 0x08));

            var source = mesh.Positions[Math.Min(i, mesh.Positions.Count - 1)];
            WriteInt16(data, offset + 0x0A, Quantize(source.X / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0C, Quantize(-source.Z / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0E, Quantize(source.Y / scale, ref quantizationClipCount));
        }

        var fill = mesh.Positions.Count == 0 ? Vector3.Zero : mesh.Positions[^1];
        for (var i = vertexCount; i < totalVertexRowCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            WriteInt16(data, offset + 0x0A, Quantize(fill.X / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0C, Quantize(-fill.Z / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0E, Quantize(fill.Y / scale, ref quantizationClipCount));
        }

        if (options.CustomStaticGenerateTemplateEpilogueControlPrefix)
        {
            WriteGeneratedTemplateEpilogueControlPrefix(data, templateData, vertexTableOffset, vertexCount, Math.Min(totalVertexRowCount, templateTotalVertexRowCount), options);
        }

        var indexByOriginalIndex = new int[mesh.Positions.Count];
        for (var i = 0; i < indexByOriginalIndex.Length; i++)
        {
            indexByOriginalIndex[i] = i;
        }

        return new VertexBuildResult(
            data,
            indexByOriginalIndex,
            UsedTemplateVertexData: false,
            UsedMetadataVertexLayout: false,
            UsedMetadataRowPrefixes: templateRowPrefix is not null,
            UsedMetadataLowVertexBits: preserveTemplateLow9MaxValue is not null,
            PreservedTemplateLow9MaxValue: preserveTemplateLow9MaxValue);
    }

    private static VertexBuildResult BuildRigidRowsInTemplateVertexLayout(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        MobyGltfImportOptions options,
        ref int quantizationClipCount)
    {
        var data = (byte[])templateEntry.VertexData.Clone();
        var templateData = (byte[])templateEntry.VertexData.Clone();
        var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
        var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
        var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
        var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        var totalVertexRowCount = vertexTableOffset <= data.Length
            ? (data.Length - vertexTableOffset) / 0x10
            : 0;
        if (vertexTableOffset <= 0
            || vertexTableOffset % 0x10 != 0
            || vertexTableOffset + inFileVertexCount * 0x10 > data.Length
            || inFileVertexCount > mesh.Positions.Count)
        {
            throw new InvalidDataException(
                $"Custom static rigid-template-row mode found unsupported vertex data layout for mesh {mesh.TemplateMeshIndex:0000}.");
        }

        var forcedLow9Value = ResolveCustomStaticVertexControlLow9Value(options);
        var preserveTemplateLow9 = options.CustomStaticPreserveTemplateVertexControlLowBits
            || (!options.CustomStaticPreserveTemplateVertexControlWords
                && forcedLow9Value is null);
        var zeroHighBits = options.CustomStaticZeroVertexControlHighBits
            || !options.CustomStaticPreserveTemplateVertexControlWords;
        var templateRowPrefix = ResolveCustomStaticVertexRowPrefix(data, vertexTableOffset, inFileVertexCount, options);
        var preserveTemplateLow9MaxValue = options.CustomStaticPreserveTemplateLow9MaxValue
            ?? (options.CustomStaticAutoPreserveTemplateLow9MaxValue
                ? DeriveTemplateLow9ActiveWindowMaxValue(templateData, vertexTableOffset, inFileVertexCount)
                : null);
        var duplicateLow9Values = BuildDuplicateLow9PreserveSet(templateData, options);
        NeutralizeTemplateSkinRows(data, vertexTableOffset, inFileVertexCount, templateEntry.CommonTransformJointIndex);

        for (var i = 0; i < inFileVertexCount; i++)
        {
            var source = mesh.Positions[i];
            var offset = vertexTableOffset + i * 0x10;
            if (options.CustomStaticPreserveTemplateVertexControlWords)
            {
                data[offset] = templateData[offset];
                data[offset + 1] = templateData[offset + 1];
            }
            else
            {
                var low9 = preserveTemplateLow9
                    ? BitConverter.ToUInt16(templateData, offset) & 0x01FF
                    : forcedLow9Value is { } low9Value
                        ? ShouldPreserveTemplateSparseLow9(templateData, vertexTableOffset, i, options.CustomStaticPreserveTemplateSparseLow9Count)
                            || ShouldPreserveTemplateLow9UpToValue(templateData, offset, preserveTemplateLow9MaxValue)
                            || ShouldPreserveTemplateDuplicateLow9(templateData, offset, duplicateLow9Values)
                            ? BitConverter.ToUInt16(templateData, offset) & 0x01FF
                            : options.CustomStaticVertexControlLow9WarmupZeroCount is { } warmupZeroCount && i < warmupZeroCount
                            ? 0
                            : low9Value & 0x01FF
                        : i & 0x01FF;
                var highBits = zeroHighBits
                    ? 0
                    : BitConverter.ToUInt16(data, offset) & 0xFE00;
                WriteUInt16(data, offset, checked((ushort)(highBits | low9)));
            }

            templateRowPrefix?.CopyTo(data.AsSpan(offset + 0x02, 0x08));
            WriteInt16(data, offset + 0x0A, Quantize(source.X / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0C, Quantize(-source.Z / scale, ref quantizationClipCount));
            WriteInt16(data, offset + 0x0E, Quantize(source.Y / scale, ref quantizationClipCount));
        }

        var rewriteEpiloguePrefixes = options.CustomStaticRewriteTemplateEpilogueRows
            || options.CustomStaticRewriteTemplateEpiloguePrefixes;
        var rewriteEpiloguePositions = options.CustomStaticRewriteTemplateEpilogueRows
            || options.CustomStaticRewriteTemplateEpiloguePositions;
        if (rewriteEpiloguePrefixes || rewriteEpiloguePositions)
        {
            var fillIndex = Math.Clamp(inFileVertexCount - 1, 0, mesh.Positions.Count - 1);
            var fill = mesh.Positions.Count == 0 ? Vector3.Zero : mesh.Positions[fillIndex];
            for (var i = inFileVertexCount; i < totalVertexRowCount; i++)
            {
                var offset = vertexTableOffset + i * 0x10;
                if (rewriteEpiloguePrefixes)
                {
                    templateRowPrefix?.CopyTo(data.AsSpan(offset + 0x02, 0x08));
                }

                if (rewriteEpiloguePositions)
                {
                    WriteInt16(data, offset + 0x0A, Quantize(fill.X / scale, ref quantizationClipCount));
                    WriteInt16(data, offset + 0x0C, Quantize(-fill.Z / scale, ref quantizationClipCount));
                    WriteInt16(data, offset + 0x0E, Quantize(fill.Y / scale, ref quantizationClipCount));
                }
            }
        }

        if (options.CustomStaticGenerateTemplateEpilogueControlPrefix)
        {
            WriteGeneratedTemplateEpilogueControlPrefix(data, templateData, vertexTableOffset, inFileVertexCount, totalVertexRowCount, options);
        }

        var indexByOriginalIndex = new int[mesh.Positions.Count];
        for (var i = 0; i < indexByOriginalIndex.Length; i++)
        {
            indexByOriginalIndex[i] = i;
        }

        var usedTemplateLow9Metadata = UsesTemplateLow9Metadata(options, preserveTemplateLow9, preserveTemplateLow9MaxValue);
        return new VertexBuildResult(
            data,
            indexByOriginalIndex,
            UsedTemplateVertexData: false,
            UsedMetadataVertexLayout: true,
            UsedMetadataRowPrefixes: templateRowPrefix is not null,
            UsedMetadataLowVertexBits: usedTemplateLow9Metadata,
            PreservedTemplateLow9MaxValue: preserveTemplateLow9MaxValue);
    }

    private static void WriteGeneratedCompactVertexHeader(byte[] data, int vertexTableOffset, int vertexCount, byte domainCapacity = 0x61)
    {
        data.AsSpan(0, Math.Min(vertexTableOffset, data.Length)).Clear();
        data[0] = 1;
        WriteUInt16(data, 0x02, 0);
        WriteUInt16(data, 0x04, 0);
        WriteUInt16(data, 0x06, checked((ushort)vertexCount));
        WriteUInt16(data, 0x08, 0);
        WriteUInt16(data, 0x0A, domainCapacity);
        WriteUInt16(data, 0x0C, checked((ushort)vertexTableOffset));
        WriteUInt16(data, 0x0E, 0);
    }

    private static bool UsesTemplateLow9Metadata(
        MobyGltfImportOptions options,
        bool preserveTemplateLow9,
        int? preserveTemplateLow9MaxValue)
    {
        return options.CustomStaticPreserveTemplateVertexControlWords
            || preserveTemplateLow9
            || options.CustomStaticPreserveTemplateSparseLow9Count is not null
            || preserveTemplateLow9MaxValue is not null
            || options.CustomStaticPreserveDuplicateLow9Values
            || options.CustomStaticPreserveLow9UpToMaxDuplicate;
    }

    private static int? ResolveCustomStaticVertexControlLow9Value(MobyGltfImportOptions options)
    {
        return options.CustomStaticVertexControlLow9Value
            ?? (options.CustomStaticAutoVertexControlLow9Tail ? 0xFF : null);
    }

    private static byte[]? ResolveCustomStaticVertexRowPrefix(
        byte[] data,
        int vertexTableOffset,
        int inFileVertexCount,
        MobyGltfImportOptions options)
    {
        if (options.CustomStaticVertexPrefixBytes is { Count: 8 } prefixBytes)
        {
            return prefixBytes.ToArray();
        }

        var resolvedShade = options.CustomStaticVertexPrefixShade
            ?? (options.CustomStaticAutoVertexPrefixShade
                ? DeriveTemplateVertexPrefixShade(data, vertexTableOffset, inFileVertexCount)
                    ?? DefaultCustomStaticVertexPrefixShade
                : null);
        if (resolvedShade is { } shade)
        {
            return [0x00, 0xF4, 0x00, 0x00, 0x00, 0x00, 0x00, shade];
        }

        return options.CustomStaticFlattenVertexPrefixes
            && inFileVertexCount > 0
            && vertexTableOffset + 0x0A <= data.Length
                ? data.AsSpan(vertexTableOffset + 0x02, 0x08).ToArray()
                : null;
    }

    private static byte? DeriveTemplateVertexPrefixShade(byte[] data, int vertexTableOffset, int inFileVertexCount)
    {
        if (inFileVertexCount <= 0 || vertexTableOffset + 0x0A > data.Length)
        {
            return null;
        }

        var shade = data[vertexTableOffset + 0x09];
        return shade == 0 ? null : shade;
    }

    private static int? DeriveTemplateLow9ActiveWindowMaxValue(byte[] templateData, int vertexTableOffset, int inFileVertexCount)
    {
        var nonFF = new List<int>();
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            if (offset + 2 > templateData.Length)
            {
                break;
            }

            var low9 = BitConverter.ToUInt16(templateData, offset) & 0x01FF;
            if (low9 != 0xFF)
            {
                nonFF.Add(low9);
            }
        }

        if (nonFF.Count <= 1)
        {
            return DefaultCustomStaticLow9ActiveWindowMaxValue;
        }

        nonFF.RemoveAt(nonFF.Count - 1);
        return nonFF.Count == 0
            ? DefaultCustomStaticLow9ActiveWindowMaxValue
            : Math.Max(nonFF.Max(), DefaultCustomStaticLow9ActiveWindowMaxValue);
    }

    private static void WriteGeneratedTemplateEpilogueControlPrefix(
        byte[] data,
        byte[] templateData,
        int vertexTableOffset,
        int inFileVertexCount,
        int totalVertexRowCount,
        MobyGltfImportOptions options)
    {
        for (var i = inFileVertexCount; i < totalVertexRowCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            data.AsSpan(offset + 0x02, 0x08).Clear();
        }

        if (totalVertexRowCount <= inFileVertexCount)
        {
            return;
        }

        if (options.CustomStaticClearTemplateEpilogueFinalMarker)
        {
            return;
        }

        var lastOffset = vertexTableOffset + (totalVertexRowCount - 1) * 0x10;
        if (options.CustomStaticGenerateTemplateEpilogueFinalMarker)
        {
            data.AsSpan(lastOffset + 0x04, 0x06).Fill(0xFF);
            data[lastOffset + 0x05] = 0;
            data[lastOffset + 0x07] = 0;
            data[lastOffset + 0x09] = 0;
            return;
        }

        templateData.AsSpan(lastOffset + 0x04, 0x06).CopyTo(data.AsSpan(lastOffset + 0x04, 0x06));
    }

    private static bool ShouldPreserveTemplateSparseLow9(
        byte[] templateData,
        int vertexTableOffset,
        int rowIndex,
        int? sparsePreserveCount)
    {
        if (sparsePreserveCount is null || sparsePreserveCount <= 0)
        {
            return false;
        }

        var preserved = 0;
        for (var i = 0; i <= rowIndex; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            if (offset + 2 > templateData.Length)
            {
                return false;
            }

            var low9 = BitConverter.ToUInt16(templateData, offset) & 0x01FF;
            if (low9 == 0xFF)
            {
                continue;
            }

            preserved++;
            if (i == rowIndex)
            {
                return preserved <= sparsePreserveCount;
            }
        }

        return false;
    }

    private static bool ShouldPreserveTemplateLow9UpToValue(
        byte[] templateData,
        int rowOffset,
        int? maxValue)
    {
        if (maxValue is null || maxValue < 0 || rowOffset + 2 > templateData.Length)
        {
            return false;
        }

        var low9 = BitConverter.ToUInt16(templateData, rowOffset) & 0x01FF;
        return low9 != 0xFF && low9 <= maxValue;
    }

    private static HashSet<int>? BuildDuplicateLow9PreserveSet(byte[] templateData, MobyGltfImportOptions options)
    {
        if (!options.CustomStaticPreserveDuplicateLow9Values && !options.CustomStaticPreserveLow9UpToMaxDuplicate)
        {
            return null;
        }

        if (templateData.Length < 0x10)
        {
            return [];
        }

        var matrixTransferCount = BitConverter.ToUInt16(templateData, 0x00);
        var duplicateVertexCount = BitConverter.ToUInt16(templateData, 0x08);
        var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
        if (duplicateIndicesOffset % 4 != 0)
        {
            duplicateIndicesOffset += 2;
        }
        if (duplicateIndicesOffset % 8 != 0)
        {
            duplicateIndicesOffset += 4;
        }

        var duplicateValues = new List<int>(duplicateVertexCount);
        for (var i = 0; i < duplicateVertexCount; i++)
        {
            var offset = duplicateIndicesOffset + i * 2;
            if (offset + 2 > templateData.Length)
            {
                break;
            }

            duplicateValues.Add((BitConverter.ToUInt16(templateData, offset) >> 7) & 0x01FF);
        }

        if (options.CustomStaticPreserveLow9UpToMaxDuplicate && duplicateValues.Count > 0)
        {
            var max = duplicateValues.Max();
            return Enumerable.Range(0, max + 1).ToHashSet();
        }

        return duplicateValues.ToHashSet();
    }

    private static bool ShouldPreserveTemplateDuplicateLow9(
        byte[] templateData,
        int rowOffset,
        HashSet<int>? duplicateLow9Values)
    {
        if (duplicateLow9Values is null || rowOffset + 2 > templateData.Length)
        {
            return false;
        }

        var low9 = BitConverter.ToUInt16(templateData, rowOffset) & 0x01FF;
        return duplicateLow9Values.Contains(low9);
    }

    private static VertexBuildResult BuildMetadataVertexData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        TemplateDecodedMesh? templateMesh,
        MobyGltfImportOptions options,
        ref int quantizationClipCount)
    {
        var layout = mesh.Metadata?.VertexLayout ?? TryBuildTemplateVertexLayoutMetadata(templateEntry);
        if (layout is null || !layout.Supported)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} to have moby vertex layout metadata.");
        }

        if (templateMesh is null || templateMesh.Positions.Count != mesh.Positions.Count)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} to have the same vertex count as the template.");
        }

        var hasUsableSkinRows = HasUsableSkinRows(mesh);
        var rewriteSkinRows = options.CustomStaticTransferReferenceSkinning
            && hasUsableSkinRows;
        if (mesh.Joints is not null
            && mesh.Weights is not null
            && !rewriteSkinRows
            && !options.CustomStaticTransferReferenceSkinning)
        {
            if (templateMesh.Joints.Count != mesh.Joints.Count || templateMesh.Weights.Count != mesh.Weights.Count)
            {
                throw new InvalidDataException(
                    $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} skin rows to match the template.");
            }

            for (var i = 0; i < mesh.Joints.Count; i++)
            {
                if (!SkinRowsMatch(templateMesh.Joints[i], templateMesh.Weights[i], mesh.Joints[i], mesh.Weights[i]))
                {
                    throw new InvalidDataException(
                        $"Packet mode '{options.PacketMode}' requires mesh {mesh.TemplateMeshIndex:0000} skin rows to match the template.");
                }
            }
        }

        var inFileVertexCount = layout.TwoWayBlendVertexCount + layout.ThreeWayBlendVertexCount + layout.MainVertexCount;
        if (layout.VertexTableOffset <= 0
            || layout.VertexTableOffset % 0x10 != 0
            || layout.VertexTableOffset + inFileVertexCount * 0x10 > templateEntry.VertexData.Length
            || inFileVertexCount > mesh.Positions.Count)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' found unsupported vertex layout metadata for mesh {mesh.TemplateMeshIndex:0000}.");
        }

        var metadataVertexDataLength = layout.VertexTableOffset + (inFileVertexCount + layout.EpilogueVertexCount) * 0x10;
        if (metadataVertexDataLength <= 0 || metadataVertexDataLength != templateEntry.VertexData.Length)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' found vertex data size metadata that does not match template mesh {mesh.TemplateMeshIndex:0000}.");
        }

        if (layout.RowPrefixBytes.Length < (inFileVertexCount + layout.EpilogueVertexCount) * 0x0A)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' requires row prefix metadata for mesh {mesh.TemplateMeshIndex:0000}.");
        }

        var data = new byte[metadataVertexDataLength];
        if (layout.HeaderBytes.Length >= 0x10)
        {
            layout.HeaderBytes.AsSpan(0, 0x10).CopyTo(data);
        }
        if (layout.EpilogueBytes.Length == layout.EpilogueVertexCount * 0x10)
        {
            layout.EpilogueBytes.CopyTo(data.AsSpan(layout.VertexTableOffset + inFileVertexCount * 0x10));
        }

        WriteUInt16(data, 0x00, checked((ushort)layout.MatrixTransferCount));
        WriteUInt16(data, 0x02, checked((ushort)layout.TwoWayBlendVertexCount));
        WriteUInt16(data, 0x04, checked((ushort)layout.ThreeWayBlendVertexCount));
        WriteUInt16(data, 0x06, checked((ushort)layout.MainVertexCount));
        WriteUInt16(data, 0x08, checked((ushort)layout.DuplicateVertexCount));
        WriteUInt16(data, 0x0C, checked((ushort)layout.VertexTableOffset));

        for (var i = 0; i < layout.MatrixTransfers.Count; i++)
        {
            var offset = 0x10 + i * 2;
            if (offset + 2 > data.Length)
            {
                break;
            }

            data[offset] = unchecked((byte)layout.MatrixTransfers[i].Joint);
            data[offset + 1] = checked((byte)layout.MatrixTransfers[i].Vu0DestinationAddress);
        }

        for (var i = 0; i < layout.DuplicateIndices.Count; i++)
        {
            var offset = layout.DuplicateIndicesOffset + i * 2;
            if (offset + 2 > data.Length)
            {
                break;
            }

            WriteUInt16(data, offset, checked((ushort)(layout.DuplicateIndices[i] << 7)));
        }

        var metadataSourceVertices = rewriteSkinRows || (options.CustomStaticSkinPositionsRelativeToBind && hasUsableSkinRows)
            ? layout.MatrixTransferCount > 0
                ? BuildSourceVerticesForMetadataLayout(mesh, templateEntry.CommonTransformJointIndex, layout, options)
                : BuildDominantSourceVerticesForMetadataLayout(mesh, templateEntry.CommonTransformJointIndex)
            : null;

        for (var i = 0; i < inFileVertexCount; i++)
        {
            var source = options.CustomStaticSkinPositionsRelativeToBind && metadataSourceVertices is not null && i < metadataSourceVertices.Count
                ? GetWeightedBindLocalPosition(mesh, metadataSourceVertices[i].Position, metadataSourceVertices[i].Influences)
                : mesh.Positions[i];
            var x = Quantize(source.X / scale, ref quantizationClipCount);
            var sourceY = Quantize(-source.Z / scale, ref quantizationClipCount);
            var sourceZ = Quantize(source.Y / scale, ref quantizationClipCount);
            var offset = layout.VertexTableOffset + i * 0x10;
            WriteInt16(data, offset + 0x0A, x);
            WriteInt16(data, offset + 0x0C, sourceY);
            WriteInt16(data, offset + 0x0E, sourceZ);
        }

        var usedRowPrefixes = WriteMetadataRowPrefixes(data, layout.VertexTableOffset, inFileVertexCount, layout.RowPrefixBytes);
        if (rewriteSkinRows)
        {
            if (layout.MatrixTransferCount > 0)
            {
                WriteMetadataSkinRows(data, layout, metadataSourceVertices!, mesh.TemplateMeshIndex, options);
            }
            else
            {
                WriteMetadataDirectSkinRows(data, layout, metadataSourceVertices!, mesh.TemplateMeshIndex, options);
            }
        }
        else
        {
            WriteMetadataLowVertexBits(data, layout.VertexTableOffset, inFileVertexCount, layout.Low9StorageValues);
        }
        if (options.CustomStaticFlattenVertexPrefixes && !mesh.CustomStaticHideMesh)
        {
            FlattenVertexRowPrefixes(data, layout.VertexTableOffset, inFileVertexCount);
        }

        var indexByOriginalIndex = new int[mesh.Positions.Count];
        for (var i = 0; i < indexByOriginalIndex.Length; i++)
        {
            indexByOriginalIndex[i] = i;
        }

        return new VertexBuildResult(
            data,
            indexByOriginalIndex,
            UsedTemplateVertexData: false,
            UsedMetadataVertexLayout: true,
            UsedMetadataRowPrefixes: usedRowPrefixes,
            UsedMetadataLowVertexBits: true);
    }

    private static void WriteMetadataSkinRows(
        byte[] data,
        ImportedVertexLayoutMetadata layout,
        IReadOnlyList<SourceVertex> sourceVertices,
        int meshIndex,
        MobyGltfImportOptions options)
    {
        var inFileVertexCount = layout.TwoWayBlendVertexCount + layout.ThreeWayBlendVertexCount + layout.MainVertexCount;
        if (sourceVertices.Count < inFileVertexCount)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' cannot rewrite skin rows for mesh {meshIndex:0000}; vertex layout has more rows than source vertices.");
        }

        var uniqueJoints = sourceVertices
            .Take(inFileVertexCount)
            .SelectMany(vertex => vertex.Influences.Select(influence => influence.Joint))
            .Distinct()
            .Order()
            .ToList();
        if (uniqueJoints.Count > layout.MatrixTransferCount)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' cannot rewrite skin rows for mesh {meshIndex:0000}; {uniqueJoints.Count} joints exceed metadata matrix transfer capacity {layout.MatrixTransferCount}.");
        }

        var jointAddressByJoint = new Dictionary<ushort, byte>();
        for (var i = 0; i < layout.MatrixTransferCount; i++)
        {
            var offset = 0x10 + i * 2;
            if (offset + 2 > data.Length)
            {
                break;
            }

            var address = i < layout.MatrixTransfers.Count
                ? checked((byte)layout.MatrixTransfers[i].Vu0DestinationAddress)
                : checked((byte)(i * 4));
            if (i < uniqueJoints.Count)
            {
                data[offset] = checked((byte)uniqueJoints[i]);
                data[offset + 1] = address;
                jointAddressByJoint[uniqueJoints[i]] = address;
            }
            else if (i < layout.MatrixTransfers.Count)
            {
                data[offset] = checked((byte)Math.Clamp(layout.MatrixTransfers[i].Joint, 0, 127));
                data[offset + 1] = address;
            }
        }

        var scratchAddress = layout.MatrixTransfers.Count > 0
            ? checked((byte)Math.Min(byte.MaxValue, layout.MatrixTransfers.Max(transfer => transfer.Vu0DestinationAddress) + 4))
            : checked((byte)(layout.MatrixTransferCount * 4));
        var lowVertexIndices = new ushort[inFileVertexCount];
        var highJointBits = new ushort[inFileVertexCount];
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var vertex = sourceVertices[i];
            var offset = layout.VertexTableOffset + i * 0x10;
            lowVertexIndices[i] = checked((ushort)(i & 0x01FF));
            highJointBits[i] = BuildSkinVertexBytes(data, offset, vertex, jointAddressByJoint, scratchAddress);
        }

        for (var i = 7; i < inFileVertexCount; i++)
        {
            WriteUInt16(data, layout.VertexTableOffset + i * 0x10, checked((ushort)(highJointBits[i] | lowVertexIndices[i - 7])));
        }

        for (var epilogue = 0; epilogue < layout.EpilogueVertexCount; epilogue++)
        {
            var destination = inFileVertexCount + epilogue - 7;
            if (destination >= 0 && destination < lowVertexIndices.Length)
            {
                WriteUInt16(data, layout.VertexTableOffset + (inFileVertexCount + epilogue) * 0x10, lowVertexIndices[destination]);
            }
        }
    }

    private static void WriteMetadataDirectSkinRows(
        byte[] data,
        ImportedVertexLayoutMetadata layout,
        IReadOnlyList<SourceVertex> sourceVertices,
        int meshIndex,
        MobyGltfImportOptions options)
    {
        var inFileVertexCount = layout.TwoWayBlendVertexCount + layout.ThreeWayBlendVertexCount + layout.MainVertexCount;
        if (sourceVertices.Count < inFileVertexCount)
        {
            throw new InvalidDataException(
                $"Packet mode '{options.PacketMode}' cannot rewrite direct skin rows for mesh {meshIndex:0000}; vertex layout has more rows than source vertices.");
        }

        var lowVertexIndices = new ushort[inFileVertexCount];
        var highJointBits = new ushort[inFileVertexCount];
        for (var i = 0; i < inFileVertexCount; i++)
        {
            var joint = sourceVertices[i].Influences.Count > 0
                ? sourceVertices[i].Influences[0].Joint
                : (ushort)0;
            lowVertexIndices[i] = checked((ushort)(i & 0x01FF));
            highJointBits[i] = checked((ushort)((joint & 0x7F) << 9));
            WriteUInt16(data, layout.VertexTableOffset + i * 0x10, highJointBits[i]);
        }

        for (var i = 7; i < inFileVertexCount; i++)
        {
            WriteUInt16(data, layout.VertexTableOffset + i * 0x10, checked((ushort)(highJointBits[i] | lowVertexIndices[i - 7])));
        }

        for (var epilogue = 0; epilogue < layout.EpilogueVertexCount; epilogue++)
        {
            var destination = inFileVertexCount + epilogue - 7;
            if (destination >= 0 && destination < lowVertexIndices.Length)
            {
                WriteUInt16(data, layout.VertexTableOffset + (inFileVertexCount + epilogue) * 0x10, lowVertexIndices[destination]);
            }
        }
    }

    private static List<SourceVertex> BuildSourceVerticesForMetadataLayout(
        ImportedMesh mesh,
        byte fallbackJoint,
        ImportedVertexLayoutMetadata layout,
        MobyGltfImportOptions options)
    {
        var vertices = new List<SourceVertex>(mesh.Positions.Count);
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var requiredInfluenceCount = i < layout.TwoWayBlendVertexCount
                ? 2
                : i < layout.TwoWayBlendVertexCount + layout.ThreeWayBlendVertexCount
                    ? 3
                    : 1;
            var influences = ReadInfluences(mesh, i, fallbackJoint)
                .OrderByDescending(influence => influence.Weight)
                .Take(Math.Min(options.MaxInfluences, requiredInfluenceCount))
                .ToList();
            if (influences.Count == 0)
            {
                influences.Add(new MobySkinInfluence(fallbackJoint, 1f));
            }

            while (influences.Count < requiredInfluenceCount)
            {
                influences.Add(new MobySkinInfluence(influences[0].Joint, 0f));
            }

            NormalizeInfluences(influences);
            vertices.Add(new SourceVertex(i, mesh.Positions[i], influences));
        }

        return vertices;
    }

    private static List<SourceVertex> BuildDominantSourceVerticesForMetadataLayout(
        ImportedMesh mesh,
        byte fallbackJoint)
    {
        var vertices = new List<SourceVertex>(mesh.Positions.Count);
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var influence = ReadInfluences(mesh, i, fallbackJoint)
                .OrderByDescending(influence => influence.Weight)
                .FirstOrDefault();
            var influences = influence.Weight > 0f
                ? new List<MobySkinInfluence> { new(influence.Joint, 1f) }
                : new List<MobySkinInfluence> { new(fallbackJoint, 1f) };
            vertices.Add(new SourceVertex(i, mesh.Positions[i], influences));
        }

        return vertices;
    }

    private static ImportedVertexLayoutMetadata? TryBuildTemplateVertexLayoutMetadata(MobyMeshTableEntry templateEntry)
    {
        var data = templateEntry.VertexData;
        if (data.Length < 0x10)
        {
            return null;
        }

        var matrixTransferCount = BitConverter.ToUInt16(data, 0x00);
        var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
        var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
        var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
        var duplicateVertexCount = BitConverter.ToUInt16(data, 0x08);
        var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
        if (vertexTableOffset <= 0 || vertexTableOffset % 0x10 != 0 || vertexTableOffset > data.Length)
        {
            return null;
        }

        var matrixTransfers = new List<ImportedMatrixTransferMetadata>();
        for (var i = 0; i < matrixTransferCount; i++)
        {
            var offset = 0x10 + i * 2;
            if (offset + 2 > data.Length)
            {
                return null;
            }

            matrixTransfers.Add(new ImportedMatrixTransferMetadata(unchecked((sbyte)data[offset]), data[offset + 1]));
        }

        var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
        if (duplicateIndicesOffset % 4 != 0)
        {
            duplicateIndicesOffset += 2;
        }
        if (duplicateIndicesOffset % 8 != 0)
        {
            duplicateIndicesOffset += 4;
        }

        var duplicateIndices = new List<int>();
        for (var i = 0; i < duplicateVertexCount; i++)
        {
            var offset = duplicateIndicesOffset + i * 2;
            if (offset + 2 > data.Length)
            {
                return null;
            }

            duplicateIndices.Add((BitConverter.ToUInt16(data, offset) >> 7) & 0x01FF);
        }

        var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;
        var vertexDataSizeQw = data.Length / 0x10;
        var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
        if (inFileVertexCount < 0 || epilogueVertexCount < 0)
        {
            return null;
        }

        var low9StorageValues = new List<int>();
        var rowPrefixBytes = new List<byte>();
        var rowCount = inFileVertexCount + epilogueVertexCount;
        for (var i = 0; i < rowCount; i++)
        {
            var offset = vertexTableOffset + i * 0x10;
            if (offset + 0x10 > data.Length)
            {
                return null;
            }

            var control = BitConverter.ToUInt16(data, offset);
            low9StorageValues.Add(control & 0x01FF);
            var highControl = (ushort)(control & ~0x01FF);
            rowPrefixBytes.Add((byte)(highControl & 0xFF));
            rowPrefixBytes.Add((byte)(highControl >> 8));
            for (var j = 2; j < 0x0A; j++)
            {
                rowPrefixBytes.Add(data[offset + j]);
            }
        }

        var epilogueOffset = vertexTableOffset + inFileVertexCount * 0x10;
        var epilogueLength = epilogueVertexCount * 0x10;
        var epilogueBytes = epilogueLength > 0
            ? data.AsSpan(epilogueOffset, epilogueLength).ToArray()
            : Array.Empty<byte>();

        return new ImportedVertexLayoutMetadata(
            Supported: true,
            MatrixTransferCount: matrixTransferCount,
            TwoWayBlendVertexCount: twoWayBlendVertexCount,
            ThreeWayBlendVertexCount: threeWayBlendVertexCount,
            MainVertexCount: mainVertexCount,
            DuplicateVertexCount: duplicateVertexCount,
            VertexTableOffset: vertexTableOffset,
            DuplicateIndicesOffset: duplicateIndicesOffset,
            EpilogueVertexCount: epilogueVertexCount,
            HeaderBytes: data.AsSpan(0, 0x10).ToArray(),
            EpilogueBytes: epilogueBytes,
            MatrixTransfers: matrixTransfers,
            DuplicateIndices: duplicateIndices,
            Low9StorageValues: low9StorageValues,
            RowPrefixBytes: rowPrefixBytes.ToArray());
    }

    private static bool WriteMetadataRowPrefixes(
        byte[] data,
        int vertexTableOffset,
        int inFileVertexCount,
        IReadOnlyList<byte> rowPrefixBytes)
    {
        if (rowPrefixBytes.Count == 0)
        {
            return false;
        }

        var vertexDataSizeQw = data.Length / 0x10;
        var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
        if (epilogueVertexCount < 0)
        {
            return false;
        }

        var bytesPerRow = rowPrefixBytes.Count >= (inFileVertexCount + epilogueVertexCount) * 0x0A
            ? 0x0A
            : 8;
        var rowCount = Math.Min(rowPrefixBytes.Count / bytesPerRow, inFileVertexCount + epilogueVertexCount);
        for (var i = 0; i < rowCount; i++)
        {
            var destinationOffset = vertexTableOffset + i * 0x10;
            var sourceOffset = i * bytesPerRow;
            for (var j = 0; j < bytesPerRow; j++)
            {
                data[destinationOffset + j] = rowPrefixBytes[sourceOffset + j];
            }
        }

        return rowCount > 0;
    }

    private static void WriteMetadataLowVertexBits(
        byte[] data,
        int vertexTableOffset,
        int inFileVertexCount,
        IReadOnlyList<int> low9StorageValues)
    {
        var vertexDataSizeQw = data.Length / 0x10;
        var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
        if (epilogueVertexCount < 0)
        {
            return;
        }

        var rowCount = Math.Min(low9StorageValues.Count, inFileVertexCount + epilogueVertexCount);
        for (var i = 0; i < rowCount; i++)
        {
            WriteLow9Bits(data, vertexTableOffset + i * 0x10, checked((ushort)low9StorageValues[i]));
        }
    }

    private static bool SkinRowsMatch(
        IReadOnlyList<ushort> templateJoints,
        IReadOnlyList<float> templateWeights,
        IReadOnlyList<ushort> importedJoints,
        IReadOnlyList<float> importedWeights)
    {
        var template = BuildInfluenceMap(templateJoints, templateWeights);
        var imported = BuildInfluenceMap(importedJoints, importedWeights);
        if (template.Count != imported.Count)
        {
            return false;
        }

        const float tolerance = 0.005f;
        return template.All(pair => imported.TryGetValue(pair.Key, out var weight) && MathF.Abs(weight - pair.Value) <= tolerance);
    }

    private static Dictionary<ushort, float> BuildInfluenceMap(IReadOnlyList<ushort> joints, IReadOnlyList<float> weights)
    {
        var result = new Dictionary<ushort, float>();
        for (var i = 0; i < Math.Min(joints.Count, weights.Count); i++)
        {
            if (weights[i] <= 0.0001f)
            {
                continue;
            }

            result[joints[i]] = result.TryGetValue(joints[i], out var current) ? current + weights[i] : weights[i];
        }

        return result;
    }

}

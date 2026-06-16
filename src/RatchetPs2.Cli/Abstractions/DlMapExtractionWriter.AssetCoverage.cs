using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static AssetWadCoverageSummary WriteAssetWadCoverageArtifacts(
        string outputDirectory,
        DlAssetHeader header,
        byte[] assetBytes,
        IReadOnlyList<int> knownAssetOffsets,
        IReadOnlyList<DlAssetModelDefinition> mobyDefinitions,
        IReadOnlyList<DlAssetModelDefinition> tieDefinitions,
        IReadOnlyList<DlAssetShrubDefinition> shrubDefinitions,
        IReadOnlyList<DlNormalizedTextureMetadata> textureMetadata)
    {
        var ranges = new List<AssetCoverageRange>();
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "tfrag/tfrag.bin", header.TerrainOffset, knownAssetOffsets, allowZeroOffset: true);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "occlusion.bin", header.OcclusionOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, SkyboxSourcePath, header.SkyOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "collision.bin", header.CollisionOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "light_cuboids.bin", header.LightCuboidsOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "heightmap.bin", header.HeightmapOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "occlusion_octree.bin", header.OcclusionOctreeOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "occlusion_radius.bin", header.OcclusionRadiusOffset, knownAssetOffsets);
        AddAssetSliceCoverageRange(ranges, assetBytes.Length, "occlusion_radius2.bin", header.OcclusionRadius2Offset, knownAssetOffsets);

        AddModelCoverageRanges(ranges, assetBytes.Length, "moby", mobyDefinitions, knownAssetOffsets);
        AddModelCoverageRanges(ranges, assetBytes.Length, "tie", tieDefinitions, knownAssetOffsets);
        AddShrubCoverageRanges(ranges, assetBytes.Length, shrubDefinitions, knownAssetOffsets);
        foreach (var texture in textureMetadata)
        {
            AddTextureCoverageRanges(ranges, header, assetBytes.Length, texture);
        }

        var mergedRanges = MergeAssetCoverageRanges(ranges, assetBytes.Length);
        var gaps = FindAssetCoverageGaps(assetBytes, mergedRanges);
        var preservedUnknownRanges = WriteUnknownAssetRanges(outputDirectory, assetBytes, gaps);
        var semanticCoveredByteCount = mergedRanges.Sum(range => range.Length);

        return new AssetWadCoverageSummary(
            assetBytes.Length,
            semanticCoveredByteCount,
            mergedRanges.Count,
            gaps.Count,
            gaps.Count(gap => gap.NonZeroByteCount == 0),
            preservedUnknownRanges.Count,
            preservedUnknownRanges.Sum(range => range.Length),
            preservedUnknownRanges);
    }

    private static void AddModelCoverageRanges(
        List<AssetCoverageRange> ranges,
        int assetLength,
        string family,
        IReadOnlyList<DlAssetModelDefinition> modelDefinitions,
        IReadOnlyList<int> knownAssetOffsets)
    {
        foreach (var definition in modelDefinitions)
        {
            if (IsEmptyMobyModel(family, definition))
            {
                continue;
            }

            AddAssetSliceCoverageRange(
                ranges,
                assetLength,
                $"{family}/{DlAssetReader.GetAssetFolderName(definition.ModelId)}/{family}.bin",
                definition.ModelOffset,
                knownAssetOffsets);
        }
    }

    private static void AddShrubCoverageRanges(
        List<AssetCoverageRange> ranges,
        int assetLength,
        IReadOnlyList<DlAssetShrubDefinition> shrubDefinitions,
        IReadOnlyList<int> knownAssetOffsets)
    {
        foreach (var definition in shrubDefinitions)
        {
            AddAssetSliceCoverageRange(
                ranges,
                assetLength,
                $"shrub/{DlAssetReader.GetAssetFolderName(definition.ModelId)}/shrub.bin",
                definition.ModelOffset,
                knownAssetOffsets);
        }
    }

    private static void AddTextureCoverageRanges(
        List<AssetCoverageRange> ranges,
        DlAssetHeader header,
        int assetLength,
        DlNormalizedTextureMetadata texture)
    {
        switch (texture.SourceDefinition)
        {
            case DlAssetTextureDefinition definition when (definition.Type & 1) != 0:
                AddAssetCoverageRange(
                    ranges,
                    assetLength,
                    $"textures/{texture.Family}.{texture.Index:0000}.pixels",
                    texture.PixelOffset,
                    texture.PixelLength);
                for (var i = 0; i < texture.MipPixelOffsets.Count; i++)
                {
                    var mipOffset = texture.MipPixelOffsets[i];
                    if (mipOffset >= header.TextureDataOffset)
                    {
                        AddAssetCoverageRange(
                            ranges,
                            assetLength,
                            $"textures/{texture.Family}.{texture.Index:0000}.mip{i + 1}",
                            mipOffset,
                            texture.MipPixelLengths[i]);
                    }
                }

                break;
            case DlParticleTextureDefinition:
            case DlFxTextureDefinition:
                AddAssetCoverageRange(
                    ranges,
                    assetLength,
                    $"textures/{texture.Family}.{texture.Index:0000}.palette",
                    texture.PaletteOffset,
                    0x400);
                AddAssetCoverageRange(
                    ranges,
                    assetLength,
                    $"textures/{texture.Family}.{texture.Index:0000}.pixels",
                    texture.PixelOffset,
                    texture.PixelLength);
                break;
        }
    }

    private static void AddAssetSliceCoverageRange(
        List<AssetCoverageRange> ranges,
        int assetLength,
        string label,
        int offset,
        IReadOnlyList<int> knownAssetOffsets,
        bool allowZeroOffset = false)
    {
        if (offset < 0 || (offset == 0 && !allowZeroOffset) || offset >= assetLength)
        {
            return;
        }

        var nextOffset = knownAssetOffsets
            .Where(candidate => candidate > offset && candidate <= assetLength)
            .DefaultIfEmpty(assetLength)
            .Min();
        AddAssetCoverageRange(ranges, assetLength, label, offset, nextOffset - offset);
    }

    private static void AddAssetCoverageRange(
        List<AssetCoverageRange> ranges,
        int assetLength,
        string label,
        int offset,
        int length)
    {
        if (offset < 0 || length <= 0 || offset >= assetLength)
        {
            return;
        }

        var end = Math.Min(assetLength, offset + length);
        if (end > offset)
        {
            ranges.Add(new AssetCoverageRange(offset, end, label));
        }
    }

    private static IReadOnlyList<AssetCoverageRange> MergeAssetCoverageRanges(
        IReadOnlyList<AssetCoverageRange> ranges,
        int assetLength)
    {
        var merged = new List<AssetCoverageRange>();
        foreach (var range in ranges
            .Where(range => range.Start < range.End && range.Start < assetLength)
            .OrderBy(range => range.Start)
            .ThenBy(range => range.End))
        {
            var start = Math.Max(0, range.Start);
            var end = Math.Min(assetLength, range.End);
            if (merged.Count == 0 || start > merged[^1].End)
            {
                merged.Add(new AssetCoverageRange(start, end, range.Label));
                continue;
            }

            if (end > merged[^1].End)
            {
                merged[^1] = merged[^1] with { End = end };
            }
        }

        return merged;
    }

    private static IReadOnlyList<AssetCoverageGap> FindAssetCoverageGaps(
        byte[] assetBytes,
        IReadOnlyList<AssetCoverageRange> mergedRanges)
    {
        var gaps = new List<AssetCoverageGap>();
        var position = 0;
        foreach (var range in mergedRanges)
        {
            if (range.Start > position)
            {
                gaps.Add(CreateAssetCoverageGap(assetBytes, position, range.Start));
            }

            position = Math.Max(position, range.End);
        }

        if (position < assetBytes.Length)
        {
            gaps.Add(CreateAssetCoverageGap(assetBytes, position, assetBytes.Length));
        }

        return gaps;
    }

    private static AssetCoverageGap CreateAssetCoverageGap(byte[] assetBytes, int start, int end)
    {
        var nonZeroByteCount = 0;
        for (var i = start; i < end; i++)
        {
            if (assetBytes[i] != 0)
            {
                nonZeroByteCount++;
            }
        }

        return new AssetCoverageGap(start, end, nonZeroByteCount);
    }

    private static IReadOnlyList<AssetUnknownRange> WriteUnknownAssetRanges(
        string outputDirectory,
        byte[] assetBytes,
        IReadOnlyList<AssetCoverageGap> gaps)
    {
        var unknownRanges = new List<AssetUnknownRange>();
        foreach (var gap in gaps.Where(gap => gap.NonZeroByteCount > 0))
        {
            var relativePath = $"unknown/range_{gap.Start:X8}_{gap.End:X8}.bin";
            var outputPath = CombineRelativePath(outputDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, assetBytes.AsSpan(gap.Start, gap.Length).ToArray());
            unknownRanges.Add(new AssetUnknownRange(
                gap.Start,
                gap.End,
                gap.Length,
                gap.NonZeroByteCount,
                relativePath));
        }

        return unknownRanges;
    }

    private static IReadOnlyList<int> CollectKnownAssetOffsets(
        DlAssetHeader header,
        int assetLength,
        IEnumerable<DlAssetModelDefinition> mobyDefinitions,
        IEnumerable<DlAssetModelDefinition> tieDefinitions,
        IEnumerable<DlAssetShrubDefinition> shrubDefinitions)
    {
        var offsets = new List<int>
        {
            header.TerrainOffset,
            header.OcclusionOffset,
            header.SkyOffset,
            header.CollisionOffset,
            header.TextureDataOffset,
            header.ParticleTextureDataOffset,
            header.FxTextureDataOffset,
            header.LightCuboidsOffset,
            header.HeightmapOffset,
            header.OcclusionOctreeOffset,
            header.OcclusionRadiusOffset,
            header.OcclusionRadius2Offset,
            assetLength
        };
        offsets.AddRange(mobyDefinitions.Select(definition => definition.ModelOffset));
        offsets.AddRange(tieDefinitions.Select(definition => definition.ModelOffset));
        offsets.AddRange(shrubDefinitions.Select(definition => definition.ModelOffset));

        return offsets.Where(offset => offset > 0 && offset <= assetLength).Distinct().OrderBy(offset => offset).ToArray();
    }
}

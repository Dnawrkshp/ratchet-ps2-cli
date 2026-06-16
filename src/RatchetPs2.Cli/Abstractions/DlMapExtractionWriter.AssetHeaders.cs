using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static IReadOnlyList<HeaderPayloadRoute> WriteAssetHeaderArtifacts(
        string outputDirectory,
        byte[] headerBytes,
        DlAssetHeader header)
    {
        var routes = new List<HeaderPayloadRoute>
        {
            WriteHeaderRange(outputDirectory, "asset_header.bin", "header/fixed.bin", headerBytes, 0, AssetHeaderFixedLength)
        };

        AddHeaderTableRoute(
            routes,
            outputDirectory,
            "header/tables/mipmaps.bin",
            headerBytes,
            header.GsRamOffset,
            Math.Max(0, header.GsRamCount + header.ExtraMipmapCount),
            AssetMipmapDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/moby_models.bin", headerBytes, header.MobyModelOffset, header.MobyModelCount, AssetModelDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/tie_models.bin", headerBytes, header.TieModelOffset, header.TieModelCount, AssetModelDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/shrub_models.bin", headerBytes, header.ShrubModelOffset, header.ShrubModelCount, AssetShrubDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/tfrag_textures.bin", headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount, AssetTextureDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/moby_textures.bin", headerBytes, header.MobyTextureOffset, header.MobyTextureCount, AssetTextureDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/tie_textures.bin", headerBytes, header.TieTextureOffset, header.TieTextureCount, AssetTextureDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/shrub_textures.bin", headerBytes, header.ShrubTextureOffset, header.ShrubTextureCount, AssetTextureDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/particle_textures.bin", headerBytes, header.ParticleTextureDefOffset, header.ParticleTextureCount, AssetParticleTextureDefinitionLength);
        AddHeaderTableRoute(routes, outputDirectory, "header/tables/fx_textures.bin", headerBytes, header.FxTextureDefOffset, header.FxTextureCount, AssetFxTextureDefinitionLength);

        return routes;
    }

    private static void AddHeaderTableRoute(
        ICollection<HeaderPayloadRoute> routes,
        string outputDirectory,
        string relativePath,
        byte[] headerBytes,
        int offset,
        int count,
        int recordLength)
    {
        var length = count <= 0
            ? 0
            : checked(count * recordLength);
        routes.Add(WriteHeaderRange(outputDirectory, "asset_header.bin", relativePath, headerBytes, offset, length));
    }

    private static IReadOnlyList<HeaderPayloadRoute> ExtractLooseAssetBlocks(
        string outputDirectory,
        DlAssetHeader header,
        byte[] headerBytes,
        byte[] assetBytes,
        IReadOnlyList<int> knownAssetOffsets)
    {
        WriteAssetBlock(outputDirectory, SkyboxSourcePath, assetBytes, header.SkyOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "occlusion.bin", assetBytes, header.OcclusionOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "collision.bin", assetBytes, header.CollisionOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "light_cuboids.bin", assetBytes, header.LightCuboidsOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "heightmap.bin", assetBytes, header.HeightmapOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "occlusion_octree.bin", assetBytes, header.OcclusionOctreeOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "occlusion_radius.bin", assetBytes, header.OcclusionRadiusOffset, knownAssetOffsets);
        WriteAssetBlock(outputDirectory, "occlusion_radius2.bin", assetBytes, header.OcclusionRadius2Offset, knownAssetOffsets);

        return
        [
            WriteHeaderBlock(outputDirectory, "particle_def.bin", "particle/definitions.bin", headerBytes, header.ParticleDefOffset),
            WriteHeaderBlock(outputDirectory, "sound_remap.bin", "sound/remap.bin", headerBytes, header.SoundRemapOffset),
            WriteHeaderBlock(outputDirectory, "moby_sound_remap.bin", "moby/sound_remap.bin", headerBytes, header.MobySoundRemapOffset),
            WriteHeaderBlock(outputDirectory, "moby_gs_stash_list.bin", "moby/gs_stash_list.bin", headerBytes, header.MobyGsStashListOffset)
        ];
    }

    private static HeaderPayloadRoute WriteHeaderRange(
        string outputDirectory,
        string sourcePath,
        string relativePath,
        byte[] headerBytes,
        int offset,
        int length)
    {
        var outputPath = CombineRelativePath(outputDirectory, relativePath);
        if (offset < 0 || length <= 0 || offset >= headerBytes.Length)
        {
            DeleteIfExists(outputPath);
            return new HeaderPayloadRoute(sourcePath, relativePath, offset, Math.Max(0, length), "empty");
        }

        if ((long)offset + length > headerBytes.Length)
        {
            DeleteIfExists(outputPath);
            return new HeaderPayloadRoute(sourcePath, relativePath, offset, length, "out_of_range");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, headerBytes.AsSpan(offset, length).ToArray());
        return new HeaderPayloadRoute(sourcePath, relativePath, offset, length, "written");
    }

    private static HeaderPayloadRoute WriteHeaderBlock(
        string outputDirectory,
        string sourcePath,
        string relativePath,
        byte[] headerBytes,
        int offset)
    {
        var outputPath = CombineRelativePath(outputDirectory, relativePath);
        if (offset <= 0 || offset >= headerBytes.Length)
        {
            DeleteIfExists(outputPath);
            return new HeaderPayloadRoute(sourcePath, relativePath, offset, 0, "empty");
        }

        var nextOffset = BitConverter.ToInt32(headerBytes, offset);
        var length = nextOffset > 0 && nextOffset < headerBytes.Length - offset
            ? nextOffset
            : headerBytes.Length - offset;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, headerBytes.AsSpan(offset, length).ToArray());
        return new HeaderPayloadRoute(sourcePath, relativePath, offset, length, "written");
    }
}

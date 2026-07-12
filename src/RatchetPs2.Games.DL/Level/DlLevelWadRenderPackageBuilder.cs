using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.DL.Level;

public sealed record DlLevelWadRenderPackageBuildOptions
{
    public static DlLevelWadRenderPackageBuildOptions Default { get; } = new();
    public static DlLevelWadRenderPackageBuildOptions Browser { get; } = new()
    {
        IncludeSourceFiles = false,
        IncludeDiagnostics = false,
        MinifyGltf = true,
        GltfMetadataMode = GltfExportMetadataMode.RuntimeOnly,
        TfragLodIndex = 0,
        MobyLodIndex = 0
    };

    public bool IncludeSourceFiles { get; init; } = true;
    public bool IncludeDiagnostics { get; init; } = true;
    public bool MinifyGltf { get; init; }
    public GltfExportMetadataMode GltfMetadataMode { get; init; } = GltfExportMetadataMode.Full;
    public int? TfragLodIndex { get; init; }
    public int? MobyLodIndex { get; init; }
}

public static class DlLevelWadRenderPackageBuilder
{
    private const string SkyboxSourcePath = "skybox/sky.bin";
    private const string SkyboxGltfPath = "skybox/skybox.gltf";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static PackedFilePackage BuildPacked(
        ReadOnlySpan<byte> levelWadBytes,
        DlLevelWadRenderPackageBuildOptions? options = null)
    {
        options ??= DlLevelWadRenderPackageBuildOptions.Default;
        var totalStart = Stopwatch.GetTimestamp();
        var timings = new List<RenderPackageTiming>();
        var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
        var coreLevelBytes = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data);
        if (coreLevelBytes.Length == 0)
        {
            throw new InvalidDataException("DL level WAD does not contain a core level payload.");
        }

        var files = new List<PackedFile>();
        var coreSegmentStart = Stopwatch.GetTimestamp();
        var coreSegments = DlCoreLevelSegmentReader.Read(coreLevelBytes);
        AddTiming(
            timings,
            "managed.core-segments",
            "Core segment decompression",
            coreSegmentStart,
            $"{coreSegments.Count} segments");
        var coreSegmentByHeaderOffset = coreSegments.ToDictionary(segment => segment.HeaderOffset);

        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = "DL",
            ["Source"] = "loose_level_wad",
            ["RenderPackageVersion"] = 1,
            ["Level"] = levelWad.Level,
            ["LevelWad"] = levelWad,
            ["CoreLevelLength"] = coreLevelBytes.Length,
            ["CoreLevelSegmentTableLength"] = DlLevelConstants.CoreLevelSegmentTableLength,
            ["CoreSegments"] = CreateCoreSegmentManifest(coreSegments)
        };

        if (!coreSegmentByHeaderOffset.TryGetValue(0x10, out var assetHeader)
            || !coreSegmentByHeaderOffset.TryGetValue(0x18, out var palette)
            || !coreSegmentByHeaderOffset.TryGetValue(0x50, out var assetWad))
        {
            throw new InvalidDataException("DL level WAD is missing one or more required asset core segments.");
        }

        var assetsStart = Stopwatch.GetTimestamp();
        BuildAssets(
            files,
            GameId.DL,
            levelWad.Level,
            assetHeader.PayloadBytes,
            palette.PayloadBytes,
            assetWad.PayloadBytes,
            manifest,
            timings,
            options,
            ReadChunkWads(levelWadBytes, levelWad.Chunks));
        AddTiming(
            timings,
            "managed.assets-total",
            "Asset package build",
            assetsStart,
            $"{files.Count} files so far");

        if (coreSegmentByHeaderOffset.TryGetValue(0x58, out var worldInstances))
        {
            var worldStart = Stopwatch.GetTimestamp();
            BuildWorldInstances(files, worldInstances.PayloadBytes, manifest);
            AddTiming(
                timings,
                "managed.world",
                "World sidecar build",
                worldStart,
                $"{files.Count} files so far");
        }
        else
        {
            throw new InvalidDataException("DL level WAD is missing the world instance core segment.");
        }

        AddTiming(
            timings,
            "managed.before-pack",
            "Managed build before pack",
            totalStart,
            $"{files.Count} files");
        manifest["PerformanceTimings"] = timings;
        AddJsonFile(files, "manifest.json", manifest);
        return PackFiles(files);
    }

    public static IReadOnlyList<PackedFile> BuildAssetFiles(
        GameId gameId,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetBytes,
        DlLevelWadRenderPackageBuildOptions? options = null,
        IReadOnlyDictionary<int, byte[]>? chunkWads = null)
    {
        ArgumentNullException.ThrowIfNull(headerBytes);
        ArgumentNullException.ThrowIfNull(paletteBytes);
        ArgumentNullException.ThrowIfNull(assetBytes);

        options ??= DlLevelWadRenderPackageBuildOptions.Default;
        var assetWadWasCompressed = BinaryMagic.IsWad(assetBytes);
        var assetPayloadBytes = assetWadWasCompressed
            ? WadCompression.Decompress(assetBytes)
            : assetBytes;
        var files = new List<PackedFile>();
        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = gameId.ToString(),
            ["Source"] = "loose_asset_files",
            ["RenderPackageVersion"] = 1,
            ["Level"] = levelIndex,
            ["AssetWadWasCompressed"] = assetWadWasCompressed,
            ["AssetWadRawLength"] = assetBytes.Length,
            ["AssetWadPayloadLength"] = assetPayloadBytes.Length
        };
        var timings = new List<RenderPackageTiming>();
        var assetsStart = Stopwatch.GetTimestamp();

        BuildAssets(
            files,
            gameId,
            levelIndex,
            headerBytes,
            paletteBytes,
            assetPayloadBytes,
            manifest,
            timings,
            options,
            chunkWads);

        AddTiming(
            timings,
            "managed.assets-total",
            "Asset package build",
            assetsStart,
            $"{files.Count} files");
        manifest["PerformanceTimings"] = timings;
        AddJsonFile(files, "assets/render_manifest.json", manifest);
        return files;
    }

    private static void BuildAssets(
        List<PackedFile> files,
        GameId gameId,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetBytes,
        IDictionary<string, object?> rootManifest,
        List<RenderPackageTiming> timings,
        DlLevelWadRenderPackageBuildOptions options,
        IReadOnlyDictionary<int, byte[]>? chunkWads)
    {
        var header = DlAssetReader.ReadHeader(headerBytes);
        var allMipmapDefinitions = DlAssetReader.ReadMipmapDefinitions(
            headerBytes,
            header.GsRamOffset,
            Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStashDefinitions = allMipmapDefinitions.Skip(header.GsRamCount).ToArray();
        var mobyDefinitions = DlAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var tieDefinitions = DlAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubDefinitions = DlAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var tfragTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount);
        var mobyTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var fxDefinitions = DlAssetReader.ReadFxTextureDefinitions(headerBytes, header.FxTextureDefOffset, header.FxTextureCount);
        var textureIsSwizzled = ShouldSwizzleAssetTextures(gameId);
        var knownAssetOffsets = CollectKnownAssetOffsets(
            header,
            assetBytes.Length,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions);
        var gltfExports = new List<GltfExportRoute>();

        var skyboxStart = Stopwatch.GetTimestamp();
        gltfExports.Add(BuildSkybox(files, gameId, levelIndex, header, assetBytes, knownAssetOffsets, options));
        AddTiming(
            timings,
            "managed.assets.skybox",
            "Skybox glTF export",
            skyboxStart,
            SummarizeRoutes(gltfExports, route => route.Family == "skybox"));

        var tfragStart = Stopwatch.GetTimestamp();
        var tfragTimings = new List<RenderPackageTiming>();
        var tfragTextureResources = BuildTfragTextureResources(
            files,
            header,
            tfragTextureDefinitions,
            paletteBytes,
            assetBytes,
            textureIsSwizzled);
        gltfExports.Add(BuildTfrag(
            files,
            gameId,
            null,
            ReadAssetRange(
                assetBytes,
                header.TerrainOffset,
                header.OcclusionOffset,
                allowZeroOffset: true),
            "tfrag/tfrag.bin",
            "tfrag/tfrag.gltf",
            "tfrag/tfrag.buffer.bin",
            "tfrag/tfrag.diagnostics.json",
            "assets/tfrag",
            tfragTextureResources,
            tfragTimings,
            options));
        gltfExports.AddRange(BuildChunkTfrags(
            files,
            gameId,
            chunkWads,
            tfragTextureResources,
            tfragTimings,
            options));
        AddTiming(
            timings,
            "managed.assets.tfrag",
            "Terrain glTF export",
            tfragStart,
            SummarizeRoutes(gltfExports, route => route.Family == "tfrag"));
        timings.AddRange(tfragTimings);

        var mobyStart = Stopwatch.GetTimestamp();
        var mobyRouteStart = gltfExports.Count;
        gltfExports.AddRange(BuildMobyGltfs(
            files,
            gameId,
            mobyDefinitions,
            mobyTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            gsStashDefinitions,
            knownAssetOffsets,
            textureIsSwizzled,
            options));
        AddTiming(
            timings,
            "managed.assets.mobys",
            "Moby glTF exports",
            mobyStart,
            SummarizeRoutes(gltfExports.Skip(mobyRouteStart)));

        var tieStart = Stopwatch.GetTimestamp();
        var tieRouteStart = gltfExports.Count;
        var tieTimingAggregates = new Dictionary<string, TimingAggregate>(StringComparer.Ordinal);
        gltfExports.AddRange(BuildTieGltfs(
            files,
            gameId,
            tieDefinitions,
            tieTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            knownAssetOffsets,
            (key, label, durationMs, detail) => AddAggregateTiming(
                tieTimingAggregates,
                $"managed.{key}",
                label,
                durationMs,
                detail),
            textureIsSwizzled,
            options));
        AddTiming(
            timings,
            "managed.assets.ties",
            "Tie glTF exports",
            tieStart,
            SummarizeRoutes(gltfExports.Skip(tieRouteStart)));
        FlushAggregateTimings(timings, tieTimingAggregates.Values);

        var shrubStart = Stopwatch.GetTimestamp();
        var shrubRouteStart = gltfExports.Count;
        gltfExports.AddRange(BuildShrubGltfs(
            files,
            gameId,
            shrubDefinitions,
            shrubTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            knownAssetOffsets,
            textureIsSwizzled,
            options));
        AddTiming(
            timings,
            "managed.assets.shrubs",
            "Shrub glTF exports",
            shrubStart,
            SummarizeRoutes(gltfExports.Skip(shrubRouteStart)));

        var fxStart = Stopwatch.GetTimestamp();
        BuildFxTextures(files, fxDefinitions, assetBytes, header.FxTextureDataOffset, textureIsSwizzled);
        AddTiming(
            timings,
            "managed.assets.fx-textures",
            "FX texture exports",
            fxStart,
            $"{fxDefinitions.Count} textures");

        var assetManifest = new Dictionary<string, object?>
        {
            ["Game"] = gameId.ToString(),
            ["TextureIsSwizzled"] = textureIsSwizzled,
            ["Header"] = header,
            ["HeaderLength"] = headerBytes.Length,
            ["HeaderTables"] = new
            {
                MipmapDefinitions = allMipmapDefinitions,
                MobyDefinitions = mobyDefinitions,
                TieDefinitions = tieDefinitions,
                ShrubDefinitions = shrubDefinitions,
                TfragTextureDefinitions = tfragTextureDefinitions,
                MobyTextureDefinitions = mobyTextureDefinitions,
                TieTextureDefinitions = tieTextureDefinitions,
                ShrubTextureDefinitions = shrubTextureDefinitions,
                FxTextureDefinitions = fxDefinitions
            },
            ["GltfExports"] = gltfExports,
            ["GltfExportCount"] = gltfExports.Count(export => export.Status == "written"),
            ["GltfExportFailureCount"] = gltfExports.Count(export => export.Status == "error")
        };

        AddJsonFile(files, "assets/manifest.json", assetManifest);
        rootManifest["AssetHeader"] = header;
        rootManifest["TextureIsSwizzled"] = textureIsSwizzled;
    }

    private static GltfExportRoute BuildTfrag(
        List<PackedFile> files,
        GameId gameId,
        int? modelId,
        byte[] tfragBytes,
        string sourcePath,
        string gltfPath,
        string bufferPath,
        string diagnosticsPath,
        string packageRoot,
        RenderTextureResources textureResources,
        List<RenderPackageTiming> timings,
        DlLevelWadRenderPackageBuildOptions options)
    {
        if (options.IncludeSourceFiles)
        {
            AddFile(files, $"{packageRoot}/tfrag.bin", tfragBytes);
        }
        if (tfragBytes.Length == 0)
        {
            return GltfExportRoute.Empty("tfrag", modelId, sourcePath, gltfPath);
        }

        try
        {
            using var input = new MemoryStream(tfragBytes, writable: false);
            var export = TfragGltfExporter.Export(
                input,
                Path.GetFileName(gltfPath),
                new TfragGltfExportOptions
                {
                    BufferFileName = Path.GetFileName(bufferPath),
                    GameLabel = gameId.ToString(),
                    ExternalTextureUris = textureResources.Uris,
                    ExternalTextureSizes = textureResources.Sizes,
                    ExternalTextureAlpha = textureResources.Alpha,
                    IncludeDiagnostics = options.IncludeDiagnostics,
                    Minify = options.MinifyGltf,
                    MetadataMode = options.GltfMetadataMode,
                    LodIndex = options.TfragLodIndex,
                    TimingSink = (key, label, durationMs, detail) => AddTiming(
                        timings,
                        $"managed.{key}",
                        label,
                        durationMs,
                        detail)
                });

            AddFile(files, $"assets/{gltfPath}", export.GltfBytes, "model/gltf+json");
            AddFile(files, $"assets/{bufferPath}", export.BinBytes);
            AddOptionalDiagnostics(files, $"assets/{diagnosticsPath}", export.DiagnosticsBytes, options);
            return GltfExportRoute.Written(
                "tfrag",
                modelId,
                sourcePath,
                gltfPath,
                bufferPath,
                options.IncludeDiagnostics ? diagnosticsPath : null);
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("tfrag", modelId, sourcePath, gltfPath, ex.Message);
        }
    }

    private static RenderTextureResources BuildTfragTextureResources(
        List<PackedFile> files,
        DlAssetHeader header,
        IReadOnlyList<DlAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        bool textureIsSwizzled)
    {
        var textureResources = new RenderTextureResources();
        foreach (var definition in textureDefinitions)
        {
            var texture = DlAssetReader.BuildAssetTexture(
                "tfrag",
                definition.Index,
                definition,
                paletteBytes,
                assetBytes,
                header.TextureDataOffset,
                isSwizzled: textureIsSwizzled);
            AddTexture(files, "assets/tfrag/textures", "textures", texture, textureResources, TfragTextureAlpha.FullOpacityAlpha);
        }

        return textureResources;
    }

    private static IEnumerable<GltfExportRoute> BuildChunkTfrags(
        List<PackedFile> files,
        GameId gameId,
        IReadOnlyDictionary<int, byte[]>? chunkWads,
        RenderTextureResources textureResources,
        List<RenderPackageTiming> timings,
        DlLevelWadRenderPackageBuildOptions options)
    {
        if (chunkWads is null || chunkWads.Count == 0)
        {
            yield break;
        }

        foreach (var (chunkIndex, chunkBytes) in chunkWads.OrderBy(entry => entry.Key))
        {
            if (chunkIndex == 0)
            {
                continue;
            }

            var relativeDirectory = $"tfrag/chunks/chunk{chunkIndex}";
            var sourcePath = $"{relativeDirectory}/tfrag.bin";
            var gltfPath = $"{relativeDirectory}/tfrag.gltf";
            byte[] tfragBytes;
            GltfExportRoute? failedRoute = null;
            try
            {
                tfragBytes = TfragChunkWadReader.ReadTerrainPayload(chunkBytes);
            }
            catch (Exception ex) when (IsGltfExportFailure(ex) || ex is OverflowException)
            {
                failedRoute = GltfExportRoute.Failed("tfrag", chunkIndex, sourcePath, gltfPath, ex.Message);
                tfragBytes = [];
            }

            if (failedRoute is not null)
            {
                yield return failedRoute;
                continue;
            }

            yield return BuildTfrag(
                files,
                gameId,
                chunkIndex,
                tfragBytes,
                sourcePath,
                gltfPath,
                $"{relativeDirectory}/tfrag.buffer.bin",
                $"{relativeDirectory}/tfrag.diagnostics.json",
                $"assets/{relativeDirectory}",
                textureResources.Rebased("../../textures"),
                timings,
                options);
        }
    }

    private static GltfExportRoute BuildSkybox(
        List<PackedFile> files,
        GameId gameId,
        int levelIndex,
        DlAssetHeader header,
        byte[] assetBytes,
        IReadOnlyList<int> knownAssetOffsets,
        DlLevelWadRenderPackageBuildOptions options)
    {
        const string packageRoot = "assets/skybox";
        var skyboxBytes = DlAssetReader.ReadAssetSlice(assetBytes, header.SkyOffset, knownAssetOffsets);
        if (options.IncludeSourceFiles)
        {
            AddFile(files, $"{packageRoot}/sky.bin", skyboxBytes);
        }
        if (skyboxBytes.Length == 0)
        {
            return GltfExportRoute.Empty("skybox", null, SkyboxSourcePath, SkyboxGltfPath);
        }

        try
        {
            using var input = new MemoryStream(skyboxBytes, writable: false);
            var skybox = SkyboxReader.Read(input);
            var profile = SkyboxGameProfile.ForGame(gameId);
            var export = SkyboxGltfExporter.Export(
                skybox,
                "skybox.gltf",
                profile.CreateExportOptions(
                    "skybox.buffer.bin",
                    levelIndex,
                    skybox.Shells.Count,
                    includeDiagnostics: options.IncludeDiagnostics,
                    minify: options.MinifyGltf,
                    metadataMode: options.GltfMetadataMode));

            AddFile(files, $"{packageRoot}/skybox.gltf", export.GltfBytes, "model/gltf+json");
            AddFile(files, $"{packageRoot}/skybox.buffer.bin", export.BinBytes);
            AddOptionalDiagnostics(files, $"{packageRoot}/skybox.diagnostics.json", export.DiagnosticsBytes, options);
            foreach (var texture in export.Textures)
            {
                AddFile(files, $"{packageRoot}/textures/{texture.FileName}", texture.PngBytes, "image/png");
            }

            return GltfExportRoute.Written(
                "skybox",
                null,
                SkyboxSourcePath,
                SkyboxGltfPath,
                "skybox/skybox.buffer.bin",
                options.IncludeDiagnostics ? "skybox/skybox.diagnostics.json" : null);
        }
        catch (Exception ex) when (IsGltfExportFailure(ex))
        {
            return GltfExportRoute.Failed("skybox", null, SkyboxSourcePath, SkyboxGltfPath, ex.Message);
        }
    }

    private static IEnumerable<GltfExportRoute> BuildMobyGltfs(
        List<PackedFile> files,
        GameId gameId,
        IReadOnlyList<DlAssetModelDefinition> modelDefinitions,
        IReadOnlyList<DlAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<DlAssetMipmapDefinition> gsStashDefinitions,
        IReadOnlyList<int> knownAssetOffsets,
        bool textureIsSwizzled,
        DlLevelWadRenderPackageBuildOptions options)
    {
        foreach (var definition in modelDefinitions)
        {
            var folderName = DlAssetReader.GetAssetFolderName(definition.ModelId);
            var relativeDirectory = $"moby/{folderName}";
            var sourcePath = $"{relativeDirectory}/moby.bin";
            var gltfPath = $"{relativeDirectory}/moby.gltf";
            var packageRoot = $"assets/{relativeDirectory}";
            var mobyBytes = DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownAssetOffsets);
            if (options.IncludeSourceFiles)
            {
                AddFile(files, $"{packageRoot}/moby.bin", mobyBytes);
                AddJsonFile(files, $"{packageRoot}/moby.json", definition);
            }

            if (mobyBytes.Length == 0)
            {
                yield return GltfExportRoute.Empty("moby", definition.ModelId, sourcePath, gltfPath);
                continue;
            }

            GltfExportRoute route;
            try
            {
                var textureResources = new RenderTextureResources();
                var relativeTextureIndex = 0;
                foreach (var textureId in definition.TextureIds)
                {
                    if (textureId == 0xff || textureId >= textureDefinitions.Count)
                    {
                        continue;
                    }

                    var texture = DlAssetReader.BuildAssetTexture(
                        "moby",
                        relativeTextureIndex,
                        textureDefinitions[textureId],
                        paletteBytes,
                        assetBytes,
                        textureDataOffset,
                        gsStashDefinitions,
                        isSwizzled: textureIsSwizzled);
                    AddTexture(files, $"{packageRoot}/textures", "textures", texture, textureResources);
                    relativeTextureIndex++;
                }

                using var input = new MemoryStream(mobyBytes, writable: false);
                var export = MobyGltfExporter.Export(
                    input,
                    "moby.gltf",
                    new MobyGltfExportOptions
                    {
                        SkipAnimationSequences = true,
                        AnimationFormat = GetMobyAnimationFormat(gameId),
                        LodIndex = options.MobyLodIndex,
                        ExternalTextureUris = textureResources.Uris,
                        ExternalTextureSizes = textureResources.Sizes,
                        ExternalTextureAlpha = textureResources.Alpha,
                        BufferFileName = "moby.buffer.bin"
                    });

                AddFile(files, $"{packageRoot}/moby.gltf", export.GltfBytes, "model/gltf+json");
                AddFile(files, $"{packageRoot}/moby.buffer.bin", export.BinBytes);
                AddOptionalDiagnostics(files, $"{packageRoot}/moby.diagnostics.json", export.DiagnosticsBytes, options);
                route = GltfExportRoute.Written(
                    "moby",
                    definition.ModelId,
                    sourcePath,
                    gltfPath,
                    $"{relativeDirectory}/moby.buffer.bin",
                    options.IncludeDiagnostics ? $"{relativeDirectory}/moby.diagnostics.json" : null);
            }
            catch (Exception ex) when (IsAssetTextureFailure(ex))
            {
                route = GltfExportRoute.Failed("moby", definition.ModelId, sourcePath, gltfPath, ex.Message);
            }

            yield return route;
        }
    }

    private static IEnumerable<GltfExportRoute> BuildTieGltfs(
        List<PackedFile> files,
        GameId gameId,
        IReadOnlyList<DlAssetModelDefinition> modelDefinitions,
        IReadOnlyList<DlAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<int> knownAssetOffsets,
        Action<string, string, double, string?>? timingSink,
        bool textureIsSwizzled,
        DlLevelWadRenderPackageBuildOptions options)
    {
        foreach (var definition in modelDefinitions)
        {
            var folderName = DlAssetReader.GetAssetFolderName(definition.ModelId);
            var relativeDirectory = $"tie/{folderName}";
            var sourcePath = $"{relativeDirectory}/tie.bin";
            var gltfPath = $"{relativeDirectory}/tie.gltf";
            var packageRoot = $"assets/{relativeDirectory}";
            var tieBytes = DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownAssetOffsets);
            if (options.IncludeSourceFiles)
            {
                AddFile(files, $"{packageRoot}/tie.bin", tieBytes);
                AddJsonFile(files, $"{packageRoot}/tie.json", definition);
            }

            if (tieBytes.Length == 0)
            {
                yield return GltfExportRoute.Empty("tie", definition.ModelId, sourcePath, gltfPath);
                continue;
            }

            var textureResources = new RenderTextureResources();
            var relativeTextureIndex = 0;
            foreach (var textureId in definition.TextureIds)
            {
                if (textureId == 0xff || textureId >= textureDefinitions.Count)
                {
                    continue;
                }

                var texture = DlAssetReader.BuildAssetTexture(
                    "tie",
                    relativeTextureIndex,
                    textureDefinitions[textureId],
                    paletteBytes,
                    assetBytes,
                    textureDataOffset,
                    isSwizzled: textureIsSwizzled);
                AddTexture(files, $"{packageRoot}/textures", "textures", texture, textureResources);
                relativeTextureIndex++;
            }

            GltfExportRoute route;
            try
            {
                using var input = new MemoryStream(tieBytes, writable: false);
                var export = TieGltfExporter.Export(
                    input,
                    "tie.gltf",
                    new TieGltfExportOptions
                    {
                        LodIndex = 0,
                        BufferFileName = "tie.buffer.bin",
                        GameProfile = TieGameProfile.ForGame(gameId),
                        ExternalTextureUris = textureResources.Uris,
                        ExternalTextureSizes = textureResources.Sizes,
                        ExternalTextureAlpha = textureResources.Alpha,
                        IncludeDiagnostics = options.IncludeDiagnostics,
                        Minify = options.MinifyGltf,
                        MetadataMode = options.GltfMetadataMode,
                        TimingSink = timingSink
                    });

                AddFile(files, $"{packageRoot}/tie.gltf", export.GltfBytes, "model/gltf+json");
                AddFile(files, $"{packageRoot}/tie.buffer.bin", export.BinBytes);
                AddOptionalDiagnostics(files, $"{packageRoot}/tie.diagnostics.json", export.DiagnosticsBytes, options);
                route = GltfExportRoute.Written(
                    "tie",
                    definition.ModelId,
                    sourcePath,
                    gltfPath,
                    $"{relativeDirectory}/tie.buffer.bin",
                    options.IncludeDiagnostics ? $"{relativeDirectory}/tie.diagnostics.json" : null);
            }
            catch (Exception ex) when (IsGltfExportFailure(ex))
            {
                route = GltfExportRoute.Failed("tie", definition.ModelId, sourcePath, gltfPath, ex.Message);
            }

            yield return route;
        }
    }

    private static IEnumerable<GltfExportRoute> BuildShrubGltfs(
        List<PackedFile> files,
        GameId gameId,
        IReadOnlyList<DlAssetShrubDefinition> shrubDefinitions,
        IReadOnlyList<DlAssetTextureDefinition> textureDefinitions,
        byte[] paletteBytes,
        byte[] assetBytes,
        int textureDataOffset,
        IReadOnlyList<int> knownAssetOffsets,
        bool textureIsSwizzled,
        DlLevelWadRenderPackageBuildOptions options)
    {
        foreach (var definition in shrubDefinitions)
        {
            var folderName = DlAssetReader.GetAssetFolderName(definition.ModelId);
            var relativeDirectory = $"shrub/{folderName}";
            var sourcePath = $"{relativeDirectory}/shrub.bin";
            var gltfPath = $"{relativeDirectory}/shrub.gltf";
            var packageRoot = $"assets/{relativeDirectory}";
            var shrubBytes = DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownAssetOffsets);
            if (options.IncludeSourceFiles)
            {
                AddFile(files, $"{packageRoot}/shrub.bin", shrubBytes);
                AddJsonFile(files, $"{packageRoot}/shrub.json", definition);
            }

            if (shrubBytes.Length == 0)
            {
                yield return GltfExportRoute.Empty("shrub", definition.ModelId, sourcePath, gltfPath);
                continue;
            }

            var textureResources = new RenderTextureResources();
            var relativeTextureIndex = 0;
            foreach (var textureId in definition.TextureIds)
            {
                if (textureId == 0xff || textureId >= textureDefinitions.Count)
                {
                    continue;
                }

                var texture = DlAssetReader.BuildAssetTexture(
                    "shrub",
                    relativeTextureIndex,
                    textureDefinitions[textureId],
                    paletteBytes,
                    assetBytes,
                    textureDataOffset,
                    isSwizzled: textureIsSwizzled);
                AddTexture(files, $"{packageRoot}/textures", "textures", texture, textureResources);
                relativeTextureIndex++;
            }

            RenderTextureResource? billboard = null;
            if (definition.Width > 0 && definition.Height > 0 && definition.TextureId > 0)
            {
                billboard = AddTexture(
                    files,
                    $"{packageRoot}/textures",
                    "textures",
                    DlAssetReader.BuildShrubBillboardTexture(definition, paletteBytes),
                    null,
                    outputFileName: "billboard.png");
            }

            GltfExportRoute route;
            try
            {
                using var input = new MemoryStream(shrubBytes, writable: false);
                var export = ShrubGltfExporter.Export(
                    input,
                    "shrub.gltf",
                    new ShrubGltfExportOptions
                    {
                        BufferFileName = "shrub.buffer.bin",
                        GameLabel = gameId.ToString(),
                        ExternalTextureUris = textureResources.Uris,
                        ExternalTextureSizes = textureResources.Sizes,
                        ExternalTextureAlpha = textureResources.Alpha,
                        ExternalBillboardTextureUri = billboard?.Uri,
                        ExternalBillboardTextureSize = billboard?.Size,
                        ExternalBillboardTextureAlpha = billboard?.Alpha,
                        IncludeDiagnostics = options.IncludeDiagnostics,
                        Minify = options.MinifyGltf,
                        MetadataMode = options.GltfMetadataMode
                    });

                AddFile(files, $"{packageRoot}/shrub.gltf", export.GltfBytes, "model/gltf+json");
                AddFile(files, $"{packageRoot}/shrub.buffer.bin", export.BinBytes);
                AddOptionalDiagnostics(files, $"{packageRoot}/shrub.diagnostics.json", export.DiagnosticsBytes, options);
                route = GltfExportRoute.Written(
                    "shrub",
                    definition.ModelId,
                    sourcePath,
                    gltfPath,
                    $"{relativeDirectory}/shrub.buffer.bin",
                    options.IncludeDiagnostics ? $"{relativeDirectory}/shrub.diagnostics.json" : null);
            }
            catch (Exception ex) when (IsGltfExportFailure(ex))
            {
                route = GltfExportRoute.Failed("shrub", definition.ModelId, sourcePath, gltfPath, ex.Message);
            }

            yield return route;
        }
    }

    private static void BuildFxTextures(
        List<PackedFile> files,
        IReadOnlyList<DlFxTextureDefinition> fxDefinitions,
        byte[] assetBytes,
        int fxTextureDataOffset,
        bool textureIsSwizzled)
    {
        var textures = new List<object>(fxDefinitions.Count);
        var errors = new List<object>();
        foreach (var definition in fxDefinitions)
        {
            try
            {
                var texture = DlAssetReader.BuildFxTexture(
                    definition,
                    assetBytes,
                    fxTextureDataOffset,
                    isSwizzled: textureIsSwizzled);
                AddTexture(files, "assets/fx/textures", "textures", texture, null);
                textures.Add(new
                {
                    definition.Index,
                    Path = $"fx/textures/tex.{definition.Index:0000}.png",
                    definition.Width,
                    definition.Height,
                    definition.PaletteOffset,
                    definition.TextureOffset
                });
            }
            catch (Exception ex) when (IsAssetTextureFailure(ex))
            {
                errors.Add(new
                {
                    definition.Index,
                    definition.Width,
                    definition.Height,
                    definition.PaletteOffset,
                    definition.TextureOffset,
                    Error = ex.Message
                });
            }
        }

        AddJsonFile(files, "assets/fx/manifest.json", new
        {
            TextureCount = fxDefinitions.Count,
            WrittenTextureCount = textures.Count,
            ErrorCount = errors.Count,
            Textures = textures,
            Errors = errors
        });
    }

    private static void BuildWorldInstances(
        List<PackedFile> files,
        byte[] worldBytes,
        IDictionary<string, object?> rootManifest)
    {
        var world = DlWorldInstanceReader.Read(worldBytes);
        var slotRoutes = new List<WorldSlotRoute>(world.Slots.Count);

        foreach (var slot in world.Slots)
        {
            if (slot.PayloadBytes.Length == 0)
            {
                slotRoutes.Add(CreateWorldSlotRoute(slot, null, "empty"));
                continue;
            }

            var relativePath = GetWorldSlotRelativePath(slot);
            AddFile(files, $"world/{relativePath}", slot.PayloadBytes);
            slotRoutes.Add(CreateWorldSlotRoute(slot, relativePath, IsKnownWorldSlot(slot.HeaderOffset) ? "mapped" : "unknown"));
        }

        var worldManifest = new Dictionary<string, object?>
        {
            ["Length"] = world.Length,
            ["PointerTableLength"] = DlWorldInstanceReader.PointerTableLength,
            ["Slots"] = slotRoutes,
            ["DirectionalLightCount"] = world.DirectionalLights?.Count ?? 0,
            ["TieClassCount"] = world.TieClasses?.Count ?? 0,
            ["TieInstanceCount"] = world.TieInstances?.Count ?? 0,
            ["ShrubClassCount"] = world.ShrubClasses?.Count ?? 0,
            ["ShrubInstanceCount"] = world.ShrubInstances?.Count ?? 0,
            ["OcclusionMapping"] = world.OcclusionMapping
        };

        AddJsonFile(files, "world/manifest.json", worldManifest);
        AddWorldChildManifests(files, world, slotRoutes);
        rootManifest["World"] = worldManifest;
    }

    private static void AddWorldChildManifests(
        List<PackedFile> files,
        DlWorldInstances world,
        IReadOnlyList<WorldSlotRoute> slotRoutes)
    {
        if (world.DirectionalLights is not null)
        {
            AddJsonFile(
                files,
                "world/lighting/manifest.json",
                new
                {
                    Path = FindWorldSlotPath(slotRoutes, 0x00),
                    world.DirectionalLights.Count,
                    world.DirectionalLights.RecordSize,
                    world.DirectionalLights.DataOffset,
                    world.DirectionalLights.IsLengthValid,
                    world.DirectionalLights.PaddingLength,
                    world.DirectionalLights.Records
                });
        }

        if (world.TieClasses is not null
            || world.TieInstances is not null
            || world.TieGroups is not null
            || world.TieInstanceColors is not null)
        {
            AddJsonFile(
                files,
                "world/tie/manifest.json",
                new
                {
                    ClassIdsPath = FindWorldSlotPath(slotRoutes, 0x04),
                    InstancesPath = FindWorldSlotPath(slotRoutes, 0x08),
                    GroupsPath = FindWorldSlotPath(slotRoutes, 0x0c),
                    ColorsPath = FindWorldSlotPath(slotRoutes, 0x20),
                    Classes = world.TieClasses,
                    Instances = world.TieInstances,
                    Groups = world.TieGroups,
                    Colors = world.TieInstanceColors
                });
        }

        if (world.ShrubClasses is not null || world.ShrubInstances is not null || world.ShrubGroups is not null)
        {
            AddJsonFile(
                files,
                "world/shrub/manifest.json",
                new
                {
                    ClassIdsPath = FindWorldSlotPath(slotRoutes, 0x10),
                    InstancesPath = FindWorldSlotPath(slotRoutes, 0x14),
                    GroupsPath = FindWorldSlotPath(slotRoutes, 0x18),
                    Classes = world.ShrubClasses,
                    Instances = world.ShrubInstances,
                    Groups = world.ShrubGroups
                });
        }

        if (world.OcclusionMapping is not null)
        {
            AddJsonFile(
                files,
                "world/occlusion/manifest.json",
                new
                {
                    MappingPath = FindWorldSlotPath(slotRoutes, 0x1c),
                    Mapping = world.OcclusionMapping
                });
        }
    }

    private static IReadOnlyList<object> CreateCoreSegmentManifest(IReadOnlyList<DlCoreLevelSegment> segments)
    {
        return segments.Select(segment => new
        {
            segment.Index,
            segment.HeaderOffset,
            segment.Offset,
            segment.Length,
            segment.Name,
            segment.SemanticName,
            segment.WasCompressedWad,
            segment.OutputExtension,
            RawLength = segment.RawBytes.Length,
            PayloadLength = segment.PayloadBytes.Length
        }).ToArray<object>();
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

    private static IReadOnlyDictionary<int, byte[]> ReadChunkWads(
        ReadOnlySpan<byte> levelWadBytes,
        IReadOnlyList<DlFileBlock> chunks)
    {
        var chunkWads = new Dictionary<int, byte[]>();
        for (var i = 1; i < chunks.Count; i++)
        {
            var bytes = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, chunks[i]);
            if (bytes.Length > 0)
            {
                chunkWads[i] = bytes;
            }
        }

        return chunkWads;
    }

    private static RenderTextureResource AddTexture(
        List<PackedFile> files,
        string packageDirectory,
        string gltfTextureDirectory,
        DlNormalizedTexture texture,
        RenderTextureResources? resources,
        byte? normalizePs2FullOpacityAlpha = null,
        string? outputFileName = null)
    {
        var fileName = outputFileName ?? $"tex.{texture.Index:0000}.png";
        var pngBytes = texture.PngBytes;
        var metadata = ReadPngMetadata(pngBytes);
        if (normalizePs2FullOpacityAlpha.HasValue
            && metadata.Alpha.HasAlpha
            && metadata.Alpha.MaxAlpha <= normalizePs2FullOpacityAlpha.Value)
        {
            using var input = new MemoryStream(pngBytes, writable: false);
            using var output = new MemoryStream();
            metadata = PngAlphaNormalizer.WriteWithPs2AlphaNormalized(input, output, normalizePs2FullOpacityAlpha.Value);
            pngBytes = output.ToArray();
        }

        AddFile(files, $"{packageDirectory}/{fileName}", pngBytes, "image/png");

        var uri = $"{gltfTextureDirectory.Trim().Trim('/')}/{fileName}";
        var resource = new RenderTextureResource(
            texture.Index,
            uri,
            new TextureSize(metadata.Size.Width, metadata.Size.Height),
            metadata.Alpha);
        resources?.Add(resource);
        return resource;
    }

    private static byte[] ReadAssetRange(
        byte[] assetBytes,
        int offset,
        int endOffset,
        bool allowZeroOffset = false)
    {
        if (offset < 0 || (offset == 0 && !allowZeroOffset) || offset >= assetBytes.Length)
        {
            return [];
        }

        var end = endOffset > offset && endOffset <= assetBytes.Length
            ? endOffset
            : assetBytes.Length;
        return assetBytes.AsSpan(offset, end - offset).ToArray();
    }

    private static TextureMetadata ReadPngMetadata(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        return PngTextureMetadataReader.ReadPng(input);
    }

    private static WorldSlotRoute CreateWorldSlotRoute(DlWorldInstanceSlot slot, string? relativePath, string status)
    {
        return new WorldSlotRoute(
            slot.Index,
            slot.HeaderOffset,
            slot.Pointer,
            slot.Length,
            slot.SemanticName,
            relativePath,
            status);
    }

    private static string? FindWorldSlotPath(IReadOnlyList<WorldSlotRoute> slotRoutes, int headerOffset)
    {
        return slotRoutes.FirstOrDefault(route => route.HeaderOffset == headerOffset)?.Path;
    }

    private static string GetWorldSlotRelativePath(DlWorldInstanceSlot slot)
    {
        return slot.HeaderOffset switch
        {
            0x00 => "lighting/directional_lights.bin",
            0x04 => "tie/class_ids.bin",
            0x08 => "tie/instances.bin",
            0x0c => "tie/groups.bin",
            0x10 => "shrub/class_ids.bin",
            0x14 => "shrub/instances.bin",
            0x18 => "shrub/groups.bin",
            0x1c => "occlusion/instance_mapping.bin",
            0x20 => "tie/colors.bin",
            _ => $"unknown/slot_{slot.HeaderOffset:X2}.bin"
        };
    }

    private static bool IsKnownWorldSlot(int headerOffset)
    {
        return headerOffset is 0x00 or 0x04 or 0x08 or 0x0c or 0x10 or 0x14 or 0x18 or 0x1c or 0x20;
    }

    private static MobyAnimationFormat GetMobyAnimationFormat(GameId gameId)
    {
        return gameId == GameId.DL
            ? MobyAnimationFormat.Compact
            : MobyAnimationFormat.Standard;
    }

    private static bool ShouldSwizzleAssetTextures(GameId gameId)
    {
        return gameId == GameId.DL;
    }

    private static bool IsGltfExportFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException;
    }

    private static bool IsAssetTextureFailure(Exception ex)
    {
        return IsGltfExportFailure(ex)
            || ex is OverflowException;
    }

    private static void AddJsonFile(List<PackedFile> files, string path, object value)
    {
        AddFile(files, path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json");
    }

    private static void AddOptionalDiagnostics(
        List<PackedFile> files,
        string path,
        byte[] bytes,
        DlLevelWadRenderPackageBuildOptions options)
    {
        if (options.IncludeDiagnostics)
        {
            AddFile(files, path, bytes, "application/json");
        }
    }

    private static void AddTiming(
        List<RenderPackageTiming> timings,
        string key,
        string label,
        long startTimestamp,
        string? detail = null)
    {
        AddTiming(
            timings,
            key,
            label,
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
            detail);
    }

    private static void AddTiming(
        List<RenderPackageTiming> timings,
        string key,
        string label,
        double durationMs,
        string? detail = null)
    {
        timings.Add(new RenderPackageTiming(
            key,
            label,
            durationMs,
            detail));
    }

    private static void AddAggregateTiming(
        IDictionary<string, TimingAggregate> aggregates,
        string key,
        string label,
        double durationMs,
        string? detail)
    {
        if (!aggregates.TryGetValue(key, out var aggregate))
        {
            aggregate = new TimingAggregate(key, label);
            aggregates[key] = aggregate;
        }

        aggregate.Add(durationMs, detail);
    }

    private static void FlushAggregateTimings(
        List<RenderPackageTiming> timings,
        IEnumerable<TimingAggregate> aggregates)
    {
        foreach (var aggregate in aggregates)
        {
            timings.Add(aggregate.ToTiming());
        }
    }

    private static string SummarizeRoutes(IEnumerable<GltfExportRoute> routes)
    {
        var routeArray = routes.ToArray();
        return $"{routeArray.Count(route => route.Status == "written")} written, "
            + $"{routeArray.Count(route => route.Status == "empty")} empty, "
            + $"{routeArray.Count(route => route.Status == "error")} errors";
    }

    private static string SummarizeRoutes(
        IEnumerable<GltfExportRoute> routes,
        Func<GltfExportRoute, bool> predicate)
    {
        return SummarizeRoutes(routes.Where(predicate));
    }

    private static void AddFile(
        List<PackedFile> files,
        string path,
        byte[] bytes,
        string? contentType = null)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        files.Add(new PackedFile(path, bytes, contentType ?? GetContentType(path)));
    }

    private static PackedFilePackage PackFiles(IReadOnlyList<PackedFile> files)
    {
        return PackedFilePackageBuilder.Pack(files);
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".gltf" => "model/gltf+json",
            ".png" => "image/png",
            ".bin" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }

    private sealed record GltfExportRoute(
        string Family,
        int? ModelId,
        string SourcePath,
        string GltfPath,
        string? BufferPath,
        string? DiagnosticsPath,
        string Status,
        string? Error)
    {
        public static GltfExportRoute Empty(string family, int? modelId, string sourcePath, string gltfPath)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "empty", null);
        }

        public static GltfExportRoute Written(
            string family,
            int? modelId,
            string sourcePath,
            string gltfPath,
            string bufferPath,
            string? diagnosticsPath)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, bufferPath, diagnosticsPath, "written", null);
        }

        public static GltfExportRoute Failed(string family, int? modelId, string sourcePath, string gltfPath, string error)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "error", error);
        }
    }

    private sealed record WorldSlotRoute(
        int Index,
        int HeaderOffset,
        int Pointer,
        int Length,
        string SemanticName,
        string? Path,
        string Status);

    private sealed record RenderTextureResource(
        int Index,
        string Uri,
        TextureSize Size,
        TextureAlphaInfo Alpha);

    private sealed class RenderTextureResources
    {
        private readonly Dictionary<int, string> _uris = [];
        private readonly Dictionary<int, TextureSize> _sizes = [];
        private readonly Dictionary<int, TextureAlphaInfo> _alpha = [];

        public IReadOnlyDictionary<int, string> Uris => _uris;

        public IReadOnlyDictionary<int, TextureSize> Sizes => _sizes;

        public IReadOnlyDictionary<int, TextureAlphaInfo> Alpha => _alpha;

        public void Add(RenderTextureResource resource)
        {
            _uris[resource.Index] = resource.Uri;
            _sizes[resource.Index] = resource.Size;
            _alpha[resource.Index] = resource.Alpha;
        }

        public RenderTextureResources Rebased(string textureDirectory)
        {
            var result = new RenderTextureResources();
            var prefix = textureDirectory.Trim().TrimEnd('/');
            foreach (var (index, uri) in _uris)
            {
                var fileName = uri.Split('/').Last();
                result.Add(new RenderTextureResource(
                    index,
                    string.IsNullOrWhiteSpace(prefix) ? fileName : $"{prefix}/{fileName}",
                    _sizes[index],
                    _alpha[index]));
            }

            return result;
        }
    }

    private sealed record RenderPackageTiming(
        string Key,
        string Label,
        double DurationMs,
        string? Detail);

    private sealed class TimingAggregate(string key, string label)
    {
        private string? _maxDetail;

        public string Key { get; } = key;

        public string Label { get; } = label;

        public int Count { get; private set; }

        public double TotalMs { get; private set; }

        public double MaxMs { get; private set; }

        public void Add(double durationMs, string? detail)
        {
            Count++;
            TotalMs += durationMs;
            if (durationMs > MaxMs)
            {
                MaxMs = durationMs;
                _maxDetail = detail;
            }
        }

        public RenderPackageTiming ToTiming()
        {
            var averageMs = Count == 0 ? 0 : TotalMs / Count;
            var detail = $"{Count} calls, max {FormatMilliseconds(MaxMs)} ms, avg {FormatMilliseconds(averageMs)} ms";
            if (!string.IsNullOrWhiteSpace(_maxDetail))
            {
                detail += $", max detail: {_maxDetail}";
            }

            return new RenderPackageTiming(Key, Label, TotalMs, detail);
        }

        private static string FormatMilliseconds(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}

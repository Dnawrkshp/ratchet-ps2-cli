using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Moby;

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
    public bool IncludeMissionMobys { get; init; } = true;
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
        return PackFiles(BuildFiles(levelWadBytes, options));
    }

    public static IReadOnlyList<PackedFile> BuildFiles(
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
        var mobyEntries = BuildAssets(
            files,
            GameId.DL,
            levelWad.Level,
            assetHeader.PayloadBytes,
            palette.PayloadBytes,
            assetWad.PayloadBytes,
            manifest,
            timings,
            options,
            ReadChunkWads(levelWadBytes, levelWad.Chunks),
            null);
        var exportedMobyClassIds = mobyEntries
            .Where(entry => entry.Status == "written")
            .Select(entry => entry.ClassId)
            .ToHashSet();

        for (var missionIndex = 0; options.IncludeMissionMobys && missionIndex < levelWad.GameplayMissionData.Count; missionIndex++)
        {
            var missionData = DlLevelWadReader.ReadSectorFileBlock(
                levelWadBytes,
                levelWad.GameplayMissionData[missionIndex]);
            var classes = DlMissionDataReader.ReadClasses(missionData);
            if (classes.Length > 0)
            {
                BuildMissionMobyGltfs(files, mobyEntries, exportedMobyClassIds, missionIndex, classes, options);
            }
        }
        manifest["Mobys"] = mobyEntries;
        manifest["MobyExportCount"] = mobyEntries.Count(entry => entry.Status == "written");
        manifest["MobyExportFailureCount"] = mobyEntries.Count(entry => entry.Status == "error");
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

        if (coreSegmentByHeaderOffset.TryGetValue(0x60, out var gameplayCore))
        {
            AddFile(files, "gameplay/gameplay_core.bin", gameplayCore.PayloadBytes);
        }

        AddTiming(
            timings,
            "managed.before-pack",
            "Managed build before pack",
            totalStart,
            $"{files.Count} files");
        manifest["PerformanceTimings"] = timings;
        AddJsonFile(files, "manifest.json", manifest);
        return files;
    }

    public static IReadOnlyList<PackedFile> BuildAssetFiles(
        GameId gameId,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetBytes,
        DlLevelWadRenderPackageBuildOptions? options = null,
        IReadOnlyDictionary<int, byte[]>? chunkWads = null,
        IReadOnlyDictionary<int, Vector3>? skyRotationDeltasRadiansPerFrame = null)
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

        var mobyEntries = BuildAssets(
            files,
            gameId,
            levelIndex,
            headerBytes,
            paletteBytes,
            assetPayloadBytes,
            manifest,
            timings,
            options,
            chunkWads,
            skyRotationDeltasRadiansPerFrame);
        manifest["Mobys"] = mobyEntries;

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

    private static List<MobyExportManifestEntry> BuildAssets(
        List<PackedFile> files,
        GameId gameId,
        int levelIndex,
        byte[] headerBytes,
        byte[] paletteBytes,
        byte[] assetBytes,
        IDictionary<string, object?> rootManifest,
        List<RenderPackageTiming> timings,
        DlLevelWadRenderPackageBuildOptions options,
        IReadOnlyDictionary<int, byte[]>? chunkWads,
        IReadOnlyDictionary<int, Vector3>? skyRotationDeltasRadiansPerFrame)
    {
        var header = DlAssetReader.ReadHeader(headerBytes);
        var allMipmapDefinitions = DlAssetReader.ReadMipmapDefinitions(
            headerBytes,
            header.GsRamOffset,
            Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStashDefinitions = allMipmapDefinitions.Skip(header.GsRamCount).ToArray();
        var environmentTextures = BuildEnvironmentTextures(files, header, paletteBytes, gsStashDefinitions);
        var mobyGsStashClassIds = DlAssetReader.ReadMobyGsStashClassIds(
            headerBytes,
            header.MobyGsStashListOffset);
        var mobyDefinitions = DlAssetReader.ReadModelDefinitions(headerBytes, header.MobyModelOffset, header.MobyModelCount);
        var tieDefinitions = DlAssetReader.ReadModelDefinitions(headerBytes, header.TieModelOffset, header.TieModelCount);
        var shrubDefinitions = DlAssetReader.ReadShrubDefinitions(headerBytes, header.ShrubModelOffset, header.ShrubModelCount);
        var tfragTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.TerrainTextureOffset, header.TerrainTextureCount);
        var mobyTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.MobyTextureOffset, header.MobyTextureCount);
        var tieTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.TieTextureOffset, header.TieTextureCount);
        var shrubTextureDefinitions = DlAssetReader.ReadTextureDefinitions(headerBytes, header.ShrubTextureOffset, header.ShrubTextureCount);
        var fxDefinitions = DlAssetReader.ReadFxTextureDefinitions(headerBytes, header.FxTextureDefOffset, header.FxTextureCount);
        var textureIsSwizzled = ShouldSwizzleAssetTextures(gameId);
        var knownAssetOffsets = DlAssetReader.CollectKnownAssetOffsets(
            gameId,
            header,
            assetBytes.Length,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions);
        var gltfExports = new List<GltfExportRoute>();

        var skyboxStart = Stopwatch.GetTimestamp();
        gltfExports.Add(BuildSkybox(
            files,
            gameId,
            levelIndex,
            header,
            assetBytes,
            knownAssetOffsets,
            options,
            skyRotationDeltasRadiansPerFrame));
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
        var mobyRoutes = BuildMobyGltfs(
            files,
            gameId,
            mobyDefinitions,
            mobyTextureDefinitions,
            paletteBytes,
            assetBytes,
            header.TextureDataOffset,
            gsStashDefinitions,
            mobyGsStashClassIds,
            knownAssetOffsets,
            textureIsSwizzled,
            options).ToArray();
        gltfExports.AddRange(mobyRoutes);
        var mobyEntries = CreateMobyManifestEntries("main", "assets", mobyRoutes).ToList();
        AddTiming(
            timings,
            "managed.assets.mobys",
            "Moby glTF exports",
            mobyStart,
            SummarizeRoutes(mobyRoutes));

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
            ["EnvironmentTextures"] = environmentTextures,
            ["Header"] = header,
            ["HeaderLength"] = headerBytes.Length,
            ["HeaderTables"] = new
            {
                MipmapDefinitions = allMipmapDefinitions,
                MobyGsStashClassIds = mobyGsStashClassIds,
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
            ["Mobys"] = mobyEntries,
            ["GltfExportCount"] = gltfExports.Count(export => export.Status == "written"),
            ["GltfExportFailureCount"] = gltfExports.Count(export => export.Status == "error")
        };

        AddJsonFile(files, "assets/manifest.json", assetManifest);
        rootManifest["AssetHeader"] = header;
        rootManifest["TextureIsSwizzled"] = textureIsSwizzled;
        return mobyEntries;
    }

    private static IReadOnlyDictionary<string, string> BuildEnvironmentTextures(
        List<PackedFile> files,
        DlAssetHeader header,
        byte[] paletteBytes,
        IReadOnlyList<DlAssetMipmapDefinition> gsStashDefinitions)
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, textureOffset, paletteOffset) in new[]
        {
            ("chrome", header.ChromeTextureOffset, header.ChromePaletteOffset),
            ("glass", header.GlassTextureOffset, header.GlassPaletteOffset)
        })
        {
            var definition = gsStashDefinitions.FirstOrDefault(item => item.Offset2 == textureOffset);
            if (definition is null)
            {
                continue;
            }

            var path = $"environment/{name}.png";
            var texture = DlAssetReader.BuildGsStashTexture(
                name,
                0,
                definition,
                paletteOffset,
                paletteBytes);
            AddFile(files, $"assets/{path}", texture.PngBytes, "image/png");
            paths[name] = path;
        }

        return paths;
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
            AddTexture(files, "assets/tfrag/textures", "textures", texture, textureResources);
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
        DlLevelWadRenderPackageBuildOptions options,
        IReadOnlyDictionary<int, Vector3>? skyRotationDeltasRadiansPerFrame)
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
            var skybox = SkyboxReader.Read(input, gameId);
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
                    metadataMode: options.GltfMetadataMode,
                    rotationDeltasRadiansPerFrame: skyRotationDeltasRadiansPerFrame));

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
        IReadOnlyList<int> mobyGsStashClassIds,
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
                var mobyTextureIsSwizzled = textureIsSwizzled
                    && !mobyGsStashClassIds.Contains(definition.ModelId);
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
                        isSwizzled: mobyTextureIsSwizzled);
                    AddTexture(
                        files,
                        $"{packageRoot}/textures",
                        "textures",
                        texture,
                        textureResources);
                    relativeTextureIndex++;
                }

                var stats = WriteMobyGltfFiles(
                    files,
                    gameId,
                    mobyBytes,
                    packageRoot,
                    new MobyGltfExportOptions
                    {
                        AnimationFormat = GetMobyAnimationFormat(gameId),
                        LodIndex = options.MobyLodIndex,
                        ExternalTextureUris = textureResources.Uris,
                        ExternalTextureSizes = textureResources.Sizes,
                        ExternalTextureAlpha = textureResources.Alpha,
                        BufferFileName = "moby.buffer.bin"
                    },
                    options);

                route = GltfExportRoute.Written(
                    "moby",
                    definition.ModelId,
                    sourcePath,
                    gltfPath,
                    $"{relativeDirectory}/moby.buffer.bin",
                    options.IncludeDiagnostics ? $"{relativeDirectory}/moby.diagnostics.json" : null,
                    stats);
            }
            catch (Exception ex) when (IsAssetTextureFailure(ex))
            {
                route = GltfExportRoute.Failed("moby", definition.ModelId, sourcePath, gltfPath, ex.Message);
            }

            yield return route;
        }
    }

    private static void BuildMissionMobyGltfs(
        List<PackedFile> files,
        List<MobyExportManifestEntry> entries,
        HashSet<int> exportedClassIds,
        int missionIndex,
        byte[] classes,
        DlLevelWadRenderPackageBuildOptions options)
    {
        var group = $"mission_{missionIndex}";
        foreach (var moby in DlMissionMobyBankReader.Read(classes))
        {
            if (!exportedClassIds.Add(moby.Definition.ClassId))
            {
                continue;
            }

            var name = moby.Definition.ClassId.ToString("x4", CultureInfo.InvariantCulture);
            var packageRoot = $"missions/{group}/moby/{name}";
            var gltfPath = $"{packageRoot}/moby.gltf";
            if (options.IncludeSourceFiles)
            {
                AddFile(files, $"{packageRoot}/moby.bin", moby.ModelBytes);
            }

            if (moby.ModelBytes.Length == 0)
            {
                exportedClassIds.Remove(moby.Definition.ClassId);
                entries.Add(MobyExportManifestEntry.Empty(group, name, moby.Definition.ClassId));
                continue;
            }

            try
            {
                var textures = new RenderTextureResources();
                for (var textureIndex = 0; textureIndex < moby.PifTextures.Count; textureIndex++)
                {
                    var texture = PifAssetExporter.Export(moby.PifTextures[textureIndex]);
                    var normalized = new DlNormalizedTexture(
                        textureIndex,
                        "mission_moby",
                        texture.PifBytes,
                        texture.PngBytes,
                        new DlNormalizedTextureMetadata(
                            "mission_moby",
                            textureIndex,
                            texture.Texture.Header.USize,
                            texture.Texture.Header.VSize,
                            texture.Texture.IsSwizzled,
                            0,
                            0,
                            texture.Texture.PixelData.Length,
                            [],
                            [],
                            moby.Definition));
                    AddTexture(
                        files,
                        $"{packageRoot}/textures",
                        "textures",
                        normalized,
                        textures);
                }

                var stats = WriteMobyGltfFiles(
                    files,
                    GameId.DL,
                    moby.ModelBytes,
                    packageRoot,
                    new MobyGltfExportOptions
                    {
                        AnimationFormat = MobyAnimationFormat.Compact,
                        LodIndex = options.MobyLodIndex,
                        ExternalTextureUris = textures.Uris,
                        ExternalTextureSizes = textures.Sizes,
                        ExternalTextureAlpha = textures.Alpha,
                        BufferFileName = "moby.buffer.bin"
                    },
                    options);
                entries.Add(MobyExportManifestEntry.Written(
                    group,
                    name,
                    moby.Definition.ClassId,
                    gltfPath,
                    stats));
            }
            catch (Exception ex) when (IsAssetTextureFailure(ex))
            {
                exportedClassIds.Remove(moby.Definition.ClassId);
                entries.Add(MobyExportManifestEntry.Failed(group, name, moby.Definition.ClassId, ex.Message));
            }
        }
    }

    private static IEnumerable<MobyExportManifestEntry> CreateMobyManifestEntries(
        string group,
        string pathPrefix,
        IEnumerable<GltfExportRoute> routes)
    {
        foreach (var route in routes)
        {
            var name = route.SourcePath.Split('/')[1];
            if (route.Status != "written")
            {
                yield return route.Status == "empty"
                    ? MobyExportManifestEntry.Empty(group, name, route.ModelId ?? 0)
                    : MobyExportManifestEntry.Failed(group, name, route.ModelId ?? 0, route.Error ?? "Export failed.");
                continue;
            }

            yield return MobyExportManifestEntry.Written(
                group,
                name,
                route.ModelId ?? 0,
                $"{pathPrefix}/{route.GltfPath}",
                route.Stats ?? throw new InvalidDataException($"Written moby route '{route.GltfPath}' has no export statistics."));
        }
    }

    private static MobyExportStats WriteMobyGltfFiles(
        List<PackedFile> files,
        GameId gameId,
        byte[] mobyBytes,
        string packageRoot,
        MobyGltfExportOptions exportOptions,
        DlLevelWadRenderPackageBuildOptions buildOptions)
    {
        var export = ExportMobyGltf(gameId, mobyBytes, "moby.gltf", exportOptions);
        AddFile(files, $"{packageRoot}/moby.gltf", export.GltfBytes, "model/gltf+json");
        AddFile(files, $"{packageRoot}/moby.buffer.bin", export.BinBytes);
        AddOptionalDiagnostics(files, $"{packageRoot}/moby.diagnostics.json", export.DiagnosticsBytes, buildOptions);
        return ReadMobyExportStats(export.GltfBytes, export.DiagnosticsBytes);
    }

    private static MobyGltfExport ExportMobyGltf(
        GameId gameId,
        byte[] mobyBytes,
        string gltfFileName,
        MobyGltfExportOptions options)
    {
        using var input = new MemoryStream(mobyBytes, writable: false);
        return gameId == GameId.DL
            ? DlMobyGltfExporter.Export(input, gltfFileName, options)
            : MobyGltfExporter.Export(input, gltfFileName, options);
    }

    private static MobyExportStats ReadMobyExportStats(byte[] gltfBytes, byte[] diagnosticsBytes)
    {
        using var gltf = JsonDocument.Parse(gltfBytes);
        var root = gltf.RootElement;
        var accessors = root.GetProperty("accessors");
        var meshes = root.GetProperty("meshes");
        var vertices = 0;
        var triangles = 0;
        foreach (var mesh in meshes.EnumerateArray())
        {
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                var positionAccessor = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
                var indexAccessor = primitive.GetProperty("indices").GetInt32();
                vertices += accessors[positionAccessor].GetProperty("count").GetInt32();
                triangles += accessors[indexAccessor].GetProperty("count").GetInt32() / 3;
            }
        }

        var invalid = 0;
        if (diagnosticsBytes.Length > 0)
        {
            using var diagnostics = JsonDocument.Parse(diagnosticsBytes);
            invalid = diagnostics.RootElement.GetProperty("Meshes").EnumerateArray().Count(mesh =>
                mesh.TryGetProperty("InvalidVertexCount", out var invalidVertices)
                    && invalidVertices.GetInt32() > 0
                || mesh.TryGetProperty("Detail", out var detail)
                    && detail.TryGetProperty("RejectedInvalidTriangles", out var invalidTriangles)
                    && invalidTriangles.GetInt32() > 0);
        }

        return new MobyExportStats(
            meshes.GetArrayLength(),
            vertices,
            triangles,
            invalid,
            root.TryGetProperty("images", out var images) ? images.GetArrayLength() : 0);
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
        string? outputFileName = null)
    {
        var fileName = outputFileName ?? $"tex.{texture.Index:0000}.png";
        var metadata = ReadPngMetadata(texture.PngBytes);
        AddFile(files, $"{packageDirectory}/{fileName}", texture.PngBytes, "image/png");

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
        string? Error,
        [property: JsonIgnore] MobyExportStats? Stats)
    {
        public static GltfExportRoute Empty(string family, int? modelId, string sourcePath, string gltfPath)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "empty", null, null);
        }

        public static GltfExportRoute Written(
            string family,
            int? modelId,
            string sourcePath,
            string gltfPath,
            string bufferPath,
            string? diagnosticsPath,
            MobyExportStats? stats = null)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, bufferPath, diagnosticsPath, "written", null, stats);
        }

        public static GltfExportRoute Failed(string family, int? modelId, string sourcePath, string gltfPath, string error)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "error", error, null);
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

    private sealed record MobyExportStats(
        int Meshes,
        int Vertices,
        int Triangles,
        int InvalidMeshRecords,
        int Images);

    private sealed record MobyExportManifestEntry(
        string Group,
        string Name,
        int ClassId,
        string? Gltf,
        string Status,
        string? Error,
        int Meshes,
        int Vertices,
        int Triangles,
        int InvalidMeshRecords,
        int Images)
    {
        public static MobyExportManifestEntry Written(
            string group,
            string name,
            int classId,
            string gltf,
            MobyExportStats stats)
        {
            return new MobyExportManifestEntry(
                group,
                name,
                classId,
                gltf,
                "written",
                null,
                stats.Meshes,
                stats.Vertices,
                stats.Triangles,
                stats.InvalidMeshRecords,
                stats.Images);
        }

        public static MobyExportManifestEntry Empty(string group, string name, int classId)
        {
            return new MobyExportManifestEntry(group, name, classId, null, "empty", null, 0, 0, 0, 0, 0);
        }

        public static MobyExportManifestEntry Failed(string group, string name, int classId, string error)
        {
            return new MobyExportManifestEntry(group, name, classId, null, "error", error, 0, 0, 0, 0, 0);
        }
    }

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

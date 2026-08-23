using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaLevelWadRenderPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static PackedFilePackage BuildPacked(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles,
        Func<UyaLevelAssetSourceFiles, IReadOnlyList<PackedFile>> buildAssetFiles,
        GameId gameId = GameId.UYA)
    {
        return PackedFilePackageBuilder.Pack(BuildFiles(levelIndex, unpackedFiles, buildAssetFiles, gameId));
    }

    public static IReadOnlyList<PackedFile> BuildFiles(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles,
        Func<UyaLevelAssetSourceFiles, IReadOnlyList<PackedFile>> buildAssetFiles,
        GameId gameId = GameId.UYA)
    {
        ArgumentNullException.ThrowIfNull(unpackedFiles);
        ArgumentNullException.ThrowIfNull(buildAssetFiles);
        if (gameId is not (GameId.GC or GameId.UYA))
        {
            throw new ArgumentOutOfRangeException(nameof(gameId), "GC/UYA render packages require GC or UYA.");
        }

        var totalStart = Stopwatch.GetTimestamp();
        var timings = new List<RenderPackageTiming>();
        var sourceFiles = CreateSourceFileLookup(unpackedFiles);
        var files = new List<PackedFile>();
        var manifest = new Dictionary<string, object?>
        {
            ["Game"] = gameId.ToString(),
            ["Source"] = "loose_level_wad",
            ["RenderPackageVersion"] = 1,
            ["Level"] = levelIndex,
            ["UnpackedFileCount"] = unpackedFiles.Count
        };

        var assetsStart = Stopwatch.GetTimestamp();
        var assetFiles = buildAssetFiles(new UyaLevelAssetSourceFiles(
            RequireSourceFile(sourceFiles, "assets/asset_header.bin").Bytes,
            RequireSourceFile(sourceFiles, "assets/palette.bin").Bytes,
            RequireSourceFile(sourceFiles, "assets/asset_wad.bin").Bytes,
            TryGetSourceFile(sourceFiles, "code/code.bin")?.Bytes ?? [],
            CollectChunkWads(sourceFiles)));
        files.AddRange(assetFiles);
        AddMobyManifest(manifest, assetFiles);
        AddTiming(
            timings,
            "managed.assets-total",
            "Asset package build",
            assetsStart,
            $"{files.Count} files so far");

        var worldStart = Stopwatch.GetTimestamp();
        BuildWorldFiles(files, sourceFiles, manifest);
        AddTiming(
            timings,
            "managed.world",
            "World sidecar build",
            worldStart,
            $"{files.Count} files so far");

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

    private static void AddMobyManifest(
        IDictionary<string, object?> manifest,
        IReadOnlyList<PackedFile> assetFiles)
    {
        var assetManifest = assetFiles.FirstOrDefault(file => file.Path == "assets/render_manifest.json");
        if (assetManifest is null)
        {
            return;
        }

        using var document = JsonDocument.Parse(assetManifest.Bytes);
        if (document.RootElement.TryGetProperty("Mobys", out var mobys))
        {
            manifest["Mobys"] = mobys.Clone();
        }
    }

    private static void BuildWorldFiles(
        List<PackedFile> files,
        IReadOnlyDictionary<string, PackedFile> sourceFiles,
        IDictionary<string, object?> rootManifest)
    {
        var slotRoutes = new List<WorldSlotRoute>();
        AddWorldSlot(files, sourceFiles, slotRoutes, 0, 0x00, "directional_lights", "directional_lights", "lighting/directional_lights.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 1, 0x04, "tie_class_ids", "tie_classes", "tie/class_ids.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 2, 0x08, "tie_instances", "tie_instances", "tie/instances.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 3, 0x0c, "tie_groups", "tie_groups", "tie/groups.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 4, 0x10, "shrub_class_ids", "shrub_classes", "shrub/class_ids.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 5, 0x14, "shrub_instances", "shrub_instances", "shrub/instances.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 6, 0x18, "shrub_groups", "shrub_groups", "shrub/groups.bin");
        AddWorldSlot(files, sourceFiles, slotRoutes, 7, 0x20, "tie_instance_colors", "tie_ambient_rgbas", "tie/colors.bin");

        var worldManifest = new Dictionary<string, object?>
        {
            ["Length"] = TryGetSourceFile(sourceFiles, "gameplay/gameplay_core.bin")?.Bytes.Length,
            ["Slots"] = slotRoutes,
            ["DirectionalLightCount"] = ReadLeadingCount(sourceFiles, "gameplay/core/directional_lights.bin"),
            ["TieClassCount"] = ReadLeadingCount(sourceFiles, "gameplay/core/tie_classes.bin"),
            ["TieInstanceCount"] = ReadLeadingCount(sourceFiles, "gameplay/core/tie_instances.bin"),
            ["ShrubClassCount"] = ReadLeadingCount(sourceFiles, "gameplay/core/shrub_classes.bin"),
            ["ShrubInstanceCount"] = ReadLeadingCount(sourceFiles, "gameplay/core/shrub_instances.bin")
        };

        AddJsonFile(files, "world/manifest.json", worldManifest);
        rootManifest["World"] = worldManifest;
    }

    private static void AddWorldSlot(
        List<PackedFile> files,
        IReadOnlyDictionary<string, PackedFile> sourceFiles,
        List<WorldSlotRoute> slotRoutes,
        int index,
        int headerOffset,
        string semanticName,
        string sourceName,
        string relativePath)
    {
        var sourcePath = $"gameplay/core/{sourceName}.bin";
        var sourceFile = TryGetSourceFile(sourceFiles, sourcePath);
        if (sourceFile is null)
        {
            slotRoutes.Add(new WorldSlotRoute(index, headerOffset, 0, 0, semanticName, null, "empty"));
            return;
        }

        AddFile(files, $"world/{relativePath}", sourceFile.Bytes, sourceFile.ContentType);
        slotRoutes.Add(new WorldSlotRoute(index, headerOffset, 0, sourceFile.Bytes.Length, semanticName, relativePath, "mapped"));
    }

    private static Dictionary<string, PackedFile> CreateSourceFileLookup(IReadOnlyList<PackedFile> files)
    {
        var lookup = new Dictionary<string, PackedFile>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            lookup[NormalizePackagePath(file.Path)] = file;
        }

        return lookup;
    }

    private static PackedFile RequireSourceFile(IReadOnlyDictionary<string, PackedFile> files, string path)
    {
        return TryGetSourceFile(files, path)
            ?? throw new InvalidDataException($"UYA level WAD is missing '{path}'.");
    }

    private static PackedFile? TryGetSourceFile(IReadOnlyDictionary<string, PackedFile> files, string path)
    {
        return files.TryGetValue(NormalizePackagePath(path), out var file) ? file : null;
    }

    private static IReadOnlyDictionary<int, byte[]> CollectChunkWads(IReadOnlyDictionary<string, PackedFile> files)
    {
        var chunkWads = new Dictionary<int, byte[]>();
        foreach (var (path, file) in files)
        {
            if (TryGetChunkIndex(path, out var chunkIndex) && chunkIndex > 0 && file.Bytes.Length > 0)
            {
                chunkWads[chunkIndex] = file.Bytes;
            }
        }

        return chunkWads;
    }

    private static bool TryGetChunkIndex(string path, out int chunkIndex)
    {
        chunkIndex = 0;
        const string prefix = "level_wad/chunks/chunk";
        const string suffix = ".wad";
        if (!path.StartsWith(prefix, StringComparison.Ordinal)
            || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(path[prefix.Length..^suffix.Length], out chunkIndex);
    }

    private static int? ReadLeadingCount(IReadOnlyDictionary<string, PackedFile> files, string path)
    {
        var file = TryGetSourceFile(files, path);
        if (file is null || file.Bytes.Length < sizeof(int))
        {
            return null;
        }

        return Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(file.Bytes.AsSpan(0, sizeof(int))));
    }

    private static string NormalizePackagePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static void AddJsonFile(List<PackedFile> files, string path, object value)
    {
        AddFile(files, path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "application/json");
    }

    private static void AddTiming(
        List<RenderPackageTiming> timings,
        string key,
        string label,
        long startTimestamp,
        string? detail = null)
    {
        timings.Add(new RenderPackageTiming(
            key,
            label,
            Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds,
            detail));
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

    private sealed record WorldSlotRoute(
        int Index,
        int HeaderOffset,
        int Pointer,
        int Length,
        string SemanticName,
        string? Path,
        string Status);

    private sealed record RenderPackageTiming(
        string Key,
        string Label,
        double DurationMs,
        string? Detail);
}

public sealed record UyaLevelAssetSourceFiles(
    byte[] HeaderBytes,
    byte[] PaletteBytes,
    byte[] AssetWadBytes,
    byte[] CodeBytes,
    IReadOnlyDictionary<int, byte[]> ChunkWads);

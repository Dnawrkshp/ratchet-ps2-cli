using Microsoft.JSInterop;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.UYA.Level;
using System.Runtime.Versioning;

namespace RatchetPs2.Wasm;

[SupportedOSPlatform("browser")]
public static partial class Exports
{
    [JSInvokable("ConvertPifToPng")]
    public static byte[] ConvertPifToPng(byte[] pifBytes, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifBytes);

        var texture = PifReader.Read(pifBytes);
        var format = ParseTexturePixelFormat(pngFormat);

        return TextureConverter.ConvertToPng(
            texture,
            format,
            new TextureConversionOptions
            {
                DoubleAlpha = doubleAlpha,
            });
    }

    [JSInvokable("ConvertPifListToPng")]
    public static byte[][] ConvertPifListToPng(byte[][] pifImages, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifImages);

        var format = ParseTexturePixelFormat(pngFormat);
        var options = new TextureConversionOptions
        {
            DoubleAlpha = doubleAlpha,
        };

        return PifAssetExporter
            .ExportMany(pifImages, format, options)
            .Select(result => result.PngBytes)
            .ToArray();
    }

    [JSInvokable("ConvertPifListToPngPacked")]
    public static PifPackedBatchResult ConvertPifListToPngPacked(byte[][] pifImages, string? pngFormat = null, bool doubleAlpha = false)
    {
        ArgumentNullException.ThrowIfNull(pifImages);

        var format = ParseTexturePixelFormat(pngFormat);
        var options = new TextureConversionOptions
        {
            DoubleAlpha = doubleAlpha,
        };

        return PifAssetExporter.ExportManyPacked(pifImages, format, options);
    }

    [JSInvokable("UnpackDlLevelWad")]
    public static PackedFilePackage UnpackDlLevelWad(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return DlLevelWadUnpacker.UnpackPacked(levelWadBytes);
    }

    [JSInvokable("UnpackUyaLevelWad")]
    public static PackedFilePackage UnpackUyaLevelWad(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return UyaLevelWadUnpacker.UnpackPacked(levelWadBytes);
    }

    [JSInvokable("BuildDlLevelWadRenderPackage")]
    public static PackedFilePackage BuildDlLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return DlLevelWadRenderPackageBuilder.BuildPacked(
            levelWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser);
    }

    [JSInvokable("BuildUyaLevelWadRenderPackage")]
    public static PackedFilePackage BuildUyaLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var package = UyaLevelWadUnpacker.Unpack(levelWadBytes);
        var renderPackage = BuildUyaRenderPackage(
            package.LevelWad.Level,
            package.Files);
        return AppendPackedFiles(renderPackage, package.Files.Where(IsGameplayMetadataFile));
    }

    [JSInvokable("BuildUyaCustomMapZipRenderPackage")]
    public static PackedFilePackage BuildUyaCustomMapZipRenderPackage(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        var package = UyaCustomMapZipUnpacker.Unpack(zipBytes);
        var renderPackage = BuildUyaRenderPackage(levelIndex: 0, package.Files);
        return AppendPackedFiles(renderPackage, package.Files.Where(IsGameplayMetadataFile));
    }

    private static PackedFilePackage BuildUyaRenderPackage(
        int levelIndex,
        IReadOnlyList<PackedFile> unpackedFiles)
    {
        return UyaLevelWadRenderPackageBuilder.BuildPacked(
            levelIndex,
            unpackedFiles,
            assetFiles => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
                GameId.UYA,
                levelIndex,
                assetFiles.HeaderBytes,
                assetFiles.PaletteBytes,
                assetFiles.AssetWadBytes,
                DlLevelWadRenderPackageBuildOptions.Browser,
                assetFiles.ChunkWads));
    }

    [JSInvokable("ExportMobyGltf")]
    public static PackedFilePackage ExportMobyGltf(byte[] mobyBytes, string? game = null, bool skipAnimations = false, int? lod = null)
    {
        ArgumentNullException.ThrowIfNull(mobyBytes);

        using var input = new MemoryStream(mobyBytes, writable: false);
        var animationFormat = ParseMobyAnimationFormat(game);
        var options = new MobyGltfExportOptions
        {
            AnimationFormat = animationFormat,
            SkipAnimationSequences = skipAnimations,
            LodIndex = lod,
            BufferFileName = "moby.buffer.bin"
        };
        var export = animationFormat == MobyAnimationFormat.Compact
            ? DlMobyGltfExporter.Export(input, "moby.gltf", options)
            : MobyGltfExporter.Export(input, "moby.gltf", options);

        return PackFiles(
            new PackedFile("moby.gltf", export.GltfBytes, "model/gltf+json"),
            new PackedFile("moby.buffer.bin", export.BinBytes, "application/octet-stream"),
            new PackedFile("moby.diagnostics.json", export.DiagnosticsBytes, "application/json"));
    }

    [JSInvokable("GetApiVersion")]
    public static string GetApiVersion() => "1";

    private static MobyAnimationFormat ParseMobyAnimationFormat(string? game)
    {
        return game?.Trim().ToUpperInvariant() switch
        {
            null or "" or "DL" => MobyAnimationFormat.Compact,
            "UYA" => MobyAnimationFormat.Standard,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Expected DL or UYA."),
        };
    }

    private static TexturePixelFormat ParseTexturePixelFormat(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "rgba32" => TexturePixelFormat.Rgba32,
            "indexed8" => TexturePixelFormat.Indexed8,
            "indexed4" => TexturePixelFormat.Indexed4,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Expected rgba32, indexed8, or indexed4."),
        };
    }

    private static PackedFilePackage PackFiles(params PackedFile[] files)
    {
        return PackedFilePackageBuilder.Pack(files);
    }

    private static bool IsGameplayMetadataFile(PackedFile file)
    {
        return string.Equals(file.Path, "gameplay/gameplay_core.bin", StringComparison.Ordinal)
            || file.Path.StartsWith("gameplay/core/", StringComparison.Ordinal);
    }

    private static PackedFilePackage AppendPackedFiles(PackedFilePackage package, IEnumerable<PackedFile> extraFiles)
    {
        var files = package.Entries
            .Select(entry => new PackedFile(
                entry.Path,
                package.PackedBytes.AsSpan(entry.Offset, entry.Length).ToArray(),
                entry.ContentType))
            .Concat(extraFiles)
            .ToArray();

        return PackedFilePackageBuilder.Pack(files);
    }
}

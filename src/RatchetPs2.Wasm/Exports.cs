using Microsoft.JSInterop;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.DL.Level;
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

    [JSInvokable("BuildDlLevelWadRenderPackage")]
    public static PackedFilePackage BuildDlLevelWadRenderPackage(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        return DlLevelWadRenderPackageBuilder.BuildPacked(
            levelWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser);
    }

    [JSInvokable("ExportMobyGltf")]
    public static PackedFilePackage ExportMobyGltf(byte[] mobyBytes, string? game = null, bool skipAnimations = false, int? lod = null)
    {
        ArgumentNullException.ThrowIfNull(mobyBytes);

        using var input = new MemoryStream(mobyBytes, writable: false);
        var export = MobyGltfExporter.Export(
            input,
            "moby.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = ParseMobyAnimationFormat(game),
                SkipAnimationSequences = skipAnimations,
                LodIndex = lod,
                BufferFileName = "moby.buffer.bin"
            });

        return PackFiles(
            new DlLevelWadFile("moby.gltf", export.GltfBytes, "model/gltf+json"),
            new DlLevelWadFile("moby.buffer.bin", export.BinBytes, "application/octet-stream"),
            new DlLevelWadFile("moby.diagnostics.json", export.DiagnosticsBytes, "application/json"));
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

    private static PackedFilePackage PackFiles(params DlLevelWadFile[] files)
    {
        var entries = new PackedFileEntry[files.Length];
        var totalLength = 0;

        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            entries[i] = new PackedFileEntry(file.Path, totalLength, file.Bytes.Length, file.ContentType);
            totalLength = checked(totalLength + file.Bytes.Length);
        }

        var packedBytes = new byte[totalLength];
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            file.Bytes.AsSpan().CopyTo(packedBytes.AsSpan(entries[i].Offset, file.Bytes.Length));
        }

        return new PackedFilePackage(packedBytes, entries);
    }
}

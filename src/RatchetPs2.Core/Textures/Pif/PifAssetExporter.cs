using RatchetPs2.Core.Textures;

namespace RatchetPs2.Core.Textures.Pif;

public static class PifAssetExporter
{
    public sealed record ExportResult(byte[] PifBytes, byte[] PngBytes, PifTextureData Texture);

    public static IReadOnlyList<ExportResult> ExportMany(
        IReadOnlyList<byte[]> pifImages,
        TexturePixelFormat pngPixelFormat = TexturePixelFormat.Rgba32,
        TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pifImages);

        var results = new ExportResult[pifImages.Count];
        for (var i = 0; i < pifImages.Count; i++)
        {
            var pifBytes = pifImages[i] ?? throw new ArgumentException("PIF image list cannot contain null entries.", nameof(pifImages));
            results[i] = Export(pifBytes, pngPixelFormat, options);
        }

        return results;
    }

    public static PifPackedBatchResult ExportManyPacked(
        IReadOnlyList<byte[]> pifImages,
        TexturePixelFormat pngPixelFormat = TexturePixelFormat.Rgba32,
        TextureConversionOptions? options = null)
    {
        var results = ExportMany(pifImages, pngPixelFormat, options);
        var offsets = new int[results.Count];
        var lengths = new int[results.Count];
        var totalSize = 0;

        for (var i = 0; i < results.Count; i++)
        {
            var pngBytes = results[i].PngBytes;
            offsets[i] = totalSize;
            lengths[i] = pngBytes.Length;
            totalSize = checked(totalSize + pngBytes.Length);
        }

        var packedPngBytes = new byte[totalSize];
        for (var i = 0; i < results.Count; i++)
        {
            var pngBytes = results[i].PngBytes;
            pngBytes.CopyTo(packedPngBytes.AsSpan(offsets[i], pngBytes.Length));
        }

        return new PifPackedBatchResult(packedPngBytes, offsets, lengths);
    }

    public static ExportResult Export(
        Stream stream,
        TexturePixelFormat pngPixelFormat = TexturePixelFormat.Rgba32,
        TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The provided stream must be seekable.", nameof(stream));
        }

        stream.Position = 0;
        var pifBytes = new byte[stream.Length];
        stream.ReadExactly(pifBytes);
        return Export(pifBytes, pngPixelFormat, options);
    }

    public static ExportResult Export(
        byte[] pifBytes,
        TexturePixelFormat pngPixelFormat = TexturePixelFormat.Rgba32,
        TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pifBytes);

        var texture = PifReader.Read(pifBytes);
        var normalizedPifBytes = pifBytes[..GetSerializedSize(texture)];
        var pngBytes = TextureConverter.ConvertToPng(texture, pngPixelFormat, options);
        return new ExportResult(normalizedPifBytes, pngBytes, texture);
    }

    private static int GetSerializedSize(PifTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        return PifWriter.GetSerializedSize(texture);
    }
}

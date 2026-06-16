using System.Buffers.Binary;

namespace RatchetPs2.Core.Textures.Pif;

public static class PifWriter
{
    public static PifTextureData CreateIndexed8(
        int width,
        int height,
        byte[] paletteData,
        byte[] pixelData,
        IReadOnlyList<byte[]>? mipPixelData = null,
        bool isSwizzled = false,
        int paletteFormat = 0,
        int paletteOrder = 0)
    {
        ValidateDimensions(width, height);
        ArgumentNullException.ThrowIfNull(paletteData);
        ArgumentNullException.ThrowIfNull(pixelData);

        if (paletteData.Length != 0x400 && paletteData.Length != 0x200)
        {
            throw new ArgumentException("Indexed8 PIF palette data must be 0x400 or 0x200 bytes.", nameof(paletteData));
        }

        var basePixelSize = checked(width * height);
        if (pixelData.Length < basePixelSize)
        {
            throw new ArgumentException(
                $"Indexed8 base pixel data must contain at least {basePixelSize} bytes.",
                nameof(pixelData));
        }

        var resolvedPaletteFormat = ResolvePaletteFormat(paletteData.Length, paletteFormat);
        var normalizedMips = NormalizeMipData(width, height, mipPixelData);
        var texFormat = 0x13 | (isSwizzled ? PifHeader.SwizzledFlag : 0);
        var serializedSize = checked(PifHeader.SizeInBytes + paletteData.Length + basePixelSize + normalizedMips.Sum(mip => mip.Length));
        var header = new PifHeader(
            PifHeader.ExpectedMagic,
            serializedSize,
            width,
            height,
            texFormat,
            resolvedPaletteFormat,
            paletteOrder,
            1 + normalizedMips.Count);

        return new PifTextureData(
            header,
            PifTextureEncoding.Indexed8,
            paletteData.ToArray(),
            pixelData[..basePixelSize],
            normalizedMips);
    }

    public static byte[] Write(PifTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        using var stream = new MemoryStream();
        Write(stream, texture);
        return stream.ToArray();
    }

    public static void Write(Stream stream, PifTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(texture);

        if (!stream.CanWrite)
        {
            throw new ArgumentException("The provided stream must be writable.", nameof(stream));
        }

        Span<byte> headerBytes = stackalloc byte[PifHeader.SizeInBytes];
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[0..4], texture.Header.Magic);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[4..8], GetSerializedSize(texture));
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[8..12], texture.Header.USize);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[12..16], texture.Header.VSize);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[16..20], texture.Header.TexFormat);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[20..24], texture.Header.PaletteFormat);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[24..28], texture.Header.PaletteOrder);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes[28..32], texture.TotalMipLevels);

        stream.Write(headerBytes);
        stream.Write(texture.PaletteData);
        stream.Write(texture.PixelData);

        foreach (var mip in texture.MipPixelData)
        {
            stream.Write(mip);
        }
    }

    public static int GetSerializedSize(PifTextureData texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return checked(PifHeader.SizeInBytes
            + texture.PaletteData.Length
            + texture.PixelData.Length
            + texture.MipPixelData.Sum(mip => mip.Length));
    }

    private static IReadOnlyList<byte[]> NormalizeMipData(
        int width,
        int height,
        IReadOnlyList<byte[]>? mipPixelData)
    {
        if (mipPixelData is null || mipPixelData.Count == 0)
        {
            return [];
        }

        var normalized = new byte[mipPixelData.Count][];
        var mipWidth = width;
        var mipHeight = height;

        for (var i = 0; i < mipPixelData.Count; i++)
        {
            var mip = mipPixelData[i] ?? throw new ArgumentException("Mip pixel data cannot contain null entries.", nameof(mipPixelData));
            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
            var mipSize = checked(mipWidth * mipHeight);
            if (mip.Length < mipSize)
            {
                throw new ArgumentException(
                    $"Mip level {i + 1} must contain at least {mipSize} bytes.",
                    nameof(mipPixelData));
            }

            normalized[i] = mip[..mipSize];
        }

        return normalized;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
    }

    private static int ResolvePaletteFormat(int paletteLength, int requestedPaletteFormat)
    {
        if (paletteLength == 0x400)
        {
            if (requestedPaletteFormat != 0)
            {
                throw new ArgumentException("A 0x400-byte Indexed8 PIF palette requires palette format 0.");
            }

            return 0;
        }

        return requestedPaletteFormat == 0
            ? 1
            : requestedPaletteFormat;
    }
}

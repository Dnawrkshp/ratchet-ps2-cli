using System.Buffers.Binary;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Textures;

public static class TextureConverter
{
    private static readonly uint[] s_crcTable = BuildCrcTable();

    public static Rgba32Image Decode(
        byte[] pixelData,
        int width,
        int height,
        TexturePixelFormat pixelFormat,
        byte[]? paletteData = null,
        TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        return pixelFormat switch
        {
            TexturePixelFormat.Indexed4 => DecodeIndexed4(pixelData, paletteData, width, height, options),
            TexturePixelFormat.Indexed8 => DecodeIndexed8(pixelData, paletteData, width, height, options),
            TexturePixelFormat.Rgba32 => DecodeRgba32(pixelData, width, height, options),
            _ => throw new NotSupportedException($"Unsupported texture pixel format '{pixelFormat}'."),
        };
    }

    public static Rgba32Image Decode(PifTextureData texture, TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(texture);

        var effectiveOptions = options ?? new TextureConversionOptions();
        effectiveOptions = new TextureConversionOptions
        {
            IsSwizzled = texture.IsSwizzled,
            DoubleAlpha = effectiveOptions.DoubleAlpha,
            DecodePaletteIndexes = texture.Encoding != PifTextureEncoding.Indexed4 && effectiveOptions.DecodePaletteIndexes,
        };

        return texture.Encoding switch
        {
            PifTextureEncoding.Indexed4 => Decode(
                texture.PixelData,
                texture.Header.USize,
                texture.Header.VSize,
                TexturePixelFormat.Indexed4,
                texture.PaletteData,
                effectiveOptions),
            PifTextureEncoding.Indexed8 => Decode(
                texture.PixelData,
                texture.Header.USize,
                texture.Header.VSize,
                TexturePixelFormat.Indexed8,
                texture.PaletteData,
                effectiveOptions),
            _ => throw new NotSupportedException($"Unsupported PIF texture encoding '{texture.Encoding}'."),
        };
    }

    public static byte[] ConvertToPng(
        byte[] pixelData,
        int width,
        int height,
        TexturePixelFormat pixelFormat,
        byte[]? paletteData = null,
        TextureConversionOptions? options = null)
    {
        var image = Decode(pixelData, width, height, pixelFormat, paletteData, options);
        return EncodePng(image);
    }

    public static byte[] ConvertToPng(PifTextureData texture, TextureConversionOptions? options = null)
    {
        var image = Decode(texture, options);
        return EncodePng(image);
    }

    public static byte[] ConvertToPng(
        PifTextureData texture,
        TexturePixelFormat pngPixelFormat,
        TextureConversionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(texture);

        return pngPixelFormat switch
        {
            TexturePixelFormat.Rgba32 => ConvertToPng(texture, options),
            TexturePixelFormat.Indexed8 => EncodePng(CreateIndexedImage(texture, 8, options)),
            TexturePixelFormat.Indexed4 => EncodePng(CreateIndexedImage(texture, 4, options)),
            _ => throw new NotSupportedException($"Unsupported PNG pixel format '{pngPixelFormat}'."),
        };
    }

    public static byte[] EncodePng(Rgba32Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var stream = new MemoryStream();
        WritePng(stream, image);
        return stream.ToArray();
    }

    public static byte[] EncodePng(IndexedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var stream = new MemoryStream();
        WritePng(stream, image);
        return stream.ToArray();
    }

    public static void WritePng(Stream stream, Rgba32Image image)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(image);

        if (!stream.CanWrite)
        {
            throw new ArgumentException("The provided stream must be writable.", nameof(stream));
        }

        stream.Write(PngSignature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)image.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(stream, "IHDR", ihdr);

        var filtered = BuildFilteredScanlines(image);
        var compressed = ZlibCompressStored(filtered);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    public static void WritePng(Stream stream, IndexedImage image)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(image);

        if (!stream.CanWrite)
        {
            throw new ArgumentException("The provided stream must be writable.", nameof(stream));
        }

        stream.Write(PngSignature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)image.Height);
        ihdr[8] = (byte)image.BitsPerPixel;
        ihdr[9] = 3;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(stream, "IHDR", ihdr);

        WriteChunk(stream, "PLTE", BuildPlteChunk(image));
        WriteChunk(stream, "tRNS", BuildTrnsChunk(image));

        var filtered = BuildFilteredScanlines(image);
        var compressed = ZlibCompressStored(filtered);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    public static int RemapPixelIndexFromRac4(int index, int width)
    {
        var s = index / (width * 2);
        var r = s % 2 == 0 ? s * 2 : ((s - 1) * 2) + 1;
        var q = (index % (width * 2)) / 32;

        var m = index % 4;
        var n = (index / 4) % 4;
        var o = index % 2;
        var p = (index / 16) % 2;

        if ((s / 2) % 2 == 1)
        {
            p = 1 - p;
        }

        if (o == 0)
        {
            m = (m + p) % 4;
        }
        else
        {
            m = ((m - p) + 4) % 4;
        }

        var x = n + ((m + (q * 4)) * 4);
        var y = r + (o * 2);
        return (x % width) + (y * width);
    }

    public static byte DecodePaletteIndex(byte index, int high = 4, int low = 3)
    {
        var difference = high - low;
        uint highMask = 1u << high;
        uint lowMask = 1u << low;
        uint otherMask = ~(highMask | lowMask);
        uint preserved = index & otherMask;
        return (byte)(((index & highMask) >> difference) | ((index & lowMask) << difference) | preserved);
    }

    private static Rgba32Image DecodeIndexed8(
        byte[] pixelData,
        byte[]? paletteData,
        int width,
        int height,
        TextureConversionOptions? options)
    {
        ValidateDimensions(width, height);
        ArgumentNullException.ThrowIfNull(paletteData);

        var pixelCount = checked(width * height);
        if (pixelData.Length < pixelCount)
        {
            throw new ArgumentException("Indexed8 pixel data is smaller than the target image size.", nameof(pixelData));
        }

        var palette = ReadPalette(paletteData, options);
        var rgba = new byte[pixelCount * 4];
        var swizzled = ShouldSwizzle(width, height, options);
        var decodePaletteIndexes = options?.DecodePaletteIndexes ?? true;

        for (var i = 0; i < pixelCount; i++)
        {
            var paletteIndex = pixelData[i];
            if (decodePaletteIndexes)
            {
                paletteIndex = DecodePaletteIndex(paletteIndex);
            }

            WritePaletteColor(rgba, swizzled ? RemapPixelIndexFromRac4(i, width) : i, palette, paletteIndex);
        }

        return new Rgba32Image(width, height, rgba);
    }

    private static IndexedImage CreateIndexedImage(PifTextureData texture, int bitsPerPixel, TextureConversionOptions? options)
    {
        var effectiveOptions = options ?? new TextureConversionOptions();
        effectiveOptions = new TextureConversionOptions
        {
            IsSwizzled = texture.IsSwizzled,
            DoubleAlpha = effectiveOptions.DoubleAlpha,
            DecodePaletteIndexes = texture.Encoding == PifTextureEncoding.Indexed8
                && bitsPerPixel == 8
                && effectiveOptions.DecodePaletteIndexes,
        };

        return texture.Encoding switch
        {
            PifTextureEncoding.Indexed8 when bitsPerPixel == 8 => DecodeIndexed8ToIndexedImage(texture, effectiveOptions),
            PifTextureEncoding.Indexed4 when bitsPerPixel == 8 => ExpandIndexed4ToIndexed8Image(texture, effectiveOptions),
            PifTextureEncoding.Indexed4 when bitsPerPixel == 4 => DecodeIndexed4ToIndexedImage(texture, effectiveOptions),
            PifTextureEncoding.Indexed4 or PifTextureEncoding.Indexed8 => throw new NotSupportedException(
                $"Cannot export {texture.Encoding} as indexed {bitsPerPixel}bpp PNG without palette conversion."),
            _ => throw new NotSupportedException($"Unsupported PIF texture encoding '{texture.Encoding}'."),
        };
    }

    private static IndexedImage DecodeIndexed8ToIndexedImage(PifTextureData texture, TextureConversionOptions? options)
    {
        ValidateDimensions(texture.Header.USize, texture.Header.VSize);

        var width = texture.Header.USize;
        var height = texture.Header.VSize;
        var pixelCount = checked(width * height);
        if (texture.PixelData.Length < pixelCount)
        {
            throw new ArgumentException("Indexed8 pixel data is smaller than the target image size.", nameof(texture));
        }

        var palette = ReadPalette(texture.PaletteData, options);
        var indices = new byte[pixelCount];
        var swizzled = ShouldSwizzle(width, height, options);
        var decodePaletteIndexes = options?.DecodePaletteIndexes ?? true;

        for (var i = 0; i < pixelCount; i++)
        {
            var paletteIndex = texture.PixelData[i];
            if (decodePaletteIndexes)
            {
                paletteIndex = DecodePaletteIndex(paletteIndex);
            }

            indices[swizzled ? RemapPixelIndexFromRac4(i, width) : i] = paletteIndex;
        }

        return new IndexedImage(width, height, 8, palette, indices);
    }

    private static IndexedImage DecodeIndexed4ToIndexedImage(PifTextureData texture, TextureConversionOptions? options)
    {
        ValidateDimensions(texture.Header.USize, texture.Header.VSize);

        var width = texture.Header.USize;
        var height = texture.Header.VSize;
        var pixelCount = checked(width * height);
        var expectedPackedLength = (pixelCount + 1) / 2;
        if (texture.PixelData.Length < expectedPackedLength)
        {
            throw new ArgumentException("Indexed4 pixel data is smaller than the target image size.", nameof(texture));
        }

        var unpacked = new byte[pixelCount];
        var unpackedIndex = 0;
        foreach (var pair in texture.PixelData)
        {
            if (unpackedIndex < unpacked.Length)
            {
                unpacked[unpackedIndex++] = (byte)(pair & 0x0f);
            }

            if (unpackedIndex < unpacked.Length)
            {
                unpacked[unpackedIndex++] = (byte)(pair >> 4);
            }
        }

        var palette = ReadPalette(texture.PaletteData, options);
        var indices = new byte[pixelCount];
        var swizzled = ShouldSwizzle(width, height, options);
        var decodePaletteIndexes = options?.DecodePaletteIndexes ?? false;

        for (var i = 0; i < pixelCount; i++)
        {
            var paletteIndex = unpacked[i];
            if (decodePaletteIndexes)
            {
                paletteIndex = DecodePaletteIndex(paletteIndex);
            }

            indices[swizzled ? RemapPixelIndexFromRac4(i, width) : i] = paletteIndex;
        }

        return new IndexedImage(width, height, 4, palette, indices);
    }

    private static IndexedImage ExpandIndexed4ToIndexed8Image(PifTextureData texture, TextureConversionOptions? options)
    {
        var indexed4 = DecodeIndexed4ToIndexedImage(texture, options);
        var expandedPalette = new byte[256 * 4];
        indexed4.PaletteRgba.CopyTo(expandedPalette, 0);

        return new IndexedImage(
            indexed4.Width,
            indexed4.Height,
            8,
            expandedPalette,
            indexed4.PixelIndices.ToArray());
    }

    private static Rgba32Image DecodeIndexed4(
        byte[] pixelData,
        byte[]? paletteData,
        int width,
        int height,
        TextureConversionOptions? options)
    {
        ValidateDimensions(width, height);
        ArgumentNullException.ThrowIfNull(paletteData);

        var pixelCount = checked(width * height);
        var expectedPackedLength = (pixelCount + 1) / 2;
        if (pixelData.Length < expectedPackedLength)
        {
            throw new ArgumentException("Indexed4 pixel data is smaller than the target image size.", nameof(pixelData));
        }

        var unpacked = new byte[pixelCount];
        var unpackedIndex = 0;
        foreach (var pair in pixelData)
        {
            if (unpackedIndex < unpacked.Length)
            {
                unpacked[unpackedIndex++] = (byte)(pair & 0x0f);
            }

            if (unpackedIndex < unpacked.Length)
            {
                unpacked[unpackedIndex++] = (byte)(pair >> 4);
            }
        }

        var palette = ReadPalette(paletteData, options);
        var rgba = new byte[pixelCount * 4];
        var swizzled = ShouldSwizzle(width, height, options);
        var decodePaletteIndexes = options?.DecodePaletteIndexes ?? false;

        for (var i = 0; i < pixelCount; i++)
        {
            var paletteIndex = unpacked[i];
            if (decodePaletteIndexes)
            {
                paletteIndex = DecodePaletteIndex(paletteIndex);
            }

            WritePaletteColor(rgba, swizzled ? RemapPixelIndexFromRac4(i, width) : i, palette, paletteIndex);
        }

        return new Rgba32Image(width, height, rgba);
    }

    private static Rgba32Image DecodeRgba32(
        byte[] pixelData,
        int width,
        int height,
        TextureConversionOptions? options)
    {
        ValidateDimensions(width, height);

        var expectedLength = checked(width * height * 4);
        if (pixelData.Length != expectedLength)
        {
            throw new ArgumentException(
                $"RGBA32 pixel data length must be exactly {expectedLength} bytes.",
                nameof(pixelData));
        }

        var rgba = pixelData.ToArray();
        if (options?.DoubleAlpha == true)
        {
            DoubleAlpha(rgba);
        }

        return new Rgba32Image(width, height, rgba);
    }

    private static byte[] ReadPalette(byte[] paletteData, TextureConversionOptions? options)
    {
        if (paletteData.Length % 4 != 0)
        {
            throw new ArgumentException("Palette data length must be a multiple of 4 bytes.", nameof(paletteData));
        }

        var palette = paletteData.ToArray();
        if (options?.DoubleAlpha == true)
        {
            DoubleAlpha(palette);
        }

        return palette;
    }

    private static void DoubleAlpha(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            rgba[i] = Ps2Color.ExpandOpacityAlpha(rgba[i]);
        }
    }

    private static bool ShouldSwizzle(int width, int height, TextureConversionOptions? options)
    {
        return options?.IsSwizzled == true && width >= 4 && height >= 4;
    }

    private static void WritePaletteColor(byte[] rgba, int pixelIndex, byte[] palette, int paletteIndex)
    {
        var paletteOffset = checked(paletteIndex * 4);
        if (paletteOffset + 3 >= palette.Length)
        {
            throw new InvalidDataException($"Palette index {paletteIndex} is outside the palette bounds.");
        }

        var outputOffset = checked(pixelIndex * 4);
        rgba[outputOffset] = palette[paletteOffset];
        rgba[outputOffset + 1] = palette[paletteOffset + 1];
        rgba[outputOffset + 2] = palette[paletteOffset + 2];
        rgba[outputOffset + 3] = palette[paletteOffset + 3];
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

    private static byte[] BuildFilteredScanlines(Rgba32Image image)
    {
        var stride = checked(image.Width * 4);
        var filtered = new byte[checked((stride + 1) * image.Height)];

        for (var row = 0; row < image.Height; row++)
        {
            var destinationOffset = row * (stride + 1);
            filtered[destinationOffset] = 0;
            Buffer.BlockCopy(image.PixelData, row * stride, filtered, destinationOffset + 1, stride);
        }

        return filtered;
    }

    private static byte[] BuildFilteredScanlines(IndexedImage image)
    {
        var packedRowLength = image.BitsPerPixel == 4 ? (image.Width + 1) / 2 : image.Width;
        var filtered = new byte[checked((packedRowLength + 1) * image.Height)];

        for (var row = 0; row < image.Height; row++)
        {
            var destinationOffset = row * (packedRowLength + 1);
            filtered[destinationOffset] = 0;

            if (image.BitsPerPixel == 8)
            {
                Buffer.BlockCopy(image.PixelIndices, row * image.Width, filtered, destinationOffset + 1, image.Width);
                continue;
            }

            var sourceOffset = row * image.Width;
            var packedOffset = destinationOffset + 1;
            for (var x = 0; x < image.Width; x += 2)
            {
                var low = image.PixelIndices[sourceOffset + x] & 0x0f;
                var high = x + 1 < image.Width ? image.PixelIndices[sourceOffset + x + 1] & 0x0f : 0;
                filtered[packedOffset++] = (byte)((low << 4) | high);
            }
        }

        return filtered;
    }

    private static byte[] BuildPlteChunk(IndexedImage image)
    {
        var paletteEntries = image.PaletteRgba.Length / 4;
        var plte = new byte[paletteEntries * 3];

        for (var i = 0; i < paletteEntries; i++)
        {
            var sourceOffset = i * 4;
            var destinationOffset = i * 3;
            plte[destinationOffset] = image.PaletteRgba[sourceOffset];
            plte[destinationOffset + 1] = image.PaletteRgba[sourceOffset + 1];
            plte[destinationOffset + 2] = image.PaletteRgba[sourceOffset + 2];
        }

        return plte;
    }

    private static byte[] BuildTrnsChunk(IndexedImage image)
    {
        var paletteEntries = image.PaletteRgba.Length / 4;
        var trns = new byte[paletteEntries];

        for (var i = 0; i < paletteEntries; i++)
        {
            trns[i] = image.PaletteRgba[(i * 4) + 3];
        }

        return trns;
    }

    private static byte[] ZlibCompressStored(byte[] data)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(0x78);
        stream.WriteByte(0x01);
        Span<byte> lengthBytes = stackalloc byte[4];

        var offset = 0;
        while (offset < data.Length)
        {
            var blockLength = Math.Min(ushort.MaxValue, data.Length - offset);
            var isFinalBlock = offset + blockLength >= data.Length;

            stream.WriteByte((byte)(isFinalBlock ? 0x01 : 0x00));

            BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes[0..2], (ushort)blockLength);
            BinaryPrimitives.WriteUInt16LittleEndian(lengthBytes[2..4], (ushort)~blockLength);
            stream.Write(lengthBytes);
            stream.Write(data, offset, blockLength);
            offset += blockLength;
        }

        Span<byte> adlerBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adlerBytes, Adler32(data));
        stream.Write(adlerBytes);
        return stream.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint ModAdler = 65521;
        uint a = 1;
        uint b = 0;

        foreach (var value in data)
        {
            a = (a + value) % ModAdler;
            b = (b + a) % ModAdler;
        }

        return (b << 16) | a;
    }

    private static void WriteChunk(Stream stream, string chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        stream.Write(lengthBytes);

        Span<byte> typeBytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)chunkType[i];
        }

        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;

        foreach (var value in chunkType)
        {
            crc = (crc >> 8) ^ s_crcTable[(crc ^ value) & 0xff];
        }

        foreach (var value in data)
        {
            crc = (crc >> 8) ^ s_crcTable[(crc ^ value) & 0xff];
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }

    private static ReadOnlySpan<byte> PngSignature => new byte[]
    {
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
    };
}

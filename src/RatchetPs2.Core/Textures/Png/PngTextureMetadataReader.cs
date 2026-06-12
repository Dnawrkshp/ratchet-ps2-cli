using System.Buffers.Binary;
using System.IO.Compression;

namespace RatchetPs2.Core.Textures.Png;

public readonly record struct TextureSize(int Width, int Height);

public enum TextureAlphaMode
{
    Opaque,
    Mask,
    Blend
}

public readonly record struct TextureAlphaInfo(byte MinAlpha, byte MaxAlpha, bool UsesBinaryAlpha)
{
    public static TextureAlphaInfo Opaque { get; } = new(255, 255, true);

    public bool HasAlpha => MinAlpha < 255;

    public TextureAlphaMode AlphaMode => !HasAlpha
        ? TextureAlphaMode.Opaque
        : UsesBinaryAlpha ? TextureAlphaMode.Mask : TextureAlphaMode.Blend;

    public string? GltfAlphaMode => AlphaMode switch
    {
        TextureAlphaMode.Mask => "MASK",
        TextureAlphaMode.Blend => "BLEND",
        _ => null
    };
}

public readonly record struct TextureMetadata(TextureSize Size, TextureAlphaInfo Alpha);

public static class PngTextureMetadataReader
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static TextureMetadata ReadPng(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var png = ReadPngData(stream);
        return new TextureMetadata(
            new TextureSize(png.Header.Width, png.Header.Height),
            ReadAlphaInfo(png.Header, png.Idat, png.Trns));
    }

    public static Rgba32Image ReadRgba32(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var png = ReadPngData(stream);
        if (png.Header.InterlaceMethod != 0)
        {
            throw new InvalidDataException("Interlaced PNG images are not supported for RGBA decoding.");
        }

        var rows = InflateRows(png.Idat);
        var bitsPerPixel = GetBitsPerPixel(png.Header);
        var rowByteCount = checked((png.Header.Width * bitsPerPixel + 7) / 8);
        var bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
        var expectedLength = checked(png.Header.Height * (rowByteCount + 1));
        if (rows.Length < expectedLength)
        {
            throw new InvalidDataException(
                $"PNG pixel payload is too short. Expected at least {expectedLength} byte(s), got {rows.Length}.");
        }

        var pixels = new byte[checked(png.Header.Width * png.Header.Height * 4)];
        var row = new byte[rowByteCount];
        var previousRow = new byte[rowByteCount];
        var offset = 0;

        for (var y = 0; y < png.Header.Height; y++)
        {
            var filter = rows[offset++];
            rows.AsSpan(offset, rowByteCount).CopyTo(row);
            offset += rowByteCount;
            UnfilterRow(row, previousRow, bytesPerPixel, filter);
            DecodeRowRgba(png.Header, row, png.Plte, png.Trns, pixels.AsSpan(checked(y * png.Header.Width * 4)));
            (row, previousRow) = (previousRow, row);
        }

        return new Rgba32Image(png.Header.Width, png.Header.Height, pixels);
    }

    private static PngData ReadPngData(Stream stream)
    {
        Span<byte> signature = stackalloc byte[PngSignature.Length];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("Texture is not a supported PNG file.");
        }

        PngHeader? header = null;
        byte[]? plte = null;
        byte[]? trns = null;
        var chunkHeader = new byte[8];
        var crc = new byte[4];
        using var idat = new MemoryStream();

        while (true)
        {
            stream.ReadExactly(chunkHeader);
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader[..4]);
            if (length > int.MaxValue)
            {
                throw new InvalidDataException($"PNG chunk length {length} is too large.");
            }

            var chunkType = new string([
                (char)chunkHeader[4],
                (char)chunkHeader[5],
                (char)chunkHeader[6],
                (char)chunkHeader[7]
            ]);
            var data = new byte[length];
            stream.ReadExactly(data);
            stream.ReadExactly(crc);

            switch (chunkType)
            {
                case "IHDR":
                    header = ReadHeader(data);
                    break;
                case "PLTE":
                    plte = data;
                    break;
                case "tRNS":
                    trns = data;
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    if (header is not { } resolvedHeader)
                    {
                        throw new InvalidDataException("PNG is missing an IHDR chunk.");
                    }

                    return new PngData(resolvedHeader, idat.ToArray(), plte, trns);
            }
        }
    }

    private static PngHeader ReadHeader(byte[] data)
    {
        if (data.Length != 13)
        {
            throw new InvalidDataException($"PNG IHDR chunk length {data.Length} is invalid.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"PNG dimensions {width}x{height} are invalid.");
        }

        var header = new PngHeader(
            width,
            height,
            data[8],
            data[9],
            data[10],
            data[11],
            data[12]);
        if (header.CompressionMethod != 0 || header.FilterMethod != 0)
        {
            throw new InvalidDataException("PNG uses unsupported compression or filter metadata.");
        }

        _ = GetBitsPerPixel(header);
        return header;
    }

    private static TextureAlphaInfo ReadAlphaInfo(PngHeader header, byte[] idat, byte[]? trns)
    {
        if (!CanHaveNonOpaqueAlpha(header, trns))
        {
            return TextureAlphaInfo.Opaque;
        }

        if (header.InterlaceMethod != 0)
        {
            return header.ColorType is 4 or 6
                ? new TextureAlphaInfo(0, 255, false)
                : ReadTrnsAlphaFallback(header, trns);
        }

        var rows = InflateRows(idat);
        var bitsPerPixel = GetBitsPerPixel(header);
        var rowByteCount = checked((header.Width * bitsPerPixel + 7) / 8);
        var bytesPerPixel = Math.Max(1, (bitsPerPixel + 7) / 8);
        var expectedLength = checked(header.Height * (rowByteCount + 1));
        if (rows.Length < expectedLength)
        {
            throw new InvalidDataException(
                $"PNG pixel payload is too short. Expected at least {expectedLength} byte(s), got {rows.Length}.");
        }

        byte minAlpha = 255;
        byte maxAlpha = 0;
        var usesBinaryAlpha = true;
        var row = new byte[rowByteCount];
        var previousRow = new byte[rowByteCount];
        var offset = 0;

        for (var y = 0; y < header.Height; y++)
        {
            var filter = rows[offset++];
            rows.AsSpan(offset, rowByteCount).CopyTo(row);
            offset += rowByteCount;
            UnfilterRow(row, previousRow, bytesPerPixel, filter);
            ScanRowAlpha(header, row, trns, AddAlpha);
            (row, previousRow) = (previousRow, row);
        }

        return new TextureAlphaInfo(minAlpha, maxAlpha, usesBinaryAlpha);

        void AddAlpha(byte alpha)
        {
            minAlpha = Math.Min(minAlpha, alpha);
            maxAlpha = Math.Max(maxAlpha, alpha);
            usesBinaryAlpha &= alpha is 0 or 255;
        }
    }

    private static TextureAlphaInfo ReadTrnsAlphaFallback(PngHeader header, byte[]? trns)
    {
        if (header.ColorType == 3 && trns is { Length: > 0 })
        {
            var min = trns.Min();
            var max = trns.Max();
            return new TextureAlphaInfo(min, max, trns.All(alpha => alpha is 0 or 255));
        }

        return trns is { Length: > 0 }
            ? new TextureAlphaInfo(0, 255, true)
            : TextureAlphaInfo.Opaque;
    }

    private static bool CanHaveNonOpaqueAlpha(PngHeader header, byte[]? trns)
    {
        return header.ColorType is 4 or 6
            || (header.ColorType is 0 or 2 or 3 && trns is { Length: > 0 });
    }

    private static byte[] InflateRows(byte[] idat)
    {
        using var compressed = new MemoryStream(idat);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        return inflated.ToArray();
    }

    private static void UnfilterRow(byte[] row, byte[] previousRow, int bytesPerPixel, byte filter)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    row[i] = unchecked((byte)(row[i] + left));
                }
                return;
            case 2:
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = unchecked((byte)(row[i] + previousRow[i]));
                }
                return;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    var up = previousRow[i];
                    row[i] = unchecked((byte)(row[i] + ((left + up) / 2)));
                }
                return;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    var up = previousRow[i];
                    var upLeft = i >= bytesPerPixel ? previousRow[i - bytesPerPixel] : 0;
                    row[i] = unchecked((byte)(row[i] + Paeth(left, up, upLeft)));
                }
                return;
            default:
                throw new InvalidDataException($"PNG row filter {filter} is not supported.");
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var leftDistance = Math.Abs(estimate - left);
        var upDistance = Math.Abs(estimate - up);
        var upLeftDistance = Math.Abs(estimate - upLeft);

        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
        {
            return left;
        }

        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static void ScanRowAlpha(PngHeader header, byte[] row, byte[]? trns, Action<byte> addAlpha)
    {
        switch (header.ColorType)
        {
            case 0:
                ScanGrayscaleAlpha(header, row, trns, addAlpha);
                return;
            case 2:
                ScanTruecolorAlpha(header, row, trns, addAlpha);
                return;
            case 3:
                ScanIndexedAlpha(header, row, trns, addAlpha);
                return;
            case 4:
                ScanAlphaChannel(header, row, channels: 2, alphaChannel: 1, addAlpha);
                return;
            case 6:
                ScanAlphaChannel(header, row, channels: 4, alphaChannel: 3, addAlpha);
                return;
            default:
                throw new InvalidDataException($"PNG color type {header.ColorType} is not supported.");
        }
    }

    private static void DecodeRowRgba(
        PngHeader header,
        byte[] row,
        byte[]? plte,
        byte[]? trns,
        Span<byte> destination)
    {
        switch (header.ColorType)
        {
            case 0:
                DecodeGrayscaleRow(header, row, trns, destination);
                return;
            case 2:
                DecodeTruecolorRow(header, row, trns, destination);
                return;
            case 3:
                DecodeIndexedRow(header, row, plte, trns, destination);
                return;
            case 4:
                DecodeGrayscaleAlphaRow(header, row, destination);
                return;
            case 6:
                DecodeRgbaRow(header, row, destination);
                return;
            default:
                throw new InvalidDataException($"PNG color type {header.ColorType} is not supported.");
        }
    }

    private static void DecodeGrayscaleRow(PngHeader header, byte[] row, byte[]? trns, Span<byte> destination)
    {
        var transparentSample = trns is { Length: >= 2 }
            ? BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(0, 2))
            : (ushort?)null;
        for (var x = 0; x < header.Width; x++)
        {
            var sample = ReadSample(row, x, header.BitDepth, channels: 1, channel: 0);
            var value = SampleToByte(sample, header.BitDepth);
            var destinationOffset = checked(x * 4);
            destination[destinationOffset] = value;
            destination[destinationOffset + 1] = value;
            destination[destinationOffset + 2] = value;
            destination[destinationOffset + 3] = sample == transparentSample ? (byte)0 : (byte)255;
        }
    }

    private static void DecodeTruecolorRow(PngHeader header, byte[] row, byte[]? trns, Span<byte> destination)
    {
        var transparentR = trns is { Length: >= 6 }
            ? BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(0, 2))
            : (ushort?)null;
        var transparentG = trns is { Length: >= 6 }
            ? BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2, 2))
            : (ushort?)null;
        var transparentB = trns is { Length: >= 6 }
            ? BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4, 2))
            : (ushort?)null;

        for (var x = 0; x < header.Width; x++)
        {
            var r = ReadSample(row, x, header.BitDepth, channels: 3, channel: 0);
            var g = ReadSample(row, x, header.BitDepth, channels: 3, channel: 1);
            var b = ReadSample(row, x, header.BitDepth, channels: 3, channel: 2);
            var destinationOffset = checked(x * 4);
            destination[destinationOffset] = SampleToByte(r, header.BitDepth);
            destination[destinationOffset + 1] = SampleToByte(g, header.BitDepth);
            destination[destinationOffset + 2] = SampleToByte(b, header.BitDepth);
            destination[destinationOffset + 3] = r == transparentR && g == transparentG && b == transparentB
                ? (byte)0
                : (byte)255;
        }
    }

    private static void DecodeIndexedRow(
        PngHeader header,
        byte[] row,
        byte[]? plte,
        byte[]? trns,
        Span<byte> destination)
    {
        if (plte is null || plte.Length == 0 || plte.Length % 3 != 0)
        {
            throw new InvalidDataException("Indexed PNG is missing a valid PLTE chunk.");
        }

        for (var x = 0; x < header.Width; x++)
        {
            var index = ReadPackedSample(row, x, header.BitDepth);
            var paletteOffset = checked(index * 3);
            if (paletteOffset + 2 >= plte.Length)
            {
                throw new InvalidDataException($"Indexed PNG palette index {index} is outside the PLTE chunk.");
            }

            var destinationOffset = checked(x * 4);
            destination[destinationOffset] = plte[paletteOffset];
            destination[destinationOffset + 1] = plte[paletteOffset + 1];
            destination[destinationOffset + 2] = plte[paletteOffset + 2];
            destination[destinationOffset + 3] = trns is not null && index < trns.Length
                ? trns[index]
                : (byte)255;
        }
    }

    private static void DecodeGrayscaleAlphaRow(PngHeader header, byte[] row, Span<byte> destination)
    {
        for (var x = 0; x < header.Width; x++)
        {
            var value = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 2, channel: 0), header.BitDepth);
            var alpha = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 2, channel: 1), header.BitDepth);
            var destinationOffset = checked(x * 4);
            destination[destinationOffset] = value;
            destination[destinationOffset + 1] = value;
            destination[destinationOffset + 2] = value;
            destination[destinationOffset + 3] = alpha;
        }
    }

    private static void DecodeRgbaRow(PngHeader header, byte[] row, Span<byte> destination)
    {
        for (var x = 0; x < header.Width; x++)
        {
            var destinationOffset = checked(x * 4);
            destination[destinationOffset] = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 4, channel: 0), header.BitDepth);
            destination[destinationOffset + 1] = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 4, channel: 1), header.BitDepth);
            destination[destinationOffset + 2] = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 4, channel: 2), header.BitDepth);
            destination[destinationOffset + 3] = SampleToByte(ReadSample(row, x, header.BitDepth, channels: 4, channel: 3), header.BitDepth);
        }
    }

    private static void ScanGrayscaleAlpha(PngHeader header, byte[] row, byte[]? trns, Action<byte> addAlpha)
    {
        if (trns is null || trns.Length < 2)
        {
            AddOpaqueRow(header.Width, addAlpha);
            return;
        }

        var transparentSample = BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(0, 2));
        for (var x = 0; x < header.Width; x++)
        {
            var sample = ReadSample(row, x, header.BitDepth, channels: 1, channel: 0);
            addAlpha(sample == transparentSample ? (byte)0 : (byte)255);
        }
    }

    private static void ScanTruecolorAlpha(PngHeader header, byte[] row, byte[]? trns, Action<byte> addAlpha)
    {
        if (trns is null || trns.Length < 6)
        {
            AddOpaqueRow(header.Width, addAlpha);
            return;
        }

        var transparentR = BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(0, 2));
        var transparentG = BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(2, 2));
        var transparentB = BinaryPrimitives.ReadUInt16BigEndian(trns.AsSpan(4, 2));
        for (var x = 0; x < header.Width; x++)
        {
            var r = ReadSample(row, x, header.BitDepth, channels: 3, channel: 0);
            var g = ReadSample(row, x, header.BitDepth, channels: 3, channel: 1);
            var b = ReadSample(row, x, header.BitDepth, channels: 3, channel: 2);
            addAlpha(r == transparentR && g == transparentG && b == transparentB ? (byte)0 : (byte)255);
        }
    }

    private static void ScanIndexedAlpha(PngHeader header, byte[] row, byte[]? trns, Action<byte> addAlpha)
    {
        if (trns is null || trns.Length == 0)
        {
            AddOpaqueRow(header.Width, addAlpha);
            return;
        }

        for (var x = 0; x < header.Width; x++)
        {
            var index = ReadPackedSample(row, x, header.BitDepth);
            addAlpha(index < trns.Length ? trns[index] : (byte)255);
        }
    }

    private static void ScanAlphaChannel(
        PngHeader header,
        byte[] row,
        int channels,
        int alphaChannel,
        Action<byte> addAlpha)
    {
        for (var x = 0; x < header.Width; x++)
        {
            var sample = ReadSample(row, x, header.BitDepth, channels, alphaChannel);
            addAlpha(SampleToByte(sample, header.BitDepth));
        }
    }

    private static void AddOpaqueRow(int width, Action<byte> addAlpha)
    {
        for (var x = 0; x < width; x++)
        {
            addAlpha(255);
        }
    }

    private static ushort ReadSample(byte[] row, int pixelIndex, int bitDepth, int channels, int channel)
    {
        if (bitDepth < 8)
        {
            return (ushort)ReadPackedSample(row, pixelIndex, bitDepth);
        }

        var bytesPerSample = bitDepth / 8;
        var offset = checked((pixelIndex * channels + channel) * bytesPerSample);
        return bitDepth == 8
            ? row[offset]
            : BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(offset, 2));
    }

    private static int ReadPackedSample(byte[] row, int pixelIndex, int bitDepth)
    {
        var bitOffset = pixelIndex * bitDepth;
        var shift = 8 - bitDepth - bitOffset % 8;
        return (row[bitOffset / 8] >> shift) & ((1 << bitDepth) - 1);
    }

    private static byte SampleToByte(ushort sample, int bitDepth)
    {
        return bitDepth == 16
            ? (byte)((sample + 128) / 257)
            : (byte)sample;
    }

    private static int GetBitsPerPixel(PngHeader header)
    {
        var channels = header.ColorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"PNG color type {header.ColorType} is not supported.")
        };

        return header.BitDepth switch
        {
            1 or 2 or 4 when header.ColorType is 0 or 3 => header.BitDepth * channels,
            8 => header.BitDepth * channels,
            16 when header.ColorType != 3 => header.BitDepth * channels,
            _ => throw new InvalidDataException(
                $"PNG bit depth {header.BitDepth} is not supported for color type {header.ColorType}.")
        };
    }

    private readonly record struct PngHeader(
        int Width,
        int Height,
        byte BitDepth,
        byte ColorType,
        byte CompressionMethod,
        byte FilterMethod,
        byte InterlaceMethod);

    private readonly record struct PngData(PngHeader Header, byte[] Idat, byte[]? Plte, byte[]? Trns);
}

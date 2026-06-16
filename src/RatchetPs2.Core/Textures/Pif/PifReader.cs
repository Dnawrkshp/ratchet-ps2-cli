using RatchetPs2.Core.IO;

namespace RatchetPs2.Core.Textures.Pif;

public static class PifReader
{
    private static readonly IReadOnlyDictionary<int, PifFormatHandler> s_handlers =
        new Dictionary<int, PifFormatHandler>
        {
            [0x13] = new(
                PifTextureEncoding.Indexed8,
                header => checked(header.USize * header.VSize)),
            [0x14] = new(
                PifTextureEncoding.Indexed4,
                header => checked((header.USize * header.VSize) / 2)),
        };

    public static PifTextureData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(stream));
        }

        var header = ReadHeader(stream);
        ValidateHeader(header, stream);

        var handler = GetHandler(header.TextureFormatId);
        var paletteSize = GetPaletteSize(header);
        var pixelSize = handler.GetPixelDataSize(header);

        var paletteData = stream.ReadBytesExactly(paletteSize);
        var pixelData = stream.ReadBytesExactly(pixelSize);
        var mipPixelData = ReadMipPixelData(stream, header, handler);

        return new PifTextureData(header, handler.Encoding, paletteData, pixelData, mipPixelData);
    }

    public static PifTextureData Read(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        return Read(stream);
    }

    public static PifHeader ReadHeader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new PifHeader(
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian());
    }

    public static int GetPaletteSize(PifHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.BaseTextureFormat == 0x14)
        {
            return 0x40;
        }

        return header.PaletteFormat == 0 ? 0x400 : 0x200;
    }

    public static PifTextureEncoding GetEncoding(PifHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return GetHandler(header.BaseTextureFormat).Encoding;
    }

    private static void ValidateHeader(PifHeader header, Stream stream)
    {
        if (!header.HasValidMagic)
        {
            throw new InvalidDataException($"Invalid PIF magic 0x{header.Magic:X8}.");
        }

        if (header.USize <= 0 || header.VSize <= 0)
        {
            throw new InvalidDataException("PIF texture dimensions must be positive.");
        }

        var handler = GetHandler(header.BaseTextureFormat);
        var paletteSize = GetPaletteSize(header);
        var pixelSize = handler.GetPixelDataSize(header);
        var minimumLength = checked(PifHeader.SizeInBytes + paletteSize + pixelSize + GetMipPixelDataSize(header, handler));

        if (stream.CanSeek && stream.Length < minimumLength)
        {
            throw new InvalidDataException(
                $"PIF stream is too small. Expected at least {minimumLength} bytes, but found {stream.Length}.");
        }
    }

    private static PifFormatHandler GetHandler(int textureFormatId)
    {
        if (s_handlers.TryGetValue(textureFormatId, out var handler))
        {
            return handler;
        }

        throw new NotSupportedException(
            $"Unsupported PIF base texture format 0x{textureFormatId:X2}. Add a handler for this format once its layout is understood.");
    }

    private sealed record PifFormatHandler(
        PifTextureEncoding Encoding,
        Func<PifHeader, int> GetPixelDataSize);

    private static IReadOnlyList<byte[]> ReadMipPixelData(Stream stream, PifHeader header, PifFormatHandler handler)
    {
        var mipCount = Math.Max(0, header.MipLevels - 1);
        if (mipCount == 0)
        {
            return [];
        }

        var mips = new byte[mipCount][];
        var width = header.USize;
        var height = header.VSize;

        for (var i = 0; i < mipCount; i++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            var mipHeader = header with
            {
                USize = width,
                VSize = height
            };
            mips[i] = stream.ReadBytesExactly(handler.GetPixelDataSize(mipHeader));
        }

        return mips;
    }

    private static int GetMipPixelDataSize(PifHeader header, PifFormatHandler handler)
    {
        var mipCount = Math.Max(0, header.MipLevels - 1);
        var total = 0;
        var width = header.USize;
        var height = header.VSize;

        for (var i = 0; i < mipCount; i++)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            var mipHeader = header with
            {
                USize = width,
                VSize = height
            };
            total = checked(total + handler.GetPixelDataSize(mipHeader));
        }

        return total;
    }
}

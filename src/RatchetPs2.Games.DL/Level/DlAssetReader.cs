using System.Buffers.Binary;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.DL.Level;

public static class DlAssetReader
{
    private const int PaletteEntryCount = 256;
    private const int PaletteBytes = PaletteEntryCount * 4;
    private const int AssetPaletteStrideBytes = 0x100;

    public static DlAssetHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        using var stream = CreateStream(data);
        return new DlAssetHeader(
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian(),
            stream.ReadInt32LittleEndian());
    }

    public static IReadOnlyList<DlAssetModelDefinition> ReadModelDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        using var stream = CreateStream(headerData);
        var definitions = new DlAssetModelDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x20));
            definitions[i] = new DlAssetModelDefinition(
                i,
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadBytesExactly(0x10));
        }

        return definitions;
    }

    public static IReadOnlyList<DlAssetShrubDefinition> ReadShrubDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        using var stream = CreateStream(headerData);
        var definitions = new DlAssetShrubDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x30));
            var modelOffset = stream.ReadInt32LittleEndian();
            var modelId = stream.ReadInt32LittleEndian();
            var unknown8 = stream.ReadInt32LittleEndian();
            var unknownC = stream.ReadInt32LittleEndian();
            var textureIds = stream.ReadBytesExactly(0x10);
            var mipmaps = new short[3];

            definitions[i] = new DlAssetShrubDefinition(
                i,
                modelOffset,
                modelId,
                unknown8,
                unknownC,
                textureIds,
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                ReadInt16Array(stream, mipmaps));
        }

        return definitions;
    }

    public static IReadOnlyList<DlAssetTextureDefinition> ReadTextureDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        using var stream = CreateStream(headerData);
        var definitions = new DlAssetTextureDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x10));
            definitions[i] = new DlAssetTextureDefinition(
                i,
                stream.ReadInt32LittleEndian(),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream));
        }

        return definitions;
    }

    public static IReadOnlyList<DlAssetMipmapDefinition> ReadMipmapDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        if (offset <= 0 || count <= 0)
        {
            return [];
        }

        using var stream = CreateStream(headerData);
        var definitions = new DlAssetMipmapDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x10));
            definitions[i] = new DlAssetMipmapDefinition(
                i,
                stream.ReadInt32LittleEndian(),
                (short)ReadInt16(stream),
                (short)ReadInt16(stream),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian());
        }

        return definitions;
    }

    public static IReadOnlyList<int> ReadMobyGsStashClassIds(ReadOnlySpan<byte> headerData, int offset)
    {
        if (offset <= 0)
        {
            return [];
        }

        var classIds = new List<int>();
        for (var position = offset; position <= headerData.Length - sizeof(short); position += sizeof(short))
        {
            var classId = BinaryPrimitives.ReadInt16LittleEndian(headerData[position..]);
            if (classId < 0)
            {
                return classIds;
            }

            classIds.Add(classId);
        }

        throw new InvalidDataException("DL moby GS stash class list is missing its terminator.");
    }

    public static IReadOnlyList<DlParticleTextureDefinition> ReadParticleTextureDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        using var stream = CreateStream(headerData);
        var definitions = new DlParticleTextureDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x10));
            definitions[i] = new DlParticleTextureDefinition(
                i,
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian());
        }

        return definitions;
    }

    public static IReadOnlyList<DlFxTextureDefinition> ReadFxTextureDefinitions(
        ReadOnlySpan<byte> headerData,
        int offset,
        int count)
    {
        using var stream = CreateStream(headerData);
        var definitions = new DlFxTextureDefinition[count];

        for (var i = 0; i < definitions.Length; i++)
        {
            stream.Position = checked(offset + (i * 0x10));
            definitions[i] = new DlFxTextureDefinition(
                i,
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian(),
                stream.ReadInt32LittleEndian());
        }

        return definitions;
    }

    public static DlNormalizedTexture BuildAssetTexture(
        string family,
        int outputIndex,
        DlAssetTextureDefinition definition,
        ReadOnlySpan<byte> paletteData,
        ReadOnlySpan<byte> assetData,
        int textureDataOffset,
        IReadOnlyList<DlAssetMipmapDefinition>? gsStashDefinitions = null,
        bool isSwizzled = true)
    {
        ValidateDimensions(definition.Width, definition.Height, definition);

        var paletteOffset = checked(definition.PaletteId * AssetPaletteStrideBytes);
        var palette = ReadSlice(paletteData, paletteOffset, PaletteBytes, $"palette {definition.PaletteId}");
        byte[] pixels;
        var pixelOffset = 0;
        var mipOffsets = new List<int>();
        var mipLengths = new List<int>();
        var mips = new List<byte[]>();

        if ((definition.Type & 1) != 0)
        {
            pixelOffset = checked(textureDataOffset + definition.TextureOffset);
            var pixelLength = checked(definition.Width * definition.Height);
            pixels = ReadSlice(assetData, pixelOffset, pixelLength, $"{family} texture {definition.Index}");

            if ((definition.Type & 2) != 0)
            {
                var mip1Offset = checked(pixelOffset + pixelLength);
                var mip1Length = checked(Math.Max(1, definition.Width / 2) * Math.Max(1, definition.Height / 2));
                mips.Add(ReadSlice(assetData, mip1Offset, mip1Length, $"{family} texture {definition.Index} mip1"));
                mipOffsets.Add(mip1Offset);
                mipLengths.Add(mip1Length);
            }
        }
        else
        {
            var stash = gsStashDefinitions?.FirstOrDefault(item => item.Offset2 == definition.TextureOffset);
            if (stash is null)
            {
                throw new InvalidDataException(
                    $"{family} texture {definition.Index} references GS stash offset 0x{definition.TextureOffset:X}, but no matching stash entry was found.");
            }

            pixelOffset = stash.Offset1;
            var pixelLength = checked(definition.Width * definition.Height);
            pixels = ReadSlice(paletteData, pixelOffset, pixelLength, $"{family} GS stash texture {definition.Index}");
        }

        if (definition.MipmapPaletteId >= 0)
        {
            var mip2Offset = checked(definition.MipmapPaletteId * 0x100);
            var mip2Length = checked(Math.Max(1, definition.Width / 4) * Math.Max(1, definition.Height / 4));
            mips.Add(ReadSlice(paletteData, mip2Offset, mip2Length, $"{family} texture {definition.Index} mip2"));
            mipOffsets.Add(mip2Offset);
            mipLengths.Add(mip2Length);
        }

        return BuildTexture(
            family,
            outputIndex,
            definition.Width,
            definition.Height,
            palette,
            pixels,
            mips,
            isSwizzled,
            paletteOffset,
            pixelOffset,
            mipOffsets,
            mipLengths,
            definition);
    }

    public static DlNormalizedTexture BuildShrubBillboardTexture(
        DlAssetShrubDefinition definition,
        ReadOnlySpan<byte> paletteData)
    {
        ValidateDimensions(definition.Width, definition.Height, definition);

        var paletteOffset = checked(definition.PaletteId * AssetPaletteStrideBytes);
        var palette = ReadSlice(paletteData, paletteOffset, PaletteBytes, $"shrub billboard palette {definition.PaletteId}");
        var pixelOffset = checked(definition.TextureId * 0x100);
        var pixelLength = checked(definition.Width * definition.Height);
        var pixels = ReadSlice(paletteData, pixelOffset, pixelLength, $"shrub billboard {definition.ModelId:X4}");
        var mips = new List<byte[]>();
        var mipOffsets = new List<int>();
        var mipLengths = new List<int>();
        var mipWidth = (int)definition.Width;
        var mipHeight = (int)definition.Height;

        foreach (var mipmap in definition.Mipmaps)
        {
            if (mipmap <= 0)
            {
                continue;
            }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
            var mipOffset = checked(mipmap * 0x100);
            var mipLength = checked(mipWidth * mipHeight);
            mips.Add(ReadSlice(paletteData, mipOffset, mipLength, $"shrub billboard {definition.ModelId:X4} mip"));
            mipOffsets.Add(mipOffset);
            mipLengths.Add(mipLength);
        }

        return BuildTexture(
            "shrub_billboard",
            0,
            definition.Width,
            definition.Height,
            palette,
            pixels,
            mips,
            isSwizzled: false,
            paletteOffset,
            pixelOffset,
            mipOffsets,
            mipLengths,
            definition);
    }

    public static DlNormalizedTexture BuildParticleTexture(
        DlParticleTextureDefinition definition,
        ReadOnlySpan<byte> assetData,
        int dataOffset,
        bool isSwizzled = true)
    {
        var paletteOffset = checked(dataOffset + definition.PaletteOffset);
        var pixelOffset = checked(dataOffset + definition.TextureOffset);
        var size = definition.Size;
        ValidateDimensions(size, size, definition);

        return BuildTexture(
            "particle",
            definition.Index,
            size,
            size,
            ReadSlice(assetData, paletteOffset, PaletteBytes, $"particle palette {definition.Index}"),
            ReadSlice(assetData, pixelOffset, checked(size * size), $"particle texture {definition.Index}"),
            [],
            isSwizzled,
            paletteOffset,
            pixelOffset,
            [],
            [],
            definition);
    }

    public static DlNormalizedTexture BuildFxTexture(
        DlFxTextureDefinition definition,
        ReadOnlySpan<byte> assetData,
        int dataOffset,
        bool isSwizzled = true)
    {
        ValidateDimensions(definition.Width, definition.Height, definition);
        var paletteOffset = checked(dataOffset + definition.PaletteOffset);
        var pixelOffset = checked(dataOffset + definition.TextureOffset);
        var pixelLength = checked(definition.Width * definition.Height);

        return BuildTexture(
            "fx",
            definition.Index,
            definition.Width,
            definition.Height,
            ReadSlice(assetData, paletteOffset, PaletteBytes, $"fx palette {definition.Index}"),
            ReadSlice(assetData, pixelOffset, pixelLength, $"fx texture {definition.Index}"),
            [],
            isSwizzled,
            paletteOffset,
            pixelOffset,
            [],
            [],
            definition);
    }

    public static DlNormalizedTexture BuildGsStashTexture(
        string family,
        int outputIndex,
        DlAssetMipmapDefinition definition,
        int paletteOffset,
        ReadOnlySpan<byte> paletteData,
        bool isSwizzled = false)
    {
        ValidateDimensions(definition.Width, definition.Height, definition);
        var pixelLength = checked(definition.Width * definition.Height);

        return BuildTexture(
            family,
            outputIndex,
            definition.Width,
            definition.Height,
            ReadSlice(paletteData, paletteOffset, PaletteBytes, $"{family} palette"),
            ReadSlice(paletteData, definition.Offset1, pixelLength, $"{family} texture"),
            [],
            isSwizzled,
            paletteOffset,
            definition.Offset1,
            [],
            [],
            definition);
    }

    public static byte[] ReadAssetSlice(
        ReadOnlySpan<byte> assetData,
        int offset,
        IEnumerable<int> knownOffsets,
        bool allowZeroOffset = false)
    {
        if (offset < 0 || (offset == 0 && !allowZeroOffset))
        {
            return [];
        }

        var assetLength = assetData.Length;
        var nextOffset = knownOffsets
            .Where(candidate => candidate > offset && candidate <= assetLength)
            .DefaultIfEmpty(assetLength)
            .Min();
        var length = nextOffset - offset;
        return length <= 0
            ? []
            : ReadSlice(assetData, offset, length, $"asset slice 0x{offset:X}");
    }

    public static string GetAssetFolderName(int oClass)
    {
        return $"{oClass:00000}_{oClass:X4}";
    }

    private static DlNormalizedTexture BuildTexture(
        string family,
        int outputIndex,
        int width,
        int height,
        byte[] palette,
        byte[] pixels,
        IReadOnlyList<byte[]> mips,
        bool isSwizzled,
        int paletteOffset,
        int pixelOffset,
        IReadOnlyList<int> mipOffsets,
        IReadOnlyList<int> mipLengths,
        object sourceDefinition)
    {
        var texture = PifWriter.CreateIndexed8(
            width,
            height,
            palette,
            pixels,
            mips,
            isSwizzled);
        var pifBytes = PifWriter.Write(texture);
        var pngBytes = TextureConverter.ConvertToPng(texture, TexturePixelFormat.Rgba32);

        return new DlNormalizedTexture(
            outputIndex,
            family,
            pifBytes,
            pngBytes,
            new DlNormalizedTextureMetadata(
                family,
                outputIndex,
                width,
                height,
                isSwizzled,
                paletteOffset,
                pixelOffset,
                pixels.Length,
                mipOffsets,
                mipLengths,
                sourceDefinition));
    }

    private static MemoryStream CreateStream(ReadOnlySpan<byte> data)
    {
        return new MemoryStream(data.ToArray(), writable: false);
    }

    private static int ReadInt16(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(short)];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt16LittleEndian(bytes);
    }

    private static IReadOnlyList<short> ReadInt16Array(Stream stream, short[] buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (short)ReadInt16(stream);
        }

        return buffer;
    }

    private static byte[] ReadSlice(ReadOnlySpan<byte> data, int offset, int length, string description)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new InvalidDataException(
                $"Cannot read {description}: offset 0x{offset:X}, length 0x{length:X}, data length 0x{data.Length:X}.");
        }

        return data.Slice(offset, length).ToArray();
    }

    private static void ValidateDimensions(int width, int height, object source)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException($"Invalid texture dimensions {width}x{height} in {source.GetType().Name}.");
        }
    }
}

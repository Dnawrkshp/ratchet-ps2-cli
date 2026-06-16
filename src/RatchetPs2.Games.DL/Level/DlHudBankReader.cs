using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.DL.Level;

public static class DlHudBankReader
{
    public const int HeaderFixedLength = 0xb4;
    public const int BankCount = 5;
    public const int PaletteLength = 0x400;

    public static DlHudBankSet Read(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> bankBytes)
    {
        ArgumentNullException.ThrowIfNull(bankBytes);

        if (headerBytes.Length < HeaderFixedLength)
        {
            throw new InvalidDataException("DL HUD header is too small.");
        }

        var banks = Enumerable.Range(0, BankCount)
            .Select(index => index < bankBytes.Count ? bankBytes[index] : [])
            .ToArray();
        var header = ReadHeader(headerBytes);
        var icons = ReadIcons(headerBytes, header);
        var frames = ReadFrames(headerBytes, header);
        var palettes = ReadPalettes(headerBytes, banks, header);
        var textures = ReadTextures(headerBytes, banks, header);

        return new DlHudBankSet(header, icons, frames, palettes, textures);
    }

    public static bool TryGetPalette(DlHudBankSet hud, int paletteId, out DlHudPaletteEntry palette)
    {
        palette = hud.Palettes.FirstOrDefault(entry => entry.Index == paletteId)!;
        return palette is not null && palette.IsLengthValid;
    }

    public static bool TryGetTexture(DlHudBankSet hud, int textureId, out DlHudTextureEntry texture)
    {
        texture = hud.Textures.FirstOrDefault(entry => entry.Index == textureId)!;
        return texture is not null && texture.IsLengthValid;
    }

    private static DlHudHeader ReadHeader(ReadOnlySpan<byte> data)
    {
        var paletteCounts = ReadInt32Array(data, 0x14, BankCount);
        var textureCounts = ReadInt32Array(data, 0x34, BankCount);
        return new DlHudHeader(
            ReadUInt16LittleEndian(data, 0x00),
            ReadUInt16LittleEndian(data, 0x02),
            ReadInt32LittleEndian(data, 0x04),
            ReadInt32LittleEndian(data, 0x08),
            ReadInt32LittleEndian(data, 0x0c),
            ReadInt32LittleEndian(data, 0x10),
            paletteCounts,
            ReadInt32Array(data, 0x28, 3),
            textureCounts,
            ReadInt32Array(data, 0x48, 3),
            ReadInt32Array(data, 0x54, BankCount),
            data.Slice(0x68, HeaderFixedLength - 0x68).ToArray());
    }

    private static IReadOnlyList<DlHudIconEntry> ReadIcons(ReadOnlySpan<byte> headerBytes, DlHudHeader header)
    {
        var icons = new List<DlHudIconEntry>(header.IconCount);
        for (var i = 0; i < header.IconCount; i++)
        {
            var offset = checked(header.IconListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "DL HUD icon entry");
            icons.Add(new DlHudIconEntry(
                i,
                ReadUInt16LittleEndian(headerBytes, offset),
                ReadUInt16LittleEndian(headerBytes, offset + 2),
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                ReadUInt16LittleEndian(headerBytes, offset + 6)));
        }

        return icons;
    }

    private static IReadOnlyList<DlHudFrameEntry> ReadFrames(ReadOnlySpan<byte> headerBytes, DlHudHeader header)
    {
        var frames = new List<DlHudFrameEntry>(header.FrameCount);
        for (var i = 0; i < header.FrameCount; i++)
        {
            var offset = checked(header.FrameListOffset + (i * 4));
            ValidateHeaderRange(headerBytes, offset, 4, "DL HUD frame entry");
            frames.Add(new DlHudFrameEntry(
                i,
                ReadInt16LittleEndian(headerBytes, offset),
                ReadInt16LittleEndian(headerBytes, offset + 2)));
        }

        return frames;
    }

    private static IReadOnlyList<DlHudPaletteEntry> ReadPalettes(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> banks,
        DlHudHeader header)
    {
        var paletteCount = GetFinalCount(header.PaletteCumulativeCounts);
        var palettes = new List<DlHudPaletteEntry>(paletteCount);
        for (var i = 0; i < paletteCount; i++)
        {
            var offset = checked(header.PaletteListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "DL HUD palette entry");
            var bankIndex = GetBankIndex(header.PaletteCumulativeCounts, i);
            var encodedOffset = ReadUInt32LittleEndian(headerBytes, offset);
            var paletteOffset = DecodePayloadOffset(encodedOffset);
            var bankBytes = banks[bankIndex].AsSpan();
            var isLengthValid = IsPayloadRangeValid(bankBytes, paletteOffset, PaletteLength);
            palettes.Add(new DlHudPaletteEntry(
                i,
                bankIndex,
                encodedOffset,
                paletteOffset,
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                ReadUInt16LittleEndian(headerBytes, offset + 6),
                isLengthValid,
                isLengthValid
                    ? bankBytes.Slice(paletteOffset, PaletteLength).ToArray()
                    : []));
        }

        return palettes;
    }

    private static IReadOnlyList<DlHudTextureEntry> ReadTextures(
        ReadOnlySpan<byte> headerBytes,
        IReadOnlyList<byte[]> banks,
        DlHudHeader header)
    {
        var textureCount = GetFinalCount(header.TextureCumulativeCounts);
        var textures = new List<DlHudTextureEntry>(textureCount);
        for (var i = 0; i < textureCount; i++)
        {
            var offset = checked(header.TextureListOffset + (i * 8));
            ValidateHeaderRange(headerBytes, offset, 8, "DL HUD texture entry");
            var bankIndex = GetBankIndex(header.TextureCumulativeCounts, i);
            var encodedOffset = ReadUInt32LittleEndian(headerBytes, offset);
            var textureOffset = DecodePayloadOffset(encodedOffset);
            var uLog = headerBytes[offset + 6];
            var vLog = headerBytes[offset + 7];
            var width = DimensionFromLog(uLog);
            var height = DimensionFromLog(vLog);
            var pixelLength = checked(width * height);
            var bankBytes = banks[bankIndex].AsSpan();
            var isLengthValid = IsPayloadRangeValid(bankBytes, textureOffset, pixelLength);

            textures.Add(new DlHudTextureEntry(
                i,
                bankIndex,
                encodedOffset,
                textureOffset,
                ReadUInt16LittleEndian(headerBytes, offset + 4),
                uLog,
                vLog,
                width,
                height,
                pixelLength,
                isLengthValid,
                isLengthValid
                    ? bankBytes.Slice(textureOffset, pixelLength).ToArray()
                    : []));
        }

        return textures;
    }

    private static IReadOnlyList<int> ReadInt32Array(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new int[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadInt32LittleEndian(data, offset + (i * 4));
        }

        return values;
    }

    private static int GetFinalCount(IReadOnlyList<int> cumulativeCounts)
    {
        var count = cumulativeCounts.Count == 0 ? 0 : cumulativeCounts.Max();
        if (count < 0)
        {
            throw new InvalidDataException("DL HUD cumulative count is negative.");
        }

        return count;
    }

    private static int GetBankIndex(IReadOnlyList<int> cumulativeCounts, int itemIndex)
    {
        for (var bank = 0; bank < cumulativeCounts.Count; bank++)
        {
            if (itemIndex < cumulativeCounts[bank])
            {
                return bank;
            }
        }

        throw new InvalidDataException($"DL HUD item index {itemIndex} is outside cumulative bank counts.");
    }

    private static int DimensionFromLog(byte log)
    {
        if (log > 30)
        {
            throw new InvalidDataException($"DL HUD texture dimension log {log} is too large.");
        }

        return 1 << log;
    }

    private static int DecodePayloadOffset(uint encodedOffset)
    {
        return checked((int)(encodedOffset & 0x7fffffff));
    }

    private static bool IsPayloadRangeValid(ReadOnlySpan<byte> data, int offset, int length)
    {
        return offset >= 0 && length >= 0 && (long)offset + length <= data.Length;
    }

    private static void ValidateHeaderRange(ReadOnlySpan<byte> data, int offset, int length, string label)
    {
        if (!IsPayloadRangeValid(data, offset, length))
        {
            throw new InvalidDataException($"{label} points outside the HUD header.");
        }
    }

}

public sealed record DlHudBankSet(
    DlHudHeader Header,
    IReadOnlyList<DlHudIconEntry> Icons,
    IReadOnlyList<DlHudFrameEntry> Frames,
    IReadOnlyList<DlHudPaletteEntry> Palettes,
    IReadOnlyList<DlHudTextureEntry> Textures);

public sealed record DlHudHeader(
    ushort IconCount,
    ushort FrameCount,
    int IconListOffset,
    int FrameListOffset,
    int PaletteListOffset,
    int TextureListOffset,
    IReadOnlyList<int> PaletteCumulativeCounts,
    IReadOnlyList<int> UnknownCounts28,
    IReadOnlyList<int> TextureCumulativeCounts,
    IReadOnlyList<int> UnknownCounts48,
    IReadOnlyList<int> BankSizes,
    byte[] RuntimePointerArea);

public sealed record DlHudIconEntry(
    int Index,
    ushort IconId,
    ushort FrameCount,
    ushort FirstFrameIndex,
    ushort Padding);

public sealed record DlHudFrameEntry(
    int Index,
    short PaletteIndex,
    short TextureIndex);

public sealed record DlHudPaletteEntry(
    int Index,
    int BankIndex,
    uint EncodedOffset,
    int Offset,
    ushort GsRam,
    ushort Padding,
    bool IsLengthValid,
    byte[] PaletteBytes);

public sealed record DlHudTextureEntry(
    int Index,
    int BankIndex,
    uint EncodedOffset,
    int Offset,
    ushort GsRam,
    byte ULog,
    byte VLog,
    int Width,
    int Height,
    int PixelLength,
    bool IsLengthValid,
    byte[] PixelBytes);

using System.Buffers.Binary;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Games.DL.Online;

public readonly record struct DlOnlineByteRange(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;
}

public sealed record DlOnlineArmorEntry(
    int Index,
    int ClassId,
    DlOnlineByteRange Model,
    DlOnlineByteRange Textures,
    byte[] ModelBytes,
    IReadOnlyList<PifTextureData> PifTextures);

public sealed record DlOnlineArmorWad(IReadOnlyList<DlOnlineArmorEntry> Armors);

public static class DlOnlineArmorWadReader
{
    public const int ArmorCount = 44;

    private const int StandalonePayloadOffset = DlLevelConstants.SectorSize;
    private const int OnlineDataHeaderSize = 0x5c0;
    private const int ArmorEntryOffset = 0x250;
    private const int ArmorEntrySize = 0x14;

    public static DlOnlineArmorWad ReadWad(Stream wadStream)
    {
        ValidateWadStream(wadStream);
        var header = wadStream.ReadBytesExactly(DlOnlineWadExtractor.HeaderSize);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (headerSize != DlOnlineWadExtractor.HeaderSize)
        {
            throw new InvalidDataException(
                $"Unsupported DL online WAD header size 0x{headerSize:X}; expected 0x{DlOnlineWadExtractor.HeaderSize:X}.");
        }

        var dataRange = ReadSectorRange(header, 0x08);
        if (dataRange.IsEmpty)
        {
            throw new InvalidDataException("DL online WAD does not contain an online data payload.");
        }

        var dataOffset = checked((long)StandalonePayloadOffset + ((long)dataRange.Offset * DlLevelConstants.SectorSize));
        var dataLength = checked((long)dataRange.Length * DlLevelConstants.SectorSize);
        if (dataOffset < StandalonePayloadOffset
            || dataLength < OnlineDataHeaderSize
            || dataOffset + dataLength > wadStream.Length
            || dataLength > int.MaxValue)
        {
            throw new InvalidDataException("DL online data range exceeds the multiplayer WAD bounds.");
        }

        wadStream.Position = dataOffset;
        return ReadData(wadStream.ReadBytesExactly((int)dataLength));
    }

    public static DlOnlineArmorWad ReadData(ReadOnlySpan<byte> data)
    {
        if (data.Length < OnlineDataHeaderSize)
        {
            throw new InvalidDataException("DL online data header is truncated.");
        }

        var armors = new List<DlOnlineArmorEntry>(ArmorCount);
        for (var index = 0; index < ArmorCount; index++)
        {
            var entryOffset = ArmorEntryOffset + (index * ArmorEntrySize);
            var classId = BinaryPrimitives.ReadInt32LittleEndian(data[entryOffset..]);
            var model = ReadByteRange(data, entryOffset + 4);
            var textures = ReadByteRange(data, entryOffset + 0x0c);
            if (model.IsEmpty)
            {
                continue;
            }

            var modelBytes = DecompressRange(data, model, $"online armor {index} model");
            var textureBytes = textures.IsEmpty
                ? []
                : DecompressRange(data, textures, $"online armor {index} texture list");
            armors.Add(new DlOnlineArmorEntry(
                index,
                classId,
                model,
                textures,
                modelBytes,
                DlMobyTextureListReader.Read(textureBytes)));
        }

        return new DlOnlineArmorWad(armors);
    }

    private static DlOnlineByteRange ReadByteRange(ReadOnlySpan<byte> data, int offset)
    {
        return new DlOnlineByteRange(
            BinaryPrimitives.ReadInt32LittleEndian(data[offset..]),
            BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]));
    }

    private static DlOnlineByteRange ReadSectorRange(ReadOnlySpan<byte> data, int offset)
    {
        return ReadByteRange(data, offset);
    }

    private static byte[] DecompressRange(ReadOnlySpan<byte> data, DlOnlineByteRange range, string description)
    {
        if (range.Offset < 0 || range.Length < 0 || (long)range.Offset + range.Length > data.Length)
        {
            throw new InvalidDataException($"DL {description} range exceeds the online data bounds.");
        }

        try
        {
            return WadCompression.Decompress(data.Slice(range.Offset, range.Length));
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"DL {description} is not valid WAD-compressed data: {ex.Message}", ex);
        }
    }

    private static void ValidateWadStream(Stream wadStream)
    {
        ArgumentNullException.ThrowIfNull(wadStream);
        if (!wadStream.CanRead || !wadStream.CanSeek)
        {
            throw new ArgumentException("The DL online WAD stream must be readable and seekable.", nameof(wadStream));
        }

        if (wadStream.Length < StandalonePayloadOffset)
        {
            throw new InvalidDataException("DL online WAD is truncated before its payload.");
        }

        wadStream.Position = 0;
    }
}

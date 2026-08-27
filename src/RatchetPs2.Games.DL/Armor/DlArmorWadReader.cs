using System.Buffers.Binary;
using RatchetPs2.Core.IO;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Games.DL.Armor;

public readonly record struct DlArmorSectorRange(int Offset, int Length)
{
    public bool IsEmpty => Length <= 0;
}

public sealed record DlArmorWadEntry(
    int Index,
    DlArmorSectorRange Model,
    DlArmorSectorRange Textures,
    byte[] ModelBytes,
    IReadOnlyList<PifTextureData> PifTextures);

public sealed record DlArmorWad(
    int HeaderSize,
    int PayloadSector,
    IReadOnlyList<DlArmorWadEntry> Armors);

public sealed record DlArmorWadExtraction(
    int HeaderSize,
    int SourcePayloadSector,
    int PayloadSectorCount);

public static class DlArmorWadReader
{
    public const int StandardHeaderSize = 0x228;
    public const int JapaneseHeaderSize = 0x248;
    public const int StandardArmorCount = 20;
    public const int JapaneseArmorCount = 22;

    private const int TableOfContentsSector = 1001;
    private const int MaximumTableOfContentsSize = 0x200000;
    private const int ArmorEntryOffset = 0x08;
    private const int ArmorEntrySize = 0x10;

    public static DlArmorWad ReadFromIso(Stream isoStream)
    {
        ValidateIsoStream(isoStream);

        var header = FindHeaderInIsoTableOfContents(isoStream);
        var payloadSector = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (payloadSector <= 0)
        {
            throw new InvalidDataException("DL armor WAD has an invalid payload sector.");
        }

        return ReadCore(header, range => ReadIsoRange(isoStream, payloadSector, range));
    }

    public static DlArmorWad ReadWad(Stream wadStream)
    {
        ValidateWadStream(wadStream);
        var header = ReadStandaloneHeader(wadStream);
        var payloadOffset = AlignToSector(header.Length);
        return ReadCore(header, range => ReadWadRange(wadStream, payloadOffset, range));
    }

    public static DlArmorWadExtraction ExtractWadFromIso(Stream isoStream, Stream output)
    {
        ValidateIsoStream(isoStream);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The armor WAD output stream must be writable.", nameof(output));
        }

        var header = FindHeaderInIsoTableOfContents(isoStream);
        var headerSize = ReadAndValidateHeaderSize(header);
        var payloadSector = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (payloadSector <= 0)
        {
            throw new InvalidDataException("DL armor WAD has an invalid payload sector.");
        }

        var payloadSectorCount = GetPayloadSectorCount(header);
        var payloadOffset = checked((long)payloadSector * DlLevelConstants.SectorSize);
        var payloadLength = checked((long)payloadSectorCount * DlLevelConstants.SectorSize);
        if (payloadOffset + payloadLength > isoStream.Length)
        {
            throw new InvalidDataException("DL armor WAD payload exceeds the ISO bounds.");
        }

        output.Write(header);
        WriteZeroPadding(output, AlignToSector(headerSize) - headerSize);
        isoStream.Position = payloadOffset;
        CopyExactly(isoStream, output, payloadLength);
        return new DlArmorWadExtraction(headerSize, payloadSector, payloadSectorCount);
    }

    public static DlArmorWad ReadPayload(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload)
    {
        var headerBytes = header.ToArray();
        var payloadBytes = payload.ToArray();
        return ReadCore(headerBytes, range => ReadPayloadRange(payloadBytes, range));
    }

    private static DlArmorWad ReadCore(byte[] header, Func<DlArmorSectorRange, byte[]> readRange)
    {
        var headerSize = ReadAndValidateHeaderSize(header);
        var armorCount = headerSize == JapaneseHeaderSize
            ? JapaneseArmorCount
            : StandardArmorCount;
        var payloadSector = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        var armors = new List<DlArmorWadEntry>(armorCount);

        for (var index = 0; index < armorCount; index++)
        {
            var entryOffset = ArmorEntryOffset + (index * ArmorEntrySize);
            var model = ReadSectorRange(header, entryOffset);
            var textures = ReadSectorRange(header, entryOffset + 8);
            if (model.IsEmpty)
            {
                continue;
            }

            var modelBytes = readRange(model);
            var textureBytes = textures.IsEmpty ? [] : readRange(textures);
            armors.Add(new DlArmorWadEntry(
                index,
                model,
                textures,
                modelBytes,
                DlMobyTextureListReader.Read(textureBytes)));
        }

        return new DlArmorWad(headerSize, payloadSector, armors);
    }

    private static byte[] FindHeaderInIsoTableOfContents(Stream isoStream)
    {
        var tocOffset = checked((long)TableOfContentsSector * DlLevelConstants.SectorSize);
        if (tocOffset + sizeof(int) > isoStream.Length)
        {
            throw new InvalidDataException("DL ISO is too small to contain its global WAD table of contents.");
        }

        var tocEnd = Math.Min(isoStream.Length, tocOffset + MaximumTableOfContentsSize);
        var offset = tocOffset;
        Span<byte> sizeBytes = stackalloc byte[sizeof(int)];
        while (offset + sizeof(int) <= tocEnd)
        {
            isoStream.Position = offset;
            isoStream.ReadExactly(sizeBytes);
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
            if (headerSize < 8 || headerSize > ushort.MaxValue || offset + headerSize > tocEnd)
            {
                break;
            }

            var header = new byte[headerSize];
            sizeBytes.CopyTo(header);
            isoStream.ReadExactly(header.AsSpan(sizeof(int)));
            if (headerSize is StandardHeaderSize or JapaneseHeaderSize)
            {
                return header;
            }

            offset += headerSize;
        }

        throw new InvalidDataException("DL ISO table of contents does not contain an armor WAD header.");
    }

    private static byte[] ReadStandaloneHeader(Stream wadStream)
    {
        Span<byte> sizeBytes = stackalloc byte[sizeof(int)];
        wadStream.Position = 0;
        wadStream.ReadExactly(sizeBytes);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
        if (headerSize is not StandardHeaderSize and not JapaneseHeaderSize)
        {
            throw new InvalidDataException(
                $"Unsupported DL armor WAD header size 0x{headerSize:X}; expected 0x{StandardHeaderSize:X} or 0x{JapaneseHeaderSize:X}.");
        }

        if (wadStream.Length < AlignToSector(headerSize))
        {
            throw new InvalidDataException("DL armor WAD is truncated before its payload.");
        }

        var header = new byte[headerSize];
        sizeBytes.CopyTo(header);
        wadStream.ReadExactly(header.AsSpan(sizeof(int)));
        return header;
    }

    private static int ReadAndValidateHeaderSize(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8)
        {
            throw new InvalidDataException("DL armor WAD header is truncated.");
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (headerSize is not StandardHeaderSize and not JapaneseHeaderSize)
        {
            throw new InvalidDataException(
                $"Unsupported DL armor WAD header size 0x{headerSize:X}; expected 0x{StandardHeaderSize:X} or 0x{JapaneseHeaderSize:X}.");
        }

        if (header.Length < headerSize)
        {
            throw new InvalidDataException("DL armor WAD header is truncated.");
        }

        return headerSize;
    }

    private static DlArmorSectorRange ReadSectorRange(ReadOnlySpan<byte> header, int offset)
    {
        return new DlArmorSectorRange(
            BinaryPrimitives.ReadInt32LittleEndian(header[offset..]),
            BinaryPrimitives.ReadInt32LittleEndian(header[(offset + 4)..]));
    }

    private static byte[] ReadIsoRange(Stream isoStream, int payloadSector, DlArmorSectorRange range)
    {
        if (range.Offset < 0 || range.Length < 0)
        {
            throw new InvalidDataException("DL armor WAD contains a negative sector range.");
        }

        return DlLevelInfoReader.ReadAbsoluteSectorRange(
            isoStream,
            checked(payloadSector + range.Offset),
            range.Length);
    }

    private static byte[] ReadPayloadRange(ReadOnlySpan<byte> payload, DlArmorSectorRange range)
    {
        if (range.Offset < 0 || range.Length < 0)
        {
            throw new InvalidDataException("DL armor WAD contains a negative sector range.");
        }

        var offset = checked(range.Offset * DlLevelConstants.SectorSize);
        var length = checked(range.Length * DlLevelConstants.SectorSize);
        if ((long)offset + length > payload.Length)
        {
            throw new InvalidDataException("DL armor WAD sector range exceeds the payload bounds.");
        }

        return payload.Slice(offset, length).ToArray();
    }

    private static byte[] ReadWadRange(Stream wadStream, int payloadOffset, DlArmorSectorRange range)
    {
        if (range.Offset < 0 || range.Length < 0)
        {
            throw new InvalidDataException("DL armor WAD contains a negative sector range.");
        }

        var offset = checked((long)payloadOffset + ((long)range.Offset * DlLevelConstants.SectorSize));
        var length = checked((long)range.Length * DlLevelConstants.SectorSize);
        if (offset + length > wadStream.Length || length > int.MaxValue)
        {
            throw new InvalidDataException("DL armor WAD sector range exceeds the file bounds.");
        }

        wadStream.Position = offset;
        return wadStream.ReadBytesExactly((int)length);
    }

    private static int GetPayloadSectorCount(ReadOnlySpan<byte> header)
    {
        var maximumEndSector = 0;
        for (var offset = ArmorEntryOffset; offset <= header.Length - 8; offset += 8)
        {
            var range = ReadSectorRange(header, offset);
            if (range.Offset < 0 || range.Length < 0)
            {
                throw new InvalidDataException("DL armor WAD contains a negative sector range.");
            }

            maximumEndSector = Math.Max(maximumEndSector, checked(range.Offset + range.Length));
        }

        return maximumEndSector;
    }

    private static int AlignToSector(int value)
    {
        return checked((value + DlLevelConstants.SectorSize - 1) / DlLevelConstants.SectorSize
            * DlLevelConstants.SectorSize);
    }

    private static void WriteZeroPadding(Stream output, int length)
    {
        Span<byte> zeros = stackalloc byte[256];
        while (length > 0)
        {
            var count = Math.Min(length, zeros.Length);
            output.Write(zeros[..count]);
            length -= count;
        }
    }

    private static void CopyExactly(Stream input, Stream output, long length)
    {
        var buffer = new byte[128 * 1024];
        while (length > 0)
        {
            var count = input.Read(buffer, 0, (int)Math.Min(length, buffer.Length));
            if (count == 0)
            {
                throw new EndOfStreamException();
            }

            output.Write(buffer, 0, count);
            length -= count;
        }
    }

    private static void ValidateIsoStream(Stream isoStream)
    {
        ArgumentNullException.ThrowIfNull(isoStream);
        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The DL ISO stream must be readable and seekable.", nameof(isoStream));
        }
    }

    private static void ValidateWadStream(Stream wadStream)
    {
        ArgumentNullException.ThrowIfNull(wadStream);
        if (!wadStream.CanRead || !wadStream.CanSeek)
        {
            throw new ArgumentException("The DL armor WAD stream must be readable and seekable.", nameof(wadStream));
        }

        if (wadStream.Length < sizeof(int))
        {
            throw new InvalidDataException("DL armor WAD is truncated.");
        }
    }
}

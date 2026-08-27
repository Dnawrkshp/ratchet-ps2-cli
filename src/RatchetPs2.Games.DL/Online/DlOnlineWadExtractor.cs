using System.Buffers.Binary;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.DL.Online;

public sealed record DlOnlineWadExtraction(
    int HeaderSize,
    int SourcePayloadSector,
    int PayloadSectorCount);

public static class DlOnlineWadExtractor
{
    public const int HeaderSize = 0x68;

    private const int TableOfContentsSector = 1001;
    private const int MaximumTableOfContentsSize = 0x200000;
    private const int MinimumOnlineDataSectorCount = 0x75e;
    private const int MaximumOnlineDataSectorCount = 0x1000;

    public static DlOnlineWadExtraction ExtractFromIso(Stream isoStream, Stream output)
    {
        ValidateIsoStream(isoStream);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The multiplayer WAD output stream must be writable.", nameof(output));
        }

        var header = FindHeaderInIsoTableOfContents(isoStream);
        var payloadSector = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (payloadSector <= 0)
        {
            throw new InvalidDataException("DL online WAD has an invalid payload sector.");
        }

        var payloadSectorCount = GetPayloadSectorCount(header);
        var payloadOffset = checked((long)payloadSector * DlLevelConstants.SectorSize);
        var payloadLength = checked((long)payloadSectorCount * DlLevelConstants.SectorSize);
        if (payloadOffset + payloadLength > isoStream.Length)
        {
            throw new InvalidDataException("DL online WAD payload exceeds the ISO bounds.");
        }

        output.Write(header);
        WriteZeroPadding(output, DlLevelConstants.SectorSize - header.Length);
        isoStream.Position = payloadOffset;
        CopyExactly(isoStream, output, payloadLength);
        return new DlOnlineWadExtraction(HeaderSize, payloadSector, payloadSectorCount);
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
            if (IsOnlineHeader(header))
            {
                return header;
            }

            offset += headerSize;
        }

        throw new InvalidDataException("DL ISO table of contents does not contain an online multiplayer WAD header.");
    }

    private static bool IsOnlineHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderSize)
        {
            return false;
        }

        var dataSectorCount = BinaryPrimitives.ReadInt32LittleEndian(header[0x0c..]);
        var firstTransitionSectorCount = BinaryPrimitives.ReadInt32LittleEndian(header[0x14..]);
        return dataSectorCount is >= MinimumOnlineDataSectorCount and <= MaximumOnlineDataSectorCount
            && firstTransitionSectorCount != 1;
    }

    private static int GetPayloadSectorCount(ReadOnlySpan<byte> header)
    {
        var maximumEndSector = 0;
        for (var offset = 0x08; offset <= header.Length - 8; offset += 8)
        {
            var rangeOffset = BinaryPrimitives.ReadInt32LittleEndian(header[offset..]);
            var rangeLength = BinaryPrimitives.ReadInt32LittleEndian(header[(offset + 4)..]);
            if (rangeOffset < 0 || rangeLength < 0)
            {
                throw new InvalidDataException("DL online WAD contains a negative sector range.");
            }

            maximumEndSector = Math.Max(maximumEndSector, checked(rangeOffset + rangeLength));
        }

        return maximumEndSector;
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
}

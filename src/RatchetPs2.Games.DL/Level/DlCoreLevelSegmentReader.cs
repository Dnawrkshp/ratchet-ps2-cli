using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Games.DL.Level;

public static class DlCoreLevelSegmentReader
{
    public static IReadOnlyList<DlCoreLevelSegment> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < DlLevelConstants.CoreLevelSegmentTableLength)
        {
            throw new InvalidDataException("DL core level data is too small to contain the 14-slot segment table.");
        }

        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var segments = new List<DlCoreLevelSegment>(DlLevelConstants.CoreLevelSegmentCount);

        for (var i = 0; i < DlLevelConstants.CoreLevelSegmentCount; i++)
        {
            var headerOffset = i * 8;
            stream.Position = headerOffset;
            var offset = stream.ReadInt32LittleEndian();
            var length = stream.ReadInt32LittleEndian();

            if (offset <= 0 || length <= 0)
            {
                continue;
            }

            if (offset < 0 || length < 0 || (long)offset + length > data.Length)
            {
                throw new InvalidDataException(
                    $"DL core segment 0x{headerOffset:X2} points outside core level bounds.");
            }

            var rawBytes = data.Slice(offset, length).ToArray();
            var wasCompressed = BinaryMagic.IsWad(rawBytes);
            var payloadBytes = rawBytes;
            var extension = ".bin";

            if (wasCompressed)
            {
                try
                {
                    payloadBytes = WadCompression.Decompress(rawBytes);
                    extension = ".wad";
                }
                catch (InvalidDataException)
                {
                    wasCompressed = false;
                }
            }

            segments.Add(new DlCoreLevelSegment(
                i,
                headerOffset,
                offset,
                length,
                $"{headerOffset:X2}",
                GetSemanticName(headerOffset),
                rawBytes,
                payloadBytes,
                wasCompressed,
                extension));
        }

        return segments;
    }

    private static string GetSemanticName(int headerOffset)
    {
        return headerOffset switch
        {
            0x00 => "moby8355_pvars",
            0x08 => "bin",
            0x10 => "asset_header",
            0x18 => "palette",
            0x20 => "hud_header",
            0x28 => "hud_bank_0",
            0x30 => "hud_bank_1",
            0x38 => "hud_bank_2",
            0x40 => "hud_bank_3",
            0x48 => "hud_bank_4",
            0x50 => "asset_wad",
            0x58 => "art_instances",
            0x60 => "gameplay_core",
            0x68 => "global_nav_data",
            _ => $"segment_{headerOffset:X2}"
        };
    }
}

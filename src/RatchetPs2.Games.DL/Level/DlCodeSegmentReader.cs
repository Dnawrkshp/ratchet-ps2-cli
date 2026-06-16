using System.Buffers.Binary;

namespace RatchetPs2.Games.DL.Level;

public static class DlCodeSegmentReader
{
    public const int RecordHeaderLength = 0x10;

    public static DlCodeSegment Read(ReadOnlySpan<byte> data)
    {
        var records = new List<DlCodePatchRecord>();
        var offset = 0;

        while (offset < data.Length)
        {
            if (data.Length - offset < RecordHeaderLength)
            {
                break;
            }

            var header = data.Slice(offset, RecordHeaderLength);
            var payloadSize = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
            if (payloadSize < 0 || (long)offset + RecordHeaderLength + payloadSize > data.Length)
            {
                break;
            }

            var payloadOffset = offset + RecordHeaderLength;
            records.Add(new DlCodePatchRecord(
                records.Count,
                offset,
                BinaryPrimitives.ReadUInt32LittleEndian(header[0..4]),
                payloadSize,
                BinaryPrimitives.ReadInt32LittleEndian(header[8..12]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]),
                header.ToArray(),
                data.Slice(payloadOffset, payloadSize).ToArray()));

            offset = payloadOffset + payloadSize;
        }

        return new DlCodeSegment(
            data.Length,
            records,
            offset,
            data[offset..].ToArray());
    }
}

public sealed record DlCodeSegment(
    int Length,
    IReadOnlyList<DlCodePatchRecord> Records,
    int ParsedLength,
    byte[] UnparsedTail);

public sealed record DlCodePatchRecord(
    int Index,
    int Offset,
    uint InjectAddress,
    int PayloadSize,
    int Type,
    uint EntrypointAddress,
    byte[] HeaderBytes,
    byte[] PayloadBytes);

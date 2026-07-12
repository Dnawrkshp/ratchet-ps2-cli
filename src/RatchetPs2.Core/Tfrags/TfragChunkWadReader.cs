using RatchetPs2.Core.IO;
using RatchetPs2.Core.Wad;

namespace RatchetPs2.Core.Tfrags;

public static class TfragChunkWadReader
{
    private const int HeaderSize = 0x10;

    public static byte[] ReadTerrainPayload(ReadOnlySpan<byte> chunkBytes)
    {
        if (!TryGetTerrainPayload(chunkBytes, out var payload))
        {
            return [];
        }

        return BinaryMagic.IsWad(payload)
            ? WadCompression.Decompress(payload)
            : payload.ToArray();
    }

    private static bool TryGetTerrainPayload(ReadOnlySpan<byte> chunkBytes, out ReadOnlySpan<byte> payload)
    {
        payload = [];
        if (chunkBytes.Length < HeaderSize)
        {
            return false;
        }

        var payloadOffset = BinarySpanReader.ReadInt32LittleEndian(chunkBytes, 0x00);
        var nextPayloadOffset = BinarySpanReader.ReadInt32LittleEndian(chunkBytes, 0x04);
        if (payloadOffset < HeaderSize || payloadOffset >= chunkBytes.Length)
        {
            return false;
        }

        var payloadEnd = nextPayloadOffset > payloadOffset && nextPayloadOffset <= chunkBytes.Length
            ? nextPayloadOffset
            : chunkBytes.Length;
        payload = chunkBytes[payloadOffset..payloadEnd];
        return payload.Length > 0;
    }
}

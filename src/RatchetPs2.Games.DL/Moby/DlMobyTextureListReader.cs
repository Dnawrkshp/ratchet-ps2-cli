using System.Buffers.Binary;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.DL.Moby;

public static class DlMobyTextureListReader
{
    private const int MaximumTextureCount = 0x1000;

    public static IReadOnlyList<PifTextureData> Read(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return [];
        }

        if (data.Length < sizeof(int))
        {
            throw new InvalidDataException("DL moby texture list is truncated.");
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (count < 0 || count > MaximumTextureCount || 4L + (count * 4L) > data.Length)
        {
            throw new InvalidDataException($"DL moby texture count {count} exceeds the texture block bounds.");
        }

        var textures = new PifTextureData[count];
        for (var index = 0; index < count; index++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data[(4 + (index * 4))..]);
            if (offset <= 0 || offset > data.Length - PifHeader.SizeInBytes)
            {
                throw new InvalidDataException($"DL moby texture {index} points outside its texture block.");
            }

            var texture = PifReader.Read(data[offset..]);
            var serializedSize = PifWriter.GetSerializedSize(texture);
            if ((long)offset + serializedSize > data.Length)
            {
                throw new InvalidDataException($"DL moby texture {index} exceeds its texture block.");
            }

            textures[index] = texture;
        }

        return textures;
    }
}

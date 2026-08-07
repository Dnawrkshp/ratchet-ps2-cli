using System.Buffers.Binary;
using RatchetPs2.Core.Textures.Pif;

namespace RatchetPs2.Games.DL.Level;

public static class DlMissionMobyBankReader
{
    private const int HeaderSize = 0x10;
    private const int DefinitionSize = 0x10;

    public static IReadOnlyList<DlMissionMoby> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (count < 0 || count > (data.Length - HeaderSize) / DefinitionSize)
        {
            throw new InvalidDataException($"DL mission moby count {count} exceeds the model bank bounds.");
        }

        var definitions = new DlMissionMobyDefinition[count];
        var offsets = new SortedSet<int> { data.Length };
        for (var i = 0; i < count; i++)
        {
            var definition = data.Slice(HeaderSize + (i * DefinitionSize), DefinitionSize);
            definitions[i] = new DlMissionMobyDefinition(
                i,
                BinaryPrimitives.ReadInt32LittleEndian(definition),
                BinaryPrimitives.ReadInt32LittleEndian(definition[0x04..]),
                BinaryPrimitives.ReadInt32LittleEndian(definition[0x08..]));
            AddOffset(offsets, definitions[i].ModelOffset, data.Length);
            AddOffset(offsets, definitions[i].TextureOffset, data.Length);
        }

        var mobys = new DlMissionMoby[count];
        for (var i = 0; i < count; i++)
        {
            var definition = definitions[i];
            mobys[i] = new DlMissionMoby(
                definition,
                ReadBlock(data, definition.ModelOffset, offsets),
                ReadTextures(data, definition.TextureOffset, offsets));
        }

        return mobys;
    }

    private static IReadOnlyList<byte[]> ReadTextures(
        ReadOnlySpan<byte> data,
        int offset,
        SortedSet<int> bankOffsets)
    {
        if (offset <= 0 || offset > data.Length - HeaderSize)
        {
            return [];
        }

        var textureCount = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
        if (textureCount < 0 || textureCount > (data.Length - offset - sizeof(int)) / sizeof(int))
        {
            throw new InvalidDataException($"DL mission texture count {textureCount} exceeds the model bank bounds.");
        }

        var textures = new byte[textureCount][];
        for (var i = 0; i < textureCount; i++)
        {
            var relativeOffset = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4 + (i * 4))..]);
            var textureOffset = checked(offset + relativeOffset);
            if (relativeOffset <= 0 || textureOffset > data.Length - PifHeader.SizeInBytes)
            {
                throw new InvalidDataException($"DL mission texture {i} points outside the model bank.");
            }

            var texture = PifReader.Read(data[textureOffset..]);
            var textureLength = PifWriter.GetSerializedSize(texture);
            var bankEnd = bankOffsets.GetViewBetween(textureOffset + 1, data.Length).Min;
            if ((long)textureOffset + textureLength > bankEnd)
            {
                throw new InvalidDataException($"DL mission texture {i} overlaps the next model bank block.");
            }

            textures[i] = data.Slice(textureOffset, textureLength).ToArray();
        }

        return textures;
    }

    private static byte[] ReadBlock(ReadOnlySpan<byte> data, int offset, SortedSet<int> offsets)
    {
        if (offset <= 0 || offset >= data.Length)
        {
            return [];
        }

        var end = offsets.GetViewBetween(offset + 1, data.Length).Min;
        return data.Slice(offset, end - offset).ToArray();
    }

    private static void AddOffset(SortedSet<int> offsets, int offset, int length)
    {
        if (offset > 0 && offset < length)
        {
            offsets.Add(offset);
        }
    }
}

public sealed record DlMissionMobyDefinition(
    int Index,
    int ClassId,
    int ModelOffset,
    int TextureOffset);

public sealed record DlMissionMoby(
    DlMissionMobyDefinition Definition,
    byte[] ModelBytes,
    IReadOnlyList<byte[]> PifTextures);

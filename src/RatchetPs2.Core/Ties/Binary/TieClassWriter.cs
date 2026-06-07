namespace RatchetPs2.Core.Ties;

public static class TieClassWriter
{
    public static byte[] Build(TieClass tie)
    {
        ArgumentNullException.ThrowIfNull(tie);

        var output = new byte[tie.ByteLength];
        var cursor = 0;
        foreach (var section in tie.FileSections.OrderBy(section => section.Offset))
        {
            if (section.Offset != cursor)
            {
                throw new InvalidDataException(
                    $"Tie raw sections are not contiguous at 0x{cursor:X}; next section starts at 0x{section.Offset:X}.");
            }

            if (section.Length != section.Bytes.Length)
            {
                throw new InvalidDataException(
                    $"Tie raw section '{section.Name}' length {section.Length} does not match byte length {section.Bytes.Length}.");
            }

            if (section.Offset + section.Length > output.Length)
            {
                throw new InvalidDataException(
                    $"Tie raw section '{section.Name}' extends past output size 0x{output.Length:X}.");
            }

            Array.Copy(section.Bytes, 0, output, section.Offset, section.Length);
            cursor += section.Length;
        }

        if (cursor != output.Length)
        {
            throw new InvalidDataException(
                $"Tie raw sections ended at 0x{cursor:X}, expected 0x{output.Length:X}.");
        }

        return output;
    }
}

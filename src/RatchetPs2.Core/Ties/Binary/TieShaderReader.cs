using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TieShaderReader
{
    public static List<TieShader> Read(byte[] bytes, TieClassHeader header)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(header);

        var shaders = new List<TieShader>(header.TextureCount);
        if (header.ShadersOffset == 0 || header.TextureCount == 0)
        {
            return shaders;
        }

        var offset = CheckedOffset(header.ShadersOffset, "shader table");
        EnsureRange(bytes, offset, header.TextureCount * TieShader.Size, "shader table");

        for (var i = 0; i < header.TextureCount; i++)
        {
            var shaderOffset = offset + i * TieShader.Size;
            shaders.Add(new TieShader
            {
                Index = i,
                Offset = shaderOffset,
                ClampU = BitConverter.ToInt32(bytes, shaderOffset + 0x30) != 0,
                ClampV = BitConverter.ToInt32(bytes, shaderOffset + 0x34) != 0,
                Bytes = Slice(bytes, shaderOffset, TieShader.Size)
            });
        }

        return shaders;
    }
}

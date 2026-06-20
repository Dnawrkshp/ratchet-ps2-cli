namespace RatchetPs2.Core.Ties;

internal static class TiePassFlags
{
    public const int QwordSize = 0x10;
    public const int GeneratedEnvPassHeaderQwords = 3;
    public const int GeneratedEnvPassBleedColorQwordOffset = 1;
    public const int GlowEmissionPassFlags = 0x08;
    public const int ReflectiveMaskPassFlags = 0x0A;
    public const int TextureMatrixMask = 0x01;
    public const int EnvironmentPassMask = 0x06;
    public const int TextureMatrixSelectorMask = 0xF0;

    public static bool UsesTextureMatrix(int flags)
    {
        // DL retail tie DMA builders treat packet-table byte +0x0e as pass flags:
        // FUN_00593d90 0x00594158 masks bit 0 then jumps to the texture-matrix setup
        // at 0x00594390; FUN_00595168 has the same path at 0x00595540 -> 0x00595794.
        return (flags & TextureMatrixMask) != 0;
    }

    public static int EnvironmentPassBits(int flags)
    {
        // The envpass branch masks bits 1-2: FUN_00593d90 0x00594244/0x00594248 and
        // FUN_00595168 0x00595618/0x0059561c branch to generated-reflection paths
        // instead of the plain UV path.
        return flags & EnvironmentPassMask;
    }

    public static bool UsesEnvironmentPass(int flags)
    {
        return EnvironmentPassBits(flags) != 0;
    }

    public static int TextureMatrixSelector(int flags)
    {
        // When bit 0 is set, FUN_00593d90 0x00594394-0x005943a8 and FUN_00595168
        // 0x00595798-0x005957ac select a PTR_DAT_0021ff70 texture matrix with
        // (flags & 0xf0) << 2. FUN_00594688 then applies that matrix to packed UVs.
        return (flags & TextureMatrixSelectorMask) >> 4;
    }

    public static string ResolveSecondPassMode(int flags)
    {
        var envBits = EnvironmentPassBits(flags);
        if (envBits != 0)
        {
            return envBits == 0x02
                ? "GeneratedEnvPass"
                : envBits == 0x04
                    ? "GeneratedEnvPassAlt"
                    : "GeneratedEnvPassMixed";
        }

        return UsesTextureMatrix(flags) ? "TextureMatrix" : "None";
    }

    public static string ResolveMultipassUvRole(int flags, int multipassUvSize)
    {
        if (multipassUvSize <= 0)
        {
            return "None";
        }

        if (UsesEnvironmentPass(flags))
        {
            // FUN_00595168 branches on (flags & 0x06) at 0x00595618/0x0059561c.
            // The generated envpass path sets s5 to multipass+0x30 at 0x005959c0,
            // emits count-derived DMA tags at 0x00595a40-0x00595a5c, then calls the
            // selected helper at 0x00595a64. FUN_00594bf0 starts by reading qwords
            // from s5 and unpacking them for VU microcode, so these qwords are
            // generator input, not final GS UV coordinates.
            return "GeneratedEnvPassInput";
        }

        return UsesTextureMatrix(flags) ? "TextureMatrixUv" : "GsUv";
    }

    public static string FormatByteBits(int value)
    {
        return $"0x{value & 0xFF:X2}";
    }
}

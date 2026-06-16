using System.Numerics;

namespace RatchetPs2.Core.Textures;

public static class Ps2Color
{
    public const byte FullOpacityAlpha = 128;
    public const byte FullIntensity = 128;

    public static float NormalizeByteComponent(byte value)
    {
        return value / (float)byte.MaxValue;
    }

    public static float NormalizeIntensityComponent(byte value)
    {
        return Math.Clamp(value / (float)FullIntensity, 0f, 1f);
    }

    public static float NormalizeOpacityAlpha(byte value)
    {
        return Math.Clamp(value / (float)FullOpacityAlpha, 0f, 1f);
    }

    public static byte ExpandOpacityAlpha(byte value)
    {
        return (byte)Math.Min(byte.MaxValue, value * 2);
    }

    public static Vector4 ToGltfVertexColor(byte r, byte g, byte b, byte a)
    {
        return new Vector4(
            NormalizeIntensityComponent(r),
            NormalizeIntensityComponent(g),
            NormalizeIntensityComponent(b),
            NormalizeOpacityAlpha(a));
    }
}

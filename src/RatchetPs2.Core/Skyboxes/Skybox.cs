using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Skyboxes;

public sealed class Skybox
{
    public Skybox(
        SkyboxHeader header,
        IReadOnlyList<SkyboxShell> shells,
        IReadOnlyList<SkyboxTexture> textures,
        IReadOnlyList<SkyboxSprite> sprites,
        byte[]? fxList,
        long byteLength)
    {
        Header = header;
        Shells = shells ?? throw new ArgumentNullException(nameof(shells));
        Textures = textures ?? throw new ArgumentNullException(nameof(textures));
        Sprites = sprites ?? throw new ArgumentNullException(nameof(sprites));
        FxList = fxList;
        ByteLength = byteLength;
    }

    public SkyboxHeader Header { get; }

    public IReadOnlyList<SkyboxShell> Shells { get; }

    public IReadOnlyList<SkyboxTexture> Textures { get; }

    public IReadOnlyList<SkyboxSprite> Sprites { get; }

    public byte[]? FxList { get; }

    public long ByteLength { get; }
}

public sealed record SkyboxHeader(
    SkyboxColor Color,
    short ClearScreen,
    short ShellCount,
    short SpriteCount,
    short SpriteMax,
    short TextureCount,
    short FxCount,
    uint TextureDefOffset,
    uint TextureDataOffset,
    int FxListOffset,
    uint SpritesOffset);

public readonly record struct SkyboxColor(byte R, byte G, byte B, byte A)
{
    public float[] ToGltfFactor()
    {
        return
        [
            SrgbByteToGltfLinear(R),
            SrgbByteToGltfLinear(G),
            SrgbByteToGltfLinear(B),
            A / 255f
        ];
    }

    public static float SrgbByteToGltfLinear(byte channel)
    {
        var srgb = channel / 255f;
        return srgb <= 0.04045f
            ? srgb / 12.92f
            : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
    }
}

public sealed record SkyboxTexture(
    int Index,
    uint PaletteOffset,
    uint TextureOffset,
    int Width,
    int Height,
    byte[] PaletteData,
    byte[] PixelData);

public sealed record SkyboxShell(
    int Index,
    uint Offset,
    short ClusterCount,
    short Flags,
    short RotationX,
    short RotationY,
    short RotationZ,
    short RotationDeltaX,
    short RotationDeltaY,
    short RotationDeltaZ,
    IReadOnlyList<SkyboxCluster> Clusters);

public sealed record SkyboxCluster(
    int Index,
    SkyboxSphere Sphere,
    uint DataOffset,
    short VertexCount,
    short TriangleCount,
    short VertexOffset,
    short TexCoordOffset,
    short TriangleOffset,
    short DataSize,
    byte[] Data,
    IReadOnlyList<SkyboxVertex> Vertices,
    IReadOnlyList<SkyboxTexCoord> TexCoords,
    IReadOnlyList<SkyboxTriangle> Triangles);

public readonly record struct SkyboxSphere(float X, float Y, float Z, float Radius);

public readonly record struct SkyboxVertex(short X, short Y, short Z, short W)
{
    public Vector3 ToGltfPosition(float scale)
    {
        return GltfCoordinateBasis.FromPs2Position(X * scale, Y * scale, Z * scale);
    }

    public Vector4 ToGltfColor()
    {
        return new Vector4(1f, 1f, 1f, ToGltfAlpha());
    }

    public float ToGltfAlpha()
    {
        var alpha = W >= 0x80
            ? byte.MaxValue
            : Math.Clamp(W * 2, 0, byte.MaxValue);
        return alpha / 255f;
    }
}

public readonly record struct SkyboxTexCoord(short S, short T)
{
    public Vector2 ToGltfTexCoord()
    {
        return new Vector2(S / 4096f, T / 4096f);
    }

    public Vector4 ToGltfGouraudColor()
    {
        var rawS = unchecked((ushort)S);
        var rawT = unchecked((ushort)T);
        var r = (byte)(rawS & 0xFF);
        var g = (byte)(rawS >> 8);
        var b = (byte)(rawT & 0xFF);
        var a = (byte)(rawT >> 8);
        var alpha = a >= 0x80
            ? byte.MaxValue
            : Math.Clamp(a * 2, 0, byte.MaxValue);

        return new Vector4(
            SkyboxColor.SrgbByteToGltfLinear(r),
            SkyboxColor.SrgbByteToGltfLinear(g),
            SkyboxColor.SrgbByteToGltfLinear(b),
            alpha / 255f);
    }
}

public readonly record struct SkyboxTriangle(byte A, byte B, byte C, byte TextureId);

public sealed record SkyboxSprite(
    byte Type,
    byte Drawn,
    byte Texture,
    byte GsAlpha,
    int Rgba,
    float Rotation,
    int User,
    Vector4 Position);

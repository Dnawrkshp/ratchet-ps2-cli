namespace RatchetPs2.Core.Textures.Pif;

public sealed class PifTextureData
{
    public PifTextureData(
        PifHeader header,
        PifTextureEncoding encoding,
        byte[] paletteData,
        byte[] pixelData)
        : this(header, encoding, paletteData, pixelData, [])
    {
    }

    public PifTextureData(
        PifHeader header,
        PifTextureEncoding encoding,
        byte[] paletteData,
        byte[] pixelData,
        IReadOnlyList<byte[]> mipPixelData)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Encoding = encoding;
        PaletteData = paletteData ?? throw new ArgumentNullException(nameof(paletteData));
        PixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
        ArgumentNullException.ThrowIfNull(mipPixelData);

        foreach (var mip in mipPixelData)
        {
            ArgumentNullException.ThrowIfNull(mip);
        }

        MipPixelData = mipPixelData.ToArray();
    }

    public PifHeader Header { get; }

    public PifTextureEncoding Encoding { get; }

    public bool IsSwizzled => Header.IsSwizzled;

    public byte[] PaletteData { get; }

    public byte[] PixelData { get; }

    public IReadOnlyList<byte[]> MipPixelData { get; }

    public int TotalMipLevels => 1 + MipPixelData.Count;
}

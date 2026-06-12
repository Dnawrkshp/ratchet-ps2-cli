using System.Numerics;

namespace RatchetPs2.Core.Tfrags;

public sealed class TfragTerrain
{
    public TfragTerrain(
        int byteLength,
        int tfragTableOffset,
        int tfragCount,
        float tfragRadius,
        int totalTfragCount,
        IReadOnlyList<TfragChunk> chunks,
        byte[] sourceBytes)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(sourceBytes);

        ByteLength = byteLength;
        TfragTableOffset = tfragTableOffset;
        TfragCount = tfragCount;
        TfragRadius = tfragRadius;
        TotalTfragCount = totalTfragCount;
        Chunks = chunks;
        SourceBytes = sourceBytes;
    }

    public int ByteLength { get; }

    public int TfragTableOffset { get; }

    public int TfragCount { get; }

    public float TfragRadius { get; }

    public int TotalTfragCount { get; }

    public IReadOnlyList<TfragChunk> Chunks { get; }

    public ReadOnlyMemory<byte> SourceBytes { get; }
}

public sealed record TfragBoundingSphere(float X, float Y, float Z, float Radius)
{
    public Vector4 ToVector4() => new(X, Y, Z, Radius);
}

public sealed record TfragChunk(
    int Index,
    int RecordOffset,
    TfragBoundingSphere BoundingSphere,
    int DataOffsetRaw,
    int DataOffset,
    int DataLength,
    ushort Lod2Offset,
    ushort SharedOffset,
    ushort Lod1Offset,
    ushort Lod0Offset,
    ushort TextureOffset,
    ushort RgbaOffset,
    byte CommonSize,
    byte Lod2Size,
    byte Lod1Size,
    byte Lod0Size,
    byte Lod2RgbaCount,
    byte Lod1RgbaCount,
    byte Lod0RgbaCount,
    byte BaseOnly,
    byte TextureCount,
    byte RgbaSize,
    byte RgbaVerticesLocation,
    byte OcclusionIndexStash,
    byte MSphereCount,
    byte Flags,
    short MSphereOffset,
    short LightOffset,
    short LightVertexStartOffset,
    byte DirectionalLightsOne,
    byte DirectionalLightsUpdated,
    ushort PointLights,
    short CubeOffset,
    short OcclusionIndex,
    byte VertexCount,
    byte TriangleCount,
    short MipDistance,
    IReadOnlyList<TfragTextureEntry> TextureEntries,
    IReadOnlyList<TfragRgba> RgbaEntries)
{
    public byte RgbaCountForLod(int lodIndex)
    {
        return lodIndex switch
        {
            0 => Lod0RgbaCount,
            1 => Lod1RgbaCount,
            2 => Lod2RgbaCount,
            _ => throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "Tfrag LOD index must be 0, 1, or 2.")
        };
    }

    public byte DmaQwordCountForLod(int lodIndex)
    {
        return lodIndex switch
        {
            0 => Lod0Size,
            1 => Lod1Size,
            2 => Lod2Size,
            _ => throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "Tfrag LOD index must be 0, 1, or 2.")
        };
    }
}

public sealed record TfragTextureEntry(
    int Index,
    int Offset,
    int TextureId,
    bool ClampU,
    bool ClampV);

public readonly record struct TfragRgba(byte R, byte G, byte B, byte A);

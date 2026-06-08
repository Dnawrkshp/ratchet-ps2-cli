using System.Numerics;

namespace RatchetPs2.Core.Shrubs;

public sealed class ShrubClass
{
    public required ShrubClassHeader Header { get; init; }
    public required int ByteLength { get; init; }
    public IReadOnlyList<ShrubPacket> Packets { get; init; } = [];
    public IReadOnlyList<ShrubNormal> Normals { get; init; } = [];
    public ShrubBillboard? Billboard { get; init; }
}

public sealed class ShrubClassHeader
{
    public const int Size = 0x40;

    public required Vector4 BoundingSphere { get; init; }
    public float MipDistance { get; init; }
    public ushort ModeBits { get; init; }
    public short InstanceCount { get; init; }
    public int InstancesPointer { get; init; }
    public int BillboardOffset { get; init; }
    public float Scale { get; init; }
    public short OClass { get; init; }
    public short SClass { get; init; }
    public short PacketCount { get; init; }
    public short Padding2A { get; init; }
    public int NormalsOffset { get; init; }
    public int Padding30 { get; init; }
    public short DrawnCount { get; init; }
    public short ScisCount { get; init; }
    public short BillboardCount { get; init; }
    public short Padding3A { get; init; }
    public short Padding3C { get; init; }
    public short Padding3E { get; init; }
}

public sealed record ShrubPacketEntry(int Offset, int Size);

public sealed class ShrubPacket
{
    public required int PacketIndex { get; init; }
    public required ShrubPacketEntry Entry { get; init; }
    public required ShrubPacketHeader Header { get; init; }
    public IReadOnlyList<ShrubPrimitive> Primitives { get; init; } = [];
}

public sealed record ShrubPacketHeader(
    int TextureCount,
    int GifTagCount,
    int VertexCount,
    int VertexOffset);

public abstract record ShrubPrimitive(int GsPacketOffset);

public sealed record ShrubTexturePrimitive(
    int GsPacketOffset,
    int TextureId,
    byte[] Bytes) : ShrubPrimitive(GsPacketOffset);

public sealed record ShrubVertexPrimitive(
    int GsPacketOffset,
    ShrubGeometryType GeometryType,
    IReadOnlyList<ShrubVertex> Vertices) : ShrubPrimitive(GsPacketOffset);

public enum ShrubGeometryType
{
    TriangleList,
    TriangleStrip
}

public sealed record ShrubVertex(
    short X,
    short Y,
    short Z,
    short S,
    short T,
    short H,
    short NormalIndex);

public sealed record ShrubNormal(short X, short Y, short Z, short Padding);

public sealed record ShrubBillboard(
    float FadeDistance,
    float Width,
    float Height,
    float ZOffset,
    byte[] Bytes);

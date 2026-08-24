namespace RatchetPs2.Core.Ties;

public sealed class TieClass
{
    public required TieClassHeader Header { get; init; }
    public required int ByteLength { get; init; }
    public IReadOnlyList<TiePacketTable> PacketTables { get; init; } = [];
    public IReadOnlyList<TiePacketDataBlock> PacketDataBlocks { get; init; } = [];
    public IReadOnlyList<TieLodTopology> LodTopologies { get; init; } = [];
    public IReadOnlyList<TieVertexNormal> VertexNormals { get; init; } = [];
    public IReadOnlyList<TieVertexNormalRemap> VertexNormalRemaps { get; init; } = [];
    public IReadOnlyList<TieRgbaRemapOperation> RgbaRemapOperations { get; init; } = [];
    public IReadOnlyList<TieGlowRgbaRemap> GlowRgbaRemaps { get; init; } = [];
    public IReadOnlyList<TieGlowRgbaVertex> GlowRgbaVertices { get; init; } = [];
    public IReadOnlyList<TieShader> Shaders { get; init; } = [];
    public IReadOnlyList<TieRawSection> FileSections { get; init; } = [];
}

public sealed record TieClassReadOptions
{
    public static TieClassReadOptions Default { get; } = new();

    public int VertexNormalHeaderSize { get; init; } = 0x10;

    public static TieClassReadOptions ForGameProfile(TieGameProfile? profile)
    {
        return new TieClassReadOptions
        {
            VertexNormalHeaderSize = profile?.VertexNormalHeaderSize ?? Default.VertexNormalHeaderSize
        };
    }
}

public sealed class TieClassHeader
{
    public const int Size = 0x80;

    public required uint[] PacketTableOffsets { get; init; }
    public required byte[] PacketCounts { get; init; }
    public byte TextureCount { get; init; }
    public float NearDistance { get; init; }
    public float MediumDistance { get; init; }
    public float FarDistance { get; init; }
    public uint ShadersOffset { get; init; }
    public int InstanceIndex { get; init; }
    public required short[] CacheSizes { get; init; }
    public required ushort[] RgbaRemapOffsets { get; init; }
    public uint AmbientRgbaOffset { get; init; }
    public uint VertexNormalsOffset { get; init; }
    public short VertexNormalsCount { get; init; }
    public short AmbientSize { get; init; }
    public short ModeBits { get; init; }
    public short InstanceCount { get; init; }
    public float Scale { get; init; }
    public short OClass { get; init; }
    public short TClass { get; init; }
    public float MipmapDistance { get; init; }
    public int GlowRgba { get; init; }
    public TieBoundingSphere BoundingSphere { get; init; }
    public required TieLod[] Lods { get; init; }
    public required ushort[] UnknownOffsets78 { get; init; }
    public short Padding { get; init; }
}

public readonly record struct TieBoundingSphere(float X, float Y, float Z, float Radius);

public readonly record struct TieLod(short VertexCount, short TriangleCount, short StripCount, short Padding);

public sealed class TiePacketTable
{
    public required int LodIndex { get; init; }
    public required uint Offset { get; init; }
    public required byte Count { get; init; }
    public IReadOnlyList<TiePacket> Packets { get; init; } = [];
}

public sealed class TiePacket
{
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public uint DataOffset { get; init; }
    public int AbsoluteDataOffset { get; init; }
    public byte ShaderCount { get; init; }
    public byte BfcDistance { get; init; }
    public byte ControlCount { get; init; }
    public byte ControlSize { get; init; }
    public byte VertexOffset { get; init; }
    public byte VertexSize { get; init; }
    public byte RgbaCount { get; init; }
    public byte MultipassOffset { get; init; }
    public byte ScissorOffset { get; init; }
    public byte ScissorSize { get; init; }
    public byte PassFlags { get; init; }
    public byte MultipassUvSize { get; init; }
    public IReadOnlyList<TiePacketShaderReference> ShaderReferences { get; init; } = [];
    public IReadOnlyList<int> ShaderSwitchVuAddresses { get; init; } = [];
}

public sealed class TiePacketShaderReference
{
    public required int Index { get; init; }
    public required int ShaderByteOffset { get; init; }
    public required int ShaderIndex { get; init; }
}

public sealed class TiePacketDataBlock
{
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public required int Offset { get; init; }
    public required int Length { get; init; }
    public required int QwordCount { get; init; }
    public required byte[] Bytes { get; init; }
    public IReadOnlyList<TiePacketDataRegion> Regions { get; init; } = [];
    public IReadOnlyList<TiePacketSetupRow> SetupRows { get; init; } = [];
    public TiePacketUnpackHeader? UnpackHeader { get; init; }
    public IReadOnlyList<TiePacketControlRow> ControlRows { get; init; } = [];
    public IReadOnlyList<TiePacketStripControl> StripControls { get; init; } = [];
    public IReadOnlyList<TiePacketStripToken> StripTokens { get; init; } = [];
    public IReadOnlyList<TiePacketScissorToken> ScissorTokens { get; init; } = [];
    public IReadOnlyList<TiePacketVertexRow> VertexRows { get; init; } = [];
    public IReadOnlyList<TiePacketDecodedVertex> DecodedVertices { get; init; } = [];
    public IReadOnlyList<TiePacketPrimitive> PhysicalPrimitives { get; init; } = [];
    public IReadOnlyList<TiePacketPrimitive> TokenReferencePrimitives { get; init; } = [];
    public IReadOnlyList<TiePacketPrimitive> Primitives { get; init; } = [];
}

public sealed class TiePacketDataRegion
{
    public required string Name { get; init; }
    public required int QwordOffset { get; init; }
    public required int QwordCount { get; init; }
    public required int Offset { get; init; }
    public required int Length { get; init; }
    public required byte[] Bytes { get; init; }
}

public enum TiePacketSetupWordRole
{
    Unknown,
    ShaderSwitchVuAddress,
    ShaderByteOffset
}

public sealed class TiePacketSetupRow
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required byte[] Bytes { get; init; }
    public IReadOnlyList<TiePacketSetupWord> Words { get; init; } = [];
}

public sealed class TiePacketSetupWord
{
    public required int RowIndex { get; init; }
    public required int WordIndex { get; init; }
    public required int Offset { get; init; }
    public required int Raw { get; init; }
    public required TiePacketSetupWordRole Role { get; init; }
}

public sealed class TiePacketControlRow
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required byte Data0 { get; init; }
    public required byte Data1 { get; init; }
    public required byte Data2 { get; init; }
    public required byte Data3 { get; init; }
    public required uint Raw { get; init; }
    public required bool IsStripControl { get; init; }
}

public sealed class TiePacketUnpackHeader
{
    public required byte Unknown0 { get; init; }
    public required byte Unknown1 { get; init; }
    public required byte Unknown2 { get; init; }
    public required byte StripCount { get; init; }
    public required byte Unknown4 { get; init; }
    public required byte Unknown5 { get; init; }
    public required byte Unknown6 { get; init; }
    public required byte Unknown7 { get; init; }
    public required byte DinkyVerticesSizePlusFour { get; init; }
    public required byte FatVerticesSize { get; init; }
    public required byte Unknown10 { get; init; }
    public required byte Unknown11 { get; init; }

    public int DinkyVertexCount => Math.Max(0, (DinkyVerticesSizePlusFour - 4) / 2);
}

public sealed class TiePacketStripControl
{
    public required int Index { get; init; }
    public required int ControlRowIndex { get; init; }
    public required int Offset { get; init; }
    public required byte TokenCount { get; init; }
    public required int TokenOffset { get; init; }
    public required byte VuAddress { get; init; }
    public required byte ControlData1 { get; init; }
    public required byte Flags { get; init; }
    public required byte[] Tokens { get; init; }
    public IReadOnlyList<TiePacketStripToken> DecodedTokens { get; init; } = [];
}

public enum TiePacketStripTokenAddressMode
{
    Unknown,
    AbsoluteVertexWriteOffset,
    ForwardVertexWriteOffsetStep,
    PreviousStripVertexReference
}

public sealed class TiePacketStripToken
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required int StripIndex { get; init; }
    public required int IndexInStrip { get; init; }
    public required byte Value { get; init; }
    public required int SignedValue { get; init; }
    public required TiePacketStripTokenAddressMode AddressMode { get; init; }
    public required int? ResolvedGsPacketWriteOffset { get; init; }
    public required int? ReferencedGsPacketWriteOffset { get; init; }
    public required int ExpectedGsPacketWriteOffset { get; init; }
    public required bool MatchesExpectedGsPacketWriteOffset { get; init; }
    public required bool ReferencesPreviousStripVertex { get; init; }
    public int? RestartGap { get; init; }
}

public sealed class TiePacketScissorToken
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required byte Value { get; init; }
    public required int? StripIndex { get; init; }
    public required bool IsEndToken { get; init; }
}

public sealed class TieLodTopology
{
    public required int LodIndex { get; init; }
    public required int LogicalVertexCount { get; init; }
    public required int PacketVertexRowCount { get; init; }
    public required int PrimaryAddressMappedLogicalVertexCount { get; init; }
    public required int SecondaryAddressMappedLogicalVertexCount { get; init; }
    public required int UnresolvedLogicalVertexCount { get; init; }
    public required int StripCount { get; init; }
    public required int TriangleCount { get; init; }
    public IReadOnlyList<TieLogicalVertex> LogicalVertices { get; init; } = [];
    public IReadOnlyList<TieTriangleStrip> Strips { get; init; } = [];
    public IReadOnlyList<TieTriangle> Triangles { get; init; } = [];
}

public sealed class TieTriangleStrip
{
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public required int PacketStripIndex { get; init; }
    public required int StripIndex { get; init; }
    public required int LogicalVertexStartIndex { get; init; }
    public required int LogicalVertexCount { get; init; }
    public required int TriangleStartIndex { get; init; }
    public required int TriangleCount { get; init; }
    public required byte VuAddress { get; init; }
    public required byte Flags { get; init; }
    public int? ShaderIndex { get; init; }
    public required byte[] Tokens { get; init; }
    public IReadOnlyList<TieLogicalVertex> LogicalVertices { get; init; } = [];
}

public readonly record struct TieTriangle(
    int LodIndex,
    int StripIndex,
    int TriangleIndexInStrip,
    int A,
    int B,
    int C);

public enum TieLogicalVertexMappingKind
{
    Unresolved,
    PrimaryRowAddress,
    SecondaryRowAddress
}

public enum TiePacketVertexRowKind
{
    Unknown,
    DinkyVertex,
    FatVertexHeader,
    FatVertexData
}

public enum TiePacketDecodedVertexKind
{
    Dinky,
    Fat
}

public sealed class TiePacketDecodedVertex
{
    public required int Index { get; init; }
    public required int SourceIndex { get; init; }
    public required TiePacketDecodedVertexKind Kind { get; init; }
    public required int Offset { get; init; }
    public required byte[] Bytes { get; init; }
    public required int SourceRowIndex { get; init; }
    public required TiePacketVertexRow? SourceRow { get; init; }
    public required short X { get; init; }
    public required short Y { get; init; }
    public required short Z { get; init; }
    public required ushort GsPacketWriteOffset { get; init; }
    public required ushort S { get; init; }
    public required ushort T { get; init; }
    public required ushort Q { get; init; }
    public required ushort SecondaryGsPacketWriteOffset { get; init; }
}

public sealed class TiePacketVertexReference
{
    public required int Index { get; init; }
    public required int GsPacketWriteOffset { get; init; }
    public required bool IsSecondaryWriteOffset { get; init; }
    public required TiePacketDecodedVertex Vertex { get; init; }
}

public sealed class TiePacketPrimitive
{
    public required int Index { get; init; }
    public required int PacketStripIndex { get; init; }
    public required int MaterialIndex { get; init; }
    public required bool WindingOrder { get; init; }
    public IReadOnlyList<TiePacketVertexReference> Vertices { get; init; } = [];
}

public sealed class TieLogicalVertex
{
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public required int PacketStripIndex { get; init; }
    public required int StripIndex { get; init; }
    public required int IndexInStrip { get; init; }
    public required int LogicalVertexIndex { get; init; }
    public required int VuAddress { get; init; }
    public required byte Token { get; init; }
    public int? GsPacketWriteOffset { get; init; }
    public required TieLogicalVertexMappingKind MappingKind { get; init; }
    public TiePacketDecodedVertex? DecodedVertex { get; init; }
    public required TiePacketVertexRow? AddressRow { get; init; }
    public required TiePacketVertexRow? VertexRow { get; init; }
    public int? AddressRowIndex => AddressRow?.Index;
    public int? AddressRowOffset => AddressRow?.Offset;
    public int? VertexRowIndex => VertexRow?.Index;
    public int? VertexRowOffset => VertexRow?.Offset;
}

public sealed class TiePacketVertexRow
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public TiePacketVertexRowKind Kind { get; init; } = TiePacketVertexRowKind.Unknown;
    public int? PairedVertexRowIndex { get; init; }
    public required short X { get; init; }
    public required short Y { get; init; }
    public required short Z { get; init; }
    public required short W { get; init; }
    public required short Data0 { get; init; }
    public required short Data1 { get; init; }
    public required short Data2 { get; init; }
    public required short Data3 { get; init; }
    public required float ModelX { get; init; }
    public required float ModelY { get; init; }
    public required float ModelZ { get; init; }
    public short PrimaryVuAddress => W;
    public short SecondaryVuAddress => Data3;
    public bool HasPrimaryVuAddress => PrimaryVuAddress > 0;
    public bool HasSecondaryVuAddress => SecondaryVuAddress > 0;
}

public sealed class TieShader
{
    public const int Size = 0x50;

    public required int Index { get; init; }
    public required int Offset { get; init; }
    public bool ClampU { get; init; }
    public bool ClampV { get; init; }
    public required byte[] Bytes { get; init; }
}

public sealed class TieVertexNormal
{
    public required int Index { get; init; }
    public required int Offset { get; init; }
    public required short X { get; init; }
    public required short Y { get; init; }
    public required short Z { get; init; }
    public required short W { get; init; }
    public ushort Packed { get; init; }
}

public sealed class TieVertexNormalRemap
{
    public required int ChunkIndex { get; init; }
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public required int Offset { get; init; }
    public required int NormalIndex { get; init; }
    public required int VertexRowIndex { get; init; }
    public int? LogicalVertexIndex { get; init; }
    public required ushort RawNormal { get; init; }
    public required ushort RawVertex { get; init; }
}

public enum TieRgbaRemapOperationKind
{
    DirectCopy,
    Average2,
    WeightedAverage3To1,
    WeightedAverage2To1To1,
    Average4
}

public sealed class TieRgbaRemapOperation
{
    // LightTies writes the per-instance glow color at source byte offset 0x7fc.
    public const int ConstantColorSourceSlot = 0x7FC / sizeof(int);

    public required int LodIndex { get; init; }
    public required int GroupIndex { get; init; }
    public required int OperationIndex { get; init; }
    public required int Offset { get; init; }
    public required TieRgbaRemapOperationKind Kind { get; init; }
    public required int GroupTargetSlotBase { get; init; }
    public required int TargetSlot { get; init; }
    public required int[] SourceSlots { get; init; }

    public int TargetCacheSlot => checked(GroupTargetSlotBase + TargetSlot);
}

public readonly record struct TieRgba32(byte R, byte G, byte B, byte A)
{
    public static TieRgba32 FromRaw(int raw)
    {
        var value = unchecked((uint)raw);
        return new TieRgba32(
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24));
    }

    public string ToRgbaHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

public enum TieGlowRgbaRemapResolutionKind
{
    Unresolved,
    PacketVertexRowRange,
    PacketDataOffsetRange,
    PacketShaderRange,
    PacketMultipassRange,
    PacketMultipassSet
}

public sealed class TieGlowRgbaRemap
{
    public required int RemapIndex { get; init; }
    public required int Offset { get; init; }
    public required int RawRgba { get; init; }
    public required TieRgba32 Rgba { get; init; }
    public required TieGlowRgbaRemapResolutionKind ResolutionKind { get; init; }
    public int? ResolvedStartOffset { get; init; }
    public int? EndOffset { get; init; }
    public int? LodIndex { get; init; }
    public int? PacketIndex { get; init; }
    public int? ResolvedPacketIndex { get; init; }
    public IReadOnlyList<int> ResolvedPacketIndices { get; init; } = [];
    public int? ResolvedShaderIndex { get; init; }
    public int? StartVertexRowIndex { get; init; }
    public int? EndVertexRowIndexExclusive { get; init; }
    public int ResolvedPacketCount { get; init; }
    public int ResolvedVertexRowCount { get; init; }
    public int ResolvedLogicalVertexCount { get; init; }
}

public sealed class TieGlowRgbaVertex
{
    public required int RemapIndex { get; init; }
    public required int RemapOffset { get; init; }
    public required int LodIndex { get; init; }
    public required int PacketIndex { get; init; }
    public required int StripIndex { get; init; }
    public required int PacketStripIndex { get; init; }
    public required int IndexInStrip { get; init; }
    public required int LogicalVertexIndex { get; init; }
    public required int VertexRowIndex { get; init; }
    public required int VertexRowOffset { get; init; }
    public required int RawRgba { get; init; }
    public required TieRgba32 Rgba { get; init; }
    public required float GlowWeight { get; init; }
}

public sealed class TieRawSection
{
    public required string Name { get; init; }
    public required int Offset { get; init; }
    public required int Length { get; init; }
    public required byte[] Bytes { get; init; }
}

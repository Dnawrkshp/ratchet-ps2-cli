using System.Numerics;

namespace RatchetPs2.Core.Moby;

public sealed class MobyModel
{
    public MobyAnimationFormat AnimationFormat { get; set; } = MobyAnimationFormat.Standard;
    public MobyAnimationFormat SkeletonFormat { get; set; } = MobyAnimationFormat.Standard;
    public int MeshTableOffset { get; set; }
    public byte HighLodMeshCount { get; set; }
    public byte LowLodMeshCount { get; set; }
    public byte MetalCount { get; set; }
    public byte MetalOffsets { get; set; }
    public byte JointCount { get; set; }
    public byte Padding { get; set; }
    public byte MeshCountType2 { get; set; }
    public byte TeamPalettes { get; set; }
    public byte AnimationCount { get; set; }
    public byte SoundCount { get; set; }
    public byte LodTrans { get; set; }
    public byte Shadow { get; set; }
    public int CollisionOffset { get; set; }
    public int SkeletonOffset { get; set; }
    public int CommonTransOffset { get; set; }
    public int AnimationJointsOffset { get; set; }
    public int GifUsageOffset { get; set; }
    public float Scale { get; set; }
    public int SoundDefOffset { get; set; }
    public byte BangleTableOffset { get; set; }
    public byte MipmapDistance { get; set; }
    public short CornCobOffset { get; set; }
    public MobyBoundingSphere BoundingSphere { get; set; } = new();
    public int GlowRgba { get; set; }
    public short ModeBits { get; set; }
    public byte Type { get; set; }
    public byte ModeBits2 { get; set; }

    public MobyMeshTable? MeshTable { get; set; }
    public MobyCollision? Collision { get; set; }
    public MobyBangleTable? BangleTable { get; set; }
    public MobyCornCob? CornCob { get; set; }
    public List<MobySequence> Sequences { get; } = [];
    public MobySkeleton? Skeleton { get; set; }
    public List<MobyAnimationJoint>? AnimationJoints { get; set; }
    public byte[]? CommonTransforms { get; set; }
    public List<MobyGifTag> GifTags { get; } = [];
    public Dictionary<int, List<byte[]>> TeamPaletteData { get; } = [];
    public List<MobySound>? Sounds { get; set; }
    public byte[]? ShadowData { get; set; }
    public byte[]? ShadowPrefixData { get; set; }
    public byte[]? PreAnimationSectionPadding { get; set; }
}

public enum MobyAnimationFormat
{
    Standard,
    Compact
}

public sealed class MobyBoundingSphere
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }

    public static MobyBoundingSphere Read(BinaryReader reader)
    {
        return new MobyBoundingSphere
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            Radius = reader.ReadSingle()
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(Radius);
    }
}

public enum MobyMeshType
{
    HighLod,
    LowLod,
    MeshType2,
    Bangle,
    Metal
}

public sealed class MobyMeshTable
{
    public List<MobyMeshTableEntry> Entries { get; } = [];
}

public sealed class MobyMeshTableEntry
{
    public int VifListOffset { get; set; }
    public short VifListSize { get; set; }
    public short VifListTextureSize { get; set; }
    public int VertexDataOffset { get; set; }
    public byte VertexDataSize { get; set; }
    // Observed as ceil(VertexCount * 3 / 8), likely a 3-bit-per-vertex control payload byte count.
    public byte Unknown0A { get; set; }
    // Despite the old name, static meshes use this as ceil(VertexCount / 4) even when JointCount is zero.
    public byte CommonTransformJointIndex { get; set; }
    public byte VertexCount { get; set; }
    public MobyMeshType MeshType { get; set; }
    public byte[] VifData { get; set; } = [];
    public byte[] VertexData { get; set; } = [];
    public byte[]? VifTextureData { get; set; }
    public MobyGifTag? GifTag { get; set; }

    public void WriteHeader(BinaryWriter writer)
    {
        writer.Write(VifListOffset);
        writer.Write(VifListSize);
        writer.Write(VifListTextureSize);
        writer.Write(VertexDataOffset);
        writer.Write(VertexDataSize);
        writer.Write(Unknown0A);
        writer.Write(CommonTransformJointIndex);
        writer.Write(VertexCount);
    }
}

public sealed class MobyGifTag
{
    public byte[] TextureIds { get; set; } = new byte[0x0C];
    public uint GifDataOffset { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(TextureIds);
        writer.Write(GifDataOffset);
    }
}

public sealed class MobySkeleton
{
    public List<MobyMatrix4> Bones { get; } = [];
}

public sealed class MobyMatrix4
{
    public MobyMatrixRow Row1 { get; set; } = new();
    public MobyMatrixRow Row2 { get; set; } = new();
    public MobyMatrixRow Row3 { get; set; } = new();
    public MobyMatrixRow Row4 { get; set; } = new();

    public static MobyMatrix4 Read(BinaryReader reader)
    {
        return new MobyMatrix4
        {
            Row1 = MobyMatrixRow.Read(reader),
            Row2 = MobyMatrixRow.Read(reader),
            Row3 = MobyMatrixRow.Read(reader),
            Row4 = MobyMatrixRow.Read(reader)
        };
    }

    public static MobyMatrix4 ReadCompact(BinaryReader reader)
    {
        return new MobyMatrix4
        {
            Row1 = MobyMatrixRow.Read(reader),
            Row2 = MobyMatrixRow.Read(reader),
            Row3 = MobyMatrixRow.Read(reader),
            Row4 = new MobyMatrixRow { W = 1f }
        };
    }

    public void Write(BinaryWriter writer)
    {
        Row1.Write(writer);
        Row2.Write(writer);
        Row3.Write(writer);
        Row4.Write(writer);
    }

    public void WriteCompact(BinaryWriter writer)
    {
        Row1.Write(writer);
        Row2.Write(writer);
        Row3.Write(writer);
    }
}

public sealed class MobyMatrixRow
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public static MobyMatrixRow Read(BinaryReader reader)
    {
        return new MobyMatrixRow
        {
            X = reader.ReadSingle(),
            Y = reader.ReadSingle(),
            Z = reader.ReadSingle(),
            W = reader.ReadSingle()
        };
    }

    public void Write(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Y);
        writer.Write(Z);
        writer.Write(W);
    }
}

public sealed class MobyAnimationJoint
{
    public short SubSkeletonTokenOffset { get; set; }
    public short AnimationJointFlagsOrAuxIndex { get; set; }
    public byte[] Data { get; set; } = [];

    public void Write(BinaryWriter writer)
    {
        writer.Write(SubSkeletonTokenOffset);
        writer.Write(AnimationJointFlagsOrAuxIndex);
        writer.Write(Data);
    }
}

public sealed class MobySequence
{
    public MobyAnimationFormat Format { get; set; } = MobyAnimationFormat.Standard;
    public byte[]? RawData { get; set; }
    public MobyBoundingSphere BoundingSphere { get; set; } = new();
    public byte FrameCount { get; set; }
    public byte Sound { get; set; }
    public byte TriggerCount { get; set; }
    public byte Padding { get; set; }
    public int Unknown14 { get; set; }
    public int Unknown18 { get; set; }
    public List<uint> FrameOffsets { get; } = [];
    public int CompactTriggerOffset { get; set; }
    public int CompactAnimDataOffset { get; set; }
    public int CompactFrameDataOffset { get; set; }
    public List<MobyCompactAnimationFrame> CompactFrames { get; } = [];
    public byte[] CompactAnimInfoData { get; set; } = new byte[0x08];
    public byte[] CompactFrameData { get; set; } = [];
    public List<MobyAnimationTrigger> Triggers { get; } = [];
    public List<MobyAnimationFrame> Frames { get; } = [];

    public void WriteHeader(BinaryWriter writer)
    {
        BoundingSphere.Write(writer);
        writer.Write(FrameCount);
        writer.Write(Sound);
        writer.Write(TriggerCount);
        writer.Write(Padding);
        writer.Write(Unknown14);
        writer.Write(Unknown18);
    }
}

public sealed class MobyCompactAnimationFrame
{
    public short Unknown00 { get; set; }
    public short FrameId { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(FrameId);
    }
}

public sealed class MobyAnimationFrame
{
    public byte Unknown00 { get; set; }
    public byte Unknown01 { get; set; }
    public byte Unknown02 { get; set; }
    public byte Unknown03 { get; set; }
    public byte Unknown04 { get; set; }
    public byte Unknown05 { get; set; }
    public byte FrameDataSize { get; set; }
    public byte Unknown07 { get; set; }
    public int Unknown08 { get; set; }
    public int Unknown0C { get; set; }
    public byte[] FrameData { get; set; } = [];

    public void WriteHeader(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Unknown01);
        writer.Write(Unknown02);
        writer.Write(Unknown03);
        writer.Write(Unknown04);
        writer.Write(Unknown05);
        writer.Write(FrameDataSize);
        writer.Write(Unknown07);
        writer.Write(Unknown08);
        writer.Write(Unknown0C);
    }
}

public sealed class MobyAnimationTrigger
{
    public short Unknown00 { get; set; }
    public short Unknown02 { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Unknown02);
    }
}

public sealed class MobyBangleTable
{
    public byte Unknown00 { get; set; }
    public byte BangleCount { get; set; }
    public byte Unknown02 { get; set; }
    public byte Unknown03 { get; set; }
    public List<MobyBangleListEntry> OffsetList { get; } = [];
    public List<MobyBangleData> DataList { get; } = [];

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(BangleCount);
        writer.Write(Unknown02);
        writer.Write(Unknown03);

        foreach (var entry in OffsetList)
        {
            entry.Write(writer);
        }

        foreach (var data in DataList)
        {
            data.Write(writer);
        }
    }
}

public sealed class MobyBangleListEntry
{
    public short MeshTableIndex { get; set; }
    public short Unknown02 { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(MeshTableIndex);
        writer.Write(Unknown02);
    }
}

public sealed class MobyBangleData
{
    public int Unknown00 { get; set; }
    public int Unknown04 { get; set; }
    public int Unknown08 { get; set; }
    public int Unknown0C { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Unknown04);
        writer.Write(Unknown08);
        writer.Write(Unknown0C);
    }
}

public sealed class MobyCornCob
{
    public byte[] KernelOffsets { get; set; } = new byte[0x10];
    public List<MobyCornKernel?> Kernels { get; } = [];
    public byte[]? RawData { get; set; }
}

public sealed class MobyCornKernel
{
    public Vector4 Vector { get; set; }
    public List<MobyKernelVertex> Vertices { get; } = [];

    public void Write(BinaryWriter writer)
    {
        writer.Write(Vector.X);
        writer.Write(Vector.Y);
        writer.Write(Vector.Z);
        writer.Write(Vector.W);

        foreach (var vertex in Vertices)
        {
            vertex.Write(writer);
        }
    }
}

public sealed class MobyKernelVertex
{
    public int Unknown00 { get; set; }
    public short Unknown04 { get; set; }
    public short VertexCount { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Unknown04);
        writer.Write(VertexCount);
    }
}

public sealed class MobyCollision
{
    public int Unknown00 { get; set; }
    public int Size1 { get; set; }
    public int Size2 { get; set; }
    public int Size3 { get; set; }
    public byte[]? Data1 { get; set; }
    public byte[]? Data2 { get; set; }
    public byte[]? Data3 { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Size1);
        writer.Write(Size2);
        writer.Write(Size3);
        if (Data1 is not null)
        {
            writer.Write(Data1);
        }
        if (Data2 is not null)
        {
            writer.Write(Data2);
        }
        if (Data3 is not null)
        {
            writer.Write(Data3);
        }
    }
}

public sealed class MobySound
{
    public float MinRange { get; set; }
    public float MaxRange { get; set; }
    public int MinVolume { get; set; }
    public int MaxVolume { get; set; }
    public int MinPitch { get; set; }
    public int MaxPitch { get; set; }
    public byte Loop { get; set; }
    public byte Flags { get; set; }
    public short Index { get; set; }
    public int BankIndex { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(MinRange);
        writer.Write(MaxRange);
        writer.Write(MinVolume);
        writer.Write(MaxVolume);
        writer.Write(MinPitch);
        writer.Write(MaxPitch);
        writer.Write(Loop);
        writer.Write(Flags);
        writer.Write(Index);
        writer.Write(BankIndex);
    }
}

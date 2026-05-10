using System.Numerics;

namespace RatchetPs2.Games.UYA.Moby;

public sealed class UyaMobyModel
{
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
    public UyaBoundingSphere BoundingSphere { get; set; } = new();
    public int GlowRgba { get; set; }
    public short ModeBits { get; set; }
    public byte Type { get; set; }
    public byte ModeBits2 { get; set; }

    public UyaMobyMeshTable? MeshTable { get; set; }
    public UyaMobyCollision? Collision { get; set; }
    public UyaMobyBangleTable? BangleTable { get; set; }
    public UyaMobyCornCob? CornCob { get; set; }
    public List<UyaMobySequence> Sequences { get; } = [];
    public UyaMobySkeleton? Skeleton { get; set; }
    public List<UyaMobyAnimationJoint>? AnimationJoints { get; set; }
    public byte[]? CommonTransforms { get; set; }
    public List<UyaMobyGifTag> GifTags { get; } = [];
    public Dictionary<int, List<byte[]>> TeamPaletteData { get; } = [];
    public List<UyaMobySound>? Sounds { get; set; }
    public byte[]? ShadowData { get; set; }
    public byte[]? ShadowPrefixData { get; set; }
}

public sealed class UyaBoundingSphere
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; }

    public static UyaBoundingSphere Read(BinaryReader reader)
    {
        return new UyaBoundingSphere
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

public enum UyaMobyMeshType
{
    HighLod,
    LowLod,
    MeshType2,
    Bangle,
    Metal
}

public sealed class UyaMobyMeshTable
{
    public List<UyaMobyMeshTableEntry> Entries { get; } = [];
}

public sealed class UyaMobyMeshTableEntry
{
    public int VifListOffset { get; set; }
    public short VifListSize { get; set; }
    public short VifListTextureSize { get; set; }
    public int VertexDataOffset { get; set; }
    public byte VertexDataSize { get; set; }
    public byte Unknown0A { get; set; }
    public byte CommonTransformJointIndex { get; set; }
    public byte VertexCount { get; set; }
    public UyaMobyMeshType MeshType { get; set; }
    public byte[] VifData { get; set; } = [];
    public byte[] VertexData { get; set; } = [];
    public byte[]? VifTextureData { get; set; }
    public UyaMobyGifTag? GifTag { get; set; }

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

public sealed class UyaMobyGifTag
{
    public byte[] TextureIds { get; set; } = new byte[0x0C];
    public uint GifDataOffset { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(TextureIds);
        writer.Write(GifDataOffset);
    }
}

public sealed class UyaMobySkeleton
{
    public List<UyaMatrix4> Bones { get; } = [];
}

public sealed class UyaMatrix4
{
    public UyaMatrixRow Row1 { get; set; } = new();
    public UyaMatrixRow Row2 { get; set; } = new();
    public UyaMatrixRow Row3 { get; set; } = new();
    public UyaMatrixRow Row4 { get; set; } = new();

    public static UyaMatrix4 Read(BinaryReader reader)
    {
        return new UyaMatrix4
        {
            Row1 = UyaMatrixRow.Read(reader),
            Row2 = UyaMatrixRow.Read(reader),
            Row3 = UyaMatrixRow.Read(reader),
            Row4 = UyaMatrixRow.Read(reader)
        };
    }

    public void Write(BinaryWriter writer)
    {
        Row1.Write(writer);
        Row2.Write(writer);
        Row3.Write(writer);
        Row4.Write(writer);
    }
}

public sealed class UyaMatrixRow
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public static UyaMatrixRow Read(BinaryReader reader)
    {
        return new UyaMatrixRow
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

public sealed class UyaMobyAnimationJoint
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

public sealed class UyaMobySequence
{
    public UyaBoundingSphere BoundingSphere { get; set; } = new();
    public byte FrameCount { get; set; }
    public byte Sound { get; set; }
    public byte TriggerCount { get; set; }
    public byte Padding { get; set; }
    public int Unknown14 { get; set; }
    public int Unknown18 { get; set; }
    public List<uint> FrameOffsets { get; } = [];
    public List<UyaMobyAnimationTrigger> Triggers { get; } = [];
    public List<UyaMobyAnimationFrame> Frames { get; } = [];

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

public sealed class UyaMobyAnimationFrame
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

public sealed class UyaMobyAnimationTrigger
{
    public short Unknown00 { get; set; }
    public short Unknown02 { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(Unknown00);
        writer.Write(Unknown02);
    }
}

public sealed class UyaMobyBangleTable
{
    public byte Unknown00 { get; set; }
    public byte BangleCount { get; set; }
    public byte Unknown02 { get; set; }
    public byte Unknown03 { get; set; }
    public List<UyaMobyBangleListEntry> OffsetList { get; } = [];
    public List<UyaMobyBangleData> DataList { get; } = [];

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

public sealed class UyaMobyBangleListEntry
{
    public short MeshTableIndex { get; set; }
    public short Unknown02 { get; set; }

    public void Write(BinaryWriter writer)
    {
        writer.Write(MeshTableIndex);
        writer.Write(Unknown02);
    }
}

public sealed class UyaMobyBangleData
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

public sealed class UyaMobyCornCob
{
    public byte[] KernelOffsets { get; set; } = new byte[0x10];
    public List<UyaMobyCornKernel?> Kernels { get; } = [];
    public byte[]? RawData { get; set; }
}

public sealed class UyaMobyCornKernel
{
    public Vector4 Vector { get; set; }
    public List<UyaMobyKernelVertex> Vertices { get; } = [];

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

public sealed class UyaMobyKernelVertex
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

public sealed class UyaMobyCollision
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

public sealed class UyaMobySound
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

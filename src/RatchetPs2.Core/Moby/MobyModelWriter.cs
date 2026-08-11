namespace RatchetPs2.Core.Moby;

internal static class MobyModelWriter
{
    public static byte[] WriteHeader(MobyModel model)
    {
        return WriteToBytes(writer =>
        {
            writer.Write(model.MeshTableOffset);
            writer.Write(model.HighLodMeshCount);
            writer.Write(model.LowLodMeshCount);
            writer.Write(model.MetalCount);
            writer.Write(model.MetalOffsets);
            writer.Write(model.JointCount);
            writer.Write(model.Padding);
            writer.Write(model.FarLodMeshCount);
            writer.Write(model.TeamPalettes);
            writer.Write(model.AnimationCount);
            writer.Write(model.SoundCount);
            writer.Write(model.LodTrans);
            writer.Write(model.Shadow);
            writer.Write(model.CollisionOffset);
            writer.Write(model.SkeletonOffset);
            writer.Write(model.CommonTransOffset);
            writer.Write(model.AnimationJointsOffset);
            writer.Write(model.GifUsageOffset);
            writer.Write(model.Scale);
            writer.Write(model.SoundDefOffset);
            writer.Write(model.BangleTableOffset);
            writer.Write(model.MipmapDistance);
            writer.Write(model.CornCobOffset);
            model.BoundingSphere.Write(writer);
            writer.Write(model.GlowRgba);
            writer.Write(model.ModeBits);
            writer.Write(model.Type);
            writer.Write(model.ModeBits2);
        });
    }

    public static byte[] WriteBoundingSphere(MobyBoundingSphere boundingSphere)
    {
        return WriteToBytes(boundingSphere.Write);
    }

    public static byte[] WriteMeshEntry(MobyMeshTableEntry entry)
    {
        return WriteToBytes(entry.WriteHeader);
    }

    public static byte[] WriteGifTag(MobyGifTag tag)
    {
        return WriteToBytes(tag.Write);
    }

    public static byte[] WriteBangleTable(MobyBangleTable table)
    {
        return WriteToBytes(table.Write);
    }

    public static byte[] WriteCornKernel(MobyCornKernel kernel)
    {
        return WriteToBytes(kernel.Write);
    }

    public static byte[] WriteCollision(MobyCollision collision)
    {
        return WriteToBytes(collision.Write);
    }

    public static byte[] WriteBone(MobyMatrix4 bone)
    {
        return WriteToBytes(bone.Write);
    }

    public static byte[] WriteAnimationJoint(MobyAnimationJoint joint)
    {
        return WriteToBytes(joint.Write);
    }

    public static byte[] WriteSequenceHeader(MobySequence sequence)
    {
        return WriteToBytes(sequence.WriteHeader);
    }

    public static byte[] WriteAnimationFrameHeader(MobyAnimationFrame frame)
    {
        return WriteToBytes(frame.WriteHeader);
    }

    public static byte[] WriteAnimationTrigger(MobyAnimationTrigger trigger)
    {
        return WriteToBytes(trigger.Write);
    }

    public static byte[] WriteSound(MobySound sound)
    {
        return WriteToBytes(sound.Write);
    }

    private static byte[] WriteToBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }
}

namespace RatchetPs2.Games.UYA.Moby;

internal static class UyaMobyModelWriter
{
    public static byte[] WriteHeader(UyaMobyModel model)
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
            writer.Write(model.MeshCountType2);
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

    public static byte[] WriteBoundingSphere(UyaBoundingSphere boundingSphere)
    {
        return WriteToBytes(boundingSphere.Write);
    }

    public static byte[] WriteMeshEntry(UyaMobyMeshTableEntry entry)
    {
        return WriteToBytes(entry.WriteHeader);
    }

    public static byte[] WriteGifTag(UyaMobyGifTag tag)
    {
        return WriteToBytes(tag.Write);
    }

    public static byte[] WriteBangleTable(UyaMobyBangleTable table)
    {
        return WriteToBytes(table.Write);
    }

    public static byte[] WriteCornKernel(UyaMobyCornKernel kernel)
    {
        return WriteToBytes(kernel.Write);
    }

    public static byte[] WriteCollision(UyaMobyCollision collision)
    {
        return WriteToBytes(collision.Write);
    }

    public static byte[] WriteBone(UyaMatrix4 bone)
    {
        return WriteToBytes(bone.Write);
    }

    public static byte[] WriteAnimationJoint(UyaMobyAnimationJoint joint)
    {
        return WriteToBytes(joint.Write);
    }

    public static byte[] WriteSequenceHeader(UyaMobySequence sequence)
    {
        return WriteToBytes(sequence.WriteHeader);
    }

    public static byte[] WriteAnimationFrameHeader(UyaMobyAnimationFrame frame)
    {
        return WriteToBytes(frame.WriteHeader);
    }

    public static byte[] WriteAnimationTrigger(UyaMobyAnimationTrigger trigger)
    {
        return WriteToBytes(trigger.Write);
    }

    public static byte[] WriteSound(UyaMobySound sound)
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

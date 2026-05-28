namespace RatchetPs2.Core.Moby;

public static class MobyModelPacker
{
    public static byte[] Pack(IMobyModelInput input, MobyLooseModelReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Build(MobyLooseModelReader.Read(input, options));
    }

    public static byte[] Build(MobyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(new byte[0x48]);
        writer.Write(new byte[model.AnimationCount * 4]);
        Align(writer, 0x10);
        WritePreAnimationSectionPadding(writer, model);

        WriteBangles(writer, model);
        WriteCornCob(writer, model);
        WriteAnimations(writer, model);

        model.MeshTableOffset = checked((int)writer.BaseStream.Length);
        writer.Write(new byte[(model.MeshTable?.Entries.Count ?? 0) * 0x10]);

        WriteCollision(writer, model);
        WriteShadow(writer, model);
        WriteSkeleton(writer, model);
        WriteCommonTransforms(writer, model);
        WriteAnimationJoints(writer, model);

        Align(writer, 0x10);
        WriteSounds(writer, model);
        WriteMeshData(writer, model);
        WriteTeamPalettes(writer, model);
        WriteGifTags(writer, model);

        writer.BaseStream.Seek(0, SeekOrigin.Begin);
        writer.Write(MobyModelWriter.WriteHeader(model));
        writer.Flush();

        return stream.ToArray();
    }

    private static void WritePreAnimationSectionPadding(BinaryWriter writer, MobyModel model)
    {
        if (model.PreAnimationSectionPadding is not null)
        {
            writer.Write(model.PreAnimationSectionPadding);
            return;
        }

        var candidateOffsets = new List<int>();
        if (model.BangleTable is not null && model.BangleTableOffset > 0)
        {
            candidateOffsets.Add(model.BangleTableOffset * 0x10);
        }

        if (model.CornCob is not null && model.CornCobOffset > 0)
        {
            candidateOffsets.Add(model.CornCobOffset * 0x10);
        }

        if (candidateOffsets.Count == 0)
        {
            writer.Write(new byte[0x10]);
            return;
        }

        var targetOffset = candidateOffsets.Min();
        var paddingLength = targetOffset - writer.BaseStream.Position;
        if (paddingLength > 0)
        {
            writer.Write(new byte[paddingLength]);
        }
    }

    private static void WriteBangles(BinaryWriter writer, MobyModel model)
    {
        if (model.BangleTable is null || model.BangleTable.BangleCount <= 0)
        {
            model.BangleTableOffset = 0;
            return;
        }

        model.BangleTableOffset = checked((byte)(writer.BaseStream.Length / 0x10));
        model.BangleTable.Write(writer);
    }

    private static void WriteCornCob(BinaryWriter writer, MobyModel model)
    {
        if (model.CornCob is null || (model.CornCob.RawData is null && model.CornCob.Kernels.Count == 0))
        {
            model.CornCobOffset = 0;
            return;
        }

        model.CornCobOffset = checked((short)(writer.BaseStream.Length / 0x10));
        if (model.CornCob.RawData is not null)
        {
            writer.Write(model.CornCob.RawData);
            return;
        }

        writer.Write(model.CornCob.KernelOffsets);
        foreach (var kernel in model.CornCob.Kernels)
        {
            if (kernel is null)
            {
                continue;
            }

            kernel.Write(writer);
            Align(writer, 0x10);
        }
    }

    private static void WriteAnimations(BinaryWriter writer, MobyModel model)
    {
        for (var i = 0; i < model.Sequences.Count; i++)
        {
            using var sequenceStream = new MemoryStream();
            using (var sequenceWriter = new BinaryWriter(sequenceStream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                WriteSequence(sequenceWriter, model.Sequences[i], checked((int)writer.BaseStream.Length));
            }

            writer.BaseStream.Seek(0x48 + 0x04 * i, SeekOrigin.Begin);
            var newOffset = checked((int)writer.BaseStream.Length);
            writer.Write(newOffset);
            writer.BaseStream.Seek(newOffset, SeekOrigin.Begin);
            writer.Write(sequenceStream.ToArray());
            Align(writer, 0x10);
        }
    }

    private static void WriteSequence(BinaryWriter writer, MobySequence sequence, int globalOffset)
    {
        if (sequence.RawData is not null)
        {
            writer.Write(sequence.RawData);
            return;
        }

        if (sequence.Format == MobyAnimationFormat.Compact)
        {
            WriteCompactSequence(writer, sequence);
            return;
        }

        sequence.WriteHeader(writer);
        var frameListOffset = checked((int)writer.BaseStream.Position);
        writer.Write(new byte[sequence.Frames.Count * 0x04]);

        sequence.TriggerCount = checked((byte)sequence.Triggers.Count);
        foreach (var trigger in sequence.Triggers)
        {
            trigger.Write(writer);
        }

        Align(writer, 0x10);

        for (var i = 0; i < sequence.Frames.Count; i++)
        {
            var frame = sequence.Frames[i];
            var frameOffset = frameListOffset + 0x04 * i;
            writer.BaseStream.Seek(frameOffset, SeekOrigin.Begin);
            writer.Write(checked((int)(writer.BaseStream.Length + globalOffset)));

            writer.BaseStream.Seek(writer.BaseStream.Length, SeekOrigin.Begin);
            frame.FrameDataSize = checked((byte)(frame.FrameData.Length / 0x10));
            frame.WriteHeader(writer);
            writer.Write(frame.FrameData);
        }
    }

    private static void WriteCompactSequence(BinaryWriter writer, MobySequence sequence)
    {
        var startOffset = checked((int)writer.BaseStream.Position);
        sequence.TriggerCount = checked((byte)sequence.Triggers.Count);
        sequence.FrameCount = checked((byte)sequence.CompactFrames.Count);

        WriteCompactSequenceHeader(writer, sequence);
        foreach (var frame in sequence.CompactFrames)
        {
            frame.Write(writer);
        }

        if (sequence.TriggerCount > 0)
        {
            sequence.CompactTriggerOffset = checked((int)(writer.BaseStream.Position - startOffset));
            foreach (var trigger in sequence.Triggers)
            {
                trigger.Write(writer);
            }
        }
        else
        {
            sequence.CompactTriggerOffset = 0;
        }

        sequence.CompactAnimDataOffset = checked((int)(writer.BaseStream.Position - startOffset));
        writer.Write(sequence.CompactAnimInfoData.Length == 0 ? new byte[0x08] : sequence.CompactAnimInfoData);
        Align(writer, 0x10);

        sequence.CompactFrameDataOffset = checked((int)(writer.BaseStream.Position - startOffset));
        writer.Write(sequence.CompactFrameData);

        var endOffset = writer.BaseStream.Position;
        writer.BaseStream.Seek(startOffset, SeekOrigin.Begin);
        WriteCompactSequenceHeader(writer, sequence);
        writer.BaseStream.Seek(endOffset, SeekOrigin.Begin);
    }

    private static void WriteCompactSequenceHeader(BinaryWriter writer, MobySequence sequence)
    {
        sequence.BoundingSphere.Write(writer);
        writer.Write(sequence.FrameCount);
        writer.Write(sequence.Sound);
        writer.Write(sequence.TriggerCount);
        writer.Write(sequence.Padding);
        writer.Write(sequence.CompactTriggerOffset);
        writer.Write(sequence.CompactAnimDataOffset);
        writer.Write(sequence.CompactFrameDataOffset);
    }

    private static void WriteCollision(BinaryWriter writer, MobyModel model)
    {
        if (model.Collision is null)
        {
            model.CollisionOffset = 0;
            return;
        }

        model.CollisionOffset = checked((int)writer.BaseStream.Length);
        model.Collision.Write(writer);
        Align(writer, 0x10);
    }

    private static void WriteShadow(BinaryWriter writer, MobyModel model)
    {
        if (model.ShadowData is not null)
        {
            if (model.ShadowPrefixData is not null)
            {
                writer.Write(model.ShadowPrefixData);
            }

            model.Shadow = checked((byte)(model.ShadowData.Length / 0x10));
            writer.Write(model.ShadowData);
        }
        else
        {
            model.Shadow = 0;
        }
    }

    private static void WriteSkeleton(BinaryWriter writer, MobyModel model)
    {
        model.JointCount = checked((byte)(model.Skeleton?.Bones.Count ?? 0));
        if (model.JointCount == 0 || model.Skeleton is null)
        {
            if (model.SkeletonOffset > writer.BaseStream.Length)
            {
                writer.Write(new byte[model.SkeletonOffset - writer.BaseStream.Length]);
            }

            model.SkeletonOffset = model.SkeletonOffset > 0
                ? checked((int)writer.BaseStream.Length)
                : 0;
            return;
        }

        model.SkeletonOffset = checked((int)writer.BaseStream.Length);
        foreach (var bone in model.Skeleton.Bones)
        {
            if (model.SkeletonFormat == MobyAnimationFormat.Compact)
            {
                bone.WriteCompact(writer);
            }
            else
            {
                bone.Write(writer);
            }
        }
    }

    private static void WriteCommonTransforms(BinaryWriter writer, MobyModel model)
    {
        model.CommonTransOffset = checked((int)writer.BaseStream.Length);
        writer.Write(model.CommonTransforms ?? []);
    }

    private static void WriteAnimationJoints(BinaryWriter writer, MobyModel model)
    {
        if (model.AnimationJoints is null)
        {
            if (model.AnimationJointsOffset > writer.BaseStream.Length)
            {
                writer.Write(new byte[model.AnimationJointsOffset - writer.BaseStream.Length]);
            }

            model.AnimationJointsOffset = model.AnimationJointsOffset > 0
                ? checked((int)writer.BaseStream.Length)
                : 0;
            return;
        }

        model.AnimationJointsOffset = checked((int)writer.BaseStream.Length);
        writer.Write(model.AnimationJoints.Count);
        writer.Write(new byte[model.AnimationJoints.Count * 4]);

        for (var i = 0; i < model.AnimationJoints.Count; i++)
        {
            var joint = model.AnimationJoints[i];
            writer.BaseStream.Seek(model.AnimationJointsOffset + 0x04 + 0x04 * i, SeekOrigin.Begin);
            var jointOffset = checked((int)writer.BaseStream.Length);
            writer.Write(jointOffset);
            writer.BaseStream.Seek(jointOffset, SeekOrigin.Begin);
            joint.Write(writer);
            Align(writer, 0x04);
        }
    }

    private static void WriteSounds(BinaryWriter writer, MobyModel model)
    {
        if (model.Sounds is null)
        {
            model.SoundDefOffset = 0;
            model.SoundCount = 0;
            return;
        }

        model.SoundDefOffset = checked((int)writer.BaseStream.Length);
        model.SoundCount = checked((byte)model.Sounds.Count);
        foreach (var sound in model.Sounds)
        {
            sound.Write(writer);
        }
    }

    private static void WriteMeshData(BinaryWriter writer, MobyModel model)
    {
        if (model.MeshTable is null)
        {
            return;
        }

        for (var i = 0; i < model.MeshTable.Entries.Count; i++)
        {
            var mesh = model.MeshTable.Entries[i];
            mesh.VifListOffset = checked((int)writer.BaseStream.Length);
            writer.BaseStream.Seek(mesh.VifListOffset, SeekOrigin.Begin);
            writer.Write(mesh.VifData);

            if (mesh.GifTag is not null)
            {
                mesh.GifTag.GifDataOffset = checked((uint)writer.BaseStream.Length);
            }

            if (mesh.VifTextureData is not null)
            {
                writer.Write(mesh.VifTextureData);
            }

            mesh.VertexDataOffset = checked((int)writer.BaseStream.Length);
            writer.BaseStream.Seek(mesh.VertexDataOffset, SeekOrigin.Begin);
            writer.Write(mesh.VertexData);

            writer.BaseStream.Seek(model.MeshTableOffset + i * 0x10, SeekOrigin.Begin);
            mesh.WriteHeader(writer);
        }

        writer.BaseStream.Seek(writer.BaseStream.Length, SeekOrigin.Begin);
    }

    private static void WriteTeamPalettes(BinaryWriter writer, MobyModel model)
    {
        if (model.TeamPaletteData.Count == 0)
        {
            model.TeamPalettes = 0;
            return;
        }

        writer.Write(new byte[0x10]);
        foreach (var palette in model.TeamPaletteData.OrderBy(kvp => kvp.Key).SelectMany(kvp => kvp.Value))
        {
            writer.Write(palette);
        }
    }

    private static void WriteGifTags(BinaryWriter writer, MobyModel model)
    {
        if (model.MeshTable is null)
        {
            model.GifUsageOffset = 0;
            return;
        }

        var gifTags = GetGifTagsToWrite(model);
        if (gifTags.Count == 0)
        {
            model.GifUsageOffset = 0;
            return;
        }

        model.GifUsageOffset = checked((int)writer.BaseStream.Length);
        for (var i = 0; i < gifTags.Count; i++)
        {
            var tag = gifTags[i];
            writer.Write(tag.TextureIds);
            var gifDataOffset = tag.GifDataOffset & 0x7FFFFFFFu;
            if (i == gifTags.Count - 1)
            {
                gifDataOffset |= 0x80000000u;
            }

            writer.Write(gifDataOffset);
        }
    }

    private static List<MobyGifTag> GetGifTagsToWrite(MobyModel model)
    {
        var gifTags = model.GifTags.Count > 0
            ? [.. model.GifTags]
            : new List<MobyGifTag>();

        if (model.MeshTable is null)
        {
            return gifTags;
        }

        foreach (var entry in model.MeshTable.Entries)
        {
            if (entry.GifTag is null || gifTags.Any(tag => ReferenceEquals(tag, entry.GifTag)))
            {
                continue;
            }

            gifTags.Add(entry.GifTag);
        }

        return gifTags;
    }

    private static void Align(BinaryWriter writer, int alignment)
    {
        var remainder = writer.BaseStream.Position % alignment;
        if (remainder == 0)
        {
            return;
        }

        writer.Write(new byte[alignment - remainder]);
    }
}

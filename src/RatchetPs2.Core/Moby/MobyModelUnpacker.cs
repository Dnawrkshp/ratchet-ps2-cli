namespace RatchetPs2.Core.Moby;

public static class MobyModelUnpacker
{
    private static readonly IReadOnlyDictionary<MobyMeshType, string> MeshTypeFolders =
        new Dictionary<MobyMeshType, string>
        {
            [MobyMeshType.HighLod] = "lod_high",
            [MobyMeshType.LowLod] = "lod_low",
            [MobyMeshType.FarLod] = "lod_far",
            [MobyMeshType.Bangle] = "bangle",
            [MobyMeshType.Metal] = "metal"
        };

    public static MobyModel Unpack(Stream input, IMobyModelOutput output, MobyModelReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var model = MobyModelReader.Read(input, options);
        Export(model, output);
        return model;
    }

    public static void Export(MobyModel model, IMobyModelOutput output)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteBytes("header.def", MobyModelWriter.WriteHeader(model));
        output.WriteBytes("bsphere.def", MobyModelWriter.WriteBoundingSphere(model.BoundingSphere));

        ExportMeshes(model, output);
        ExportTeamPalettes(model, output);
        ExportBangles(model, output);
        ExportCornCob(model, output);
        ExportCollision(model, output);
        ExportShadow(model, output);
        ExportSkeleton(model, output);
        ExportAnimationJoints(model, output);
        output.WriteBytes("common_trans.def", model.CommonTransforms ?? []);
        ExportSounds(model, output);
        ExportAnimations(model, output);
    }

    private static void ExportMeshes(MobyModel model, IMobyModelOutput output)
    {
        if (model.MeshTable is null)
        {
            return;
        }

        for (var i = 0; i < model.MeshTable.Entries.Count; i++)
        {
            var entry = model.MeshTable.Entries[i];
            var folder = Path.Combine("mesh", MeshTypeFolders[entry.MeshType], i.ToString("0000"));
            output.WriteBytes(Path.Combine(folder, "entry.def"), MobyModelWriter.WriteMeshEntry(entry));
            output.WriteBytes(Path.Combine(folder, "vif_list.bin"), entry.VifData);
            output.WriteBytes(Path.Combine(folder, "vertex_list.bin"), entry.VertexData);

            if (entry.VifTextureData is not null)
            {
                output.WriteBytes(Path.Combine(folder, "vif_textures.bin"), entry.VifTextureData);
            }

            if (entry.GifTag is not null)
            {
                output.WriteBytes(Path.Combine(folder, "gif_tag.def"), MobyModelWriter.WriteGifTag(entry.GifTag));
            }
        }
    }

    private static void ExportTeamPalettes(MobyModel model, IMobyModelOutput output)
    {
        foreach (var (textureId, palettes) in model.TeamPaletteData)
        {
            var folder = Path.Combine("team_palettes", textureId.ToString("0000"));
            for (var i = 0; i < palettes.Count; i++)
            {
                output.WriteBytes(Path.Combine(folder, $"{i:0000}.palette"), palettes[i]);
            }
        }
    }

    private static void ExportBangles(MobyModel model, IMobyModelOutput output)
    {
        if (model.BangleTable is not null)
        {
            output.WriteBytes("bangles.def", MobyModelWriter.WriteBangleTable(model.BangleTable));
        }
    }

    private static void ExportCornCob(MobyModel model, IMobyModelOutput output)
    {
        if (model.CornCob is null)
        {
            return;
        }

        for (var i = 0; i < model.CornCob.KernelOffsets.Length; i++)
        {
            var bangleId = model.CornCob.KernelOffsets[i];
            if (bangleId == 0xFF)
            {
                continue;
            }

            var bytes = GetCornKernelBytes(model.CornCob, i);
            if (bytes.Length == 0)
            {
                continue;
            }

            output.WriteBytes(
                Path.Combine("corncob", $"kernel.{i:00}.{bangleId:0000}.bin"),
                bytes);
        }
    }

    private static byte[] GetCornKernelBytes(MobyCornCob cornCob, int kernelIndex)
    {
        if (cornCob.RawData is not null)
        {
            var offset = cornCob.KernelOffsets[kernelIndex] * 0x10;
            if (offset < 0 || offset >= cornCob.RawData.Length)
            {
                return [];
            }

            var end = cornCob.RawData.Length;
            foreach (var nextOffsetByte in cornCob.KernelOffsets)
            {
                if (nextOffsetByte == 0xFF)
                {
                    continue;
                }

                var nextOffset = nextOffsetByte * 0x10;
                if (nextOffset > offset && nextOffset < end)
                {
                    end = nextOffset;
                }
            }

            return cornCob.RawData[offset..end];
        }

        var bangleIndex = kernelIndex - 1;
        var kernel = bangleIndex >= 0 && bangleIndex < cornCob.Kernels.Count
            ? cornCob.Kernels[bangleIndex]
            : null;
        return kernel is null ? [] : MobyModelWriter.WriteCornKernel(kernel);
    }

    private static void ExportCollision(MobyModel model, IMobyModelOutput output)
    {
        if (model.Collision is not null)
        {
            output.WriteBytes("collision.bin", MobyModelWriter.WriteCollision(model.Collision));
        }
    }

    private static void ExportShadow(MobyModel model, IMobyModelOutput output)
    {
        if (model.Shadow > 0 && model.ShadowData is not null)
        {
            if (model.ShadowPrefixData is { Length: > 0 })
            {
                output.WriteBytes("shadow_prefix.bin", model.ShadowPrefixData);
            }

            output.WriteBytes("shadow.bin", model.ShadowData);
        }
    }

    private static void ExportSkeleton(MobyModel model, IMobyModelOutput output)
    {
        if (model.Skeleton?.Bones is null)
        {
            return;
        }

        for (var i = 0; i < model.Skeleton.Bones.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("skeleton", $"bone_{i:0000}.def"),
                MobyModelWriter.WriteBone(model.Skeleton.Bones[i]));
        }
    }

    private static void ExportAnimationJoints(MobyModel model, IMobyModelOutput output)
    {
        if (model.AnimationJoints is null)
        {
            return;
        }

        for (var i = 0; i < model.AnimationJoints.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("anim_joints", $"joint_{i:0000}.def"),
                MobyModelWriter.WriteAnimationJoint(model.AnimationJoints[i]));
        }
    }

    private static void ExportSounds(MobyModel model, IMobyModelOutput output)
    {
        if (model.Sounds is null)
        {
            return;
        }

        for (var i = 0; i < model.Sounds.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("sound_defs", $"sound_{i:0000}.def"),
                MobyModelWriter.WriteSound(model.Sounds[i]));
        }
    }

    private static void ExportAnimations(MobyModel model, IMobyModelOutput output)
    {
        for (var i = 0; i < model.Sequences.Count; i++)
        {
            var sequence = model.Sequences[i];
            var folder = Path.Combine("animations", i.ToString("0000"));
            if (sequence.RawData is { Length: > 0 })
            {
                output.WriteBytes(Path.Combine(folder, "sequence.bin"), sequence.RawData);
                if (sequence.Format == MobyAnimationFormat.Compact)
                {
                    continue;
                }
            }

            output.WriteBytes(Path.Combine(folder, "seq.def"), MobyModelWriter.WriteSequenceHeader(sequence));

            for (var frameIndex = 0; frameIndex < sequence.Frames.Count; frameIndex++)
            {
                var frame = sequence.Frames[frameIndex];
                output.WriteBytes(
                    Path.Combine(folder, $"frame_{frameIndex:0000}.def"),
                    MobyModelWriter.WriteAnimationFrameHeader(frame));
                output.WriteBytes(Path.Combine(folder, $"frame_{frameIndex:0000}.bin"), frame.FrameData);
            }

            for (var triggerIndex = 0; triggerIndex < sequence.Triggers.Count; triggerIndex++)
            {
                output.WriteBytes(
                    Path.Combine(folder, $"trig_{triggerIndex:0000}.def"),
                    MobyModelWriter.WriteAnimationTrigger(sequence.Triggers[triggerIndex]));
            }
        }
    }
}

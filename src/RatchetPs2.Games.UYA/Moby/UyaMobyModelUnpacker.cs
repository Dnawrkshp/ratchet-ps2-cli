namespace RatchetPs2.Games.UYA.Moby;

public static class UyaMobyModelUnpacker
{
    private static readonly IReadOnlyDictionary<UyaMobyMeshType, string> MeshTypeFolders =
        new Dictionary<UyaMobyMeshType, string>
        {
            [UyaMobyMeshType.HighLod] = "lod_high",
            [UyaMobyMeshType.LowLod] = "lod_low",
            [UyaMobyMeshType.MeshType2] = "mesh_type_2",
            [UyaMobyMeshType.Bangle] = "bangle",
            [UyaMobyMeshType.Metal] = "metal"
        };

    public static UyaMobyModel Unpack(Stream input, IMobyModelOutput output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var model = UyaMobyModelReader.Read(input);
        Export(model, output);
        return model;
    }

    public static void Export(UyaMobyModel model, IMobyModelOutput output)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(output);

        output.WriteBytes("header.def", UyaMobyModelWriter.WriteHeader(model));
        output.WriteBytes("bsphere.def", UyaMobyModelWriter.WriteBoundingSphere(model.BoundingSphere));

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

    private static void ExportMeshes(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.MeshTable is null)
        {
            return;
        }

        for (var i = 0; i < model.MeshTable.Entries.Count; i++)
        {
            var entry = model.MeshTable.Entries[i];
            var folder = Path.Combine("mesh", MeshTypeFolders[entry.MeshType], i.ToString("0000"));
            output.WriteBytes(Path.Combine(folder, "entry.def"), UyaMobyModelWriter.WriteMeshEntry(entry));
            output.WriteBytes(Path.Combine(folder, "vif_list.bin"), entry.VifData);
            output.WriteBytes(Path.Combine(folder, "vertex_list.bin"), entry.VertexData);

            if (entry.VifTextureData is not null)
            {
                output.WriteBytes(Path.Combine(folder, "vif_textures.bin"), entry.VifTextureData);
            }

            if (entry.GifTag is not null)
            {
                output.WriteBytes(Path.Combine(folder, "gif_tag.def"), UyaMobyModelWriter.WriteGifTag(entry.GifTag));
            }
        }
    }

    private static void ExportTeamPalettes(UyaMobyModel model, IMobyModelOutput output)
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

    private static void ExportBangles(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.BangleTable is not null)
        {
            output.WriteBytes("bangles.def", UyaMobyModelWriter.WriteBangleTable(model.BangleTable));
        }
    }

    private static void ExportCornCob(UyaMobyModel model, IMobyModelOutput output)
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

    private static byte[] GetCornKernelBytes(UyaMobyCornCob cornCob, int kernelIndex)
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

        var kernel = kernelIndex < cornCob.Kernels.Count ? cornCob.Kernels[kernelIndex] : null;
        return kernel is null ? [] : UyaMobyModelWriter.WriteCornKernel(kernel);
    }

    private static void ExportCollision(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.Collision is not null)
        {
            output.WriteBytes("collision.bin", UyaMobyModelWriter.WriteCollision(model.Collision));
        }
    }

    private static void ExportShadow(UyaMobyModel model, IMobyModelOutput output)
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

    private static void ExportSkeleton(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.Skeleton?.Bones is null)
        {
            return;
        }

        for (var i = 0; i < model.Skeleton.Bones.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("skeleton", $"bone_{i:0000}.def"),
                UyaMobyModelWriter.WriteBone(model.Skeleton.Bones[i]));
        }
    }

    private static void ExportAnimationJoints(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.AnimationJoints is null)
        {
            return;
        }

        for (var i = 0; i < model.AnimationJoints.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("anim_joints", $"joint_{i:0000}.def"),
                UyaMobyModelWriter.WriteAnimationJoint(model.AnimationJoints[i]));
        }
    }

    private static void ExportSounds(UyaMobyModel model, IMobyModelOutput output)
    {
        if (model.Sounds is null)
        {
            return;
        }

        for (var i = 0; i < model.Sounds.Count; i++)
        {
            output.WriteBytes(
                Path.Combine("sound_defs", $"sound_{i:0000}.def"),
                UyaMobyModelWriter.WriteSound(model.Sounds[i]));
        }
    }

    private static void ExportAnimations(UyaMobyModel model, IMobyModelOutput output)
    {
        for (var i = 0; i < model.Sequences.Count; i++)
        {
            var sequence = model.Sequences[i];
            var folder = Path.Combine("animations", i.ToString("0000"));
            output.WriteBytes(Path.Combine(folder, "seq.def"), UyaMobyModelWriter.WriteSequenceHeader(sequence));

            for (var frameIndex = 0; frameIndex < sequence.Frames.Count; frameIndex++)
            {
                var frame = sequence.Frames[frameIndex];
                output.WriteBytes(
                    Path.Combine(folder, $"frame_{frameIndex:0000}.def"),
                    UyaMobyModelWriter.WriteAnimationFrameHeader(frame));
                output.WriteBytes(Path.Combine(folder, $"frame_{frameIndex:0000}.bin"), frame.FrameData);
            }

            for (var triggerIndex = 0; triggerIndex < sequence.Triggers.Count; triggerIndex++)
            {
                output.WriteBytes(
                    Path.Combine(folder, $"trig_{triggerIndex:0000}.def"),
                    UyaMobyModelWriter.WriteAnimationTrigger(sequence.Triggers[triggerIndex]));
            }
        }
    }
}

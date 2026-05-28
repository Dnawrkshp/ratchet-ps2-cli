using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static class MobyModelReader
{
    public static MobyModel Read(Stream input, MobyModelReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new MobyModelReadOptions();

        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        var model = ReadHeader(reader);
        model.AnimationFormat = options.AnimationFormat;
        model.SkeletonFormat = options.AnimationFormat;

        if (!options.SkipAnimationSequences)
        {
            ReadSequences(reader, model, options.AnimationFormat);
        }
        ReadPreAnimationSectionPadding(reader, model);

        ReadShadow(reader, model);
        ReadSkeleton(reader, model, options.AnimationFormat);
        ReadAnimationJoints(reader, model);
        ReadCommonTransforms(reader, model);
        ReadGifTags(reader, model);
        ReadBangleTable(reader, model);
        ReadCornCob(reader, model);
        ReadMeshes(reader, model);
        ReadTeamPalettes(reader, model);
        ReadSoundDefs(reader, model);
        ReadCollision(reader, model);

        return model;
    }

    internal static MobyModel ReadHeader(BinaryReader reader)
    {
        return new MobyModel
        {
            MeshTableOffset = reader.ReadInt32(),
            HighLodMeshCount = reader.ReadByte(),
            LowLodMeshCount = reader.ReadByte(),
            MetalCount = reader.ReadByte(),
            MetalOffsets = reader.ReadByte(),
            JointCount = reader.ReadByte(),
            Padding = reader.ReadByte(),
            MeshCountType2 = reader.ReadByte(),
            TeamPalettes = reader.ReadByte(),
            AnimationCount = reader.ReadByte(),
            SoundCount = reader.ReadByte(),
            LodTrans = reader.ReadByte(),
            Shadow = reader.ReadByte(),
            CollisionOffset = reader.ReadInt32(),
            SkeletonOffset = reader.ReadInt32(),
            CommonTransOffset = reader.ReadInt32(),
            AnimationJointsOffset = reader.ReadInt32(),
            GifUsageOffset = reader.ReadInt32(),
            Scale = reader.ReadSingle(),
            SoundDefOffset = reader.ReadInt32(),
            BangleTableOffset = reader.ReadByte(),
            MipmapDistance = reader.ReadByte(),
            CornCobOffset = reader.ReadInt16(),
            BoundingSphere = MobyBoundingSphere.Read(reader),
            GlowRgba = reader.ReadInt32(),
            ModeBits = reader.ReadInt16(),
            Type = reader.ReadByte(),
            ModeBits2 = reader.ReadByte()
        };
    }

    private static void ReadSequences(BinaryReader reader, MobyModel model, MobyAnimationFormat format)
    {
        var animationOffsets = new int[model.AnimationCount];
        for (var i = 0; i < model.AnimationCount; i++)
        {
            reader.BaseStream.Seek(0x48 + 0x04 * i, SeekOrigin.Begin);
            animationOffsets[i] = reader.ReadInt32();
        }

        for (var i = 0; i < model.AnimationCount; i++)
        {
            var animationOffset = animationOffsets[i];
            if (animationOffset == 0)
            {
                continue;
            }

            reader.BaseStream.Seek(animationOffset, SeekOrigin.Begin);
            model.Sequences.Add(format == MobyAnimationFormat.Compact
                ? ReadCompactSequence(reader, model, animationOffset, animationOffsets)
                : ReadSequence(reader, model, animationOffset, animationOffsets));
        }
    }

    private static void ReadPreAnimationSectionPadding(BinaryReader reader, MobyModel model)
    {
        var afterAnimationOffsets = 0x48 + model.AnimationCount * 0x04;
        var alignedStart = Align(afterAnimationOffsets, 0x10);
        var nextSectionOffset = FindNextPreAnimationSectionOffset(reader, model);
        if (nextSectionOffset <= alignedStart)
        {
            model.PreAnimationSectionPadding = [];
            return;
        }

        reader.BaseStream.Seek(alignedStart, SeekOrigin.Begin);
        model.PreAnimationSectionPadding = reader.ReadBytes(checked(nextSectionOffset - alignedStart));
    }

    private static int FindNextPreAnimationSectionOffset(BinaryReader reader, MobyModel model)
    {
        var candidates = new List<int>();
        if (model.BangleTableOffset > 0)
        {
            candidates.Add(model.BangleTableOffset * 0x10);
        }

        if (model.CornCobOffset > 0)
        {
            candidates.Add(model.CornCobOffset * 0x10);
        }

        for (var i = 0; i < model.AnimationCount; i++)
        {
            reader.BaseStream.Seek(0x48 + 0x04 * i, SeekOrigin.Begin);
            var animationOffset = reader.ReadInt32();
            if (animationOffset > 0)
            {
                candidates.Add(animationOffset);
            }
        }

        if (model.MeshTableOffset > 0)
        {
            candidates.Add(model.MeshTableOffset);
        }

        return candidates.Where(offset => offset > 0).DefaultIfEmpty(model.MeshTableOffset).Min();
    }

    private static MobySequence ReadCompactSequence(
        BinaryReader reader,
        MobyModel model,
        int startOffset,
        IReadOnlyList<int> animationOffsets)
    {
        var endOffset = FindNextSequenceEndOffset(reader, model, startOffset, animationOffsets);
        var rawData = Array.Empty<byte>();
        if (endOffset > startOffset)
        {
            reader.BaseStream.Seek(startOffset, SeekOrigin.Begin);
            rawData = reader.ReadBytes(checked((int)(endOffset - startOffset)));
        }

        reader.BaseStream.Seek(startOffset, SeekOrigin.Begin);
        var sequence = new MobySequence
        {
            Format = MobyAnimationFormat.Compact,
            RawData = rawData,
            BoundingSphere = MobyBoundingSphere.Read(reader),
            FrameCount = reader.ReadByte(),
            Sound = reader.ReadByte(),
            TriggerCount = reader.ReadByte(),
            Padding = reader.ReadByte(),
            CompactTriggerOffset = reader.ReadInt32(),
            CompactAnimDataOffset = reader.ReadInt32(),
            CompactFrameDataOffset = reader.ReadInt32()
        };

        for (var i = 0; i < sequence.FrameCount; i++)
        {
            sequence.CompactFrames.Add(new MobyCompactAnimationFrame
            {
                Unknown00 = reader.ReadInt16(),
                FrameId = reader.ReadInt16()
            });
        }

        return sequence;
    }

    private static long FindNextSequenceEndOffset(
        BinaryReader reader,
        MobyModel model,
        long startOffset,
        IReadOnlyList<int> animationOffsets)
    {
        var originalPosition = reader.BaseStream.Position;
        var candidates = animationOffsets
            .Where(offset => offset > startOffset)
            .Select(offset => (long)offset)
            .ToList();

        candidates.Add(model.MeshTableOffset);
        if (model.BangleTableOffset > 0)
        {
            candidates.Add(model.BangleTableOffset * 0x10L);
        }
        if (model.CornCobOffset > 0)
        {
            candidates.Add(model.CornCobOffset * 0x10L);
        }

        reader.BaseStream.Seek(originalPosition, SeekOrigin.Begin);
        return candidates
            .Where(offset => offset > startOffset)
            .DefaultIfEmpty(reader.BaseStream.Length)
            .Min();
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }

    private static void AddPositive(List<int> values, int value)
    {
        if (value > 0)
        {
            values.Add(value);
        }
    }

    private static MobySequence ReadSequence(
        BinaryReader reader,
        MobyModel model,
        int startOffset,
        IReadOnlyList<int> animationOffsets)
    {
        var endOffset = FindNextSequenceEndOffset(reader, model, startOffset, animationOffsets);
        var rawData = Array.Empty<byte>();
        if (endOffset > startOffset)
        {
            reader.BaseStream.Seek(startOffset, SeekOrigin.Begin);
            rawData = reader.ReadBytes(checked((int)(endOffset - startOffset)));
        }

        reader.BaseStream.Seek(startOffset, SeekOrigin.Begin);
        var sequence = new MobySequence
        {
            RawData = rawData,
            BoundingSphere = MobyBoundingSphere.Read(reader),
            FrameCount = reader.ReadByte(),
            Sound = reader.ReadByte(),
            TriggerCount = reader.ReadByte(),
            Padding = reader.ReadByte(),
            Unknown14 = reader.ReadInt32(),
            Unknown18 = reader.ReadInt32()
        };

        for (var i = 0; i < sequence.FrameCount; i++)
        {
            sequence.FrameOffsets.Add(reader.ReadUInt32());
        }

        for (var i = 0; i < sequence.TriggerCount; i++)
        {
            sequence.Triggers.Add(new MobyAnimationTrigger
            {
                Unknown00 = reader.ReadInt16(),
                Unknown02 = reader.ReadInt16()
            });
        }

        foreach (var frameOffset in sequence.FrameOffsets)
        {
            reader.BaseStream.Seek(frameOffset, SeekOrigin.Begin);
            sequence.Frames.Add(ReadAnimationFrame(reader));
        }

        return sequence;
    }

    private static MobyAnimationFrame ReadAnimationFrame(BinaryReader reader)
    {
        var frame = new MobyAnimationFrame
        {
            Unknown00 = reader.ReadByte(),
            Unknown01 = reader.ReadByte(),
            Unknown02 = reader.ReadByte(),
            Unknown03 = reader.ReadByte(),
            Unknown04 = reader.ReadByte(),
            Unknown05 = reader.ReadByte(),
            FrameDataSize = reader.ReadByte(),
            Unknown07 = reader.ReadByte(),
            Unknown08 = reader.ReadInt32(),
            Unknown0C = reader.ReadInt32()
        };

        frame.FrameData = reader.ReadBytes(frame.FrameDataSize * 0x10);
        return frame;
    }

    private static void ReadShadow(BinaryReader reader, MobyModel model)
    {
        if (model.Shadow <= 0 || model.SkeletonOffset == 0)
        {
            return;
        }

        var shadowSize = model.Shadow * 0x10;
        var shadowOffset = model.SkeletonOffset - shadowSize;
        var meshTableEntryCount = model.HighLodMeshCount + model.LowLodMeshCount + model.MeshCountType2 + model.MetalCount;
        var afterMeshTable = model.MeshTableOffset > 0 ? model.MeshTableOffset + meshTableEntryCount * 0x10 : 0;
        if (model.CollisionOffset == 0 && afterMeshTable > 0 && shadowOffset > afterMeshTable)
        {
            reader.BaseStream.Seek(afterMeshTable, SeekOrigin.Begin);
            model.ShadowPrefixData = reader.ReadBytes(shadowOffset - afterMeshTable);
        }

        reader.BaseStream.Seek(shadowOffset, SeekOrigin.Begin);
        model.ShadowData = reader.ReadBytes(shadowSize);
    }

    private static void ReadSkeleton(BinaryReader reader, MobyModel model, MobyAnimationFormat format)
    {
        if (model.SkeletonOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.SkeletonOffset, SeekOrigin.Begin);
        var skeleton = new MobySkeleton();
        for (var i = 0; i < model.JointCount; i++)
        {
            skeleton.Bones.Add(format == MobyAnimationFormat.Compact
                ? MobyMatrix4.ReadCompact(reader)
                : MobyMatrix4.Read(reader));
        }

        model.Skeleton = skeleton;
    }

    private static void ReadAnimationJoints(BinaryReader reader, MobyModel model)
    {
        if (model.AnimationJointsOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.AnimationJointsOffset, SeekOrigin.Begin);
        var jointCount = reader.ReadInt32();
        var offsetListStart = reader.BaseStream.Position;
        model.AnimationJoints = [];

        for (var i = 0; i < jointCount; i++)
        {
            reader.BaseStream.Seek(offsetListStart + 0x04 * i, SeekOrigin.Begin);
            var offset = reader.ReadInt32();

            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            model.AnimationJoints.Add(ReadAnimationJoint(reader));
        }
    }

    private static MobyAnimationJoint ReadAnimationJoint(BinaryReader reader)
    {
        var joint = new MobyAnimationJoint
        {
            SubSkeletonTokenOffset = reader.ReadInt16(),
            AnimationJointFlagsOrAuxIndex = reader.ReadInt16()
        };

        using var data = new MemoryStream();
        byte value;
        do
        {
            value = reader.ReadByte();
            data.WriteByte(value);
        } while (value != 0xFF);

        joint.Data = data.ToArray();
        return joint;
    }

    private static void ReadCommonTransforms(BinaryReader reader, MobyModel model)
    {
        if (model.CommonTransOffset == 0)
        {
            return;
        }

        var byteCount = GetCommonTransformByteCount(reader, model);
        reader.BaseStream.Seek(model.CommonTransOffset, SeekOrigin.Begin);
        model.CommonTransforms = reader.ReadBytes(byteCount);
    }

    private static int GetCommonTransformByteCount(BinaryReader reader, MobyModel model)
    {
        if (model.AnimationJointsOffset >= model.CommonTransOffset && model.AnimationJointsOffset != 0)
        {
            return model.AnimationJointsOffset - model.CommonTransOffset;
        }

        var nextSectionOffset = FindNextSectionOffset(reader, model, model.CommonTransOffset);
        if (nextSectionOffset > model.CommonTransOffset)
        {
            return checked((int)(nextSectionOffset - model.CommonTransOffset));
        }

        return GetCommonTransformCount(reader, model) * 0x10;
    }

    private static int GetCommonTransformCount(BinaryReader reader, MobyModel model)
    {
        var count = (int)model.JointCount;
        if (model.MeshTableOffset <= 0)
        {
            return count;
        }

        var entryCount = model.HighLodMeshCount + model.LowLodMeshCount + model.MeshCountType2 + model.MetalCount;
        if (entryCount <= 0)
        {
            return count;
        }

        var currentPosition = reader.BaseStream.Position;
        try
        {
            for (var i = 0; i < entryCount; i++)
            {
                reader.BaseStream.Seek(model.MeshTableOffset + i * 0x10 + 0x0E, SeekOrigin.Begin);
                var commonTransformIndex = reader.ReadByte();
                count = Math.Max(count, commonTransformIndex + 1);
            }
        }
        finally
        {
            reader.BaseStream.Seek(currentPosition, SeekOrigin.Begin);
        }

        return count;
    }

    private static void ReadGifTags(BinaryReader reader, MobyModel model)
    {
        if (model.GifUsageOffset == 0)
        {
            return;
        }

        var maxGifTags = Math.Max((reader.BaseStream.Length - model.GifUsageOffset) / 0x10, 1);
        for (var index = 0; index < maxGifTags; index++)
        {
            reader.BaseStream.Seek(model.GifUsageOffset + 0x10 * index, SeekOrigin.Begin);
            var tag = new MobyGifTag
            {
                TextureIds = reader.ReadBytes(0x0C),
                GifDataOffset = reader.ReadUInt32()
            };

            model.GifTags.Add(tag);
            if ((tag.GifDataOffset & 0x80000000u) != 0)
            {
                tag.GifDataOffset -= 0x80000000;
                return;
            }
        }
    }

    private static void ReadBangleTable(BinaryReader reader, MobyModel model)
    {
        if (model.BangleTableOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.BangleTableOffset * 0x10, SeekOrigin.Begin);
        var table = new MobyBangleTable
        {
            Unknown00 = reader.ReadByte(),
            BangleCount = reader.ReadByte(),
            Unknown02 = reader.ReadByte(),
            Unknown03 = reader.ReadByte()
        };

        for (var i = 0; i < 15; i++)
        {
            table.OffsetList.Add(new MobyBangleListEntry
            {
                MeshTableIndex = reader.ReadInt16(),
                Unknown02 = reader.ReadInt16()
            });
        }

        foreach (var entry in table.OffsetList)
        {
            if (entry.MeshTableIndex == 0)
            {
                continue;
            }

            table.DataList.Add(new MobyBangleData
            {
                Unknown00 = reader.ReadInt32(),
                Unknown04 = reader.ReadInt32(),
                Unknown08 = reader.ReadInt32(),
                Unknown0C = reader.ReadInt32()
            });
        }

        model.BangleTable = table;
    }

    private static void ReadCornCob(BinaryReader reader, MobyModel model)
    {
        if (model.CornCobOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.CornCobOffset * 0x10, SeekOrigin.Begin);
        var startOffset = reader.BaseStream.Position;
        var cornCob = new MobyCornCob
        {
            KernelOffsets = reader.ReadBytes(0x10)
        };

        var endOffset = FindNextSectionOffset(reader, model, startOffset);
        if (endOffset > startOffset)
        {
            reader.BaseStream.Seek(startOffset, SeekOrigin.Begin);
            cornCob.RawData = reader.ReadBytes(checked((int)(endOffset - startOffset)));
        }

        foreach (var kernelOffset in cornCob.KernelOffsets)
        {
            if (kernelOffset == 0xFF)
            {
                cornCob.Kernels.Add(null);
                continue;
            }

            try
            {
                reader.BaseStream.Seek(startOffset + kernelOffset * 0x10, SeekOrigin.Begin);
                cornCob.Kernels.Add(ReadCornKernel(reader));
            }
            catch (EndOfStreamException)
            {
                cornCob.Kernels.Add(null);
            }
        }

        model.CornCob = cornCob;
    }

    private static long FindNextSectionOffset(BinaryReader reader, MobyModel model, long startOffset)
    {
        var originalPosition = reader.BaseStream.Position;
        var candidates = new List<long>
        {
            model.MeshTableOffset,
            model.CollisionOffset,
            model.SkeletonOffset,
            model.CommonTransOffset,
            model.AnimationJointsOffset,
            model.GifUsageOffset,
            model.SoundDefOffset
        };

        for (var i = 0; i < model.AnimationCount; i++)
        {
            reader.BaseStream.Seek(0x48 + 0x04 * i, SeekOrigin.Begin);
            candidates.Add(reader.ReadInt32());
        }

        reader.BaseStream.Seek(originalPosition, SeekOrigin.Begin);

        return candidates
            .Where(offset => offset > startOffset)
            .DefaultIfEmpty(startOffset)
            .Min();
    }

    private static MobyCornKernel ReadCornKernel(BinaryReader reader)
    {
        var kernel = new MobyCornKernel
        {
            Vector = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
        };

        var firstVertex = ReadKernelVertex(reader);
        kernel.Vertices.Add(firstVertex);
        for (var i = 0; i < firstVertex.VertexCount - 1; i++)
        {
            kernel.Vertices.Add(ReadKernelVertex(reader));
        }

        return kernel;
    }

    private static MobyKernelVertex ReadKernelVertex(BinaryReader reader)
    {
        return new MobyKernelVertex
        {
            Unknown00 = reader.ReadInt32(),
            Unknown04 = reader.ReadInt16(),
            VertexCount = reader.ReadInt16()
        };
    }

    private static void ReadMeshes(BinaryReader reader, MobyModel model)
    {
        if (model.MeshTableOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.MeshTableOffset, SeekOrigin.Begin);
        var table = new MobyMeshTable();
        var meshCounts = new (MobyMeshType Type, int Count)[]
        {
            (MobyMeshType.HighLod, model.HighLodMeshCount),
            (MobyMeshType.LowLod, model.LowLodMeshCount),
            (MobyMeshType.MeshType2, model.MeshCountType2),
            (MobyMeshType.Bangle, model.BangleTable?.BangleCount ?? 0),
            (MobyMeshType.Metal, model.MetalCount)
        };

        var tableOffset = model.MeshTableOffset;
        foreach (var (type, count) in meshCounts)
        {
            for (var i = 0; i < count; i++)
            {
                reader.BaseStream.Seek(tableOffset, SeekOrigin.Begin);
                var entry = ReadMeshEntry(reader, type);
                AttachMeshData(reader, entry, model.GifTags);
                table.Entries.Add(entry);
                tableOffset += 0x10;
            }
        }

        model.MeshTable = table;
    }

    private static MobyMeshTableEntry ReadMeshEntry(BinaryReader reader, MobyMeshType type)
    {
        return new MobyMeshTableEntry
        {
            VifListOffset = reader.ReadInt32(),
            VifListSize = reader.ReadInt16(),
            VifListTextureSize = reader.ReadInt16(),
            VertexDataOffset = reader.ReadInt32(),
            VertexDataSize = reader.ReadByte(),
            Unknown0A = reader.ReadByte(),
            CommonTransformJointIndex = reader.ReadByte(),
            VertexCount = reader.ReadByte(),
            MeshType = type
        };
    }

    private static void AttachMeshData(BinaryReader reader, MobyMeshTableEntry entry, List<MobyGifTag> gifTags)
    {
        if (entry.VifListOffset != 0)
        {
            var vifListTextureOffset = (entry.VifListOffset + entry.VifListSize * 0x10) -
                                       (0x10 + entry.VifListTextureSize * 0x10);
            entry.GifTag = gifTags.FirstOrDefault(tag => tag.GifDataOffset == vifListTextureOffset);
        }

        reader.BaseStream.Seek(entry.VifListOffset, SeekOrigin.Begin);
        var vifSizeToRead = entry.VifListSize * 0x10;
        if (entry.VifListTextureSize > 0)
        {
            vifSizeToRead -= 0x10 + entry.VifListTextureSize * 0x10;
        }

        entry.VifData = reader.ReadBytes(vifSizeToRead);

        if (entry.VifListTextureSize > 0)
        {
            entry.VifTextureData = reader.ReadBytes(0x10 + entry.VifListTextureSize * 0x10);
        }

        reader.BaseStream.Seek(entry.VertexDataOffset, SeekOrigin.Begin);
        entry.VertexData = reader.ReadBytes(entry.VertexDataSize * 0x10);
    }

    private static void ReadTeamPalettes(BinaryReader reader, MobyModel model)
    {
        if (model.MeshTable is null || model.TeamPalettes == 0)
        {
            return;
        }

        reader.BaseStream.Position += 0x10;

        var paletteCountPerTexture = model.TeamPalettes & 0x0F;
        var modelTextureCount = (model.TeamPalettes & 0xF0) >> 4;
        if (paletteCountPerTexture == 0 || modelTextureCount == 0)
        {
            return;
        }

        for (var i = 0; i < paletteCountPerTexture * modelTextureCount; i++)
        {
            var textureIndex = i / paletteCountPerTexture;
            var palette = reader.ReadBytes(0x400);
            if (!model.TeamPaletteData.TryGetValue(textureIndex, out var palettes))
            {
                palettes = [];
                model.TeamPaletteData.Add(textureIndex, palettes);
            }

            palettes.Add(palette);
        }
    }

    private static void ReadSoundDefs(BinaryReader reader, MobyModel model)
    {
        if (model.SoundDefOffset <= 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.SoundDefOffset, SeekOrigin.Begin);
        model.Sounds = [];
        for (var i = 0; i < model.SoundCount; i++)
        {
            model.Sounds.Add(new MobySound
            {
                MinRange = reader.ReadSingle(),
                MaxRange = reader.ReadSingle(),
                MinVolume = reader.ReadInt32(),
                MaxVolume = reader.ReadInt32(),
                MinPitch = reader.ReadInt32(),
                MaxPitch = reader.ReadInt32(),
                Loop = reader.ReadByte(),
                Flags = reader.ReadByte(),
                Index = reader.ReadInt16(),
                BankIndex = reader.ReadInt32()
            });
        }
    }

    private static void ReadCollision(BinaryReader reader, MobyModel model)
    {
        if (model.CollisionOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.CollisionOffset, SeekOrigin.Begin);
        var collision = new MobyCollision
        {
            Unknown00 = reader.ReadInt32(),
            Size1 = reader.ReadInt32(),
            Size2 = reader.ReadInt32(),
            Size3 = reader.ReadInt32()
        };

        if (collision.Size1 > 0)
        {
            collision.Data1 = reader.ReadBytes(collision.Size1);
        }
        if (collision.Size2 > 0)
        {
            collision.Data2 = reader.ReadBytes(collision.Size2);
        }
        if (collision.Size3 > 0)
        {
            collision.Data3 = reader.ReadBytes(collision.Size3);
        }

        model.Collision = collision;
    }
}

public sealed class MobyModelReadOptions
{
    public bool SkipAnimationSequences { get; init; }
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
}

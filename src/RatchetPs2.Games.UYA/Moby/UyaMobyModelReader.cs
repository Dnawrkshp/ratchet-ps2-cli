using System.Numerics;

namespace RatchetPs2.Games.UYA.Moby;

public static class UyaMobyModelReader
{
    public static UyaMobyModel Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        var model = ReadHeader(reader);

        ReadSequences(reader, model);
        ReadShadow(reader, model);
        ReadSkeleton(reader, model);
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

    internal static UyaMobyModel ReadHeader(BinaryReader reader)
    {
        return new UyaMobyModel
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
            BoundingSphere = UyaBoundingSphere.Read(reader),
            GlowRgba = reader.ReadInt32(),
            ModeBits = reader.ReadInt16(),
            Type = reader.ReadByte(),
            ModeBits2 = reader.ReadByte()
        };
    }

    private static void ReadSequences(BinaryReader reader, UyaMobyModel model)
    {
        for (var i = 0; i < model.AnimationCount; i++)
        {
            reader.BaseStream.Seek(0x48 + 0x04 * i, SeekOrigin.Begin);
            var animationOffset = reader.ReadInt32();
            if (animationOffset == 0)
            {
                continue;
            }

            reader.BaseStream.Seek(animationOffset, SeekOrigin.Begin);
            model.Sequences.Add(ReadSequence(reader));
        }
    }

    private static UyaMobySequence ReadSequence(BinaryReader reader)
    {
        var sequence = new UyaMobySequence
        {
            BoundingSphere = UyaBoundingSphere.Read(reader),
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
            sequence.Triggers.Add(new UyaMobyAnimationTrigger
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

    private static UyaMobyAnimationFrame ReadAnimationFrame(BinaryReader reader)
    {
        var frame = new UyaMobyAnimationFrame
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

    private static void ReadShadow(BinaryReader reader, UyaMobyModel model)
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

    private static void ReadSkeleton(BinaryReader reader, UyaMobyModel model)
    {
        if (model.SkeletonOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.SkeletonOffset, SeekOrigin.Begin);
        var skeleton = new UyaMobySkeleton();
        for (var i = 0; i < model.JointCount; i++)
        {
            skeleton.Bones.Add(UyaMatrix4.Read(reader));
        }

        model.Skeleton = skeleton;
    }

    private static void ReadAnimationJoints(BinaryReader reader, UyaMobyModel model)
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

    private static UyaMobyAnimationJoint ReadAnimationJoint(BinaryReader reader)
    {
        var joint = new UyaMobyAnimationJoint
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

    private static void ReadCommonTransforms(BinaryReader reader, UyaMobyModel model)
    {
        if (model.CommonTransOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.CommonTransOffset, SeekOrigin.Begin);
        model.CommonTransforms = reader.ReadBytes(model.JointCount * 0x10);
    }

    private static void ReadGifTags(BinaryReader reader, UyaMobyModel model)
    {
        if (model.GifUsageOffset == 0)
        {
            return;
        }

        for (var index = 0; index < 50; index++)
        {
            reader.BaseStream.Seek(model.GifUsageOffset + 0x10 * index, SeekOrigin.Begin);
            var tag = new UyaMobyGifTag
            {
                TextureIds = reader.ReadBytes(0x0C),
                GifDataOffset = reader.ReadUInt32()
            };

            model.GifTags.Add(tag);
            if (tag.GifDataOffset >> 24 == 0x80)
            {
                tag.GifDataOffset -= 0x80000000;
                return;
            }
        }
    }

    private static void ReadBangleTable(BinaryReader reader, UyaMobyModel model)
    {
        if (model.BangleTableOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.BangleTableOffset * 0x10, SeekOrigin.Begin);
        var table = new UyaMobyBangleTable
        {
            Unknown00 = reader.ReadByte(),
            BangleCount = reader.ReadByte(),
            Unknown02 = reader.ReadByte(),
            Unknown03 = reader.ReadByte()
        };

        for (var i = 0; i < 15; i++)
        {
            table.OffsetList.Add(new UyaMobyBangleListEntry
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

            table.DataList.Add(new UyaMobyBangleData
            {
                Unknown00 = reader.ReadInt32(),
                Unknown04 = reader.ReadInt32(),
                Unknown08 = reader.ReadInt32(),
                Unknown0C = reader.ReadInt32()
            });
        }

        model.BangleTable = table;
    }

    private static void ReadCornCob(BinaryReader reader, UyaMobyModel model)
    {
        if (model.CornCobOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.CornCobOffset * 0x10, SeekOrigin.Begin);
        var startOffset = reader.BaseStream.Position;
        var cornCob = new UyaMobyCornCob
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

    private static long FindNextSectionOffset(BinaryReader reader, UyaMobyModel model, long startOffset)
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

    private static UyaMobyCornKernel ReadCornKernel(BinaryReader reader)
    {
        var kernel = new UyaMobyCornKernel
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

    private static UyaMobyKernelVertex ReadKernelVertex(BinaryReader reader)
    {
        return new UyaMobyKernelVertex
        {
            Unknown00 = reader.ReadInt32(),
            Unknown04 = reader.ReadInt16(),
            VertexCount = reader.ReadInt16()
        };
    }

    private static void ReadMeshes(BinaryReader reader, UyaMobyModel model)
    {
        if (model.MeshTableOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.MeshTableOffset, SeekOrigin.Begin);
        var table = new UyaMobyMeshTable();
        var meshCounts = new (UyaMobyMeshType Type, int Count)[]
        {
            (UyaMobyMeshType.HighLod, model.HighLodMeshCount),
            (UyaMobyMeshType.LowLod, model.LowLodMeshCount),
            (UyaMobyMeshType.MeshType2, model.MeshCountType2),
            (UyaMobyMeshType.Bangle, model.BangleTable?.BangleCount ?? 0),
            (UyaMobyMeshType.Metal, model.MetalCount)
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

    private static UyaMobyMeshTableEntry ReadMeshEntry(BinaryReader reader, UyaMobyMeshType type)
    {
        return new UyaMobyMeshTableEntry
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

    private static void AttachMeshData(BinaryReader reader, UyaMobyMeshTableEntry entry, List<UyaMobyGifTag> gifTags)
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

    private static void ReadTeamPalettes(BinaryReader reader, UyaMobyModel model)
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

    private static void ReadSoundDefs(BinaryReader reader, UyaMobyModel model)
    {
        if (model.SoundDefOffset <= 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.SoundDefOffset, SeekOrigin.Begin);
        model.Sounds = [];
        for (var i = 0; i < model.SoundCount; i++)
        {
            model.Sounds.Add(new UyaMobySound
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

    private static void ReadCollision(BinaryReader reader, UyaMobyModel model)
    {
        if (model.CollisionOffset == 0)
        {
            return;
        }

        reader.BaseStream.Seek(model.CollisionOffset, SeekOrigin.Begin);
        var collision = new UyaMobyCollision
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

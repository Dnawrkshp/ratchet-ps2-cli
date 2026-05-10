namespace RatchetPs2.Games.UYA.Moby;

public static class UyaMobyLooseModelReader
{
    private static readonly IReadOnlyList<(UyaMobyMeshType Type, string Folder)> MeshFolders =
    [
        (UyaMobyMeshType.HighLod, "lod_high"),
        (UyaMobyMeshType.LowLod, "lod_low"),
        (UyaMobyMeshType.MeshType2, "mesh_type_2"),
        (UyaMobyMeshType.Bangle, "bangle"),
        (UyaMobyMeshType.Metal, "metal")
    ];

    public static UyaMobyModel Read(IMobyModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var model = ReadHeader(input);
        model.BoundingSphere = ReadWithBinaryReader(input.ReadBytes("bsphere.def"), UyaBoundingSphere.Read);

        ReadBangles(input, model);
        ReadCornCob(input, model);
        ReadCollision(input, model);
        ReadAnimations(input, model);
        ReadShadow(input, model);
        ReadSkeleton(input, model);
        model.CommonTransforms = input.ReadBytes("common_trans.def");
        ReadSounds(input, model);
        ReadAnimationJoints(input, model);
        ReadTeamPalettes(input, model);
        ReadMeshes(input, model);
        ApplyDerivedHeaderFields(model);

        return model;
    }

    private static UyaMobyModel ReadHeader(IMobyModelInput input)
    {
        return ReadWithBinaryReader(input.ReadBytes("header.def"), UyaMobyModelReader.ReadHeader);
    }

    private static void ReadBangles(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.FileExists("bangles.def"))
        {
            model.BangleTable = null;
            return;
        }

        model.BangleTable = ReadWithBinaryReader(input.ReadBytes("bangles.def"), reader =>
        {
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

            return table;
        });
    }

    private static void ReadCornCob(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.DirectoryExists("corncob"))
        {
            model.CornCob = null;
            return;
        }

        var cornCob = new UyaMobyCornCob
        {
            KernelOffsets = Enumerable.Repeat((byte)0xFF, 0x10).ToArray()
        };
        var rawSlices = new List<(int Offset, byte[] Bytes)>();

        foreach (var file in input.EnumerateFiles("corncob", "kernel*.bin"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var parts = name.Split('.');
            if (parts.Length < 3 ||
                !int.TryParse(parts[1], out var kernelIndex) ||
                !int.TryParse(parts[2], out var bangleIndex) ||
                kernelIndex < 0 ||
                kernelIndex >= cornCob.KernelOffsets.Length)
            {
                throw new InvalidDataException($"Unsupported corncob kernel file name '{file}'.");
            }

            cornCob.KernelOffsets[kernelIndex] = (byte)bangleIndex;
            rawSlices.Add((bangleIndex * 0x10, input.ReadBytes(Path.Combine("corncob", file))));
        }

        if (rawSlices.Count > 0)
        {
            var rawLength = rawSlices.Max(slice => slice.Offset + slice.Bytes.Length);
            cornCob.RawData = new byte[rawLength];
            cornCob.KernelOffsets.CopyTo(cornCob.RawData, 0);
            foreach (var (offset, bytes) in rawSlices)
            {
                Buffer.BlockCopy(bytes, 0, cornCob.RawData, offset, bytes.Length);
            }
        }

        model.CornCob = cornCob;
    }

    private static UyaMobyCornKernel ReadCornKernel(byte[] bytes)
    {
        return ReadWithBinaryReader(bytes, reader =>
        {
            var kernel = new UyaMobyCornKernel
            {
                Vector = new System.Numerics.Vector4(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle())
            };

            var firstVertex = ReadKernelVertex(reader);
            kernel.Vertices.Add(firstVertex);
            for (var i = 0; i < firstVertex.VertexCount - 1; i++)
            {
                kernel.Vertices.Add(ReadKernelVertex(reader));
            }

            return kernel;
        });
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

    private static void ReadCollision(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.FileExists("collision.bin"))
        {
            model.Collision = null;
            return;
        }

        model.Collision = ReadWithBinaryReader(input.ReadBytes("collision.bin"), reader =>
        {
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

            return collision;
        });
    }

    private static void ReadAnimations(IMobyModelInput input, UyaMobyModel model)
    {
        model.Sequences.Clear();
        if (!input.DirectoryExists("animations"))
        {
            model.AnimationCount = 0;
            return;
        }

        foreach (var directory in input.EnumerateDirectories("animations"))
        {
            model.Sequences.Add(ReadSequence(input, Path.Combine("animations", directory)));
        }

        model.AnimationCount = checked((byte)model.Sequences.Count);
    }

    private static UyaMobySequence ReadSequence(IMobyModelInput input, string relativeDirectory)
    {
        var sequence = ReadWithBinaryReader(input.ReadBytes(Path.Combine(relativeDirectory, "seq.def")), reader =>
        {
            return new UyaMobySequence
            {
                BoundingSphere = UyaBoundingSphere.Read(reader),
                FrameCount = reader.ReadByte(),
                Sound = reader.ReadByte(),
                TriggerCount = reader.ReadByte(),
                Padding = reader.ReadByte(),
                Unknown14 = reader.ReadInt32(),
                Unknown18 = reader.ReadInt32()
            };
        });

        foreach (var frameFile in input.EnumerateFiles(relativeDirectory, "frame_*.def"))
        {
            var frame = ReadWithBinaryReader(input.ReadBytes(Path.Combine(relativeDirectory, frameFile)), reader =>
            {
                return new UyaMobyAnimationFrame
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
            });

            var frameDataPath = Path.Combine(relativeDirectory, Path.ChangeExtension(frameFile, ".bin"));
            frame.FrameData = input.ReadBytes(frameDataPath);
            frame.FrameDataSize = checked((byte)(frame.FrameData.Length / 0x10));
            sequence.Frames.Add(frame);
        }

        sequence.FrameCount = checked((byte)sequence.Frames.Count);

        foreach (var triggerFile in input.EnumerateFiles(relativeDirectory, "trig_*.def"))
        {
            sequence.Triggers.Add(ReadWithBinaryReader(input.ReadBytes(Path.Combine(relativeDirectory, triggerFile)), reader =>
            {
                return new UyaMobyAnimationTrigger
                {
                    Unknown00 = reader.ReadInt16(),
                    Unknown02 = reader.ReadInt16()
                };
            }));
        }

        sequence.TriggerCount = checked((byte)sequence.Triggers.Count);
        return sequence;
    }

    private static void ReadShadow(IMobyModelInput input, UyaMobyModel model)
    {
        model.ShadowPrefixData = input.FileExists("shadow_prefix.bin")
            ? input.ReadBytes("shadow_prefix.bin")
            : null;

        if (!input.FileExists("shadow.bin"))
        {
            model.ShadowData = null;
            model.Shadow = 0;
            return;
        }

        model.ShadowData = input.ReadBytes("shadow.bin");
        model.Shadow = checked((byte)(model.ShadowData.Length / 0x10));
    }

    private static void ReadSkeleton(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.DirectoryExists("skeleton"))
        {
            model.Skeleton = null;
            model.JointCount = 0;
            return;
        }

        var skeleton = new UyaMobySkeleton();
        foreach (var file in input.EnumerateFiles("skeleton", "bone_*.def"))
        {
            skeleton.Bones.Add(ReadWithBinaryReader(input.ReadBytes(Path.Combine("skeleton", file)), UyaMatrix4.Read));
        }

        model.Skeleton = skeleton;
        model.JointCount = checked((byte)skeleton.Bones.Count);
    }

    private static void ReadSounds(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.DirectoryExists("sound_defs"))
        {
            model.Sounds = null;
            model.SoundCount = 0;
            return;
        }

        model.Sounds = [];
        foreach (var file in input.EnumerateFiles("sound_defs", "sound_*.def"))
        {
            model.Sounds.Add(ReadWithBinaryReader(input.ReadBytes(Path.Combine("sound_defs", file)), reader =>
            {
                return new UyaMobySound
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
                };
            }));
        }

        model.SoundCount = checked((byte)model.Sounds.Count);
    }

    private static void ReadAnimationJoints(IMobyModelInput input, UyaMobyModel model)
    {
        if (!input.DirectoryExists("anim_joints"))
        {
            model.AnimationJoints = null;
            return;
        }

        model.AnimationJoints = [];
        foreach (var file in input.EnumerateFiles("anim_joints", "joint_*.def"))
        {
            model.AnimationJoints.Add(ReadWithBinaryReader(input.ReadBytes(Path.Combine("anim_joints", file)), reader =>
            {
                var joint = new UyaMobyAnimationJoint
                {
                    SubSkeletonTokenOffset = reader.ReadInt16(),
                    AnimationJointFlagsOrAuxIndex = reader.ReadInt16()
                };

                joint.Data = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
                return joint;
            }));
        }
    }

    private static void ReadTeamPalettes(IMobyModelInput input, UyaMobyModel model)
    {
        model.TeamPaletteData.Clear();
        model.TeamPalettes = 0;
        if (!input.DirectoryExists("team_palettes"))
        {
            return;
        }

        var palettesPerTexture = 0;
        foreach (var directory in input.EnumerateDirectories("team_palettes"))
        {
            var textureIndex = int.Parse(directory);
            var palettes = new List<byte[]>();
            foreach (var file in input.EnumerateFiles(Path.Combine("team_palettes", directory), "*.palette"))
            {
                palettes.Add(input.ReadBytes(Path.Combine("team_palettes", directory, file)));
            }

            palettesPerTexture = palettes.Count;
            model.TeamPaletteData.Add(textureIndex, palettes);
        }

        model.TeamPalettes = checked((byte)((model.TeamPaletteData.Count << 4) | (palettesPerTexture & 0x0F)));
    }

    private static void ReadMeshes(IMobyModelInput input, UyaMobyModel model)
    {
        var table = new UyaMobyMeshTable();
        foreach (var (type, folder) in MeshFolders)
        {
            var relativeFolder = Path.Combine("mesh", folder);
            if (!input.DirectoryExists(relativeFolder))
            {
                continue;
            }

            foreach (var meshDirectory in input.EnumerateDirectories(relativeFolder))
            {
                table.Entries.Add(ReadMesh(input, Path.Combine(relativeFolder, meshDirectory), type));
            }
        }

        model.MeshTable = table;
    }

    private static UyaMobyMeshTableEntry ReadMesh(IMobyModelInput input, string relativeDirectory, UyaMobyMeshType type)
    {
        var entry = ReadWithBinaryReader(input.ReadBytes(Path.Combine(relativeDirectory, "entry.def")), reader =>
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
        });

        entry.VifData = input.ReadBytes(Path.Combine(relativeDirectory, "vif_list.bin"));
        entry.VifListSize = checked((short)(entry.VifData.Length / 0x10));

        if (input.FileExists(Path.Combine(relativeDirectory, "vif_textures.bin")))
        {
            entry.VifTextureData = input.ReadBytes(Path.Combine(relativeDirectory, "vif_textures.bin"));
            entry.VifListTextureSize = checked((short)((entry.VifTextureData.Length / 0x10) - 1));
            entry.VifListSize += checked((short)(entry.VifListTextureSize + 1));
        }
        else
        {
            entry.VifTextureData = null;
            entry.VifListTextureSize = 0;
        }

        entry.VertexData = input.ReadBytes(Path.Combine(relativeDirectory, "vertex_list.bin"));
        entry.VertexDataSize = checked((byte)(entry.VertexData.Length / 0x10));

        if (input.FileExists(Path.Combine(relativeDirectory, "gif_tag.def")))
        {
            entry.GifTag = ReadWithBinaryReader(input.ReadBytes(Path.Combine(relativeDirectory, "gif_tag.def")), reader =>
            {
                return new UyaMobyGifTag
                {
                    TextureIds = reader.ReadBytes(0x0C),
                    GifDataOffset = reader.ReadUInt32()
                };
            });
        }

        return entry;
    }

    private static void ApplyDerivedHeaderFields(UyaMobyModel model)
    {
        var entries = model.MeshTable?.Entries ?? [];
        model.HighLodMeshCount = checked((byte)entries.Count(entry => entry.MeshType == UyaMobyMeshType.HighLod));
        model.LowLodMeshCount = checked((byte)entries.Count(entry => entry.MeshType == UyaMobyMeshType.LowLod));
        model.MeshCountType2 = checked((byte)entries.Count(entry => entry.MeshType == UyaMobyMeshType.MeshType2));
        model.MetalOffsets = checked((byte)(model.HighLodMeshCount + model.LowLodMeshCount + model.MeshCountType2));
        model.MetalCount = checked((byte)entries.Count(entry => entry.MeshType == UyaMobyMeshType.Metal));
    }

    private static T ReadWithBinaryReader<T>(byte[] bytes, Func<BinaryReader, T> read)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        return read(reader);
    }
}

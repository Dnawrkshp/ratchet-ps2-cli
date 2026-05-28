namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static void UpdateMeshCounts(MobyModel model)
    {
        if (model.MeshTable is null)
        {
            model.HighLodMeshCount = 0;
            model.LowLodMeshCount = 0;
            model.MeshCountType2 = 0;
            model.MetalOffsets = 0;
            model.MetalCount = 0;
            return;
        }

        model.HighLodMeshCount = checked((byte)model.MeshTable.Entries.Count(entry => entry.MeshType == MobyMeshType.HighLod));
        model.LowLodMeshCount = checked((byte)model.MeshTable.Entries.Count(entry => entry.MeshType == MobyMeshType.LowLod));
        model.MeshCountType2 = checked((byte)model.MeshTable.Entries.Count(entry => entry.MeshType == MobyMeshType.MeshType2));
        model.MetalOffsets = checked((byte)(model.HighLodMeshCount + model.LowLodMeshCount + model.MeshCountType2));
        model.MetalCount = checked((byte)model.MeshTable.Entries.Count(entry => entry.MeshType == MobyMeshType.Metal));
    }

    private static void RebucketCustomStaticHighLodOverflowMeshes(
        MobyModel model,
        IReadOnlyList<ImportedMesh> importedMeshes,
        int maxHighLodMeshes)
    {
        if (maxHighLodMeshes < 0)
        {
            throw new InvalidDataException("--custom-static-max-high-lod-meshes must be zero or greater.");
        }

        if (model.MeshTable is null)
        {
            return;
        }

        var highLodOrdinal = 0;
        foreach (var mesh in importedMeshes.OrderBy(mesh => mesh.TemplateMeshIndex))
        {
            if (mesh.TemplateMeshIndex < 0 || mesh.TemplateMeshIndex >= model.MeshTable.Entries.Count)
            {
                continue;
            }

            var entry = model.MeshTable.Entries[mesh.TemplateMeshIndex];
            if (entry.MeshType != MobyMeshType.HighLod)
            {
                continue;
            }

            if (highLodOrdinal >= maxHighLodMeshes)
            {
                entry.MeshType = MobyMeshType.MeshType2;
                mesh.MeshType = MobyMeshType.MeshType2;
            }

            highLodOrdinal++;
        }
    }

    private static int KeepOnlyCustomStaticReplaceMeshTemplate(MobyModel model, int replaceMeshIndex)
    {
        if (model.MeshTable is null)
        {
            throw new InvalidDataException("Template moby has no mesh table.");
        }

        if (replaceMeshIndex < 0 || replaceMeshIndex >= model.MeshTable.Entries.Count)
        {
            throw new InvalidDataException(
                $"--custom-static-use-only-replace-mesh-as-template requires --replace-mesh to select an existing template entry. Received {replaceMeshIndex}.");
        }

        var replacementEntry = CloneMeshEntry(model.MeshTable.Entries[replaceMeshIndex]);
        model.MeshTable.Entries.Clear();
        model.MeshTable.Entries.Add(replacementEntry);
        model.BangleTable = null;
        model.CornCob = null;
        UpdateMeshCounts(model);
        return 0;
    }

    private static void GenerateCustomStaticMeshSlots(MobyModel model, int capacity)
    {
        if (model.MeshTable is null)
        {
            return;
        }

        var clampedCapacity = checked((byte)Math.Clamp(capacity, 3, 127));
        for (var i = 0; i < model.MeshTable.Entries.Count; i++)
        {
            model.MeshTable.Entries[i] = CreateGeneratedCustomStaticMeshEntry(
                model.MeshTable.Entries[i].MeshType,
                clampedCapacity);
        }

        UpdateMeshCounts(model);
    }

    private static MobyModel CreateGeneratedCustomStaticContainer(MobyGltfImportOptions options)
    {
        var capacity = checked((byte)Math.Clamp(options.CustomStaticGeneratedMeshSlotCapacity, 3, 127));
        var model = new MobyModel
        {
            MeshTable = new MobyMeshTable(),
            Scale = options.OutputModelScale ?? 1f
        };
        model.MeshTable.Entries.Add(CreateGeneratedCustomStaticMeshEntry(MobyMeshType.HighLod, capacity));
        GenerateCustomStaticGlobalScaffold(model);
        GenerateCustomStaticHeaderDefaults(model);
        UpdateMeshCounts(model);
        return model;
    }

    private static MobyMeshTableEntry CreateGeneratedCustomStaticMeshEntry(
        MobyMeshType meshType,
        byte vertexCapacity)
    {
        const int vertexTableOffset = 0x30;
        var vertexData = new byte[vertexTableOffset + vertexCapacity * 0x10];
        WriteGeneratedCompactVertexHeader(vertexData, vertexTableOffset, vertexCapacity);

        return new MobyMeshTableEntry
        {
            VifListOffset = 0,
            VifListSize = 0,
            VifListTextureSize = 0,
            VertexDataOffset = 0,
            VertexDataSize = checked((byte)(vertexData.Length / 0x10)),
            Unknown0A = checked((byte)ResolveGeneratedMeshEntryUnknown0A(vertexCapacity)),
            CommonTransformJointIndex = ResolveGeneratedCommonTransformJointIndex(vertexCapacity),
            VertexCount = vertexCapacity,
            MeshType = meshType,
            VifData = [],
            VertexData = vertexData,
            VifTextureData = BuildCustomStaticTextureMetadataPayload(),
            GifTag = new MobyGifTag
            {
                TextureIds = BuildEmptyGifTextureIdList(),
                GifDataOffset = 0
            }
        };
    }

    private static void StripCustomStaticTemplateGameplayData(MobyModel model)
    {
        DropCustomStaticTemplateCollision(model);
        DropCustomStaticTemplateAnimations(model);
        DropCustomStaticTemplateAnimationJoints(model);
        DropCustomStaticTemplateSounds(model);
        DropCustomStaticTemplateShadow(model);
    }

    private static void DropCustomStaticTemplateCollision(MobyModel model)
    {
        model.Collision = null;
        model.CollisionOffset = 0;
    }

    private static void DropCustomStaticTemplateAnimations(MobyModel model)
    {
        model.Sequences.Clear();
        model.AnimationCount = 0;
    }

    private static void DropCustomStaticTemplateAnimationJoints(MobyModel model)
    {
        model.AnimationJoints = null;
        model.AnimationJointsOffset = 0;
    }

    private static void DropCustomStaticTemplateSounds(MobyModel model)
    {
        model.Sounds = null;
        model.SoundCount = 0;
        model.SoundDefOffset = 0;
    }

    private static void DropCustomStaticTemplateShadow(MobyModel model)
    {
        model.ShadowData = null;
        model.ShadowPrefixData = null;
        model.Shadow = 0;
    }

    private static void GenerateCustomStaticGlobalScaffold(MobyModel model)
    {
        model.BangleTable = null;
        model.BangleTableOffset = 0;
        model.CornCob = null;
        model.CornCobOffset = 0;
        DropCustomStaticTemplateCollision(model);
        DropCustomStaticTemplateShadow(model);
        DropCustomStaticTemplateSounds(model);
        DropCustomStaticTemplateAnimationJoints(model);
        MobyAnimationSlicer.ReplaceWithDefaultAnimation(model, model.AnimationFormat);
        model.TeamPaletteData.Clear();
        model.TeamPalettes = 0;
    }

    private static void GenerateCustomStaticHeaderDefaults(MobyModel model)
    {
        model.Padding = 0;
        model.LodTrans = 0xFF;
        model.MipmapDistance = 0x05;
        model.GlowRgba = 0;
        model.ModeBits = 0;
        model.Type = 0;
        model.ModeBits2 = 0;
    }

    private static void ApplyCustomStaticHeaderOverrides(MobyModel model, MobyGltfImportOptions options)
    {
        if (options.CustomStaticHeaderLodTrans is byte lodTrans)
        {
            model.LodTrans = lodTrans;
        }

        if (options.CustomStaticHeaderMipmapDistance is byte mipmapDistance)
        {
            model.MipmapDistance = mipmapDistance;
        }
    }

    private static void GenerateCustomStaticCommonTransforms(MobyModel model, bool generateSkeleton)
    {
        var maxControlIndex = model.MeshTable?.Entries.Count > 0
            ? model.MeshTable.Entries.Max(entry => (int)entry.CommonTransformJointIndex)
            : 0;
        var count = Math.Clamp(maxControlIndex + 1, 1, byte.MaxValue);
        var commonTransforms = new byte[count * 0x10];
        for (var i = 0; i < count; i++)
        {
            // Zero translation, parent 0. For row 0 this decodes as root; later rows share the root.
            BitConverter.GetBytes((ushort)0).CopyTo(commonTransforms, i * 0x10 + 0x0C);
        }

        model.CommonTransforms = commonTransforms;
        if (!generateSkeleton)
        {
            model.JointCount = 0;
            model.Skeleton = null;
            model.SkeletonOffset = 0;
            return;
        }

        model.JointCount = checked((byte)count);
        model.Skeleton = new MobySkeleton();
        for (var i = 0; i < count; i++)
        {
            model.Skeleton.Bones.Add(new MobyMatrix4
            {
                Row1 = new MobyMatrixRow { X = 1f },
                Row2 = new MobyMatrixRow { Y = 1f },
                Row3 = new MobyMatrixRow { Z = 1f },
                Row4 = new MobyMatrixRow { W = 1f }
            });
        }
    }

    private static void ApplyCustomStaticRigSource(
        MobyModel model,
        MobyModel rigSource,
        bool copyAnimation0,
        int? copyAnimationIndex)
    {
        model.JointCount = rigSource.JointCount;
        model.Skeleton = CloneSkeleton(rigSource.Skeleton);
        model.CommonTransforms = rigSource.CommonTransforms is null ? null : (byte[])rigSource.CommonTransforms.Clone();
        model.AnimationJoints = CloneAnimationJoints(rigSource.AnimationJoints);

        var animationIndex = copyAnimationIndex ?? (copyAnimation0 ? 0 : -1);
        if (animationIndex >= 0)
        {
            MobyAnimationSlicer.CopyAnimationAsZero(model, rigSource, animationIndex, model.AnimationFormat);
        }
        else
        {
            MobyAnimationSlicer.ReplaceWithDefaultAnimation(model, model.AnimationFormat);
        }
    }

    private static MobySkeleton? CloneSkeleton(MobySkeleton? source)
    {
        if (source is null)
        {
            return null;
        }

        var clone = new MobySkeleton();
        foreach (var bone in source.Bones)
        {
            clone.Bones.Add(CloneMatrix(bone));
        }

        return clone;
    }

    private static MobyMatrix4 CloneMatrix(MobyMatrix4 source)
    {
        return new MobyMatrix4
        {
            Row1 = CloneMatrixRow(source.Row1),
            Row2 = CloneMatrixRow(source.Row2),
            Row3 = CloneMatrixRow(source.Row3),
            Row4 = CloneMatrixRow(source.Row4)
        };
    }

    private static MobyMatrixRow CloneMatrixRow(MobyMatrixRow source)
    {
        return new MobyMatrixRow
        {
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            W = source.W
        };
    }

    private static List<MobyAnimationJoint>? CloneAnimationJoints(List<MobyAnimationJoint>? source)
    {
        if (source is null)
        {
            return null;
        }

        return source
            .Select(joint => new MobyAnimationJoint
            {
                SubSkeletonTokenOffset = joint.SubSkeletonTokenOffset,
                AnimationJointFlagsOrAuxIndex = joint.AnimationJointFlagsOrAuxIndex,
                Data = (byte[])joint.Data.Clone()
            })
            .ToList();
    }
}

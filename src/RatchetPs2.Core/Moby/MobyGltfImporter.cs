using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private const int DefaultCustomStaticMaxHighLodMeshes = 101;
    private const byte DefaultCustomStaticVertexPrefixShade = 0x29;
    private const int DefaultCustomStaticLow9ActiveWindowMaxValue = 13;
    private const float GeneratedScaleTargetQuantizedCoordinate = 24576f;

    public static MobyModel Import(
        Stream templateMoby,
        Stream gltf,
        Func<string, Stream> openBuffer,
        MobyGltfImportOptions? options = null,
        Stream? rigSourceMoby = null,
        Stream? skinReferenceMoby = null)
    {
        return ImportWithDiagnostics(templateMoby, gltf, openBuffer, options, rigSourceMoby, skinReferenceMoby).Model;
    }

    public static MobyGltfImportResult ImportWithDiagnostics(
        Stream templateMoby,
        Stream gltf,
        Func<string, Stream> openBuffer,
        MobyGltfImportOptions? options = null,
        Stream? rigSourceMoby = null,
        Stream? skinReferenceMoby = null)
    {
        ArgumentNullException.ThrowIfNull(templateMoby);
        ArgumentNullException.ThrowIfNull(gltf);
        ArgumentNullException.ThrowIfNull(openBuffer);

        options ??= new MobyGltfImportOptions();
        var readOptions = new MobyModelReadOptions { AnimationFormat = options.AnimationFormat };
        var rigSourceModel = rigSourceMoby is null ? null : MobyModelReader.Read(rigSourceMoby, readOptions);
        var skinReferenceModel = skinReferenceMoby is null ? null : MobyModelReader.Read(skinReferenceMoby, readOptions);
        var model = options.CustomStatic && options.CustomStaticUseGeneratedContainer
            ? CreateGeneratedCustomStaticContainer(options)
            : MobyModelReader.Read(templateMoby, readOptions);
        model.AnimationFormat = options.AnimationFormat;
        if (model.MeshTable is null)
        {
            throw new InvalidDataException("Template moby has no mesh table.");
        }
        if (options.CustomStatic)
        {
            model.GifTags.Clear();
        }

        var customStaticReplaceMeshIndex = options.CustomStaticReplaceMeshIndex;
        if (options.CustomStatic && options.CustomStaticUseOnlyReplaceMeshAsTemplate)
        {
            customStaticReplaceMeshIndex = KeepOnlyCustomStaticReplaceMeshTemplate(model, customStaticReplaceMeshIndex);
        }
        if (options.CustomStatic && options.CustomStaticGenerateMeshSlots && !options.CustomStaticGenerateMeshTable)
        {
            GenerateCustomStaticMeshSlots(model, options.CustomStaticGeneratedMeshSlotCapacity);
        }

        var templateModelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
        var templateDecodeScale = templateModelScale / 1024f;
        var imported = options.CustomStatic
            ? ReadCustomStaticGltf(
                gltf,
                openBuffer,
                model.MeshTable.Entries,
                customStaticReplaceMeshIndex,
                options.CustomStaticSplitMeshes,
                options.CustomStaticExpandTemplateMeshes,
                options.CustomStaticIsolatedTriangleTopology,
                options.CustomStaticMaxTrianglesPerMesh,
                options.CustomStaticMaxGeneratedMeshes,
                options.CustomStaticMaxHighLodMeshes,
                options.CustomStaticInitialTriangleCap,
                options.CustomStaticInitialTriangleCount,
                options)
            : ReadExporterShapedGltf(gltf, openBuffer);
        if (options.CustomStatic
            && options.CustomStaticGenerateMeshTable
            && options.CustomStaticProbeMeshIndices is not null)
        {
            CompactCustomStaticGeneratedProbeMeshTable(model, imported.Meshes, options.CustomStaticProbeMeshIndices);
        }
        if (options.CustomStatic && options.CustomStaticMaxHighLodMeshes is { } maxHighLodMeshes)
        {
            RebucketCustomStaticHighLodOverflowMeshes(model, imported.Meshes, maxHighLodMeshes);
        }
        UpdateMeshCounts(model);
        if (options.CustomStatic && Math.Abs(options.CustomStaticScale - 1f) > 0.000001f)
        {
            ScaleImportedMeshes(imported.Meshes, options.CustomStaticScale);
        }
        if (options.CustomStatic && Math.Abs(options.CustomStaticYawDegrees) > 0.000001f)
        {
            RotateImportedMeshesYaw(imported.Meshes, options.CustomStaticYawDegrees);
        }
        if (options.CustomStatic && Math.Abs(options.CustomStaticPitchDegrees) > 0.000001f)
        {
            RotateImportedMeshesPitch(imported.Meshes, options.CustomStaticPitchDegrees);
        }
        if (options.CustomStatic && Math.Abs(options.CustomStaticRollDegrees) > 0.000001f)
        {
            RotateImportedMeshesRoll(imported.Meshes, options.CustomStaticRollDegrees);
        }

        var outputScale = ResolveOutputModelScale(model, imported.Meshes, rigSourceModel, options, out var outputScaleSource);
        model.Scale = outputScale;
        var resolvedOutputModelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
        var outputQuantizationScale = resolvedOutputModelScale / 1024f;

        if (options.CustomStaticTransferReferenceSkinning)
        {
            if (skinReferenceModel is null)
            {
                throw new InvalidDataException("--custom-static-transfer-reference-skinning requires a skin reference moby.");
            }

            TransferReferenceSkinning(
                imported.Meshes,
                skinReferenceModel,
                outputQuantizationScale,
                options.CustomStaticReferenceSkinningSampleCount,
                options.CustomStaticReferenceSkinningVerticalWindow,
                options.CustomStaticReferenceSkinningSameSide,
                options.CustomStaticReferenceSkinningSideAxis,
                options.CustomStaticReferenceSkinningSideDeadzoneRatio,
                options.CustomStaticReferenceSkinningMaterialRegions,
                options.CustomStaticReferenceSkinningDisableAnatomicalFilters,
                options.CustomStaticReferenceSkinningPreserveLowerBodyFilters,
                options.CustomStaticReferenceSkinningPreserveShoulderFilters,
                options.CustomStaticReferenceSkinningShoulderInwardBias,
                options.CustomStaticReferenceSkinningTriangleCoherent,
                options.CustomStaticReferenceSkinningSplitPrimarySeams,
                options.CustomStaticReferenceSkinningRigidMeshCentroid,
                options.CustomStaticReferenceSkinningRigidTriangleCentroid,
                options.CustomStaticReferenceSkinningSmoothPrimaryIterations,
                options.CustomStaticReferenceSkinningDistancePower,
                options.CustomStaticReferenceSkinningYawDegrees,
                options.CustomStaticForcedSkinJointsByMeshIndex,
                options.CustomStaticForcedSourceTriangleSkinJoints,
                options.AnimationFormat);
            AssignReferenceBindWorldPositions(
                imported.Meshes,
                rigSourceModel ?? skinReferenceModel,
                options.CustomStaticReferenceSkinningYawDegrees);
        }
        else if (options.CustomStatic && options.CustomStaticApproximateRigSkinning)
        {
            if (rigSourceModel is null)
            {
                throw new InvalidDataException("--custom-static-approximate-rig-skinning requires a rig source moby.");
            }

            ApplyApproximateRigSkinning(imported.Meshes, rigSourceModel, options.CustomStaticApproximateRigSkinningUseSourcePose);
        }
        if (options.CustomStatic && Math.Abs(options.CustomStaticPostSkinYawDegrees) > 0.000001f)
        {
            RotateImportedMeshesYaw(imported.Meshes, options.CustomStaticPostSkinYawDegrees);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateAttachments)
        {
            model.BangleTable = null;
            model.CornCob = null;
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateNonBodyMeshes)
        {
            model.BangleTable = null;
            model.CornCob = null;
            model.MeshTable.Entries.RemoveAll(entry =>
                entry.MeshType is MobyMeshType.Bangle or MobyMeshType.Metal);
            UpdateMeshCounts(model);
        }
        if (options.CustomStatic && options.CustomStaticStripTemplateGameplayData)
        {
            StripCustomStaticTemplateGameplayData(model);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateCollision)
        {
            DropCustomStaticTemplateCollision(model);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateAnimations)
        {
            DropCustomStaticTemplateAnimations(model);
        }
        if (options.CustomStatic && options.CustomStaticGenerateDefaultAnimation)
        {
            MobyAnimationSlicer.ReplaceWithDefaultAnimation(model, model.AnimationFormat);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateAnimationJoints)
        {
            DropCustomStaticTemplateAnimationJoints(model);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateSounds)
        {
            DropCustomStaticTemplateSounds(model);
        }
        if (options.CustomStatic && options.CustomStaticDropTemplateShadow)
        {
            DropCustomStaticTemplateShadow(model);
        }
        if (options.CustomStatic && options.CustomStaticGenerateGlobalScaffold)
        {
            GenerateCustomStaticGlobalScaffold(model);
        }
        if (options.CustomStatic && options.CustomStaticGenerateHeaderDefaults)
        {
            GenerateCustomStaticHeaderDefaults(model);
        }
        if (options.CustomStatic)
        {
            ApplyCustomStaticHeaderOverrides(model, options);
        }
        if (rigSourceModel is not null)
        {
            ApplyCustomStaticRigSource(
                model,
                rigSourceModel,
                options.CustomStaticCopyRigAnimation0,
                options.CustomStaticCopyRigAnimationIndex);
            if (options.CustomStatic && options.CustomStaticWriteFittedRigCommonTransforms)
            {
                model.CommonTransforms = BuildFittedRigCommonTransforms(imported.Meshes, rigSourceModel);
            }
        }
        if (options.CustomStatic
            && options.CustomStaticProbeMeshIndices is not null
            && !(options.CustomStaticGenerateMeshTable && model.MeshTable is not null))
        {
            if (options.CustomStaticSkipUnprobedMeshes)
            {
                RemoveImportedMeshesOutsideProbe(imported.Meshes, options.CustomStaticProbeMeshIndices);
            }
            else
            {
                CollapseImportedMeshesOutsideProbe(imported.Meshes, options.CustomStaticProbeMeshIndices);
            }
        }
        MobyBoundingSphere? recalculatedBoundingSphere = null;
        if (options.CustomStatic && (options.CustomStaticRecalculateBoundingSphere || options.CustomStaticGenerateGlobalScaffold))
        {
            recalculatedBoundingSphere = RecalculateCustomStaticBoundingSphere(
                imported.Meshes,
                outputQuantizationScale,
                options.CustomStaticBoundingSpherePadding);
            if (recalculatedBoundingSphere is not null)
            {
                model.BoundingSphere = recalculatedBoundingSphere;
                foreach (var sequence in model.Sequences)
                {
                    sequence.BoundingSphere = new MobyBoundingSphere
                    {
                        X = recalculatedBoundingSphere.X,
                        Y = recalculatedBoundingSphere.Y,
                        Z = recalculatedBoundingSphere.Z,
                        Radius = recalculatedBoundingSphere.Radius
                    };
                }
            }
        }
        var templateMeshes = DecodeTemplateMeshes(model.MeshTable?.Entries ?? [], templateDecodeScale, model.JointCount);
        var customStaticTemplateReplaceIndex = options.CustomStaticSplitMeshes
            ? -1
            : customStaticReplaceMeshIndex;
        if (options.CustomStatic && options.CustomStaticPreserveTemplatePackets)
        {
            ApplyCustomStaticTemplateDeform(imported.Meshes, templateMeshes, customStaticTemplateReplaceIndex);
        }
        else if (options.CustomStatic && options.CustomStaticPreserveTemplateVertexLayout)
        {
            ApplyCustomStaticTemplateVertexLayout(imported.Meshes, templateMeshes, customStaticTemplateReplaceIndex);
        }
        if (options.CustomStatic && options.CustomStaticHideOtherMeshes)
        {
            AddHiddenTemplateMeshes(imported.Meshes, model.MeshTable!.Entries, templateMeshes, customStaticReplaceMeshIndex);
        }
        var diagnostics = new List<object>();
        var replaced = 0;
        foreach (var mesh in imported.Meshes.OrderBy(mesh => mesh.TemplateMeshIndex))
        {
            if (mesh.TemplateMeshIndex < 0 || mesh.TemplateMeshIndex >= model.MeshTable!.Entries.Count)
            {
                diagnostics.Add(new
                {
                    MeshIndex = mesh.TemplateMeshIndex,
                    Skipped = true,
                    Reason = "Mesh index is outside the template mesh table."
                });
                continue;
            }

            var entry = model.MeshTable.Entries[mesh.TemplateMeshIndex];
            var generateMeshEntryUnknown0A =
                options.CustomStatic
                &&
                (options.CustomStaticGenerateMeshEntryMetadata
                    || options.CustomStaticGenerateMeshEntryUnknown0A
                    || options.CustomStaticGenerateMeshEntryUnknown0ATotalQw)
                && !mesh.CustomStaticHideMesh;
            var generateCommonTransformJoint =
                options.CustomStatic && options.CustomStaticGenerateMeshEntryMetadata && !mesh.CustomStaticHideMesh;
            var zeroCommonTransformJoint =
                options.CustomStatic
                &&
                options.CustomStaticZeroCommonTransformJoint
                && !mesh.CustomStaticHideMesh;
            var zeroCommonTransformJointHeaderOnly =
                options.CustomStatic && options.CustomStaticZeroCommonTransformJointHeaderOnly && !mesh.CustomStaticHideMesh;
            if (zeroCommonTransformJoint)
            {
                entry.CommonTransformJointIndex = 0;
            }
            if (generateMeshEntryUnknown0A)
            {
                entry.Unknown0A = 0;
            }

            if (entry.MeshType != mesh.MeshType)
            {
                diagnostics.Add(new
                {
                    MeshIndex = mesh.TemplateMeshIndex,
                    Expected = entry.MeshType,
                    Actual = mesh.MeshType,
                    Skipped = true,
                    Reason = "glTF mesh type does not match the template mesh entry."
                });
                continue;
            }

            templateMeshes.TryGetValue(mesh.TemplateMeshIndex, out var templateMesh);
            var appliedPacketMode = mesh.CustomStaticHideMesh
                ? MobyGltfImportPacketMode.GenerateVertexPositions
                : options.CustomStatic && options.PacketMode == MobyGltfImportPacketMode.Auto
                    ? options.CustomStaticPreserveTemplatePackets
                        ? MobyGltfImportPacketMode.GenerateVertexPositions
                        : MobyGltfImportPacketMode.GenerateAll
                    : GetAppliedPacketMode(options, mesh.TemplateMeshIndex);

            byte? preReplacementCommonTransformJoint = null;
            if (generateCommonTransformJoint && !options.CustomStatic)
            {
                preReplacementCommonTransformJoint =
                    options.CustomStaticUseReferenceMeshCommonTransform
                    && TryGetDominantReferenceMeshCommonTransform(mesh, skinReferenceModel, out var referenceCommonTransformJoint, out _)
                        ? referenceCommonTransformJoint
                        : options.CustomStaticUseDominantSkinJointAsCommonTransform
                        && TryGetDominantSkinJoint(mesh, out var dominantSkinJoint)
                            ? dominantSkinJoint
                            : options.CustomStaticUseDominantHeadSkinJointAsCommonTransform
                            && TryGetDominantHeadSkinJoint(mesh, options.AnimationFormat, out var dominantHeadSkinJoint)
                                ? dominantHeadSkinJoint
                            : null;
                if (preReplacementCommonTransformJoint is not null)
                {
                    entry.CommonTransformJointIndex = preReplacementCommonTransformJoint.Value;
                }
            }

            var conversion = BuildMeshReplacement(entry, mesh, outputQuantizationScale, options, templateMesh, appliedPacketMode);
            entry.VertexData = conversion.VertexData;
            entry.VertexDataSize = checked((byte)(entry.VertexData.Length / 0x10));
            entry.VertexCount = options.CustomStatic
                && (options.CustomStaticPreserveTemplateRowContract || options.CustomStaticPreserveTemplateMeshVertexCount)
                && !mesh.CustomStaticHideMesh
                ? entry.VertexCount
                : options.CustomStatic && options.CustomStaticGenerateMeshTableVertexCount && !mesh.CustomStaticHideMesh
                    ? ResolveGeneratedMeshTableVertexCount(mesh, entry.VertexData)
                : options.CustomStatic && options.CustomStaticGenerateVifDomainCapacity && !mesh.CustomStaticHideMesh
                    ? entry.VertexCount
                : ResolveMeshTableVertexCount(mesh, conversion.VifData);
            entry.VifData = conversion.VifData;
            entry.VifListSize = checked((short)(entry.VifData.Length / 0x10));
            entry.VifTextureData = conversion.VifTextureData;
            if (options.CustomStatic && options.CustomStaticDropTextures && !mesh.CustomStaticHideMesh)
            {
                entry.VifTextureData = null;
                entry.GifTag = null;
            }
            else if (options.CustomStatic && options.CustomStaticGenerateTextureMetadata && !mesh.CustomStaticHideMesh)
            {
                entry.VifTextureData = BuildCustomStaticTextureMetadataPayload(
                    distance: options.CustomStaticTextureMetadataDistance);
                entry.GifTag = new MobyGifTag
                {
                    TextureIds = BuildEmptyGifTextureIdList(),
                    GifDataOffset = 0
                };
            }
            else if (options.CustomStatic && options.CustomStaticConstantTextures && !mesh.CustomStaticHideMesh)
            {
                entry.VifTextureData = BuildConstantTexturePayload(entry.VifTextureData);
            }
            var materialTextureId = TryApplyCustomStaticMaterialTextureId(entry, mesh, options);
            if (entry.VifTextureData is not null)
            {
                entry.VifListTextureSize = checked((short)((entry.VifTextureData.Length / 0x10) - 1));
                entry.VifListSize += checked((short)(entry.VifListTextureSize + 1));
            }
            else
            {
                entry.VifListTextureSize = 0;
            }
            if (generateMeshEntryUnknown0A)
            {
                var unknown0AQw = options.CustomStaticGenerateMeshEntryUnknown0ATotalQw
                    ? entry.VifListSize
                    : ResolveGeneratedMeshEntryUnknown0A(entry.VertexCount);
                entry.Unknown0A = checked((byte)Math.Min(byte.MaxValue, unknown0AQw));
            }
            if (generateCommonTransformJoint)
            {
                entry.CommonTransformJointIndex = preReplacementCommonTransformJoint
                    ?? ResolveGeneratedCommonTransformJointIndex(entry.VertexCount);
            }
            if (zeroCommonTransformJointHeaderOnly)
            {
                entry.CommonTransformJointIndex = 0;
            }
            replaced++;
            var meshBounds = Bounds3.From(mesh.Positions);
            var texCoordBounds = mesh.TexCoords is { Count: > 0 }
                ? Bounds2.From(mesh.TexCoords)
                : (Bounds2?)null;
            var vifVertexDomainCount = TryReadLeadingVertexDomainUnpackCount(entry.VifData, out var domainCount)
                ? domainCount
                : (byte?)null;
            var vertexHeaderDomainCapacity = entry.VertexData.Length >= 0x0C
                ? BitConverter.ToUInt16(entry.VertexData, 0x0A)
                : (ushort?)null;

            diagnostics.Add(new
            {
                MeshIndex = mesh.TemplateMeshIndex,
                entry.MeshType,
                ImportedVertexCount = mesh.Positions.Count,
                VertexCount = mesh.Positions.Count,
                VifVertexDomainCount = vifVertexDomainCount,
                VertexHeaderDomainCapacity = vertexHeaderDomainCapacity,
                MeshTableVertexCount = entry.VertexCount,
                MeshTableUnknown0A = entry.Unknown0A,
                MeshTableCommonTransformJointIndex = entry.CommonTransformJointIndex,
                ReferenceMeshCommonTransformSource = options.CustomStaticUseReferenceMeshCommonTransform
                    && TryGetDominantReferenceMeshCommonTransform(mesh, skinReferenceModel, out var diagnosticReferenceCommonTransformJoint, out var diagnosticReferenceMeshIndex)
                        ? new
                        {
                            MeshIndex = diagnosticReferenceMeshIndex,
                            CommonTransformJointIndex = diagnosticReferenceCommonTransformJoint
                        }
                        : null,
                TriangleCount = mesh.Indices.Count / 3,
                HasTexCoords = mesh.TexCoords is not null && mesh.TexCoords.Count == mesh.Positions.Count,
                CustomStaticSource = mesh.CustomStaticSourceMeshIndex is null
                    ? null
                    : new
                    {
                        MeshIndex = mesh.CustomStaticSourceMeshIndex,
                        PrimitiveIndex = mesh.CustomStaticSourcePrimitiveIndex,
                        MaterialIndex = mesh.CustomStaticSourceMaterialIndex,
                        MaterialName = mesh.CustomStaticSourceMaterialName,
                        AppliedUvScale = mesh.CustomStaticAppliedUvScale is null
                            ? null
                            : ToArray(mesh.CustomStaticAppliedUvScale.Value),
                        StartTriangle = mesh.CustomStaticSourceStartTriangle,
                        TriangleCount = mesh.CustomStaticSourceTriangleCount
                    },
                AppliedTextureId = materialTextureId,
                GifTextureIds = entry.GifTag?.TextureIds.Select(id => (int)id).ToArray(),
                VifTextureActiveTextureId = TryReadActiveTextureIdFromVifTextureData(entry.VifTextureData),
                DonorData = BuildMeshDonorDataSummary(entry, mesh, options, conversion),
                SkinAssignment = BuildSkinAssignmentDiagnostics(mesh),
                PositionBounds = new
                {
                    Min = ToArray(meshBounds.Min),
                    Max = ToArray(meshBounds.Max),
                    Size = ToArray(meshBounds.Size)
                },
                TexCoordBounds = texCoordBounds is null
                    ? null
                    : new
                    {
                        Min = ToArray(texCoordBounds.Value.Min),
                        Max = ToArray(texCoordBounds.Value.Max),
                        Size = ToArray(texCoordBounds.Value.Size)
                    },
                HasMeshMetadata = mesh.Metadata is not null,
                MetadataVersion = mesh.Metadata?.Version,
                RequestedPacketMode = options.PacketMode,
                AppliedPacketMode = appliedPacketMode,
                conversion.QuantizationClipCount,
                conversion.TruncatedInfluenceCount,
                conversion.TopologyConnectorIndexCount,
                conversion.UsedTemplateVertexData,
                conversion.UsedMetadataVertexLayout,
                conversion.UsedMetadataRowPrefixes,
                conversion.UsedMetadataLowVertexBits,
                conversion.PreservedTemplateLow9MaxValue,
                conversion.UsedMetadataTopologyLayout,
                conversion.WroteTexCoords,
                conversion.TexCoordWriteCount,
                conversion.TexCoordPaddingWriteCount,
                conversion.GeneratedTopologyFromGltf,
                conversion.GeneratedTopologyTokenCount,
                conversion.GeneratedTopologySourceTriangleCount,
                conversion.GeneratedTopologyPayloadFitsMetadata,
                conversion.GeneratedTopologyMatchesSourceTriangles,
                conversion.GeneratedTopologyPreservesTemplateControlMarkers,
                conversion.GeneratedTopologyMatchesTemplateControlShape,
                conversion.TemplateTopologyRestartCount,
                conversion.GeneratedTopologyRestartCount,
                conversion.TemplateTopologyNegativeTokenCount,
                conversion.GeneratedTopologyNegativeTokenCount,
                conversion.TemplateTopologyShape,
                conversion.GeneratedTopologyShape,
                conversion.TemplateTopologyTrace,
                conversion.GeneratedTopologyTrace,
                conversion.GeneratedTopologyRowUsage,
                conversion.TemplateTopologyZeroMarkers,
                conversion.GeneratedTopologyZeroMarkers,
                conversion.TopologySourceDiff,
                conversion.TopologyPayloadDiff,
                conversion.PreservedTemplateVifLayout,
                conversion.ExpandedTopologyVifPacket,
                conversion.ReusedTemplateTopology,
                conversion.RemappedTemplateTopology,
                conversion.OriginalTopologyPayloadBytes,
                conversion.NewTopologyPayloadBytes,
                conversion.CompactTopologyTextureOverlapBytes,
                Replaced = true,
                Note = "v1 importer preserves template texture/GIF metadata and non-topology VIF packets, then regenerates geometry vertex data and VIF topology. Skinning supports up to three weighted joints per vertex."
            });
        }

        if (options.CustomStatic && options.CustomStaticGenerateCommonTransforms && rigSourceModel is null)
        {
            GenerateCustomStaticCommonTransforms(model, options.CustomStaticGenerateCommonTransformSkeleton);
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var diagnosticsBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ImportType = "moby glTF template replacement",
            MeshesFound = imported.Meshes.Count,
            MeshesReplaced = replaced,
            TemplateMeshCount = model.MeshTable!.Entries.Count,
            CustomStatic = options.CustomStatic
                ? BuildCustomStaticDiagnostics(imported, options, model.MeshTable!.Entries)
                : null,
            Options = new
            {
                options.MaxInfluences,
                options.ScaleTolerance,
                options.PacketMode,
                options.CustomStatic,
                options.CustomStaticUseGeneratedContainer,
                options.CustomStaticReplaceMeshIndex,
                EffectiveCustomStaticReplaceMeshIndex = customStaticReplaceMeshIndex,
                options.CustomStaticScale,
                options.CustomStaticYawDegrees,
                options.CustomStaticPitchDegrees,
                options.CustomStaticRollDegrees,
                options.CustomStaticPostSkinYawDegrees,
                options.CustomStaticSplitMeshes,
                options.CustomStaticExpandTemplateMeshes,
                options.CustomStaticUseOnlyReplaceMeshAsTemplate,
                options.CustomStaticUseMinimalExpandedMeshSlots,
                options.CustomStaticGenerateMeshSlots,
                options.CustomStaticGenerateMeshTable,
                options.CustomStaticGeneratedMeshSlotCapacity,
                options.CustomStaticGenerateGlobalScaffold,
                options.CustomStaticGenerateHeaderDefaults,
                CustomStaticProbeMeshIndices = options.CustomStaticProbeMeshIndices is null
                    ? "all"
                    : string.Join(",", options.CustomStaticProbeMeshIndices.Order()),
                options.CustomStaticSkipUnprobedMeshes,
                options.OutputModelScale,
                ResolvedOutputModelScale = resolvedOutputModelScale,
                OutputModelScaleSource = outputScaleSource,
                options.CustomStaticRecalculateBoundingSphere,
                options.CustomStaticBoundingSpherePadding,
                RecalculatedBoundingSphere = recalculatedBoundingSphere is null
                    ? null
                    : new
                    {
                        recalculatedBoundingSphere.X,
                        recalculatedBoundingSphere.Y,
                        recalculatedBoundingSphere.Z,
                        recalculatedBoundingSphere.Radius
                    },
                options.CustomStaticPreserveTemplatePackets,
                options.CustomStaticPreserveTemplateVertexLayout,
                options.CustomStaticHideOtherMeshes,
                options.CustomStaticDropTemplateAttachments,
                options.CustomStaticDropTemplateNonBodyMeshes,
                options.CustomStaticStripTemplateGameplayData,
                options.CustomStaticDropTemplateCollision,
                options.CustomStaticDropTemplateAnimations,
                options.CustomStaticGenerateDefaultAnimation,
                options.CustomStaticDropTemplateAnimationJoints,
                options.CustomStaticDropTemplateSounds,
                options.CustomStaticDropTemplateShadow,
                options.CustomStaticDropTextures,
                options.CustomStaticConstantTextures,
                options.CustomStaticGenerateTextureMetadata,
                options.CustomStaticUseGeneratedTextureMetadataPrototype,
                options.CustomStaticGenerateMeshEntryMetadata,
                options.CustomStaticGenerateMeshEntryUnknown0A,
                options.CustomStaticGenerateMeshEntryUnknown0ATotalQw,
                options.CustomStaticZeroCommonTransformJoint,
                options.CustomStaticZeroCommonTransformJointHeaderOnly,
                options.CustomStaticUseDominantSkinJointAsCommonTransform,
                options.CustomStaticUseReferenceMeshCommonTransform,
                options.CustomStaticGenerateCommonTransforms,
                options.CustomStaticGenerateCommonTransformSkeleton,
                options.CustomStaticApproximateRigSkinning,
                options.CustomStaticTransferReferenceSkinning,
                options.CustomStaticReferenceSkinningSampleCount,
                options.CustomStaticReferenceSkinningVerticalWindow,
                options.CustomStaticReferenceSkinningSameSide,
                options.CustomStaticReferenceSkinningMaterialRegions,
                options.CustomStaticReferenceSkinningTriangleCoherent,
                options.CustomStaticReferenceSkinningSplitPrimarySeams,
                options.CustomStaticReferenceSkinningRigidMeshCentroid,
                options.CustomStaticReferenceSkinningRigidTriangleCentroid,
                options.CustomStaticReferenceSkinningSmoothPrimaryIterations,
                options.CustomStaticReferenceSkinningDistancePower,
                options.CustomStaticReferenceSkinningYawDegrees,
                options.CustomStaticCopyRigAnimation0,
                options.CustomStaticDoubleSided,
                options.CustomStaticPreserveTopologyTail,
                options.CustomStaticCompactTopologyPacket,
                options.CustomStaticForceZeroMarkerTopology,
                options.CustomStaticGenerateMinimalVifContainer,
                options.CustomStaticGenerateVifDomainCapacity,
                options.CustomStaticGenerateVertexHeaderDomainCapacity,
                options.CustomStaticGenerateMeshTableVertexCount,
                options.CustomStaticGenerateRigidVertexData,
                options.CustomStaticGenerateRigidRowsInTemplateLayout,
                options.CustomStaticGenerateCompactVertexHeader,
                options.CustomStaticRewriteTemplateEpilogueRows,
                options.CustomStaticRewriteTemplateEpiloguePrefixes,
                options.CustomStaticRewriteTemplateEpiloguePositions,
                options.CustomStaticGenerateTemplateEpilogueControlPrefix,
                options.CustomStaticClearTemplateEpilogueFinalMarker,
                options.CustomStaticGenerateTemplateEpilogueFinalMarker,
                options.CustomStaticNeutralizeTemplateSkinning,
                CustomStaticVertexPrefixBytes = options.CustomStaticVertexPrefixBytes is null
                    ? null
                    : Convert.ToHexString(options.CustomStaticVertexPrefixBytes.ToArray()),
                CustomStaticVertexPrefixShade = options.CustomStaticVertexPrefixShade,
                options.CustomStaticAutoVertexPrefixShade,
                options.CustomStaticAutoVertexControlLow9Tail,
                options.CustomStaticAutoPreserveTemplateLow9MaxValue,
                options.CustomStaticIsolatedTriangleTopology,
                options.CustomStaticMaxTrianglesPerMesh,
                options.CustomStaticMaxGeneratedMeshes,
                options.CustomStaticMaxHighLodMeshes,
                EffectiveCustomStaticMaxHighLodMeshes = options.CustomStaticMaxHighLodMeshes ?? DefaultCustomStaticMaxHighLodMeshes,
                options.CustomStaticInitialTriangleCap,
                options.CustomStaticInitialTriangleCount,
                CustomStaticMaterialTextureIds = options.CustomStaticMaterialTextureIds,
                CustomStaticMaterialUvScales = options.CustomStaticMaterialUvScales?.ToDictionary(
                    pair => pair.Key,
                    pair => ToArray(pair.Value),
                    StringComparer.OrdinalIgnoreCase),
                options.CustomStaticClampUvs,
                PacketModeMeshIndices = options.PacketModeMeshIndices is null
                    ? "all"
                    : string.Join(",", options.PacketModeMeshIndices.Order()),
                V1Skinning = "up to three weighted joints per vertex"
            },
            Meshes = diagnostics
        }, jsonOptions);

        return new MobyGltfImportResult(model, diagnosticsBytes);
    }

    private static MobyGltfImportPacketMode GetAppliedPacketMode(MobyGltfImportOptions options, int meshIndex)
    {
        return options.PacketModeMeshIndices is null || options.PacketModeMeshIndices.Contains(meshIndex)
            ? options.PacketMode
            : MobyGltfImportPacketMode.Passthrough;
    }

    private static float ResolveOutputModelScale(
        MobyModel model,
        IReadOnlyList<ImportedMesh> meshes,
        MobyModel? rigSourceModel,
        MobyGltfImportOptions options,
        out string source)
    {
        if (options.OutputModelScale is { } explicitScale && IsUsableScale(explicitScale))
        {
            source = "explicit_option";
            return explicitScale;
        }

        if (options.CustomStatic
            && rigSourceModel is not null
            && IsUsableScale(rigSourceModel.Scale)
            && (options.CustomStaticUseGeneratedContainer
                || options.CustomStaticCopyRigAnimation0
                || options.CustomStaticCopyRigAnimationIndex is not null
                || options.CustomStaticApproximateRigSkinning
                || options.CustomStaticTransferReferenceSkinning))
        {
            source = "rig_source";
            return rigSourceModel.Scale;
        }

        if (options.CustomStatic
            && options.CustomStaticUseGeneratedContainer
            && TryEstimateOutputModelScaleFromGeometry(meshes, out var estimatedScale))
        {
            source = "geometry_bounds";
            return estimatedScale;
        }

        if (IsUsableScale(model.Scale))
        {
            source = "template_or_container";
            return model.Scale;
        }

        source = "fallback";
        return 1f;
    }

    private static bool TryEstimateOutputModelScaleFromGeometry(
        IReadOnlyList<ImportedMesh> meshes,
        out float scale)
    {
        scale = 1f;
        var maxAbsCoordinate = 0f;
        foreach (var position in meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .SelectMany(mesh => mesh.Positions))
        {
            maxAbsCoordinate = MathF.Max(maxAbsCoordinate, MathF.Abs(position.X));
            maxAbsCoordinate = MathF.Max(maxAbsCoordinate, MathF.Abs(position.Y));
            maxAbsCoordinate = MathF.Max(maxAbsCoordinate, MathF.Abs(position.Z));
        }

        if (!float.IsFinite(maxAbsCoordinate) || maxAbsCoordinate <= 0.000001f)
        {
            return false;
        }

        scale = maxAbsCoordinate * 1024f / GeneratedScaleTargetQuantizedCoordinate;
        return IsUsableScale(scale);
    }

    private static bool IsUsableScale(float scale)
    {
        return float.IsFinite(scale) && Math.Abs(scale) > 1e-8f;
    }

    private static void ScaleImportedMeshes(IEnumerable<ImportedMesh> meshes, float scale)
    {
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.Positions.Count; i++)
            {
                mesh.Positions[i] *= scale;
            }
        }
    }


}

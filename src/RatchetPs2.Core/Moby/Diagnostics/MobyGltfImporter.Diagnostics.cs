using System.Numerics;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static object BuildMeshDonorDataSummary(
        MobyMeshTableEntry entry,
        ImportedMesh mesh,
        MobyGltfImportOptions options,
        MeshReplacement conversion)
    {
        var usesGeneratedMeshSlot =
            options.CustomStaticGenerateMeshTable
            || options.CustomStaticGenerateMeshSlots
            || options.CustomStaticUseMinimalExpandedMeshSlots;
        var generatesTextureMetadata = options.CustomStaticGenerateTextureMetadata
            && entry.GifTag is not null
            && entry.VifTextureData is not null;
        var hasSkinRows = !mesh.CustomStaticHideMesh
            && mesh.Joints is not null
            && mesh.Weights is not null;

        return new
        {
            UsesDonorMeshSlot = !usesGeneratedMeshSlot,
            UsesGeneratedMeshSlot = usesGeneratedMeshSlot,
            PreservesTemplateGifTagShape = entry.GifTag is not null
                && !options.CustomStaticGenerateTextureMetadata,
            PreservesTemplateVifTexturePacketShape = entry.VifTextureData is not null
                && !options.CustomStaticDropTextures
                && !options.CustomStaticGenerateTextureMetadata,
            GeneratesTextureMetadataFromScratch = generatesTextureMetadata,
            UsesGeneratedTextureMetadataPrototype = false,
            TextureMetadataSource = generatesTextureMetadata
                ? "generated_static_uya_payload"
                : entry.VifTextureData is not null
                    ? "template"
                    : "none",
            GeneratesMinimalVifContainer = options.CustomStaticGenerateMinimalVifContainer
                && !mesh.CustomStaticHideMesh,
            GeneratesVifDomainCapacity =
                options.CustomStaticGenerateVifDomainCapacity
                && !mesh.CustomStaticHideMesh,
            GeneratesVertexHeaderDomainCapacity =
                options.CustomStaticGenerateVertexHeaderDomainCapacity
                && !mesh.CustomStaticHideMesh,
            GeneratesMeshTableVertexCount =
                options.CustomStaticGenerateMeshTableVertexCount
                && !mesh.CustomStaticHideMesh,
            MeshTableVertexCountSource = options.CustomStaticGenerateMeshTableVertexCount && !mesh.CustomStaticHideMesh
                ? "vertex_header_domain_capacity"
                : "template_or_vif_domain",
            GeneratesMeshEntryMetadata =
                options.CustomStaticGenerateMeshEntryMetadata
                && !mesh.CustomStaticHideMesh,
            GeneratesMeshEntryUnknown0A =
                (options.CustomStaticGenerateMeshEntryMetadata
                    || options.CustomStaticGenerateMeshEntryUnknown0A
                    || options.CustomStaticGenerateMeshEntryUnknown0ATotalQw)
                && !mesh.CustomStaticHideMesh,
            GeneratesMeshEntryUnknown0AFromTotalQw =
                options.CustomStaticGenerateMeshEntryUnknown0ATotalQw
                && !mesh.CustomStaticHideMesh,
            GeneratesCommonTransformJointIndex =
                options.CustomStaticGenerateMeshEntryMetadata
                && !mesh.CustomStaticHideMesh,
            ZeroesCommonTransformJoint =
                (options.CustomStaticZeroCommonTransformJoint
                    || options.CustomStaticZeroCommonTransformJointHeaderOnly)
                && !mesh.CustomStaticHideMesh,
            ZeroesCommonTransformJointHeaderOnly =
                options.CustomStaticZeroCommonTransformJointHeaderOnly
                && !mesh.CustomStaticHideMesh,
            PatchesMaterialTextureIds = !mesh.CustomStaticHideMesh
                && options.CustomStaticMaterialTextureIds is not null
                && !string.IsNullOrWhiteSpace(mesh.CustomStaticSourceMaterialName),
            PatchesVifTextureActiveSelector = !mesh.CustomStaticHideMesh
                && entry.VifTextureData is not null
                && options.CustomStaticMaterialTextureIds is not null
                && !string.IsNullOrWhiteSpace(mesh.CustomStaticSourceMaterialName),
            UsesCompactGeneratedRows = options.CustomStaticGenerateCompactRigidRows && !mesh.CustomStaticHideMesh,
            GeneratesCompactVertexHeader = options.CustomStaticGenerateCompactVertexHeader && !mesh.CustomStaticHideMesh,
            PreservesTemplateRowContract = options.CustomStaticPreserveTemplateRowContract && !mesh.CustomStaticHideMesh,
            PreservesTemplateMeshVertexCount = (options.CustomStaticPreserveTemplateRowContract
                || options.CustomStaticPreserveTemplateMeshVertexCount) && !mesh.CustomStaticHideMesh,
            PreservesTemplateVertexHeaderCounts = (options.CustomStaticPreserveTemplateRowContract
                || options.CustomStaticPreserveTemplateVertexHeaderCounts) && !mesh.CustomStaticHideMesh,
            PreservesTemplateVertexAllocationSize = (options.CustomStaticPreserveTemplateRowContract
                || options.CustomStaticPadCompactRigidRowsToTemplateSize) && !mesh.CustomStaticHideMesh,
            PreservesTemplateVertexRowLayout = conversion.UsedMetadataVertexLayout
                || options.CustomStaticPreserveTemplateVertexLayout,
            PreservedTemplateLow9MaxValue = conversion.PreservedTemplateLow9MaxValue,
            UsesGeneratedPositions = !conversion.UsedTemplateVertexData,
            UsesGeneratedTexCoords = conversion.WroteTexCoords,
            UsesGeneratedTexCoordPadding = conversion.TexCoordPaddingWriteCount > 0,
            UsesGeneratedTopology = conversion.GeneratedTopologyFromGltf,
            UsesGeneratedSkinning = hasSkinRows
                && (!options.CustomStatic
                    || options.CustomStaticApproximateRigSkinning
                    || options.CustomStaticTransferReferenceSkinning
                    || HasUsableSkinRows(mesh)),
            UsesNeutralOrTemplateSkinning = !hasSkinRows
                || (options.CustomStatic
                    && !options.CustomStaticApproximateRigSkinning
                    && !options.CustomStaticTransferReferenceSkinning
                    && !HasUsableSkinRows(mesh)),
            Note = mesh.CustomStaticHideMesh
                ? "Hidden donor mesh keeps a tiny generated position payload so unrelated template slots do not render."
                : options.CustomStaticPreserveTemplateRowContract
                    ? "Custom static import generates row contents while preserving the donor mesh vertex-count, vertex-header, and vertex-allocation contract required by the template VU/VIF path."
                : "Custom static import generates mesh geometry, row data, topology, mesh metadata, and texture metadata; template dependency is now the enclosing moby container and packed texture assets."
        };
    }

    private static object? BuildSkinAssignmentDiagnostics(ImportedMesh mesh)
    {
        if (mesh.Joints is null || mesh.Weights is null || mesh.Joints.Count == 0 || mesh.Weights.Count == 0)
        {
            return null;
        }

        var vertexCount = Math.Min(mesh.Positions.Count, Math.Min(mesh.Joints.Count, mesh.Weights.Count));
        if (vertexCount <= 0)
        {
            return null;
        }

        var primaryJoints = new ushort[vertexCount];
        var primaryCounts = new Dictionary<ushort, int>();
        var primaryBounds = new Dictionary<ushort, (Vector3 Min, Vector3 Max, int Count)>();
        var multiInfluenceVertices = 0;
        for (var i = 0; i < vertexCount; i++)
        {
            var primary = GetPrimaryJoint(mesh.Joints[i], mesh.Weights[i]);
            primaryJoints[i] = primary;
            primaryCounts[primary] = primaryCounts.TryGetValue(primary, out var count) ? count + 1 : 1;
            var influenceCount = mesh.Weights[i].Count(weight => weight > 0.00001f);
            if (influenceCount > 1)
            {
                multiInfluenceVertices++;
            }

            if (primaryBounds.TryGetValue(primary, out var bounds))
            {
                primaryBounds[primary] = (Vector3.Min(bounds.Min, mesh.Positions[i]), Vector3.Max(bounds.Max, mesh.Positions[i]), bounds.Count + 1);
            }
            else
            {
                primaryBounds[primary] = (mesh.Positions[i], mesh.Positions[i], 1);
            }
        }

        var triangleCount = 0;
        var mixedPrimaryTriangles = 0;
        var edgeCount = 0;
        var disagreeingEdges = 0;
        var edgeSet = new HashSet<(int A, int B)>();
        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount || c < 0 || c >= vertexCount)
            {
                continue;
            }

            triangleCount++;
            if (primaryJoints[a] != primaryJoints[b] || primaryJoints[a] != primaryJoints[c])
            {
                mixedPrimaryTriangles++;
            }

            AddEdge(a, b);
            AddEdge(b, c);
            AddEdge(c, a);
        }

        foreach (var (a, b) in edgeSet)
        {
            edgeCount++;
            if (primaryJoints[a] != primaryJoints[b])
            {
                disagreeingEdges++;
            }
        }

        var topPrimaryJoints = primaryCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(8)
            .Select(pair => new
            {
                Joint = pair.Key,
                VertexCount = pair.Value,
                Ratio = pair.Value / (float)vertexCount
            })
            .ToArray();

        var widestJointBounds = primaryBounds
            .OrderByDescending(pair =>
            {
                var size = pair.Value.Max - pair.Value.Min;
                return Math.Max(size.X, Math.Max(size.Y, size.Z));
            })
            .ThenBy(pair => pair.Key)
            .Take(8)
            .Select(pair =>
            {
                var size = pair.Value.Max - pair.Value.Min;
                return new
                {
                    Joint = pair.Key,
                    pair.Value.Count,
                    Min = ToArray(pair.Value.Min),
                    Max = ToArray(pair.Value.Max),
                    Size = ToArray(size)
                };
            })
            .ToArray();

        var transferDiagnostics = mesh.SkinTransferDiagnostics.Count == vertexCount
            ? mesh.SkinTransferDiagnostics
            : null;
        var transferSummary = transferDiagnostics is null
            ? null
            : new
            {
                AverageNearestSampleDistance = transferDiagnostics.Average(item => item.NearestSampleDistance),
                MaxNearestSampleDistance = transferDiagnostics.Max(item => item.NearestSampleDistance),
                AverageConfidence = transferDiagnostics.Average(item => item.Confidence),
                LowConfidenceVertexCount = transferDiagnostics.Count(item => item.Confidence < 0.15f),
                LowConfidenceVertexRatio = transferDiagnostics.Count(item => item.Confidence < 0.15f) / (float)vertexCount,
                PrimaryNearestJointMismatchCount = transferDiagnostics.Count(item => item.PrimaryJoint != item.NearestSamplePrimaryJoint),
                PrimaryNearestJointMismatchRatio = transferDiagnostics.Count(item => item.PrimaryJoint != item.NearestSamplePrimaryJoint) / (float)vertexCount
            };
        var worstTransferVertices = transferDiagnostics is null
            ? null
            : transferDiagnostics
                .Select((item, index) => new
                {
                    VertexIndex = index,
                    item.PrimaryJoint,
                    item.NearestSamplePrimaryJoint,
                    item.NearestSampleDistance,
                    item.SecondSampleDistance,
                    item.Confidence,
                    item.CandidateCount,
                    item.NearestSampleMeshIndex,
                    item.NearestSampleVertexIndex,
                    Position = ToArray(item.Position),
                    NearestSamplePosition = ToArray(item.NearestSamplePosition)
                })
                .OrderByDescending(item => item.NearestSampleDistance)
                .ThenBy(item => item.Confidence)
                .Take(12)
                .ToArray();
        var worstMixedTriangles = BuildWorstMixedTriangleDiagnostics(mesh, primaryJoints, transferDiagnostics, vertexCount);

        return new
        {
            PrimaryJointCount = primaryCounts.Count,
            TopPrimaryJoints = topPrimaryJoints,
            MultiInfluenceVertexCount = multiInfluenceVertices,
            MultiInfluenceVertexRatio = multiInfluenceVertices / (float)vertexCount,
            MixedPrimaryTriangleCount = mixedPrimaryTriangles,
            MixedPrimaryTriangleRatio = triangleCount == 0 ? 0f : mixedPrimaryTriangles / (float)triangleCount,
            DisagreeingEdgeCount = disagreeingEdges,
            DisagreeingEdgeRatio = edgeCount == 0 ? 0f : disagreeingEdges / (float)edgeCount,
            WidestJointBounds = widestJointBounds,
            TransferSummary = transferSummary,
            WorstTransferVertices = worstTransferVertices,
            WorstMixedTriangles = worstMixedTriangles
        };

        void AddEdge(int a, int b)
        {
            if (a == b)
            {
                return;
            }

            edgeSet.Add(a < b ? (a, b) : (b, a));
        }
    }

    private static object[] BuildWorstMixedTriangleDiagnostics(
        ImportedMesh mesh,
        ushort[] primaryJoints,
        IReadOnlyList<SkinTransferVertexDiagnostics>? transferDiagnostics,
        int vertexCount)
    {
        var rows = new List<(float MaxNearestDistance, float MinConfidence, object Row)>();
        for (var i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            var a = checked((int)mesh.Indices[i]);
            var b = checked((int)mesh.Indices[i + 1]);
            var c = checked((int)mesh.Indices[i + 2]);
            if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount || c < 0 || c >= vertexCount)
            {
                continue;
            }

            if (primaryJoints[a] == primaryJoints[b] && primaryJoints[a] == primaryJoints[c])
            {
                continue;
            }

            var positions = new[] { mesh.Positions[a], mesh.Positions[b], mesh.Positions[c] };
            var maxEdge = MathF.Max(
                Vector3.Distance(positions[0], positions[1]),
                MathF.Max(
                    Vector3.Distance(positions[1], positions[2]),
                    Vector3.Distance(positions[2], positions[0])));
            var maxNearestDistance = transferDiagnostics is null
                ? 0f
                : MathF.Max(
                    transferDiagnostics[a].NearestSampleDistance,
                    MathF.Max(
                        transferDiagnostics[b].NearestSampleDistance,
                        transferDiagnostics[c].NearestSampleDistance));
            var minConfidence = transferDiagnostics is null
                ? 0f
                : MathF.Min(
                    transferDiagnostics[a].Confidence,
                    MathF.Min(
                        transferDiagnostics[b].Confidence,
                        transferDiagnostics[c].Confidence));

            rows.Add((maxNearestDistance, minConfidence, new
            {
                TriangleIndex = i / 3,
                Indices = new[] { a, b, c },
                PrimaryJoints = new[] { primaryJoints[a], primaryJoints[b], primaryJoints[c] },
                MaxLocalEdgeLength = maxEdge,
                MaxNearestSampleDistance = maxNearestDistance,
                MinTransferConfidence = minConfidence,
                Positions = positions.Select(ToArray).ToArray(),
                Transfer = transferDiagnostics is null
                    ? null
                    : new[]
                    {
                        BuildTriangleVertexTransferDiagnostic(transferDiagnostics[a]),
                        BuildTriangleVertexTransferDiagnostic(transferDiagnostics[b]),
                        BuildTriangleVertexTransferDiagnostic(transferDiagnostics[c])
                    }
            }));
        }

        return rows
            .OrderByDescending(row => row.MaxNearestDistance)
            .ThenBy(row => row.MinConfidence)
            .Take(12)
            .Select(row => row.Row)
            .ToArray();

        static object BuildTriangleVertexTransferDiagnostic(SkinTransferVertexDiagnostics item)
        {
            return new
            {
                item.PrimaryJoint,
                item.NearestSamplePrimaryJoint,
                item.NearestSampleDistance,
                item.SecondSampleDistance,
                item.Confidence,
                item.NearestSampleMeshIndex,
                item.NearestSampleVertexIndex,
                NearestSamplePosition = ToArray(item.NearestSamplePosition)
            };
        }
    }

    private static object BuildCustomStaticDiagnostics(
        ImportedGltf imported,
        MobyGltfImportOptions options,
        IReadOnlyList<MobyMeshTableEntry> templateEntries)
    {
        var visibleMeshes = imported.Meshes
            .Where(mesh => !mesh.CustomStaticHideMesh)
            .ToList();
        var usedTemplateMeshIndices = visibleMeshes
            .Select(mesh => mesh.TemplateMeshIndex)
            .Where(index => index >= 0 && index < templateEntries.Count)
            .Distinct()
            .Order()
            .ToArray();
        var hiddenMeshCount = imported.Meshes.Count(mesh => mesh.CustomStaticHideMesh);
        var highLodVisibleMeshCount = visibleMeshes.Count(mesh => mesh.MeshType == MobyMeshType.HighLod);
        var effectiveHighLodMeshLimit = options.CustomStaticMaxHighLodMeshes ?? DefaultCustomStaticMaxHighLodMeshes;
        var materialChunks = visibleMeshes
            .Where(mesh => mesh.CustomStaticSourceMeshIndex is not null)
            .GroupBy(mesh => mesh.CustomStaticSourceMaterialName ?? "(none)", StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                MaterialName = group.Key,
                TextureId = options.CustomStaticMaterialTextureIds is not null
                    && options.CustomStaticMaterialTextureIds.TryGetValue(group.Key, out var textureId)
                        ? textureId
                        : (byte?)null,
                UvScale = options.CustomStaticMaterialUvScales is not null
                    && options.CustomStaticMaterialUvScales.TryGetValue(group.Key, out var uvScale)
                        ? ToArray(uvScale)
                        : null,
                ChunkCount = group.Count(),
                TriangleCount = group.Sum(mesh => mesh.Indices.Count / 3),
                VertexCount = group.Sum(mesh => mesh.Positions.Count),
                TargetMeshIndices = group.Select(mesh => mesh.TemplateMeshIndex).Order().ToArray()
            })
            .OrderBy(group => group.TextureId)
            .ThenBy(group => group.MaterialName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new
        {
            ContractVersion = 1,
            SplitPolicy = new
            {
                MaterialFirst = true,
                SortKeys = "mapped texture id, material index, material name, original primitive order",
                OneMaterialPerSourcePrimitive = true
            },
            SourcePrimitives = imported.CustomStaticSourceMeshes?.Select(source =>
            {
                var positionBounds = Bounds3.From(source.Positions);
                var texCoordBounds = source.TexCoords is { Count: > 0 }
                    ? Bounds2.From(source.TexCoords)
                    : (Bounds2?)null;
                return new
                {
                    source.MeshIndex,
                    source.PrimitiveIndex,
                    source.OriginalOrder,
                    source.SplitOrder,
                    source.MaterialIndex,
                    source.MaterialName,
                    TextureId = source.MaterialName is not null
                        && options.CustomStaticMaterialTextureIds is not null
                        && options.CustomStaticMaterialTextureIds.TryGetValue(source.MaterialName, out var textureId)
                            ? textureId
                            : (byte?)null,
                    AppliedUvScale = source.AppliedUvScale is null ? null : ToArray(source.AppliedUvScale.Value),
                    source.ClampedUvComponentCount,
                    VertexCount = source.Positions.Count,
                    TriangleCount = source.Indices.Count / 3,
                    HasTexCoords = source.TexCoords is not null && source.TexCoords.Count == source.Positions.Count,
                    PositionBounds = new
                    {
                        Min = ToArray(positionBounds.Min),
                        Max = ToArray(positionBounds.Max),
                        Size = ToArray(positionBounds.Size)
                    },
                    TexCoordBounds = texCoordBounds is null
                        ? null
                        : new
                        {
                            Min = ToArray(texCoordBounds.Value.Min),
                            Max = ToArray(texCoordBounds.Value.Max),
                            Size = ToArray(texCoordBounds.Value.Size),
                            OutOfZeroToOneRange =
                                texCoordBounds.Value.Min.X < 0f
                                || texCoordBounds.Value.Min.Y < 0f
                                || texCoordBounds.Value.Max.X > 1f
                                || texCoordBounds.Value.Max.Y > 1f
                        }
                };
            }).ToArray(),
            Materials = materialChunks,
            GeneratedMeshUsage = new
            {
                VisibleMeshCount = visibleMeshes.Count,
                HighLodVisibleMeshCount = highLodVisibleMeshCount,
                EffectiveHighLodMeshLimit = effectiveHighLodMeshLimit,
                ExceedsKnownHighLodLimit = highLodVisibleMeshCount > effectiveHighLodMeshLimit,
                HiddenTemplateMeshCount = hiddenMeshCount,
                UsedTemplateMeshIndices = usedTemplateMeshIndices,
                FirstUsedTemplateMeshIndex = usedTemplateMeshIndices.Length == 0 ? (int?)null : usedTemplateMeshIndices[0],
                LastUsedTemplateMeshIndex = usedTemplateMeshIndices.Length == 0 ? (int?)null : usedTemplateMeshIndices[^1],
                ExpandedTemplateMeshTable = imported.OriginalTemplateMeshCount is not null
                    && templateEntries.Count > imported.OriginalTemplateMeshCount.Value,
                UsesMinimalExpandedMeshSlots = options.CustomStaticUseMinimalExpandedMeshSlots,
                UsesGeneratedMeshSlots = options.CustomStaticGenerateMeshSlots
                    || options.CustomStaticGenerateMeshTable,
                GeneratedMeshSlotCapacity = options.CustomStaticGenerateMeshSlots || options.CustomStaticGenerateMeshTable
                    ? options.CustomStaticGeneratedMeshSlotCapacity
                    : (int?)null,
                GeneratedMeshTableFromScratch = options.CustomStaticGenerateMeshTable,
                OriginalTemplateMeshCount = imported.OriginalTemplateMeshCount,
                FinalTemplateMeshCount = templateEntries.Count
            },
            DonorDependencyContract = new
            {
                RequiresTemplateMoby = !options.CustomStaticUseGeneratedContainer,
                UsesTemplateMeshSlots = !(options.CustomStaticGenerateMeshSlots || options.CustomStaticGenerateMeshTable),
                GeneratesMeshSlotPrototypes = options.CustomStaticGenerateMeshSlots
                    || options.CustomStaticGenerateMeshTable,
                GeneratesMeshTableFromScratch = options.CustomStaticGenerateMeshTable,
                GeneratesGlobalScaffold = options.CustomStaticGenerateGlobalScaffold,
                GeneratesHeaderDefaults = options.CustomStaticGenerateHeaderDefaults,
                GeneratesBoundingSphere = options.CustomStaticRecalculateBoundingSphere
                    || options.CustomStaticGenerateGlobalScaffold,
                GeneratesDefaultAnimation = options.CustomStaticGenerateDefaultAnimation
                    || options.CustomStaticGenerateGlobalScaffold,
                DropsTemplateAttachments = options.CustomStaticDropTemplateAttachments
                    || options.CustomStaticDropTemplateNonBodyMeshes
                    || options.CustomStaticGenerateGlobalScaffold,
                DropsTemplateCollision = options.CustomStaticDropTemplateCollision
                    || options.CustomStaticStripTemplateGameplayData
                    || options.CustomStaticGenerateGlobalScaffold,
                DropsTemplateShadow = options.CustomStaticDropTemplateShadow
                    || options.CustomStaticStripTemplateGameplayData
                    || options.CustomStaticGenerateGlobalScaffold,
                DropsTemplateSounds = options.CustomStaticDropTemplateSounds
                    || options.CustomStaticStripTemplateGameplayData
                    || options.CustomStaticGenerateGlobalScaffold,
                DropsTemplateAnimationJoints = options.CustomStaticDropTemplateAnimationJoints
                    || options.CustomStaticStripTemplateGameplayData
                    || options.CustomStaticGenerateGlobalScaffold,
                GeneratesVertexPositions = true,
                GeneratesTexCoords = true,
                GeneratesTopology = true,
                CompactsTopologyPacket = options.CustomStaticCompactTopologyPacket,
                GeneratesMinimalVifContainer = options.CustomStaticGenerateMinimalVifContainer,
                GeneratesVifDomainCapacity = options.CustomStaticGenerateVifDomainCapacity,
                GeneratesVertexHeaderDomainCapacity = options.CustomStaticGenerateVertexHeaderDomainCapacity,
                GeneratesMeshTableVertexCount = options.CustomStaticGenerateMeshTableVertexCount,
                MeshTableVertexCountSource = options.CustomStaticGenerateMeshTableVertexCount
                    ? "vertex_header_domain_capacity"
                    : "template_or_vif_domain",
                RewritesTemplateEpilogueRows = options.CustomStaticRewriteTemplateEpilogueRows,
                GeneratesCompactVertexHeader = options.CustomStaticGenerateCompactVertexHeader,
                RewritesTemplateEpiloguePrefixes = options.CustomStaticRewriteTemplateEpiloguePrefixes,
                RewritesTemplateEpiloguePositions = options.CustomStaticRewriteTemplateEpiloguePositions,
                GeneratesTemplateEpilogueControlPrefix = options.CustomStaticGenerateTemplateEpilogueControlPrefix,
                ClearsTemplateEpilogueFinalMarker = options.CustomStaticClearTemplateEpilogueFinalMarker,
                GeneratesTemplateEpilogueFinalMarker = options.CustomStaticGenerateTemplateEpilogueFinalMarker,
                GeneratesTexturePixels = false,
                GeneratesTexturePalettes = false,
                GeneratesMeshEntryMetadata = options.CustomStaticGenerateMeshEntryMetadata,
                GeneratesMeshEntryUnknown0A = options.CustomStaticGenerateMeshEntryMetadata
                    || options.CustomStaticGenerateMeshEntryUnknown0A
                    || options.CustomStaticGenerateMeshEntryUnknown0ATotalQw,
                GeneratesMeshEntryUnknown0AFromTotalQw = options.CustomStaticGenerateMeshEntryUnknown0ATotalQw,
                GeneratesCommonTransformJointIndex = options.CustomStaticGenerateMeshEntryMetadata,
                UsesDominantSkinJointAsCommonTransform = options.CustomStaticUseDominantSkinJointAsCommonTransform,
                UsesReferenceMeshCommonTransform = options.CustomStaticUseReferenceMeshCommonTransform,
                GeneratesCommonTransforms = options.CustomStaticGenerateCommonTransforms,
                GeneratesCommonTransformSkeleton = options.CustomStaticGenerateCommonTransformSkeleton,
                GeneratesApproximateRigSkinning = options.CustomStaticApproximateRigSkinning,
                ApproximateRigSkinningPoseSource = options.CustomStaticApproximateRigSkinningUseSourcePose
                    ? "rig_source_common_trans"
                    : "fitted_to_imported_mesh_bounds",
                WritesFittedRigCommonTransforms = options.CustomStaticWriteFittedRigCommonTransforms,
                WritesSkinPositionsRelativeToBind = options.CustomStaticSkinPositionsRelativeToBind,
                TransfersReferenceSkinning = options.CustomStaticTransferReferenceSkinning,
                ReferenceSkinningSampleCount = options.CustomStaticReferenceSkinningSampleCount,
                ReferenceSkinningVerticalWindow = options.CustomStaticReferenceSkinningVerticalWindow,
                ReferenceSkinningSameSide = options.CustomStaticReferenceSkinningSameSide,
                ReferenceSkinningSideAxis = options.CustomStaticReferenceSkinningSideAxis,
                ReferenceSkinningSideDeadzoneRatio = options.CustomStaticReferenceSkinningSideDeadzoneRatio,
                ReferenceSkinningMaterialRegions = options.CustomStaticReferenceSkinningMaterialRegions,
                ReferenceSkinningDisablesAnatomicalFilters = options.CustomStaticReferenceSkinningDisableAnatomicalFilters,
                ReferenceSkinningPreservesLowerBodyFilters = options.CustomStaticReferenceSkinningPreserveLowerBodyFilters,
                ReferenceSkinningPreservesShoulderFilters = options.CustomStaticReferenceSkinningPreserveShoulderFilters,
                ReferenceSkinningShoulderInwardBias = options.CustomStaticReferenceSkinningShoulderInwardBias,
                ReferenceSkinningTriangleCoherent = options.CustomStaticReferenceSkinningTriangleCoherent,
                ReferenceSkinningSplitPrimarySeams = options.CustomStaticReferenceSkinningSplitPrimarySeams,
                ReferenceSkinningRigidMeshCentroid = options.CustomStaticReferenceSkinningRigidMeshCentroid,
                ReferenceSkinningRigidTriangleCentroid = options.CustomStaticReferenceSkinningRigidTriangleCentroid,
                ReferenceSkinningSmoothPrimaryIterations = options.CustomStaticReferenceSkinningSmoothPrimaryIterations,
                ReferenceSkinningDistancePower = options.CustomStaticReferenceSkinningDistancePower,
                ReferenceSkinningYawDegrees = options.CustomStaticReferenceSkinningYawDegrees,
                CopiesRigAnimation0 = options.CustomStaticCopyRigAnimation0,
                CopiedRigAnimationIndex = options.CustomStaticCopyRigAnimationIndex,
                ZeroesCommonTransformJoint = options.CustomStaticZeroCommonTransformJoint
                    || options.CustomStaticZeroCommonTransformJointHeaderOnly,
                ZeroesCommonTransformJointHeaderOnly = options.CustomStaticZeroCommonTransformJointHeaderOnly,
                GeneratesGifAndVifTextureMetadataFromScratch = options.CustomStaticGenerateTextureMetadata,
                UsesGeneratedTextureMetadataPrototype = false,
                TextureMetadataSource = options.CustomStaticGenerateTextureMetadata
                    ? "generated_static_uya_payload"
                    : options.CustomStaticDropTextures
                        ? "none"
                        : "template",
                GeneratesGifAndVifTextureMetadataFromTemplatePrototype = false,
                PreservesAndPatchesTemplateGifAndVifTextureMetadata =
                    options.CustomStaticMaterialTextureIds is not null
                    && !options.CustomStaticDropTextures
                    && !options.CustomStaticGenerateTextureMetadata,
                PreservesTemplateVertexLayoutOrRowControl =
                    options.CustomStaticPreserveTemplateVertexLayout
                    || options.CustomStaticFlattenVertexPrefixes
                    || options.CustomStaticNeutralizeTemplateSkinning,
                VertexRowControlStrategy = new
                {
                    PreservesFullTemplateControlWord = options.CustomStaticPreserveTemplateVertexControlWords,
                    ZeroesHighBits = options.CustomStaticZeroVertexControlHighBits
                        || !options.CustomStaticPreserveTemplateVertexControlWords,
                    ForcedLow9Value = ResolveCustomStaticVertexControlLow9Value(options),
                    AutoVertexControlLow9Tail = options.CustomStaticAutoVertexControlLow9Tail,
                    PreservesAllTemplateLow9 = options.CustomStaticPreserveTemplateVertexControlLowBits
                        || (!options.CustomStaticPreserveTemplateVertexControlWords
                            && ResolveCustomStaticVertexControlLow9Value(options) is null),
                    VertexPrefixBytes = options.CustomStaticVertexPrefixBytes is null
                        ? null
                        : Convert.ToHexString(options.CustomStaticVertexPrefixBytes.ToArray()),
                    VertexPrefixShade = options.CustomStaticVertexPrefixShade,
                    AutoVertexPrefixShade = options.CustomStaticAutoVertexPrefixShade,
                    AutoPreservesTemplateLow9MaxValue = options.CustomStaticAutoPreserveTemplateLow9MaxValue,
                    PreservesTemplateLow9MaxValue = options.CustomStaticPreserveTemplateLow9MaxValue,
                    PreservesTemplateSparseLow9Count = options.CustomStaticPreserveTemplateSparseLow9Count,
                    PreservesDuplicateLow9Values = options.CustomStaticPreserveDuplicateLow9Values,
                    PreservesLow9UpToMaxDuplicate = options.CustomStaticPreserveLow9UpToMaxDuplicate
                },
                SupportsArbitrarySkeletonImport = false,
                RecommendedCurrentInputContract =
                    "Static glTF, triangle primitives, one material per primitive, square PS2-ready textures, alpha <= 128, material-to-texture-id mapping supplied, and conservative split limits."
            }
        };
    }

}

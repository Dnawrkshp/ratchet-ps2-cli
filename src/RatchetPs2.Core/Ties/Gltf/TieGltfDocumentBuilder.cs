using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfDocumentBuilder
{
    public static TieGltfExport Build(
        TieClass tie,
        TieLodTopology topology,
        GltfGeometry geometry,
        TieGltfNormalBuildResult normalResult,
        TieGltfSourceNormalPhaseAnalysis sourceNormalPhaseAnalysis,
        TieGltfGlowBuildResult glowColorResult,
        TieGltfAmbientBuildResult ambientIndexResult,
        IReadOnlyList<PacketIndexGroup> sourcePacketIndexGroups,
        int packetRgbaSlotCount,
        int sourceNormalPhaseWindingRepairStripCount,
        int sourceNormalPhaseWindingRepairTriangleCount,
        int sourcePositionCount,
        string binFileName,
        TieGameProfile profile,
        IReadOnlyDictionary<int, string>? externalTextureUris,
        IReadOnlyDictionary<int, TextureSize>? externalTextureSizes,
        IReadOnlyDictionary<int, TextureAlphaInfo>? externalTextureAlpha,
        bool includeDiagnostics,
        bool minify,
        GltfExportMetadataMode metadataMode)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(normalResult);
        ArgumentNullException.ThrowIfNull(sourceNormalPhaseAnalysis);
        ArgumentNullException.ThrowIfNull(glowColorResult);
        ArgumentNullException.ThrowIfNull(ambientIndexResult);
        ArgumentNullException.ThrowIfNull(sourcePacketIndexGroups);
        ArgumentNullException.ThrowIfNull(profile);

        var materialBuilder = new TieGltfMaterialBuilder(
            tie.Shaders,
            externalTextureUris,
            externalTextureAlpha,
            profile,
            metadataMode);
        var glowEmission = TieGltfGlowBuilder.BuildEmissionMaterial(glowColorResult.Rgba);
        using var binStream = new MemoryStream();
        using var writer = new BinaryWriter(binStream);
        var gltfBufferWriter = new GltfBufferWriter(writer);

        var positionAccessor = gltfBufferWriter.WriteVector3Accessor(
            geometry.Positions,
            target: GltfBufferWriter.ArrayBufferTarget,
            includeMinMax: true);
        var normalAccessor = gltfBufferWriter.WriteVector3Accessor(
            geometry.Normals,
            target: GltfBufferWriter.ArrayBufferTarget,
            includeMinMax: false);
        var useSourceNormalMaskAttribute = geometry.SourceNormalMask.Count == geometry.Positions.Count
            && geometry.SourceNormalMask.Any(value => value < 0.5f);
        var sourceNormalMaskAccessor = useSourceNormalMaskAttribute
            ? gltfBufferWriter.WriteScalarFloatAccessor(
                geometry.SourceNormalMask,
                target: GltfBufferWriter.ArrayBufferTarget,
                includeMinMax: true)
            : (int?)null;
        var useSourceNormalStateAttribute = geometry.SourceNormalStates.Count == geometry.Positions.Count
            && geometry.SourceNormalStates.Any(value => value > 0.5f);
        var sourceNormalStateAccessor = useSourceNormalStateAttribute
            ? gltfBufferWriter.WriteScalarFloatAccessor(
                geometry.SourceNormalStates,
                target: GltfBufferWriter.ArrayBufferTarget,
                includeMinMax: true)
            : (int?)null;
        var texCoordAccessor = gltfBufferWriter.WriteVector2Accessor(
            geometry.TexCoords,
            target: GltfBufferWriter.ArrayBufferTarget);
        var useMultipassTexCoordAttribute = geometry.MultipassTexCoords.Count == geometry.Positions.Count;
        var multipassTexCoordAccessor = useMultipassTexCoordAttribute
            ? gltfBufferWriter.WriteVector2Accessor(
                geometry.MultipassTexCoords,
                target: GltfBufferWriter.ArrayBufferTarget)
            : (int?)null;
        var useGlowEmissionAttribute = geometry.GlowColors.Count == geometry.Positions.Count
            && TieGltfGlowBuilder.CountActiveVertices(geometry.GlowColors) > 0;
        var glowEmissionAccessor = useGlowEmissionAttribute
            ? gltfBufferWriter.WriteVector4Accessor(
                geometry.GlowColors.Select(TieGltfGlowBuilder.ToEmissionAttribute).ToArray(),
                target: GltfBufferWriter.ArrayBufferTarget)
            : (int?)null;
        var useAmbientIndexAttribute = geometry.AmbientIndices.Count == geometry.Positions.Count
            && ambientIndexResult.ResolvedVertexCount > 0;
        var ambientIndexAccessor = useAmbientIndexAttribute
            ? gltfBufferWriter.WriteScalarFloatAccessor(
                geometry.AmbientIndices,
                target: GltfBufferWriter.ArrayBufferTarget,
                includeMinMax: true)
            : (int?)null;

        var attributes = new Dictionary<string, int>
        {
            ["POSITION"] = positionAccessor,
            ["NORMAL"] = normalAccessor,
            ["TEXCOORD_0"] = texCoordAccessor
        };
        if (multipassTexCoordAccessor.HasValue)
        {
            attributes["TEXCOORD_1"] = multipassTexCoordAccessor.Value;
        }
        if (sourceNormalMaskAccessor.HasValue)
        {
            attributes[profile.SourceNormalAttributeName] = sourceNormalMaskAccessor.Value;
        }
        if (sourceNormalStateAccessor.HasValue)
        {
            attributes[profile.SourceNormalStateAttributeName] = sourceNormalStateAccessor.Value;
        }
        if (glowEmissionAccessor.HasValue)
        {
            attributes[profile.GlowEmissionAttributeName] = glowEmissionAccessor.Value;
        }
        if (ambientIndexAccessor.HasValue)
        {
            attributes[profile.AmbientIndexAttributeName] = ambientIndexAccessor.Value;
        }

        var primitives = new List<Dictionary<string, object>>();
        foreach (var group in geometry.PacketIndexGroups)
        {
            var indexAccessor = gltfBufferWriter.WriteUInt32IndexAccessor(group.Indices);
            var materialIndex = materialBuilder.GetMaterialIndex(
                group.ShaderIndex,
                group.MultipassOffset,
                group.PassFlags,
                group.MultipassUvSize,
                group.EnvPassBleedColor,
                tie.Header.ModeBits,
                group.UseGlowEmission ? glowEmission : null);
            var glowRgbaIndexCount = TieGltfGlowBuilder.CountActiveIndices(group.Indices, geometry.GlowColors);
            var primitiveDefinition = new Dictionary<string, object>
            {
                ["attributes"] = attributes,
                ["indices"] = indexAccessor,
                ["mode"] = 4,
                ["material"] = materialIndex
            };
            if (ShouldWriteFullMetadata(metadataMode))
            {
                primitiveDefinition["extras"] = new
                {
                    group.PacketIndex,
                    group.ShaderIndex,
                    group.MultipassOffset,
                    MultipassType = group.PassFlags,
                    group.PassFlags,
                    TiePassFlags = group.PassFlags,
                    TiePassFlagsBits = TiePassFlags.FormatByteBits(group.PassFlags),
                    TieSecondPassMode = TiePassFlags.ResolveSecondPassMode(group.PassFlags),
                    TieTextureMatrixEnabled = TiePassFlags.UsesTextureMatrix(group.PassFlags),
                    TieTextureMatrixSelector = TiePassFlags.TextureMatrixSelector(group.PassFlags),
                    TieEnvironmentPassBits = TiePassFlags.EnvironmentPassBits(group.PassFlags),
                    group.MultipassUvSize,
                    TieMultipassUvRole = TiePassFlags.ResolveMultipassUvRole(
                        group.PassFlags,
                        group.MultipassUvSize),
                    TieReflectiveBleedColor = group.EnvPassBleedColor?.ToRgbaHex(),
                    PacketShaderIndices = group.PacketShaderIndices,
                    PacketShaderSwitchVuAddresses = group.PacketShaderSwitchVuAddresses,
                    HeaderModeBits = FormatModeBits(tie.Header.ModeBits),
                    GlowRgbaIndexCount = glowRgbaIndexCount,
                    GlowRgbaUsesEmission = group.UseGlowEmission,
                    GlowRgbaEmissionStrength = group.UseGlowEmission ? glowEmission.Strength : 0f,
                    TriangleCount = group.Indices.Count / 3
                };
            }

            primitives.Add(primitiveDefinition);
        }

        var binBytes = binStream.ToArray();
        var gameLabel = profile.GameLabel;
        var meshName = $"tie_0x{(ushort)tie.Header.OClass:X4}_lod{topology.LodIndex}";
        var gltf = new Dictionary<string, object>
        {
            ["asset"] = new { version = "2.0", generator = $"RatchetPs2 {gameLabel} tie glTF exporter" },
            ["scene"] = 0,
            ["scenes"] = new[] { new { nodes = new[] { 0 } } },
            ["nodes"] = new[]
            {
                new
                {
                    name = meshName,
                    mesh = 0,
                    extras = BuildNodeExtras(tie, topology, gameLabel, metadataMode)
                }
            },
            ["meshes"] = new[]
            {
                new
                {
                    name = meshName,
                    primitives,
                    extras = BuildMeshExtras(
                        tie,
                        topology,
                        normalResult,
                        sourceNormalPhaseAnalysis,
                        glowColorResult,
                        ambientIndexResult,
                        packetRgbaSlotCount,
                        sourceNormalPhaseWindingRepairStripCount,
                        sourceNormalPhaseWindingRepairTriangleCount,
                        geometry.WindingRepairResult,
                        geometry.SuppressedGeneratedNormalFallbackVertexCount,
                        geometry.SourceNormalMask.Count(value => value < 0.5f),
                        sourceNormalMaskAccessor.HasValue ? geometry.SourceNormalMask.Count : 0,
                        sourceNormalStateAccessor.HasValue ? geometry.SourceNormalStates.Count : 0,
                        geometry.PacketIndexGroups.Any(group => group.UseGlowEmission),
                        gameLabel,
                        metadataMode)
                }
            },
            ["materials"] = materialBuilder.Materials,
            ["buffers"] = new[] { new { uri = binFileName, byteLength = binBytes.Length } },
            ["bufferViews"] = gltfBufferWriter.BufferViews,
            ["accessors"] = gltfBufferWriter.Accessors
        };
        if (geometry.PacketIndexGroups.Any(group => group.UseGlowEmission))
        {
            gltf["extensionsUsed"] = new[] { TieGltfMaterialBuilder.EmissiveStrengthExtensionName };
        }

        if (materialBuilder.Images.Count > 0)
        {
            gltf["images"] = materialBuilder.Images;
        }

        if (materialBuilder.Textures.Count > 0)
        {
            gltf["samplers"] = materialBuilder.Samplers;
            gltf["textures"] = materialBuilder.Textures;
        }

        var jsonOptions = new JsonSerializerOptions { WriteIndented = !minify };
        var gltfBytes = JsonSerializer.SerializeToUtf8Bytes(gltf, jsonOptions);
        var diagnosticsBytes = includeDiagnostics
            ? JsonSerializer.SerializeToUtf8Bytes(new
        {
            ExportType = $"{gameLabel} tie LOD geometry",
            Note = "Preview geometry reconstructed from tie packet strip controls, decoded logical vertex VU address mapping, packet shader references, packet shader switch VU addresses, and ST texture coordinates.",
            LodIndex = topology.LodIndex,
            topology.LogicalVertexCount,
            topology.PrimaryAddressMappedLogicalVertexCount,
            topology.SecondaryAddressMappedLogicalVertexCount,
            topology.UnresolvedLogicalVertexCount,
            topology.TriangleCount,
            PreviousStripReferencePhaseStripCount = topology.Strips.Count(strip => strip.UsesPreviousStripReferencePhase),
            PreviousStripReferencePhaseTriangleCount = topology.Strips
                .Where(strip => strip.UsesPreviousStripReferencePhase)
                .Sum(strip => strip.TriangleCount),
            SourceNormalVertexCount = normalResult.SourceNormalVertexCount,
            SourceNormalIndexOffsetCount = normalResult.SourceNormalIndexOffsets.Count,
            SourcePacketRowNormalVertexCount = normalResult.PacketRowNormalVertexCount,
            SourceTableNormalVertexCount = normalResult.TableNormalVertexCount,
            SourceRgbaRecipeNormalVertexCount = normalResult.RgbaRecipeNormalVertexCount,
            SourceRgbaRecipeConstantColorVertexCount = normalResult.RgbaRecipeConstantColorVertexCount,
            SourceRgbaRecipeDegenerateBlendVertexCount = normalResult.RgbaRecipeDegenerateBlendVertexCount,
            SourceCrossLodExactNormalVertexCount = normalResult.CrossLodExactNormalVertexCount,
            SourceDuplicatePositionExactNormalVertexCount = normalResult.DuplicatePositionExactNormalVertexCount,
            SourceTableNormalLayout = normalResult.TableNormalLayout,
            SourceTableNormalTargetMode = normalResult.TableNormalTargetMode,
            SourceTableNormalPreserveSourceOrientation = normalResult.TableNormalPreserveSourceOrientation,
            SourceTableNormalCandidateVertexCount = normalResult.TableNormalCandidateVertexCount,
            SourceTableNormalAcceptedVertexCount = normalResult.TableNormalAcceptedVertexCount,
            SourceTableNormalSignedAcceptedVertexCount = normalResult.TableNormalSignedAcceptedVertexCount,
            SourceTableNormalInvertedAcceptedVertexCount = normalResult.TableNormalInvertedAcceptedVertexCount,
            SourceTableNormalUpperHemisphereVertexCount = normalResult.TableNormalUpperHemisphereVertexCount,
            SourceTableNormalUpperHemisphereStrongDownVertexCount = normalResult.TableNormalUpperHemisphereStrongDownVertexCount,
            SourceNormalPhaseDominantLayout = sourceNormalPhaseAnalysis.DominantLayout?.ToString(),
            SourceNormalPhaseScoredStripCount = sourceNormalPhaseAnalysis.ScoredStripCount,
            SourceNormalPhaseCurrentVoteStripCount = sourceNormalPhaseAnalysis.CurrentVoteStripCount,
            SourceNormalPhaseInvertedVoteStripCount = sourceNormalPhaseAnalysis.InvertedVoteStripCount,
            SourceNormalPhaseAmbiguousVoteStripCount = sourceNormalPhaseAnalysis.AmbiguousVoteStripCount,
            SourceNormalPhaseInsufficientVoteStripCount = sourceNormalPhaseAnalysis.InsufficientVoteStripCount,
            SourceNormalPhaseWindingRepairStripCount = sourceNormalPhaseWindingRepairStripCount,
            SourceNormalPhaseWindingRepairTriangleCount = sourceNormalPhaseWindingRepairTriangleCount,
            normalResult.DuplicatePositionNormalWeldMode,
            normalResult.DuplicatePositionNormalPairCount,
            normalResult.DuplicatePositionIncompatibleNormalPairCount,
            normalResult.DuplicatePositionCurrentAverageFaceDot,
            normalResult.DuplicatePositionWeldedAverageFaceDot,
            normalResult.DuplicatePositionWeldedMinimumFaceDot,
            WindingInvertedComponentTriangleCount = geometry.WindingRepairResult.InvertedComponentTriangleCount,
            WindingLocalInwardTriangleCount = geometry.WindingRepairResult.LocalInwardTriangleCount,
            WindingOpposedNormalTriangleCount = geometry.WindingRepairResult.OpposedNormalTriangleCount,
            WindingUpperHorizontalTriangleCount = geometry.WindingRepairResult.UpperHorizontalTriangleCount,
            GeneratedNormalFallbackVertexCount = sourcePositionCount - normalResult.SourceNormalVertexCount,
            ExpandedGeneratedNormalFallbackVertexCount = geometry.SourceNormalMask.Count(value => value < 0.5f),
            SourceNormalMaskAttributeCount = sourceNormalMaskAccessor.HasValue ? geometry.SourceNormalMask.Count : 0,
            SourceNormalStateAttributeCount = sourceNormalStateAccessor.HasValue ? geometry.SourceNormalStates.Count : 0,
            SourceNormalStateMissingVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.Missing),
            SourceNormalStateTableExactVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.TableExact),
            SourceNormalStatePacketRowExactVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.PacketRowExact),
            SourceNormalStateAmbiguousRemapVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.AmbiguousRemap),
            SourceNormalStateRejectedRemapVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.RejectedRemap),
            SourceNormalStateCrossLodExactVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.CrossLodExact),
            SourceNormalStateDuplicatePositionExactVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.DuplicatePositionExact),
            SourceNormalStateRgbaRecipeConstantColorVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.RgbaRecipeConstantColor),
            SourceNormalStateRgbaRecipeDegenerateBlendVertexCount = CountSourceNormalState(geometry, TieGltfSourceNormalState.RgbaRecipeDegenerateBlend),
            SourceNormalMissingDecodedDinkyVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex?.Kind == TiePacketDecodedVertexKind.Dinky),
            SourceNormalMissingDecodedFatVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex?.Kind == TiePacketDecodedVertexKind.Fat),
            SourceNormalMissingPrimaryAddressVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex is null
                    && vertex.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress),
            SourceNormalMissingSecondaryAddressVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex is null
                    && vertex.MappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress),
            SuppressedGeneratedNormalFallbackVertexCount = geometry.SuppressedGeneratedNormalFallbackVertexCount,
            DecodedVertexNormalCount = tie.VertexNormals.Count,
            DecodedVertexNormalRemapCount = tie.VertexNormalRemaps.Count,
            PacketRgbaSlotCount = packetRgbaSlotCount,
            VertexColor0AccessorCount = 0,
            AmbientWordCount = ambientIndexResult.AmbientWordCount,
            AmbientSlotCount = ambientIndexResult.AmbientSlotCount,
            AmbientNormalIndexOffset = ambientIndexResult.NormalIndexOffset,
            AmbientIndexTargetMode = ambientIndexResult.TargetMode,
            AmbientIndexAccessorCount = ambientIndexAccessor.HasValue ? geometry.AmbientIndices.Count : 0,
            ResolvedAmbientIndexVertexCount = ambientIndexResult.ResolvedVertexCount,
            ResolvedAmbientIndexIndexCount = ambientIndexResult.ResolvedIndexCount,
            AmbientIndexFallbackResolvedVertexCount = ambientIndexResult.FallbackResolvedVertexCount,
            AmbientIndexOutOfRangeVertexCount = ambientIndexResult.OutOfRangeVertexCount,
            AmbientColorRecipeCount = ambientIndexResult.ColorRecipes.Count,
            AmbientColorAverage2RecipeCount = ambientIndexResult.ColorRecipes.Count(
                recipe => string.Equals(recipe.Kind, TieRgbaRemapOperationKind.Average2.ToString(), StringComparison.Ordinal)),
            GlowRgba = FormatRgba(tie.Header.GlowRgba),
            GlowRgbaColor = BuildRgbaDiagnostic(TieRgba32.FromRaw(tie.Header.GlowRgba)),
            DecodedGlowRgbaRemapCount = tie.GlowRgbaRemaps.Count,
            ResolvedGlowRgbaVertexCount = glowColorResult.ResolvedVertexCount,
            GlowRgbaColorAccessorCount = 0,
            GlowRgbaCustomAttributeCount = glowEmissionAccessor.HasValue ? geometry.GlowColors.Count : 0,
            GlowRgbaEmissionVertexCount = TieGltfGlowBuilder.CountActiveVertices(geometry.GlowColors),
            GlowRgbaEmissionStrength = glowEmission.Strength,
            GlowRgbaEmissivePrimitiveCount = geometry.PacketIndexGroups.Count(group => group.UseGlowEmission),
            TextureCoordinateCount = geometry.TexCoords.Count,
            MultipassTextureCoordinateCount = geometry.MultipassTexCoords.Count,
            PrimitiveCount = primitives.Count,
            TexturedPrimitiveCount = primitives.Count(primitive => (int)primitive["material"] != 0),
            AlphaTextureCount = externalTextureAlpha?.Count(pair => pair.Value.HasAlpha) ?? 0,
            AlphaTexturedMaterialCount = materialBuilder.Diagnostics.Count(material => material.TextureHasAlpha),
            Textures = BuildTextureDiagnostics(
                externalTextureUris,
                externalTextureSizes,
                externalTextureAlpha),
            Materials = materialBuilder.Diagnostics,
            Shaders = BuildShaderDiagnostics(tie.Shaders),
            PacketTables = BuildPacketTableDiagnostics(tie),
            Packets = sourcePacketIndexGroups.Select(group => new
            {
                group.PacketIndex,
                group.ShaderIndex,
                group.MultipassOffset,
                MultipassType = group.PassFlags,
                group.PassFlags,
                TiePassFlags = group.PassFlags,
                TiePassFlagsBits = TiePassFlags.FormatByteBits(group.PassFlags),
                TieSecondPassMode = TiePassFlags.ResolveSecondPassMode(group.PassFlags),
                TieTextureMatrixEnabled = TiePassFlags.UsesTextureMatrix(group.PassFlags),
                TieTextureMatrixSelector = TiePassFlags.TextureMatrixSelector(group.PassFlags),
                TieEnvironmentPassBits = TiePassFlags.EnvironmentPassBits(group.PassFlags),
                group.MultipassUvSize,
                TieMultipassUvRole = TiePassFlags.ResolveMultipassUvRole(
                    group.PassFlags,
                    group.MultipassUvSize),
                TieReflectiveBleedColor = group.EnvPassBleedColor?.ToRgbaHex(),
                group.PacketShaderIndices,
                group.PacketShaderSwitchVuAddresses,
                HeaderModeBits = FormatModeBits(tie.Header.ModeBits),
                GlowRgbaIndexCount = TieGltfGlowBuilder.CountActiveIndices(group.Indices, geometry.GlowColors),
                GlowRgbaUsesEmission = group.UseGlowEmission,
                GlowRgbaEmissionStrength = group.UseGlowEmission ? glowEmission.Strength : 0f,
                TriangleCount = group.Indices.Count / 3
            }).ToArray(),
            SourceNormalPhaseVotes = sourceNormalPhaseAnalysis.Strips.Select(strip => new
            {
                strip.LodIndex,
                strip.StripIndex,
                strip.PacketIndex,
                strip.PacketStripIndex,
                strip.ShaderIndex,
                strip.TriangleCount,
                strip.UsesPreviousStripReferencePhase,
                strip.FirstToken,
                strip.PacketDinkyUploadRemappedVertexCount,
                strip.LogicalRemappedVertexCount,
                strip.PacketRowRemappedVertexCount,
                strip.PacketDinkyUploadNormalRemapChunks,
                strip.LogicalNormalRemapChunks,
                strip.PacketRowNormalRemapChunks,
                strip.UsedNormalRemapChunks,
                strip.DominantUsedNormalRemapChunkIndex,
                strip.DominantUsedNormalRemapChunkRemapCount,
                strip.SelectedTargetMode,
                strip.TargetModeVotes,
                strip.WindingRepairTriangleCount,
                strip.WindingRepairTriangleIndices,
                strip.TriangleVotes,
                strip.ScoredTriangleCount,
                strip.BestCurrentLayout,
                strip.BestCurrentStrongTriangleCount,
                strip.BestCurrentAverageDot,
                strip.BestInvertedLayout,
                strip.BestInvertedStrongTriangleCount,
                strip.BestInvertedAverageDot,
                strip.UsesDenseSourceNormalRepair,
                strip.UsesSmallStripSourceNormalRepair,
                strip.UsesMixedTriangleSourceNormalRepair,
                strip.ShouldApplyWindingRepair,
                PhaseVote = strip.PhaseVote.ToString()
            }).ToArray(),
            GlowRgbaRemaps = tie.GlowRgbaRemaps.Select(remap => new
            {
                remap.RemapIndex,
                Offset = FormatOffset(remap.Offset),
                ResolvedStartOffset = remap.ResolvedStartOffset.HasValue ? FormatOffset(remap.ResolvedStartOffset.Value) : null,
                EndOffset = remap.EndOffset.HasValue ? FormatOffset(remap.EndOffset.Value) : null,
                Rgba = BuildRgbaDiagnostic(remap.Rgba),
                ResolutionKind = remap.ResolutionKind.ToString(),
                remap.LodIndex,
                remap.PacketIndex,
                remap.ResolvedPacketIndex,
                remap.ResolvedPacketIndices,
                remap.ResolvedShaderIndex,
                remap.StartVertexRowIndex,
                remap.EndVertexRowIndexExclusive,
                remap.ResolvedPacketCount,
                remap.ResolvedVertexRowCount,
                remap.ResolvedLogicalVertexCount
            }).ToArray(),
            GlowRgbaVertices = tie.GlowRgbaVertices
                .Where(vertex => vertex.LodIndex == topology.LodIndex)
                .Select(vertex => new
                {
                    vertex.RemapIndex,
                    RemapOffset = FormatOffset(vertex.RemapOffset),
                    vertex.PacketIndex,
                    vertex.StripIndex,
                    vertex.PacketStripIndex,
                    vertex.IndexInStrip,
                    vertex.LogicalVertexIndex,
                    vertex.VertexRowIndex,
                    VertexRowOffset = FormatOffset(vertex.VertexRowOffset),
                    Rgba = BuildRgbaDiagnostic(vertex.Rgba)
                })
                .ToArray()
        }, jsonOptions)
            : [];

        return new TieGltfExport(gltfBytes, binBytes, diagnosticsBytes);
    }

    private static object BuildRgbaDiagnostic(TieRgba32 rgba)
    {
        return new
        {
            Hex = rgba.ToRgbaHex(),
            rgba.R,
            rgba.G,
            rgba.B,
            rgba.A,
            Gltf = new[]
            {
                Ps2Color.NormalizeByteComponent(rgba.R),
                Ps2Color.NormalizeByteComponent(rgba.G),
                Ps2Color.NormalizeByteComponent(rgba.B),
                Ps2Color.NormalizeOpacityAlpha(rgba.A)
            }
        };
    }

    private static int CountSourceNormalState(GltfGeometry geometry, TieGltfSourceNormalState state)
    {
        return geometry.SourceNormalStates.Count(value => MathF.Abs(value - (float)state) < 0.5f);
    }

    private static int CountSourceNormalState(
        IReadOnlyList<TieGltfSourceNormalState> states,
        TieGltfSourceNormalState state)
    {
        return states.Count(value => value == state);
    }

    private static int CountSourceNormalStateVertices(
        TieLodTopology topology,
        IReadOnlyList<TieGltfSourceNormalState> states,
        TieGltfSourceNormalState state,
        Func<TieLogicalVertex, bool> predicate)
    {
        return topology.LogicalVertices.Count(vertex =>
            vertex.LogicalVertexIndex >= 0
            && vertex.LogicalVertexIndex < states.Count
            && states[vertex.LogicalVertexIndex] == state
            && predicate(vertex));
    }

    private static string FormatRgba(int rawRgba)
    {
        return $"0x{unchecked((uint)rawRgba):X8}";
    }

    private static string FormatOffset(int offset)
    {
        return $"0x{offset:X}";
    }

    private static string FormatOffset(uint offset)
    {
        return offset == 0 ? "none" : $"0x{offset:X}";
    }

    private static string FormatModeBits(short modeBits)
    {
        return $"0x{(ushort)modeBits:X4}";
    }

    private static string FormatWord(int value)
    {
        return $"0x{unchecked((uint)value):X8}";
    }

    private static string FormatByte(byte value)
    {
        return $"0x{value:X2}";
    }

    private static string FormatBytes(IReadOnlyList<byte> bytes)
    {
        return Convert.ToHexString(bytes.ToArray());
    }

    private static object[] BuildTextureDiagnostics(
        IReadOnlyDictionary<int, string>? textureUris,
        IReadOnlyDictionary<int, TextureSize>? textureSizes,
        IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha)
    {
        if (textureUris is null || textureUris.Count == 0)
        {
            return [];
        }

        return textureUris
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var size = textureSizes is not null && textureSizes.TryGetValue(pair.Key, out var resolvedSize)
                    ? resolvedSize
                    : default(TextureSize?);
                var alpha = textureAlpha is not null && textureAlpha.TryGetValue(pair.Key, out var resolvedAlpha)
                    ? resolvedAlpha
                    : TextureAlphaInfo.Opaque;

                return new
                {
                    ShaderIndex = pair.Key,
                    Uri = pair.Value,
                    Width = size?.Width,
                    Height = size?.Height,
                    HasAlpha = alpha.HasAlpha,
                    AlphaMode = alpha.AlphaMode.ToString(),
                    GltfAlphaMode = alpha.GltfAlphaMode,
                    MinAlpha = alpha.MinAlpha,
                    MaxAlpha = alpha.MaxAlpha,
                    UsesBinaryAlpha = alpha.UsesBinaryAlpha
                };
            })
            .ToArray();
    }

    private static object[] BuildShaderDiagnostics(IReadOnlyList<TieShader> shaders)
    {
        return shaders
            .Select(shader => new
            {
                shader.Index,
                Offset = FormatOffset(shader.Offset),
                shader.ClampU,
                shader.ClampV,
                Bytes = FormatBytes(shader.Bytes),
                Words = Enumerable.Range(0, shader.Bytes.Length / sizeof(int))
                    .Select(wordIndex => new
                    {
                        Index = wordIndex,
                        Offset = FormatOffset(shader.Offset + wordIndex * sizeof(int)),
                        Raw = FormatWord(BitConverter.ToInt32(shader.Bytes, wordIndex * sizeof(int)))
                    })
                    .ToArray()
            })
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildPacketTableDiagnostics(TieClass tie)
    {
        var blocksByKey = tie.PacketDataBlocks.ToDictionary(block => (block.LodIndex, block.PacketIndex));
        var topologiesByLod = tie.LodTopologies.ToDictionary(topology => topology.LodIndex);
        return tie.PacketTables
            .Select(table => new
            {
                table.LodIndex,
                Offset = FormatOffset(table.Offset),
                table.Count,
                PacketCount = table.Packets.Count,
                Packets = table.Packets.Select(packet =>
                {
                    blocksByKey.TryGetValue((packet.LodIndex, packet.PacketIndex), out var block);
                    topologiesByLod.TryGetValue(packet.LodIndex, out var topology);
                    var logicalVertices = topology is null
                        ? Array.Empty<TieLogicalVertex>()
                        : topology.LogicalVertices
                            .Where(vertex => vertex.PacketIndex == packet.PacketIndex)
                            .ToArray();
                    var strips = topology is null
                        ? Array.Empty<TieTriangleStrip>()
                        : topology.Strips
                            .Where(strip => strip.PacketIndex == packet.PacketIndex)
                            .ToArray();

                    return new
                    {
                        packet.PacketIndex,
                        DataOffset = FormatOffset(packet.DataOffset),
                        AbsoluteDataOffset = FormatOffset(packet.AbsoluteDataOffset),
                        packet.ShaderCount,
                        packet.BfcDistance,
                        packet.ControlCount,
                        packet.ControlSize,
                        packet.VertexOffset,
                        packet.VertexSize,
                        packet.RgbaCount,
                        packet.MultipassOffset,
                        packet.ScissorOffset,
                        packet.ScissorSize,
                        MultipassType = packet.PassFlags,
                        packet.PassFlags,
                        TiePassFlags = packet.PassFlags,
                        TiePassFlagsBits = TiePassFlags.FormatByteBits(packet.PassFlags),
                        TieSecondPassMode = TiePassFlags.ResolveSecondPassMode(packet.PassFlags),
                        TieTextureMatrixEnabled = TiePassFlags.UsesTextureMatrix(packet.PassFlags),
                        TieTextureMatrixSelector = TiePassFlags.TextureMatrixSelector(packet.PassFlags),
                        TieEnvironmentPassBits = TiePassFlags.EnvironmentPassBits(packet.PassFlags),
                        packet.MultipassUvSize,
                        TieMultipassUvRole = TiePassFlags.ResolveMultipassUvRole(
                            packet.PassFlags,
                            packet.MultipassUvSize),
                        ShaderSwitchVuAddresses = packet.ShaderSwitchVuAddresses,
                        ShaderReferences = packet.ShaderReferences.Select(reference => new
                        {
                            reference.Index,
                            ShaderByteOffset = FormatWord(reference.ShaderByteOffset),
                            reference.ShaderIndex
                        }).ToArray(),
                        SetupRows = block is null
                            ? Array.Empty<object>()
                            : block.SetupRows.Select(BuildSetupRowDiagnostic).ToArray(),
                        StripControls = block is null
                            ? Array.Empty<object>()
                            : block.StripControls.Select(BuildStripControlDiagnostic).ToArray(),
                        Primitives = block is null
                            ? Array.Empty<object>()
                            : block.Primitives
                                .Select((primitive, primitiveIndex) => BuildPrimitiveDiagnostic(
                                    tie,
                                    block,
                                    primitive,
                                    primitiveIndex > 0 ? block.Primitives[primitiveIndex - 1] : null,
                                    primitive.Index >= 0
                                        && primitive.Index < block.TokenReferencePrimitives.Count
                                            ? block.TokenReferencePrimitives[primitive.Index]
                                            : null,
                                    strips.FirstOrDefault(strip =>
                                        strip.PacketStripIndex == primitive.PacketStripIndex),
                                    includeTriangleDiagnostics: true))
                                .ToArray(),
                        PhysicalPrimitives = block is null
                            ? Array.Empty<object>()
                            : block.PhysicalPrimitives
                                .Select(primitive => BuildPrimitiveDiagnostic(
                                    tie,
                                    block,
                                    primitive,
                                    null,
                                    null,
                                    null,
                                    includeTriangleDiagnostics: false))
                                .ToArray(),
                        TokenReferencePrimitives = block is null
                            ? Array.Empty<object>()
                            : block.TokenReferencePrimitives
                                .Select(primitive => BuildPrimitiveDiagnostic(
                                    tie,
                                    block,
                                    primitive,
                                    null,
                                    null,
                                    null,
                                    includeTriangleDiagnostics: false))
                                .ToArray(),
                        Regions = block is null
                            ? Array.Empty<object>()
                            : block.Regions.Select(region => new
                            {
                                region.Name,
                                region.QwordOffset,
                                region.QwordCount,
                                Offset = FormatOffset(region.Offset),
                                region.Length
                            }).Cast<object>().ToArray(),
                        Counts = new
                        {
                            Qwords = block?.QwordCount ?? 0,
                            SetupRows = block?.SetupRows.Count ?? 0,
                            ControlRows = block?.ControlRows.Count ?? 0,
                            StripControls = block?.StripControls.Count ?? 0,
                            StripTokens = block?.StripTokens.Count ?? 0,
                            DecodedStripTokenAddressMismatches = block?.StripTokens.Count(token => !token.MatchesExpectedGsPacketWriteOffset) ?? 0,
                            ScissorTokens = block?.ScissorTokens.Count ?? 0,
                            VertexRows = block?.VertexRows.Count ?? 0,
                            DecodedVertices = block?.DecodedVertices.Count ?? 0,
                            Primitives = block?.Primitives.Count ?? 0,
                            PhysicalPrimitives = block?.PhysicalPrimitives.Count ?? 0,
                            TokenReferencePrimitives = block?.TokenReferencePrimitives.Count ?? 0,
                            PhysicalTokenReferenceDivergentPrimitives = block is null
                                ? 0
                                : CountDivergentPrimitiveSequences(block),
                            LogicalVertices = logicalVertices.Length,
                            Strips = strips.Length,
                            Triangles = strips.Sum(strip => strip.TriangleCount)
                        },
                        Consistency = new
                        {
                            HasPacketDataBlock = block is not null,
                            ShaderReferenceCountMatches = packet.ShaderReferences.Count == packet.ShaderCount,
                            ShaderSwitchCountMatches = packet.ShaderSwitchVuAddresses.Count == Math.Max(0, packet.ShaderCount - 1),
                            SetupRowCountMatches = block is not null && block.SetupRows.Count == 2,
                            SetupShaderReferenceWordsMatch = block is not null && SetupShaderReferenceWordsMatch(packet, block),
                            SetupShaderSwitchWordsMatch = block is not null && SetupShaderSwitchWordsMatch(packet, block),
                            ControlRowsMatchPacketCount = block is not null && block.ControlRows.Count == packet.ControlCount,
                            VertexRowsMatchPacketSize = block is not null && block.VertexRows.Count == packet.VertexSize,
                            StripControlsMatchControlRows = block is not null
                                && block.StripControls.Count == Math.Max(0, packet.ControlCount - 3),
                            DecodedPrimitivesMatchStripControls = block is not null
                                && block.Primitives.Count == block.StripControls.Count,
                            DecodedPhysicalPrimitivesMatchStripControls = block is not null
                                && block.PhysicalPrimitives.Count == block.StripControls.Count,
                            DecodedTokenReferencePrimitivesMatchStripControls = block is not null
                                && block.TokenReferencePrimitives.Count == block.StripControls.Count,
                            DecodedPrimitiveVertexCountsMatch = block is not null
                                && block.Primitives.All(primitive =>
                                    primitive.PacketStripIndex >= 0
                                    && primitive.PacketStripIndex < block.StripControls.Count
                                    && primitive.Vertices.Count == block.StripControls[primitive.PacketStripIndex].TokenCount),
                            DecodedStripTokensMatchRawTokens = block is not null
                                && block.StripControls.All(strip => strip.DecodedTokens.Count == strip.Tokens.Length),
                            ScissorEndTokenPresent = block is not null
                                && (block.ScissorTokens.Count == 0 || block.ScissorTokens.Any(token => token.IsEndToken))
                        }
                    };
                }).ToArray()
            })
            .Cast<object>()
            .ToArray();
    }

    private static int CountDivergentPrimitiveSequences(TiePacketDataBlock block)
    {
        var divergentCount = Math.Abs(block.PhysicalPrimitives.Count - block.TokenReferencePrimitives.Count);
        var count = Math.Min(block.PhysicalPrimitives.Count, block.TokenReferencePrimitives.Count);
        for (var i = 0; i < count; i++)
        {
            if (!PrimitiveVertexReferencesMatch(block.PhysicalPrimitives[i], block.TokenReferencePrimitives[i]))
            {
                divergentCount++;
            }
        }

        return divergentCount;
    }

    private static bool PrimitiveVertexReferencesMatch(
        TiePacketPrimitive left,
        TiePacketPrimitive right)
    {
        if (left.PacketStripIndex != right.PacketStripIndex
            || left.Vertices.Count != right.Vertices.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Vertices.Count; i++)
        {
            var leftVertex = left.Vertices[i];
            var rightVertex = right.Vertices[i];
            if (leftVertex.GsPacketWriteOffset != rightVertex.GsPacketWriteOffset
                || leftVertex.Vertex.Index != rightVertex.Vertex.Index
                || leftVertex.IsSecondaryWriteOffset != rightVertex.IsSecondaryWriteOffset)
            {
                return false;
            }
        }

        return true;
    }

    private static object BuildSetupRowDiagnostic(TiePacketSetupRow row)
    {
        return new
        {
            row.Index,
            Offset = FormatOffset(row.Offset),
            Bytes = FormatBytes(row.Bytes),
            Words = row.Words.Select(word => new
            {
                word.RowIndex,
                word.WordIndex,
                Offset = FormatOffset(word.Offset),
                Raw = FormatWord(word.Raw),
                Role = word.Role.ToString()
            }).ToArray()
        };
    }

    private static object BuildStripControlDiagnostic(TiePacketStripControl strip)
    {
        return new
        {
            strip.Index,
            strip.ControlRowIndex,
            Offset = FormatOffset(strip.Offset),
            strip.TokenCount,
            ActualTokenCount = strip.Tokens.Length,
            strip.TokenOffset,
            VuAddress = FormatByte(strip.VuAddress),
            ControlData1 = FormatByte(strip.ControlData1),
            Flags = FormatByte(strip.Flags),
            Tokens = FormatBytes(strip.Tokens),
            DecodedTokenAddressMismatchCount = strip.DecodedTokens.Count(token => !token.MatchesExpectedGsPacketWriteOffset),
            FirstToken = strip.DecodedTokens.Count == 0
                ? null
                : BuildStripTokenDiagnostic(strip.DecodedTokens[0]),
            DecodedTokens = strip.DecodedTokens.Select(BuildStripTokenDiagnostic).ToArray()
        };
    }

    private static object BuildPrimitiveDiagnostic(
        TieClass tie,
        TiePacketDataBlock block,
        TiePacketPrimitive primitive,
        TiePacketPrimitive? previousPrimitive,
        TiePacketPrimitive? tokenReferencePrimitive,
        TieTriangleStrip? topologyStrip,
        bool includeTriangleDiagnostics)
    {
        var stripControl = primitive.PacketStripIndex >= 0 && primitive.PacketStripIndex < block.StripControls.Count
            ? block.StripControls[primitive.PacketStripIndex]
            : null;
        var firstToken = stripControl?.DecodedTokens.FirstOrDefault();
        var tokenReferenceDiverges = tokenReferencePrimitive is not null
            && tokenReferencePrimitive.PacketStripIndex == primitive.PacketStripIndex
            && !PrimitiveVertexReferencesMatch(primitive, tokenReferencePrimitive);
        var topologyUsesPreviousStripReferencePhase = topologyStrip?.UsesPreviousStripReferencePhase ?? false;
        var appliedStartPhaseFlip = primitive.WindingOrder ^ topologyUsesPreviousStripReferencePhase;
        return new
        {
            primitive.Index,
            primitive.PacketStripIndex,
            primitive.MaterialIndex,
            primitive.WindingOrder,
            PreviousPrimitiveMaterialIndex = previousPrimitive?.MaterialIndex,
            PreviousPrimitiveSameMaterial = previousPrimitive is not null
                && primitive.MaterialIndex == previousPrimitive.MaterialIndex,
            PreviousPrimitiveLastGsPacketWriteOffset = previousPrimitive is null || previousPrimitive.Vertices.Count == 0
                ? null
                : FormatOffset(previousPrimitive.Vertices[^1].GsPacketWriteOffset),
            TopologyStripIndex = topologyStrip?.StripIndex,
            TopologyUsesPreviousStripReferencePhase = topologyStrip?.UsesPreviousStripReferencePhase,
            TopologyUsesTokenReferenceTriangleSequence = topologyStrip?.UsesTokenReferenceTriangleSequence,
            AppliedStartPhaseFlip = appliedStartPhaseFlip,
            StripTokenCount = stripControl?.TokenCount,
            StripVuAddress = stripControl is null ? null : FormatByte(stripControl.VuAddress),
            StripFlags = stripControl is null ? null : FormatByte(stripControl.Flags),
            StripTokens = stripControl is null ? null : FormatBytes(stripControl.Tokens),
            FirstToken = firstToken is null ? null : BuildStripTokenDiagnostic(firstToken),
            VertexCount = primitive.Vertices.Count,
            FirstGsPacketWriteOffset = primitive.Vertices.Count == 0
                ? null
                : FormatOffset(primitive.Vertices[0].GsPacketWriteOffset),
            LastGsPacketWriteOffset = primitive.Vertices.Count == 0
                ? null
                : FormatOffset(primitive.Vertices[^1].GsPacketWriteOffset),
            FirstVertexIsSecondaryWriteOffset = primitive.Vertices.Count > 0
                && primitive.Vertices[0].IsSecondaryWriteOffset,
            SecondaryWriteOffsetIndices = primitive.Vertices
                .Select((reference, index) => (reference, index))
                .Where(item => item.reference.IsSecondaryWriteOffset)
                .Select(item => item.index)
                .ToArray(),
            VertexKindSignature = string.Join(
                ",",
                primitive.Vertices
                    .Select(reference => reference.Vertex.Kind.ToString())
                    .Distinct()),
            TokenReferenceDivergesFromPhysical = tokenReferenceDiverges,
            TokenReferenceVertexCount = tokenReferencePrimitive?.Vertices.Count,
            TokenReferenceFirstGsPacketWriteOffset = tokenReferencePrimitive is null || tokenReferencePrimitive.Vertices.Count == 0
                ? null
                : FormatOffset(tokenReferencePrimitive.Vertices[0].GsPacketWriteOffset),
            TokenReferenceLastGsPacketWriteOffset = tokenReferencePrimitive is null || tokenReferencePrimitive.Vertices.Count == 0
                ? null
                : FormatOffset(tokenReferencePrimitive.Vertices[^1].GsPacketWriteOffset),
            TokenReferenceFirstVertexIsSecondaryWriteOffset = tokenReferencePrimitive is not null
                && tokenReferencePrimitive.Vertices.Count > 0
                && tokenReferencePrimitive.Vertices[0].IsSecondaryWriteOffset,
            VertexReferences = primitive.Vertices
                .Select(BuildPrimitiveVertexReferenceDiagnostic)
                .ToArray(),
            StripTriangles = includeTriangleDiagnostics
                ? BuildPrimitiveTriangleDiagnostics(
                    tie,
                    stripControl,
                    primitive,
                    appliedStartPhaseFlip)
                : Array.Empty<object>(),
            TriangleSequenceCandidates = includeTriangleDiagnostics
                ? BuildTriangleSequenceCandidateDiagnostics(tie, primitive, tokenReferencePrimitive)
                : Array.Empty<object>()
        };
    }

    private static object BuildPrimitiveVertexReferenceDiagnostic(
        TiePacketVertexReference reference,
        int indexInPrimitive)
    {
        var vertex = reference.Vertex;
        var sourceRow = vertex.SourceRow;
        return new
        {
            IndexInPrimitive = indexInPrimitive,
            reference.Index,
            GsPacketWriteOffset = FormatOffset(reference.GsPacketWriteOffset),
            reference.IsSecondaryWriteOffset,
            DecodedVertexIndex = vertex.Index,
            Kind = vertex.Kind.ToString(),
            SourceRecordOffset = FormatOffset(vertex.Offset),
            SourceRecordBytes = FormatBytes(vertex.Bytes),
            SourceRecordHalfwords = BuildHalfwordDiagnostics(vertex.Bytes, vertex.Offset),
            SourceRowIndex = vertex.SourceRowIndex,
            SourceRowOffset = sourceRow is null ? null : FormatOffset(sourceRow.Offset),
            vertex.X,
            vertex.Y,
            vertex.Z,
            VertexGsPacketWriteOffset = FormatOffset(vertex.GsPacketWriteOffset),
            vertex.S,
            vertex.T,
            vertex.Q,
            SecondaryGsPacketWriteOffset = FormatOffset(vertex.SecondaryGsPacketWriteOffset),
            SourceRow = sourceRow is null
                ? null
                : new
                {
                    sourceRow.Index,
                    Offset = FormatOffset(sourceRow.Offset),
                    Kind = sourceRow.Kind.ToString(),
                    sourceRow.X,
                    sourceRow.Y,
                    sourceRow.Z,
                    sourceRow.W,
                    sourceRow.Data0,
                    sourceRow.Data1,
                    sourceRow.Data2,
                    sourceRow.Data3,
                    sourceRow.PrimaryVuAddress,
                    sourceRow.SecondaryVuAddress,
                    sourceRow.HasPrimaryVuAddress,
                    sourceRow.HasSecondaryVuAddress
                }
        };
    }

    private static object[] BuildPrimitiveTriangleDiagnostics(
        TieClass tie,
        TiePacketStripControl? stripControl,
        TiePacketPrimitive primitive,
        bool startPhaseFlip)
    {
        if (primitive.Vertices.Count < 3)
        {
            return [];
        }

        var scale = tie.Header.Scale / 1024f;
        var triangles = new List<object>(primitive.Vertices.Count - 2);
        var flip = startPhaseFlip;
        for (var i = 2; i < primitive.Vertices.Count; i++)
        {
            var windowA = i - 2;
            var windowB = i - 1;
            var windowC = i;
            var a = windowA;
            var b = flip ? windowC : windowB;
            var c = flip ? windowB : windowC;
            var currentNormal = TryBuildFaceNormal(primitive.Vertices[a], primitive.Vertices[b], primitive.Vertices[c], scale, out var normal)
                ? normal
                : default(Vector3?);
            var tokenValues = BuildTriangleTokenValues(stripControl, windowA, windowB, windowC);
            var tokenModes = BuildTriangleTokenModes(stripControl, windowA, windowB, windowC);
            var tokenReferences = BuildTriangleTokenReferences(stripControl, windowA, windowB, windowC);

            triangles.Add(new
            {
                TriangleIndexInStrip = i - 2,
                VertexWindow = new[] { windowA, windowB, windowC },
                WindingFlip = flip,
                Indices = new[] { a, b, c },
                GsPacketWriteOffsets = new[]
                {
                    FormatOffset(primitive.Vertices[a].GsPacketWriteOffset),
                    FormatOffset(primitive.Vertices[b].GsPacketWriteOffset),
                    FormatOffset(primitive.Vertices[c].GsPacketWriteOffset)
                },
                UsesSecondaryWriteOffsets = new[]
                {
                    primitive.Vertices[a].IsSecondaryWriteOffset,
                    primitive.Vertices[b].IsSecondaryWriteOffset,
                    primitive.Vertices[c].IsSecondaryWriteOffset
                },
                TokenValues = tokenValues,
                TokenAddressModes = tokenModes,
                TokenReferencedGsPacketWriteOffsets = tokenReferences,
                Degenerate = !currentNormal.HasValue,
                FaceNormal = currentNormal.HasValue
                    ? new
                    {
                        currentNormal.Value.X,
                        currentNormal.Value.Y,
                        currentNormal.Value.Z
                    }
                    : null,
                FaceNormalY = currentNormal?.Y,
                OppositeStartPhaseFaceNormalY = currentNormal.HasValue ? -currentNormal.Value.Y : default(float?)
            });
            flip = !flip;
        }

        return triangles.ToArray();
    }

    private static object[] BuildTriangleSequenceCandidateDiagnostics(
        TieClass tie,
        TiePacketPrimitive primitive,
        TiePacketPrimitive? tokenReferencePrimitive)
    {
        var scale = tie.Header.Scale / 1024f;
        var candidates = new List<object>
        {
            BuildTriangleSequenceCandidateDiagnostic(
                "physical-current",
                primitive.Vertices,
                primitive.WindingOrder,
                firstTriangleIndex: 2,
                advanceSuppressedTriangleParity: false,
                scale),
            BuildTriangleSequenceCandidateDiagnostic(
                "physical-opposite",
                primitive.Vertices,
                !primitive.WindingOrder,
                firstTriangleIndex: 2,
                advanceSuppressedTriangleParity: false,
                scale)
        };

        if (tokenReferencePrimitive is not null
            && tokenReferencePrimitive.PacketStripIndex == primitive.PacketStripIndex
            && tokenReferencePrimitive.Vertices.Count == primitive.Vertices.Count
            && !PrimitiveVertexReferencesMatch(primitive, tokenReferencePrimitive))
        {
            candidates.Add(BuildTriangleSequenceCandidateDiagnostic(
                "token-reference-current",
                tokenReferencePrimitive.Vertices,
                primitive.WindingOrder,
                firstTriangleIndex: 2,
                advanceSuppressedTriangleParity: false,
                scale));
            candidates.Add(BuildTriangleSequenceCandidateDiagnostic(
                "token-reference-opposite",
                tokenReferencePrimitive.Vertices,
                !primitive.WindingOrder,
                firstTriangleIndex: 2,
                advanceSuppressedTriangleParity: false,
                scale));
            candidates.Add(BuildTriangleSequenceCandidateDiagnostic(
                "token-reference-suppress-first-advance-parity",
                tokenReferencePrimitive.Vertices,
                primitive.WindingOrder,
                firstTriangleIndex: 3,
                advanceSuppressedTriangleParity: true,
                scale));
            candidates.Add(BuildTriangleSequenceCandidateDiagnostic(
                "token-reference-suppress-first-keep-parity",
                tokenReferencePrimitive.Vertices,
                primitive.WindingOrder,
                firstTriangleIndex: 3,
                advanceSuppressedTriangleParity: false,
                scale));
        }

        return candidates.ToArray();
    }

    private static object BuildTriangleSequenceCandidateDiagnostic(
        string name,
        IReadOnlyList<TiePacketVertexReference> references,
        bool startPhaseFlip,
        int firstTriangleIndex,
        bool advanceSuppressedTriangleParity,
        float scale)
    {
        var emittedTriangleCount = 0;
        var suppressedTriangleCount = 0;
        var degenerateTriangleCount = 0;
        var strongUpYTriangleCount = 0;
        var strongDownYTriangleCount = 0;
        var sideTriangleCount = 0;
        var longEdgeTriangleCount = 0;
        var maxEdgeLength = 0f;
        float? firstEmittedFaceNormalY = null;
        string[]? firstEmittedGsPacketWriteOffsets = null;
        var flip = startPhaseFlip;

        for (var i = 2; i < references.Count; i++)
        {
            var suppress = i < firstTriangleIndex;
            if (suppress)
            {
                suppressedTriangleCount++;
                if (advanceSuppressedTriangleParity)
                {
                    flip = !flip;
                }

                continue;
            }

            var a = i - 2;
            var b = flip ? i : i - 1;
            var c = flip ? i - 1 : i;
            emittedTriangleCount++;
            if (!TryBuildFaceNormal(references[a], references[b], references[c], scale, out var normal))
            {
                degenerateTriangleCount++;
            }
            else if (normal.Y > 0.75f)
            {
                strongUpYTriangleCount++;
            }
            else if (normal.Y < -0.75f)
            {
                strongDownYTriangleCount++;
            }
            else
            {
                sideTriangleCount++;
            }

            var maxTriangleEdge = MaxTriangleEdgeLength(references[a], references[b], references[c], scale);
            maxEdgeLength = MathF.Max(maxEdgeLength, maxTriangleEdge);
            if (maxTriangleEdge > 8f)
            {
                longEdgeTriangleCount++;
            }

            if (firstEmittedGsPacketWriteOffsets is null)
            {
                firstEmittedFaceNormalY = degenerateTriangleCount == emittedTriangleCount
                    ? null
                    : normal.Y;
                firstEmittedGsPacketWriteOffsets =
                [
                    FormatOffset(references[a].GsPacketWriteOffset),
                    FormatOffset(references[b].GsPacketWriteOffset),
                    FormatOffset(references[c].GsPacketWriteOffset)
                ];
            }

            flip = !flip;
        }

        return new
        {
            Name = name,
            VertexCount = references.Count,
            StartPhaseFlip = startPhaseFlip,
            FirstTriangleIndex = firstTriangleIndex - 2,
            advanceSuppressedTriangleParity,
            SuppressedTriangleCount = suppressedTriangleCount,
            TriangleCount = emittedTriangleCount,
            DegenerateTriangleCount = degenerateTriangleCount,
            StrongUpYTriangleCount = strongUpYTriangleCount,
            StrongDownYTriangleCount = strongDownYTriangleCount,
            SideTriangleCount = sideTriangleCount,
            LongEdgeTriangleCount = longEdgeTriangleCount,
            MaxEdgeLength = maxEdgeLength,
            FirstEmittedFaceNormalY = firstEmittedFaceNormalY,
            FirstEmittedGsPacketWriteOffsets = firstEmittedGsPacketWriteOffsets ?? []
        };
    }

    private static float MaxTriangleEdgeLength(
        TiePacketVertexReference a,
        TiePacketVertexReference b,
        TiePacketVertexReference c,
        float scale)
    {
        var aPosition = GltfCoordinateBasis.FromPs2Position(a.Vertex.X, a.Vertex.Y, a.Vertex.Z, scale);
        var bPosition = GltfCoordinateBasis.FromPs2Position(b.Vertex.X, b.Vertex.Y, b.Vertex.Z, scale);
        var cPosition = GltfCoordinateBasis.FromPs2Position(c.Vertex.X, c.Vertex.Y, c.Vertex.Z, scale);
        return MathF.Max(
            Vector3.Distance(aPosition, bPosition),
            MathF.Max(
                Vector3.Distance(bPosition, cPosition),
                Vector3.Distance(cPosition, aPosition)));
    }

    private static string?[] BuildTriangleTokenValues(
        TiePacketStripControl? stripControl,
        int a,
        int b,
        int c)
    {
        if (stripControl is null)
        {
            return [];
        }

        return new[] { a, b, c }
            .Select(index => index >= 0 && index < stripControl.Tokens.Length
                ? FormatByte(stripControl.Tokens[index])
                : null)
            .ToArray();
    }

    private static string?[] BuildTriangleTokenModes(
        TiePacketStripControl? stripControl,
        int a,
        int b,
        int c)
    {
        if (stripControl is null)
        {
            return [];
        }

        return new[] { a, b, c }
            .Select(index => index >= 0 && index < stripControl.DecodedTokens.Count
                ? stripControl.DecodedTokens[index].AddressMode.ToString()
                : null)
            .ToArray();
    }

    private static string?[] BuildTriangleTokenReferences(
        TiePacketStripControl? stripControl,
        int a,
        int b,
        int c)
    {
        if (stripControl is null)
        {
            return [];
        }

        return new[] { a, b, c }
            .Select(index =>
            {
                var token = index >= 0 && index < stripControl.DecodedTokens.Count
                    ? stripControl.DecodedTokens[index]
                    : null;
                return token?.ReferencedGsPacketWriteOffset.HasValue == true
                    ? FormatOffset(token.ReferencedGsPacketWriteOffset.Value)
                    : null;
            })
            .ToArray();
    }

    private static object[] BuildHalfwordDiagnostics(IReadOnlyList<byte> bytes, int baseOffset)
    {
        var count = bytes.Count / 2;
        var halfwords = new object[count];
        for (var i = 0; i < count; i++)
        {
            var value = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
            halfwords[i] = new
            {
                Index = i,
                Offset = FormatOffset(baseOffset + i * 2),
                U16 = $"0x{value:X4}",
                I16 = unchecked((short)value)
            };
        }

        return halfwords;
    }

    private static bool TryBuildFaceNormal(
        TiePacketVertexReference a,
        TiePacketVertexReference b,
        TiePacketVertexReference c,
        float scale,
        out Vector3 normal)
    {
        var aPosition = GltfCoordinateBasis.FromPs2Position(a.Vertex.X, a.Vertex.Y, a.Vertex.Z, scale);
        var bPosition = GltfCoordinateBasis.FromPs2Position(b.Vertex.X, b.Vertex.Y, b.Vertex.Z, scale);
        var cPosition = GltfCoordinateBasis.FromPs2Position(c.Vertex.X, c.Vertex.Y, c.Vertex.Z, scale);
        var cross = Vector3.Cross(bPosition - aPosition, cPosition - aPosition);
        var lengthSquared = cross.LengthSquared();
        if (lengthSquared <= 1e-12f)
        {
            normal = default;
            return false;
        }

        normal = Vector3.Normalize(cross);
        return true;
    }

    private static object BuildStripTokenDiagnostic(TiePacketStripToken token)
    {
        return new
        {
            token.Index,
            Offset = FormatOffset(token.Offset),
            token.StripIndex,
            token.IndexInStrip,
            Value = FormatByte(token.Value),
            token.SignedValue,
            AddressMode = token.AddressMode.ToString(),
            ResolvedGsPacketWriteOffset = token.ResolvedGsPacketWriteOffset.HasValue
                ? FormatOffset(token.ResolvedGsPacketWriteOffset.Value)
                : null,
            ReferencedGsPacketWriteOffset = token.ReferencedGsPacketWriteOffset.HasValue
                ? FormatOffset(token.ReferencedGsPacketWriteOffset.Value)
                : null,
            ExpectedGsPacketWriteOffset = FormatOffset(token.ExpectedGsPacketWriteOffset),
            token.MatchesExpectedGsPacketWriteOffset,
            token.ReferencesPreviousStripVertex,
            token.RestartGap
        };
    }

    private static bool SetupShaderReferenceWordsMatch(TiePacket packet, TiePacketDataBlock block)
    {
        var row = block.SetupRows.FirstOrDefault(row => row.Index == 1);
        return row is not null
            && packet.ShaderReferences.All(reference =>
                reference.Index < row.Words.Count
                && row.Words[reference.Index].Role == TiePacketSetupWordRole.ShaderByteOffset
                && row.Words[reference.Index].Raw == reference.ShaderByteOffset);
    }

    private static bool SetupShaderSwitchWordsMatch(TiePacket packet, TiePacketDataBlock block)
    {
        var row = block.SetupRows.FirstOrDefault(row => row.Index == 0);
        return row is not null
            && packet.ShaderSwitchVuAddresses
                .Select((address, index) => (address, index))
                .All(item =>
                    item.index < row.Words.Count
                    && row.Words[item.index].Role == TiePacketSetupWordRole.ShaderSwitchVuAddress
                    && row.Words[item.index].Raw == item.address);
    }

    private static object? BuildNodeExtras(
        TieClass tie,
        TieLodTopology topology,
        string gameLabel,
        GltfExportMetadataMode metadataMode)
    {
        if (metadataMode == GltfExportMetadataMode.None)
        {
            return null;
        }

        if (metadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                topology.LodIndex
            };
        }

        return new
        {
            source = "tie.bin",
            game = gameLabel,
            oClass = $"0x{(ushort)tie.Header.OClass:X4}",
            topology.LodIndex,
            coordinateBasis = GltfCoordinateBasis.Ps2XzyBasisDescription
        };
    }

    private static object BuildMeshExtras(
        TieClass tie,
        TieLodTopology topology,
        TieGltfNormalBuildResult normalResult,
        TieGltfSourceNormalPhaseAnalysis sourceNormalPhaseAnalysis,
        TieGltfGlowBuildResult glowColorResult,
        TieGltfAmbientBuildResult ambientIndexResult,
        int packetRgbaSlotCount,
        int sourceNormalPhaseWindingRepairStripCount,
        int sourceNormalPhaseWindingRepairTriangleCount,
        TieGltfWindingRepairResult windingRepairResult,
        int suppressedGeneratedNormalFallbackVertexCount,
        int expandedGeneratedNormalFallbackVertexCount,
        int sourceNormalMaskAttributeCount,
        int sourceNormalStateAttributeCount,
        bool usesGlowEmission,
        string gameLabel,
        GltfExportMetadataMode metadataMode)
    {
        if (metadataMode == GltfExportMetadataMode.None)
        {
            return null!;
        }

        if (metadataMode == GltfExportMetadataMode.RuntimeOnly)
        {
            return new
            {
                topology.LodIndex,
                AmbientWordCount = ambientIndexResult.AmbientWordCount,
                AmbientSlotCount = ambientIndexResult.AmbientSlotCount,
                AmbientColorRecipes = ambientIndexResult.ColorRecipes
            };
        }

        return new
        {
            game = gameLabel,
            oClass = $"0x{(ushort)tie.Header.OClass:X4}",
            topology.LodIndex,
            topology.LogicalVertexCount,
            topology.PacketVertexRowCount,
            topology.PrimaryAddressMappedLogicalVertexCount,
            topology.SecondaryAddressMappedLogicalVertexCount,
            topology.UnresolvedLogicalVertexCount,
            topology.StripCount,
            topology.TriangleCount,
            PreviousStripReferencePhaseStripCount = topology.Strips.Count(strip => strip.UsesPreviousStripReferencePhase),
            PreviousStripReferencePhaseTriangleCount = topology.Strips
                .Where(strip => strip.UsesPreviousStripReferencePhase)
                .Sum(strip => strip.TriangleCount),
            normalResult.SourceNormalVertexCount,
            SourceNormalIndexOffsetCount = normalResult.SourceNormalIndexOffsets.Count,
            normalResult.PacketRowNormalVertexCount,
            normalResult.TableNormalVertexCount,
            normalResult.RgbaRecipeNormalVertexCount,
            normalResult.RgbaRecipeConstantColorVertexCount,
            normalResult.RgbaRecipeDegenerateBlendVertexCount,
            normalResult.CrossLodExactNormalVertexCount,
            normalResult.DuplicatePositionExactNormalVertexCount,
            normalResult.TableNormalLayout,
            normalResult.TableNormalTargetMode,
            normalResult.TableNormalPreserveSourceOrientation,
            normalResult.TableNormalCandidateVertexCount,
            normalResult.TableNormalAcceptedVertexCount,
            normalResult.TableNormalSignedAcceptedVertexCount,
            normalResult.TableNormalInvertedAcceptedVertexCount,
            normalResult.TableNormalUpperHemisphereVertexCount,
            normalResult.TableNormalUpperHemisphereStrongDownVertexCount,
            SourceNormalPhaseDominantLayout = sourceNormalPhaseAnalysis.DominantLayout?.ToString(),
            SourceNormalPhaseScoredStripCount = sourceNormalPhaseAnalysis.ScoredStripCount,
            SourceNormalPhaseCurrentVoteStripCount = sourceNormalPhaseAnalysis.CurrentVoteStripCount,
            SourceNormalPhaseInvertedVoteStripCount = sourceNormalPhaseAnalysis.InvertedVoteStripCount,
            SourceNormalPhaseAmbiguousVoteStripCount = sourceNormalPhaseAnalysis.AmbiguousVoteStripCount,
            SourceNormalPhaseInsufficientVoteStripCount = sourceNormalPhaseAnalysis.InsufficientVoteStripCount,
            SourceNormalPhaseWindingRepairStripCount = sourceNormalPhaseWindingRepairStripCount,
            SourceNormalPhaseWindingRepairTriangleCount = sourceNormalPhaseWindingRepairTriangleCount,
            normalResult.DuplicatePositionNormalWeldMode,
            normalResult.DuplicatePositionNormalPairCount,
            normalResult.DuplicatePositionIncompatibleNormalPairCount,
            normalResult.DuplicatePositionCurrentAverageFaceDot,
            normalResult.DuplicatePositionWeldedAverageFaceDot,
            normalResult.DuplicatePositionWeldedMinimumFaceDot,
            WindingInvertedComponentTriangleCount = windingRepairResult.InvertedComponentTriangleCount,
            WindingLocalInwardTriangleCount = windingRepairResult.LocalInwardTriangleCount,
            WindingOpposedNormalTriangleCount = windingRepairResult.OpposedNormalTriangleCount,
            WindingUpperHorizontalTriangleCount = windingRepairResult.UpperHorizontalTriangleCount,
            ExpandedGeneratedNormalFallbackVertexCount = expandedGeneratedNormalFallbackVertexCount,
            SourceNormalMaskAttributeCount = sourceNormalMaskAttributeCount,
            SourceNormalStateAttributeCount = sourceNormalStateAttributeCount,
            SourceNormalStateMissingVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.Missing),
            SourceNormalStateTableExactVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.TableExact),
            SourceNormalStatePacketRowExactVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.PacketRowExact),
            SourceNormalStateAmbiguousRemapVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.AmbiguousRemap),
            SourceNormalStateRejectedRemapVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.RejectedRemap),
            SourceNormalStateCrossLodExactVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.CrossLodExact),
            SourceNormalStateDuplicatePositionExactVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.DuplicatePositionExact),
            SourceNormalStateRgbaRecipeConstantColorVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.RgbaRecipeConstantColor),
            SourceNormalStateRgbaRecipeDegenerateBlendVertexCount = CountSourceNormalState(normalResult.SourceNormalVertexStates, TieGltfSourceNormalState.RgbaRecipeDegenerateBlend),
            SourceNormalMissingDecodedDinkyVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex?.Kind == TiePacketDecodedVertexKind.Dinky),
            SourceNormalMissingDecodedFatVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex?.Kind == TiePacketDecodedVertexKind.Fat),
            SourceNormalMissingPrimaryAddressVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex is null
                    && vertex.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress),
            SourceNormalMissingSecondaryAddressVertexCount = CountSourceNormalStateVertices(
                topology,
                normalResult.SourceNormalVertexStates,
                TieGltfSourceNormalState.Missing,
                vertex => vertex.DecodedVertex is null
                    && vertex.MappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress),
            SuppressedGeneratedNormalFallbackVertexCount = suppressedGeneratedNormalFallbackVertexCount,
            DecodedVertexNormalCount = tie.VertexNormals.Count,
            DecodedVertexNormalRemapCount = tie.VertexNormalRemaps.Count,
            PacketRgbaSlotCount = packetRgbaSlotCount,
            AmbientWordCount = ambientIndexResult.AmbientWordCount,
            AmbientSlotCount = ambientIndexResult.AmbientSlotCount,
            AmbientNormalIndexOffset = ambientIndexResult.NormalIndexOffset,
            AmbientIndexTargetMode = ambientIndexResult.TargetMode,
            AmbientIndexAccessorCount = ambientIndexResult.ResolvedIndexCount > 0
                ? ambientIndexResult.IndexIndices.Count
                : ambientIndexResult.ResolvedVertexCount > 0
                    ? ambientIndexResult.Indices.Count
                    : 0,
            ResolvedAmbientIndexVertexCount = ambientIndexResult.ResolvedVertexCount,
            ResolvedAmbientIndexIndexCount = ambientIndexResult.ResolvedIndexCount,
            AmbientIndexFallbackResolvedVertexCount = ambientIndexResult.FallbackResolvedVertexCount,
            AmbientIndexOutOfRangeVertexCount = ambientIndexResult.OutOfRangeVertexCount,
            AmbientColorRecipeCount = ambientIndexResult.ColorRecipes.Count,
            AmbientColorAverage2RecipeCount = ambientIndexResult.ColorRecipes.Count(
                recipe => string.Equals(recipe.Kind, TieRgbaRemapOperationKind.Average2.ToString(), StringComparison.Ordinal)),
            AmbientColorRecipes = ambientIndexResult.ColorRecipes,
            GlowRgba = FormatRgba(tie.Header.GlowRgba),
            GlowRgbaColor = BuildRgbaDiagnostic(glowColorResult.Rgba),
            DecodedGlowRgbaRemapCount = tie.GlowRgbaRemaps.Count,
            ResolvedGlowRgbaVertexCount = glowColorResult.ResolvedVertexCount,
            UsesVertexColor0 = false,
            UsesDlGlow0 = glowColorResult.ResolvedVertexCount > 0,
            UsesGlowEmission = usesGlowEmission,
            GlowRgbaEmissionStrength = TieGltfGlowBuilder.GetEmissionStrength(glowColorResult.Rgba),
            tie.Header.Scale
        };
    }

    private static bool ShouldWriteFullMetadata(GltfExportMetadataMode metadataMode)
    {
        return metadataMode == GltfExportMetadataMode.Full;
    }
}

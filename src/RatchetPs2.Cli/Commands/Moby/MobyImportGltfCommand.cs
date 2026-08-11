using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Games.DL.Moby;
using System.CommandLine;
using System.Globalization;
using System.Numerics;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyImportGltfCommand
{
    public static Command Build(GameModuleResolver gameModuleResolver)
    {
        var gameOption = CommonOptions.Game();
        var templateOption = new Option<FileInfo?>("--template")
        {
            Description = "Path to the template moby model binary."
        };
        var profileOption = new Option<string>("--profile")
        {
            Description = "Import defaults profile: template, template-shape, custom-static-world, or custom-static-player.",
            DefaultValueFactory = _ => "template"
        };
        var packetModeOption = new Option<string>("--packet-mode")
        {
            Description = "Mesh packet generation mode: auto, passthrough, generate-topology, generate-vertex-positions, generate-vertex-data-from-metadata, generate-topology-from-metadata-shape, generate-vertex-data-with-metadata-shape, generate-vertex-data, or generate-all.",
            DefaultValueFactory = _ => "auto"
        };
        var packetModeMeshesOption = new Option<string?>("--packet-mode-meshes")
        {
            Description = "Optional mesh indices/ranges to apply --packet-mode to, for example 0,3,10-12. Other meshes use passthrough."
        };
        var maxInfluencesOption = new Option<int>("--max-influences")
        {
            Description = "Maximum skin influences to keep per imported vertex.",
            DefaultValueFactory = _ => 3
        };
        var customStaticOption = new Option<bool>("--custom-static")
        {
            Description = "Import the first glTF primitive as a static replacement mesh instead of requiring exporter-shaped node metadata."
        };
        var customStaticGeneratedContainerOption = new Option<bool>("--custom-static-generated-container")
        {
            Description = "For --custom-static, create a generated static moby container instead of reading --template."
        };
        var replaceMeshOption = new Option<int>("--replace-mesh")
        {
            Description = "Template mesh index to replace when --custom-static is used. Use -1 to replace every mesh entry.",
            DefaultValueFactory = _ => 0
        };
        var customStaticScaleOption = new Option<float>("--custom-static-scale")
        {
            Description = "Scale factor applied to positions when --custom-static is used.",
            DefaultValueFactory = _ => 1f
        };
        var customStaticYawDegreesOption = new Option<float>("--custom-static-yaw-degrees")
        {
            Description = "Rotate imported custom-static positions around the vertical axis before skin transfer and packing.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticPitchDegreesOption = new Option<float>("--custom-static-pitch-degrees")
        {
            Description = "Rotate imported custom-static positions around the X axis before skin transfer and packing.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticRollDegreesOption = new Option<float>("--custom-static-roll-degrees")
        {
            Description = "Rotate imported custom-static positions around Blender's visual Z-up axis before skin transfer and packing.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticPostSkinYawDegreesOption = new Option<float>("--custom-static-post-skin-yaw-degrees")
        {
            Description = "Rotate imported custom-static positions around the vertical axis after skin transfer and before packing.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticSplitMeshesOption = new Option<bool>("--custom-static-split-meshes")
        {
            Description = "For --custom-static, split glTF triangle primitives across available template mesh entries."
        };
        var customStaticSplitConnectedComponentsOption = new Option<bool>("--custom-static-split-connected-components")
        {
            Description = "For --custom-static split imports, split disconnected triangle islands before chunking."
        };
        var customStaticSplitConnectedComponentMinTrianglesOption = new Option<int>("--custom-static-split-connected-component-min-triangles")
        {
            Description = "For --custom-static connected-component splitting, keep smaller components bundled with the source mesh instead of making standalone mesh chunks.",
            DefaultValueFactory = _ => 0
        };
        var customStaticSplitAnatomicalRegionsOption = new Option<bool>("--custom-static-split-anatomical-regions")
        {
            Description = "For --custom-static split imports, bucket source triangles by coarse humanoid body region before packet chunking."
        };
        var customStaticSplitSideAxisOption = new Option<string?>("--custom-static-split-side-axis")
        {
            Description = "For --custom-static split imports, split source triangles into negative/center/positive side buckets before chunking: x or z."
        };
        var customStaticSplitSideDeadzoneRatioOption = new Option<float>("--custom-static-split-side-deadzone-ratio")
        {
            Description = "For --custom-static-split-side-axis, whole-source side-axis ratio treated as center/deadzone.",
            DefaultValueFactory = _ => 0.02f
        };
        var customStaticExpandTemplateMeshesOption = new Option<bool>("--custom-static-expand-template-meshes")
        {
            Description = "For --custom-static split imports, clone the replacement template mesh entry when more chunks are needed."
        };
        var customStaticUseOnlyReplaceMeshAsTemplateOption = new Option<bool>("--custom-static-use-only-replace-mesh-as-template")
        {
            Description = "For --custom-static split imports, discard other donor mesh entries and clone only --replace-mesh as the template slot source."
        };
        var customStaticUseMinimalExpandedMeshSlotsOption = new Option<bool>("--custom-static-use-minimal-expanded-mesh-slots")
        {
            Description = "For --custom-static split imports, create expanded mesh slots from a minimal generated prototype instead of deep-cloning the donor mesh entry.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateMeshSlotsOption = new Option<bool>("--custom-static-generate-mesh-slots")
        {
            Description = "For --custom-static split imports, replace template mesh slot prototypes with generated static mesh slots before chunking."
        };
        var customStaticGenerateMeshTableOption = new Option<bool>("--custom-static-generate-mesh-table")
        {
            Description = "For --custom-static split imports, clear donor mesh entries and build the output mesh table from generated static slots.",
            DefaultValueFactory = _ => true
        };
        var customStaticGeneratedMeshSlotCapacityOption = new Option<int>("--custom-static-generated-mesh-slot-capacity")
        {
            Description = "Vertex capacity for --custom-static-generate-mesh-slots.",
            DefaultValueFactory = _ => 127
        };
        var customStaticGenerateGlobalScaffoldOption = new Option<bool>("--custom-static-generate-global-scaffold")
        {
            Description = "For --custom-static, generate static global sections: bounds, default animation, and empty optional gameplay sections.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateHeaderDefaultsOption = new Option<bool>("--custom-static-generate-header-defaults")
        {
            Description = "For --custom-static, generate conservative static defaults for non-offset header policy fields.",
            DefaultValueFactory = _ => true
        };
        var customStaticHeaderLodTransOption = new Option<byte?>("--custom-static-header-lod-trans")
        {
            Description = "For --custom-static probes, override the moby header LOD transition byte."
        };
        var customStaticHeaderMipmapDistanceOption = new Option<byte?>("--custom-static-header-mipmap-distance")
        {
            Description = "For --custom-static probes, override the moby header mipmap distance byte."
        };
        var customStaticTextureMetadataDistanceOption = new Option<float?>("--custom-static-texture-metadata-distance")
        {
            Description = "For --custom-static generated texture metadata probes, override the texture metadata distance float."
        };
        var customStaticProbeMeshesOption = new Option<string?>("--custom-static-probe-meshes")
        {
            Description = "For --custom-static, keep only selected generated mesh indices visible, for example 0-10 or 11-20."
        };
        var customStaticSkipUnprobedMeshesOption = new Option<bool>("--custom-static-skip-unprobed-meshes")
        {
            Description = "For --custom-static probes, leave unselected template mesh slots untouched instead of replacing them with collapsed hidden geometry."
        };
        var customStaticForceSkinJointsOption = new Option<string?>("--custom-static-force-skin-joints")
        {
            Description = "For --custom-static skin transfer, pin generated mesh indices to a primary joint, for example 51-54,80-90=10;55-79,99-100=12."
        };
        var customStaticForceSourceTriangleJointsOption = new Option<string?>("--custom-static-force-source-triangle-joints")
        {
            Description = "For --custom-static skin transfer, split and pin source triangles to a primary joint, for example 0:0:65-82,503-538=12."
        };
        var outputModelScaleOption = new Option<float?>("--output-model-scale")
        {
            Description = "Override the output moby header scale field. Use 1 to write 0x3F800000 at offset 0x24."
        };
        var customStaticRecalculateBoundingSphereOption = new Option<bool>("--custom-static-recalculate-bounding-sphere")
        {
            Description = "For --custom-static, recalculate the model and animation bounding spheres from visible imported geometry."
        };
        var customStaticBoundingSpherePaddingOption = new Option<float>("--custom-static-bounding-sphere-padding")
        {
            Description = "Padding multiplier used with --custom-static-recalculate-bounding-sphere.",
            DefaultValueFactory = _ => 8f
        };
        var customStaticPreserveTemplatePacketsOption = new Option<bool>("--custom-static-preserve-template-packets")
        {
            Description = "For --custom-static, deform the template vertices into the input bounds while preserving template VIF/topology packets."
        };
        var customStaticPreserveTemplateVertexLayoutOption = new Option<bool>("--custom-static-preserve-template-vertex-layout")
        {
            Description = "For --custom-static, keep the template vertex-data layout/count while generating topology from the input primitive."
        };
        var customStaticHideOtherMeshesOption = new Option<bool>("--custom-static-hide-other-meshes")
        {
            Description = "For --custom-static with a single --replace-mesh target, collapse other template mesh entries to their centers."
        };
        var customStaticDropTemplateAttachmentsOption = new Option<bool>("--custom-static-drop-template-attachments")
        {
            Description = "For --custom-static, remove donor bangle/corncob attachment sections from the output moby."
        };
        var customStaticDropTemplateNonBodyMeshesOption = new Option<bool>("--custom-static-drop-template-non-body-meshes")
        {
            Description = "For --custom-static, remove donor bangle/corncob sections plus bangle/metal mesh entries from the output moby."
        };
        var customStaticStripTemplateGameplayDataOption = new Option<bool>("--custom-static-strip-template-gameplay-data")
        {
            Description = "For --custom-static, remove donor collision, animations, animation joints, sounds, and shadow data from the output moby."
        };
        var customStaticDropTemplateCollisionOption = new Option<bool>("--custom-static-drop-template-collision")
        {
            Description = "For --custom-static, remove donor collision data from the output moby."
        };
        var customStaticDropTemplateAnimationsOption = new Option<bool>("--custom-static-drop-template-animations")
        {
            Description = "For --custom-static, remove donor animation sequences from the output moby."
        };
        var customStaticGenerateDefaultAnimationOption = new Option<bool>("--custom-static-generate-default-animation")
        {
            Description = "For --custom-static, replace donor animations with a generated static one-frame default animation scaffold."
        };
        var customStaticDropTemplateAnimationJointsOption = new Option<bool>("--custom-static-drop-template-animation-joints")
        {
            Description = "For --custom-static, remove donor animation-joint data from the output moby."
        };
        var customStaticDropTemplateSoundsOption = new Option<bool>("--custom-static-drop-template-sounds")
        {
            Description = "For --custom-static, remove donor sound definitions from the output moby."
        };
        var customStaticDropTemplateShadowOption = new Option<bool>("--custom-static-drop-template-shadow")
        {
            Description = "For --custom-static, remove donor shadow data from the output moby."
        };
        var customStaticDropTexturesOption = new Option<bool>("--custom-static-drop-textures")
        {
            Description = "For --custom-static, remove texture VIF/GIF data from replaced visible mesh entries."
        };
        var customStaticConstantTexturesOption = new Option<bool>("--custom-static-constant-textures")
        {
            Description = "For --custom-static, keep texture VIF/GIF data but repeat the first texture record across the visible replacement mesh."
        };
        var customStaticGenerateTextureMetadataOption = new Option<bool>("--custom-static-generate-texture-metadata")
        {
            Description = "For --custom-static, generate GIF usage and VIF texture metadata from one template texture prototype instead of preserving each donor mesh entry's texture metadata.",
            DefaultValueFactory = _ => true
        };
        var customStaticUseGeneratedTextureMetadataPrototypeOption = new Option<bool>("--custom-static-use-generated-texture-metadata-prototype")
        {
            Description = "For --custom-static probes, use generated texture metadata while building topology instead of donor texture metadata.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateMeshEntryMetadataOption = new Option<bool>("--custom-static-generate-mesh-entry-metadata")
        {
            Description = "For --custom-static probes, generate neutral mesh table metadata fields instead of cloning the donor values.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateMeshEntryUnknown0AOption = new Option<bool>("--custom-static-generate-mesh-entry-unknown0a")
        {
            Description = "For --custom-static probes, generate mesh table Unknown0A from the visible mesh VIF data length."
        };
        var customStaticGenerateMeshEntryUnknown0ATotalQwOption = new Option<bool>("--custom-static-generate-mesh-entry-unknown0a-total-qw")
        {
            Description = "For --custom-static probes, generate mesh table Unknown0A from the total visible mesh VIF list length including texture metadata."
        };
        var customStaticZeroCommonTransformJointOption = new Option<bool>("--custom-static-zero-common-transform-joint")
        {
            Description = "For --custom-static probes, set visible mesh table common-transform joint indices to zero before vertex rows are generated."
        };
        var customStaticZeroCommonTransformJointHeaderOnlyOption = new Option<bool>("--custom-static-zero-common-transform-joint-header-only")
        {
            Description = "For --custom-static probes, set visible mesh table common-transform joint indices to zero after vertex rows are generated."
        };
        var customStaticUseDominantSkinJointAsCommonTransformOption = new Option<bool>("--custom-static-use-dominant-skin-joint-as-common-transform")
        {
            Description = "For --custom-static skinned probes, set each generated mesh entry common-transform joint to the dominant assigned skin joint."
        };
        var customStaticUseDominantHeadSkinJointAsCommonTransformOption = new Option<bool>("--custom-static-use-dominant-head-skin-joint-as-common-transform")
        {
            Description = "For --custom-static skinned probes, set generated head/core mesh common-transform joints from stable dominant head skin joints only."
        };
        var customStaticUseReferenceMeshCommonTransformOption = new Option<bool>("--custom-static-use-reference-mesh-common-transform")
        {
            Description = "For --custom-static skinned probes, copy each generated mesh entry common-transform joint from the dominant skin reference source mesh."
        };
        var customStaticGenerateCommonTransformsOption = new Option<bool>("--custom-static-generate-common-transforms")
        {
            Description = "For --custom-static, generate a minimal static common transform byte table sized for generated mesh metadata.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateCommonTransformSkeletonOption = new Option<bool>("--custom-static-generate-common-transform-skeleton")
        {
            Description = "For --custom-static probes, pair generated common transforms with generated identity skeleton bones."
        };
        var customStaticRigSourceOption = new Option<FileInfo?>("--custom-static-rig-source")
        {
            Description = "For --custom-static, copy skeleton/common transforms/animation joints from this moby."
        };
        var customStaticSkinReferenceOption = new Option<FileInfo?>("--custom-static-skin-reference")
        {
            Description = "For --custom-static, transfer decoded skin weights from this moby by nearest fitted reference vertex."
        };
        var customStaticTransferReferenceSkinningOption = new Option<bool>("--custom-static-transfer-reference-skinning")
        {
            Description = "For --custom-static, use --custom-static-skin-reference for weight assignment instead of joint-distance approximation."
        };
        var customStaticReferenceSkinningSampleCountOption = new Option<int>("--custom-static-reference-skinning-sample-count")
        {
            Description = "For reference skinning transfer, blend this many nearest reference vertices.",
            DefaultValueFactory = _ => 1
        };
        var customStaticReferenceSkinningVerticalWindowOption = new Option<float?>("--custom-static-reference-skinning-vertical-window")
        {
            Description = "For reference skinning transfer, only sample reference vertices within this fitted vertical distance when enough samples exist."
        };
        var customStaticReferenceSkinningSameSideOption = new Option<bool>("--custom-static-reference-skinning-same-side")
        {
            Description = "For reference skinning transfer, prefer samples from the same fitted left/right side of the model."
        };
        var customStaticReferenceSkinningSideAxisOption = new Option<string>("--custom-static-reference-skinning-side-axis")
        {
            Description = "For reference skinning transfer, choose the side axis used by --custom-static-reference-skinning-same-side: x or z.",
            DefaultValueFactory = _ => "x"
        };
        var customStaticReferenceSkinningSideDeadzoneRatioOption = new Option<float>("--custom-static-reference-skinning-side-deadzone-ratio")
        {
            Description = "For reference skinning transfer, whole-model side-axis ratio treated as center/deadzone by --custom-static-reference-skinning-same-side.",
            DefaultValueFactory = _ => 0.03f
        };
        var customStaticReferenceSkinningMaterialRegionsOption = new Option<bool>("--custom-static-reference-skinning-material-regions")
        {
            Description = "For reference skinning transfer, constrain candidate samples by material body region names like head, torso, legs, and shoes."
        };
        var customStaticReferenceSkinningDisableAnatomicalFiltersOption = new Option<bool>("--custom-static-reference-skinning-disable-anatomical-filters")
        {
            Description = "For reference skinning transfer probes, skip generic humanoid joint-family gates and rely on fitted nearest/same-side samples."
        };
        var customStaticReferenceSkinningPreserveLowerBodyFiltersOption = new Option<bool>("--custom-static-reference-skinning-preserve-lower-body-filters")
        {
            Description = "For reference skinning transfer probes, keep DL/UYA leg and foot joint-family gates even when anatomical filters are otherwise disabled."
        };
        var customStaticReferenceSkinningPreserveShoulderFiltersOption = new Option<bool>("--custom-static-reference-skinning-preserve-shoulder-filters")
        {
            Description = "For reference skinning transfer probes, keep shoulder and upper-arm joint-family gates even when anatomical filters are otherwise disabled."
        };
        var customStaticReferenceSkinningShoulderInwardBiasOption = new Option<float>("--custom-static-reference-skinning-shoulder-inward-bias")
        {
            Description = "For reference skinning transfer probes, move fitted DL shoulder reference samples inward along the side axis before nearest-sample selection.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticReferenceSkinningTriangleCoherentOption = new Option<bool>("--custom-static-reference-skinning-triangle-coherent")
        {
            Description = "For reference skinning transfer, assign skin votes from triangle centroids so adjacent vertices are less likely to split one triangle across unrelated joints."
        };
        var customStaticReferenceSkinningSplitPrimarySeamsOption = new Option<bool>("--custom-static-reference-skinning-split-primary-seams")
        {
            Description = "For reference skinning transfer probes, duplicate only vertices shared by triangles assigned to different primary joints."
        };
        var customStaticReferenceSkinningRigidMeshCentroidOption = new Option<bool>("--custom-static-reference-skinning-rigid-mesh-centroid")
        {
            Description = "For reference skinning transfer probes, assign every vertex in a generated mesh chunk from that chunk's centroid."
        };
        var customStaticReferenceSkinningRigidTriangleCentroidOption = new Option<bool>("--custom-static-reference-skinning-rigid-triangle-centroid")
        {
            Description = "For reference skinning transfer probes, duplicate triangle vertices and assign each triangle from its centroid."
        };
        var customStaticReferenceSkinningSmoothPrimaryIterationsOption = new Option<int>("--custom-static-reference-skinning-smooth-primary-iterations")
        {
            Description = "For reference skinning transfer probes, smooth single-primary joint assignments over mesh adjacency this many iterations.",
            DefaultValueFactory = _ => 0
        };
        var customStaticReferenceSkinningDistancePowerOption = new Option<float>("--custom-static-reference-skinning-distance-power")
        {
            Description = "For reference skinning transfer, controls how strongly nearer samples dominate. 1 is linear inverse distance; 2 is sharper.",
            DefaultValueFactory = _ => 1f
        };
        var customStaticReferenceSkinningYawDegreesOption = new Option<float>("--custom-static-reference-skinning-yaw-degrees")
        {
            Description = "For reference skinning transfer, rotate the reference sample cloud around the fitted vertical axis before matching.",
            DefaultValueFactory = _ => 0f
        };
        var customStaticApproximateRigSkinningOption = new Option<bool>("--custom-static-approximate-rig-skinning")
        {
            Description = "For --custom-static with --custom-static-rig-source, approximate vertex weights from the rig joint positions."
        };
        var customStaticApproximateRigSkinningUseSourcePoseOption = new Option<bool>("--custom-static-approximate-rig-skinning-use-source-pose")
        {
            Description = "For --custom-static approximate rig skinning, assign weights from the rig source common-transform pose instead of fitting the rig to the imported mesh bounds."
        };
        var customStaticWriteFittedRigCommonTransformsOption = new Option<bool>("--custom-static-write-fitted-rig-common-transforms")
        {
            Description = "For --custom-static approximate rig skinning probes, write fitted rig common-transform positions while preserving the rig source hierarchy bytes."
        };
        var customStaticSkinPositionsRelativeToBindOption = new Option<bool>("--custom-static-skin-positions-relative-to-bind")
        {
            Description = "For --custom-static approximate rig skinning probes, write skinned vertex positions relative to the weighted bind joint position."
        };
        var customStaticCopyRigAnimation0Option = new Option<bool>("--custom-static-copy-rig-animation0")
        {
            Description = "For --custom-static with --custom-static-rig-source, copy animation 0 from the rig source into the output moby."
        };
        var customStaticCopyRigAnimationOption = new Option<int?>("--custom-static-copy-rig-animation")
        {
            Description = "For --custom-static with --custom-static-rig-source, copy this animation index from the rig source into the output moby."
        };
        var customStaticDoubleSidedOption = new Option<bool>("--custom-static-double-sided")
        {
            Description = "For --custom-static, duplicate imported triangles with reversed winding."
        };
        var customStaticPreserveTopologyTailOption = new Option<bool>("--custom-static-preserve-topology-tail")
        {
            Description = "For --custom-static, preserve template topology payload bytes after generated topology tokens."
        };
        var customStaticCompactTopologyPacketOption = new Option<bool>("--custom-static-compact-topology-packet")
        {
            Description = "For --custom-static probes, shrink the topology VIF packet to the generated payload size instead of preserving template packet capacity.",
            DefaultValueFactory = _ => true
        };
        var customStaticStrictTriangleCapOption = new Option<bool>("--custom-static-strict-triangle-cap")
        {
            Description = "For --custom-static split imports, do not raise --custom-static-max-triangles-per-mesh to fit the high-lod mesh budget."
        };
        var customStaticUseZeroMarkerTopologyOption = new Option<bool>("--custom-static-force-zero-marker-topology")
        {
            Description = "For --custom-static, force generated topology to use texture/GIF zero markers.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateMinimalVifContainerOption = new Option<bool>("--custom-static-generate-minimal-vif-container")
        {
            Description = "For --custom-static probes, generate the leading VIF vertex-domain unpack and topology container instead of preserving the donor VIF prefix.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateVifDomainCapacityOption = new Option<bool>("--custom-static-generate-vif-domain-capacity")
        {
            Description = "For --custom-static probes, generate the leading VIF vertex-domain capacity from imported vertex count plus compact epilogue rows.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateVertexHeaderDomainCapacityOption = new Option<bool>("--custom-static-generate-vertex-header-domain-capacity")
        {
            Description = "For --custom-static compact rigid-row mode, generate the vertex-data header domain capacity from imported vertex count plus compact epilogue rows.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateMeshTableVertexCountOption = new Option<bool>("--custom-static-generate-mesh-table-vertex-count")
        {
            Description = "For --custom-static probes, generate the mesh table vertex count from imported vertex count plus compact epilogue rows.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateRigidVertexDataOption = new Option<bool>("--custom-static-generate-rigid-vertex-data")
        {
            Description = "For --custom-static, generate fresh rigid vertex rows for visible custom meshes instead of inheriting template skin/control rows."
        };
        var customStaticGenerateRigidRowsInTemplateLayoutOption = new Option<bool>("--custom-static-generate-rigid-rows-in-template-layout")
        {
            Description = "For --custom-static, keep template vertex-data header/layout but replace visible custom mesh rows with generated neutral rigid rows."
        };
        var customStaticGenerateCompactRigidRowsOption = new Option<bool>("--custom-static-generate-compact-rigid-rows")
        {
            Description = "For --custom-static, generate a compact rigid vertex table using only imported vertices plus epilogue rows.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateCompactVertexHeaderOption = new Option<bool>("--custom-static-generate-compact-vertex-header")
        {
            Description = "For --custom-static compact rigid-row mode, generate the vertex-data header instead of copying donor header bytes.",
            DefaultValueFactory = _ => true
        };
        var customStaticPreserveTemplateRowContractOption = new Option<bool>("--custom-static-preserve-template-row-contract")
        {
            Description = "For --custom-static compact rigid-row mode, preserve donor mesh count, vertex header counts, and vertex allocation size while generating row contents."
        };
        var customStaticPadCompactRigidRowsToTemplateSizeOption = new Option<bool>("--custom-static-pad-compact-rigid-rows-to-template-size")
        {
            Description = "For --custom-static compact rigid-row mode, keep the donor vertex data allocation size while compacting active row counts."
        };
        var customStaticPreserveTemplateMeshVertexCountOption = new Option<bool>("--custom-static-preserve-template-mesh-vertex-count")
        {
            Description = "For --custom-static probes, preserve the mesh table vertex count from the template entry."
        };
        var customStaticPreserveTemplateVertexHeaderCountsOption = new Option<bool>("--custom-static-preserve-template-vertex-header-counts")
        {
            Description = "For --custom-static compact rigid-row mode, preserve the donor vertex-data blend/main row counts while filling extra rows with generated data."
        };
        var customStaticRewriteTemplateEpilogueRowsOption = new Option<bool>("--custom-static-rewrite-template-epilogue-rows")
        {
            Description = "For --custom-static rigid template-row mode, rewrite template epilogue rows with generated prefix/positions while preserving their low9 control values."
        };
        var customStaticRewriteTemplateEpiloguePrefixesOption = new Option<bool>("--custom-static-rewrite-template-epilogue-prefixes")
        {
            Description = "For --custom-static rigid template-row mode, rewrite only template epilogue row prefix bytes."
        };
        var customStaticRewriteTemplateEpiloguePositionsOption = new Option<bool>("--custom-static-rewrite-template-epilogue-positions")
        {
            Description = "For --custom-static rigid template-row mode, rewrite only template epilogue row positions.",
            DefaultValueFactory = _ => true
        };
        var customStaticGenerateTemplateEpilogueControlPrefixOption = new Option<bool>("--custom-static-generate-template-epilogue-control-prefix")
        {
            Description = "For --custom-static rigid template-row mode, regenerate epilogue control prefixes while preserving the template's final marker payload.",
            DefaultValueFactory = _ => true
        };
        var customStaticClearTemplateEpilogueFinalMarkerOption = new Option<bool>("--custom-static-clear-template-epilogue-final-marker")
        {
            Description = "For --custom-static probes, clear the generated epilogue final marker instead of preserving donor bytes."
        };
        var customStaticGenerateTemplateEpilogueFinalMarkerOption = new Option<bool>("--custom-static-generate-template-epilogue-final-marker")
        {
            Description = "For --custom-static probes, generate a neutral epilogue final marker instead of preserving donor bytes.",
            DefaultValueFactory = _ => true
        };
        var customStaticNeutralizeTemplateSkinningOption = new Option<bool>("--custom-static-neutralize-template-skinning")
        {
            Description = "For --custom-static, keep template vertex-data layout but rewrite visible custom mesh skin/control rows to a single rigid transform."
        };
        var customStaticFlattenVertexPrefixesOption = new Option<bool>("--custom-static-flatten-vertex-prefixes")
        {
            Description = "For --custom-static, normalize visible custom mesh vertex row prefix bytes to the first row pattern for flatter shading."
        };
        var customStaticVertexPrefixOption = new Option<string?>("--custom-static-vertex-prefix")
        {
            Description = "For --custom-static rigid template-row mode, force each visible generated vertex row prefix to 8 bytes of hex, for example 00F4000000000000."
        };
        var customStaticVertexPrefixShadeOption = new Option<byte?>("--custom-static-vertex-prefix-shade")
        {
            Description = "For --custom-static rigid template-row mode, generate a static vertex row prefix using this final shade byte, for example 41 for 0x29."
        };
        var customStaticAutoVertexPrefixShadeOption = new Option<bool>("--custom-static-auto-vertex-prefix-shade")
        {
            Description = "For --custom-static rigid template-row mode, derive the generated static vertex row prefix shade byte from the donor mesh's first row.",
            DefaultValueFactory = _ => true
        };
        var customStaticPreserveTemplateVertexControlWordsOption = new Option<bool>("--custom-static-preserve-template-vertex-control-words")
        {
            Description = "For --custom-static rigid template-row mode, preserve each template vertex row's first control word while replacing positions."
        };
        var customStaticZeroVertexControlHighBitsOption = new Option<bool>("--custom-static-zero-vertex-control-high-bits")
        {
            Description = "For --custom-static rigid template-row mode, force generated vertex control words to use high bits of zero."
        };
        var customStaticPreserveTemplateVertexControlLowBitsOption = new Option<bool>("--custom-static-preserve-template-vertex-control-low-bits")
        {
            Description = "For --custom-static rigid template-row mode, preserve each template vertex control word's low 9 bits while allowing the high bits probe mode to vary."
        };
        var customStaticVertexControlLow9ValueOption = new Option<int?>("--custom-static-vertex-control-low9-value")
        {
            Description = "For --custom-static rigid template-row mode, force every generated vertex control word's low 9 bits to this value."
        };
        var customStaticAutoVertexControlLow9TailOption = new Option<bool>("--custom-static-auto-vertex-control-low9-tail")
        {
            Description = "For --custom-static rigid template-row mode, force generated low9 tail rows to 255 while preserving any selected donor active-window rows.",
            DefaultValueFactory = _ => true
        };
        var customStaticVertexControlLow9WarmupZeroCountOption = new Option<int?>("--custom-static-vertex-control-low9-warmup-zero-count")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, write zero for this many initial vertex rows before the forced value."
        };
        var customStaticPreserveTemplateSparseLow9CountOption = new Option<int?>("--custom-static-preserve-template-sparse-low9-count")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, preserve this many non-255 template low9 rows before falling back to the forced value."
        };
        var customStaticPreserveTemplateLow9MaxValueOption = new Option<int?>("--custom-static-preserve-template-low9-max-value")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, preserve template low9 rows whose values are between 0 and this value."
        };
        var customStaticAutoPreserveTemplateLow9MaxValueOption = new Option<bool>("--custom-static-auto-preserve-template-low9-max-value")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, derive the preserved template low9 value window from the donor mesh.",
            DefaultValueFactory = _ => true
        };
        var customStaticPreserveDuplicateLow9ValuesOption = new Option<bool>("--custom-static-preserve-duplicate-low9-values")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, preserve template low9 rows whose values appear in the duplicate vertex cache table."
        };
        var customStaticPreserveLow9UpToMaxDuplicateOption = new Option<bool>("--custom-static-preserve-low9-up-to-max-duplicate")
        {
            Description = "For --custom-static rigid template-row mode with a forced low9 value, preserve template low9 rows from 0 through the maximum duplicate vertex cache value."
        };
        var customStaticIsolatedTriangleTopologyOption = new Option<bool>("--custom-static-isolated-triangle-topology")
        {
            Description = "For --custom-static, emit each visible custom triangle as an independent topology restart instead of building greedy strips."
        };
        var customStaticMaxTrianglesPerMeshOption = new Option<int?>("--custom-static-max-triangles-per-mesh")
        {
            Description = "For --custom-static split imports, cap source triangles per generated mesh chunk."
        };
        var customStaticMaxGeneratedMeshesOption = new Option<int?>("--custom-static-max-generated-meshes")
        {
            Description = "For --custom-static split imports, stop after generating this many visible source mesh chunks."
        };
        var customStaticMaxHighLodMeshesOption = new Option<int?>("--custom-static-max-high-lod-meshes")
        {
            Description = "For --custom-static split imports, keep only the first N generated high-lod chunks as high-lod and bucket later chunks as far LOD."
        };
        var customStaticInitialTriangleCapOption = new Option<int?>("--custom-static-initial-triangle-cap")
        {
            Description = "For --custom-static split imports, override the triangle cap for the first source primitive's initial triangle range."
        };
        var customStaticInitialTriangleCountOption = new Option<int?>("--custom-static-initial-triangle-count")
        {
            Description = "For --custom-static split imports, number of initial source triangles affected by --custom-static-initial-triangle-cap."
        };
        var customStaticMaterialTextureIdsOption = new Option<string?>("--custom-static-material-texture-ids")
        {
            Description = "For --custom-static, map glTF material names to moby texture IDs, for example head=0,torso=1,legs=2,shoes=3."
        };
        var customStaticMaterialUvScalesOption = new Option<string?>("--custom-static-material-uv-scales")
        {
            Description = "For --custom-static, scale glTF UVs by material name before import, for example legs=0.5:1."
        };
        var customStaticClampUvsOption = new Option<bool>("--custom-static-clamp-uvs")
        {
            Description = "For --custom-static, clamp imported UVs into the 0..1 range after material UV scaling."
        };
        var customStaticSkipTexCoordVifWriteOption = new Option<bool>("--custom-static-skip-texcoord-vif-write")
        {
            Description = "For --custom-static probes, do not patch imported UVs into VIF unpack payloads."
        };
        var inputOption = CommonOptions.InputFile("Path to the .gltf file to import.");
        var outputOption = CommonOptions.OutputFile("Path to write the imported moby model binary.");

        HideOptions(
            packetModeMeshesOption,
            customStaticGeneratedContainerOption,
            customStaticSplitConnectedComponentsOption,
            customStaticSplitConnectedComponentMinTrianglesOption,
            customStaticSplitAnatomicalRegionsOption,
            customStaticSplitSideAxisOption,
            customStaticSplitSideDeadzoneRatioOption,
            customStaticUseOnlyReplaceMeshAsTemplateOption,
            customStaticUseMinimalExpandedMeshSlotsOption,
            customStaticGenerateMeshSlotsOption,
            customStaticGenerateMeshTableOption,
            customStaticGeneratedMeshSlotCapacityOption,
            customStaticGenerateGlobalScaffoldOption,
            customStaticGenerateHeaderDefaultsOption,
            customStaticHeaderLodTransOption,
            customStaticHeaderMipmapDistanceOption,
            customStaticTextureMetadataDistanceOption,
            customStaticProbeMeshesOption,
            customStaticSkipUnprobedMeshesOption,
            customStaticForceSkinJointsOption,
            customStaticForceSourceTriangleJointsOption,
            customStaticBoundingSpherePaddingOption,
            customStaticPreserveTemplatePacketsOption,
            customStaticPreserveTemplateVertexLayoutOption,
            customStaticHideOtherMeshesOption,
            customStaticDropTemplateAttachmentsOption,
            customStaticDropTemplateNonBodyMeshesOption,
            customStaticStripTemplateGameplayDataOption,
            customStaticDropTemplateCollisionOption,
            customStaticDropTemplateAnimationsOption,
            customStaticGenerateDefaultAnimationOption,
            customStaticDropTemplateAnimationJointsOption,
            customStaticDropTemplateSoundsOption,
            customStaticDropTemplateShadowOption,
            customStaticDropTexturesOption,
            customStaticConstantTexturesOption,
            customStaticGenerateTextureMetadataOption,
            customStaticUseGeneratedTextureMetadataPrototypeOption,
            customStaticGenerateMeshEntryMetadataOption,
            customStaticGenerateMeshEntryUnknown0AOption,
            customStaticGenerateMeshEntryUnknown0ATotalQwOption,
            customStaticZeroCommonTransformJointOption,
            customStaticZeroCommonTransformJointHeaderOnlyOption,
            customStaticUseDominantSkinJointAsCommonTransformOption,
            customStaticUseDominantHeadSkinJointAsCommonTransformOption,
            customStaticUseReferenceMeshCommonTransformOption,
            customStaticGenerateCommonTransformsOption,
            customStaticGenerateCommonTransformSkeletonOption,
            customStaticReferenceSkinningVerticalWindowOption,
            customStaticReferenceSkinningSameSideOption,
            customStaticReferenceSkinningSideAxisOption,
            customStaticReferenceSkinningSideDeadzoneRatioOption,
            customStaticReferenceSkinningMaterialRegionsOption,
            customStaticReferenceSkinningDisableAnatomicalFiltersOption,
            customStaticReferenceSkinningPreserveLowerBodyFiltersOption,
            customStaticReferenceSkinningPreserveShoulderFiltersOption,
            customStaticReferenceSkinningShoulderInwardBiasOption,
            customStaticReferenceSkinningTriangleCoherentOption,
            customStaticReferenceSkinningSplitPrimarySeamsOption,
            customStaticReferenceSkinningRigidMeshCentroidOption,
            customStaticReferenceSkinningRigidTriangleCentroidOption,
            customStaticReferenceSkinningSmoothPrimaryIterationsOption,
            customStaticReferenceSkinningDistancePowerOption,
            customStaticReferenceSkinningYawDegreesOption,
            customStaticApproximateRigSkinningOption,
            customStaticApproximateRigSkinningUseSourcePoseOption,
            customStaticWriteFittedRigCommonTransformsOption,
            customStaticSkinPositionsRelativeToBindOption,
            customStaticPreserveTopologyTailOption,
            customStaticCompactTopologyPacketOption,
            customStaticStrictTriangleCapOption,
            customStaticUseZeroMarkerTopologyOption,
            customStaticGenerateMinimalVifContainerOption,
            customStaticGenerateVifDomainCapacityOption,
            customStaticGenerateVertexHeaderDomainCapacityOption,
            customStaticGenerateMeshTableVertexCountOption,
            customStaticGenerateRigidVertexDataOption,
            customStaticGenerateRigidRowsInTemplateLayoutOption,
            customStaticGenerateCompactRigidRowsOption,
            customStaticGenerateCompactVertexHeaderOption,
            customStaticPreserveTemplateRowContractOption,
            customStaticPadCompactRigidRowsToTemplateSizeOption,
            customStaticPreserveTemplateMeshVertexCountOption,
            customStaticPreserveTemplateVertexHeaderCountsOption,
            customStaticRewriteTemplateEpilogueRowsOption,
            customStaticRewriteTemplateEpiloguePrefixesOption,
            customStaticRewriteTemplateEpiloguePositionsOption,
            customStaticGenerateTemplateEpilogueControlPrefixOption,
            customStaticClearTemplateEpilogueFinalMarkerOption,
            customStaticGenerateTemplateEpilogueFinalMarkerOption,
            customStaticNeutralizeTemplateSkinningOption,
            customStaticFlattenVertexPrefixesOption,
            customStaticVertexPrefixOption,
            customStaticVertexPrefixShadeOption,
            customStaticAutoVertexPrefixShadeOption,
            customStaticPreserveTemplateVertexControlWordsOption,
            customStaticZeroVertexControlHighBitsOption,
            customStaticPreserveTemplateVertexControlLowBitsOption,
            customStaticVertexControlLow9ValueOption,
            customStaticAutoVertexControlLow9TailOption,
            customStaticVertexControlLow9WarmupZeroCountOption,
            customStaticPreserveTemplateSparseLow9CountOption,
            customStaticPreserveTemplateLow9MaxValueOption,
            customStaticAutoPreserveTemplateLow9MaxValueOption,
            customStaticPreserveDuplicateLow9ValuesOption,
            customStaticPreserveLow9UpToMaxDuplicateOption,
            customStaticIsolatedTriangleTopologyOption,
            customStaticInitialTriangleCapOption,
            customStaticInitialTriangleCountOption,
            customStaticSkipTexCoordVifWriteOption);

        var command = CliCommandBuilder.Create(
            "import-gltf",
            "Import glTF geometry and DL compact animations into a template moby model.",
            gameOption,
            templateOption,
            profileOption,
            packetModeOption,
            packetModeMeshesOption,
            maxInfluencesOption,
            customStaticOption,
            customStaticGeneratedContainerOption,
            replaceMeshOption,
            customStaticScaleOption,
            customStaticYawDegreesOption,
            customStaticPitchDegreesOption,
            customStaticRollDegreesOption,
            customStaticPostSkinYawDegreesOption,
            customStaticSplitMeshesOption,
            customStaticSplitConnectedComponentsOption,
            customStaticSplitConnectedComponentMinTrianglesOption,
            customStaticSplitAnatomicalRegionsOption,
            customStaticSplitSideAxisOption,
            customStaticSplitSideDeadzoneRatioOption,
            customStaticExpandTemplateMeshesOption,
            customStaticUseOnlyReplaceMeshAsTemplateOption,
            customStaticUseMinimalExpandedMeshSlotsOption,
            customStaticGenerateMeshSlotsOption,
            customStaticGenerateMeshTableOption,
            customStaticGeneratedMeshSlotCapacityOption,
            customStaticGenerateGlobalScaffoldOption,
            customStaticGenerateHeaderDefaultsOption,
            customStaticHeaderLodTransOption,
            customStaticHeaderMipmapDistanceOption,
            customStaticTextureMetadataDistanceOption,
            customStaticProbeMeshesOption,
            customStaticSkipUnprobedMeshesOption,
            customStaticForceSkinJointsOption,
            customStaticForceSourceTriangleJointsOption,
            outputModelScaleOption,
            customStaticRecalculateBoundingSphereOption,
            customStaticBoundingSpherePaddingOption,
            customStaticPreserveTemplatePacketsOption,
            customStaticPreserveTemplateVertexLayoutOption,
            customStaticHideOtherMeshesOption,
            customStaticDropTemplateAttachmentsOption,
            customStaticDropTemplateNonBodyMeshesOption,
            customStaticStripTemplateGameplayDataOption,
            customStaticDropTemplateCollisionOption,
            customStaticDropTemplateAnimationsOption,
            customStaticGenerateDefaultAnimationOption,
            customStaticDropTemplateAnimationJointsOption,
            customStaticDropTemplateSoundsOption,
            customStaticDropTemplateShadowOption,
            customStaticDropTexturesOption,
            customStaticConstantTexturesOption,
            customStaticGenerateTextureMetadataOption,
            customStaticUseGeneratedTextureMetadataPrototypeOption,
            customStaticGenerateMeshEntryMetadataOption,
            customStaticGenerateMeshEntryUnknown0AOption,
            customStaticGenerateMeshEntryUnknown0ATotalQwOption,
            customStaticZeroCommonTransformJointOption,
            customStaticZeroCommonTransformJointHeaderOnlyOption,
            customStaticUseDominantSkinJointAsCommonTransformOption,
            customStaticUseDominantHeadSkinJointAsCommonTransformOption,
            customStaticUseReferenceMeshCommonTransformOption,
            customStaticGenerateCommonTransformsOption,
            customStaticGenerateCommonTransformSkeletonOption,
            customStaticRigSourceOption,
            customStaticSkinReferenceOption,
            customStaticTransferReferenceSkinningOption,
            customStaticReferenceSkinningSampleCountOption,
            customStaticReferenceSkinningVerticalWindowOption,
            customStaticReferenceSkinningSameSideOption,
            customStaticReferenceSkinningSideAxisOption,
            customStaticReferenceSkinningSideDeadzoneRatioOption,
            customStaticReferenceSkinningMaterialRegionsOption,
            customStaticReferenceSkinningDisableAnatomicalFiltersOption,
            customStaticReferenceSkinningPreserveLowerBodyFiltersOption,
            customStaticReferenceSkinningPreserveShoulderFiltersOption,
            customStaticReferenceSkinningShoulderInwardBiasOption,
            customStaticReferenceSkinningTriangleCoherentOption,
            customStaticReferenceSkinningSplitPrimarySeamsOption,
            customStaticReferenceSkinningRigidMeshCentroidOption,
            customStaticReferenceSkinningRigidTriangleCentroidOption,
            customStaticReferenceSkinningSmoothPrimaryIterationsOption,
            customStaticReferenceSkinningDistancePowerOption,
            customStaticReferenceSkinningYawDegreesOption,
            customStaticApproximateRigSkinningOption,
            customStaticApproximateRigSkinningUseSourcePoseOption,
            customStaticWriteFittedRigCommonTransformsOption,
            customStaticSkinPositionsRelativeToBindOption,
            customStaticCopyRigAnimation0Option,
            customStaticCopyRigAnimationOption,
            customStaticDoubleSidedOption,
            customStaticPreserveTopologyTailOption,
            customStaticCompactTopologyPacketOption,
            customStaticStrictTriangleCapOption,
            customStaticUseZeroMarkerTopologyOption,
            customStaticGenerateMinimalVifContainerOption,
            customStaticGenerateVifDomainCapacityOption,
            customStaticGenerateVertexHeaderDomainCapacityOption,
            customStaticGenerateMeshTableVertexCountOption,
            customStaticGenerateRigidVertexDataOption,
            customStaticGenerateRigidRowsInTemplateLayoutOption,
            customStaticGenerateCompactRigidRowsOption,
            customStaticGenerateCompactVertexHeaderOption,
            customStaticPreserveTemplateRowContractOption,
            customStaticPadCompactRigidRowsToTemplateSizeOption,
            customStaticPreserveTemplateMeshVertexCountOption,
            customStaticPreserveTemplateVertexHeaderCountsOption,
            customStaticRewriteTemplateEpilogueRowsOption,
            customStaticRewriteTemplateEpiloguePrefixesOption,
            customStaticRewriteTemplateEpiloguePositionsOption,
            customStaticGenerateTemplateEpilogueControlPrefixOption,
            customStaticClearTemplateEpilogueFinalMarkerOption,
            customStaticGenerateTemplateEpilogueFinalMarkerOption,
            customStaticNeutralizeTemplateSkinningOption,
            customStaticFlattenVertexPrefixesOption,
            customStaticVertexPrefixOption,
            customStaticVertexPrefixShadeOption,
            customStaticAutoVertexPrefixShadeOption,
            customStaticPreserveTemplateVertexControlWordsOption,
            customStaticZeroVertexControlHighBitsOption,
            customStaticPreserveTemplateVertexControlLowBitsOption,
            customStaticVertexControlLow9ValueOption,
            customStaticAutoVertexControlLow9TailOption,
            customStaticVertexControlLow9WarmupZeroCountOption,
            customStaticPreserveTemplateSparseLow9CountOption,
            customStaticPreserveTemplateLow9MaxValueOption,
            customStaticAutoPreserveTemplateLow9MaxValueOption,
            customStaticPreserveDuplicateLow9ValuesOption,
            customStaticPreserveLow9UpToMaxDuplicateOption,
            customStaticIsolatedTriangleTopologyOption,
            customStaticMaxTrianglesPerMeshOption,
            customStaticMaxGeneratedMeshesOption,
            customStaticMaxHighLodMeshesOption,
            customStaticInitialTriangleCapOption,
            customStaticInitialTriangleCountOption,
            customStaticMaterialTextureIdsOption,
            customStaticMaterialUvScalesOption,
            customStaticClampUvsOption,
            customStaticSkipTexCoordVifWriteOption,
            inputOption,
            outputOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var templateFile = parseResult.GetValue(templateOption);
            var profileValue = parseResult.GetValue(profileOption);
            var packetModeValue = parseResult.GetValue(packetModeOption);
            var packetModeMeshesValue = parseResult.GetValue(packetModeMeshesOption);
            var maxInfluences = parseResult.GetValue(maxInfluencesOption);
            var customStatic = parseResult.GetValue(customStaticOption);
            var customStaticGeneratedContainer = parseResult.GetValue(customStaticGeneratedContainerOption);
            var replaceMesh = parseResult.GetValue(replaceMeshOption);
            var customStaticScale = parseResult.GetValue(customStaticScaleOption);
            var customStaticYawDegrees = parseResult.GetValue(customStaticYawDegreesOption);
            var customStaticPitchDegrees = parseResult.GetValue(customStaticPitchDegreesOption);
            var customStaticRollDegrees = parseResult.GetValue(customStaticRollDegreesOption);
            var customStaticPostSkinYawDegrees = parseResult.GetValue(customStaticPostSkinYawDegreesOption);
            var customStaticSplitMeshes = parseResult.GetValue(customStaticSplitMeshesOption);
            var customStaticSplitConnectedComponents = parseResult.GetValue(customStaticSplitConnectedComponentsOption);
            var customStaticSplitConnectedComponentMinTriangles = parseResult.GetValue(customStaticSplitConnectedComponentMinTrianglesOption);
            var customStaticSplitAnatomicalRegions = parseResult.GetValue(customStaticSplitAnatomicalRegionsOption);
            var customStaticSplitSideAxis = parseResult.GetValue(customStaticSplitSideAxisOption);
            var customStaticSplitSideDeadzoneRatio = parseResult.GetValue(customStaticSplitSideDeadzoneRatioOption);
            var customStaticExpandTemplateMeshes = parseResult.GetValue(customStaticExpandTemplateMeshesOption);
            var customStaticUseOnlyReplaceMeshAsTemplate = parseResult.GetValue(customStaticUseOnlyReplaceMeshAsTemplateOption);
            var customStaticUseMinimalExpandedMeshSlots = parseResult.GetValue(customStaticUseMinimalExpandedMeshSlotsOption);
            var customStaticGenerateMeshSlots = parseResult.GetValue(customStaticGenerateMeshSlotsOption);
            var customStaticGenerateMeshTable = parseResult.GetValue(customStaticGenerateMeshTableOption);
            var customStaticGeneratedMeshSlotCapacity = parseResult.GetValue(customStaticGeneratedMeshSlotCapacityOption);
            var customStaticGenerateGlobalScaffold = parseResult.GetValue(customStaticGenerateGlobalScaffoldOption);
            var customStaticGenerateHeaderDefaults = parseResult.GetValue(customStaticGenerateHeaderDefaultsOption);
            var customStaticHeaderLodTrans = parseResult.GetValue(customStaticHeaderLodTransOption);
            var customStaticHeaderMipmapDistance = parseResult.GetValue(customStaticHeaderMipmapDistanceOption);
            var customStaticTextureMetadataDistance = parseResult.GetValue(customStaticTextureMetadataDistanceOption);
            var customStaticProbeMeshesValue = parseResult.GetValue(customStaticProbeMeshesOption);
            var customStaticSkipUnprobedMeshes = parseResult.GetValue(customStaticSkipUnprobedMeshesOption);
            var customStaticForceSkinJointsValue = parseResult.GetValue(customStaticForceSkinJointsOption);
            var customStaticForceSourceTriangleJointsValue = parseResult.GetValue(customStaticForceSourceTriangleJointsOption);
            var outputModelScale = parseResult.GetValue(outputModelScaleOption);
            var customStaticRecalculateBoundingSphere = parseResult.GetValue(customStaticRecalculateBoundingSphereOption);
            var customStaticBoundingSpherePadding = parseResult.GetValue(customStaticBoundingSpherePaddingOption);
            var customStaticPreserveTemplatePackets = parseResult.GetValue(customStaticPreserveTemplatePacketsOption);
            var customStaticPreserveTemplateVertexLayout = parseResult.GetValue(customStaticPreserveTemplateVertexLayoutOption);
            var customStaticHideOtherMeshes = parseResult.GetValue(customStaticHideOtherMeshesOption);
            var customStaticDropTemplateAttachments = parseResult.GetValue(customStaticDropTemplateAttachmentsOption);
            var customStaticDropTemplateNonBodyMeshes = parseResult.GetValue(customStaticDropTemplateNonBodyMeshesOption);
            var customStaticStripTemplateGameplayData = parseResult.GetValue(customStaticStripTemplateGameplayDataOption);
            var customStaticDropTemplateCollision = parseResult.GetValue(customStaticDropTemplateCollisionOption);
            var customStaticDropTemplateAnimations = parseResult.GetValue(customStaticDropTemplateAnimationsOption);
            var customStaticGenerateDefaultAnimation = parseResult.GetValue(customStaticGenerateDefaultAnimationOption);
            var customStaticDropTemplateAnimationJoints = parseResult.GetValue(customStaticDropTemplateAnimationJointsOption);
            var customStaticDropTemplateSounds = parseResult.GetValue(customStaticDropTemplateSoundsOption);
            var customStaticDropTemplateShadow = parseResult.GetValue(customStaticDropTemplateShadowOption);
            var customStaticDropTextures = parseResult.GetValue(customStaticDropTexturesOption);
            var customStaticConstantTextures = parseResult.GetValue(customStaticConstantTexturesOption);
            var customStaticGenerateTextureMetadata = parseResult.GetValue(customStaticGenerateTextureMetadataOption);
            var customStaticUseGeneratedTextureMetadataPrototype = parseResult.GetValue(customStaticUseGeneratedTextureMetadataPrototypeOption);
            var customStaticGenerateMeshEntryMetadata = parseResult.GetValue(customStaticGenerateMeshEntryMetadataOption);
            var customStaticGenerateMeshEntryUnknown0A = parseResult.GetValue(customStaticGenerateMeshEntryUnknown0AOption);
            var customStaticGenerateMeshEntryUnknown0ATotalQw = parseResult.GetValue(customStaticGenerateMeshEntryUnknown0ATotalQwOption);
            var customStaticZeroCommonTransformJoint = parseResult.GetValue(customStaticZeroCommonTransformJointOption);
            var customStaticZeroCommonTransformJointHeaderOnly = parseResult.GetValue(customStaticZeroCommonTransformJointHeaderOnlyOption);
            var customStaticUseDominantSkinJointAsCommonTransform = parseResult.GetValue(customStaticUseDominantSkinJointAsCommonTransformOption);
            var customStaticUseDominantHeadSkinJointAsCommonTransform = parseResult.GetValue(customStaticUseDominantHeadSkinJointAsCommonTransformOption);
            var customStaticUseReferenceMeshCommonTransform = parseResult.GetValue(customStaticUseReferenceMeshCommonTransformOption);
            var customStaticGenerateCommonTransforms = parseResult.GetValue(customStaticGenerateCommonTransformsOption);
            var customStaticGenerateCommonTransformSkeleton = parseResult.GetValue(customStaticGenerateCommonTransformSkeletonOption);
            var customStaticRigSource = parseResult.GetValue(customStaticRigSourceOption);
            var customStaticSkinReference = parseResult.GetValue(customStaticSkinReferenceOption);
            var customStaticTransferReferenceSkinning = parseResult.GetValue(customStaticTransferReferenceSkinningOption);
            var customStaticReferenceSkinningSampleCount = parseResult.GetValue(customStaticReferenceSkinningSampleCountOption);
            var customStaticReferenceSkinningVerticalWindow = parseResult.GetValue(customStaticReferenceSkinningVerticalWindowOption);
            var customStaticReferenceSkinningSameSide = parseResult.GetValue(customStaticReferenceSkinningSameSideOption);
            var customStaticReferenceSkinningSideAxis = parseResult.GetValue(customStaticReferenceSkinningSideAxisOption) ?? "x";
            var customStaticReferenceSkinningSideDeadzoneRatio = parseResult.GetValue(customStaticReferenceSkinningSideDeadzoneRatioOption);
            var customStaticReferenceSkinningMaterialRegions = parseResult.GetValue(customStaticReferenceSkinningMaterialRegionsOption);
            var customStaticReferenceSkinningDisableAnatomicalFilters = parseResult.GetValue(customStaticReferenceSkinningDisableAnatomicalFiltersOption);
            var customStaticReferenceSkinningPreserveLowerBodyFilters = parseResult.GetValue(customStaticReferenceSkinningPreserveLowerBodyFiltersOption);
            var customStaticReferenceSkinningPreserveShoulderFilters = parseResult.GetValue(customStaticReferenceSkinningPreserveShoulderFiltersOption);
            var customStaticReferenceSkinningShoulderInwardBias = parseResult.GetValue(customStaticReferenceSkinningShoulderInwardBiasOption);
            var customStaticReferenceSkinningTriangleCoherent = parseResult.GetValue(customStaticReferenceSkinningTriangleCoherentOption);
            var customStaticReferenceSkinningSplitPrimarySeams = parseResult.GetValue(customStaticReferenceSkinningSplitPrimarySeamsOption);
            var customStaticReferenceSkinningRigidMeshCentroid = parseResult.GetValue(customStaticReferenceSkinningRigidMeshCentroidOption);
            var customStaticReferenceSkinningRigidTriangleCentroid = parseResult.GetValue(customStaticReferenceSkinningRigidTriangleCentroidOption);
            var customStaticReferenceSkinningSmoothPrimaryIterations = parseResult.GetValue(customStaticReferenceSkinningSmoothPrimaryIterationsOption);
            var customStaticReferenceSkinningDistancePower = parseResult.GetValue(customStaticReferenceSkinningDistancePowerOption);
            var customStaticReferenceSkinningYawDegrees = parseResult.GetValue(customStaticReferenceSkinningYawDegreesOption);
            var customStaticApproximateRigSkinning = parseResult.GetValue(customStaticApproximateRigSkinningOption);
            var customStaticApproximateRigSkinningUseSourcePose = parseResult.GetValue(customStaticApproximateRigSkinningUseSourcePoseOption);
            var customStaticWriteFittedRigCommonTransforms = parseResult.GetValue(customStaticWriteFittedRigCommonTransformsOption);
            var customStaticSkinPositionsRelativeToBind = parseResult.GetValue(customStaticSkinPositionsRelativeToBindOption);
            var customStaticCopyRigAnimation0 = parseResult.GetValue(customStaticCopyRigAnimation0Option);
            var customStaticCopyRigAnimation = parseResult.GetValue(customStaticCopyRigAnimationOption);
            var customStaticDoubleSided = parseResult.GetValue(customStaticDoubleSidedOption);
            var customStaticPreserveTopologyTail = parseResult.GetValue(customStaticPreserveTopologyTailOption);
            var customStaticCompactTopologyPacket = parseResult.GetValue(customStaticCompactTopologyPacketOption);
            var customStaticStrictTriangleCap = parseResult.GetValue(customStaticStrictTriangleCapOption);
            var customStaticForceZeroMarkerTopology = parseResult.GetValue(customStaticUseZeroMarkerTopologyOption);
            var customStaticGenerateMinimalVifContainer = parseResult.GetValue(customStaticGenerateMinimalVifContainerOption);
            var customStaticGenerateVifDomainCapacity = parseResult.GetValue(customStaticGenerateVifDomainCapacityOption);
            var customStaticGenerateVertexHeaderDomainCapacity = parseResult.GetValue(customStaticGenerateVertexHeaderDomainCapacityOption);
            var customStaticGenerateMeshTableVertexCount = parseResult.GetValue(customStaticGenerateMeshTableVertexCountOption);
            var customStaticGenerateRigidVertexData = parseResult.GetValue(customStaticGenerateRigidVertexDataOption);
            var customStaticGenerateRigidRowsInTemplateLayout = parseResult.GetValue(customStaticGenerateRigidRowsInTemplateLayoutOption);
            var customStaticGenerateCompactRigidRows = parseResult.GetValue(customStaticGenerateCompactRigidRowsOption);
            var customStaticGenerateCompactVertexHeader = parseResult.GetValue(customStaticGenerateCompactVertexHeaderOption);
            var customStaticPreserveTemplateRowContract = parseResult.GetValue(customStaticPreserveTemplateRowContractOption);
            var customStaticPadCompactRigidRowsToTemplateSize = parseResult.GetValue(customStaticPadCompactRigidRowsToTemplateSizeOption);
            var customStaticPreserveTemplateMeshVertexCount = parseResult.GetValue(customStaticPreserveTemplateMeshVertexCountOption);
            var customStaticPreserveTemplateVertexHeaderCounts = parseResult.GetValue(customStaticPreserveTemplateVertexHeaderCountsOption);
            var customStaticRewriteTemplateEpilogueRows = parseResult.GetValue(customStaticRewriteTemplateEpilogueRowsOption);
            var customStaticRewriteTemplateEpiloguePrefixes = parseResult.GetValue(customStaticRewriteTemplateEpiloguePrefixesOption);
            var customStaticRewriteTemplateEpiloguePositions = parseResult.GetValue(customStaticRewriteTemplateEpiloguePositionsOption);
            var customStaticGenerateTemplateEpilogueControlPrefix = parseResult.GetValue(customStaticGenerateTemplateEpilogueControlPrefixOption);
            var customStaticClearTemplateEpilogueFinalMarker = parseResult.GetValue(customStaticClearTemplateEpilogueFinalMarkerOption);
            var customStaticGenerateTemplateEpilogueFinalMarker = parseResult.GetValue(customStaticGenerateTemplateEpilogueFinalMarkerOption);
            var customStaticNeutralizeTemplateSkinning = parseResult.GetValue(customStaticNeutralizeTemplateSkinningOption);
            var customStaticFlattenVertexPrefixes = parseResult.GetValue(customStaticFlattenVertexPrefixesOption);
            var customStaticVertexPrefixValue = parseResult.GetValue(customStaticVertexPrefixOption);
            var customStaticVertexPrefixShade = parseResult.GetValue(customStaticVertexPrefixShadeOption);
            var customStaticAutoVertexPrefixShade = parseResult.GetValue(customStaticAutoVertexPrefixShadeOption);
            var customStaticPreserveTemplateVertexControlWords = parseResult.GetValue(customStaticPreserveTemplateVertexControlWordsOption);
            var customStaticZeroVertexControlHighBits = parseResult.GetValue(customStaticZeroVertexControlHighBitsOption);
            var customStaticPreserveTemplateVertexControlLowBits = parseResult.GetValue(customStaticPreserveTemplateVertexControlLowBitsOption);
            var customStaticVertexControlLow9Value = parseResult.GetValue(customStaticVertexControlLow9ValueOption);
            var customStaticAutoVertexControlLow9Tail = parseResult.GetValue(customStaticAutoVertexControlLow9TailOption);
            var customStaticVertexControlLow9WarmupZeroCount = parseResult.GetValue(customStaticVertexControlLow9WarmupZeroCountOption);
            var customStaticPreserveTemplateSparseLow9Count = parseResult.GetValue(customStaticPreserveTemplateSparseLow9CountOption);
            var customStaticPreserveTemplateLow9MaxValue = parseResult.GetValue(customStaticPreserveTemplateLow9MaxValueOption);
            var customStaticAutoPreserveTemplateLow9MaxValue = parseResult.GetValue(customStaticAutoPreserveTemplateLow9MaxValueOption);
            var customStaticPreserveDuplicateLow9Values = parseResult.GetValue(customStaticPreserveDuplicateLow9ValuesOption);
            var customStaticPreserveLow9UpToMaxDuplicate = parseResult.GetValue(customStaticPreserveLow9UpToMaxDuplicateOption);
            var customStaticIsolatedTriangleTopology = parseResult.GetValue(customStaticIsolatedTriangleTopologyOption);
            var customStaticMaxTrianglesPerMesh = parseResult.GetValue(customStaticMaxTrianglesPerMeshOption);
            var customStaticMaxGeneratedMeshes = parseResult.GetValue(customStaticMaxGeneratedMeshesOption);
            var customStaticMaxHighLodMeshes = parseResult.GetValue(customStaticMaxHighLodMeshesOption);
            var customStaticInitialTriangleCap = parseResult.GetValue(customStaticInitialTriangleCapOption);
            var customStaticInitialTriangleCount = parseResult.GetValue(customStaticInitialTriangleCountOption);
            var customStaticMaterialTextureIdsValue = parseResult.GetValue(customStaticMaterialTextureIdsOption);
            var customStaticMaterialUvScalesValue = parseResult.GetValue(customStaticMaterialUvScalesOption);
            var customStaticClampUvs = parseResult.GetValue(customStaticClampUvsOption);
            var customStaticSkipTexCoordVifWrite = parseResult.GetValue(customStaticSkipTexCoordVifWriteOption);
            var inputFile = parseResult.GetValue(inputOption);
            var outputFile = parseResult.GetValue(outputOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected UYA or DL for glTF import.");
                return;
            }

            if (!TryParseImportProfile(profileValue, out var importProfile))
            {
                parseResult.GetResult(profileOption)?.AddError(
                    $"Unsupported --profile value '{profileValue}'. Expected template, template-shape, custom-static-world, or custom-static-player.");
                return;
            }

            if (!TryParseMeshIndices(packetModeMeshesValue, out var packetModeMeshIndices, out var meshIndicesError))
            {
                parseResult.GetResult(packetModeMeshesOption)?.AddError(meshIndicesError);
                return;
            }

            if (!TryParseMeshIndices(customStaticProbeMeshesValue, out var customStaticProbeMeshIndices, out var probeMeshesError))
            {
                parseResult.GetResult(customStaticProbeMeshesOption)?.AddError(probeMeshesError);
                return;
            }

            if (!TryParseForcedSkinJoints(customStaticForceSkinJointsValue, out var customStaticForcedSkinJoints, out var forcedSkinJointsError))
            {
                parseResult.GetResult(customStaticForceSkinJointsOption)?.AddError(forcedSkinJointsError);
                return;
            }

            if (!TryParseForcedSourceTriangleSkinJoints(customStaticForceSourceTriangleJointsValue, out var customStaticForcedSourceTriangleSkinJoints, out var forcedSourceTriangleSkinJointsError))
            {
                parseResult.GetResult(customStaticForceSourceTriangleJointsOption)?.AddError(forcedSourceTriangleSkinJointsError);
                return;
            }

            if (!TryParseMaterialTextureIds(customStaticMaterialTextureIdsValue, out var customStaticMaterialTextureIds, out var materialTextureIdsError))
            {
                parseResult.GetResult(customStaticMaterialTextureIdsOption)?.AddError(materialTextureIdsError);
                return;
            }

            if (!TryParseMaterialUvScales(customStaticMaterialUvScalesValue, out var customStaticMaterialUvScales, out var materialUvScalesError))
            {
                parseResult.GetResult(customStaticMaterialUvScalesOption)?.AddError(materialUvScalesError);
                return;
            }

            if (!TryParseVertexPrefix(customStaticVertexPrefixValue, out var customStaticVertexPrefixBytes, out var vertexPrefixError))
            {
                parseResult.GetResult(customStaticVertexPrefixOption)?.AddError(vertexPrefixError);
                return;
            }

            if (!TryParsePacketMode(packetModeValue, out var packetMode))
            {
                parseResult.GetResult(packetModeOption)?.AddError(
                    $"Unsupported --packet-mode value '{packetModeValue}'. Expected auto, passthrough, generate-topology, generate-vertex-positions, generate-vertex-data-from-metadata, generate-topology-from-metadata-shape, generate-vertex-data-with-metadata-shape, generate-vertex-data, or generate-all.");
                return;
            }

            if (importProfile == MobyImportProfile.Template
                && !WasProvided(parseResult, profileOption)
                && customStatic)
            {
                importProfile = MobyImportProfile.CustomStaticWorld;
            }

            switch (importProfile)
            {
                case MobyImportProfile.TemplateShape:
                    if (!WasProvided(parseResult, packetModeOption))
                    {
                        packetMode = MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape;
                    }
                    break;
                case MobyImportProfile.CustomStaticWorld:
                    customStatic = ProfileDefault(parseResult, customStaticOption, customStatic, true);
                    maxInfluences = ProfileDefault(parseResult, maxInfluencesOption, maxInfluences, 1);
                    customStaticSplitMeshes = ProfileDefault(parseResult, customStaticSplitMeshesOption, customStaticSplitMeshes, true);
                    customStaticGenerateMeshTable = ProfileDefault(parseResult, customStaticGenerateMeshTableOption, customStaticGenerateMeshTable, true);
                    customStaticGenerateGlobalScaffold = ProfileDefault(parseResult, customStaticGenerateGlobalScaffoldOption, customStaticGenerateGlobalScaffold, true);
                    customStaticGenerateHeaderDefaults = ProfileDefault(parseResult, customStaticGenerateHeaderDefaultsOption, customStaticGenerateHeaderDefaults, true);
                    customStaticGenerateCommonTransforms = ProfileDefault(parseResult, customStaticGenerateCommonTransformsOption, customStaticGenerateCommonTransforms, true);
                    customStaticRecalculateBoundingSphere = ProfileDefault(parseResult, customStaticRecalculateBoundingSphereOption, customStaticRecalculateBoundingSphere, true);
                    customStaticReferenceSkinningSampleCount = ProfileDefault(parseResult, customStaticReferenceSkinningSampleCountOption, customStaticReferenceSkinningSampleCount, 1);
                    if (!WasProvided(parseResult, packetModeOption))
                    {
                        packetMode = MobyGltfImportPacketMode.GenerateAll;
                    }
                    break;
                case MobyImportProfile.CustomStaticPlayer:
                    customStatic = ProfileDefault(parseResult, customStaticOption, customStatic, true);
                    maxInfluences = ProfileDefault(parseResult, maxInfluencesOption, maxInfluences, 1);
                    customStaticGenerateMeshTable = ProfileDefault(parseResult, customStaticGenerateMeshTableOption, customStaticGenerateMeshTable, false);
                    customStaticGenerateGlobalScaffold = ProfileDefault(parseResult, customStaticGenerateGlobalScaffoldOption, customStaticGenerateGlobalScaffold, false);
                    customStaticGenerateHeaderDefaults = ProfileDefault(parseResult, customStaticGenerateHeaderDefaultsOption, customStaticGenerateHeaderDefaults, false);
                    customStaticGenerateTextureMetadata = ProfileDefault(parseResult, customStaticGenerateTextureMetadataOption, customStaticGenerateTextureMetadata, false);
                    customStaticUseGeneratedTextureMetadataPrototype = ProfileDefault(parseResult, customStaticUseGeneratedTextureMetadataPrototypeOption, customStaticUseGeneratedTextureMetadataPrototype, false);
                    customStaticGenerateMeshEntryMetadata = ProfileDefault(parseResult, customStaticGenerateMeshEntryMetadataOption, customStaticGenerateMeshEntryMetadata, false);
                    customStaticGenerateCommonTransforms = ProfileDefault(parseResult, customStaticGenerateCommonTransformsOption, customStaticGenerateCommonTransforms, false);
                    customStaticRecalculateBoundingSphere = ProfileDefault(parseResult, customStaticRecalculateBoundingSphereOption, customStaticRecalculateBoundingSphere, false);
                    customStaticSkipUnprobedMeshes = ProfileDefault(parseResult, customStaticSkipUnprobedMeshesOption, customStaticSkipUnprobedMeshes, true);
                    customStaticSkipTexCoordVifWrite = ProfileDefault(parseResult, customStaticSkipTexCoordVifWriteOption, customStaticSkipTexCoordVifWrite, true);
                    customStaticReferenceSkinningSampleCount = ProfileDefault(parseResult, customStaticReferenceSkinningSampleCountOption, customStaticReferenceSkinningSampleCount, 1);
                    customStaticReferenceSkinningSameSide = ProfileDefault(parseResult, customStaticReferenceSkinningSameSideOption, customStaticReferenceSkinningSameSide, true);
                    customStaticReferenceSkinningSideAxis = ProfileDefault(parseResult, customStaticReferenceSkinningSideAxisOption, customStaticReferenceSkinningSideAxis, "z");
                    customStaticReferenceSkinningDisableAnatomicalFilters = ProfileDefault(parseResult, customStaticReferenceSkinningDisableAnatomicalFiltersOption, customStaticReferenceSkinningDisableAnatomicalFilters, true);
                    customStaticReferenceSkinningPreserveLowerBodyFilters = ProfileDefault(parseResult, customStaticReferenceSkinningPreserveLowerBodyFiltersOption, customStaticReferenceSkinningPreserveLowerBodyFilters, true);
                    customStaticReferenceSkinningPreserveShoulderFilters = ProfileDefault(parseResult, customStaticReferenceSkinningPreserveShoulderFiltersOption, customStaticReferenceSkinningPreserveShoulderFilters, true);
                    customStaticReferenceSkinningShoulderInwardBias = ProfileDefault(parseResult, customStaticReferenceSkinningShoulderInwardBiasOption, customStaticReferenceSkinningShoulderInwardBias, 0.16f);
                    customStaticReferenceSkinningTriangleCoherent = ProfileDefault(parseResult, customStaticReferenceSkinningTriangleCoherentOption, customStaticReferenceSkinningTriangleCoherent, true);
                    if (customStaticSkinReference is not null)
                    {
                        customStaticTransferReferenceSkinning = ProfileDefault(parseResult, customStaticTransferReferenceSkinningOption, customStaticTransferReferenceSkinning, true);
                    }
                    if (!WasProvided(parseResult, packetModeOption))
                    {
                        packetMode = MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape;
                    }
                    break;
            }

            if (gameId is not (GameId.UYA or GameId.DL))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Moby glTF import currently supports only UYA and DL. Received {gameId}.");
                return;
            }

            if (maxInfluences < 1 || maxInfluences > 3)
            {
                parseResult.GetResult(maxInfluencesOption)?.AddError("--max-influences must be between 1 and 3.");
                return;
            }

            if (customStaticReferenceSkinningSampleCount < 1 || customStaticReferenceSkinningSampleCount > 16)
            {
                parseResult.GetResult(customStaticReferenceSkinningSampleCountOption)?.AddError(
                    "--custom-static-reference-skinning-sample-count must be between 1 and 16.");
                return;
            }
            if (customStaticReferenceSkinningDistancePower <= 0f || !float.IsFinite(customStaticReferenceSkinningDistancePower))
            {
                parseResult.GetResult(customStaticReferenceSkinningDistancePowerOption)?.AddError(
                    "--custom-static-reference-skinning-distance-power must be greater than 0.");
                return;
            }
            if (customStaticReferenceSkinningSmoothPrimaryIterations < 0 || customStaticReferenceSkinningSmoothPrimaryIterations > 16)
            {
                parseResult.GetResult(customStaticReferenceSkinningSmoothPrimaryIterationsOption)?.AddError(
                    "--custom-static-reference-skinning-smooth-primary-iterations must be between 0 and 16.");
                return;
            }
            if (!string.Equals(customStaticReferenceSkinningSideAxis, "x", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(customStaticReferenceSkinningSideAxis, "z", StringComparison.OrdinalIgnoreCase))
            {
                parseResult.GetResult(customStaticReferenceSkinningSideAxisOption)?.AddError(
                    "--custom-static-reference-skinning-side-axis must be x or z.");
                return;
            }
            if (customStaticReferenceSkinningSideDeadzoneRatio < 0f || !float.IsFinite(customStaticReferenceSkinningSideDeadzoneRatio))
            {
                parseResult.GetResult(customStaticReferenceSkinningSideDeadzoneRatioOption)?.AddError(
                    "--custom-static-reference-skinning-side-deadzone-ratio must be zero or greater.");
                return;
            }
            if (!string.IsNullOrWhiteSpace(customStaticSplitSideAxis)
                && !string.Equals(customStaticSplitSideAxis, "x", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(customStaticSplitSideAxis, "z", StringComparison.OrdinalIgnoreCase))
            {
                parseResult.GetResult(customStaticSplitSideAxisOption)?.AddError(
                    "--custom-static-split-side-axis must be x or z.");
                return;
            }
            if (customStaticSplitConnectedComponentMinTriangles < 0)
            {
                parseResult.GetResult(customStaticSplitConnectedComponentMinTrianglesOption)?.AddError(
                    "--custom-static-split-connected-component-min-triangles must be zero or greater.");
                return;
            }
            if (customStaticSplitSideDeadzoneRatio < 0f || !float.IsFinite(customStaticSplitSideDeadzoneRatio))
            {
                parseResult.GetResult(customStaticSplitSideDeadzoneRatioOption)?.AddError(
                    "--custom-static-split-side-deadzone-ratio must be zero or greater.");
                return;
            }

            if (customStaticGeneratedContainer && !customStatic)
            {
                parseResult.GetResult(customStaticGeneratedContainerOption)?.AddError(
                    "--custom-static-generated-container requires --custom-static.");
                return;
            }

            if (!customStaticGeneratedContainer && templateFile is null)
            {
                parseResult.GetResult(templateOption)?.AddError("Missing required --template option.");
                return;
            }

            if ((customStaticApproximateRigSkinning || customStaticCopyRigAnimation0 || customStaticCopyRigAnimation is not null) && customStaticRigSource is null)
            {
                parseResult.GetResult(customStaticRigSourceOption)?.AddError(
                    "--custom-static-rig-source is required when copying rig animation or approximating rig skinning.");
                return;
            }

            if (customStaticCopyRigAnimation is < 0)
            {
                parseResult.GetResult(customStaticCopyRigAnimationOption)?.AddError(
                    "--custom-static-copy-rig-animation must be zero or greater.");
                return;
            }

            if (customStaticTransferReferenceSkinning && customStaticSkinReference is null)
            {
                parseResult.GetResult(customStaticSkinReferenceOption)?.AddError(
                    "--custom-static-skin-reference is required when transferring reference skinning.");
                return;
            }

            if (inputFile is null)
            {
                parseResult.GetResult(inputOption)?.AddError("Missing required --input option.");
                return;
            }

            if (outputFile is null)
            {
                parseResult.GetResult(outputOption)?.AddError("Missing required --output option.");
                return;
            }

            if (templateFile is not null && !templateFile.Exists)
            {
                parseResult.GetResult(templateOption)?.AddError(
                    $"Template file '{templateFile.FullName}' does not exist.");
                return;
            }

            if (customStaticRigSource is not null && !customStaticRigSource.Exists)
            {
                parseResult.GetResult(customStaticRigSourceOption)?.AddError(
                    $"Rig source file '{customStaticRigSource.FullName}' does not exist.");
                return;
            }

            if (customStaticSkinReference is not null && !customStaticSkinReference.Exists)
            {
                parseResult.GetResult(customStaticSkinReferenceOption)?.AddError(
                    $"Skin reference file '{customStaticSkinReference.FullName}' does not exist.");
                return;
            }

            if (!inputFile.Exists)
            {
                parseResult.GetResult(inputOption)?.AddError(
                    $"Input file '{inputFile.FullName}' does not exist.");
                return;
            }

            using var templateStream = templateFile?.OpenRead() ?? Stream.Null;
            using var rigSourceStream = customStaticRigSource?.OpenRead();
            using var skinReferenceStream = customStaticSkinReference?.OpenRead();
            using var gltfStream = inputFile.OpenRead();
            var inputDirectory = inputFile.DirectoryName ?? Directory.GetCurrentDirectory();
            var result = MobyGltfImporter.ImportWithDiagnostics(
                templateStream,
                gltfStream,
                bufferName => File.OpenRead(Path.Combine(inputDirectory, Uri.UnescapeDataString(bufferName))),
                new MobyGltfImportOptions
                {
                    AnimationFormat = MobyGameFormats.Resolve(gameModuleResolver, gameId),
                    MaxInfluences = maxInfluences,
                    PacketMode = packetMode,
                    PacketModeMeshIndices = packetModeMeshIndices,
                    CustomStatic = customStatic,
                    CustomStaticUseGeneratedContainer = customStaticGeneratedContainer,
                    CustomStaticReplaceMeshIndex = replaceMesh,
                    CustomStaticScale = customStaticScale,
                    CustomStaticYawDegrees = customStaticYawDegrees,
                    CustomStaticPitchDegrees = customStaticPitchDegrees,
                    CustomStaticRollDegrees = customStaticRollDegrees,
                    CustomStaticPostSkinYawDegrees = customStaticPostSkinYawDegrees,
                    CustomStaticSplitMeshes = customStaticSplitMeshes,
                    CustomStaticSplitConnectedComponents = customStaticSplitConnectedComponents,
                    CustomStaticSplitConnectedComponentMinTriangles = customStaticSplitConnectedComponentMinTriangles,
                    CustomStaticSplitAnatomicalRegions = customStaticSplitAnatomicalRegions,
                    CustomStaticSplitSideAxis = customStaticSplitSideAxis,
                    CustomStaticSplitSideDeadzoneRatio = customStaticSplitSideDeadzoneRatio,
                    CustomStaticExpandTemplateMeshes = customStaticExpandTemplateMeshes,
                    CustomStaticUseOnlyReplaceMeshAsTemplate = customStaticUseOnlyReplaceMeshAsTemplate,
                    CustomStaticUseMinimalExpandedMeshSlots = customStaticUseMinimalExpandedMeshSlots,
                    CustomStaticGenerateMeshSlots = customStaticGenerateMeshSlots,
                    CustomStaticGenerateMeshTable = customStaticGenerateMeshTable,
                    CustomStaticGeneratedMeshSlotCapacity = customStaticGeneratedMeshSlotCapacity,
                    CustomStaticGenerateGlobalScaffold = customStaticGenerateGlobalScaffold,
                    CustomStaticGenerateHeaderDefaults = customStaticGenerateHeaderDefaults,
                    CustomStaticHeaderLodTrans = customStaticHeaderLodTrans,
                    CustomStaticHeaderMipmapDistance = customStaticHeaderMipmapDistance,
                    CustomStaticTextureMetadataDistance = customStaticTextureMetadataDistance,
                    CustomStaticProbeMeshIndices = customStaticProbeMeshIndices,
                    CustomStaticSkipUnprobedMeshes = customStaticSkipUnprobedMeshes,
                    CustomStaticForcedSkinJointsByMeshIndex = customStaticForcedSkinJoints,
                    CustomStaticForcedSourceTriangleSkinJoints = customStaticForcedSourceTriangleSkinJoints,
                    OutputModelScale = outputModelScale,
                    CustomStaticRecalculateBoundingSphere = customStaticRecalculateBoundingSphere,
                    CustomStaticBoundingSpherePadding = customStaticBoundingSpherePadding,
                    CustomStaticPreserveTemplatePackets = customStaticPreserveTemplatePackets,
                    CustomStaticPreserveTemplateVertexLayout = customStaticPreserveTemplateVertexLayout,
                    CustomStaticHideOtherMeshes = customStaticHideOtherMeshes,
                    CustomStaticDropTemplateAttachments = customStaticDropTemplateAttachments,
                    CustomStaticDropTemplateNonBodyMeshes = customStaticDropTemplateNonBodyMeshes,
                    CustomStaticStripTemplateGameplayData = customStaticStripTemplateGameplayData,
                    CustomStaticDropTemplateCollision = customStaticDropTemplateCollision,
                    CustomStaticDropTemplateAnimations = customStaticDropTemplateAnimations,
                    CustomStaticGenerateDefaultAnimation = customStaticGenerateDefaultAnimation,
                    CustomStaticDropTemplateAnimationJoints = customStaticDropTemplateAnimationJoints,
                    CustomStaticDropTemplateSounds = customStaticDropTemplateSounds,
                    CustomStaticDropTemplateShadow = customStaticDropTemplateShadow,
                    CustomStaticDropTextures = customStaticDropTextures,
                    CustomStaticConstantTextures = customStaticConstantTextures,
                    CustomStaticGenerateTextureMetadata = customStaticGenerateTextureMetadata,
                    CustomStaticUseGeneratedTextureMetadataPrototype = customStaticUseGeneratedTextureMetadataPrototype,
                    CustomStaticGenerateMeshEntryMetadata = customStaticGenerateMeshEntryMetadata,
                    CustomStaticGenerateMeshEntryUnknown0A = customStaticGenerateMeshEntryUnknown0A,
                    CustomStaticGenerateMeshEntryUnknown0ATotalQw = customStaticGenerateMeshEntryUnknown0ATotalQw,
                    CustomStaticZeroCommonTransformJoint = customStaticZeroCommonTransformJoint,
                    CustomStaticZeroCommonTransformJointHeaderOnly = customStaticZeroCommonTransformJointHeaderOnly,
                    CustomStaticUseDominantSkinJointAsCommonTransform = customStaticUseDominantSkinJointAsCommonTransform,
                    CustomStaticUseDominantHeadSkinJointAsCommonTransform = customStaticUseDominantHeadSkinJointAsCommonTransform,
                    CustomStaticUseReferenceMeshCommonTransform = customStaticUseReferenceMeshCommonTransform,
                    CustomStaticGenerateCommonTransforms = customStaticGenerateCommonTransforms,
                    CustomStaticGenerateCommonTransformSkeleton = customStaticGenerateCommonTransformSkeleton,
                    CustomStaticTransferReferenceSkinning = customStaticTransferReferenceSkinning,
                    CustomStaticReferenceSkinningSampleCount = customStaticReferenceSkinningSampleCount,
                    CustomStaticReferenceSkinningVerticalWindow = customStaticReferenceSkinningVerticalWindow,
                    CustomStaticReferenceSkinningSameSide = customStaticReferenceSkinningSameSide,
                    CustomStaticReferenceSkinningSideAxis = customStaticReferenceSkinningSideAxis,
                    CustomStaticReferenceSkinningSideDeadzoneRatio = customStaticReferenceSkinningSideDeadzoneRatio,
                    CustomStaticReferenceSkinningMaterialRegions = customStaticReferenceSkinningMaterialRegions,
                    CustomStaticReferenceSkinningDisableAnatomicalFilters = customStaticReferenceSkinningDisableAnatomicalFilters,
                    CustomStaticReferenceSkinningPreserveLowerBodyFilters = customStaticReferenceSkinningPreserveLowerBodyFilters,
                    CustomStaticReferenceSkinningPreserveShoulderFilters = customStaticReferenceSkinningPreserveShoulderFilters,
                    CustomStaticReferenceSkinningShoulderInwardBias = customStaticReferenceSkinningShoulderInwardBias,
                    CustomStaticReferenceSkinningTriangleCoherent = customStaticReferenceSkinningTriangleCoherent,
                    CustomStaticReferenceSkinningSplitPrimarySeams = customStaticReferenceSkinningSplitPrimarySeams,
                    CustomStaticReferenceSkinningRigidMeshCentroid = customStaticReferenceSkinningRigidMeshCentroid,
                    CustomStaticReferenceSkinningRigidTriangleCentroid = customStaticReferenceSkinningRigidTriangleCentroid,
                    CustomStaticReferenceSkinningSmoothPrimaryIterations = customStaticReferenceSkinningSmoothPrimaryIterations,
                    CustomStaticReferenceSkinningDistancePower = customStaticReferenceSkinningDistancePower,
                    CustomStaticReferenceSkinningYawDegrees = customStaticReferenceSkinningYawDegrees,
                    CustomStaticApproximateRigSkinning = customStaticApproximateRigSkinning,
                    CustomStaticApproximateRigSkinningUseSourcePose = customStaticApproximateRigSkinningUseSourcePose,
                    CustomStaticWriteFittedRigCommonTransforms = customStaticWriteFittedRigCommonTransforms,
                    CustomStaticSkinPositionsRelativeToBind = customStaticSkinPositionsRelativeToBind,
                    CustomStaticCopyRigAnimation0 = customStaticCopyRigAnimation0,
                    CustomStaticCopyRigAnimationIndex = customStaticCopyRigAnimation,
                    CustomStaticDoubleSided = customStaticDoubleSided,
                    CustomStaticPreserveTopologyTail = customStaticPreserveTopologyTail,
                    CustomStaticCompactTopologyPacket = customStaticCompactTopologyPacket,
                    CustomStaticStrictTriangleCap = customStaticStrictTriangleCap,
                    CustomStaticForceZeroMarkerTopology = customStaticForceZeroMarkerTopology,
                    CustomStaticGenerateMinimalVifContainer = customStaticGenerateMinimalVifContainer,
                    CustomStaticGenerateVifDomainCapacity = customStaticGenerateVifDomainCapacity,
                    CustomStaticGenerateVertexHeaderDomainCapacity = customStaticGenerateVertexHeaderDomainCapacity,
                    CustomStaticGenerateMeshTableVertexCount = customStaticGenerateMeshTableVertexCount,
                    CustomStaticGenerateRigidVertexData = customStaticGenerateRigidVertexData,
                    CustomStaticGenerateRigidRowsInTemplateLayout = customStaticGenerateRigidRowsInTemplateLayout,
                    CustomStaticGenerateCompactRigidRows = customStaticGenerateCompactRigidRows,
                    CustomStaticGenerateCompactVertexHeader = customStaticGenerateCompactVertexHeader,
                    CustomStaticPreserveTemplateRowContract = customStaticPreserveTemplateRowContract,
                    CustomStaticPadCompactRigidRowsToTemplateSize = customStaticPadCompactRigidRowsToTemplateSize,
                    CustomStaticPreserveTemplateMeshVertexCount = customStaticPreserveTemplateMeshVertexCount,
                    CustomStaticPreserveTemplateVertexHeaderCounts = customStaticPreserveTemplateVertexHeaderCounts,
                    CustomStaticRewriteTemplateEpilogueRows = customStaticRewriteTemplateEpilogueRows,
                    CustomStaticRewriteTemplateEpiloguePrefixes = customStaticRewriteTemplateEpiloguePrefixes,
                    CustomStaticRewriteTemplateEpiloguePositions = customStaticRewriteTemplateEpiloguePositions,
                    CustomStaticGenerateTemplateEpilogueControlPrefix = customStaticGenerateTemplateEpilogueControlPrefix,
                    CustomStaticClearTemplateEpilogueFinalMarker = customStaticClearTemplateEpilogueFinalMarker,
                    CustomStaticGenerateTemplateEpilogueFinalMarker = customStaticGenerateTemplateEpilogueFinalMarker,
                    CustomStaticNeutralizeTemplateSkinning = customStaticNeutralizeTemplateSkinning,
                    CustomStaticFlattenVertexPrefixes = customStaticFlattenVertexPrefixes,
                    CustomStaticVertexPrefixBytes = customStaticVertexPrefixBytes,
                    CustomStaticVertexPrefixShade = customStaticVertexPrefixShade,
                    CustomStaticAutoVertexPrefixShade = customStaticAutoVertexPrefixShade,
                    CustomStaticPreserveTemplateVertexControlWords = customStaticPreserveTemplateVertexControlWords,
                    CustomStaticZeroVertexControlHighBits = customStaticZeroVertexControlHighBits,
                    CustomStaticPreserveTemplateVertexControlLowBits = customStaticPreserveTemplateVertexControlLowBits,
                    CustomStaticVertexControlLow9Value = customStaticVertexControlLow9Value,
                    CustomStaticAutoVertexControlLow9Tail = customStaticAutoVertexControlLow9Tail,
                    CustomStaticVertexControlLow9WarmupZeroCount = customStaticVertexControlLow9WarmupZeroCount,
                    CustomStaticPreserveTemplateSparseLow9Count = customStaticPreserveTemplateSparseLow9Count,
                    CustomStaticPreserveTemplateLow9MaxValue = customStaticPreserveTemplateLow9MaxValue,
                    CustomStaticAutoPreserveTemplateLow9MaxValue = customStaticAutoPreserveTemplateLow9MaxValue,
                    CustomStaticPreserveDuplicateLow9Values = customStaticPreserveDuplicateLow9Values,
                    CustomStaticPreserveLow9UpToMaxDuplicate = customStaticPreserveLow9UpToMaxDuplicate,
                    CustomStaticIsolatedTriangleTopology = customStaticIsolatedTriangleTopology,
                    CustomStaticMaxTrianglesPerMesh = customStaticMaxTrianglesPerMesh,
                    CustomStaticMaxGeneratedMeshes = customStaticMaxGeneratedMeshes,
                    CustomStaticMaxHighLodMeshes = customStaticMaxHighLodMeshes,
                    CustomStaticInitialTriangleCap = customStaticInitialTriangleCap,
                    CustomStaticInitialTriangleCount = customStaticInitialTriangleCount,
                    CustomStaticMaterialTextureIds = customStaticMaterialTextureIds,
                    CustomStaticMaterialUvScales = customStaticMaterialUvScales,
                    CustomStaticClampUvs = customStaticClampUvs,
                    CustomStaticSkipTexCoordVifWrite = customStaticSkipTexCoordVifWrite
                },
                rigSourceStream,
                skinReferenceStream);
            if (gameId == GameId.DL)
            {
                gltfStream.Position = 0;
                DlMobyGltfImporter.ApplyAnimations(
                    result.Model,
                    gltfStream,
                    bufferName => File.OpenRead(Path.Combine(inputDirectory, Uri.UnescapeDataString(bufferName))));
            }

            var bytes = MobyModelPacker.Build(result.Model);

            outputFile.Directory?.Create();
            File.WriteAllBytes(outputFile.FullName, bytes);

            var diagnosticsFile = Path.Combine(
                outputFile.DirectoryName ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(outputFile.Name)}.diagnostics.json");
            File.WriteAllBytes(diagnosticsFile, result.DiagnosticsBytes);

            Console.WriteLine(
                customStaticGeneratedContainer
                    ? $"Imported {gameId} moby glTF '{inputFile.FullName}' into a generated static container and wrote '{outputFile.FullName}' ({bytes.Length} bytes)."
                    : $"Imported {gameId} moby glTF '{inputFile.FullName}' into template '{templateFile!.FullName}' and wrote '{outputFile.FullName}' ({bytes.Length} bytes).");
        });

        return command;
    }

    private enum MobyImportProfile
    {
        Template,
        TemplateShape,
        CustomStaticWorld,
        CustomStaticPlayer
    }

    private static void HideOptions(params Option[] options)
    {
        foreach (var option in options)
        {
            option.Hidden = true;
        }
    }

    private static T ProfileDefault<T>(ParseResult parseResult, Option<T> option, T currentValue, T profileValue)
    {
        return WasProvided(parseResult, option) ? currentValue : profileValue;
    }

    private static bool WasProvided(ParseResult parseResult, Option option)
    {
        return parseResult.GetResult(option) is { Implicit: false };
    }

    private static bool TryParseImportProfile(string? value, out MobyImportProfile profile)
    {
        switch ((value ?? "template").Trim().ToLowerInvariant())
        {
            case "":
            case "template":
            case "default":
                profile = MobyImportProfile.Template;
                return true;
            case "template-shape":
            case "metadata-shape":
                profile = MobyImportProfile.TemplateShape;
                return true;
            case "custom-static-world":
            case "static-world":
            case "world":
                profile = MobyImportProfile.CustomStaticWorld;
                return true;
            case "custom-static-player":
            case "static-player":
            case "player":
                profile = MobyImportProfile.CustomStaticPlayer;
                return true;
            default:
                profile = default;
                return false;
        }
    }

    private static bool TryParsePacketMode(string? value, out MobyGltfImportPacketMode packetMode)
    {
        switch ((value ?? "auto").Trim().ToLowerInvariant())
        {
            case "auto":
                packetMode = MobyGltfImportPacketMode.Auto;
                return true;
            case "passthrough":
                packetMode = MobyGltfImportPacketMode.Passthrough;
                return true;
            case "generate-topology":
                packetMode = MobyGltfImportPacketMode.GenerateTopology;
                return true;
            case "generate-vertex-positions":
                packetMode = MobyGltfImportPacketMode.GenerateVertexPositions;
                return true;
            case "generate-vertex-data-from-metadata":
                packetMode = MobyGltfImportPacketMode.GenerateVertexDataFromMetadata;
                return true;
            case "generate-topology-from-metadata-shape":
                packetMode = MobyGltfImportPacketMode.GenerateTopologyFromMetadataShape;
                return true;
            case "generate-vertex-data-with-metadata-shape":
                packetMode = MobyGltfImportPacketMode.GenerateVertexDataWithMetadataShape;
                return true;
            case "generate-vertex-data":
                packetMode = MobyGltfImportPacketMode.GenerateVertexData;
                return true;
            case "generate-all":
                packetMode = MobyGltfImportPacketMode.GenerateAll;
                return true;
            default:
                packetMode = default;
                return false;
        }
    }

    private static bool TryParseForcedSkinJoints(
        string? value,
        out IReadOnlyDictionary<int, ushort>? forcedSkinJointsByMeshIndex,
        out string error)
    {
        forcedSkinJointsByMeshIndex = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var result = new SortedDictionary<int, ushort>();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || string.IsNullOrWhiteSpace(pieces[0]))
            {
                error = $"Invalid forced skin joint mapping '{part}'. Expected mesh-list=joint.";
                return false;
            }

            if (!ushort.TryParse(pieces[1], out var joint))
            {
                error = $"Invalid forced skin joint '{pieces[1]}' in '{part}'.";
                return false;
            }

            if (!TryParseMeshIndices(pieces[0], out var meshIndices, out _)
                || meshIndices is null
                || meshIndices.Count == 0)
            {
                error = $"Invalid forced skin mesh list '{pieces[0]}' in '{part}'.";
                return false;
            }

            foreach (var meshIndex in meshIndices)
            {
                result[meshIndex] = joint;
            }
        }

        forcedSkinJointsByMeshIndex = result;
        return true;
    }

    private static bool TryParseForcedSourceTriangleSkinJoints(
        string? value,
        out IReadOnlyList<MobyGltfSourceTriangleSkinJoint>? forcedSourceTriangleSkinJoints,
        out string error)
    {
        forcedSourceTriangleSkinJoints = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var result = new List<MobyGltfSourceTriangleSkinJoint>();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || string.IsNullOrWhiteSpace(pieces[0]))
            {
                error = $"Invalid forced source triangle joint mapping '{part}'. Expected mesh:primitive:triangle-list=joint.";
                return false;
            }

            if (!ushort.TryParse(pieces[1], out var joint))
            {
                error = $"Invalid forced source triangle joint '{pieces[1]}' in '{part}'.";
                return false;
            }

            var targetPieces = pieces[0].Split(':', StringSplitOptions.TrimEntries);
            if (targetPieces.Length != 3
                || !TryParseNonNegativeInt(targetPieces[0], out var meshIndex)
                || !TryParseNonNegativeInt(targetPieces[1], out var primitiveIndex)
                || !TryParseMeshIndices(targetPieces[2], out var triangleIndices, out _)
                || triangleIndices is null
                || triangleIndices.Count == 0)
            {
                error = $"Invalid forced source triangle target '{pieces[0]}' in '{part}'.";
                return false;
            }

            result.Add(new MobyGltfSourceTriangleSkinJoint(meshIndex, primitiveIndex, triangleIndices, joint));
        }

        forcedSourceTriangleSkinJoints = result;
        return true;
    }

    private static bool TryParseMeshIndices(string? value, out IReadOnlySet<int>? meshIndices, out string error)
    {
        meshIndices = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var result = new SortedSet<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rangeParts = part.Split('-', StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 1)
            {
                if (!TryParseNonNegativeInt(rangeParts[0], out var index))
                {
                    error = $"Invalid mesh index '{part}' in --packet-mode-meshes.";
                    return false;
                }

                result.Add(index);
                continue;
            }

            if (rangeParts.Length != 2
                || !TryParseNonNegativeInt(rangeParts[0], out var start)
                || !TryParseNonNegativeInt(rangeParts[1], out var end)
                || end < start)
            {
                error = $"Invalid mesh range '{part}' in --packet-mode-meshes.";
                return false;
            }

            for (var index = start; index <= end; index++)
            {
                result.Add(index);
            }
        }

        meshIndices = result;
        return true;
    }

    private static bool TryParseNonNegativeInt(string value, out int result)
    {
        return int.TryParse(value, out result) && result >= 0;
    }

    private static bool TryParseMaterialTextureIds(
        string? value,
        out IReadOnlyDictionary<string, byte>? materialTextureIds,
        out string error)
    {
        materialTextureIds = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || string.IsNullOrWhiteSpace(pieces[0]))
            {
                error = $"Invalid material texture mapping '{part}'. Expected name=id.";
                return false;
            }

            if (!byte.TryParse(pieces[1], out var textureId))
            {
                error = $"Invalid texture ID '{pieces[1]}' for material '{pieces[0]}'. Expected 0-255.";
                return false;
            }

            result[pieces[0]] = textureId;
        }

        materialTextureIds = result;
        return true;
    }

    private static bool TryParseVertexPrefix(string? value, out byte[]? prefix, out string error)
    {
        prefix = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var hex = value.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal);
        if (hex.Length != 16)
        {
            error = "--custom-static-vertex-prefix must contain exactly 8 bytes / 16 hex characters.";
            return false;
        }

        prefix = new byte[8];
        for (var i = 0; i < prefix.Length; i++)
        {
            var byteText = hex.Substring(i * 2, 2);
            if (!byte.TryParse(byteText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out prefix[i]))
            {
                error = $"--custom-static-vertex-prefix contains invalid hex byte '{byteText}'.";
                prefix = null;
                return false;
            }
        }

        return true;
    }

    private static bool TryParseMaterialUvScales(
        string? value,
        out IReadOnlyDictionary<string, Vector2>? materialUvScales,
        out string error)
    {
        materialUvScales = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var result = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split('=', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || string.IsNullOrWhiteSpace(pieces[0]))
            {
                error = $"Invalid material UV scale mapping '{part}'. Expected name=uScale:vScale.";
                return false;
            }

            var scalePieces = pieces[1].Split(':', StringSplitOptions.TrimEntries);
            if (scalePieces.Length != 2
                || !float.TryParse(scalePieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var uScale)
                || !float.TryParse(scalePieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var vScale))
            {
                error = $"Invalid UV scale '{pieces[1]}' for material '{pieces[0]}'. Expected uScale:vScale.";
                return false;
            }

            result[pieces[0]] = new Vector2(uScale, vScale);
        }

        materialUvScales = result;
        return true;
    }
}

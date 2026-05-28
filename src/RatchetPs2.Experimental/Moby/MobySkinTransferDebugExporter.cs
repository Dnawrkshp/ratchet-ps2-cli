using RatchetPs2.Core.Moby;

namespace RatchetPs2.Experimental.Moby;

public static class MobySkinTransferDebugExporter
{
    public static void ExportGltf(
        Stream gltf,
        Func<string, Stream> openBuffer,
        Stream skinReferenceMoby,
        Stream outputGltf,
        string bufferUri,
        Stream outputBuffer,
        MobySkinTransferDebugOptions? options = null)
    {
        options ??= new MobySkinTransferDebugOptions();
        MobyGltfImporter.ExportSkinTransferDebugGltfCore(
            gltf,
            openBuffer,
            skinReferenceMoby,
            outputGltf,
            bufferUri,
            outputBuffer,
            new MobySkinTransferDebugCoreOptions
            {
                AnimationFormat = options.AnimationFormat,
                CustomStaticScale = options.CustomStaticScale,
                CustomStaticYawDegrees = options.CustomStaticYawDegrees,
                CustomStaticPitchDegrees = options.CustomStaticPitchDegrees,
                CustomStaticRollDegrees = options.CustomStaticRollDegrees,
                SplitConnectedComponents = options.SplitConnectedComponents,
                SplitSideAxis = options.SplitSideAxis,
                SplitSideDeadzoneRatio = options.SplitSideDeadzoneRatio,
                OutputModelScale = options.OutputModelScale,
                SampleCount = options.SampleCount,
                VerticalWindow = options.VerticalWindow,
                SameSide = options.SameSide,
                SideAxis = options.SideAxis,
                SideDeadzoneRatio = options.SideDeadzoneRatio,
                MaterialRegions = options.MaterialRegions,
                DisableAnatomicalFilters = options.DisableAnatomicalFilters,
                PreserveLowerBodyFilters = options.PreserveLowerBodyFilters,
                PreserveShoulderFilters = options.PreserveShoulderFilters,
                ShoulderInwardBias = options.ShoulderInwardBias,
                TriangleCoherent = options.TriangleCoherent,
                SplitPrimarySeams = options.SplitPrimarySeams,
                RigidMeshCentroid = options.RigidMeshCentroid,
                RigidTriangleCentroid = options.RigidTriangleCentroid,
                SmoothPrimaryIterations = options.SmoothPrimaryIterations,
                DistancePower = options.DistancePower,
                ReferenceYawDegrees = options.ReferenceYawDegrees,
                MaterialUvScales = options.MaterialUvScales,
                ClampUvs = options.ClampUvs
            });
    }
}

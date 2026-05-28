using System.Numerics;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Experimental.Moby;

public sealed class MobySkinTransferDebugOptions
{
    public MobyAnimationFormat AnimationFormat { get; init; } = MobyAnimationFormat.Standard;
    public float CustomStaticScale { get; init; } = 1f;
    public float CustomStaticYawDegrees { get; init; }
    public float CustomStaticPitchDegrees { get; init; }
    public float CustomStaticRollDegrees { get; init; }
    public bool SplitConnectedComponents { get; init; }
    public string? SplitSideAxis { get; init; }
    public float SplitSideDeadzoneRatio { get; init; } = 0.02f;
    public float? OutputModelScale { get; init; }
    public int SampleCount { get; init; } = 1;
    public float? VerticalWindow { get; init; }
    public bool SameSide { get; init; }
    public string SideAxis { get; init; } = "x";
    public float SideDeadzoneRatio { get; init; } = 0.03f;
    public bool MaterialRegions { get; init; }
    public bool DisableAnatomicalFilters { get; init; }
    public bool PreserveLowerBodyFilters { get; init; }
    public bool PreserveShoulderFilters { get; init; }
    public float ShoulderInwardBias { get; init; }
    public bool TriangleCoherent { get; init; }
    public bool SplitPrimarySeams { get; init; }
    public bool RigidMeshCentroid { get; init; }
    public bool RigidTriangleCentroid { get; init; }
    public int SmoothPrimaryIterations { get; init; }
    public float DistancePower { get; init; } = 1f;
    public float ReferenceYawDegrees { get; init; }
    public IReadOnlyDictionary<string, Vector2>? MaterialUvScales { get; init; }
    public bool ClampUvs { get; init; }
}

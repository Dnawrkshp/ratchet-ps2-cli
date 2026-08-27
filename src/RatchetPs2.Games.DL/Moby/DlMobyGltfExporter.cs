using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;

namespace RatchetPs2.Games.DL.Moby;

public static class DlMobyGltfExporter
{
    public static MobyGltfExport Export(
        Stream input,
        string gltfFileName = "moby.gltf",
        MobyGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new MobyGltfExportOptions();
        var model = MobyModelReader.Read(
            input,
            new MobyModelReadOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                SkipAnimationSequences = options.SkipAnimationSequences
            });
        return Export(model, gltfFileName, options);
    }

    public static MobyGltfExport Export(
        MobyModel model,
        string gltfFileName = "moby.gltf",
        MobyGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new MobyGltfExportOptions();

        var animations = new List<MobyGltfAnimationClip>();
        var failures = new List<MobyGltfAnimationFailure>();
        var jointCount = Math.Min(model.JointCount, model.Skeleton?.Bones.Count ?? 0);
        if (!options.SkipAnimationSequences)
        {
            var modelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
            for (var sequenceIndex = 0; sequenceIndex < model.Sequences.Count; sequenceIndex++)
            {
                if (DlCompactAnimationDecoder.TryDecode(
                        model.Sequences[sequenceIndex],
                        sequenceIndex,
                        jointCount,
                        modelScale,
                        out var animation,
                        out var error))
                {
                    animations.Add(animation);
                }
                else
                {
                    failures.Add(new MobyGltfAnimationFailure(sequenceIndex, error));
                }
            }
        }

        var scale = (Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f) / 1024f;
        var inverseBindMatrices = model.Skeleton?.Bones
            .Take(jointCount)
            .Select(bone => DecodeInverseBindMatrix(bone, scale))
            .ToArray();
        var dlOptions = options with
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            SkeletonParentMode = options.SkeletonParentMode == MobyGltfSkeletonParentMode.Auto
                ? MobyGltfSkeletonParentMode.SevenBitLow
                : options.SkeletonParentMode,
            RefineSkinFromInfluences = false,
            HonorSkeletonParentRotationFlags = false,
            InverseBindMatrices = inverseBindMatrices,
            Animations = animations,
            AnimationFailures = failures,
            CompactAnimationSourceData = model.Sequences
                .Select((sequence, index) => (sequence.RawData, Index: index))
                .Where(item => item.RawData is { Length: > 0 })
                .ToDictionary(item => item.Index, item => item.RawData!),
            TextureFullOpacityAlpha = Ps2Color.FullOpacityAlpha
        };
        return MobyGltfExporter.Export(model, gltfFileName, dlOptions);
    }

    public static Matrix4x4 DecodeInverseBindMatrix(MobyMatrix4 bone, float scale)
    {
        var basis = new Matrix4x4(
            1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, -1f, 0f, 0f,
            0f, 0f, 0f, 1f);
        var source = new Matrix4x4(
            bone.Row1.X, bone.Row2.X, bone.Row3.X, 0f,
            bone.Row1.Y, bone.Row2.Y, bone.Row3.Y, 0f,
            bone.Row1.Z, bone.Row2.Z, bone.Row3.Z, 0f,
            0f, 0f, 0f, 1f);
        var mapped = basis * source * Matrix4x4.Transpose(basis);
        var translation = GltfCoordinateBasis.FromPs2Position(
            bone.Row1.W * scale,
            bone.Row2.W * scale,
            bone.Row3.W * scale);
        mapped.M41 = translation.X;
        mapped.M42 = translation.Y;
        mapped.M43 = translation.Z;
        return mapped;
    }
}

using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.DL.Moby;

public static class DlMobyConverter
{
    public static void ConvertFromUya(MobyModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.AnimationFormat != MobyAnimationFormat.Standard)
        {
            throw new ArgumentException("DL conversion requires a standard-format UYA moby.", nameof(model));
        }

        var (animations, failures) = MobyStandardAnimationDecoder.Decode(model);
        if (failures.Count > 0)
        {
            throw new InvalidDataException(
                $"Could not convert standard animation(s): {string.Join(", ", failures.Select(failure => $"{failure.SourceIndex} ({failure.Reason})"))}.");
        }
        if (animations.Count != model.Sequences.Count)
        {
            throw new InvalidDataException($"Decoded {animations.Count} of {model.Sequences.Count} standard animations.");
        }

        var sourceSequences = model.Sequences.ToArray();
        var compactSequences = animations
            .Select(animation => DlMobyGltfImporter.EncodeSequence(
                model,
                animation,
                sourceSequences[animation.SourceIndex]))
            .ToArray();

        ConvertSkeleton(model);
        ConvertCommonTransforms(model);
        model.Sequences.Clear();
        model.Sequences.AddRange(compactSequences);
        model.AnimationFormat = MobyAnimationFormat.Compact;
        model.SkeletonFormat = MobyAnimationFormat.Compact;
        model.AnimationCount = checked((byte)compactSequences.Length);
    }

    private static void ConvertSkeleton(MobyModel model)
    {
        if (model.JointCount == 0)
        {
            return;
        }
        if (model.Skeleton is null || model.Skeleton.Bones.Count < model.JointCount)
        {
            throw new InvalidDataException("UYA moby skeleton is missing joints required by its animations.");
        }

        foreach (var bone in model.Skeleton.Bones.Take(model.JointCount))
        {
            var row1 = bone.Row1;
            var row2 = bone.Row2;
            var row3 = bone.Row3;
            var row4 = bone.Row4;
            bone.Row1 = new MobyMatrixRow { X = row1.X, Y = row2.X, Z = row3.X, W = row4.X };
            bone.Row2 = new MobyMatrixRow { X = row1.Y, Y = row2.Y, Z = row3.Y, W = row4.Y };
            bone.Row3 = new MobyMatrixRow { X = row1.Z, Y = row2.Z, Z = row3.Z, W = row4.Z };
            bone.Row4 = new MobyMatrixRow { W = 1f };
        }
    }

    private static void ConvertCommonTransforms(MobyModel model)
    {
        if (model.JointCount == 0)
        {
            return;
        }
        if (model.CommonTransforms is not { } transforms || transforms.Length < model.JointCount * 0x10)
        {
            throw new InvalidDataException("UYA moby common transform table is missing joints required by its animations.");
        }

        for (var joint = 0; joint < model.JointCount; joint++)
        {
            var offset = joint * 0x10 + 0x0C;
            var parent = BitConverter.ToUInt16(transforms, offset) >> 6;
            if (parent >= joint)
            {
                transforms[offset] = byte.MaxValue;
            }
            else if (parent <= 0x7E)
            {
                transforms[offset] = checked((byte)(0x80 | parent));
            }
            else
            {
                throw new InvalidDataException($"UYA moby joint {joint} parent {parent} exceeds the DL format limit.");
            }
            transforms.AsSpan(offset + 1, 3).Clear();
        }
    }
}

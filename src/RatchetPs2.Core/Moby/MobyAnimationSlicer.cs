namespace RatchetPs2.Core.Moby;

public static class MobyAnimationSlicer
{
    public static void KeepSingleAnimationAsZero(MobyModel model, int animationIndex)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (animationIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(animationIndex), "Animation index must be zero or greater.");
        }

        if (animationIndex >= model.Sequences.Count)
        {
            throw new InvalidDataException(
                $"Model has {model.Sequences.Count} parsed animations; cannot keep animation {animationIndex}.");
        }

        var sequence = model.Sequences[animationIndex];
        model.Sequences.Clear();
        model.Sequences.Add(sequence);
        model.AnimationCount = 1;
    }

    public static void ReplaceWithDefaultAnimation(MobyModel model, MobyAnimationFormat format)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sourceSequences = model.Sequences.ToArray();
        model.AnimationFormat = format;
        model.Sequences.Clear();
        if (format == MobyAnimationFormat.Compact)
        {
            if (TryBuildCompactDefaultFromExistingSequence(sourceSequences, model.BoundingSphere, out var compactDefaultSequence))
            {
                AddRepeatedCompactDefaultSequences(model, compactDefaultSequence, sourceSequences.Length);
                return;
            }

            var fallbackDefaultSequence = new MobySequence
            {
                Format = MobyAnimationFormat.Compact,
                BoundingSphere = new MobyBoundingSphere
                {
                    X = model.BoundingSphere.X,
                    Y = model.BoundingSphere.Y,
                    Z = model.BoundingSphere.Z,
                    Radius = model.BoundingSphere.Radius
                },
                FrameCount = 1,
                Sound = 255,
                TriggerCount = 0,
                Padding = 255,
                CompactFrames =
                {
                    new MobyCompactAnimationFrame
                    {
                        Unknown00 = 2,
                        FrameId = 0
                    }
                },
                CompactAnimInfoData = new byte[0x08],
                CompactFrameData = new byte[0x10]
            };
            AddRepeatedCompactDefaultSequences(model, fallbackDefaultSequence, sourceSequences.Length);
            return;
        }

        model.Sequences.Add(new MobySequence
        {
            Format = MobyAnimationFormat.Standard,
            BoundingSphere = new MobyBoundingSphere
            {
                X = model.BoundingSphere.X,
                Y = model.BoundingSphere.Y,
                Z = model.BoundingSphere.Z,
                Radius = model.BoundingSphere.Radius
            },
            FrameCount = 1,
            Sound = 255,
            TriggerCount = 0,
            Padding = 255,
            Frames =
            {
                new MobyAnimationFrame()
            }
        });
        model.AnimationCount = 1;
    }

    private static void AddRepeatedCompactDefaultSequences(
        MobyModel model,
        MobySequence defaultSequence,
        int sourceAnimationCount)
    {
        var animationCount = Math.Max(1, sourceAnimationCount);
        for (var i = 0; i < animationCount; i++)
        {
            model.Sequences.Add(CloneSequence(defaultSequence));
        }

        model.AnimationCount = checked((byte)animationCount);
    }

    public static void CopyAnimationAsZero(
        MobyModel target,
        MobyModel source,
        int animationIndex,
        MobyAnimationFormat outputFormat)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        if (animationIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(animationIndex), "Animation index must be zero or greater.");
        }

        if (animationIndex >= source.Sequences.Count)
        {
            throw new InvalidDataException(
                $"Animation source has {source.Sequences.Count} parsed animations; cannot copy animation {animationIndex}.");
        }

        var sequence = CloneSequence(source.Sequences[animationIndex]);
        sequence.Format = outputFormat;
        target.AnimationFormat = outputFormat;
        target.Sequences.Clear();
        target.Sequences.Add(sequence);
        target.AnimationCount = 1;
    }

    private static MobySequence CloneSequence(MobySequence source)
    {
        var clone = new MobySequence
        {
            Format = source.Format,
            RawData = source.RawData is null ? null : (byte[])source.RawData.Clone(),
            BoundingSphere = new MobyBoundingSphere
            {
                X = source.BoundingSphere.X,
                Y = source.BoundingSphere.Y,
                Z = source.BoundingSphere.Z,
                Radius = source.BoundingSphere.Radius
            },
            FrameCount = source.FrameCount,
            Sound = source.Sound,
            TriggerCount = source.TriggerCount,
            Padding = source.Padding,
            Unknown14 = source.Unknown14,
            Unknown18 = source.Unknown18,
            CompactTriggerOffset = source.CompactTriggerOffset,
            CompactAnimDataOffset = source.CompactAnimDataOffset,
            CompactFrameDataOffset = source.CompactFrameDataOffset,
            CompactAnimInfoData = (byte[])source.CompactAnimInfoData.Clone(),
            CompactFrameData = (byte[])source.CompactFrameData.Clone()
        };

        foreach (var offset in source.FrameOffsets)
        {
            clone.FrameOffsets.Add(offset);
        }

        foreach (var frame in source.CompactFrames)
        {
            clone.CompactFrames.Add(new MobyCompactAnimationFrame
            {
                Unknown00 = frame.Unknown00,
                FrameId = frame.FrameId
            });
        }

        foreach (var trigger in source.Triggers)
        {
            clone.Triggers.Add(new MobyAnimationTrigger
            {
                Unknown00 = trigger.Unknown00,
                Unknown02 = trigger.Unknown02
            });
        }

        foreach (var frame in source.Frames)
        {
            clone.Frames.Add(new MobyAnimationFrame
            {
                Unknown00 = frame.Unknown00,
                Unknown01 = frame.Unknown01,
                Unknown02 = frame.Unknown02,
                Unknown03 = frame.Unknown03,
                Unknown04 = frame.Unknown04,
                Unknown05 = frame.Unknown05,
                FrameDataSize = frame.FrameDataSize,
                Unknown07 = frame.Unknown07,
                Unknown08 = frame.Unknown08,
                Unknown0C = frame.Unknown0C,
                FrameData = (byte[])frame.FrameData.Clone()
            });
        }

        return clone;
    }

    private static bool TryBuildCompactDefaultFromExistingSequence(
        IReadOnlyList<MobySequence> sourceSequences,
        MobyBoundingSphere fallbackBoundingSphere,
        out MobySequence sequence)
    {
        sequence = default!;
        var source = sourceSequences.FirstOrDefault(item =>
            item.Format == MobyAnimationFormat.Compact
            && item.FrameCount > 0
            && item.CompactFrames.Count > 0
            && item.RawData is { Length: > 0 }
            && item.CompactAnimDataOffset > 0
            && item.CompactFrameDataOffset > item.CompactAnimDataOffset);
        if (source is null || !TrySliceCompactSingleFrameData(source, out var animInfoData, out var frameData))
        {
            return false;
        }

        sequence = new MobySequence
        {
            Format = MobyAnimationFormat.Compact,
            BoundingSphere = new MobyBoundingSphere
            {
                X = source.BoundingSphere.Radius > 0 ? source.BoundingSphere.X : fallbackBoundingSphere.X,
                Y = source.BoundingSphere.Radius > 0 ? source.BoundingSphere.Y : fallbackBoundingSphere.Y,
                Z = source.BoundingSphere.Radius > 0 ? source.BoundingSphere.Z : fallbackBoundingSphere.Z,
                Radius = source.BoundingSphere.Radius > 0 ? source.BoundingSphere.Radius : fallbackBoundingSphere.Radius
            },
            FrameCount = source.FrameCount,
            Sound = source.Sound,
            TriggerCount = 0,
            Padding = source.Padding,
            CompactAnimInfoData = animInfoData,
            CompactFrameData = frameData
        };
        var staticFrame = source.CompactFrames[0];
        for (var i = 0; i < source.CompactFrames.Count; i++)
        {
            sequence.CompactFrames.Add(new MobyCompactAnimationFrame
            {
                Unknown00 = staticFrame.Unknown00,
                FrameId = staticFrame.FrameId
            });
        }

        return true;
    }

    private static bool TrySliceCompactSingleFrameData(
        MobySequence source,
        out byte[] animInfoData,
        out byte[] frameData)
    {
        animInfoData = [];
        frameData = [];
        var raw = source.RawData;
        if (raw is null
            || source.CompactAnimDataOffset < 0
            || source.CompactFrameDataOffset <= source.CompactAnimDataOffset
            || source.CompactFrameDataOffset >= raw.Length)
        {
            return false;
        }

        var animInfoLength = source.CompactFrameDataOffset - source.CompactAnimDataOffset;
        if (source.CompactAnimDataOffset + animInfoLength > raw.Length)
        {
            return false;
        }

        var frameDataSource = raw.AsSpan(source.CompactFrameDataOffset);
        if (frameDataSource.Length < 0x10)
        {
            return false;
        }

        animInfoData = raw.AsSpan(source.CompactAnimDataOffset, animInfoLength).ToArray();
        frameData = frameDataSource.ToArray();
        return true;
    }
}

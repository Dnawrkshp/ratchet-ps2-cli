using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Moby;

public static class MobyStandardAnimationDecoder
{
    private const float TimestampTicksPerSecond = 8f * 60f;

    public static (IReadOnlyList<MobyGltfAnimationClip> Animations, IReadOnlyList<MobyGltfAnimationFailure> Failures) Decode(
        MobyModel model)
    {
        var animations = new List<MobyGltfAnimationClip>();
        var failures = new List<MobyGltfAnimationFailure>();
        var jointCount = Math.Min(model.JointCount, model.Skeleton?.Bones.Count ?? 0);
        var modelScale = Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f;
        var bindTranslations = ReadBindTranslations(model.CommonTransforms, jointCount, modelScale);

        for (var sequenceIndex = 0; sequenceIndex < model.Sequences.Count; sequenceIndex++)
        {
            if (TryDecode(
                    model.Sequences[sequenceIndex],
                    sequenceIndex,
                    jointCount,
                    modelScale,
                    bindTranslations,
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

        return (animations, failures);
    }

    private static bool TryDecode(
        MobySequence sequence,
        int sequenceIndex,
        int jointCount,
        float modelScale,
        IReadOnlyList<Vector3> bindTranslations,
        out MobyGltfAnimationClip animation,
        out string error)
    {
        animation = default!;
        error = string.Empty;
        if (sequence.HasSpecialFrameData)
        {
            error = "special standard animation data is not supported";
            return false;
        }
        if (jointCount <= 0 || sequence.Frames.Count == 0 || sequence.Frames.Count != sequence.FrameCount)
        {
            error = "invalid standard animation frame table";
            return false;
        }

        var rotationsByFrame = new Quaternion[sequence.Frames.Count + 1][];
        var scalesByFrame = new Dictionary<int, Vector3>[sequence.Frames.Count];
        var translationsByFrame = new Dictionary<int, Vector3>[sequence.Frames.Count];
        for (var frameIndex = 0; frameIndex < sequence.Frames.Count; frameIndex++)
        {
            if (!TryDecodeFrame(
                    sequence.Frames[frameIndex],
                    jointCount,
                    modelScale,
                    out rotationsByFrame[frameIndex],
                    out scalesByFrame[frameIndex],
                    out translationsByFrame[frameIndex],
                    out error))
            {
                error = $"frame {frameIndex}: {error}";
                return false;
            }
        }

        rotationsByFrame[^1] = (Quaternion[])rotationsByFrame[0].Clone();
        animation = new MobyGltfAnimationClip(
            sequenceIndex,
            $"animation_{sequenceIndex:0000}",
            DecodeTimes(sequence.Frames),
            CollectRotationTracks(rotationsByFrame),
            CollectVectorTracks(scalesByFrame, _ => Vector3.One),
            CollectVectorTracks(translationsByFrame, joint => bindTranslations[joint]));
        return true;
    }

    private static bool TryDecodeFrame(
        MobyAnimationFrame frame,
        int jointCount,
        float modelScale,
        out Quaternion[] rotations,
        out Dictionary<int, Vector3> scales,
        out Dictionary<int, Vector3> translations,
        out string error)
    {
        rotations = new Quaternion[jointCount];
        scales = [];
        translations = [];
        error = string.Empty;

        var jointDataSize = (ushort)frame.Unknown08;
        var scaleCount = (ushort)((uint)frame.Unknown08 >> 16);
        var translationOffset = (ushort)frame.Unknown0C;
        var translationCount = (ushort)((uint)frame.Unknown0C >> 16);
        var requiredLength = translationOffset + translationCount * 8;
        if (jointDataSize != jointCount * 8
            || translationOffset != jointDataSize + scaleCount * 8
            || requiredLength > frame.FrameData.Length)
        {
            error = "transform tables are out of bounds";
            return false;
        }

        var data = frame.FrameData.AsSpan();
        for (var joint = 0; joint < jointCount; joint++)
        {
            var offset = joint * 8;
            var x = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
            var y = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 2, 2));
            var z = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 4, 2));
            var w = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 6, 2));
            var length = MathF.Sqrt(x * (float)x + y * (float)y + z * (float)z + w * (float)w);
            if (length < 1f)
            {
                error = $"joint {joint} has an empty quaternion";
                return false;
            }

            rotations[joint] = Quaternion.Normalize(new Quaternion(-x / length, -z / length, y / length, w / length));
        }

        for (var i = 0; i < scaleCount; i++)
        {
            var offset = jointDataSize + i * 8;
            var joint = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 6, 2)) & 0x7FFF;
            if (joint < jointCount)
            {
                scales[joint] = new Vector3(
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)) / 4096f,
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 4, 2)) / 4096f,
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 2, 2)) / 4096f);
            }
        }

        for (var i = 0; i < translationCount; i++)
        {
            var offset = translationOffset + i * 8;
            var joint = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset + 6, 2)) & 0x7FFF;
            if (joint < jointCount)
            {
                translations[joint] = GltfCoordinateBasis.FromPs2Position(
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2)) * modelScale / 1024f,
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 2, 2)) * modelScale / 1024f,
                    BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 4, 2)) * modelScale / 1024f);
            }
        }

        return true;
    }

    private static float[] DecodeTimes(IReadOnlyList<MobyAnimationFrame> frames)
    {
        var times = new float[frames.Count + 1];
        for (var i = 1; i < frames.Count; i++)
        {
            var previous = ReadTimestamp(frames[i - 1]);
            var current = ReadTimestamp(frames[i]);
            times[i] = times[i - 1] + unchecked((ushort)(current - previous)) / TimestampTicksPerSecond;
        }

        var rate = ReadRate(frames[^1]);
        times[^1] = times[^2] + (rate > 0f && float.IsFinite(rate) ? 1f / (60f * rate) : 1f / 60f);
        return times;
    }

    private static ushort ReadTimestamp(MobyAnimationFrame frame)
    {
        return (ushort)(frame.Unknown04 | frame.Unknown05 << 8);
    }

    private static float ReadRate(MobyAnimationFrame frame)
    {
        return BitConverter.Int32BitsToSingle(
            frame.Unknown00
            | frame.Unknown01 << 8
            | frame.Unknown02 << 16
            | frame.Unknown03 << 24);
    }

    private static Vector3[] ReadBindTranslations(byte[]? commonTransforms, int jointCount, float modelScale)
    {
        var translations = new Vector3[jointCount];
        if (commonTransforms is null || commonTransforms.Length < jointCount * 0x10)
        {
            return translations;
        }

        for (var joint = 0; joint < jointCount; joint++)
        {
            var offset = joint * 0x10;
            translations[joint] = GltfCoordinateBasis.FromPs2Position(
                BitConverter.ToSingle(commonTransforms, offset) * modelScale / 1024f,
                BitConverter.ToSingle(commonTransforms, offset + 4) * modelScale / 1024f,
                BitConverter.ToSingle(commonTransforms, offset + 8) * modelScale / 1024f);
        }
        return translations;
    }

    private static Dictionary<int, Quaternion[]> CollectRotationTracks(IReadOnlyList<Quaternion[]> frames)
    {
        var tracks = new Dictionary<int, Quaternion[]>(frames[0].Length);
        for (var joint = 0; joint < frames[0].Length; joint++)
        {
            tracks[joint] = frames.Select(frame => frame[joint]).ToArray();
        }
        return tracks;
    }

    private static Dictionary<int, Vector3[]> CollectVectorTracks(
        IReadOnlyList<Dictionary<int, Vector3>> frames,
        Func<int, Vector3> fallback)
    {
        var tracks = new Dictionary<int, Vector3[]>();
        foreach (var joint in frames.SelectMany(frame => frame.Keys).Distinct().Order())
        {
            var values = new Vector3[frames.Count + 1];
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                values[frameIndex] = frames[frameIndex].GetValueOrDefault(joint, fallback(joint));
            }
            values[^1] = values[0];
            tracks[joint] = values;
        }
        return tracks;
    }
}

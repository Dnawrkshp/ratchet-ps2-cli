using System.Buffers.Binary;
using System.Numerics;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.DL.Moby;

public static class DlCompactAnimationDecoder
{
    private const float GameTicksPerSecond = 60f;

    public static bool TryDecode(
        MobySequence sequence,
        int sequenceIndex,
        int jointCount,
        float modelScale,
        out MobyGltfAnimationClip animation,
        out string error)
    {
        animation = default!;
        error = string.Empty;
        if (sequence.RawData is not { Length: > 0 } raw
            || sequence.CompactFrameDataOffset < 0
            || sequence.CompactFrameDataOffset + 0x10 > raw.Length)
        {
            error = "missing compact frame data";
            return false;
        }

        var data = raw.AsSpan(sequence.CompactFrameDataOffset);
        var frameCount = sequence.CompactFrames.Count;
        if (frameCount == 0 || frameCount != sequence.FrameCount)
        {
            error = "invalid compact frame table";
            return false;
        }

        var staticBytes = data[1] * 0x10;
        var perFrameBytes = data[2] * 0x10;
        const int baseDataStart = 0x10;
        var routeStart = baseDataStart + data[3] * 0x10;
        var opcodeStart = routeStart + data[4] * 0x08;
        var startJoint = data[6];
        var decodedJointCount = data[7];
        var quaternionPairCount = data[8];
        var scalePairCount = data[9];
        var translationPairCount = data[10];
        var callCount = 2 + quaternionPairCount + scalePairCount + translationPairCount;
        var requiredLength = staticBytes + frameCount * perFrameBytes;
        if (staticBytes < 0x10
            || requiredLength > data.Length
            || opcodeStart + callCount > staticBytes
            || routeStart + (quaternionPairCount + scalePairCount + translationPairCount) * 8 > opcodeStart
            || startJoint + decodedJointCount > jointCount
            || quaternionPairCount * 2 < decodedJointCount)
        {
            error = "compact animation header is out of bounds";
            return false;
        }

        var routes = new byte[callCount][];
        var opcodes = new byte[callCount];
        for (var callIndex = 0; callIndex < callCount; callIndex++)
        {
            var routeIndex = callIndex < 3 ? 0 : callIndex - 2;
            routes[callIndex] = data.Slice(routeStart + routeIndex * 8, 8).ToArray();
            opcodes[callIndex] = data[opcodeStart + callIndex];
            if (GetDestinationFamily(opcodes[callIndex]) < 0)
            {
                error = $"unsupported compact animation opcode 0x{opcodes[callIndex]:X2}";
                return false;
            }
        }

        var times = new float[frameCount + 1];
        for (var i = 0; i < frameCount; i++)
        {
            var durationTicks = (ushort)sequence.CompactFrames[i].Unknown00;
            if (durationTicks == 0)
            {
                error = $"compact animation frame {i} has no duration";
                return false;
            }

            times[i + 1] = times[i] + durationTicks / GameTicksPerSecond;
        }

        var rotationsByFrame = new Quaternion[frameCount + 1][];
        var scalesByFrame = new Dictionary<int, Vector3>[frameCount];
        var translationsByFrame = new Dictionary<int, Vector3>[frameCount];
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            if (!TryDecodeFrame(
                    data,
                    frameIndex,
                    baseDataStart,
                    staticBytes,
                    perFrameBytes,
                    routes,
                    opcodes,
                    startJoint,
                    decodedJointCount,
                    quaternionPairCount,
                    scalePairCount,
                    translationPairCount,
                    modelScale,
                    out rotationsByFrame[frameIndex],
                    out scalesByFrame[frameIndex],
                    out translationsByFrame[frameIndex],
                    out error))
            {
                return false;
            }
        }

        rotationsByFrame[^1] = (Quaternion[])rotationsByFrame[0].Clone();
        animation = new MobyGltfAnimationClip(
            sequenceIndex,
            $"animation_{sequenceIndex:0000}",
            times,
            CollectRotationTracks(rotationsByFrame),
            CollectVectorTracks(scalesByFrame, Vector3.One),
            CollectVectorTracks(translationsByFrame, Vector3.Zero));
        return true;
    }

    private static bool TryDecodeFrame(
        ReadOnlySpan<byte> data,
        int frameIndex,
        int baseDataStart,
        int staticBytes,
        int perFrameBytes,
        IReadOnlyList<byte[]> routes,
        IReadOnlyList<byte> opcodes,
        int startJoint,
        int jointCount,
        int quaternionPairCount,
        int scalePairCount,
        int translationPairCount,
        float modelScale,
        out Quaternion[] rotations,
        out Dictionary<int, Vector3> scales,
        out Dictionary<int, Vector3> translations,
        out string error)
    {
        rotations = Enumerable.Repeat(Quaternion.Identity, startJoint + jointCount).ToArray();
        scales = new Dictionary<int, Vector3>(scalePairCount * 2);
        translations = new Dictionary<int, Vector3>(translationPairCount * 2);
        error = string.Empty;

        var header = data[..0x10];
        var baseDataOffset = baseDataStart;
        var frameOffset = staticBytes + frameIndex * perFrameBytes;
        var fullFrameOffset = frameOffset;
        var deltaFrameOffset = frameOffset + header[5] * 0x10;
        Span<int> destinations = stackalloc int[] { -0x20, -0x1f, -0x1e };
        Span<int> ring = stackalloc int[0x100];
        var calls = new DecodedPair[opcodes.Count];
        Span<int> values = stackalloc int[8];

        for (var callIndex = 0; callIndex < opcodes.Count; callIndex++)
        {
            var opcode = opcodes[callIndex];
            var destinationFamily = GetDestinationFamily(opcode);
            if (opcode == 0x18)
            {
                if (fullFrameOffset + 0x10 > frameOffset + perFrameBytes
                    || !TryReadShorts(data, ref fullFrameOffset, values))
                {
                    error = "compact full-frame data is truncated";
                    return false;
                }
            }
            else
            {
                // The EE issues two delayed pipeline flushes after the packed static qwords.
                if (baseDataOffset + 0x10 <= staticBytes)
                {
                    if (!TryReadShorts(data, ref baseDataOffset, values))
                    {
                        error = "compact base data is truncated";
                        return false;
                    }
                }
                else
                {
                    values.Clear();
                    baseDataOffset += 0x10;
                }

                if (opcode == 0x30)
                {
                    if (deltaFrameOffset + 8 > frameOffset + perFrameBytes)
                    {
                        error = "compact delta data is truncated";
                        return false;
                    }

                    for (var i = 0; i < 8; i++)
                    {
                        values[i] += unchecked((sbyte)data[deltaFrameOffset + i]) << 2;
                    }
                    deltaFrameOffset += 8;
                }
            }

            destinations[destinationFamily] += 0x20;
            var destination = destinations[destinationFamily] & 0x7f;
            for (var i = 0; i < 4; i++)
            {
                ring[(0x80 + destination + i * 4) & 0xff] = values[i];
                ring[(0x90 + destination + i * 4) & 0xff] = values[i + 4];
            }

            var route = routes[callIndex];
            calls[callIndex] = new DecodedPair(
                new Vector4(ring[route[0]], ring[route[1]], ring[route[2]], ring[route[3]]),
                new Vector4(ring[route[4]], ring[route[5]], ring[route[6]], ring[route[7]]));
        }

        for (var pairIndex = 0; pairIndex < quaternionPairCount; pairIndex++)
        {
            var pair = calls[2 + pairIndex];
            var joint = startJoint + pairIndex * 2;
            if (joint < startJoint + jointCount)
            {
                rotations[joint] = DecodeRotation(pair.First);
            }
            if (joint + 1 < startJoint + jointCount)
            {
                rotations[joint + 1] = DecodeRotation(pair.Second);
            }
        }

        for (var pairIndex = 0; pairIndex < scalePairCount; pairIndex++)
        {
            var callIndex = 2 + quaternionPairCount + pairIndex;
            var pair = calls[callIndex];
            AddScale(scales, routes[callIndex][3], pair.First);
            AddScale(scales, routes[callIndex][7], pair.Second);
        }

        for (var pairIndex = 0; pairIndex < translationPairCount; pairIndex++)
        {
            var callIndex = 2 + quaternionPairCount + scalePairCount + pairIndex;
            var pair = calls[callIndex];
            AddTranslation(translations, routes[callIndex][3], pair.First, modelScale);
            AddTranslation(translations, routes[callIndex][7], pair.Second, modelScale);
        }

        return true;
    }

    private static int GetDestinationFamily(byte opcode)
    {
        return opcode switch
        {
            0x00 => 0,
            0x30 => 1,
            0x18 => 2,
            _ => -1
        };
    }

    private static bool TryReadShorts(ReadOnlySpan<byte> data, ref int offset, Span<int> values)
    {
        if (offset < 0 || offset + 0x10 > data.Length)
        {
            return false;
        }

        for (var i = 0; i < 8; i++)
        {
            values[i] = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + i * 2, 2));
        }
        offset += 0x10;
        return true;
    }

    private static Quaternion DecodeRotation(Vector4 values)
    {
        var length = MathF.Sqrt(
            values.X * values.X
            + values.Y * values.Y
            + values.Z * values.Z
            + values.W * values.W);
        if (length < 1f)
        {
            return Quaternion.Identity;
        }

        var ps2 = values / length;
        // DL stores the inverse of the active rotation expected by glTF.
        return Quaternion.Normalize(new Quaternion(-ps2.X, -ps2.Z, ps2.Y, ps2.W));
    }

    private static void AddScale(Dictionary<int, Vector3> scales, int joint, Vector4 values)
    {
        scales[joint] = new Vector3(values.X / 4096f, values.Z / 4096f, values.Y / 4096f);
    }

    private static void AddTranslation(
        Dictionary<int, Vector3> translations,
        int joint,
        Vector4 values,
        float modelScale)
    {
        translations[joint] = GltfCoordinateBasis.FromPs2Position(
            values.X * modelScale / 1024f,
            values.Y * modelScale / 1024f,
            values.Z * modelScale / 1024f);
    }

    private static Dictionary<int, Quaternion[]> CollectRotationTracks(IReadOnlyList<Quaternion[]> frames)
    {
        var tracks = new Dictionary<int, Quaternion[]>(frames[0].Length);
        for (var joint = 0; joint < frames[0].Length; joint++)
        {
            var values = new Quaternion[frames.Count];
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                values[frameIndex] = frames[frameIndex][joint];
            }
            tracks[joint] = values;
        }
        return tracks;
    }

    private static Dictionary<int, Vector3[]> CollectVectorTracks(
        IReadOnlyList<Dictionary<int, Vector3>> frames,
        Vector3 fallback)
    {
        var tracks = new Dictionary<int, Vector3[]>();
        foreach (var joint in frames.SelectMany(frame => frame.Keys).Distinct().Order())
        {
            var values = new Vector3[frames.Count + 1];
            for (var frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                values[frameIndex] = frames[frameIndex].GetValueOrDefault(joint, fallback);
            }
            values[^1] = values[0];
            tracks[joint] = values;
        }
        return tracks;
    }

    private readonly record struct DecodedPair(Vector4 First, Vector4 Second);
}

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.DL.Moby;

public static class DlMobyGltfImporter
{
    private const float GameTicksPerSecond = 60f;
    private const byte BaseOpcode = 0x00;
    private const byte FullOpcode = 0x18;
    private const byte DeltaOpcode = 0x30;

    public static MobyModel Import(
        Stream templateMoby,
        Stream gltf,
        Func<string, Stream> openBuffer,
        MobyGltfImportOptions? options = null,
        Stream? rigSourceMoby = null,
        Stream? skinReferenceMoby = null)
    {
        return ImportWithDiagnostics(templateMoby, gltf, openBuffer, options, rigSourceMoby, skinReferenceMoby).Model;
    }

    public static MobyGltfImportResult ImportWithDiagnostics(
        Stream templateMoby,
        Stream gltf,
        Func<string, Stream> openBuffer,
        MobyGltfImportOptions? options = null,
        Stream? rigSourceMoby = null,
        Stream? skinReferenceMoby = null)
    {
        ArgumentNullException.ThrowIfNull(gltf);
        ArgumentNullException.ThrowIfNull(openBuffer);
        options ??= new MobyGltfImportOptions { AnimationFormat = MobyAnimationFormat.Compact };
        if (options.AnimationFormat != MobyAnimationFormat.Compact)
        {
            throw new ArgumentException("DL moby import requires the compact animation format.", nameof(options));
        }

        using var copy = new MemoryStream();
        gltf.CopyTo(copy);
        var gltfBytes = copy.ToArray();
        using var coreInput = new MemoryStream(gltfBytes, writable: false);
        var result = MobyGltfImporter.ImportWithDiagnostics(
            templateMoby,
            coreInput,
            openBuffer,
            options,
            rigSourceMoby,
            skinReferenceMoby);
        using var animationInput = new MemoryStream(gltfBytes, writable: false);
        ApplyAnimations(result.Model, animationInput, openBuffer);
        return result;
    }

    public static void ApplyAnimations(MobyModel model, Stream gltf, Func<string, Stream> openBuffer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(gltf);
        ArgumentNullException.ThrowIfNull(openBuffer);
        if (model.AnimationFormat != MobyAnimationFormat.Compact)
        {
            throw new ArgumentException("DL animation import requires a compact-format moby model.", nameof(model));
        }

        using var document = JsonDocument.Parse(gltf);
        var root = document.RootElement;
        if (!root.TryGetProperty("animations", out var animations) || animations.GetArrayLength() == 0)
        {
            return;
        }

        var buffers = GltfAccessorReader.ReadBuffers(root, openBuffer);
        var accessors = root.GetProperty("accessors");
        var bufferViews = root.GetProperty("bufferViews");
        var nodes = root.GetProperty("nodes");
        var importedIndices = new HashSet<int>();

        for (var animationIndex = 0; animationIndex < animations.GetArrayLength(); animationIndex++)
        {
            var source = ReadAnimation(
                animations[animationIndex],
                animationIndex,
                nodes,
                accessors,
                bufferViews,
                buffers);
            if (source.Clip.SourceIndex < 0)
            {
                throw new InvalidDataException($"glTF moby animation index {source.Clip.SourceIndex} cannot be negative.");
            }
            if (!importedIndices.Add(source.Clip.SourceIndex))
            {
                throw new InvalidDataException($"glTF contains duplicate moby animation index {source.Clip.SourceIndex}.");
            }

            var template = source.Clip.SourceIndex < model.Sequences.Count
                ? model.Sequences[source.Clip.SourceIndex]
                : null;
            var sequence = source.RawSequence is not null
                && string.Equals(
                    source.SourceFingerprint,
                    MobyGltfAnimationFingerprint.Compute(source.Clip),
                    StringComparison.OrdinalIgnoreCase)
                    ? ReadRawSequence(source.RawSequence)
                    : EncodeSequence(model, source.Clip, template);

            if (source.Clip.SourceIndex < model.Sequences.Count)
            {
                model.Sequences[source.Clip.SourceIndex] = sequence;
            }
            else if (source.Clip.SourceIndex == model.Sequences.Count)
            {
                model.Sequences.Add(sequence);
            }
            else
            {
                throw new InvalidDataException(
                    $"glTF animation index {source.Clip.SourceIndex} leaves a gap after animation {model.Sequences.Count - 1}.");
            }
        }

        model.AnimationCount = checked((byte)model.Sequences.Count);
    }

    private static ImportedAnimation ReadAnimation(
        JsonElement animation,
        int animationIndex,
        JsonElement nodes,
        JsonElement accessors,
        JsonElement bufferViews,
        IReadOnlyList<byte[]> buffers)
    {
        ReadSourceMetadata(animation, out var sourceIndex, out var sourceFingerprint, out var rawSequence);
        sourceIndex ??= TryReadSourceIndexFromName(animation) ?? animationIndex;

        var times = Array.Empty<float>();
        var rotations = new Dictionary<int, Quaternion[]>();
        var scales = new Dictionary<int, Vector3[]>();
        var translations = new Dictionary<int, Vector3[]>();
        var samplers = animation.GetProperty("samplers");
        foreach (var channel in animation.GetProperty("channels").EnumerateArray())
        {
            var sampler = samplers[channel.GetProperty("sampler").GetInt32()];
            if (sampler.TryGetProperty("interpolation", out var interpolation)
                && !string.Equals(interpolation.GetString(), "LINEAR", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"DL animation {sourceIndex} uses unsupported {interpolation.GetString()} interpolation; expected LINEAR.");
            }

            var channelTimes = GltfAccessorReader.ReadScalarFloatAccessor(
                sampler.GetProperty("input").GetInt32(), accessors, bufferViews, buffers).ToArray();
            if (times.Length == 0)
            {
                times = channelTimes;
            }
            else if (!times.SequenceEqual(channelTimes))
            {
                throw new InvalidDataException($"DL animation {sourceIndex} channels must share one keyframe timeline.");
            }

            var target = channel.GetProperty("target");
            var joint = ReadJointIndex(nodes[target.GetProperty("node").GetInt32()], sourceIndex.Value);
            var outputAccessor = sampler.GetProperty("output").GetInt32();
            switch (target.GetProperty("path").GetString())
            {
                case "rotation":
                    AddTrack(
                        rotations,
                        joint,
                        GltfAccessorReader.ReadVec4FloatAccessor(outputAccessor, accessors, bufferViews, buffers)
                            .Select(value => new Quaternion(value[0], value[1], value[2], value[3]))
                            .ToArray(),
                        sourceIndex.Value,
                        "rotation");
                    break;
                case "scale":
                    AddTrack(
                        scales,
                        joint,
                        GltfAccessorReader.ReadVec3Accessor(outputAccessor, accessors, bufferViews, buffers).ToArray(),
                        sourceIndex.Value,
                        "scale");
                    break;
                case "translation":
                    AddTrack(
                        translations,
                        joint,
                        GltfAccessorReader.ReadVec3Accessor(outputAccessor, accessors, bufferViews, buffers).ToArray(),
                        sourceIndex.Value,
                        "translation");
                    break;
                default:
                    throw new InvalidDataException(
                        $"DL animation {sourceIndex} contains an unsupported channel path '{target.GetProperty("path").GetString()}'.");
            }
        }

        if (times.Length < 2 || !times.All(float.IsFinite))
        {
            throw new InvalidDataException($"DL animation {sourceIndex} requires at least two finite keyframe times.");
        }
        if (times.Zip(times.Skip(1)).Any(pair => pair.First >= pair.Second))
        {
            throw new InvalidDataException($"DL animation {sourceIndex} keyframe times must be strictly increasing.");
        }
        if (rotations.Values.Any(track => track.Length != times.Length)
            || scales.Values.Any(track => track.Length != times.Length)
            || translations.Values.Any(track => track.Length != times.Length))
        {
            throw new InvalidDataException($"DL animation {sourceIndex} channel key counts do not match its timeline.");
        }

        return new ImportedAnimation(
            new MobyGltfAnimationClip(
                sourceIndex.Value,
                animation.TryGetProperty("name", out var name) ? name.GetString() ?? $"animation_{sourceIndex:0000}" : $"animation_{sourceIndex:0000}",
                times,
                rotations,
                scales,
                translations),
            sourceFingerprint,
            rawSequence);
    }

    private static void AddTrack<T>(
        Dictionary<int, T[]> tracks,
        int joint,
        T[] values,
        int animationIndex,
        string path)
    {
        if (!tracks.TryAdd(joint, values))
        {
            throw new InvalidDataException($"DL animation {animationIndex} has duplicate {path} tracks for joint {joint}.");
        }
    }

    private static int ReadJointIndex(JsonElement node, int animationIndex)
    {
        var name = node.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        if (name is null
            || !name.StartsWith("bone_", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var joint))
        {
            throw new InvalidDataException(
                $"DL animation {animationIndex} targets node '{name ?? "<unnamed>"}', not an exported bone_#### node.");
        }

        return joint;
    }

    private static int? TryReadSourceIndexFromName(JsonElement animation)
    {
        var name = animation.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        return name is not null
            && name.StartsWith("animation_", StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var sourceIndex)
                ? sourceIndex
                : null;
    }

    private static void ReadSourceMetadata(
        JsonElement animation,
        out int? sourceIndex,
        out string? sourceFingerprint,
        out byte[]? rawSequence)
    {
        sourceIndex = null;
        sourceFingerprint = null;
        rawSequence = null;
        if (!animation.TryGetProperty("extras", out var extras)
            || !extras.TryGetProperty("RatchetPs2", out var ratchet)
            || !ratchet.TryGetProperty("mobyAnimation", out var metadata)
            || !metadata.TryGetProperty("kind", out var kind)
            || kind.GetString() != "compactAnimation")
        {
            return;
        }

        sourceIndex = metadata.GetProperty("sourceIndex").GetInt32();
        sourceFingerprint = metadata.GetProperty("sourceFingerprint").GetString();
        try
        {
            rawSequence = Convert.FromBase64String(metadata.GetProperty("sequenceBase64").GetString() ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"DL animation {sourceIndex} has invalid source sequence metadata.", ex);
        }
    }

    internal static MobySequence EncodeSequence(
        MobyModel model,
        MobyGltfAnimationClip animation,
        MobySequence? template)
    {
        RequireLoop(animation);
        var frameCount = animation.Times.Length - 1;
        if (frameCount > byte.MaxValue)
        {
            throw new InvalidDataException($"DL animation {animation.SourceIndex} has {frameCount} frames; the format limit is 255.");
        }

        var jointCount = model.JointCount;
        ValidateJointTracks(animation, jointCount);
        var rotations = BuildRotationTracks(model, animation, template);
        var scaleTracks = animation.Scales.OrderBy(track => track.Key).ToArray();
        var translationTracks = animation.Translations.OrderBy(track => track.Key).ToArray();
        var sequence = new MobySequence
        {
            Format = MobyAnimationFormat.Compact,
            BoundingSphere = Clone(template?.BoundingSphere ?? model.BoundingSphere),
            Sound = template?.Sound ?? byte.MaxValue,
            FormatMarker = 0,
            CompactAnimDataOffset = template?.Format == MobyAnimationFormat.Compact
                ? template.CompactAnimDataOffset
                : 0,
            CompactFrameDataOffset = template?.Format == MobyAnimationFormat.Compact
                ? template.CompactFrameDataOffset
                : 0,
            CompactAnimInfoData = template?.Format == MobyAnimationFormat.Compact
                ? (byte[])template.CompactAnimInfoData.Clone()
                : new byte[0x08]
        };
        if (template is not null)
        {
            foreach (var trigger in template.Triggers)
            {
                sequence.Triggers.Add(new MobyAnimationTrigger
                {
                    Unknown00 = trigger.Unknown00,
                    Unknown02 = trigger.Unknown02
                });
            }
        }

        var frameDurations = new ushort[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var ticks = checked((int)MathF.Round(
                (animation.Times[frame + 1] - animation.Times[frame]) * GameTicksPerSecond,
                MidpointRounding.AwayFromZero));
            if (ticks is < 1 or > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"DL animation {animation.SourceIndex} frame {frame} duration is outside the 1..65535 tick range.");
            }

            frameDurations[frame] = checked((ushort)ticks);
        }

        var frameIds = BuildFrameIds(template, frameDurations);
        for (var frame = 0; frame < frameCount; frame++)
        {
            sequence.CompactFrames.Add(new MobyCompactAnimationFrame
            {
                Unknown00 = unchecked((short)frameDurations[frame]),
                FrameId = unchecked((short)frameIds[frame])
            });
        }

        sequence.CompactFrameData = BuildFrameData(
            animation,
            rotations,
            scaleTracks,
            translationTracks,
            frameCount,
            Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f);
        return sequence;
    }

    private static ushort[] BuildFrameIds(MobySequence? template, IReadOnlyList<ushort> frameDurations)
    {
        var templateIds = template?.Format == MobyAnimationFormat.Compact
            ? template.CompactFrames.Select(frame => unchecked((ushort)frame.FrameId)).ToArray()
            : template?.Frames.Select(frame => (ushort)(frame.Unknown04 | frame.Unknown05 << 8)).ToArray() ?? [];
        if (templateIds.Length == frameDurations.Count
            && Enumerable.Range(0, Math.Max(0, templateIds.Length - 1)).All(index =>
                unchecked((ushort)(templateIds[index + 1] - templateIds[index]))
                == unchecked((ushort)(frameDurations[index] * 8))))
        {
            return templateIds;
        }

        var result = new ushort[frameDurations.Count];
        var frameId = templateIds.FirstOrDefault();
        for (var frame = 0; frame < result.Length; frame++)
        {
            result[frame] = frameId;
            frameId = unchecked((ushort)(frameId + frameDurations[frame] * 8));
        }
        return result;
    }

    private static Dictionary<int, Quaternion[]> BuildRotationTracks(
        MobyModel model,
        MobyGltfAnimationClip animation,
        MobySequence? template)
    {
        var result = animation.Rotations.ToDictionary(track => track.Key, track => track.Value);
        MobyGltfAnimationClip? templateAnimation = null;
        if (template is not null)
        {
            DlCompactAnimationDecoder.TryDecode(
                template,
                animation.SourceIndex,
                model.JointCount,
                Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f,
                out templateAnimation!,
                out _);
        }

        for (var joint = 0; joint < model.JointCount; joint++)
        {
            if (result.ContainsKey(joint))
            {
                continue;
            }

            result[joint] = templateAnimation?.Rotations.TryGetValue(joint, out var track) == true
                && track.Length == animation.Times.Length
                    ? track
                    : Enumerable.Repeat(Quaternion.Identity, animation.Times.Length).ToArray();
        }

        return result;
    }

    private static void ValidateJointTracks(MobyGltfAnimationClip animation, int jointCount)
    {
        foreach (var joint in animation.Rotations.Keys.Concat(animation.Scales.Keys).Concat(animation.Translations.Keys))
        {
            if (joint < 0 || joint >= jointCount)
            {
                throw new InvalidDataException(
                    $"DL animation {animation.SourceIndex} targets joint {joint}, outside the model's {jointCount} joints.");
            }
        }
    }

    private static void RequireLoop(MobyGltfAnimationClip animation)
    {
        var loops = animation.Rotations.Values.All(track => RotationEquals(track[0], track[^1]))
            && animation.Scales.Values.All(track => VectorEquals(track[0], track[^1]))
            && animation.Translations.Values.All(track => VectorEquals(track[0], track[^1]));
        if (!loops)
        {
            throw new InvalidDataException(
                $"DL animation {animation.SourceIndex} must end on its first pose because compact animations loop.");
        }
    }

    private static bool RotationEquals(Quaternion left, Quaternion right)
    {
        return MathF.Abs(Quaternion.Dot(Quaternion.Normalize(left), Quaternion.Normalize(right))) >= 0.999999f;
    }

    private static bool VectorEquals(Vector3 left, Vector3 right)
    {
        return Vector3.DistanceSquared(left, right) <= 0.0000000001f;
    }

    private static byte[] BuildFrameData(
        MobyGltfAnimationClip animation,
        IReadOnlyDictionary<int, Quaternion[]> rotations,
        IReadOnlyList<KeyValuePair<int, Vector3[]>> scaleTracks,
        IReadOnlyList<KeyValuePair<int, Vector3[]>> translationTracks,
        int frameCount,
        float modelScale)
    {
        var rotationPairCount = (rotations.Count + 1) / 2;
        var scalePairCount = (scaleTracks.Count + 1) / 2;
        var translationPairCount = (translationTracks.Count + 1) / 2;
        var pairCount = rotationPairCount + scalePairCount + translationPairCount;
        var callCount = pairCount + 2;
        if (pairCount > byte.MaxValue || callCount > byte.MaxValue)
        {
            throw new InvalidDataException($"DL animation {animation.SourceIndex} has too many transform tracks.");
        }

        var orderedRotations = rotations.OrderBy(track => track.Key).Select(track => track.Value).ToArray();
        var pairs = new List<CompactPair>(pairCount);
        for (var pair = 0; pair < rotationPairCount; pair++)
        {
            pairs.Add(BuildPair(
                frameCount,
                frame => Combine(
                    EncodeRotation(orderedRotations[pair * 2][frame]),
                    EncodeRotation(orderedRotations[Math.Min(pair * 2 + 1, orderedRotations.Length - 1)][frame]))));
        }
        for (var pair = 0; pair < scalePairCount; pair++)
        {
            pairs.Add(BuildVectorPair(scaleTracks, pair, frameCount, EncodeScale));
        }
        for (var pair = 0; pair < translationPairCount; pair++)
        {
            pairs.Add(BuildVectorPair(
                translationTracks,
                pair,
                frameCount,
                value => EncodeTranslation(value, modelScale)));
        }

        var basePairCount = pairs.Count(pair => pair.Opcode != FullOpcode);
        var staticLength = GetStaticLength(basePairCount, pairCount, callCount);
        for (var pair = pairs.Count - 1; staticLength / 0x10 > byte.MaxValue && pair >= 0; pair--)
        {
            if (pairs[pair].Opcode == FullOpcode)
            {
                continue;
            }

            pairs[pair] = pairs[pair] with { Opcode = FullOpcode };
            basePairCount--;
            staticLength = GetStaticLength(basePairCount, pairCount, callCount);
        }

        var opcodes = new byte[callCount];
        opcodes[0] = FullOpcode;
        opcodes[1] = FullOpcode;
        for (var pair = 0; pair < pairs.Count; pair++)
        {
            opcodes[pair + 2] = pairs[pair].Opcode;
        }

        var basePairs = pairs.Where(pair => pair.Opcode != FullOpcode).ToArray();
        var fullPairs = pairs.Where(pair => pair.Opcode == FullOpcode).ToArray();
        var deltaPairs = pairs.Where(pair => pair.Opcode == DeltaOpcode).ToArray();
        var fullCallCount = fullPairs.Length + 2;
        var deltaCallCount = deltaPairs.Length;
        var perFrameLength = Align(fullCallCount * 0x10 + deltaCallCount * 0x08, 0x10);
        using var stream = new MemoryStream(staticLength + frameCount * perFrameLength);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)2);
        writer.Write(checked((byte)(staticLength / 0x10)));
        writer.Write(checked((byte)(perFrameLength / 0x10)));
        writer.Write(checked((byte)basePairCount));
        writer.Write(checked((byte)pairCount));
        writer.Write(checked((byte)fullCallCount));
        writer.Write((byte)0);
        writer.Write(checked((byte)rotations.Count));
        writer.Write(checked((byte)rotationPairCount));
        writer.Write(checked((byte)scalePairCount));
        writer.Write(checked((byte)translationPairCount));
        writer.Write(new byte[5]);

        foreach (var pair in basePairs)
        {
            WriteShorts(writer, pair.BaseValues);
        }

        Span<int> destinations = stackalloc int[] { -0x20, -0x1f, -0x1e };
        // The two leading full calls consume destinations before the first transform pair.
        destinations[2] += 0x40;
        for (var pair = 0; pair < pairCount; pair++)
        {
            var call = pair + 2;
            var family = opcodes[call] switch
            {
                BaseOpcode => 0,
                DeltaOpcode => 1,
                FullOpcode => 2,
                _ => throw new InvalidDataException($"Unsupported generated DL compact opcode 0x{opcodes[call]:X2}.")
            };
            destinations[family] += 0x20;
            var destination = destinations[family] & 0x7f;
            var first = (0x80 + destination) & 0xff;
            Span<byte> route =
            [
                (byte)first,
                (byte)((first + 4) & 0xff),
                (byte)((first + 8) & 0xff),
                (byte)((first + 12) & 0xff),
                (byte)((first + 16) & 0xff),
                (byte)((first + 20) & 0xff),
                (byte)((first + 24) & 0xff),
                (byte)((first + 28) & 0xff)
            ];
            if (pair >= rotationPairCount)
            {
                var tracks = pair < rotationPairCount + scalePairCount ? scaleTracks : translationTracks;
                var trackPair = pair < rotationPairCount + scalePairCount
                    ? pair - rotationPairCount
                    : pair - rotationPairCount - scalePairCount;
                route[3] = checked((byte)tracks[trackPair * 2].Key);
                route[7] = checked((byte)tracks[Math.Min(trackPair * 2 + 1, tracks.Count - 1)].Key);
            }
            writer.Write(route);
        }

        writer.Write(opcodes);
        writer.Write((byte)0);
        writer.Write(new byte[checked((int)(staticLength - writer.BaseStream.Position))]);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var frameStart = writer.BaseStream.Position;
            writer.Write(new byte[0x20]);
            foreach (var pair in fullPairs)
            {
                WriteShorts(writer, pair.Frames[frame]);
            }
            foreach (var pair in deltaPairs)
            {
                foreach (var delta in pair.Deltas[frame])
                {
                    writer.Write(delta);
                }
            }
            writer.Write(new byte[checked((int)(perFrameLength - (writer.BaseStream.Position - frameStart)))]);
        }

        return stream.ToArray();
    }

    private static CompactPair BuildVectorPair(
        IReadOnlyList<KeyValuePair<int, Vector3[]>> tracks,
        int pair,
        int frameCount,
        Func<Vector3, short[]> encode)
    {
        return BuildPair(
            frameCount,
            frame => Combine(
                encode(tracks[pair * 2].Value[frame]),
                encode(tracks[Math.Min(pair * 2 + 1, tracks.Count - 1)].Value[frame])));
    }

    private static CompactPair BuildPair(int frameCount, Func<int, short[]> encodeFrame)
    {
        var frames = Enumerable.Range(0, frameCount).Select(encodeFrame).ToArray();
        if (frames.Skip(1).All(frame => frame.SequenceEqual(frames[0])))
        {
            return new CompactPair(BaseOpcode, frames[0], frames, []);
        }

        var baseValues = new short[8];
        for (var component = 0; component < baseValues.Length; component++)
        {
            var minimum = frames.Min(frame => (int)frame[component]);
            var maximum = frames.Max(frame => (int)frame[component]);
            if (maximum - minimum > 1020
                || frames.Any(frame => (frame[component] - frames[0][component]) % 4 != 0))
            {
                return new CompactPair(FullOpcode, [], frames, []);
            }

            baseValues[component] = checked((short)Math.Clamp(frames[0][component], maximum - 508, minimum + 512));
        }

        var deltas = frames
            .Select(frame => frame.Zip(baseValues, (value, baseValue) => checked((sbyte)((value - baseValue) / 4))).ToArray())
            .ToArray();
        return new CompactPair(DeltaOpcode, baseValues, frames, deltas);
    }

    private static short[] Combine(short[] first, short[] second)
    {
        var result = new short[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private static int GetStaticLength(int basePairCount, int pairCount, int callCount)
    {
        return Align(0x10 + basePairCount * 0x10 + pairCount * 0x08 + callCount + 1, 0x10);
    }

    private static short[] EncodeRotation(Quaternion value)
    {
        value = value.LengthSquared() > 1e-12f ? Quaternion.Normalize(value) : Quaternion.Identity;
        return
        [
            Quantize(-value.X * 32767f),
            Quantize(value.Z * 32767f),
            Quantize(-value.Y * 32767f),
            Quantize(value.W * 32767f)
        ];
    }

    private static short[] EncodeScale(Vector3 value)
    {
        return [Quantize(value.X * 4096f), Quantize(value.Z * 4096f), Quantize(value.Y * 4096f), 0];
    }

    private static short[] EncodeTranslation(Vector3 value, float modelScale)
    {
        var scale = 1024f / modelScale;
        return [Quantize(value.X * scale), Quantize(-value.Z * scale), Quantize(value.Y * scale), 0];
    }

    private static short Quantize(float value)
    {
        if (!float.IsFinite(value) || value < short.MinValue || value > short.MaxValue)
        {
            throw new InvalidDataException($"DL compact animation value {value} is outside the signed 16-bit range.");
        }

        return checked((short)MathF.Round(value, MidpointRounding.AwayFromZero));
    }

    private static void WriteShorts(BinaryWriter writer, IReadOnlyList<short> values)
    {
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static MobySequence ReadRawSequence(byte[] raw)
    {
        if (raw.Length < 0x20)
        {
            throw new InvalidDataException("DL compact animation source sequence is truncated.");
        }

        using var reader = new BinaryReader(new MemoryStream(raw, writable: false));
        var sequence = new MobySequence
        {
            Format = MobyAnimationFormat.Compact,
            RawData = raw,
            BoundingSphere = MobyBoundingSphere.Read(reader),
            FrameCount = reader.ReadByte(),
            Sound = reader.ReadByte(),
            TriggerCount = reader.ReadByte(),
            FormatMarker = reader.ReadByte(),
            CompactTriggerOffset = reader.ReadInt32(),
            CompactAnimDataOffset = reader.ReadInt32(),
            CompactFrameDataOffset = reader.ReadInt32()
        };
        EnsureRange(raw, 0x20, sequence.FrameCount * 0x04, "frame table");
        for (var i = 0; i < sequence.FrameCount; i++)
        {
            sequence.CompactFrames.Add(new MobyCompactAnimationFrame
            {
                Unknown00 = reader.ReadInt16(),
                FrameId = reader.ReadInt16()
            });
        }

        if (sequence.TriggerCount > 0)
        {
            EnsureRange(raw, sequence.CompactTriggerOffset, sequence.TriggerCount * 0x04, "trigger table");
            reader.BaseStream.Position = sequence.CompactTriggerOffset;
            for (var i = 0; i < sequence.TriggerCount; i++)
            {
                sequence.Triggers.Add(new MobyAnimationTrigger
                {
                    Unknown00 = reader.ReadInt16(),
                    Unknown02 = reader.ReadInt16()
                });
            }
        }

        if (sequence.CompactAnimDataOffset < 0
            || sequence.CompactFrameDataOffset < sequence.CompactAnimDataOffset
            || sequence.CompactFrameDataOffset > raw.Length)
        {
            throw new InvalidDataException("DL compact animation source data offsets are out of bounds.");
        }
        sequence.CompactAnimInfoData = raw[sequence.CompactAnimDataOffset..sequence.CompactFrameDataOffset];
        sequence.CompactFrameData = raw[sequence.CompactFrameDataOffset..];
        return sequence;
    }

    private static void EnsureRange(byte[] data, int offset, int length, string section)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new InvalidDataException($"DL compact animation source {section} is out of bounds.");
        }
    }

    private static MobyBoundingSphere Clone(MobyBoundingSphere source)
    {
        return new MobyBoundingSphere { X = source.X, Y = source.Y, Z = source.Z, Radius = source.Radius };
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0 ? value : value + alignment - remainder;
    }

    private sealed record ImportedAnimation(
        MobyGltfAnimationClip Clip,
        string? SourceFingerprint,
        byte[]? RawSequence);

    private sealed record CompactPair(byte Opcode, short[] BaseValues, short[][] Frames, sbyte[][] Deltas);
}

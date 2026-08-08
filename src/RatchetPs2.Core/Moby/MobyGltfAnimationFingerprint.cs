using System.Numerics;
using System.Security.Cryptography;

namespace RatchetPs2.Core.Moby;

public static class MobyGltfAnimationFingerprint
{
    public static string Compute(MobyGltfAnimationClip animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        using var data = new MemoryStream();
        using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(animation.Times.Length);
            foreach (var value in animation.Times)
            {
                writer.Write(value);
            }

            WriteTracks(writer, animation.Rotations, 4, static (output, value) =>
            {
                output.Write(value.X);
                output.Write(value.Y);
                output.Write(value.Z);
                output.Write(value.W);
            });
            WriteTracks(writer, animation.Scales, 3, WriteVector3);
            WriteTracks(writer, animation.Translations, 3, WriteVector3);
        }

        return Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, checked((int)data.Length))));
    }

    private static void WriteTracks<T>(
        BinaryWriter writer,
        IReadOnlyDictionary<int, T[]> tracks,
        byte componentCount,
        Action<BinaryWriter, T> writeValue)
    {
        writer.Write(tracks.Count);
        foreach (var (joint, values) in tracks.OrderBy(track => track.Key))
        {
            writer.Write(joint);
            writer.Write(componentCount);
            writer.Write(values.Length);
            foreach (var value in values)
            {
                writeValue(writer, value);
            }
        }
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}

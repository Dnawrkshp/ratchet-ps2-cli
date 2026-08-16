using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Experimental.Moby.Diagnostics;

public sealed record MobySkinAnalysis(
    int MeshCount,
    int DecodedMeshCount,
    int SampleCount,
    IReadOnlyList<MobySkinJointAnalysis> Joints);

public sealed record MobySkinJointAnalysis(
    ushort Joint,
    int VertexCount,
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ,
    IReadOnlyList<MobySkinJointMeshAnalysis> Meshes);

public sealed record MobySkinJointMeshAnalysis(
    int MeshIndex,
    int VertexCount,
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ);

public static class MobySkinAnalyzer
{
    public static MobySkinAnalysis AnalyzeReferenceSkin(MobyModel model, float? decodeScale = null)
    {
        var entries = model.MeshTable?.Entries;
        if (entries is null || entries.Count == 0)
        {
            return new MobySkinAnalysis(0, 0, 0, []);
        }

        var scale = decodeScale ?? ((Math.Abs(model.Scale) > 1e-8f ? model.Scale : 1f) / 1024f);
        var decoded = MobyGltfImporter.DecodeTemplateMeshes(entries, scale, model.JointCount);
        var preferredTypes = decoded
            .Where(pair => entries[pair.Key].MeshType == MobyMeshType.HighLod)
            .ToList();
        var sourceMeshes = preferredTypes.Count > 0
            ? preferredTypes
            : decoded
                .Where(pair => entries[pair.Key].MeshType is not MobyMeshType.Bangle and not MobyMeshType.Metal)
                .ToList();

        var samplesByJoint = new Dictionary<ushort, List<(int MeshIndex, Vector3 Position)>>();
        foreach (var (meshIndex, mesh) in sourceMeshes)
        {
            var count = Math.Min(mesh.Positions.Count, Math.Min(mesh.Joints.Count, mesh.Weights.Count));
            for (var i = 0; i < count; i++)
            {
                if (mesh.Weights[i].All(weight => weight <= 0.00001f))
                {
                    continue;
                }

                var joint = MobyGltfImporter.GetPrimaryJoint(mesh.Joints[i], mesh.Weights[i]);
                if (!samplesByJoint.TryGetValue(joint, out var samples))
                {
                    samples = [];
                    samplesByJoint.Add(joint, samples);
                }

                samples.Add((meshIndex, mesh.Positions[i]));
            }
        }

        var joints = samplesByJoint
            .Select(pair =>
            {
                var positions = pair.Value.Select(sample => sample.Position).ToList();
                var bounds = Bounds3.From(positions);
                var meshes = pair.Value
                    .GroupBy(sample => sample.MeshIndex)
                    .Select(group =>
                    {
                        var meshPositions = group.Select(sample => sample.Position).ToList();
                        var meshBounds = Bounds3.From(meshPositions);
                        return new MobySkinJointMeshAnalysis(
                            group.Key,
                            meshPositions.Count,
                            meshBounds.Min.X,
                            meshBounds.Min.Y,
                            meshBounds.Min.Z,
                            meshBounds.Max.X,
                            meshBounds.Max.Y,
                            meshBounds.Max.Z);
                    })
                    .OrderBy(mesh => mesh.MeshIndex)
                    .ToList();

                return new MobySkinJointAnalysis(
                    pair.Key,
                    positions.Count,
                    bounds.Min.X,
                    bounds.Min.Y,
                    bounds.Min.Z,
                    bounds.Max.X,
                    bounds.Max.Y,
                    bounds.Max.Z,
                    meshes);
            })
            .OrderBy(joint => joint.Joint)
            .ToList();

        return new MobySkinAnalysis(entries.Count, decoded.Count, samplesByJoint.Sum(pair => pair.Value.Count), joints);
    }

    public static byte[] WriteJson(MobySkinAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        return JsonSerializer.SerializeToUtf8Bytes(
            analysis,
            new JsonSerializerOptions { WriteIndented = true });
    }
}

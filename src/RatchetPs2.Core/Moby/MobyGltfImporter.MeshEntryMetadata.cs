using RatchetPs2.Core.Geometry;
using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static byte ResolveMeshTableVertexCount(ImportedMesh mesh, byte[] vifData)
    {
        if (!mesh.CustomStaticHideMesh
            && TryReadLeadingVertexDomainUnpackCount(vifData, out var vertexDomainCount))
        {
            return vertexDomainCount;
        }

        return checked((byte)mesh.Positions.Count);
    }

    private static byte ResolveGeneratedDomainCapacity(ImportedMesh mesh)
    {
        const int compactEpilogueRows = 7;
        var epilogueRows = HasUsableSkinRows(mesh)
            ? 0
            : compactEpilogueRows;
        return checked((byte)Math.Clamp(mesh.Positions.Count + epilogueRows, 1, 127));
    }

    private static byte ResolveGeneratedMeshTableVertexCount(ImportedMesh mesh, byte[] vertexData)
    {
        if (vertexData.Length >= 0x0C)
        {
            var headerCapacity = BitConverter.ToUInt16(vertexData, 0x0A);
            if (headerCapacity is > 0 and <= byte.MaxValue)
            {
                return (byte)headerCapacity;
            }
        }

        return ResolveGeneratedDomainCapacity(mesh);
    }

    private static int ResolveGeneratedMeshEntryUnknown0A(int meshTableVertexCount)
    {
        return TriangleIndexUtils.DivideRoundUp(Math.Max(0, meshTableVertexCount) * 3, 8);
    }

    private static byte ResolveGeneratedCommonTransformJointIndex(int meshTableVertexCount)
    {
        return checked((byte)Math.Min(byte.MaxValue, TriangleIndexUtils.DivideRoundUp(Math.Max(0, meshTableVertexCount), 4)));
    }

    private static bool TryGetDominantSkinJoint(ImportedMesh mesh, out byte joint)
    {
        joint = 0;
        if (mesh.Joints is null || mesh.Weights is null)
        {
            return false;
        }

        var weightByJoint = new Dictionary<ushort, float>();
        var count = Math.Min(mesh.Joints.Count, mesh.Weights.Count);
        for (var i = 0; i < count; i++)
        {
            var joints = mesh.Joints[i];
            var weights = mesh.Weights[i];
            for (var j = 0; j < Math.Min(joints.Length, weights.Length); j++)
            {
                var weight = weights[j];
                if (weight <= 0.00001f)
                {
                    continue;
                }

                weightByJoint[joints[j]] = weightByJoint.TryGetValue(joints[j], out var current)
                    ? current + weight
                    : weight;
            }
        }

        if (weightByJoint.Count == 0)
        {
            return false;
        }

        joint = checked((byte)Math.Min(byte.MaxValue, weightByJoint
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .First().Key));
        return true;
    }

    private static bool TryGetDominantHeadSkinJoint(
        ImportedMesh mesh,
        MobyAnimationFormat animationFormat,
        out byte joint)
    {
        joint = 0;
        if (!TryGetDominantSkinJoint(mesh, out var dominantJoint))
        {
            return false;
        }

        if (!IsHeadCommonTransformJoint(dominantJoint, animationFormat))
        {
            return false;
        }

        if (!IsUpperCenterMesh(mesh))
        {
            return false;
        }

        joint = dominantJoint;
        return true;
    }

    private static bool IsUpperCenterMesh(ImportedMesh mesh)
    {
        if (mesh.Positions.Count == 0)
        {
            return false;
        }

        var bounds = Bounds3.From(mesh.Positions);
        var centerZ = (bounds.Min.Z + bounds.Max.Z) * 0.5f;
        return bounds.Max.Y >= 1.05f
            && MathF.Abs(centerZ) <= 0.16f;
    }

    private static bool IsHeadCommonTransformJoint(byte joint, MobyAnimationFormat animationFormat)
    {
        return animationFormat == MobyAnimationFormat.Compact
            ? joint is 10 or 12
            : joint is 3 or 4 or 5 or 6 or 7;
    }

    private static bool TryGetDominantReferenceMeshCommonTransform(
        ImportedMesh mesh,
        MobyModel? skinReferenceModel,
        out byte commonTransformJoint,
        out int sourceMeshIndex)
    {
        commonTransformJoint = 0;
        sourceMeshIndex = -1;
        if (skinReferenceModel?.MeshTable?.Entries is not { Count: > 0 } entries
            || mesh.SkinTransferDiagnostics.Count == 0)
        {
            return false;
        }

        sourceMeshIndex = mesh.SkinTransferDiagnostics
            .Select(diagnostic => diagnostic.NearestSampleMeshIndex)
            .Where(index => index >= 0 && index < entries.Count)
            .GroupBy(index => index)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault(-1);
        if (sourceMeshIndex < 0)
        {
            return false;
        }

        commonTransformJoint = entries[sourceMeshIndex].CommonTransformJointIndex;
        return true;
    }

    private static bool TryReadLeadingVertexDomainUnpackCount(byte[] vifData, out byte count)
    {
        count = 0;
        foreach (var packet in Ps2VifPacket.ReadSpans(vifData))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x05)
            {
                continue;
            }

            if (packet.Num == 0)
            {
                return false;
            }

            count = packet.Num;
            return true;
        }

        return false;
    }
}

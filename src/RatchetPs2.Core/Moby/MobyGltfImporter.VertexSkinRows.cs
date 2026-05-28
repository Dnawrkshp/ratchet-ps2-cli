using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static VertexBuildResult BuildVertexData(
        MobyMeshTableEntry templateEntry,
        ImportedMesh mesh,
        float scale,
        MobyGltfImportOptions options,
        ref int quantizationClipCount,
        ref int truncatedInfluenceCount)
    {
        var sourceVertices = BuildSourceVertices(mesh, templateEntry.CommonTransformJointIndex, options, ref truncatedInfluenceCount);
        var uniqueJoints = sourceVertices
            .SelectMany(vertex => vertex.Influences.Select(influence => influence.Joint))
            .Distinct()
            .Order()
            .ToList();
        if (uniqueJoints.Count >= 64)
        {
            throw new InvalidDataException(
                $"Mesh {mesh.TemplateMeshIndex:0000} uses {uniqueJoints.Count} joints. v1 importer supports at most 63 unique joints per mesh.");
        }

        var jointAddressByJoint = new Dictionary<ushort, byte>();
        for (var i = 0; i < uniqueJoints.Count; i++)
        {
            jointAddressByJoint.Add(uniqueJoints[i], checked((byte)(i * 4)));
        }

        var scratchAddress = checked((byte)(uniqueJoints.Count * 4));
        var orderedVertices = sourceVertices
            .Where(vertex => vertex.Influences.Count == 2)
            .Concat(sourceVertices.Where(vertex => vertex.Influences.Count >= 3))
            .Concat(sourceVertices.Where(vertex => vertex.Influences.Count == 1))
            .ToList();
        var twoWayCount = orderedVertices.Count(vertex => vertex.Influences.Count == 2);
        var threeWayCount = orderedVertices.Count(vertex => vertex.Influences.Count >= 3);
        var mainCount = orderedVertices.Count - twoWayCount - threeWayCount;
        var indexByOriginalIndex = new int[sourceVertices.Count];
        for (var i = 0; i < orderedVertices.Count; i++)
        {
            indexByOriginalIndex[orderedVertices[i].OriginalIndex] = i;
        }

        var vertexCount = orderedVertices.Count;
        var epilogueCount = 7;
        var vertexTableOffset = Align(0x10 + uniqueJoints.Count * 2, 0x10);
        var data = new byte[vertexTableOffset + (vertexCount + epilogueCount) * 0x10];

        WriteUInt16(data, 0x00, checked((ushort)uniqueJoints.Count));
        WriteUInt16(data, 0x02, checked((ushort)twoWayCount));
        WriteUInt16(data, 0x04, checked((ushort)threeWayCount));
        WriteUInt16(data, 0x06, checked((ushort)mainCount));
        WriteUInt16(data, 0x08, 0); // duplicate vertices
        var headerDomainCapacity = options.CustomStatic && options.CustomStaticGenerateVertexHeaderDomainCapacity
            ? ResolveGeneratedDomainCapacity(mesh)
            : checked((byte)vertexCount);
        WriteUInt16(data, 0x0A, headerDomainCapacity);
        WriteUInt16(data, 0x0C, checked((ushort)vertexTableOffset));

        for (var i = 0; i < uniqueJoints.Count; i++)
        {
            data[0x10 + i * 2] = checked((byte)uniqueJoints[i]);
            data[0x10 + i * 2 + 1] = checked((byte)(i * 4));
        }

        var lowVertexIndices = new ushort[vertexCount];
        var highJointBits = new ushort[vertexCount];
        for (var i = 0; i < vertexCount; i++)
        {
            var vertex = orderedVertices[i];
            var offset = vertexTableOffset + i * 0x10;
            var source = options.CustomStaticSkinPositionsRelativeToBind
                ? GetWeightedBindLocalPosition(mesh, vertex.Position, vertex.Influences)
                : vertex.Position;
            var x = Quantize(source.X / scale, ref quantizationClipCount);
            var sourceY = Quantize(-source.Z / scale, ref quantizationClipCount);
            var sourceZ = Quantize(source.Y / scale, ref quantizationClipCount);

            lowVertexIndices[i] = checked((ushort)(i & 0x01FF));
            highJointBits[i] = BuildSkinVertexBytes(data, offset, vertex, jointAddressByJoint, scratchAddress);

            WriteUInt16(data, offset, highJointBits[i]);
            WriteInt16(data, offset + 0x0A, x);
            WriteInt16(data, offset + 0x0C, sourceY);
            WriteInt16(data, offset + 0x0E, sourceZ);
        }

        for (var i = 7; i < vertexCount; i++)
        {
            WriteUInt16(data, vertexTableOffset + i * 0x10, checked((ushort)(highJointBits[i] | lowVertexIndices[i - 7])));
        }

        for (var epilogue = 0; epilogue < epilogueCount; epilogue++)
        {
            var destination = vertexCount + epilogue - 7;
            if (destination >= 0 && destination < lowVertexIndices.Length)
            {
                WriteUInt16(data, vertexTableOffset + (vertexCount + epilogue) * 0x10, lowVertexIndices[destination]);
            }
        }

        return new VertexBuildResult(
            data,
            indexByOriginalIndex,
            UsedTemplateVertexData: false,
            UsedMetadataVertexLayout: false,
            UsedMetadataRowPrefixes: false,
            UsedMetadataLowVertexBits: false);
    }

    private static bool HasUsableSkinRows(ImportedMesh mesh)
    {
        return mesh.Joints is not null
            && mesh.Weights is not null
            && mesh.Joints.Count == mesh.Positions.Count
            && mesh.Weights.Count == mesh.Positions.Count
            && mesh.Weights.Any(row => row.Any(weight => weight > 0.00001f));
    }

    private static Vector3 GetWeightedBindLocalPosition(
        ImportedMesh mesh,
        Vector3 position,
        IReadOnlyList<MobySkinInfluence> influences)
    {
        if (mesh.RigBindWorldToLocalTransforms is not null && influences.Count > 0)
        {
            var total = 0f;
            var localPosition = Vector3.Zero;
            foreach (var influence in influences)
            {
                if (!mesh.RigBindWorldToLocalTransforms.TryGetValue(influence.Joint, out var worldToLocal))
                {
                    continue;
                }

                localPosition += Vector3.Transform(position, worldToLocal) * influence.Weight;
                total += influence.Weight;
            }

            if (total > 0.0001f)
            {
                return localPosition / total;
            }
        }

        return position - GetWeightedBindPosition(mesh, influences);
    }

    private static Vector3 GetWeightedBindPosition(
        ImportedMesh mesh,
        IReadOnlyList<MobySkinInfluence> influences)
    {
        if (mesh.RigBindWorldPositions is null || influences.Count == 0)
        {
            return Vector3.Zero;
        }

        var total = 0f;
        var position = Vector3.Zero;
        foreach (var influence in influences)
        {
            if (!mesh.RigBindWorldPositions.TryGetValue(influence.Joint, out var jointPosition))
            {
                continue;
            }

            position += jointPosition * influence.Weight;
            total += influence.Weight;
        }

        return total > 0.0001f ? position / total : Vector3.Zero;
    }

    private static ushort BuildSkinVertexBytes(
        byte[] data,
        int offset,
        SourceVertex vertex,
        IReadOnlyDictionary<ushort, byte> jointAddressByJoint,
        byte scratchAddress)
    {
        if (vertex.Influences.Count == 1)
        {
            var joint = vertex.Influences[0].Joint;
            data[offset + 0x02] = jointAddressByJoint[joint];
            data[offset + 0x03] = scratchAddress;
            data[offset + 0x04] = 255;
            data[offset + 0x05] = 0;
            data[offset + 0x06] = 0;
            data[offset + 0x07] = scratchAddress;
            data[offset + 0x08] = 0;
            data[offset + 0x09] = 0;
            return checked((ushort)((joint & 0x7F) << 9));
        }

        if (vertex.Influences.Count == 2)
        {
            var weights = QuantizeWeights(vertex.Influences);
            data[offset + 0x02] = jointAddressByJoint[vertex.Influences[0].Joint];
            data[offset + 0x03] = jointAddressByJoint[vertex.Influences[1].Joint];
            data[offset + 0x04] = weights[0];
            data[offset + 0x05] = weights[1];
            data[offset + 0x06] = scratchAddress;
            data[offset + 0x07] = scratchAddress;
            return checked((ushort)((vertex.Influences[0].Joint & 0x7F) << 9));
        }

        var threeWeights = QuantizeWeights(vertex.Influences);
        var thirdJointAddress = jointAddressByJoint[vertex.Influences[2].Joint];
        if (thirdJointAddress % 2 != 0)
        {
            throw new InvalidDataException("Internal skin encoding error: third-joint address must be even.");
        }

        data[offset + 0x02] = jointAddressByJoint[vertex.Influences[0].Joint];
        data[offset + 0x03] = jointAddressByJoint[vertex.Influences[1].Joint];
        data[offset + 0x04] = threeWeights[0];
        data[offset + 0x05] = threeWeights[1];
        data[offset + 0x06] = threeWeights[2];
        data[offset + 0x07] = scratchAddress;
        return checked((ushort)(((thirdJointAddress / 2) & 0x7F) << 9));
    }

    private static List<SourceVertex> BuildSourceVertices(
        ImportedMesh mesh,
        byte fallbackJoint,
        MobyGltfImportOptions options,
        ref int truncatedInfluenceCount)
    {
        var vertices = new List<SourceVertex>(mesh.Positions.Count);
        for (var i = 0; i < mesh.Positions.Count; i++)
        {
            var influences = ReadInfluences(mesh, i, fallbackJoint);
            if (influences.Count > options.MaxInfluences)
            {
                truncatedInfluenceCount++;
                influences = influences
                    .OrderByDescending(influence => influence.Weight)
                    .Take(options.MaxInfluences)
                    .ToList();
            }

            NormalizeInfluences(influences);
            vertices.Add(new SourceVertex(i, mesh.Positions[i], influences));
        }

        return vertices;
    }

    private static List<MobySkinInfluence> ReadInfluences(ImportedMesh mesh, int vertexIndex, byte fallbackJoint)
    {
        if (mesh.Joints is null || mesh.Weights is null || vertexIndex >= mesh.Joints.Count || vertexIndex >= mesh.Weights.Count)
        {
            return [new MobySkinInfluence(fallbackJoint, 1f)];
        }

        var influences = new List<MobySkinInfluence>();
        for (var i = 0; i < 4; i++)
        {
            var weight = mesh.Weights[vertexIndex][i];
            if (weight <= 0.00001f)
            {
                continue;
            }

            var joint = checked((ushort)Math.Clamp((int)mesh.Joints[vertexIndex][i], 0, 127));
            var existingIndex = influences.FindIndex(influence => influence.Joint == joint);
            if (existingIndex >= 0)
            {
                influences[existingIndex] = influences[existingIndex] with { Weight = influences[existingIndex].Weight + weight };
            }
            else
            {
                influences.Add(new MobySkinInfluence(joint, weight));
            }
        }

        if (influences.Count == 0)
        {
            return [new MobySkinInfluence(fallbackJoint, 1f)];
        }

        return influences;
    }

    private static void NormalizeInfluences(List<MobySkinInfluence> influences)
    {
        var total = influences.Sum(influence => influence.Weight);
        if (total <= 0f)
        {
            influences.Clear();
            influences.Add(new MobySkinInfluence(0, 1f));
            return;
        }

        for (var i = 0; i < influences.Count; i++)
        {
            influences[i] = influences[i] with { Weight = influences[i].Weight / total };
        }
    }

    private static byte[] QuantizeWeights(IReadOnlyList<MobySkinInfluence> influences)
    {
        var count = Math.Min(3, influences.Count);
        var bytes = new byte[count];
        for (var i = 0; i < count; i++)
        {
            bytes[i] = checked((byte)Math.Clamp((int)MathF.Round(influences[i].Weight * 255f), 0, 255));
        }

        return bytes;
    }
}

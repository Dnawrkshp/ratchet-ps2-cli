using System.Numerics;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    internal static Dictionary<int, TemplateDecodedMesh> DecodeTemplateMeshes(
        IReadOnlyList<MobyMeshTableEntry> entries,
        float scale,
        int jointCount)
    {
        var result = new Dictionary<int, TemplateDecodedMesh>();
        var rollingVertexCache = new Vector3?[512];
        var rollingJointCache = new ushort[512][];
        var rollingWeightCache = new float[512][];
        var rollingBlendCache = new TemplateSkinBlend?[512];

        for (var i = 0; i < entries.Count; i++)
        {
            if (TryDecodeTemplateMesh(entries[i], scale, jointCount, rollingVertexCache, rollingJointCache, rollingWeightCache, rollingBlendCache, out var mesh))
            {
                result[i] = mesh;
            }
        }

        return result;
    }

    private static bool TryDecodeTemplateMesh(
        MobyMeshTableEntry entry,
        float scale,
        int jointCount,
        Vector3?[] rollingVertexCache,
        ushort[][] rollingJointCache,
        float[][] rollingWeightCache,
        TemplateSkinBlend?[] rollingBlendCache,
        out TemplateDecodedMesh mesh)
    {
        mesh = new TemplateDecodedMesh([], [], []);
        var data = entry.VertexData;
        if (entry.MeshType == MobyMeshType.Metal)
        {
            if (data.Length < 0x10
                || BitConverter.ToUInt16(data, 0x00) != entry.VertexCount
                || data.Length < 0x10 + entry.VertexCount * 0x10)
            {
                return false;
            }

            var positions = new List<Vector3>(entry.VertexCount);
            for (var i = 0; i < entry.VertexCount; i++)
            {
                var offset = 0x10 + i * 0x10;
                positions.Add(new Vector3(
                    BitConverter.ToInt16(data, offset) * scale,
                    BitConverter.ToInt16(data, offset + 4) * scale,
                    -BitConverter.ToInt16(data, offset + 2) * scale));
            }

            mesh = new TemplateDecodedMesh(
                positions,
                Enumerable.Repeat<ushort[]>([
                    entry.CommonTransformJointIndex < jointCount ? entry.CommonTransformJointIndex : (ushort)0,
                    0,
                    0,
                    0
                ], entry.VertexCount).ToList(),
                Enumerable.Repeat<float[]>([1f, 0f, 0f, 0f], entry.VertexCount).ToList());
            return true;
        }

        if (data.Length < 0x20)
        {
            return false;
        }

        try
        {
            var matrixTransferCount = BitConverter.ToUInt16(data, 0x00);
            var twoWayBlendVertexCount = BitConverter.ToUInt16(data, 0x02);
            var threeWayBlendVertexCount = BitConverter.ToUInt16(data, 0x04);
            var mainVertexCount = BitConverter.ToUInt16(data, 0x06);
            var duplicateVertexCount = BitConverter.ToUInt16(data, 0x08);
            var vertexTableOffset = BitConverter.ToUInt16(data, 0x0C);
            var inFileVertexCount = twoWayBlendVertexCount + threeWayBlendVertexCount + mainVertexCount;

            for (var i = 0; i < matrixTransferCount; i++)
            {
                var offset = 0x10 + i * 2;
                if (offset + 2 > data.Length)
                {
                    break;
                }

                var sprJointIndex = unchecked((sbyte)data[offset]);
                var vu0DestinationAddress = data[offset + 1];
                if (vu0DestinationAddress % 4 == 0)
                {
                    var slot = vu0DestinationAddress / 4;
                    if (slot >= 0 && slot < rollingBlendCache.Length)
                    {
                        rollingBlendCache[slot] = new TemplateSkinBlend(1, sprJointIndex, 0, 0, 255, 0, 0);
                    }
                }
            }

            if (vertexTableOffset <= 0 || vertexTableOffset % 0x10 != 0 || vertexTableOffset > data.Length || inFileVertexCount <= 0)
            {
                return false;
            }

            var vertexDataSizeQw = data.Length / 0x10;
            var epilogueVertexCount = vertexDataSizeQw - (vertexTableOffset / 0x10) - inFileVertexCount;
            if (epilogueVertexCount < 0 || epilogueVertexCount > 64)
            {
                return false;
            }

            var duplicateIndicesOffset = 0x10 + matrixTransferCount * 2;
            if (duplicateIndicesOffset % 4 != 0)
            {
                duplicateIndicesOffset += 2;
            }
            if (duplicateIndicesOffset % 8 != 0)
            {
                duplicateIndicesOffset += 4;
            }

            var duplicateVertexIndices = new List<int>(duplicateVertexCount);
            for (var i = 0; i < duplicateVertexCount; i++)
            {
                var offset = duplicateIndicesOffset + i * 2;
                if (offset + 2 > data.Length)
                {
                    break;
                }

                duplicateVertexIndices.Add((BitConverter.ToUInt16(data, offset) >> 7) & 0x01FF);
            }

            var vertices = new List<byte[]>(inFileVertexCount);
            var vertexOffset = vertexTableOffset;
            for (var i = 0; i < inFileVertexCount; i++)
            {
                if (vertexOffset + 0x10 > data.Length)
                {
                    return false;
                }

                vertices.Add(data[vertexOffset..(vertexOffset + 0x10)]);
                vertexOffset += 0x10;
            }

            for (var i = 7; i < vertices.Count; i++)
            {
                WriteLow9Bits(vertices[i - 7], ReadLowHalfword(vertices[i]));
            }

            var epilogueReadOffset = vertexTableOffset + inFileVertexCount * 0x10;
            epilogueReadOffset += Math.Max(7 - inFileVertexCount, 0) * 0x10;
            for (var i = Math.Max(7 - inFileVertexCount, 0); i < epilogueVertexCount; i++)
            {
                if (epilogueReadOffset + 0x10 > data.Length)
                {
                    break;
                }

                var destinationIndex = inFileVertexCount + i - 7;
                if (destinationIndex >= 0 && destinationIndex < vertices.Count)
                {
                    WriteLow9Bits(vertices[destinationIndex], BitConverter.ToUInt16(data, epilogueReadOffset));
                }

                epilogueReadOffset += 0x10;
            }

            var lastVertexOffset = epilogueReadOffset - 0x10;
            if (lastVertexOffset < 0 || lastVertexOffset + 0x10 > data.Length)
            {
                lastVertexOffset = Math.Max(vertexTableOffset, Math.Min(data.Length - 0x10, vertexTableOffset + (inFileVertexCount - 1) * 0x10));
            }

            for (var i = Math.Max(7 - inFileVertexCount - epilogueVertexCount, 0); i < 6; i++)
            {
                var destinationIndex = inFileVertexCount + epilogueVertexCount + i - 7;
                if (destinationIndex >= 0 && destinationIndex < vertices.Count)
                {
                    WriteLow9Bits(vertices[destinationIndex], BitConverter.ToUInt16(data, lastVertexOffset + 0x04 + i * 2));
                }
            }

            var positions = new List<Vector3>(entry.VertexCount);
            var joints = new List<ushort[]>(entry.VertexCount);
            var weights = new List<float[]>(entry.VertexCount);
            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                var vertexIndex = ReadLowHalfword(vertex) & 0x01FF;
                var position = DecodeTemplatePosition(vertex, scale);
                var (jointRow, weightRow) = DecodeTemplateSkinRow(vertex, i, twoWayBlendVertexCount, threeWayBlendVertexCount, rollingBlendCache);
                positions.Add(position);
                joints.Add(jointRow);
                weights.Add(weightRow);
                if (vertexIndex >= 0 && vertexIndex < rollingVertexCache.Length)
                {
                    rollingVertexCache[vertexIndex] = position;
                    rollingJointCache[vertexIndex] = jointRow;
                    rollingWeightCache[vertexIndex] = weightRow;
                }
            }

            foreach (var duplicateIndex in duplicateVertexIndices)
            {
                if (duplicateIndex >= 0 && duplicateIndex < rollingVertexCache.Length && rollingVertexCache[duplicateIndex].HasValue)
                {
                    positions.Add(rollingVertexCache[duplicateIndex]!.Value);
                    joints.Add(rollingJointCache[duplicateIndex] ?? DefaultTemplateJoints());
                    weights.Add(rollingWeightCache[duplicateIndex] ?? DefaultTemplateWeights());
                    continue;
                }

                positions.Add(positions.Count > 0 ? positions[^1] : Vector3.Zero);
                joints.Add(DefaultTemplateJoints());
                weights.Add(DefaultTemplateWeights());
            }

            mesh = new TemplateDecodedMesh(positions, joints, weights);
            return positions.Count == entry.VertexCount;
        }
        catch
        {
            return false;
        }
    }

    private static Vector3 DecodeTemplatePosition(byte[] vertex, float scale)
    {
        var x = BitConverter.ToInt16(vertex, 0x0A) * scale;
        var sourceY = BitConverter.ToInt16(vertex, 0x0C) * scale;
        var sourceZ = BitConverter.ToInt16(vertex, 0x0E) * scale;
        return new Vector3(x, sourceZ, -sourceY);
    }

    private static (ushort[] Joints, float[] Weights) DecodeTemplateSkinRow(
        byte[] vertex,
        int vertexNumber,
        ushort twoWayBlendVertexCount,
        ushort threeWayBlendVertexCount,
        TemplateSkinBlend?[] rollingBlendCache)
    {
        var bits9To15 = (sbyte)((ReadLowHalfword(vertex) >> 9) & 0x7F);
        TemplateSkinBlend blend;
        if (vertexNumber < twoWayBlendVertexCount)
        {
            var source1 = LoadTemplateSkinBlend(rollingBlendCache, vertex[2]);
            var source2 = LoadTemplateSkinBlend(rollingBlendCache, vertex[3]);
            StoreTemplateSkinBlend(rollingBlendCache, vertex[6], new TemplateSkinBlend(1, bits9To15, 0, 0, 255, 0, 0));
            blend = new TemplateSkinBlend(2, source1.Joint0, source2.Joint0, 0, vertex[4], vertex[5], 0);
            StoreTemplateSkinBlend(rollingBlendCache, vertex[7], blend);
        }
        else if (vertexNumber < twoWayBlendVertexCount + threeWayBlendVertexCount)
        {
            var source1 = LoadTemplateSkinBlend(rollingBlendCache, vertex[2]);
            var source2 = LoadTemplateSkinBlend(rollingBlendCache, vertex[3]);
            var source3 = LoadTemplateSkinBlend(rollingBlendCache, (byte)(bits9To15 * 2));
            blend = new TemplateSkinBlend(3, source1.Joint0, source2.Joint0, source3.Joint0, vertex[4], vertex[5], vertex[6]);
            StoreTemplateSkinBlend(rollingBlendCache, vertex[7], blend);
        }
        else
        {
            StoreTemplateSkinBlend(rollingBlendCache, vertex[3], new TemplateSkinBlend(1, bits9To15, 0, 0, 255, 0, 0));
            blend = LoadTemplateSkinBlend(rollingBlendCache, vertex[2]);
            if (blend.Count == 1 && blend.Joint0 == 0 && bits9To15 != 0)
            {
                blend = new TemplateSkinBlend(1, bits9To15, 0, 0, 255, 0, 0);
            }
        }

        return TemplateSkinBlendToRows(blend);
    }

    private static TemplateSkinBlend LoadTemplateSkinBlend(TemplateSkinBlend?[] rollingBlendCache, byte vu0Address)
    {
        if (vu0Address % 4 == 0)
        {
            var slot = vu0Address / 4;
            if (slot >= 0 && slot < rollingBlendCache.Length && rollingBlendCache[slot].HasValue)
            {
                return rollingBlendCache[slot]!.Value;
            }
        }

        return new TemplateSkinBlend(1, 0, 0, 0, 255, 0, 0);
    }

    private static void StoreTemplateSkinBlend(TemplateSkinBlend?[] rollingBlendCache, byte vu0Address, TemplateSkinBlend blend)
    {
        if (vu0Address % 4 != 0)
        {
            return;
        }

        var slot = vu0Address / 4;
        if (slot >= 0 && slot < rollingBlendCache.Length)
        {
            rollingBlendCache[slot] = blend;
        }
    }

    private static (ushort[] Joints, float[] Weights) TemplateSkinBlendToRows(TemplateSkinBlend blend)
    {
        var joints = new[]
        {
            ToTemplateJointIndex(blend.Joint0),
            ToTemplateJointIndex(blend.Joint1),
            ToTemplateJointIndex(blend.Joint2),
            (ushort)0
        };
        var weights = new[]
        {
            blend.Weight0 / 255f,
            blend.Count >= 2 ? blend.Weight1 / 255f : 0f,
            blend.Count >= 3 ? blend.Weight2 / 255f : 0f,
            0f
        };

        NormalizeTemplateWeights(weights);
        return (joints, weights);
    }

    private static ushort ToTemplateJointIndex(sbyte joint) => joint < 0 ? (ushort)0 : (ushort)joint;

    private static ushort[] DefaultTemplateJoints() => [0, 0, 0, 0];

    private static float[] DefaultTemplateWeights() => [1f, 0f, 0f, 0f];

    private static void NormalizeTemplateWeights(float[] weights)
    {
        var sum = weights.Sum();
        if (sum <= 0.00001f)
        {
            weights[0] = 1f;
            weights[1] = 0f;
            weights[2] = 0f;
            weights[3] = 0f;
            return;
        }

        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] /= sum;
        }
    }

}

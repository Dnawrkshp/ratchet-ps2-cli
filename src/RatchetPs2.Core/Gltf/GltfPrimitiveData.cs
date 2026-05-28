using System.Numerics;

namespace RatchetPs2.Core.Gltf;

public sealed record GltfPrimitiveData(
    int MeshIndex,
    int PrimitiveIndex,
    int? MaterialIndex,
    string? MaterialName,
    List<Vector3> Positions,
    List<uint> Indices,
    List<Vector2>? TexCoords,
    List<ushort[]>? Joints,
    List<float[]>? Weights);

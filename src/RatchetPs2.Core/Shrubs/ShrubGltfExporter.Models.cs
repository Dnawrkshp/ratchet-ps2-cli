using System.Numerics;

namespace RatchetPs2.Core.Shrubs;

public static partial class ShrubGltfExporter
{
    private sealed class ShrubPrimitiveGroup
    {
        public ShrubPrimitiveGroup(int textureId, int packetIndex, int firstSourcePrimitiveIndex)
        {
            TextureId = textureId;
            PacketIndex = packetIndex;
            FirstSourcePrimitiveIndex = firstSourcePrimitiveIndex;
            LastSourcePrimitiveIndex = firstSourcePrimitiveIndex;
        }

        public int TextureId { get; }

        public int PacketIndex { get; }

        public int FirstSourcePrimitiveIndex { get; }

        public int LastSourcePrimitiveIndex { get; set; }

        public int SourceVertexCount { get; set; }

        public int TriangleCount { get; set; }

        public int WindingCorrectedTriangleCount { get; set; }

        public List<Vector3> Positions { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector2> TexCoords { get; } = [];

        public List<uint> Indices { get; } = [];
    }

    private sealed record ShrubMesh(IReadOnlyList<ShrubPrimitiveGroup> Groups)
    {
        public int VertexCount => Groups.Sum(group => group.Positions.Count);

        public int TriangleCount => Groups.Sum(group => group.TriangleCount);

        public IReadOnlyList<int> TextureIds => Groups
            .Select(group => group.TextureId)
            .Distinct()
            .Order()
            .ToArray();
    }

    private sealed record ShrubMaterialBuildResult(
        List<Dictionary<string, object>> Materials,
        Dictionary<int, int> MaterialIndexByTextureId,
        IReadOnlyList<int> TextureIds);

    private sealed record ShrubMeshBounds(Vector3 Min, Vector3 Max)
    {
        public Vector3 Size => Max - Min;

        public Vector3 Center => (Min + Max) * 0.5f;
    }

    private sealed record ShrubBillboardPreview(
        float Width,
        float Height,
        float CenterY,
        string SizingMode);

    private sealed record ShrubBillboardMeshBuild(object Mesh, object Node);
}

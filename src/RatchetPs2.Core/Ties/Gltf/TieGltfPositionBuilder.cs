using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfPositionBuilder
{
    public static List<Vector3> BuildPositions(TieClass tie, TieLodTopology topology)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);

        var positions = new List<Vector3>(topology.LogicalVertices.Count);
        foreach (var vertex in topology.LogicalVertices.OrderBy(vertex => vertex.LogicalVertexIndex))
        {
            if (vertex.DecodedVertex is null && vertex.VertexRow is null && vertex.AddressRow is null)
            {
                throw new InvalidDataException(
                    $"Tie LOD {topology.LodIndex} logical vertex {vertex.LogicalVertexIndex} has no decoded vertex.");
            }

            positions.Add(ToGltfPosition(tie, vertex));
        }

        return positions;
    }

    private static Vector3 ToGltfPosition(TieClass tie, TieLogicalVertex vertex)
    {
        if (vertex.DecodedVertex is { } decodedVertex)
        {
            return ToGltfPosition(tie, decodedVertex.X, decodedVertex.Y, decodedVertex.Z);
        }

        var row = vertex.VertexRow ?? vertex.AddressRow!;
        if (TiePacketVertexRowClassifier.TrySelectPositionSlot(row, out var slot)
            && slot == TiePacketVertexPositionSlot.Second)
        {
            return ToGltfPosition(tie, row.Data0, row.Data1, row.Data2);
        }

        return ToGltfPosition(tie, row.X, row.Y, row.Z);
    }

    private static Vector3 ToGltfPosition(TieClass tie, short sourceX, short sourceY, short sourceZ)
    {
        return GltfCoordinateBasis.FromPs2Position(
            sourceX,
            sourceY,
            sourceZ,
            tie.Header.Scale / 1024f);
    }
}

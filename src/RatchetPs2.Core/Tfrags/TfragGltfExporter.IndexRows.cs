using System.Numerics;
using RatchetPs2.Core.Gltf;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private static TfragTopologyDecode DecodeIndexRowTopologyPacket(
        TfragTopologyPacket packet,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<int> topologyPositionLookup,
        IReadOnlyList<Vector2?> referenceTexCoords,
        TfragResolvedTexture fallbackTexture,
        float? maxTriangleEdgeLength)
    {
        var indices = new List<uint>();
        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        var referenceAddresses = new List<int>();
        var rawTriangleCount = 0;
        var rejectedDegenerateTriangleCount = 0;
        var rejectedDuplicateTriangleCount = 0;
        var rejectedInvalidTriangleCount = 0;
        var rejectedLongEdgeTriangleCount = 0;
        var alternateDiagonalRowCount = 0;

        for (var offset = 0; offset + 3 < packet.Payload.Length; offset += 4)
        {
            var referenceA = MapTopologyReferenceAddress(packet, packet.Payload[offset + 0], offset + 0);
            var referenceB = MapTopologyReferenceAddress(packet, packet.Payload[offset + 1], offset + 1);
            var referenceC = MapTopologyReferenceAddress(packet, packet.Payload[offset + 2], offset + 2);
            var referenceD = MapTopologyReferenceAddress(packet, packet.Payload[offset + 3], offset + 3);
            var a = ResolveTopologyPosition(referenceA, topologyPositionLookup);
            var b = ResolveTopologyPosition(referenceB, topologyPositionLookup);
            var c = ResolveTopologyPosition(referenceC, topologyPositionLookup);
            var d = ResolveTopologyPosition(referenceD, topologyPositionLookup);
            if (ShouldUseAlternateIndexRowDiagonal(
                a,
                b,
                c,
                d,
                referenceA,
                referenceB,
                referenceC,
                referenceD,
                positions,
                packet.ReferenceTexCoords,
                referenceTexCoords,
                fallbackTexture))
            {
                alternateDiagonalRowCount++;
                AppendIndexRowTriangleCandidate(
                    a,
                    b,
                    d,
                    referenceA,
                    referenceB,
                    referenceD,
                    positions,
                    indices,
                    referenceAddresses,
                    seenTriangles,
                    ref rawTriangleCount,
                    ref rejectedDegenerateTriangleCount,
                    ref rejectedInvalidTriangleCount,
                    ref rejectedDuplicateTriangleCount,
                    ref rejectedLongEdgeTriangleCount,
                    maxTriangleEdgeLength);
                AppendIndexRowTriangleCandidate(
                    a,
                    d,
                    c,
                    referenceA,
                    referenceD,
                    referenceC,
                    positions,
                    indices,
                    referenceAddresses,
                    seenTriangles,
                    ref rawTriangleCount,
                    ref rejectedDegenerateTriangleCount,
                    ref rejectedInvalidTriangleCount,
                    ref rejectedDuplicateTriangleCount,
                    ref rejectedLongEdgeTriangleCount,
                    maxTriangleEdgeLength);
                continue;
            }

            AppendIndexRowTriangleCandidate(
                a,
                b,
                c,
                referenceA,
                referenceB,
                referenceC,
                positions,
                indices,
                referenceAddresses,
                seenTriangles,
                ref rawTriangleCount,
                ref rejectedDegenerateTriangleCount,
                ref rejectedInvalidTriangleCount,
                ref rejectedDuplicateTriangleCount,
                ref rejectedLongEdgeTriangleCount,
                maxTriangleEdgeLength);
            AppendIndexRowTriangleCandidate(
                c,
                b,
                d,
                referenceC,
                referenceB,
                referenceD,
                positions,
                indices,
                referenceAddresses,
                seenTriangles,
                ref rawTriangleCount,
                ref rejectedDegenerateTriangleCount,
                ref rejectedInvalidTriangleCount,
                ref rejectedDuplicateTriangleCount,
                ref rejectedLongEdgeTriangleCount,
                maxTriangleEdgeLength);
        }

        return new TfragTopologyDecode(
            packet,
            "IndexRows",
            indices,
            referenceAddresses,
            MaterialRanges: [],
            rawTriangleCount,
            rejectedDegenerateTriangleCount,
            rejectedInvalidTriangleCount,
            rejectedDuplicateTriangleCount,
            rejectedLongEdgeTriangleCount,
            alternateDiagonalRowCount);
    }

    private static bool ShouldUseAlternateIndexRowDiagonal(
        int a,
        int b,
        int c,
        int d,
        int referenceA,
        int referenceB,
        int referenceC,
        int referenceD,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2?> packetReferenceTexCoords,
        IReadOnlyList<Vector2?> referenceTexCoords,
        TfragResolvedTexture fallbackTexture)
    {
        if (a < 0 || a >= positions.Count
            || b < 0 || b >= positions.Count
            || c < 0 || c >= positions.Count
            || d < 0 || d >= positions.Count
            || a == b
            || a == c
            || a == d
            || b == c
            || b == d
            || c == d)
        {
            return false;
        }

        if (TryCompareIndexRowDiagonalTexCoordArea(
            referenceA,
            referenceB,
            referenceC,
            referenceD,
            packetReferenceTexCoords,
            referenceTexCoords,
            fallbackTexture,
            out var preferAlternateForTexCoords))
        {
            return preferAlternateForTexCoords;
        }

        var currentDiagonal = Vector3.DistanceSquared(positions[b], positions[c]);
        var alternateDiagonal = Vector3.DistanceSquared(positions[a], positions[d]);
        return alternateDiagonal < currentDiagonal;
    }

    private static bool TryCompareIndexRowDiagonalTexCoordArea(
        int referenceA,
        int referenceB,
        int referenceC,
        int referenceD,
        IReadOnlyList<Vector2?> packetReferenceTexCoords,
        IReadOnlyList<Vector2?> referenceTexCoords,
        TfragResolvedTexture fallbackTexture,
        out bool preferAlternate)
    {
        const float collapsedUvArea = 0.000001f;

        preferAlternate = false;
        var texCoordA = ResolveReferenceTexCoord(referenceA, packetReferenceTexCoords, referenceTexCoords);
        var texCoordB = ResolveReferenceTexCoord(referenceB, packetReferenceTexCoords, referenceTexCoords);
        var texCoordC = ResolveReferenceTexCoord(referenceC, packetReferenceTexCoords, referenceTexCoords);
        var texCoordD = ResolveReferenceTexCoord(referenceD, packetReferenceTexCoords, referenceTexCoords);
        if (!texCoordA.HasValue || !texCoordB.HasValue || !texCoordC.HasValue || !texCoordD.HasValue)
        {
            return false;
        }

        var currentMinArea = MathF.Min(
            AdjustedTriangleTexCoordArea(texCoordA.Value, texCoordB.Value, texCoordC.Value, fallbackTexture),
            AdjustedTriangleTexCoordArea(texCoordC.Value, texCoordB.Value, texCoordD.Value, fallbackTexture));
        var alternateMinArea = MathF.Min(
            AdjustedTriangleTexCoordArea(texCoordA.Value, texCoordB.Value, texCoordD.Value, fallbackTexture),
            AdjustedTriangleTexCoordArea(texCoordA.Value, texCoordD.Value, texCoordC.Value, fallbackTexture));

        if (currentMinArea > collapsedUvArea && alternateMinArea <= collapsedUvArea)
        {
            preferAlternate = false;
            return true;
        }

        if (alternateMinArea > collapsedUvArea && currentMinArea <= collapsedUvArea)
        {
            preferAlternate = true;
            return true;
        }

        return false;
    }

    private static float AdjustedTriangleTexCoordArea(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        TfragResolvedTexture texture)
    {
        var adjusted = GltfTexCoordUtils.AdjustTriangleTexCoords(
            a,
            b,
            c,
            textureSize: null,
            repeatU: !texture.ClampU,
            repeatV: !texture.ClampV,
            normalizeClampedAxes: true);
        return GltfTexCoordUtils.TriangleArea(adjusted[0], adjusted[1], adjusted[2]);
    }
}

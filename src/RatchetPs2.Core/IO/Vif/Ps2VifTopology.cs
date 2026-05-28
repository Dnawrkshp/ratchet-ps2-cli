using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.IO.Vif;

public readonly record struct TopologyTriangle(uint A, uint B, uint C);

public sealed record Ps2VifTopologyStripBuild(List<byte> Tokens, List<int> TriangleIndices);

public static class Ps2VifTopology
{
    public static List<byte> BuildRestartStripTokens(IReadOnlyList<uint> triangleIndices)
    {
        TriangleIndexUtils.ValidateTriangleIndexList(triangleIndices, "Mesh indices");

        var triangles = new List<TopologyTriangle>(triangleIndices.Count / 3);
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            ValidateMeshLocalVertexIndex(a);
            ValidateMeshLocalVertexIndex(b);
            ValidateMeshLocalVertexIndex(c);
            triangles.Add(new TopologyTriangle(a, b, c));
        }

        var available = Enumerable.Repeat(true, triangles.Count).ToArray();
        var remaining = triangles.Count;
        var tokens = new List<byte>(triangleIndices.Count);
        while (remaining > 0)
        {
            Ps2VifTopologyStripBuild? best = null;
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                if (!available[triangleIndex])
                {
                    continue;
                }

                for (var rotation = 0; rotation < 3; rotation++)
                {
                    var candidate = BuildGreedyRestartStrip(triangles, available, triangleIndex, rotation);
                    if (best is null
                        || candidate.TriangleIndices.Count > best.TriangleIndices.Count
                        || (candidate.TriangleIndices.Count == best.TriangleIndices.Count
                            && candidate.Tokens.Count < best.Tokens.Count))
                    {
                        best = candidate;
                    }
                }
            }

            if (best is null)
            {
                break;
            }

            tokens.AddRange(best.Tokens);
            foreach (var triangleIndex in best.TriangleIndices)
            {
                if (available[triangleIndex])
                {
                    available[triangleIndex] = false;
                    remaining--;
                }
            }
        }

        return tokens;
    }

    public static List<byte> BuildIsolatedTriangleTokens(IReadOnlyList<uint> triangleIndices)
    {
        TriangleIndexUtils.ValidateTriangleIndexList(triangleIndices, "Mesh indices");

        var tokens = new List<byte>(triangleIndices.Count);
        for (var i = 0; i < triangleIndices.Count; i += 3)
        {
            var a = triangleIndices[i];
            var b = triangleIndices[i + 1];
            var c = triangleIndices[i + 2];
            ValidateMeshLocalVertexIndex(a);
            ValidateMeshLocalVertexIndex(b);
            ValidateMeshLocalVertexIndex(c);

            tokens.Add(EncodeIndexToken(a, negative: true));
            tokens.Add(EncodeIndexToken(c, negative: true));
            tokens.Add(EncodeIndexToken(b, negative: false));
        }

        return tokens;
    }

    public static byte EncodeIndexToken(uint vertexIndex, bool negative)
    {
        ValidateMeshLocalVertexIndex(vertexIndex);
        return checked((byte)((negative ? 0x80 : 0x00) | ((int)vertexIndex + 1)));
    }

    public static byte[] BuildPayload(IReadOnlyList<byte> topologyTokens, IReadOnlyList<byte> prefixBytes)
    {
        var payloadLength = 4 + topologyTokens.Count;
        var paddedPayloadLength = Align(payloadLength, 4);
        var payload = new byte[paddedPayloadLength];
        Array.Fill<byte>(payload, 0x80);
        for (var i = 0; i < 4; i++)
        {
            payload[i] = i < prefixBytes.Count ? prefixBytes[i] : (byte)0;
        }

        for (var i = 0; i < topologyTokens.Count; i++)
        {
            payload[4 + i] = topologyTokens[i];
        }

        return payload;
    }

    public static byte[] BuildPayloadFromStripIndices(IReadOnlyList<uint> stripIndices)
    {
        return BuildPayloadFromStripIndices(stripIndices, [0, 0, 0x80, 0]);
    }

    public static byte[] BuildPayloadFromStripIndices(IReadOnlyList<uint> stripIndices, IReadOnlyList<byte> prefixBytes)
    {
        var payloadLength = 4 + stripIndices.Count;
        var paddedPayloadLength = Align(payloadLength, 4);
        var payload = new byte[paddedPayloadLength];
        Array.Fill<byte>(payload, 0x80);
        for (var i = 0; i < 4; i++)
        {
            payload[i] = i < prefixBytes.Count ? prefixBytes[i] : (byte)0;
        }

        for (var i = 0; i < stripIndices.Count; i++)
        {
            payload[4 + i] = checked((byte)(stripIndices[i] + 1));
        }

        return payload;
    }

    public static TopologyTriangle RotateTriangle(TopologyTriangle triangle, int rotation)
    {
        return rotation switch
        {
            1 => new TopologyTriangle(triangle.B, triangle.C, triangle.A),
            2 => new TopologyTriangle(triangle.C, triangle.A, triangle.B),
            _ => triangle
        };
    }

    public static bool TryAppendToCurrentStrip(
        IReadOnlyList<uint> currentStrip,
        uint triangleA,
        uint triangleB,
        uint triangleC,
        out uint nextVertex)
    {
        nextVertex = 0;
        if (currentStrip.Count < 3)
        {
            return false;
        }

        var previousA = currentStrip[^2];
        var previousB = currentStrip[^1];
        var nextFlip = ((currentStrip.Count - 2) & 1) == 1;
        if (nextFlip)
        {
            if (triangleA != previousA || triangleC != previousB)
            {
                return false;
            }

            nextVertex = triangleB;
            return true;
        }

        if (triangleA != previousA || triangleB != previousB)
        {
            return false;
        }

        nextVertex = triangleC;
        return true;
    }

    public static Ps2VifTopologyStripBuild BuildGreedyRestartStrip(
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> available,
        int seedTriangleIndex,
        int seedRotation)
    {
        var localAvailable = available.ToArray();
        localAvailable[seedTriangleIndex] = false;

        var seed = RotateTriangle(triangles[seedTriangleIndex], seedRotation);
        var currentStrip = new List<uint> { seed.A, seed.A, seed.C, seed.B };
        var tokens = new List<byte>
        {
            EncodeIndexToken(seed.A, negative: true),
            EncodeIndexToken(seed.C, negative: true),
            EncodeIndexToken(seed.B, negative: false)
        };
        var usedTriangleIndices = new List<int> { seedTriangleIndex };

        var found = true;
        while (found)
        {
            found = false;
            var bestTriangleIndex = -1;
            uint bestNextVertex = 0;
            var bestContinuationCount = -1;
            for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
            {
                if (!localAvailable[triangleIndex])
                {
                    continue;
                }

                if (!TryFindAppendableTriangleRotation(
                    currentStrip,
                    triangles[triangleIndex],
                    out _,
                    out var nextVertex))
                {
                    continue;
                }

                var continuationStrip = new List<uint>(currentStrip) { nextVertex };
                var continuationCount = CountAppendableTriangles(continuationStrip, triangles, localAvailable, triangleIndex);
                if (continuationCount <= bestContinuationCount)
                {
                    continue;
                }

                bestTriangleIndex = triangleIndex;
                bestNextVertex = nextVertex;
                bestContinuationCount = continuationCount;
                found = true;
            }

            if (bestTriangleIndex >= 0)
            {
                tokens.Add(EncodeIndexToken(bestNextVertex, negative: false));
                currentStrip.Add(bestNextVertex);
                localAvailable[bestTriangleIndex] = false;
                usedTriangleIndices.Add(bestTriangleIndex);
            }
        }

        return new Ps2VifTopologyStripBuild(tokens, usedTriangleIndices);
    }

    private static int CountAppendableTriangles(
        IReadOnlyList<uint> currentStrip,
        IReadOnlyList<TopologyTriangle> triangles,
        IReadOnlyList<bool> available,
        int excludingTriangleIndex)
    {
        var count = 0;
        for (var triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            if (triangleIndex == excludingTriangleIndex || !available[triangleIndex])
            {
                continue;
            }

            if (TryFindAppendableTriangleRotation(currentStrip, triangles[triangleIndex], out _, out _))
            {
                count++;
            }
        }

        return count;
    }

    public static bool TryFindAppendableTriangleRotation(
        IReadOnlyList<uint> currentStrip,
        TopologyTriangle triangle,
        out TopologyTriangle rotated,
        out uint nextVertex)
    {
        for (var rotation = 0; rotation < 3; rotation++)
        {
            rotated = RotateTriangle(triangle, rotation);
            if (TryAppendToCurrentStrip(currentStrip, rotated.A, rotated.B, rotated.C, out nextVertex))
            {
                return true;
            }
        }

        rotated = default;
        nextVertex = 0;
        return false;
    }

    private static void ValidateMeshLocalVertexIndex(uint vertexIndex)
    {
        if (vertexIndex > 126)
        {
            throw new InvalidDataException("VIF topology supports mesh-local vertex indices up to 126.");
        }
    }

    private static int Align(int value, int alignment)
    {
        var remainder = value % alignment;
        return remainder == 0
            ? value
            : value + alignment - remainder;
    }
}

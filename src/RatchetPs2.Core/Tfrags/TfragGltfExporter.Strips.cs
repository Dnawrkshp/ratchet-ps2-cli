using System.Numerics;
using RatchetPs2.Core.Geometry;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private static TfragTopologyDecode DecodeStripTopologyPacket(
        TfragTopologyPacket packet,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<int> topologyPositionLookup,
        float? maxTriangleEdgeLength,
        int prefixBytes)
    {
        var indices = new List<uint>();
        var referenceAddresses = new List<int>();
        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        var strip = new List<TfragTopologyVertex>();
        var flip = false;
        var rawTriangleCount = 0;
        var rejectedDegenerateTriangleCount = 0;
        var rejectedInvalidTriangleCount = 0;
        var rejectedDuplicateTriangleCount = 0;
        var rejectedLongEdgeTriangleCount = 0;
        var firstTokenOffset = Math.Min(prefixBytes, packet.Payload.Length);

        for (var i = firstTokenOffset; i < packet.Payload.Length; i++)
        {
            var raw = packet.Payload[i];
            if (raw is 0 or 0x80)
            {
                strip.Clear();
                flip = false;
                continue;
            }

            var referenceAddress = MapTopologyReferenceAddress(packet, raw, i);
            var decoded = ResolveTopologyPosition(referenceAddress, topologyPositionLookup);
            if (decoded < 0 || decoded >= positions.Count)
            {
                rejectedInvalidTriangleCount++;
                strip.Clear();
                flip = false;
                continue;
            }

            strip.Add(new TfragTopologyVertex((uint)decoded, referenceAddress));
            if (strip.Count < 3)
            {
                continue;
            }

            var a = strip[^3];
            var b = strip[^2];
            var c = strip[^1];
            var v0 = a;
            var v1 = flip ? c : b;
            var v2 = flip ? b : c;
            flip = !flip;
            AppendTriangleCandidate(
                v0.SourceIndex,
                v1.SourceIndex,
                v2.SourceIndex,
                positions,
                indices,
                referenceAddresses,
                v0.ReferenceAddress,
                v1.ReferenceAddress,
                v2.ReferenceAddress,
                seenTriangles,
                ref rawTriangleCount,
                ref rejectedDegenerateTriangleCount,
                ref rejectedDuplicateTriangleCount,
                ref rejectedLongEdgeTriangleCount,
                maxTriangleEdgeLength);
        }

        return new TfragTopologyDecode(
            packet,
            "StripTokens",
            indices,
            referenceAddresses,
            MaterialRanges: [],
            rawTriangleCount,
            rejectedDegenerateTriangleCount,
            rejectedInvalidTriangleCount,
            rejectedDuplicateTriangleCount,
            rejectedLongEdgeTriangleCount,
            AlternateDiagonalRowCount: 0);
    }

    private static TfragTopologyDecode DecodeControlStripTopologyPacket(
        TfragTopologyPacket packet,
        TfragTopologyPacket controlPacket,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<int> topologyPositionLookup,
        float? maxTriangleEdgeLength)
    {
        var indices = new List<uint>();
        var seenTriangles = new HashSet<string>(StringComparer.Ordinal);
        var rawTriangleCount = 0;
        var rejectedDegenerateTriangleCount = 0;
        var rejectedInvalidTriangleCount = 0;
        var rejectedDuplicateTriangleCount = 0;
        var rejectedLongEdgeTriangleCount = 0;
        var referenceAddresses = new List<int>();
        var materialRanges = new List<TfragMaterialRange>();
        var payloadOffset = 0;

        foreach (var controlStrip in DecodeDrawControlStrips(controlPacket.Payload))
        {
            var stripStartIndex = indices.Count;
            var strip = new List<TfragTopologyVertex?>(controlStrip.VertexCount);
            for (var i = 0; i < controlStrip.VertexCount; i++)
            {
                if (payloadOffset >= packet.Payload.Length)
                {
                    rejectedInvalidTriangleCount++;
                    strip.Add(null);
                    break;
                }

                var raw = packet.Payload[payloadOffset];
                var referenceAddress = MapTopologyReferenceAddress(packet, raw, payloadOffset);
                var decoded = ResolveTopologyPosition(referenceAddress, topologyPositionLookup);
                payloadOffset++;
                if (decoded < 0 || decoded >= positions.Count)
                {
                    rejectedInvalidTriangleCount++;
                    strip.Add(null);
                    continue;
                }

                strip.Add(new TfragTopologyVertex((uint)decoded, referenceAddress));
            }

            if ((controlStrip.VertexCount & 1) == 0)
            {
                // Even tfrag strips encode paired quad rows; glTF stores the recovered rows as triangles.
                for (var i = 0; i + 3 < strip.Count; i += 2)
                {
                    var v0 = strip[i + 2];
                    var v1 = strip[i + 3];
                    var v2 = strip[i + 1];
                    var v3 = strip[i];
                    AppendControlStripTriangleCandidate(
                        v0,
                        v1,
                        v2,
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
                    AppendControlStripTriangleCandidate(
                        v2,
                        v3,
                        v0,
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
            }
            else
            {
                for (var i = 0; i < strip.Count - 2; i++)
                {
                    AppendControlStripTriangleCandidate(
                        strip[i],
                        strip[i + 1],
                        strip[i + 2],
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
            }

            var stripIndexCount = indices.Count - stripStartIndex;
            if (stripIndexCount > 0)
            {
                materialRanges.Add(new TfragMaterialRange(stripStartIndex, stripIndexCount, controlStrip.TextureSlot));
            }
        }

        return new TfragTopologyDecode(
            packet,
            "ControlStrips",
            indices,
            referenceAddresses,
            materialRanges,
            rawTriangleCount,
            rejectedDegenerateTriangleCount,
            rejectedInvalidTriangleCount,
            rejectedDuplicateTriangleCount,
            rejectedLongEdgeTriangleCount,
            AlternateDiagonalRowCount: 0);
    }

    private static void AppendControlStripTriangleCandidate(
        TfragTopologyVertex? v0,
        TfragTopologyVertex? v1,
        TfragTopologyVertex? v2,
        IReadOnlyList<Vector3> positions,
        List<uint> indices,
        List<int> referenceAddresses,
        HashSet<string> seenTriangles,
        ref int rawTriangleCount,
        ref int rejectedDegenerateTriangleCount,
        ref int rejectedInvalidTriangleCount,
        ref int rejectedDuplicateTriangleCount,
        ref int rejectedLongEdgeTriangleCount,
        float? maxTriangleEdgeLength)
    {
        if (!v0.HasValue || !v1.HasValue || !v2.HasValue)
        {
            rawTriangleCount++;
            rejectedInvalidTriangleCount++;
            return;
        }

        AppendTriangleCandidate(
            v0.Value.SourceIndex,
            v1.Value.SourceIndex,
            v2.Value.SourceIndex,
            positions,
            indices,
            referenceAddresses,
            v0.Value.ReferenceAddress,
            v1.Value.ReferenceAddress,
            v2.Value.ReferenceAddress,
            seenTriangles,
            ref rawTriangleCount,
            ref rejectedDegenerateTriangleCount,
            ref rejectedDuplicateTriangleCount,
            ref rejectedLongEdgeTriangleCount,
            maxTriangleEdgeLength);
    }

    private static int MapTopologyPosition(
        TfragTopologyPacket packet,
        byte raw,
        int componentOffset,
        IReadOnlyList<int> topologyPositionLookup)
    {
        var referenceAddress = MapTopologyReferenceAddress(packet, raw, componentOffset);
        return ResolveTopologyPosition(referenceAddress, topologyPositionLookup);
    }

    private static int MapTopologyReferenceAddress(
        TfragTopologyPacket packet,
        byte raw,
        int componentOffset)
    {
        return GetTopologyReferenceBase(packet, componentOffset) + raw;
    }

    private static int ResolveTopologyPosition(
        int referenceAddress,
        IReadOnlyList<int> topologyPositionLookup)
    {
        return referenceAddress >= 0 && referenceAddress < topologyPositionLookup.Count
            ? topologyPositionLookup[referenceAddress]
            : -1;
    }

    private static int GetTopologyReferenceBase(TfragTopologyPacket packet, int componentOffset)
    {
        if (!packet.UsesVifBase)
        {
            return packet.Address;
        }

        return (componentOffset & 0x03) switch
        {
            0 => packet.BaseX,
            1 => packet.BaseY,
            2 => packet.BaseZ,
            _ => packet.BaseW
        };
    }

    private static int CountMappedTopologyPositions(
        TfragTopologyPacket packet,
        IReadOnlyList<int> topologyPositionLookup)
    {
        var mappedPositions = new HashSet<int>();
        for (var componentOffset = 0; componentOffset < 4; componentOffset++)
        {
            var baseAddress = GetTopologyReferenceBase(packet, componentOffset);
            for (var i = 0; i < 256 && baseAddress + i < topologyPositionLookup.Count; i++)
            {
                var mappedPosition = topologyPositionLookup[baseAddress + i];
                if (mappedPosition >= 0)
                {
                    mappedPositions.Add(mappedPosition);
                }
            }
        }

        return mappedPositions.Count;
    }

    private static TfragTopologyDecode EmptyTopologyDecode(TfragTopologyPacket packet, string decodeMode)
    {
        return new TfragTopologyDecode(
            packet,
            decodeMode,
            [],
            [],
            [],
            0,
            0,
            0,
            0,
            0,
            AlternateDiagonalRowCount: 0);
    }

    private static void AppendTriangleCandidate(
        uint i0,
        uint i1,
        uint i2,
        IReadOnlyList<Vector3> positions,
        List<uint> indices,
        List<int> referenceAddresses,
        int referenceAddress0,
        int referenceAddress1,
        int referenceAddress2,
        HashSet<string> seenTriangles,
        ref int rawTriangleCount,
        ref int rejectedDegenerateTriangleCount,
        ref int rejectedDuplicateTriangleCount,
        ref int rejectedLongEdgeTriangleCount,
        float? maxTriangleEdgeLength)
    {
        rawTriangleCount++;

        if (i0 == i1 || i0 == i2 || i1 == i2)
        {
            rejectedDegenerateTriangleCount++;
            return;
        }

        var faceNormal = Vector3.Cross(
            positions[(int)i1] - positions[(int)i0],
            positions[(int)i2] - positions[(int)i0]);
        if (faceNormal.LengthSquared() <= 0.00000001f)
        {
            rejectedDegenerateTriangleCount++;
            return;
        }

        if (maxTriangleEdgeLength.HasValue
            && TriangleGeometryUtils.HasEdgeLongerThan(
                positions[(int)i0],
                positions[(int)i1],
                positions[(int)i2],
                maxTriangleEdgeLength.Value))
        {
            rejectedLongEdgeTriangleCount++;
            return;
        }

        var key = TriangleKey(i0, i1, i2);
        if (!seenTriangles.Add(key))
        {
            rejectedDuplicateTriangleCount++;
            return;
        }

        indices.Add(i0);
        indices.Add(i1);
        indices.Add(i2);
        referenceAddresses.Add(referenceAddress0);
        referenceAddresses.Add(referenceAddress1);
        referenceAddresses.Add(referenceAddress2);
    }

    private static void AppendIndexRowTriangleCandidate(
        int i0,
        int i1,
        int i2,
        int referenceAddress0,
        int referenceAddress1,
        int referenceAddress2,
        IReadOnlyList<Vector3> positions,
        List<uint> indices,
        List<int> referenceAddresses,
        HashSet<string> seenTriangles,
        ref int rawTriangleCount,
        ref int rejectedDegenerateTriangleCount,
        ref int rejectedInvalidTriangleCount,
        ref int rejectedDuplicateTriangleCount,
        ref int rejectedLongEdgeTriangleCount,
        float? maxTriangleEdgeLength)
    {
        if (i0 < 0 || i0 >= positions.Count || i1 < 0 || i1 >= positions.Count || i2 < 0 || i2 >= positions.Count)
        {
            rawTriangleCount++;
            rejectedInvalidTriangleCount++;
            return;
        }

        AppendTriangleCandidate(
            (uint)i0,
            (uint)i1,
            (uint)i2,
            positions,
            indices,
            referenceAddresses,
            referenceAddress0,
            referenceAddress1,
            referenceAddress2,
            seenTriangles,
            ref rawTriangleCount,
            ref rejectedDegenerateTriangleCount,
            ref rejectedDuplicateTriangleCount,
            ref rejectedLongEdgeTriangleCount,
            maxTriangleEdgeLength);
    }
}

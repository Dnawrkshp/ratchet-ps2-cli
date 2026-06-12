using System.Numerics;

namespace RatchetPs2.Core.Tfrags;

public static partial class TfragGltfExporter
{
    private static TfragTopologyDecode DecodeTopologyPacket(
        int packetIndex,
        TfragTopologyPacket packet,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<int> topologyPositionLookup,
        IReadOnlyList<Vector2?> referenceTexCoords,
        TfragResolvedTexture fallbackTexture,
        float? maxTriangleEdgeLength,
        int prefixBytes,
        TfragControlStripPlans controlStripPlans)
    {
        if (positions.Count == 0)
        {
            return EmptyTopologyDecode(packet, "NoPositions");
        }

        if (controlStripPlans.ControlPacketIndices.Contains(packetIndex))
        {
            return EmptyTopologyDecode(packet, "DrawControlRows");
        }

        if (controlStripPlans.Targets.TryGetValue(packetIndex, out var controlPacket))
        {
            return DecodeControlStripTopologyPacket(
                packet,
                controlPacket,
                positions,
                topologyPositionLookup,
                maxTriangleEdgeLength);
        }

        if (controlStripPlans.Targets.Count > 0 && IsUnsignedTopologyDataPacket(packet))
        {
            return EmptyTopologyDecode(packet, "SkippedAuxTopologyRows");
        }

        if (IsIndexRowTopologyPacket(packet, topologyPositionLookup))
        {
            return DecodeIndexRowTopologyPacket(
                packet,
                positions,
                topologyPositionLookup,
                referenceTexCoords,
                fallbackTexture,
                maxTriangleEdgeLength);
        }

        if (LooksLikeControlTopologyPacket(packet, topologyPositionLookup))
        {
            return EmptyTopologyDecode(packet, "SkippedControlRows");
        }

        return DecodeStripTopologyPacket(packet, positions, topologyPositionLookup, maxTriangleEdgeLength, prefixBytes);
    }

    private static TfragControlStripPlans BuildControlStripPlans(IReadOnlyList<TfragTopologyPacket> topologyPackets)
    {
        var targets = new Dictionary<int, TfragTopologyPacket>();
        var controlPacketIndices = new HashSet<int>();
        var usedTargetIndices = new HashSet<int>();

        for (var controlIndex = 0; controlIndex < topologyPackets.Count; controlIndex++)
        {
            var controlPacket = topologyPackets[controlIndex];
            if (!IsDrawControlTopologyPacket(controlPacket))
            {
                continue;
            }

            var payloadByteCount = DecodeDrawControlStrips(controlPacket.Payload).Sum(strip => strip.VertexCount);
            if (payloadByteCount <= 0)
            {
                continue;
            }

            var targetIndex = -1;
            var bestExtraByteCount = int.MaxValue;
            for (var i = controlIndex + 1; i < topologyPackets.Count; i++)
            {
                if (usedTargetIndices.Contains(i) || !IsUnsignedTopologyDataPacket(topologyPackets[i]))
                {
                    continue;
                }

                var extraByteCount = topologyPackets[i].Payload.Length - payloadByteCount;
                if (extraByteCount < 0 || extraByteCount >= bestExtraByteCount)
                {
                    continue;
                }

                targetIndex = i;
                bestExtraByteCount = extraByteCount;
                if (extraByteCount <= 4)
                {
                    break;
                }
            }

            if (targetIndex < 0)
            {
                continue;
            }

            controlPacketIndices.Add(controlIndex);
            usedTargetIndices.Add(targetIndex);
            targets[targetIndex] = controlPacket;
        }

        return new TfragControlStripPlans(targets, controlPacketIndices);
    }

    private static bool IsIndexRowTopologyPacket(
        TfragTopologyPacket packet,
        IReadOnlyList<int> topologyPositionLookup)
    {
        if (packet.Payload.Length == 0 || packet.Payload.Length % 4 != 0)
        {
            return false;
        }

        var distinctTokens = new HashSet<int>();
        var invalidTokenCount = 0;
        for (var i = 0; i < packet.Payload.Length; i++)
        {
            var mappedIndex = MapTopologyPosition(packet, packet.Payload[i], i, topologyPositionLookup);
            if (mappedIndex < 0)
            {
                invalidTokenCount++;
            }
            else
            {
                distinctTokens.Add(mappedIndex);
            }
        }

        if (invalidTokenCount > Math.Max(2, packet.Payload.Length / 64))
        {
            return false;
        }

        var distinctEnough = distinctTokens.Count >= packet.Payload.Length - Math.Max(1, packet.Payload.Length / 64);
        var validIndexRows = distinctTokens.Count >= 3;
        var mappedPositionCount = CountMappedTopologyPositions(packet, topologyPositionLookup);
        var fullRemapLike = packet.Payload.Length >= Math.Max(8, mappedPositionCount - 8)
            && packet.Payload.Length <= mappedPositionCount + 8;

        return validIndexRows && (fullRemapLike || !LooksLikeControlTopologyPacket(packet, topologyPositionLookup))
            || distinctEnough && fullRemapLike;
    }

    private static bool LooksLikeControlTopologyPacket(
        TfragTopologyPacket packet,
        IReadOnlyList<int> topologyPositionLookup)
    {
        var controlTokenCount = 0;
        var validTokenCount = 0;
        for (var i = 0; i < packet.Payload.Length; i++)
        {
            var raw = packet.Payload[i];
            if (raw is 0x80 or 0xFF || raw >= 0x80)
            {
                controlTokenCount++;
                continue;
            }

            if (MapTopologyPosition(packet, raw, i, topologyPositionLookup) >= 0)
            {
                validTokenCount++;
            }
            else
            {
                controlTokenCount++;
            }
        }

        return controlTokenCount >= Math.Max(4, packet.Payload.Length / 3)
            || validTokenCount < 3;
    }

    private static bool IsLikelyDrawControlTopologyPacket(ReadOnlySpan<byte> payload, ushort immediate)
    {
        if ((immediate & 0xC000) != 0x8000 || payload.Length == 0 || payload.Length % 4 != 0)
        {
            return false;
        }

        var strips = DecodeDrawControlStrips(payload);
        return strips.Any(strip => strip.VertexCount >= 3) && strips.Sum(strip => strip.VertexCount) >= 4;
    }

    private static bool IsDrawControlTopologyPacket(TfragTopologyPacket packet)
    {
        return IsLikelyDrawControlTopologyPacket(packet.Payload, packet.Immediate);
    }

    private static bool IsUnsignedTopologyDataPacket(TfragTopologyPacket packet)
    {
        return (packet.Immediate & 0xC000) == 0xC000;
    }

    private static IReadOnlyList<TfragDrawControlStrip> DecodeDrawControlStrips(ReadOnlySpan<byte> payload)
    {
        var strips = new List<TfragDrawControlStrip>();
        var currentTextureSlot = 0;
        for (var offset = 0; offset + 3 < payload.Length; offset += 4)
        {
            if (payload[offset] == 0x00)
            {
                break;
            }

            var count = payload[offset] & 0x7F;
            if ((payload[offset] & 0x80) != 0 && payload[offset + 2] != 0xFF)
            {
                currentTextureSlot = payload[offset + 2] / 5;
            }

            if (count > 0)
            {
                strips.Add(new TfragDrawControlStrip(count, currentTextureSlot));
            }
        }

        return strips;
    }
}

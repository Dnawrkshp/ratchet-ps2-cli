using System.Numerics;
using RatchetPs2.Core.Textures;

namespace RatchetPs2.Core.Ties;

internal static class TieGltfPacketIndexGroupBuilder
{
    public static TieGltfPacketIndexGroupBuildResult Build(
        TieClass tie,
        TieLodTopology topology,
        IReadOnlyList<Vector4> glowColors)
    {
        ArgumentNullException.ThrowIfNull(tie);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(glowColors);

        var packetIndexGroups = SplitPacketIndexGroupsByGlowEmission(
            BuildPacketIndexGroups(tie, topology),
            Ps2Color.NormalizeOpacityAlpha(TieRgba32.FromRaw(tie.Header.GlowRgba).A),
            glowColors);
        var packetRgbaSlotCount = CountPacketRgbaSlots(tie, topology.LodIndex);

        return new TieGltfPacketIndexGroupBuildResult(
            packetIndexGroups,
            packetRgbaSlotCount);
    }

    private static int CountPacketRgbaSlots(TieClass tie, int lodIndex)
    {
        return tie.PacketTables
            .FirstOrDefault(table => table.LodIndex == lodIndex)?
            .Packets
            .Sum(packet => packet.RgbaCount) ?? 0;
    }

    private static List<PacketIndexGroup> BuildPacketIndexGroups(
        TieClass tie,
        TieLodTopology topology)
    {
        var packetsByIndex = tie.PacketTables
            .FirstOrDefault(table => table.LodIndex == topology.LodIndex)?
            .Packets
            .ToDictionary(packet => packet.PacketIndex) ?? [];
        var packetDataBlocksByIndex = tie.PacketDataBlocks
            .Where(block => block.LodIndex == topology.LodIndex)
            .ToDictionary(block => block.PacketIndex);
        var groups = new List<PacketIndexGroup>();
        PacketIndexGroup? currentGroup = null;

        foreach (var triangle in topology.Triangles)
        {
            if (triangle.StripIndex < 0 || triangle.StripIndex >= topology.Strips.Count)
            {
                throw new InvalidDataException(
                    $"Tie LOD {topology.LodIndex} triangle references missing strip {triangle.StripIndex}.");
            }

            ValidateTriangleIndex(topology, triangle.A);
            ValidateTriangleIndex(topology, triangle.B);
            ValidateTriangleIndex(topology, triangle.C);

            var strip = topology.Strips[triangle.StripIndex];
            packetsByIndex.TryGetValue(strip.PacketIndex, out var packet);
            var shaderIndex = SelectShaderIndex(packet, strip);
            var multipassOffset = packet?.MultipassOffset ?? 0;
            var passFlags = packet?.PassFlags ?? 0;
            var multipassUvSize = packet?.MultipassUvSize ?? 0;
            var envPassBleedColor = ResolveEnvPassBleedColor(packet, packetDataBlocksByIndex);
            var packetShaderIndices = GetPacketShaderIndices(packet);
            var packetShaderSwitchVuAddresses = packet?.ShaderSwitchVuAddresses ?? [];

            if (currentGroup is null
                || currentGroup.Value.PacketIndex != strip.PacketIndex
                || currentGroup.Value.ShaderIndex != shaderIndex
                || currentGroup.Value.EnvPassBleedColor != envPassBleedColor)
            {
                currentGroup = new PacketIndexGroup(
                    strip.PacketIndex,
                    shaderIndex,
                    multipassOffset,
                    passFlags,
                    multipassUvSize,
                    envPassBleedColor,
                    packetShaderIndices,
                    packetShaderSwitchVuAddresses,
                    UseGlowEmission: false,
                    []);
                groups.Add(currentGroup.Value);
            }

            currentGroup.Value.Indices.Add((uint)triangle.A);
            currentGroup.Value.Indices.Add((uint)triangle.B);
            currentGroup.Value.Indices.Add((uint)triangle.C);
        }

        return groups;
    }

    private static TieRgba32? ResolveEnvPassBleedColor(
        TiePacket? packet,
        IReadOnlyDictionary<int, TiePacketDataBlock> packetDataBlocksByIndex)
    {
        if (packet is null
            || packet.MultipassOffset == 0
            || !TiePassFlags.UsesEnvironmentPass(packet.PassFlags))
        {
            return null;
        }

        if (!packetDataBlocksByIndex.TryGetValue(packet.PacketIndex, out var block))
        {
            return null;
        }

        var offset = checked(
            (packet.MultipassOffset + TiePassFlags.GeneratedEnvPassBleedColorQwordOffset)
            * TiePassFlags.QwordSize);
        if (offset < 0 || offset + TiePassFlags.QwordSize > block.Bytes.Length)
        {
            return null;
        }

        // FUN_00595168 enters the generated envpass path at 0x00595934-0x00595954,
        // copies tie_dma_2nd_bleedcolor_template at 0x0059599c, then starts the
        // generated envpass input at multipass+0x30 (0x005959ac-0x005959c0).
        // Therefore multipass+0x10 is the second-pass RGBA bleed color qword.
        return new TieRgba32(
            block.Bytes[offset],
            block.Bytes[offset + 4],
            block.Bytes[offset + 8],
            block.Bytes[offset + 12]);
    }

    private static List<PacketIndexGroup> SplitPacketIndexGroupsByGlowEmission(
        IReadOnlyList<PacketIndexGroup> packetIndexGroups,
        float fullGlowAlpha,
        IReadOnlyList<Vector4> colors)
    {
        if (colors.Count == 0)
        {
            return packetIndexGroups.ToList();
        }

        var splitGroups = new List<PacketIndexGroup>(packetIndexGroups.Count);
        foreach (var group in packetIndexGroups)
        {
            PacketIndexGroup? currentGroup = null;
            bool? currentFullGlow = null;
            for (var i = 0; i + 2 < group.Indices.Count; i += 3)
            {
                var fullGlow = TriangleUsesFullGlowEmission(
                    group.Indices[i],
                    group.Indices[i + 1],
                    group.Indices[i + 2],
                    fullGlowAlpha,
                    colors);
                if (currentGroup is null
                    || currentFullGlow != fullGlow)
                {
                    currentFullGlow = fullGlow;
                    currentGroup = new PacketIndexGroup(
                        group.PacketIndex,
                        group.ShaderIndex,
                        group.MultipassOffset,
                        group.PassFlags,
                        group.MultipassUvSize,
                        group.EnvPassBleedColor,
                        group.PacketShaderIndices,
                        group.PacketShaderSwitchVuAddresses,
                        fullGlow,
                        []);
                    splitGroups.Add(currentGroup.Value);
                }

                currentGroup.Value.Indices.Add(group.Indices[i]);
                currentGroup.Value.Indices.Add(group.Indices[i + 1]);
                currentGroup.Value.Indices.Add(group.Indices[i + 2]);
            }
        }

        return splitGroups;
    }

    private static bool TriangleUsesFullGlowEmission(
        uint a,
        uint b,
        uint c,
        float fullGlowAlpha,
        IReadOnlyList<Vector4> colors)
    {
        return IsFullGlowColor(a, fullGlowAlpha, colors)
            && IsFullGlowColor(b, fullGlowAlpha, colors)
            && IsFullGlowColor(c, fullGlowAlpha, colors);
    }

    private static bool IsFullGlowColor(uint index, float fullGlowAlpha, IReadOnlyList<Vector4> colors)
    {
        var colorIndex = checked((int)index);
        return colorIndex >= 0
            && colorIndex < colors.Count
            && TieGltfGlowBuilder.IsActiveColor(colors[colorIndex])
            && MathF.Abs(colors[colorIndex].W - fullGlowAlpha) <= 0.000001f;
    }

    private static int SelectShaderIndex(TiePacket? packet, TieTriangleStrip strip)
    {
        if (strip.ShaderIndex is { } decodedShaderIndex)
        {
            return decodedShaderIndex;
        }

        if (packet is null || packet.ShaderReferences.Count == 0)
        {
            return -1;
        }

        var shaderReferenceIndex = 0;
        var switchCount = Math.Min(packet.ShaderSwitchVuAddresses.Count, packet.ShaderReferences.Count - 1);
        for (var i = 0; i < switchCount; i++)
        {
            if (packet.ShaderSwitchVuAddresses[i] > 0 && strip.VuAddress >= packet.ShaderSwitchVuAddresses[i])
            {
                shaderReferenceIndex = i + 1;
            }
        }

        var shaderIndex = packet.ShaderReferences[shaderReferenceIndex].ShaderIndex;
        return shaderIndex >= 0 ? shaderIndex : -1;
    }

    private static int[] GetPacketShaderIndices(TiePacket? packet)
    {
        return packet?.ShaderReferences
            .Select(reference => reference.ShaderIndex)
            .Where(shaderIndex => shaderIndex >= 0)
            .Distinct()
            .ToArray() ?? [];
    }

    private static void ValidateTriangleIndex(TieLodTopology topology, int index)
    {
        if (index < 0 || index >= topology.LogicalVertexCount)
        {
            throw new InvalidDataException(
                $"Tie LOD {topology.LodIndex} triangle vertex index {index} is outside logical vertex count {topology.LogicalVertexCount}.");
        }
    }

}

internal sealed record TieGltfPacketIndexGroupBuildResult(
    IReadOnlyList<PacketIndexGroup> PacketIndexGroups,
    int PacketRgbaSlotCount);

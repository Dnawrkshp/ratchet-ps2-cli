using static RatchetPs2.Core.Ties.TieBinaryReaderUtils;

namespace RatchetPs2.Core.Ties;

internal static class TiePacketControlDecoder
{
    public const int PacketSetupQwordCount = 2;
    private const int PacketSetupWordCount = 4;
    private const int PacketControlStartQword = 2;
    private const int PacketStripControlStartIndex = 3;
    private const byte PacketScissorEndToken = 0xF6;

    public static List<TiePacketSetupRow> DecodeSetupRows(byte[] bytes, TiePacket packet)
    {
        var offset = packet.AbsoluteDataOffset;
        var length = PacketSetupQwordCount * 0x10;
        EnsureRange(bytes, offset, length, $"setup rows LOD{packet.LodIndex}[{packet.PacketIndex}]");

        var rows = new List<TiePacketSetupRow>(PacketSetupQwordCount);
        for (var rowIndex = 0; rowIndex < PacketSetupQwordCount; rowIndex++)
        {
            var rowOffset = offset + rowIndex * 0x10;
            var words = new List<TiePacketSetupWord>(PacketSetupWordCount);
            for (var wordIndex = 0; wordIndex < PacketSetupWordCount; wordIndex++)
            {
                var wordOffset = rowOffset + wordIndex * sizeof(int);
                words.Add(new TiePacketSetupWord
                {
                    RowIndex = rowIndex,
                    WordIndex = wordIndex,
                    Offset = wordOffset,
                    Raw = BitConverter.ToInt32(bytes, wordOffset),
                    Role = GetSetupWordRole(rowIndex, wordIndex, packet.ShaderCount)
                });
            }

            rows.Add(new TiePacketSetupRow
            {
                Index = rowIndex,
                Offset = rowOffset,
                Bytes = Slice(bytes, rowOffset, 0x10),
                Words = words
            });
        }

        return rows;
    }

    public static List<TiePacketControlRow> DecodeControlRows(byte[] bytes, TiePacket packet)
    {
        if (packet.ControlCount == 0)
        {
            return [];
        }

        var offset = packet.AbsoluteDataOffset + PacketControlStartQword * 0x10;
        var length = packet.ControlCount * 4;
        EnsureRange(bytes, offset, length, $"control rows LOD{packet.LodIndex}[{packet.PacketIndex}]");

        var rows = new List<TiePacketControlRow>(packet.ControlCount);
        for (var i = 0; i < packet.ControlCount; i++)
        {
            var rowOffset = offset + i * 4;
            rows.Add(new TiePacketControlRow
            {
                Index = i,
                Offset = rowOffset,
                Data0 = bytes[rowOffset],
                Data1 = bytes[rowOffset + 1],
                Data2 = bytes[rowOffset + 2],
                Data3 = bytes[rowOffset + 3],
                Raw = BitConverter.ToUInt32(bytes, rowOffset),
                IsStripControl = i >= PacketStripControlStartIndex
            });
        }

        return rows;
    }

    public static TiePacketUnpackHeader? DecodeUnpackHeader(IReadOnlyList<TiePacketControlRow> controlRows)
    {
        if (controlRows.Count < PacketStripControlStartIndex)
        {
            return null;
        }

        var bytes = controlRows
            .Take(PacketStripControlStartIndex)
            .SelectMany(row => new[] { row.Data0, row.Data1, row.Data2, row.Data3 })
            .ToArray();
        return new TiePacketUnpackHeader
        {
            Unknown0 = bytes[0],
            Unknown1 = bytes[1],
            Unknown2 = bytes[2],
            StripCount = bytes[3],
            Unknown4 = bytes[4],
            Unknown5 = bytes[5],
            Unknown6 = bytes[6],
            Unknown7 = bytes[7],
            DinkyVerticesSizePlusFour = bytes[8],
            FatVerticesSize = bytes[9],
            Unknown10 = bytes[10],
            Unknown11 = bytes[11]
        };
    }

    public static List<TiePacketStripControl> DecodeStripControls(
        byte[] bytes,
        TiePacket packet,
        IReadOnlyList<TiePacketControlRow> controlRows)
    {
        if (controlRows.Count <= PacketStripControlStartIndex || packet.ScissorSize == 0)
        {
            return [];
        }

        var scissorOffset = packet.AbsoluteDataOffset + packet.ScissorOffset * 0x10;
        var scissorLength = packet.ScissorSize * 0x10;
        EnsureRange(bytes, scissorOffset, scissorLength, $"scissor rows LOD{packet.LodIndex}[{packet.PacketIndex}]");

        var tokenOffset = 0;
        var strips = new List<TiePacketStripControl>(controlRows.Count - PacketStripControlStartIndex);
        foreach (var row in controlRows.Skip(PacketStripControlStartIndex))
        {
            var available = Math.Max(0, scissorLength - tokenOffset);
            var tokenCount = Math.Min(row.Data0, available);
            byte[] tokens = tokenCount == 0
                ? []
                : Slice(bytes, scissorOffset + tokenOffset, tokenCount);
            var decodedTokens = DecodeStripTokens(
                strips.Count,
                row,
                scissorOffset,
                tokenOffset,
                tokens);
            strips.Add(new TiePacketStripControl
            {
                Index = strips.Count,
                ControlRowIndex = row.Index,
                Offset = row.Offset,
                TokenCount = row.Data0,
                TokenOffset = tokenOffset,
                VuAddress = row.Data2,
                ControlData1 = row.Data1,
                Flags = row.Data3,
                Tokens = tokens,
                DecodedTokens = decodedTokens
            });

            tokenOffset += row.Data0;
        }

        return strips;
    }

    public static List<TiePacketScissorToken> DecodeScissorTokens(
        byte[] bytes,
        TiePacket packet,
        IReadOnlyList<TiePacketStripControl> stripControls)
    {
        if (packet.ScissorSize == 0)
        {
            return [];
        }

        var scissorOffset = packet.AbsoluteDataOffset + packet.ScissorOffset * 0x10;
        var scissorLength = packet.ScissorSize * 0x10;
        EnsureRange(bytes, scissorOffset, scissorLength, $"scissor rows LOD{packet.LodIndex}[{packet.PacketIndex}]");

        var stripIndexByTokenIndex = new Dictionary<int, int>();
        var stripTokenCount = 0;
        foreach (var strip in stripControls)
        {
            stripTokenCount += strip.TokenCount;
            for (var i = 0; i < strip.Tokens.Length; i++)
            {
                stripIndexByTokenIndex[strip.TokenOffset + i] = strip.Index;
            }
        }

        var count = stripControls.Count == 0
            ? scissorLength
            : Math.Min(scissorLength, stripTokenCount + (stripTokenCount < scissorLength ? 1 : 0));
        var tokens = new List<TiePacketScissorToken>(count);
        for (var i = 0; i < count; i++)
        {
            var value = bytes[scissorOffset + i];
            tokens.Add(new TiePacketScissorToken
            {
                Index = i,
                Offset = scissorOffset + i,
                Value = value,
                StripIndex = stripIndexByTokenIndex.TryGetValue(i, out var stripIndex) ? stripIndex : null,
                IsEndToken = i == stripTokenCount && value == PacketScissorEndToken
            });
        }

        return tokens;
    }

    private static TiePacketSetupWordRole GetSetupWordRole(int rowIndex, int wordIndex, int shaderCount)
    {
        if (rowIndex == 0 && wordIndex < Math.Max(0, shaderCount - 1))
        {
            return TiePacketSetupWordRole.ShaderSwitchVuAddress;
        }

        if (rowIndex == 1 && wordIndex < shaderCount)
        {
            return TiePacketSetupWordRole.ShaderByteOffset;
        }

        return TiePacketSetupWordRole.Unknown;
    }

    private static List<TiePacketStripToken> DecodeStripTokens(
        int stripIndex,
        TiePacketControlRow row,
        int scissorOffset,
        int tokenOffset,
        byte[] tokens)
    {
        var decoded = new List<TiePacketStripToken>(tokens.Length);
        int? resolvedGsPacketWriteOffset = null;
        var stripReferencesPreviousVertex = tokens.Length > 0 && unchecked((sbyte)tokens[0]) < 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            var value = tokens[i];
            var signedValue = unchecked((sbyte)value);
            var expectedGsPacketWriteOffset = row.Data2 + 1 + i * 3;
            var stripWriteBaseGsPacketOffset = row.Data2 + 1;
            var mode = TiePacketStripTokenAddressMode.Unknown;
            int? restartGap = null;
            int? tokenResolvedGsPacketWriteOffset = null;
            int? referencedGsPacketWriteOffset = null;
            var referencesPreviousStripVertex = i == 0 && signedValue < 0;

            if (i == 0 && signedValue >= 0)
            {
                mode = TiePacketStripTokenAddressMode.AbsoluteVertexWriteOffset;
                tokenResolvedGsPacketWriteOffset = value;
            }
            else if (referencesPreviousStripVertex)
            {
                mode = TiePacketStripTokenAddressMode.PreviousStripVertexReference;
                restartGap = -signedValue;
                tokenResolvedGsPacketWriteOffset = expectedGsPacketWriteOffset;
                referencedGsPacketWriteOffset = stripWriteBaseGsPacketOffset + signedValue;
            }
            else if (stripReferencesPreviousVertex && signedValue > 0)
            {
                mode = TiePacketStripTokenAddressMode.ForwardVertexWriteOffsetStep;
                tokenResolvedGsPacketWriteOffset = expectedGsPacketWriteOffset;
                referencedGsPacketWriteOffset = stripWriteBaseGsPacketOffset + (i - 1) * signedValue;
            }
            else if (signedValue > 0 && resolvedGsPacketWriteOffset.HasValue)
            {
                mode = TiePacketStripTokenAddressMode.ForwardVertexWriteOffsetStep;
                tokenResolvedGsPacketWriteOffset = resolvedGsPacketWriteOffset.Value + signedValue;
            }

            referencedGsPacketWriteOffset ??= tokenResolvedGsPacketWriteOffset;
            if (tokenResolvedGsPacketWriteOffset.HasValue)
            {
                resolvedGsPacketWriteOffset = tokenResolvedGsPacketWriteOffset;
            }

            decoded.Add(new TiePacketStripToken
            {
                Index = tokenOffset + i,
                Offset = scissorOffset + tokenOffset + i,
                StripIndex = stripIndex,
                IndexInStrip = i,
                Value = value,
                SignedValue = signedValue,
                AddressMode = mode,
                ResolvedGsPacketWriteOffset = tokenResolvedGsPacketWriteOffset,
                ReferencedGsPacketWriteOffset = referencedGsPacketWriteOffset,
                ExpectedGsPacketWriteOffset = expectedGsPacketWriteOffset,
                MatchesExpectedGsPacketWriteOffset = tokenResolvedGsPacketWriteOffset == expectedGsPacketWriteOffset,
                ReferencesPreviousStripVertex = referencesPreviousStripVertex,
                RestartGap = restartGap
            });
        }

        return decoded;
    }
}

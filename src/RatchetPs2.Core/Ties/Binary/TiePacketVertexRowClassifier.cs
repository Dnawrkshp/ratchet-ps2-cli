namespace RatchetPs2.Core.Ties;

internal enum TiePacketVertexPositionSlot
{
    First,
    Second
}

internal static class TiePacketVertexRowClassifier
{
    private const short TextureCoordinateQ = 4096;

    // (0, 0, 0) is a valid center coordinate in fixtures such as 9303.
    private static readonly (short X, short Y, short Z)[] NonPositionVertexVectors =
    [
        (0, 24, 14),
        (2048, 4096, 4096)
    ];

    public static bool TrySelectPositionSlot(TiePacketVertexRow row, out TiePacketVertexPositionSlot slot)
    {
        if (row.Data2 == TextureCoordinateQ)
        {
            if (IsLikelySecondSlotCoordinateWithTextureQ(row))
            {
                slot = TiePacketVertexPositionSlot.Second;
                return true;
            }

            slot = TiePacketVertexPositionSlot.First;
            return !IsNonPositionVector(row.X, row.Y, row.Z)
                && !IsAttributeVector(row.X, row.Y, row.Z);
        }

        slot = TiePacketVertexPositionSlot.Second;
        return !IsNonPositionVector(row.Data0, row.Data1, row.Data2);
    }

    public static bool UsesSecondPositionSlot(TiePacketVertexRow row)
    {
        return TrySelectPositionSlot(row, out var slot)
            && slot == TiePacketVertexPositionSlot.Second;
    }

    public static bool IsNonPositionVector(short x, short y, short z)
    {
        return NonPositionVertexVectors.Contains((x, y, z));
    }

    public static bool IsAttributeVector(short x, short y, short z)
    {
        return z == TextureCoordinateQ;
    }

    private static bool IsLikelySecondSlotCoordinateWithTextureQ(TiePacketVertexRow row)
    {
        return IsSmallAddressMarkerVector(row.X, row.Y, row.Z)
            && HasBroadCoordinateComponent(row.Data0, row.Data1)
            && !IsNonPositionVector(row.Data0, row.Data1, row.Data2);
    }

    private static bool IsSmallAddressMarkerVector(short x, short y, short z)
    {
        return AbsCoordinate(x) <= 256
            && AbsCoordinate(y) <= 256
            && AbsCoordinate(z) <= 512
            && z != 0;
    }

    private static bool HasBroadCoordinateComponent(short x, short y)
    {
        return AbsCoordinate(x) > 8192 || AbsCoordinate(y) > 8192;
    }

    private static int AbsCoordinate(short value)
    {
        return Math.Abs((int)value);
    }
}

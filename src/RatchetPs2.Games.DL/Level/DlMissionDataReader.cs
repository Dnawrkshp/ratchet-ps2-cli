using System.Buffers.Binary;

namespace RatchetPs2.Games.DL.Level;

public static class DlMissionDataReader
{
    public static bool IsPlaceholderMissionData(ReadOnlySpan<byte> data)
    {
        if (data.Length != DlLevelConstants.SectorSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(data[0x00..]) != -1
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x04..]) != 0
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x08..]) != -1
            || BinaryPrimitives.ReadInt32LittleEndian(data[0x0c..]) != 0)
        {
            return false;
        }

        for (var i = 0x10; i < data.Length; i++)
        {
            if (data[i] != 0)
            {
                return false;
            }
        }

        return true;
    }
}

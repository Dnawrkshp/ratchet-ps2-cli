using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static Ps2VifPacketSpan? TryFindTopologyPacket(byte[] vifData)
    {
        var packets = Ps2VifPacket.ReadSpans(vifData);
        for (var i = packets.Count - 1; i >= 0; i--)
        {
            var packet = packets[i];
            if (packet.IsUnpack && (packet.Command & 0x0F) == 0x0E)
            {
                return packet;
            }
        }

        return null;
    }
}

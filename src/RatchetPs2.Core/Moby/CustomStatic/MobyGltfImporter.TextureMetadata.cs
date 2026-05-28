using System.Buffers.Binary;
using RatchetPs2.Core.IO.Vif;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static byte[]? BuildConstantTexturePayload(byte[]? textureData)
    {
        if (textureData is null)
        {
            return null;
        }

        var result = (byte[])textureData.Clone();
        foreach (var packet in Ps2VifPacket.ReadSpans(result))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x0C || packet.PayloadLength < 0x10)
            {
                continue;
            }

            var payloadOffset = packet.Offset + 4;
            var payloadLength = Math.Min(packet.PayloadLength, result.Length - payloadOffset);
            if (payloadLength < 0x10)
            {
                continue;
            }

            var record = result[payloadOffset..(payloadOffset + 0x10)];
            for (var offset = payloadOffset + 0x10; offset + 0x10 <= payloadOffset + payloadLength; offset += 0x10)
            {
                record.CopyTo(result.AsSpan(offset, 0x10));
            }
        }

        return result;
    }

    private static byte? ResolveCustomStaticMaterialTextureId(
        ImportedMesh mesh,
        MobyGltfImportOptions options)
    {
        if (mesh.CustomStaticHideMesh
            || options.CustomStaticMaterialTextureIds is null
            || string.IsNullOrWhiteSpace(mesh.CustomStaticSourceMaterialName))
        {
            return null;
        }

        return options.CustomStaticMaterialTextureIds.TryGetValue(mesh.CustomStaticSourceMaterialName, out var textureId)
            ? textureId
            : null;
    }

    private static byte? TryApplyCustomStaticMaterialTextureId(
        MobyMeshTableEntry entry,
        ImportedMesh mesh,
        MobyGltfImportOptions options)
    {
        var textureId = ResolveCustomStaticMaterialTextureId(mesh, options);
        if (textureId is null || entry.GifTag is null)
        {
            return null;
        }

        if (entry.GifTag.TextureIds.Length < 0x0C)
        {
            var textureIds = entry.GifTag.TextureIds;
            Array.Resize(ref textureIds, 0x0C);
            entry.GifTag.TextureIds = textureIds;
        }

        WriteCustomStaticGifTextureIds(entry.GifTag.TextureIds, textureId.Value, options);
        WriteActiveTextureIdToVifTextureData(entry.VifTextureData, textureId.Value);
        return textureId;
    }

    private static void WriteCustomStaticGifTextureIds(
        byte[] textureIds,
        byte textureId,
        MobyGltfImportOptions options)
    {
        textureIds[0] = textureId;
        var relatedTextureIds = options.CustomStaticMaterialTextureIds?.Values
            .Distinct()
            .Where(id => id != textureId)
            .ToList() ?? [];
        var writeIndex = 1;
        foreach (var mappedTextureId in relatedTextureIds)
        {
            if (writeIndex >= textureIds.Length)
            {
                break;
            }

            textureIds[writeIndex++] = mappedTextureId;
        }

        while (writeIndex < textureIds.Length)
        {
            textureIds[writeIndex++] = 0xFF;
        }
    }

    private static byte[] BuildEmptyGifTextureIdList()
    {
        var textureIds = new byte[0x0C];
        Array.Fill(textureIds, (byte)0xFF);
        return textureIds;
    }

    private static byte[] BuildCustomStaticTextureMetadataPayload(byte activeTextureId = 0, float? distance = null)
    {
        byte[] payload =
        [
            0x55, 0x27, 0x26, 0x28,
            0x01, 0x01, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x4C, 0x81, 0x04, 0x6C,
            0x92, 0xFF, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00,
            0x04, 0x00, 0xA0, 0x41,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x08, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x34, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        ];

        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0x20, 4), activeTextureId);

        if (distance is float textureDistance)
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(0x18, 4), textureDistance);
        }

        return payload;
    }

    private static void WriteActiveTextureIdToVifTextureData(byte[]? vifTextureData, byte textureId)
    {
        if (vifTextureData is null)
        {
            return;
        }

        foreach (var packet in Ps2VifPacket.ReadSpans(vifTextureData))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x0C || packet.PayloadLength < 0x30)
            {
                continue;
            }

            var payloadOffset = packet.Offset + 4;
            BinaryPrimitives.WriteUInt32LittleEndian(vifTextureData.AsSpan(payloadOffset + 0x20, 4), textureId);
            return;
        }
    }

    private static uint? TryReadActiveTextureIdFromVifTextureData(byte[]? vifTextureData)
    {
        if (vifTextureData is null)
        {
            return null;
        }

        foreach (var packet in Ps2VifPacket.ReadSpans(vifTextureData))
        {
            if (!packet.IsUnpack || (packet.Command & 0x0F) != 0x0C || packet.PayloadLength < 0x30)
            {
                continue;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(vifTextureData.AsSpan(packet.Offset + 4 + 0x20, 4));
        }

        return null;
    }
}

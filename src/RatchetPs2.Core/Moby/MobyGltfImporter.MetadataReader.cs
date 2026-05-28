using System.Text.Json;

namespace RatchetPs2.Core.Moby;

public static partial class MobyGltfImporter
{
    private static ImportedMeshMetadata? TryReadMobyMeshMetadata(JsonElement mesh, JsonElement node)
    {
        var metadata = TryReadMobyMeshMetadataFromExtras(mesh)
            ?? TryReadMobyMeshMetadataFromExtras(node);
        return metadata;
    }

    private static ImportedMeshMetadata? TryReadMobyMeshMetadataFromExtras(JsonElement element)
    {
        if (!element.TryGetProperty("extras", out var extras)
            || !extras.TryGetProperty("RatchetPs2", out var ratchet)
            || (!ratchet.TryGetProperty("moby", out var moby)
                && !ratchet.TryGetProperty("UYA", out moby)))
        {
            return null;
        }

        var kind = moby.TryGetProperty("kind", out var kindElement)
            ? kindElement.GetString()
            : null;
        var version = moby.TryGetProperty("version", out var versionElement)
            ? versionElement.GetInt32()
            : 0;
        var topology = moby.TryGetProperty("topologyPacket", out var topologyElement)
            && topologyElement.ValueKind == JsonValueKind.Object
                ? TryReadTopologyMetadata(topologyElement)
                : null;
        var vertexLayout = moby.TryGetProperty("vertexLayout", out var vertexLayoutElement)
            && vertexLayoutElement.ValueKind == JsonValueKind.Object
                ? TryReadVertexLayoutMetadata(vertexLayoutElement)
                : null;
        return new ImportedMeshMetadata(kind ?? string.Empty, version, topology, vertexLayout);
    }

    private static ImportedVertexLayoutMetadata? TryReadVertexLayoutMetadata(JsonElement layout)
    {
        if (!layout.TryGetProperty("supported", out var supportedElement))
        {
            return null;
        }

        var matrixTransfers = new List<ImportedMatrixTransferMetadata>();
        if (layout.TryGetProperty("matrixTransfers", out var transfersElement)
            && transfersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var transfer in transfersElement.EnumerateArray())
            {
                if (!transfer.TryGetProperty("joint", out var jointElement)
                    || !transfer.TryGetProperty("vu0DestinationAddress", out var addressElement))
                {
                    continue;
                }

                matrixTransfers.Add(new ImportedMatrixTransferMetadata(jointElement.GetInt32(), addressElement.GetInt32()));
            }
        }

        var duplicateIndices = new List<int>();
        if (layout.TryGetProperty("duplicateIndices", out var duplicatesElement)
            && duplicatesElement.ValueKind == JsonValueKind.Array)
        {
            duplicateIndices.AddRange(duplicatesElement.EnumerateArray().Select(element => element.GetInt32()));
        }

        var low9StorageValues = new List<int>();
        if (layout.TryGetProperty("low9StorageValues", out var low9Element)
            && low9Element.ValueKind == JsonValueKind.Array)
        {
            low9StorageValues.AddRange(low9Element.EnumerateArray().Select(element => element.GetInt32()));
        }

        var rowPrefixBytes = Array.Empty<byte>();
        if (layout.TryGetProperty("rowPrefixBytesBase64", out var rowPrefixElement)
            && rowPrefixElement.ValueKind == JsonValueKind.String)
        {
            rowPrefixBytes = TryReadBase64Bytes(rowPrefixElement);
        }

        var headerBytes = layout.TryGetProperty("headerBytesBase64", out var headerElement)
            && headerElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(headerElement)
                : Array.Empty<byte>();
        var epilogueBytes = layout.TryGetProperty("epilogueBytesBase64", out var epilogueElement)
            && epilogueElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(epilogueElement)
                : Array.Empty<byte>();

        return new ImportedVertexLayoutMetadata(
            supportedElement.GetBoolean(),
            GetInt32OrDefault(layout, "matrixTransferCount"),
            GetInt32OrDefault(layout, "twoWayBlendVertexCount"),
            GetInt32OrDefault(layout, "threeWayBlendVertexCount"),
            GetInt32OrDefault(layout, "mainVertexCount"),
            GetInt32OrDefault(layout, "duplicateVertexCount"),
            GetInt32OrDefault(layout, "vertexTableOffset"),
            GetInt32OrDefault(layout, "duplicateIndicesOffset"),
            GetInt32OrDefault(layout, "epilogueVertexCount"),
            headerBytes,
            epilogueBytes,
            matrixTransfers,
            duplicateIndices,
            low9StorageValues,
            rowPrefixBytes);
    }

    private static byte[] TryReadBase64Bytes(JsonElement element)
    {
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<byte>();
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private static int GetInt32OrDefault(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetInt32()
            : 0;
    }

    private static ImportedTopologyMetadata? TryReadTopologyMetadata(JsonElement topology)
    {
        if (!topology.TryGetProperty("offset", out var offsetElement)
            || !topology.TryGetProperty("immediate", out var immediateElement)
            || !topology.TryGetProperty("commandByte", out var commandByteElement)
            || !topology.TryGetProperty("vifDataSplitOffset", out var splitElement))
        {
            return null;
        }

        var payloadBase64 = topology.TryGetProperty("payloadBase64", out var payloadElement)
            ? payloadElement.GetString()
            : null;
        var alignedPayloadBytes = topology.TryGetProperty("alignedPayloadBase64", out var alignedPayloadElement)
            && alignedPayloadElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(alignedPayloadElement)
                : Array.Empty<byte>();
        var payloadPaddingBytes = topology.TryGetProperty("payloadPaddingBase64", out var payloadPaddingElement)
            && payloadPaddingElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(payloadPaddingElement)
                : Array.Empty<byte>();
        var payloadBytes = new List<int>();
        if (topology.TryGetProperty("payloadBytes", out var payloadBytesElement)
            && payloadBytesElement.ValueKind == JsonValueKind.Array)
        {
            payloadBytes.AddRange(payloadBytesElement.EnumerateArray().Select(element => element.GetInt32()));
        }
        var payloadPrefixBytes = new List<int>();
        if (topology.TryGetProperty("payloadPrefixBytes", out var payloadPrefixElement)
            && payloadPrefixElement.ValueKind == JsonValueKind.Array)
        {
            payloadPrefixBytes.AddRange(payloadPrefixElement.EnumerateArray().Select(element => element.GetInt32()));
        }

        var payloadTokens = new List<ImportedTopologyPayloadToken>();
        if (topology.TryGetProperty("payloadTokens", out var payloadTokensElement)
            && payloadTokensElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var token in payloadTokensElement.EnumerateArray())
            {
                var kind = token.TryGetProperty("kind", out var kindElement)
                    ? kindElement.GetString() ?? string.Empty
                    : string.Empty;
                var negative = token.TryGetProperty("negative", out var negativeElement)
                    && negativeElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && negativeElement.GetBoolean();
                var vertexIndex = token.TryGetProperty("vertexIndex", out var vertexIndexElement)
                    && vertexIndexElement.ValueKind == JsonValueKind.Number
                        ? vertexIndexElement.GetInt32()
                        : -1;
                payloadTokens.Add(new ImportedTopologyPayloadToken(kind, negative, vertexIndex));
            }
        }

        var beforePacketBytes = topology.TryGetProperty("beforePacketBase64", out var beforePacketElement)
            && beforePacketElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(beforePacketElement)
                : Array.Empty<byte>();
        var afterPacketBytes = topology.TryGetProperty("afterPacketBase64", out var afterPacketElement)
            && afterPacketElement.ValueKind == JsonValueKind.String
                ? TryReadBase64Bytes(afterPacketElement)
                : Array.Empty<byte>();
        return new ImportedTopologyMetadata(
            offsetElement.GetInt32(),
            immediateElement.GetInt32(),
            commandByteElement.GetInt32(),
            splitElement.GetInt32(),
            payloadBase64 ?? string.Empty,
            alignedPayloadBytes,
            payloadPaddingBytes,
            payloadBytes,
            payloadPrefixBytes,
            payloadTokens,
            beforePacketBytes,
            afterPacketBytes);
    }

}

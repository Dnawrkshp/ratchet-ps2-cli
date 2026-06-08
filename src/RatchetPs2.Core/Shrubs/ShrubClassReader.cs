using System.Numerics;
using RatchetPs2.Core.IO.Vif;
using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Core.Shrubs;

public static class ShrubClassReader
{
    private const int PacketEntrySize = 0x8;
    private const int PacketHeaderSize = 0x10;
    private const int GifTagSize = 0x10;
    private const int TexturePrimitiveSize = 0x40;
    private const int VertexPartSize = 0x8;
    private const int NormalCount = 24;
    private const int NormalSize = 0x8;
    private const int BillboardSize = 0x40;

    public static ShrubClass Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var memory = new MemoryStream();
        input.CopyTo(memory);
        var bytes = memory.ToArray();
        if (bytes.Length < ShrubClassHeader.Size)
        {
            throw new InvalidDataException("Shrub class binary is smaller than the 0x40-byte header.");
        }

        var header = ReadHeader(bytes);
        if (header.PacketCount < 0)
        {
            throw new InvalidDataException($"Shrub packet count {header.PacketCount} is invalid.");
        }

        EnsureRange(bytes, ShrubClassHeader.Size, checked(header.PacketCount * PacketEntrySize), "shrub packet table");
        var packetEntries = new List<ShrubPacketEntry>(header.PacketCount);
        for (var i = 0; i < header.PacketCount; i++)
        {
            var offset = ShrubClassHeader.Size + (i * PacketEntrySize);
            var entry = new ShrubPacketEntry(
                ReadInt32LittleEndian(bytes, offset),
                ReadInt32LittleEndian(bytes, offset + 4));
            EnsureRange(bytes, entry.Offset, entry.Size, $"shrub packet {i}");
            packetEntries.Add(entry);
        }

        var packets = new List<ShrubPacket>(packetEntries.Count);
        for (var i = 0; i < packetEntries.Count; i++)
        {
            packets.Add(ReadPacket(bytes.AsSpan(packetEntries[i].Offset, packetEntries[i].Size), i, packetEntries[i]));
        }

        var billboard = header.BillboardOffset > 0
            ? ReadBillboard(bytes, header.BillboardOffset)
            : null;
        var normals = ReadNormals(bytes, header.NormalsOffset);

        return new ShrubClass
        {
            Header = header,
            ByteLength = bytes.Length,
            Packets = packets,
            Normals = normals,
            Billboard = billboard
        };
    }

    private static ShrubClassHeader ReadHeader(ReadOnlySpan<byte> bytes)
    {
        return new ShrubClassHeader
        {
            BoundingSphere = new Vector4(
                ReadSingleLittleEndian(bytes, 0x00),
                ReadSingleLittleEndian(bytes, 0x04),
                ReadSingleLittleEndian(bytes, 0x08),
                ReadSingleLittleEndian(bytes, 0x0C)),
            MipDistance = ReadSingleLittleEndian(bytes, 0x10),
            ModeBits = ReadUInt16LittleEndian(bytes, 0x14),
            InstanceCount = ReadInt16LittleEndian(bytes, 0x16),
            InstancesPointer = ReadInt32LittleEndian(bytes, 0x18),
            BillboardOffset = ReadInt32LittleEndian(bytes, 0x1C),
            Scale = ReadSingleLittleEndian(bytes, 0x20),
            OClass = ReadInt16LittleEndian(bytes, 0x24),
            SClass = ReadInt16LittleEndian(bytes, 0x26),
            PacketCount = ReadInt16LittleEndian(bytes, 0x28),
            Padding2A = ReadInt16LittleEndian(bytes, 0x2A),
            NormalsOffset = ReadInt32LittleEndian(bytes, 0x2C),
            Padding30 = ReadInt32LittleEndian(bytes, 0x30),
            DrawnCount = ReadInt16LittleEndian(bytes, 0x34),
            ScisCount = ReadInt16LittleEndian(bytes, 0x36),
            BillboardCount = ReadInt16LittleEndian(bytes, 0x38),
            Padding3A = ReadInt16LittleEndian(bytes, 0x3A),
            Padding3C = ReadInt16LittleEndian(bytes, 0x3C),
            Padding3E = ReadInt16LittleEndian(bytes, 0x3E)
        };
    }

    private static ShrubPacket ReadPacket(ReadOnlySpan<byte> packetBytes, int packetIndex, ShrubPacketEntry entry)
    {
        var unpacks = Ps2VifPacket.ReadSpans(packetBytes)
            .Where(packet => packet.IsUnpack)
            .ToArray();
        if (unpacks.Length != 3)
        {
            throw new InvalidDataException(
                $"Shrub packet {packetIndex} has {unpacks.Length} VIF unpack(s); expected 3.");
        }

        var headerPayload = Payload(packetBytes, unpacks[0]);
        var vertexPayload = Payload(packetBytes, unpacks[1]);
        var texCoordPayload = Payload(packetBytes, unpacks[2]);
        EnsureRange(headerPayload, 0, PacketHeaderSize, $"shrub packet {packetIndex} unpack header");

        var packetHeader = new ShrubPacketHeader(
            ReadInt32LittleEndian(headerPayload, 0x00),
            ReadInt32LittleEndian(headerPayload, 0x04),
            ReadInt32LittleEndian(headerPayload, 0x08),
            ReadInt32LittleEndian(headerPayload, 0x0C));
        if (packetHeader.TextureCount < 0 || packetHeader.GifTagCount < 0 || packetHeader.VertexCount < 0)
        {
            throw new InvalidDataException($"Shrub packet {packetIndex} has negative element counts.");
        }

        var gifTagsOffset = PacketHeaderSize;
        var texturesOffset = checked(gifTagsOffset + (packetHeader.GifTagCount * GifTagSize));
        EnsureRange(
            headerPayload,
            texturesOffset,
            checked(packetHeader.TextureCount * TexturePrimitiveSize),
            $"shrub packet {packetIndex} texture primitive table");
        EnsureRange(
            vertexPayload,
            0,
            checked(packetHeader.VertexCount * VertexPartSize),
            $"shrub packet {packetIndex} vertex part 1");
        EnsureRange(
            texCoordPayload,
            0,
            checked(packetHeader.VertexCount * VertexPartSize),
            $"shrub packet {packetIndex} vertex part 2");

        var gifTags = ReadGifTags(headerPayload, gifTagsOffset, packetHeader.GifTagCount);
        var textures = ReadTexturePrimitives(headerPayload, texturesOffset, packetHeader.TextureCount);
        var vertices1 = ReadVertexPart1(vertexPayload, packetHeader.VertexCount);
        var vertices2 = ReadVertexPart2(texCoordPayload, packetHeader.VertexCount);
        var primitives = InterleavePrimitives(packetIndex, gifTags, textures, vertices1, vertices2);

        return new ShrubPacket
        {
            PacketIndex = packetIndex,
            Entry = entry,
            Header = packetHeader,
            Primitives = primitives
        };
    }

    private static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packetBytes, Ps2VifPacketSpan packet)
    {
        var payloadOffset = checked(packet.Offset + 4);
        var payloadLength = Math.Min(packet.PayloadLength, packetBytes.Length - payloadOffset);
        return packetBytes.Slice(payloadOffset, payloadLength);
    }

    private static List<ShrubGifTag> ReadGifTags(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        var tags = new List<ShrubGifTag>(count);
        for (var i = 0; i < count; i++)
        {
            var tagOffset = offset + (i * GifTagSize);
            var low = ReadUInt64LittleEndian(bytes, tagOffset);
            var prim = (int)((low >> 47) & 0x7FF);
            var primitiveType = prim & 0x07;
            var geometryType = primitiveType switch
            {
                0b011 => ShrubGeometryType.TriangleList,
                0b100 => ShrubGeometryType.TriangleStrip,
                _ => throw new InvalidDataException(
                    $"Shrub GIF tag {i} uses unsupported GS primitive type {primitiveType}.")
            };
            tags.Add(new ShrubGifTag(ReadInt32LittleEndian(bytes, tagOffset + 0x0C), geometryType));
        }

        return tags;
    }

    private static List<ShrubTexturePrimitive> ReadTexturePrimitives(ReadOnlySpan<byte> bytes, int offset, int count)
    {
        var textures = new List<ShrubTexturePrimitive>(count);
        for (var i = 0; i < count; i++)
        {
            var textureOffset = offset + (i * TexturePrimitiveSize);
            var sourceBytes = bytes.Slice(textureOffset, TexturePrimitiveSize).ToArray();
            textures.Add(new ShrubTexturePrimitive(
                ReadInt32LittleEndian(bytes, textureOffset + 0x0C),
                ReadInt32LittleEndian(bytes, textureOffset + 0x30),
                sourceBytes));
        }

        return textures;
    }

    private static List<ShrubVertexPart1> ReadVertexPart1(ReadOnlySpan<byte> bytes, int count)
    {
        var vertices = new List<ShrubVertexPart1>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * VertexPartSize;
            vertices.Add(new ShrubVertexPart1(
                ReadInt16LittleEndian(bytes, offset),
                ReadInt16LittleEndian(bytes, offset + 0x02),
                ReadInt16LittleEndian(bytes, offset + 0x04),
                ReadInt16LittleEndian(bytes, offset + 0x06)));
        }

        return vertices;
    }

    private static List<ShrubVertexPart2> ReadVertexPart2(ReadOnlySpan<byte> bytes, int count)
    {
        var vertices = new List<ShrubVertexPart2>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * VertexPartSize;
            vertices.Add(new ShrubVertexPart2(
                ReadInt16LittleEndian(bytes, offset),
                ReadInt16LittleEndian(bytes, offset + 0x02),
                ReadInt16LittleEndian(bytes, offset + 0x04),
                ReadInt16LittleEndian(bytes, offset + 0x06)));
        }

        return vertices;
    }

    private static List<ShrubPrimitive> InterleavePrimitives(
        int packetIndex,
        IReadOnlyList<ShrubGifTag> gifTags,
        IReadOnlyList<ShrubTexturePrimitive> textures,
        IReadOnlyList<ShrubVertexPart1> vertices1,
        IReadOnlyList<ShrubVertexPart2> vertices2)
    {
        var primitives = new List<ShrubPrimitive>();
        var nextGifTag = 0;
        var nextTexture = 0;
        var nextVertex = 0;
        var nextOffset = 0;
        ShrubGeometryType? activeGeometryType = null;
        List<ShrubVertex>? activeVertices = null;
        var activeGsPacketOffset = 0;

        while (nextGifTag < gifTags.Count || nextTexture < textures.Count || nextVertex < vertices1.Count)
        {
            if (nextGifTag < gifTags.Count && gifTags[nextGifTag].GsPacketOffset == nextOffset)
            {
                FlushActiveVertexPrimitive();
                activeGeometryType = gifTags[nextGifTag].GeometryType;
                nextGifTag++;
                nextOffset += 1;
                continue;
            }

            if (nextTexture < textures.Count && textures[nextTexture].GsPacketOffset == nextOffset)
            {
                FlushActiveVertexPrimitive();
                primitives.Add(textures[nextTexture]);
                nextTexture++;
                nextOffset += 5;
                continue;
            }

            if (nextVertex < vertices1.Count && vertices1[nextVertex].GsPacketOffset == nextOffset)
            {
                if (activeGeometryType is null)
                {
                    throw new InvalidDataException(
                        $"Shrub packet {packetIndex} has vertex data before a GIF primitive tag.");
                }

                activeVertices ??= [];
                if (activeVertices.Count == 0)
                {
                    activeGsPacketOffset = nextOffset;
                }

                var p1 = vertices1[nextVertex];
                var p2 = vertices2[nextVertex];
                activeVertices.Add(new ShrubVertex(
                    p1.X,
                    p1.Y,
                    p1.Z,
                    p2.S,
                    p2.T,
                    p2.H,
                    (short)(p2.NormalAndStopCondition & 0x7FFF)));
                nextVertex++;
                nextOffset += 3;
                continue;
            }

            if (nextVertex < vertices1.Count && vertices1[nextVertex].GsPacketOffset == nextOffset - 3)
            {
                break;
            }

            throw new InvalidDataException(
                $"Shrub packet {packetIndex} could not interleave GS packet offset {nextOffset}.");
        }

        FlushActiveVertexPrimitive();
        return primitives;

        void FlushActiveVertexPrimitive()
        {
            if (activeVertices is null || activeVertices.Count == 0 || activeGeometryType is null)
            {
                activeVertices = null;
                return;
            }

            primitives.Add(new ShrubVertexPrimitive(activeGsPacketOffset, activeGeometryType.Value, activeVertices));
            activeVertices = null;
        }
    }

    private static ShrubBillboard ReadBillboard(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, BillboardSize, "shrub billboard");
        return new ShrubBillboard(
            ReadSingleLittleEndian(bytes, offset),
            ReadSingleLittleEndian(bytes, offset + 0x04),
            ReadSingleLittleEndian(bytes, offset + 0x08),
            ReadSingleLittleEndian(bytes, offset + 0x0C),
            SliceToArray(bytes, offset, BillboardSize, "shrub billboard bytes"));
    }

    private static List<ShrubNormal> ReadNormals(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, NormalCount * NormalSize, "shrub normals");
        var normals = new List<ShrubNormal>(NormalCount);
        for (var i = 0; i < NormalCount; i++)
        {
            var normalOffset = offset + (i * NormalSize);
            normals.Add(new ShrubNormal(
                ReadInt16LittleEndian(bytes, normalOffset),
                ReadInt16LittleEndian(bytes, normalOffset + 0x02),
                ReadInt16LittleEndian(bytes, normalOffset + 0x04),
                ReadInt16LittleEndian(bytes, normalOffset + 0x06)));
        }

        return normals;
    }

    private sealed record ShrubGifTag(int GsPacketOffset, ShrubGeometryType GeometryType);

    private sealed record ShrubVertexPart1(short X, short Y, short Z, short GsPacketOffset);

    private sealed record ShrubVertexPart2(short S, short T, short H, short NormalAndStopCondition);
}

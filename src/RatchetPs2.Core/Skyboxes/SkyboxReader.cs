namespace RatchetPs2.Core.Skyboxes;

public static class SkyboxReader
{
    private const int HeaderSize = 0x20;
    private const int ShellOffsetTableOffset = 0x20;
    private const int MaxShellCount = 8;
    private const int TextureDefinitionSize = 0x10;
    private const int PaletteSize = 256 * 4;
    private const int ShellHeaderSize = 0x10;
    private const int ClusterHeaderSize = 0x20;
    private const int VertexSize = 0x8;
    private const int TexCoordSize = 0x4;
    private const int TriangleSize = 0x4;
    private const int SpriteSize = 0x20;

    public static Skybox Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(input));
        }

        if (!input.CanSeek)
        {
            using var copy = new MemoryStream();
            input.CopyTo(copy);
            copy.Position = 0;
            return Read(copy);
        }

        var baseOffset = input.Position;
        using var reader = new BinaryReader(input, System.Text.Encoding.UTF8, leaveOpen: true);
        var header = ReadHeader(reader);
        var availableLength = input.Length - baseOffset;

        ValidateCount(header.ShellCount, MaxShellCount, nameof(header.ShellCount));
        ValidateCount(header.TextureCount, short.MaxValue, nameof(header.TextureCount));
        ValidateCount(header.SpriteCount, short.MaxValue, nameof(header.SpriteCount));
        ValidateCount(header.FxCount, short.MaxValue, nameof(header.FxCount));

        var textures = ReadTextures(reader, baseOffset, availableLength, header);
        var fxList = ReadFxList(reader, baseOffset, availableLength, header);
        var sprites = ReadSprites(reader, baseOffset, availableLength, header);
        var shells = ReadShells(reader, baseOffset, availableLength, header);

        return new Skybox(header, shells, textures, sprites, fxList, availableLength);
    }

    private static SkyboxHeader ReadHeader(BinaryReader reader)
    {
        EnsureCanRead(reader, 0, HeaderSize, "skybox header");

        var color = new SkyboxColor(
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte(),
            reader.ReadByte());

        return new SkyboxHeader(
            color,
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadInt32(),
            reader.ReadUInt32());
    }

    private static List<SkyboxTexture> ReadTextures(
        BinaryReader reader,
        long baseOffset,
        long availableLength,
        SkyboxHeader header)
    {
        var textures = new List<SkyboxTexture>(header.TextureCount);
        EnsureRange(
            header.TextureDefOffset,
            checked(header.TextureCount * TextureDefinitionSize),
            availableLength,
            "skybox texture definitions");

        for (var i = 0; i < header.TextureCount; i++)
        {
            reader.BaseStream.Position = checked(baseOffset + header.TextureDefOffset + (i * TextureDefinitionSize));
            var paletteOffset = reader.ReadUInt32();
            var textureOffset = reader.ReadUInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException($"Skybox texture {i} has invalid dimensions {width}x{height}.");
            }

            var pixelLength = checked(width * height);
            EnsureRange(
                checked(header.TextureDataOffset + paletteOffset),
                PaletteSize,
                availableLength,
                $"skybox texture {i} palette");
            EnsureRange(
                checked(header.TextureDataOffset + textureOffset),
                pixelLength,
                availableLength,
                $"skybox texture {i} pixels");

            reader.BaseStream.Position = checked(baseOffset + header.TextureDataOffset + paletteOffset);
            var paletteData = reader.ReadBytes(PaletteSize);
            reader.BaseStream.Position = checked(baseOffset + header.TextureDataOffset + textureOffset);
            var pixelData = reader.ReadBytes(pixelLength);

            textures.Add(new SkyboxTexture(
                i,
                paletteOffset,
                textureOffset,
                width,
                height,
                paletteData,
                pixelData));
        }

        return textures;
    }

    private static byte[]? ReadFxList(
        BinaryReader reader,
        long baseOffset,
        long availableLength,
        SkyboxHeader header)
    {
        if (header.FxCount == 0)
        {
            return null;
        }

        if (header.FxListOffset < 0)
        {
            throw new InvalidDataException($"Skybox FX list offset 0x{header.FxListOffset:X8} is invalid.");
        }

        EnsureRange(header.FxListOffset, header.FxCount, availableLength, "skybox FX list");
        reader.BaseStream.Position = checked(baseOffset + header.FxListOffset);
        return reader.ReadBytes(header.FxCount);
    }

    private static List<SkyboxSprite> ReadSprites(
        BinaryReader reader,
        long baseOffset,
        long availableLength,
        SkyboxHeader header)
    {
        var sprites = new List<SkyboxSprite>(header.SpriteCount);
        if (header.SpriteCount == 0)
        {
            return sprites;
        }

        EnsureRange(
            header.SpritesOffset,
            checked(header.SpriteCount * SpriteSize),
            availableLength,
            "skybox sprites");

        for (var i = 0; i < header.SpriteCount; i++)
        {
            reader.BaseStream.Position = checked(baseOffset + header.SpritesOffset + (i * SpriteSize));
            sprites.Add(new SkyboxSprite(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadInt32(),
                new System.Numerics.Vector4(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle())));
        }

        return sprites;
    }

    private static List<SkyboxShell> ReadShells(
        BinaryReader reader,
        long baseOffset,
        long availableLength,
        SkyboxHeader header)
    {
        EnsureRange(
            ShellOffsetTableOffset,
            checked(header.ShellCount * sizeof(uint)),
            availableLength,
            "skybox shell offset table");

        var shells = new List<SkyboxShell>(header.ShellCount);
        for (var i = 0; i < header.ShellCount; i++)
        {
            reader.BaseStream.Position = checked(baseOffset + ShellOffsetTableOffset + (i * sizeof(uint)));
            var shellOffset = reader.ReadUInt32();
            EnsureRange(shellOffset, ShellHeaderSize, availableLength, $"skybox shell {i} header");

            reader.BaseStream.Position = checked(baseOffset + shellOffset);
            var clusterCount = reader.ReadInt16();
            ValidateCount(clusterCount, short.MaxValue, $"shell {i} cluster count");
            var flags = reader.ReadInt16();
            var rotationX = reader.ReadInt16();
            var rotationY = reader.ReadInt16();
            var rotationZ = reader.ReadInt16();
            var rotationDeltaX = reader.ReadInt16();
            var rotationDeltaY = reader.ReadInt16();
            var rotationDeltaZ = reader.ReadInt16();
            var clusters = ReadClusters(reader, baseOffset, availableLength, i, shellOffset, clusterCount);

            shells.Add(new SkyboxShell(
                i,
                shellOffset,
                clusterCount,
                flags,
                rotationX,
                rotationY,
                rotationZ,
                rotationDeltaX,
                rotationDeltaY,
                rotationDeltaZ,
                clusters));
        }

        return shells;
    }

    private static List<SkyboxCluster> ReadClusters(
        BinaryReader reader,
        long baseOffset,
        long availableLength,
        int shellIndex,
        uint shellOffset,
        short clusterCount)
    {
        EnsureRange(
            checked(shellOffset + ShellHeaderSize),
            checked(clusterCount * ClusterHeaderSize),
            availableLength,
            $"skybox shell {shellIndex} cluster headers");

        var clusters = new List<SkyboxCluster>(clusterCount);
        for (var i = 0; i < clusterCount; i++)
        {
            reader.BaseStream.Position = checked(baseOffset + shellOffset + ShellHeaderSize + (i * ClusterHeaderSize));
            var sphere = new SkyboxSphere(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle());
            var dataOffset = reader.ReadUInt32();
            var vertexCount = reader.ReadInt16();
            var triangleCount = reader.ReadInt16();
            var vertexOffset = reader.ReadInt16();
            var texCoordOffset = reader.ReadInt16();
            var triangleOffset = reader.ReadInt16();
            var dataSize = reader.ReadInt16();

            ValidateCount(vertexCount, byte.MaxValue + 1, $"shell {shellIndex} cluster {i} vertex count");
            ValidateCount(triangleCount, short.MaxValue, $"shell {shellIndex} cluster {i} triangle count");
            ValidateCount(dataSize, short.MaxValue, $"shell {shellIndex} cluster {i} data size");
            ValidateDataOffset(vertexOffset, dataSize, checked(vertexCount * VertexSize), shellIndex, i, "vertex");
            ValidateDataOffset(texCoordOffset, dataSize, checked(vertexCount * TexCoordSize), shellIndex, i, "texture coordinate");
            ValidateDataOffset(triangleOffset, dataSize, checked(triangleCount * TriangleSize), shellIndex, i, "triangle");
            EnsureRange(dataOffset, dataSize, availableLength, $"skybox shell {shellIndex} cluster {i} data");

            reader.BaseStream.Position = checked(baseOffset + dataOffset);
            var data = reader.ReadBytes(dataSize);
            var vertices = ReadVertices(data, vertexOffset, vertexCount);
            var texCoords = ReadTexCoords(data, texCoordOffset, vertexCount);
            var triangles = ReadTriangles(data, triangleOffset, triangleCount, vertexCount, shellIndex, i);

            clusters.Add(new SkyboxCluster(
                i,
                sphere,
                dataOffset,
                vertexCount,
                triangleCount,
                vertexOffset,
                texCoordOffset,
                triangleOffset,
                dataSize,
                data,
                vertices,
                texCoords,
                triangles));
        }

        return clusters;
    }

    private static List<SkyboxVertex> ReadVertices(byte[] data, short vertexOffset, short vertexCount)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = vertexOffset;

        var vertices = new List<SkyboxVertex>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            vertices.Add(new SkyboxVertex(
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16()));
        }

        return vertices;
    }

    private static List<SkyboxTexCoord> ReadTexCoords(byte[] data, short texCoordOffset, short vertexCount)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = texCoordOffset;

        var texCoords = new List<SkyboxTexCoord>(vertexCount);
        for (var i = 0; i < vertexCount; i++)
        {
            texCoords.Add(new SkyboxTexCoord(reader.ReadInt16(), reader.ReadInt16()));
        }

        return texCoords;
    }

    private static List<SkyboxTriangle> ReadTriangles(
        byte[] data,
        short triangleOffset,
        short triangleCount,
        short vertexCount,
        int shellIndex,
        int clusterIndex)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = triangleOffset;

        var triangles = new List<SkyboxTriangle>(triangleCount);
        for (var i = 0; i < triangleCount; i++)
        {
            var triangle = new SkyboxTriangle(
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadByte());
            if (triangle.A >= vertexCount || triangle.B >= vertexCount || triangle.C >= vertexCount)
            {
                throw new InvalidDataException(
                    $"Skybox shell {shellIndex} cluster {clusterIndex} triangle {i} references vertex " +
                    $"{triangle.A},{triangle.B},{triangle.C} outside vertex count {vertexCount}.");
            }

            triangles.Add(triangle);
        }

        return triangles;
    }

    private static void ValidateDataOffset(
        short offset,
        short dataSize,
        int byteLength,
        int shellIndex,
        int clusterIndex,
        string name)
    {
        if (offset < 0)
        {
            throw new InvalidDataException(
                $"Skybox shell {shellIndex} cluster {clusterIndex} has invalid {name} offset {offset}.");
        }

        if (offset + byteLength > dataSize)
        {
            throw new InvalidDataException(
                $"Skybox shell {shellIndex} cluster {clusterIndex} {name} data overruns cluster payload.");
        }
    }

    private static void ValidateCount(int count, int max, string name)
    {
        if (count < 0 || count > max)
        {
            throw new InvalidDataException($"Skybox {name} {count} is invalid.");
        }
    }

    private static void EnsureCanRead(BinaryReader reader, long relativeOffset, long byteLength, string name)
    {
        EnsureRange(relativeOffset, byteLength, reader.BaseStream.Length - reader.BaseStream.Position, name);
    }

    private static void EnsureRange(long relativeOffset, long byteLength, long availableLength, string name)
    {
        if (relativeOffset < 0 || byteLength < 0 || relativeOffset + byteLength > availableLength)
        {
            throw new InvalidDataException(
                $"Skybox {name} range 0x{relativeOffset:X}..0x{relativeOffset + byteLength:X} is outside the input length 0x{availableLength:X}.");
        }
    }
}

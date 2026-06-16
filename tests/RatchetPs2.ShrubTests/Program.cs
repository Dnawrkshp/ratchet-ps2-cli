using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Shrubs;
using System.Numerics;
using System.Text.Json;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var fixtures = new[]
{
    new PackedShrubFixture(
        Game: "UYA",
        Path: Path.Combine(repoRoot, "test-assets", "shrubs", "1091", "core.bin"),
        OClass: 0x0443,
        Meshes: 2,
        Primitives: 13,
        Vertices: 412,
        Triangles: 290,
        TextureUris: new Dictionary<int, string>
        {
            [0] = "textures/1091-0.png",
            [1] = "textures/1091-1.png"
        }),
    new PackedShrubFixture(
        Game: "DL",
        Path: Path.Combine(repoRoot, "test-assets", "shrubs", "1146", "core.bin"),
        OClass: 0x047A,
        Meshes: 1,
        Primitives: 9,
        Vertices: 288,
        Triangles: 216,
        TextureUris: new Dictionary<int, string>
        {
            [0] = "textures/1146-0.png",
            [1] = "textures/1146-1.png"
        })
};

if (fixtures.Any(fixture => !File.Exists(fixture.Path)))
{
    Console.WriteLine("No local shrub core.bin fixtures found under test-assets/shrubs; skipping shrub tests.");
    return;
}

foreach (var fixture in fixtures)
{
    using var input = File.OpenRead(fixture.Path);
    var shrub = ShrubClassReader.Read(input);
    Expect((ushort)shrub.Header.OClass == fixture.OClass, $"{fixture.Game} expected OClass 0x{fixture.OClass:X4}, got 0x{(ushort)shrub.Header.OClass:X4}");

    var export = ShrubGltfExporter.Export(
        shrub,
        "shrub.gltf",
        new ShrubGltfExportOptions
        {
            GameLabel = fixture.Game,
            ExternalTextureUris = fixture.TextureUris
        });
    using var gltfInput = new MemoryStream(export.GltfBytes);
    var info = GltfModelInspector.Inspect(gltfInput);

    Expect(info.MeshCount == fixture.Meshes, $"{fixture.Game} expected {fixture.Meshes} mesh(es), got {info.MeshCount}");
    Expect(info.PrimitiveCount == fixture.Primitives, $"{fixture.Game} expected {fixture.Primitives} primitive(s), got {info.PrimitiveCount}");
    Expect(info.VertexCount == fixture.Vertices, $"{fixture.Game} expected {fixture.Vertices} vertices, got {info.VertexCount}");
    Expect(info.TriangleCount == fixture.Triangles, $"{fixture.Game} expected {fixture.Triangles} triangles, got {info.TriangleCount}");
    Expect(info.TextureCount == fixture.TextureUris.Count, $"{fixture.Game} expected {fixture.TextureUris.Count} texture(s), got {info.TextureCount}");
    Expect(info.ImageUris.SequenceEqual(fixture.TextureUris.Values), $"{fixture.Game} exported image URIs should match supplied packed-shrub textures");
    Expect(export.BinBytes.Length > 0, $"{fixture.Game} packed shrub export should produce an external buffer");
    Expect(export.DiagnosticsBytes.Length > 0, $"{fixture.Game} packed shrub export should produce diagnostics");
    ValidateShrubMaterialMetadata(export.GltfBytes, fixture.Game);
    ValidateShrubTriangleWinding(export.GltfBytes, export.BinBytes, fixture.Game);
}

ValidateShrubGltfCoordinateBasis();

Console.WriteLine("Shrub packed core.bin export tests passed.");

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "src", "RatchetPs2.Core", "RatchetPs2.Core.csproj")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root.");
}

static void ValidateShrubGltfCoordinateBasis()
{
    var shrub = new ShrubClass
    {
        Header = new ShrubClassHeader
        {
            BoundingSphere = Vector4.Zero,
            Scale = 1f,
            PacketCount = 1
        },
        ByteLength = 0,
        Normals =
        [
            new ShrubNormal(0, 0, short.MaxValue, 0)
        ],
        Packets =
        [
            new ShrubPacket
            {
                PacketIndex = 0,
                Entry = new ShrubPacketEntry(0, 0),
                Header = new ShrubPacketHeader(0, 0, 3, 0),
                Primitives =
                [
                    new ShrubVertexPrimitive(
                        0,
                        ShrubGeometryType.TriangleList,
                        [
                            new ShrubVertex(0, 0, 0, 0, 0, 0, 0),
                            new ShrubVertex(2, 0, 0, 0, 0, 0, 0),
                            new ShrubVertex(0, 2, 0, 0, 0, 0, 0)
                        ])
                ]
            }
        ]
    };

    var export = ShrubGltfExporter.Export(
        shrub,
        "basis-test.gltf",
        new ShrubGltfExportOptions
        {
            GameLabel = "DL",
            PositionScale = 1f
        });

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    var coordinateBasis = root
        .GetProperty("extras")
        .GetProperty("CoordinateBasis")
        .GetString();
    Expect(
        coordinateBasis == GltfCoordinateBasis.Ps2XzyBasisDescription,
        $"expected shrub coordinate basis {GltfCoordinateBasis.Ps2XzyBasisDescription}, got {coordinateBasis}");

    var position0 = ReadVector3(export.BinBytes, 0);
    var position1 = ReadVector3(export.BinBytes, 12);
    var position2 = ReadVector3(export.BinBytes, 24);
    var normal0 = ReadVector3(export.BinBytes, 36);
    Expect(VectorNearlyEqual(position0, Vector3.Zero), $"expected first shrub position at origin, got {position0}");
    Expect(VectorNearlyEqual(position1, new Vector3(2f, 0f, 0f)), $"expected source +X to remain glTF +X, got {position1}");
    Expect(VectorNearlyEqual(position2, new Vector3(0f, 0f, -2f)), $"expected source +Y to become glTF -Z, got {position2}");
    Expect(VectorNearlyEqual(normal0, Vector3.UnitY), $"expected source +Z normal to become glTF +Y, got {normal0}");
}

static void ValidateShrubMaterialMetadata(byte[] gltfBytes, string game)
{
    using var gltfDocument = JsonDocument.Parse(gltfBytes);
    var root = gltfDocument.RootElement;
    var checkedMaterialCount = 0;

    foreach (var material in root.GetProperty("materials").EnumerateArray())
    {
        if (!material.TryGetProperty("extras", out var extras)
            || !extras.TryGetProperty("ShrubTextureId", out _))
        {
            continue;
        }

        Expect(extras.TryGetProperty("ShrubTextureHasAlpha", out _), $"{game} shrub material should expose prefixed alpha presence");
        Expect(extras.TryGetProperty("ShrubTextureAlphaMode", out _), $"{game} shrub material should expose prefixed alpha mode");
        Expect(extras.TryGetProperty("ShrubTextureAlphaUsage", out var alphaUsage), $"{game} shrub material should expose prefixed alpha usage");
        Expect(extras.TryGetProperty("ShrubTextureFullOpacityAlpha", out var fullOpacityAlpha), $"{game} shrub material should expose prefixed full-opacity alpha");
        Expect(
            alphaUsage.GetString() is "Opaque" or "Opacity",
            $"{game} shrub material alpha usage should be Opaque or Opacity");
        Expect(
            fullOpacityAlpha.GetInt32() == ShrubTextureAlpha.FullOpacityAlpha,
            $"{game} shrub material full-opacity alpha should use shared PS2 alpha scale");
        checkedMaterialCount++;
    }

    Expect(checkedMaterialCount > 0, $"{game} shrub export should contain shrub material alpha metadata");
}

static void ValidateShrubTriangleWinding(byte[] gltfBytes, byte[] binBytes, string game)
{
    using var gltfDocument = JsonDocument.Parse(gltfBytes);
    var root = gltfDocument.RootElement;
    var accessors = root.GetProperty("accessors");
    var bufferViews = root.GetProperty("bufferViews");
    var checkedTriangleCount = 0;
    var opposedTriangleCount = 0;
    var windingMetadataCount = 0;

    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            if (primitive.TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("WindingCorrectedTriangleCount", out var correctedCount))
            {
                Expect(correctedCount.GetInt32() >= 0, $"{game} shrub primitive should record corrected winding count");
                windingMetadataCount++;
            }

            var attributes = primitive.GetProperty("attributes");
            var positions = ReadAccessorVector3(
                accessors,
                bufferViews,
                binBytes,
                attributes.GetProperty("POSITION").GetInt32(),
                game,
                "POSITION");
            var normals = ReadAccessorVector3(
                accessors,
                bufferViews,
                binBytes,
                attributes.GetProperty("NORMAL").GetInt32(),
                game,
                "NORMAL");
            var indices = ReadIndexAccessor(
                accessors,
                bufferViews,
                binBytes,
                primitive.GetProperty("indices").GetInt32(),
                game);

            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var a = checked((int)indices[i + 0]);
                var b = checked((int)indices[i + 1]);
                var c = checked((int)indices[i + 2]);
                Expect(
                    (uint)a < (uint)positions.Length
                    && (uint)b < (uint)positions.Length
                    && (uint)c < (uint)positions.Length,
                    $"{game} shrub triangle index should be inside POSITION accessor");

                var faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
                var averageNormal = normals[a] + normals[b] + normals[c];
                if (faceNormal.LengthSquared() <= 0.00000001f
                    || averageNormal.LengthSquared() <= 0.00000001f)
                {
                    continue;
                }

                checkedTriangleCount++;
                var dot = Vector3.Dot(Vector3.Normalize(faceNormal), Vector3.Normalize(averageNormal));
                if (dot < -0.0001f)
                {
                    opposedTriangleCount++;
                }
            }
        }
    }

    Expect(checkedTriangleCount > 0, $"{game} shrub winding validation should inspect triangles");
    Expect(opposedTriangleCount == 0, $"{game} shrub export should not contain triangles wound opposite their normals");
    Expect(windingMetadataCount > 0, $"{game} shrub primitives should contain winding diagnostics");
}

static Vector3[] ReadAccessorVector3(
    JsonElement accessors,
    JsonElement bufferViews,
    byte[] binBytes,
    int accessorIndex,
    string game,
    string attributeName)
{
    var accessor = accessors[accessorIndex];
    var componentType = accessor.GetProperty("componentType").GetInt32();
    var type = accessor.GetProperty("type").GetString();
    Expect(componentType == 5126 && type == "VEC3", $"{game} shrub {attributeName} should be a float VEC3 accessor");

    var count = accessor.GetProperty("count").GetInt32();
    var (offset, stride) = AccessorLayout(accessor, bufferViews, componentSize: 4, componentCount: 3);
    var values = new Vector3[count];
    for (var i = 0; i < count; i++)
    {
        var elementOffset = offset + (i * stride);
        values[i] = new Vector3(
            BitConverter.ToSingle(binBytes, elementOffset + 0),
            BitConverter.ToSingle(binBytes, elementOffset + 4),
            BitConverter.ToSingle(binBytes, elementOffset + 8));
    }

    return values;
}

static uint[] ReadIndexAccessor(
    JsonElement accessors,
    JsonElement bufferViews,
    byte[] binBytes,
    int accessorIndex,
    string game)
{
    var accessor = accessors[accessorIndex];
    var componentType = accessor.GetProperty("componentType").GetInt32();
    var componentSize = componentType switch
    {
        5125 => 4,
        5123 => 2,
        5121 => 1,
        _ => throw new InvalidOperationException($"{game} shrub index accessor has unsupported component type {componentType}")
    };
    Expect(accessor.GetProperty("type").GetString() == "SCALAR", $"{game} shrub index accessor should be scalar");

    var count = accessor.GetProperty("count").GetInt32();
    var (offset, stride) = AccessorLayout(accessor, bufferViews, componentSize, componentCount: 1);
    var values = new uint[count];
    for (var i = 0; i < count; i++)
    {
        var elementOffset = offset + (i * stride);
        values[i] = componentType switch
        {
            5125 => BitConverter.ToUInt32(binBytes, elementOffset),
            5123 => BitConverter.ToUInt16(binBytes, elementOffset),
            5121 => binBytes[elementOffset],
            _ => 0
        };
    }

    return values;
}

static (int Offset, int Stride) AccessorLayout(
    JsonElement accessor,
    JsonElement bufferViews,
    int componentSize,
    int componentCount)
{
    var bufferView = bufferViews[accessor.GetProperty("bufferView").GetInt32()];
    var offset = GetOptionalInt(bufferView, "byteOffset") + GetOptionalInt(accessor, "byteOffset");
    var stride = GetOptionalInt(bufferView, "byteStride", componentSize * componentCount);
    return (offset, stride);
}

static int GetOptionalInt(JsonElement element, string propertyName, int fallback = 0)
{
    return element.TryGetProperty(propertyName, out var property)
        ? property.GetInt32()
        : fallback;
}

static Vector3 ReadVector3(byte[] bytes, int offset)
{
    return new Vector3(
        BitConverter.ToSingle(bytes, offset),
        BitConverter.ToSingle(bytes, offset + sizeof(float)),
        BitConverter.ToSingle(bytes, offset + sizeof(float) * 2));
}

static bool VectorNearlyEqual(Vector3 left, Vector3 right)
{
    return Vector3.DistanceSquared(left, right) < 0.000001f;
}

internal sealed record PackedShrubFixture(
    string Game,
    string Path,
    int OClass,
    int Meshes,
    int Primitives,
    int Vertices,
    int Triangles,
    IReadOnlyDictionary<int, string> TextureUris);

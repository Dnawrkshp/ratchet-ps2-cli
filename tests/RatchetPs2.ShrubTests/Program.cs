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
        Meshes: 1,
        Primitives: 12,
        Vertices: 408,
        Triangles: 288,
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

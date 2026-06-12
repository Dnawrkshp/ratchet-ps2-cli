using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Tfrags;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var fixtures = new[]
{
    new TfragFixture(
        Game: "DL",
        Path: Path.Combine(repoRoot, "test-assets", "tfrags", "DL", "level1", "terrain", "terrain.bin"),
        TfragCount: 414,
        FirstChunkDataOffset: 0x67C0,
        FirstChunkTextureIds: [0, 15, 4, 37, 28]),
    new TfragFixture(
        Game: "UYA",
        Path: Path.Combine(repoRoot, "test-assets", "tfrags", "UYA", "level3", "terrain", "terrain.bin"),
        TfragCount: 308,
        FirstChunkDataOffset: 0x4D40,
        FirstChunkTextureIds: [0, 6])
};

if (fixtures.Any(fixture => !File.Exists(fixture.Path)))
{
    Console.WriteLine("No local tfrag terrain fixtures found under test-assets/tfrags; skipping tfrag tests.");
    return;
}

foreach (var fixture in fixtures)
{
    using var input = File.OpenRead(fixture.Path);
    var terrain = TfragTerrainReader.Read(input);
    Expect(terrain.TfragTableOffset == 0x40, $"{fixture.Game} expected tfrag table offset 0x40, got 0x{terrain.TfragTableOffset:X}");
    Expect(terrain.TfragCount == fixture.TfragCount, $"{fixture.Game} expected {fixture.TfragCount} tfrags, got {terrain.TfragCount}");
    Expect(terrain.Chunks.Count == fixture.TfragCount, $"{fixture.Game} chunk list should match header tfrag count");

    var firstChunk = terrain.Chunks[0];
    Expect(firstChunk.DataOffset == fixture.FirstChunkDataOffset, $"{fixture.Game} first chunk data offset expected 0x{fixture.FirstChunkDataOffset:X}, got 0x{firstChunk.DataOffset:X}");
    Expect(firstChunk.TextureEntries.Select(entry => entry.TextureId).SequenceEqual(fixture.FirstChunkTextureIds),
        $"{fixture.Game} first chunk texture ids should match reference table");

    var export = TfragGltfExporter.Export(
        terrain,
        "terrain.gltf",
        new TfragGltfExportOptions
        {
            GameLabel = fixture.Game
        });

    using var gltfInput = new MemoryStream(export.GltfBytes);
    var info = GltfModelInspector.Inspect(gltfInput);
    Expect(info.MeshCount > 0, $"{fixture.Game} tfrag export should produce meshes");
    Expect(info.PrimitiveCount > 0, $"{fixture.Game} tfrag export should produce primitives");
    Expect(info.VertexCount > 0, $"{fixture.Game} tfrag export should produce vertices");
    Expect(info.TriangleCount > 0, $"{fixture.Game} tfrag export should produce triangles");
    Expect(export.BinBytes.Length > 0, $"{fixture.Game} tfrag export should produce an external buffer");
    Expect(export.DiagnosticsBytes.Length > 0, $"{fixture.Game} tfrag export should produce diagnostics");
    ValidateLodGroups(export.GltfBytes, fixture.Game);
}

Console.WriteLine("Tfrag terrain export tests passed.");

static void ValidateLodGroups(byte[] gltfBytes, string game)
{
    using var document = JsonDocument.Parse(gltfBytes);
    var root = document.RootElement;
    var extras = root.GetProperty("extras");
    Expect(
        extras.GetProperty("LodSemantics").GetString()?.Contains("shared_ofs", StringComparison.Ordinal) == true,
        $"{game} tfrag glTF should record runtime LOD semantics");

    var nodes = root.GetProperty("nodes");
    for (var lodIndex = 0; lodIndex <= 2; lodIndex++)
    {
        var lodNode = nodes.EnumerateArray().FirstOrDefault(node =>
            node.TryGetProperty("name", out var nameElement)
            && nameElement.GetString() == $"lod_{lodIndex}");
        Expect(lodNode.ValueKind == JsonValueKind.Object, $"{game} expected lod_{lodIndex} node");
        Expect(
            lodNode.TryGetProperty("children", out var children)
            && children.ValueKind == JsonValueKind.Array
            && children.GetArrayLength() > 0,
            $"{game} expected lod_{lodIndex} to contain chunk nodes");
    }
}

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

internal sealed record TfragFixture(
    string Game,
    string Path,
    int TfragCount,
    int FirstChunkDataOffset,
    IReadOnlyList<int> FirstChunkTextureIds);

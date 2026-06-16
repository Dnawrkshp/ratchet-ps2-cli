using System.Numerics;
using System.Text.Json;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Ties;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
ValidateSharedPs2ColorRules();
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
    ValidateNormals(export.GltfBytes, fixture.Game);
    ValidateLightSelectors(export.GltfBytes, fixture.Game);
    ValidateTriangleWinding(export.GltfBytes, export.BinBytes, fixture.Game);
}

Console.WriteLine("Tfrag terrain export tests passed.");

static void ValidateSharedPs2ColorRules()
{
    Expect(TfragTextureAlpha.FullOpacityAlpha == Ps2Color.FullOpacityAlpha, "tfrag full-opacity alpha should use the shared PS2 alpha scale");
    Expect(ShrubTextureAlpha.FullOpacityAlpha == Ps2Color.FullOpacityAlpha, "shrub full-opacity alpha should use the shared PS2 alpha scale");
    Expect(TieGameProfile.Default.FullOpacityAlpha == Ps2Color.FullOpacityAlpha, "tie full-opacity alpha should use the shared PS2 alpha scale");
    Expect(Math.Abs(Ps2Color.NormalizeOpacityAlpha(0x80) - 1f) < 0.000001f, "PS2 alpha 0x80 should normalize to full opacity");
    Expect(Math.Abs(Ps2Color.NormalizeIntensityComponent(0x80) - 1f) < 0.000001f, "PS2 intensity 0x80 should normalize to full intensity");
}

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

static void ValidateNormals(byte[] gltfBytes, string game)
{
    using var document = JsonDocument.Parse(gltfBytes);
    var root = document.RootElement;
    var accessors = root.GetProperty("accessors");
    var primitiveCount = 0;
    var normalMetadataCount = 0;

    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            primitiveCount++;
            var attributes = primitive.GetProperty("attributes");
            Expect(attributes.TryGetProperty("POSITION", out var positionAttribute), $"{game} tfrag primitive should have POSITION");
            Expect(attributes.TryGetProperty("NORMAL", out var normalAttribute), $"{game} tfrag primitive should have NORMAL");

            var positionCount = accessors[positionAttribute.GetInt32()].GetProperty("count").GetInt32();
            var normalCount = accessors[normalAttribute.GetInt32()].GetProperty("count").GetInt32();
            Expect(normalCount == positionCount, $"{game} tfrag normal accessor count should match POSITION count");

            Expect(primitive.TryGetProperty("extras", out var extras), $"{game} tfrag primitive should have extras");
            Expect(extras.TryGetProperty("Normals", out var normalExtras), $"{game} tfrag primitive should record normal diagnostics");
            Expect(
                normalExtras.GetProperty("DuplicatePositionNormalWeldMode").GetString()?.Length > 0,
                $"{game} tfrag normal diagnostics should record duplicate-position mode");
            Expect(
                normalExtras.GetProperty("RestoredFaceNormalIndexCount").GetInt32() >= 0,
                $"{game} tfrag normal diagnostics should record restored face-normal count");
            Expect(
                normalExtras.GetProperty("WindingCorrectedTriangleCount").GetInt32() >= 0,
                $"{game} tfrag normal diagnostics should record corrected winding count");
            normalMetadataCount++;
        }
    }

    Expect(primitiveCount > 0, $"{game} tfrag export should contain primitives");
    Expect(normalMetadataCount == primitiveCount, $"{game} tfrag normal diagnostics should cover every primitive");
}

static void ValidateLightSelectors(byte[] gltfBytes, string game)
{
    using var document = JsonDocument.Parse(gltfBytes);
    var root = document.RootElement;
    var accessors = root.GetProperty("accessors");
    var primitiveCount = 0;

    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            primitiveCount++;
            var attributes = primitive.GetProperty("attributes");
            Expect(
                attributes.TryGetProperty(TfragGltfExporter.LightSelectorAttributeName, out var lightSelectorAttribute),
                $"{game} tfrag primitive should have {TfragGltfExporter.LightSelectorAttributeName}");
            Expect(
                attributes.TryGetProperty(TfragGltfExporter.LightBaseColorAttributeName, out var lightBaseColorAttribute),
                $"{game} tfrag primitive should have {TfragGltfExporter.LightBaseColorAttributeName}");
            Expect(
                attributes.TryGetProperty(TfragGltfExporter.LightNormalAttributeName, out var lightNormalAttribute),
                $"{game} tfrag primitive should have {TfragGltfExporter.LightNormalAttributeName}");
            Expect(
                attributes.TryGetProperty(TfragGltfExporter.LightPostScaleAttributeName, out var lightPostScaleAttribute),
                $"{game} tfrag primitive should have {TfragGltfExporter.LightPostScaleAttributeName}");

            var positionCount = accessors[attributes.GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();
            var lightSelectorAccessor = accessors[lightSelectorAttribute.GetInt32()];
            Expect(
                lightSelectorAccessor.GetProperty("componentType").GetInt32() == 5126
                && lightSelectorAccessor.GetProperty("type").GetString() == "SCALAR",
                $"{game} tfrag light selector accessor should be a float scalar");
            Expect(
                lightSelectorAccessor.GetProperty("count").GetInt32() == positionCount,
                $"{game} tfrag light selector accessor count should match POSITION count");

            var lightBaseColorAccessor = accessors[lightBaseColorAttribute.GetInt32()];
            Expect(
                lightBaseColorAccessor.GetProperty("componentType").GetInt32() == 5121
                && lightBaseColorAccessor.GetProperty("type").GetString() == "VEC4"
                && lightBaseColorAccessor.TryGetProperty("normalized", out var normalized)
                && normalized.GetBoolean(),
                $"{game} tfrag light base color accessor should be a normalized byte VEC4");
            Expect(
                lightBaseColorAccessor.GetProperty("count").GetInt32() == positionCount,
                $"{game} tfrag light base color accessor count should match POSITION count");

            var lightNormalAccessor = accessors[lightNormalAttribute.GetInt32()];
            Expect(
                lightNormalAccessor.GetProperty("componentType").GetInt32() == 5126
                && lightNormalAccessor.GetProperty("type").GetString() == "VEC3",
                $"{game} tfrag light normal accessor should be a float VEC3");
            Expect(
                lightNormalAccessor.GetProperty("count").GetInt32() == positionCount,
                $"{game} tfrag light normal accessor count should match POSITION count");

            var lightPostScaleAccessor = accessors[lightPostScaleAttribute.GetInt32()];
            Expect(
                lightPostScaleAccessor.GetProperty("componentType").GetInt32() == 5126
                && lightPostScaleAccessor.GetProperty("type").GetString() == "SCALAR",
                $"{game} tfrag light post scale accessor should be a float scalar");
            Expect(
                lightPostScaleAccessor.GetProperty("count").GetInt32() == positionCount,
                $"{game} tfrag light post scale accessor count should match POSITION count");
        }
    }

    Expect(primitiveCount > 0, $"{game} tfrag light selector validation should inspect primitives");
}

static void ValidateTriangleWinding(byte[] gltfBytes, byte[] binBytes, string game)
{
    using var document = JsonDocument.Parse(gltfBytes);
    var root = document.RootElement;
    var accessors = root.GetProperty("accessors");
    var bufferViews = root.GetProperty("bufferViews");
    var checkedTriangleCount = 0;
    var opposedTriangleCount = 0;

    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            var attributes = primitive.GetProperty("attributes");
            var positions = ReadVector3Accessor(
                accessors,
                bufferViews,
                binBytes,
                attributes.GetProperty("POSITION").GetInt32(),
                game,
                "POSITION");
            var normals = ReadVector3Accessor(
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
                    $"{game} tfrag triangle index should be inside POSITION accessor");

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

    Expect(checkedTriangleCount > 0, $"{game} tfrag winding validation should inspect triangles");
    Expect(opposedTriangleCount == 0, $"{game} tfrag export should not contain triangles wound opposite their normals");
}

static Vector3[] ReadVector3Accessor(
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
    Expect(componentType == 5126 && type == "VEC3", $"{game} tfrag {attributeName} should be a float VEC3 accessor");

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
        _ => throw new InvalidOperationException($"{game} tfrag index accessor has unsupported component type {componentType}")
    };
    Expect(accessor.GetProperty("type").GetString() == "SCALAR", $"{game} tfrag index accessor should be scalar");

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

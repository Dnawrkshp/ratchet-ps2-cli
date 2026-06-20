using RatchetPs2.Core.Games;
using RatchetPs2.Core.Skyboxes;
using RatchetPs2.Core.Textures;
using System.Text.Json;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var skyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "DL", "level6", "sky.bin");
var level1SkyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "DL", "level1", "sky.bin");
var level7SkyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "DL", "level7", "sky.bin");
var level53SkyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "DL", "level53", "sky.bin");
var uyaLevel4SkyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "UYA", "level4", "sky.bin");
var uyaLevel41SkyboxPath = Path.Combine(repoRoot, "test-assets", "skyboxes", "UYA", "level41", "sky.bin");
if (!File.Exists(skyboxPath))
{
    Console.WriteLine("No local DL sky.bin fixture found under test-assets/skyboxes/DL/level6; skipping skybox tests.");
    return 0;
}

var failures = new List<string>();
using (var input = File.OpenRead(skyboxPath))
{
    var skybox = SkyboxReader.Read(input);
    Expect(skybox.Header.Color == new SkyboxColor(0, 0, 0, 0), "expected level6 skybox header color RGBA 0,0,0,0");
    Expect(skybox.Header.ShellCount == 6, $"expected 6 shells, got {skybox.Header.ShellCount}");
    Expect(skybox.Header.TextureCount == 4, $"expected 4 textures, got {skybox.Header.TextureCount}");
    Expect(skybox.Header.TextureDefOffset == 0x40, $"expected texture def offset 0x40, got 0x{skybox.Header.TextureDefOffset:X}");
    Expect(skybox.Header.TextureDataOffset == 0xA0, $"expected texture data offset 0xA0, got 0x{skybox.Header.TextureDataOffset:X}");
    Expect(skybox.Shells.Count == 6, $"expected 6 decoded shells, got {skybox.Shells.Count}");
    Expect(skybox.Shells.Sum(shell => shell.Clusters.Count) == 116, "expected 116 decoded skybox clusters");
    Expect(skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Vertices.Count) == 2302, "expected 2302 decoded source vertices");
    Expect(skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Triangles.Count) == 2270, "expected 2270 decoded triangles");
    Expect(skybox.Textures[0].Width == 256 && skybox.Textures[0].Height == 256, "expected texture 0 dimensions 256x256");
    Expect(skybox.Textures[1].Width == 128 && skybox.Textures[1].Height == 128, "expected texture 1 dimensions 128x128");
    Expect(skybox.Textures[2].Width == 512 && skybox.Textures[2].Height == 256, "expected texture 2 dimensions 512x256");
    Expect(skybox.Textures[3].Width == 512 && skybox.Textures[3].Height == 64, "expected texture 3 dimensions 512x64");
    Expect(skybox.Shells[0].Clusters[0].Triangles[0].TextureId == 0xFF, "expected first cluster to use untextured 0xFF triangles");

    var export = SkyboxGltfExporter.Export(
        skybox,
        "sky.gltf",
        new SkyboxGltfExportOptions
        {
            BufferFileName = "sky.buffer.bin",
            GameLabel = "DL",
            TextureConversionOptions = new TextureConversionOptions
            {
                IsSwizzled = true
            }
        });
    Expect(export.GltfBytes.Length > 0, "expected skybox glTF export to write glTF JSON bytes");
    Expect(export.BinBytes.Length > 0, "expected skybox glTF export to write buffer bytes");
    Expect(export.DiagnosticsBytes.Length > 0, "expected skybox glTF export to write diagnostics bytes");
    Expect(export.Textures.Count == 4, $"expected 4 exported texture PNG resources, got {export.Textures.Count}");
    Expect(export.Textures.All(texture => texture.PngBytes.Length > 0), "expected all exported texture PNG resources to have data");

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    Expect(root.GetProperty("asset").GetProperty("generator").GetString() == "RatchetPs2 DL skybox glTF exporter", "expected DL skybox glTF generator metadata");
    Expect(root.GetProperty("buffers")[0].GetProperty("uri").GetString() == "sky.buffer.bin", "expected skybox glTF buffer URI");
    Expect(root.GetProperty("images").GetArrayLength() == 4, "expected 4 skybox glTF images");
    Expect(root.GetProperty("textures").GetArrayLength() == 4, "expected 4 skybox glTF textures");
    var sampler = root.GetProperty("samplers")[0];
    Expect(sampler.GetProperty("wrapS").GetInt32() == 33071, "expected skybox glTF sampler U wrapping to clamp to edge");
    Expect(sampler.GetProperty("wrapT").GetInt32() == 33071, "expected skybox glTF sampler V wrapping to clamp to edge");
    var materials = root.GetProperty("materials");
    Expect(materials.GetArrayLength() == 5, "expected 5 skybox glTF materials including untextured 0xFF");
    var untexturedMaterial = FindMaterial(materials, "sky_untextured_preview");
    Expect(untexturedMaterial.GetProperty("alphaMode").GetString() == "BLEND", "expected untextured sky material to preserve vertex alpha as BLEND");
    Expect(untexturedMaterial.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[3].GetSingle() == 1f, "expected untextured sky material alpha to allow gouraud vertex colors");
    Expect(untexturedMaterial.GetProperty("extras").GetProperty("SkyboxUsesUntexturedGouraudColor").GetBoolean(), "expected untextured sky material to report gouraud vertex colors");
    Expect(FindMaterial(materials, "sky_tex_0000").GetProperty("extras").GetProperty("SkyboxTextureAlphaMode").GetString() == "Blend", "expected star texture material to carry Blend texture alpha metadata");
    Expect(FindMaterial(materials, "sky_tex_0001").GetProperty("extras").GetProperty("SkyboxMaterialAlphaMode").GetString() == "Blend", "expected dome texture material to carry Blend vertex alpha metadata");
    Expect(FindMaterial(materials, "sky_tex_0002").GetProperty("extras").GetProperty("SkyboxMaterialAlphaMode").GetString() == "Blend", "expected cylinder texture material to carry Blend vertex alpha metadata");
    Expect(FindMaterial(materials, "sky_tex_0003").GetProperty("extras").GetProperty("SkyboxTextureAlphaMode").GetString() == "Blend", "expected mountain texture material to carry Blend texture alpha metadata");
    var primitives = GetSkyboxPrimitives(root);
    var nodes = root.GetProperty("nodes");
    var meshes = root.GetProperty("meshes");
    Expect(nodes.GetArrayLength() == 7, $"expected parent node plus 6 shell nodes, got {nodes.GetArrayLength()}");
    Expect(nodes[0].GetProperty("children").GetArrayLength() == 6, "expected skybox parent node to contain 6 shell child nodes");
    Expect(nodes[0].GetProperty("name").GetString() == "skybox", "expected skybox parent node to use game-agnostic naming");
    Expect(nodes[1].GetProperty("name").GetString() == "skybox_shell_00", "expected first shell node to be named for shell 0");
    Expect(nodes[1].GetProperty("mesh").GetInt32() == 0, "expected first shell node to reference first shell mesh");
    Expect(meshes.GetArrayLength() == 6, $"expected 6 shell meshes, got {meshes.GetArrayLength()}");
    Expect(meshes[0].GetProperty("name").GetString() == "skybox_shell_00", "expected first shell mesh to be named for shell 0");
    Expect(primitives.Count == 6, $"expected 6 skybox primitive groups, got {primitives.Count}");
    Expect(primitives[0].GetProperty("extras").GetProperty("SkyboxShellIndex").GetInt32() == 0, "expected first visual primitive to preserve shell 0 background draw order");
    Expect(primitives[1].GetProperty("extras").GetProperty("SkyboxShellIndex").GetInt32() == 1, "expected first textured sky primitive to preserve source shell order");
    Expect(primitives[1].GetProperty("extras").GetProperty("SkyboxTextureName").GetString() == "tex.0000", "expected first textured sky primitive to preserve the star texture source order");
    Expect(primitives[5].GetProperty("extras").GetProperty("SkyboxTextureName").GetString() == "tex.0003", "expected final visual primitive to preserve mountain texture draw order");
    Expect(root.GetProperty("nodes")[0].GetProperty("extras").GetProperty("RuntimeRotatingShellCount").GetInt32() == 2, "expected level6 node metadata to report two rotating sky shells");
    var rotatingPrimitiveExtras = primitives[2].GetProperty("extras");
    var rotationDeltaRaw = rotatingPrimitiveExtras.GetProperty("SkyboxShellRotationDeltaRaw");
    Expect(rotationDeltaRaw[0].GetInt32() == 0 && rotationDeltaRaw[1].GetInt32() == 0 && rotationDeltaRaw[2].GetInt32() == 1, "expected level6 shell 2 to preserve raw Z rotation delta");
    var angularVelocity = rotatingPrimitiveExtras.GetProperty("SkyboxShellAngularVelocityRadiansPerSecond");
    var expectedAngularVelocity = 60f * MathF.PI / 32768f;
    Expect(MathF.Abs(angularVelocity[0].GetSingle()) < 0.000001f, "expected level6 shell 2 glTF X angular velocity to be zero");
    Expect(MathF.Abs(angularVelocity[1].GetSingle() - expectedAngularVelocity) < 0.000001f, "expected level6 shell 2 source Z delta to map to glTF Y angular velocity");
    Expect(MathF.Abs(angularVelocity[2].GetSingle()) < 0.000001f, "expected level6 shell 2 glTF Z angular velocity to be zero");
    var positionAccessorIndex = primitives[0].GetProperty("attributes").GetProperty("POSITION").GetInt32();
    Expect(primitives[0].GetProperty("attributes").TryGetProperty("COLOR_0", out _), "expected skybox glTF primitives to include COLOR_0 vertex alpha");
    ExpectGouraudColorData(root, export.BinBytes, primitives[0], "level6");
    var positionAccessor = root.GetProperty("accessors")[positionAccessorIndex];
    Expect(positionAccessor.GetProperty("count").GetInt32() > 0, "expected shell-local position accessor to contain vertices");

    using var diagnosticsDocument = JsonDocument.Parse(export.DiagnosticsBytes);
    var diagnostics = diagnosticsDocument.RootElement;
    Expect(diagnostics.GetProperty("ShellCount").GetInt32() == 6, "expected diagnostics shell count 6");
    Expect(diagnostics.GetProperty("ClusterCount").GetInt32() == 116, "expected diagnostics cluster count 116");
    Expect(diagnostics.GetProperty("TriangleCount").GetInt32() == 2270, "expected diagnostics triangle count 2270");
    Expect(diagnostics.GetProperty("PrimitiveCount").GetInt32() == 6, "expected diagnostics primitive count 6");
    Expect(diagnostics.GetProperty("ColorCount").GetInt32() == 6810, "expected diagnostics color count 6810");
    Expect(diagnostics.GetProperty("Shells")[2].GetProperty("Rotation").GetProperty("SkyboxShellHasRuntimeRotation").GetBoolean(), "expected diagnostics to flag level6 shell 2 runtime rotation");
}

if (File.Exists(level1SkyboxPath))
{
    using var input = File.OpenRead(level1SkyboxPath);
    var skybox = SkyboxReader.Read(input);
    Expect(skybox.Header.Color == new SkyboxColor(0, 0, 0, 0), "expected level1 skybox header color RGBA 0,0,0,0");
    Expect(skybox.Header.ShellCount == 4, $"expected level1 to have 4 shells, got {skybox.Header.ShellCount}");
    Expect(skybox.Header.TextureCount == 5, $"expected level1 to have 5 textures, got {skybox.Header.TextureCount}");
    Expect(skybox.Header.FxCount == 2, $"expected level1 to have 2 FX entries, got {skybox.Header.FxCount}");
    Expect(skybox.Header.SpriteMax == 1024, $"expected level1 sprite allocation 1024, got {skybox.Header.SpriteMax}");
    Expect(skybox.FxList?.Length == 2, "expected level1 FX list bytes to be read");
    Expect(skybox.Shells.Sum(shell => shell.Clusters.Count) == 78, "expected level1 decoded cluster count 78");
    Expect(skybox.Shells.SelectMany(shell => shell.Clusters).Sum(cluster => cluster.Triangles.Count) == 1912, "expected level1 decoded triangle count 1912");
    Expect(skybox.Shells[1].Clusters.SelectMany(cluster => cluster.TexCoords).Any(texCoord => texCoord.S == 4096 || texCoord.T == 4096), "expected level1 textured shell to preserve fixed-12 one-edge ST values");

    var export = SkyboxGltfExporter.Export(
        skybox,
        "sky.gltf",
        new SkyboxGltfExportOptions
        {
            BufferFileName = "sky.buffer.bin",
            GameLabel = "DL",
            TextureConversionOptions = new TextureConversionOptions
            {
                IsSwizzled = true
            }
        });
    using var diagnosticsDocument = JsonDocument.Parse(export.DiagnosticsBytes);
    var diagnostics = diagnosticsDocument.RootElement;
    Expect(diagnostics.GetProperty("PrimitiveCount").GetInt32() == 4, "expected level1 diagnostics primitive count 4");
    Expect(diagnostics.GetProperty("ColorCount").GetInt32() == 5736, "expected level1 diagnostics color count 5736");
    Expect(!diagnostics.GetProperty("Textures")[2].GetProperty("UsesBinaryAlpha").GetBoolean(), "expected level1 sky texture export to keep non-binary low-alpha nebula texels");
    Expect(diagnostics.GetProperty("Textures")[2].GetProperty("MaxAlpha").GetInt32() <= 128, "expected level1 sky texture export to preserve PS2 full-opacity alpha");

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    var primitives = GetSkyboxPrimitives(root);
    ExpectGouraudColorData(root, export.BinBytes, primitives[0], "level1");
    var untexturedMaterial = FindMaterial(root.GetProperty("materials"), "sky_untextured_preview");
    Expect(untexturedMaterial.GetProperty("extras").GetProperty("SkyboxUsesUntexturedGouraudColor").GetBoolean(), "expected level1 untextured material to report gouraud vertex colors");
}

if (File.Exists(level7SkyboxPath))
{
    using var input = File.OpenRead(level7SkyboxPath);
    var skybox = SkyboxReader.Read(input);
    Expect(skybox.Header.ShellCount == 5, $"expected level7 to have 5 shells, got {skybox.Header.ShellCount}");
    Expect(skybox.Shells.Skip(1).All(shell => shell.RotationX == 0 && shell.RotationY == 0 && shell.RotationZ == 0), "expected level7 source file rotations to be zero before runtime loader patches");

    var profile = SkyboxGameProfile.ForGame(GameId.DL);
    var export = SkyboxGltfExporter.Export(
        skybox,
        "sky.gltf",
        profile.CreateExportOptions("sky.buffer.bin", levelNumber: 7, shellCount: skybox.Shells.Count));

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    Expect(root.GetProperty("nodes")[0].GetProperty("extras").GetProperty("RuntimeRotationPatchCount").GetInt32() == 4, "expected level7 node metadata to report four runtime rotation patches");
    var primitives = GetSkyboxPrimitives(root);
    var shell1Primitive = FindPrimitiveByShell(primitives, 1);
    var shell1Extras = shell1Primitive.GetProperty("extras");
    var fileRotation = shell1Extras.GetProperty("SkyboxShellFileRotationRaw");
    Expect(fileRotation[0].GetInt32() == 0 && fileRotation[1].GetInt32() == 0 && fileRotation[2].GetInt32() == 0, "expected level7 shell 1 metadata to preserve zero file rotation");
    var runtimeRotation = shell1Extras.GetProperty("SkyboxShellRotationRaw");
    Expect(runtimeRotation[0].GetInt32() == 0x2AAA && runtimeRotation[1].GetInt32() == 0x1D4C && runtimeRotation[2].GetInt32() == 0, "expected level7 shell 1 metadata to apply DL runtime rotation patch");
    Expect(shell1Extras.GetProperty("SkyboxShellRotationPatchApplied").GetBoolean(), "expected level7 shell 1 metadata to mark the runtime rotation patch");
    var tickRadians = shell1Extras.GetProperty("SkyboxRotationTickRadians").GetSingle();
    Expect(MathF.Abs(tickRadians - (MathF.PI / 32768f)) < 0.00000001f, "expected skybox rotation tick to match DL UpdateSkyShellMatrix");
    var runtimeRadians = shell1Extras.GetProperty("SkyboxShellRotationRadians");
    Expect(MathF.Abs(runtimeRadians[0].GetSingle() - (0x2AAA * tickRadians)) < 0.000001f, "expected level7 shell 1 patched source X rotation to map to glTF X radians");
    Expect(MathF.Abs(runtimeRadians[1].GetSingle()) < 0.000001f, "expected level7 shell 1 patched source Z rotation to leave glTF Y radians zero");
    Expect(MathF.Abs(runtimeRadians[2].GetSingle() + (0x1D4C * tickRadians)) < 0.000001f, "expected level7 shell 1 patched source Y rotation to map to negative glTF Z radians");
    var runtimeVelocity = shell1Extras.GetProperty("SkyboxShellAngularVelocityRadiansPerSecond");
    Expect(MathF.Abs(runtimeVelocity[1].GetSingle() - (13 * tickRadians * 60f)) < 0.000001f, "expected level7 shell 1 source Z delta to map to glTF Y angular velocity at game tick scale");
}

if (File.Exists(level53SkyboxPath))
{
    using var input = File.OpenRead(level53SkyboxPath);
    var skybox = SkyboxReader.Read(input);
    var hasNegativeTexturedSt = skybox.Shells
        .Where(shell => (shell.Flags & 1) == 0)
        .SelectMany(shell => shell.Clusters)
        .SelectMany(cluster => cluster.TexCoords)
        .Any(texCoord => texCoord.S < 0 || texCoord.T < 0);
    Expect(hasNegativeTexturedSt, "expected level53 fixture to preserve signed textured ST values");
    Expect(new SkyboxTexCoord(-224, 4096).ToGltfTexCoord().X < 0f, "expected signed negative ST to export as negative fixed-12 UV");
}

if (File.Exists(uyaLevel4SkyboxPath))
{
    using var input = File.OpenRead(uyaLevel4SkyboxPath);
    var skybox = SkyboxReader.Read(input);
    Expect(skybox.Header.Color == new SkyboxColor(0, 0, 0, 0), "expected UYA level4 skybox header color RGBA 0,0,0,0");
    Expect(skybox.Header.ShellCount == 6, $"expected UYA level4 to have 6 shells, got {skybox.Header.ShellCount}");
    Expect(skybox.Header.TextureCount == 6, $"expected UYA level4 to have 6 textures, got {skybox.Header.TextureCount}");
    Expect(skybox.Shells.Sum(shell => shell.Clusters.Count) == 118, "expected UYA level4 decoded cluster count 118");

    var export = SkyboxGltfExporter.Export(
        skybox,
        "sky.gltf",
        new SkyboxGltfExportOptions
        {
            BufferFileName = "sky.buffer.bin",
            GameLabel = "UYA",
            TextureConversionOptions = new TextureConversionOptions()
        });
    Expect(export.Textures.Count == 6, $"expected UYA level4 to export 6 texture PNG resources, got {export.Textures.Count}");

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    Expect(root.GetProperty("asset").GetProperty("generator").GetString() == "RatchetPs2 UYA skybox glTF exporter", "expected UYA skybox glTF generator metadata");
    Expect(root.GetProperty("nodes")[0].GetProperty("extras").GetProperty("Game").GetString() == "UYA", "expected UYA skybox node metadata");
    var sampler = root.GetProperty("samplers")[0];
    Expect(sampler.GetProperty("wrapS").GetInt32() == 33071, "expected UYA skybox glTF sampler U wrapping to clamp to edge");
    Expect(sampler.GetProperty("wrapT").GetInt32() == 33071, "expected UYA skybox glTF sampler V wrapping to clamp to edge");
    var primitives = GetSkyboxPrimitives(root);
    Expect(primitives[3].GetProperty("extras").GetProperty("SkyboxDrawBlendMode").GetString() == "Bloom", "expected UYA level4 shell flag 0x2 primitive to export bloom blend metadata");
    var bloomMaterial = FindMaterial(root.GetProperty("materials"), "sky_tex_0002_bloom");
    Expect(bloomMaterial.GetProperty("extras").GetProperty("SkyboxUsesBloomEmission").GetBoolean(), "expected UYA level4 bloom material to report bloom emission");
    Expect(bloomMaterial.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[0].GetSingle() == 0f, "expected UYA level4 bloom material to use black base color for emission preview");
    Expect(bloomMaterial.TryGetProperty("emissiveTexture", out _), "expected UYA level4 bloom material to reuse the sky texture as an emissive texture");
    Expect(bloomMaterial.TryGetProperty("emissiveFactor", out _), "expected UYA level4 bloom material to include emissiveFactor");
    Expect(bloomMaterial.TryGetProperty("extensions", out var bloomExtensions)
        && bloomExtensions.TryGetProperty("KHR_materials_emissive_strength", out var emissiveStrengthExtension)
        && emissiveStrengthExtension.GetProperty("emissiveStrength").GetSingle() > 0f,
        "expected UYA level4 bloom material to include KHR_materials_emissive_strength");
    Expect(root.GetProperty("extensionsUsed").EnumerateArray().Any(extension => extension.GetString() == "KHR_materials_emissive_strength"),
        "expected UYA level4 glTF to declare KHR_materials_emissive_strength when bloom emission is used");
}

if (File.Exists(uyaLevel41SkyboxPath))
{
    using var input = File.OpenRead(uyaLevel41SkyboxPath);
    var skybox = SkyboxReader.Read(input);
    var export = SkyboxGltfExporter.Export(
        skybox,
        "sky.gltf",
        new SkyboxGltfExportOptions
        {
            BufferFileName = "sky.buffer.bin",
            GameLabel = "UYA",
            TextureConversionOptions = new TextureConversionOptions()
        });

    using var gltfDocument = JsonDocument.Parse(export.GltfBytes);
    var root = gltfDocument.RootElement;
    var primitives = GetSkyboxPrimitives(root);
    Expect(primitives[1].GetProperty("extras").GetProperty("SkyboxSourceDrawOrder").GetInt32() == 1, "expected UYA level41 star primitive to keep source draw order 1");
    Expect(primitives[1].GetProperty("extras").GetProperty("SkyboxTextureName").GetString() == "tex.0000", "expected UYA level41 star primitive to draw before foreground planet shells");
    Expect(primitives[2].GetProperty("extras").GetProperty("SkyboxShellIndex").GetInt32() == 2, "expected UYA level41 planet shell to draw after the star shell");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} skybox test assertion(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("Skybox reader/export tests passed.");
return 0;

void Expect(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

static string FindRepoRoot(string start)
{
    var directory = new DirectoryInfo(start);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ratchet-ps2-cli.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate ratchet-ps2-cli.sln from the test output directory.");
}

static JsonElement FindMaterial(JsonElement materials, string name)
{
    foreach (var material in materials.EnumerateArray())
    {
        if (material.GetProperty("name").GetString() == name)
        {
            return material;
        }
    }

    throw new InvalidOperationException($"Could not find glTF material '{name}'.");
}

static List<JsonElement> GetSkyboxPrimitives(JsonElement root)
{
    var primitives = new List<JsonElement>();
    foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
    {
        foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
        {
            primitives.Add(primitive.Clone());
        }
    }

    return primitives
        .OrderBy(primitive => primitive.GetProperty("extras").GetProperty("SkyboxDrawOrder").GetInt32())
        .ToList();
}

static JsonElement FindPrimitiveByShell(IEnumerable<JsonElement> primitives, int shellIndex)
{
    foreach (var primitive in primitives)
    {
        if (primitive.GetProperty("extras").GetProperty("SkyboxShellIndex").GetInt32() == shellIndex)
        {
            return primitive;
        }
    }

    throw new InvalidOperationException($"Could not find glTF primitive for shell {shellIndex}.");
}

void ExpectGouraudColorData(JsonElement root, byte[] binBytes, JsonElement primitive, string label)
{
    var colorAccessorIndex = primitive.GetProperty("attributes").GetProperty("COLOR_0").GetInt32();
    var colorAccessor = root.GetProperty("accessors")[colorAccessorIndex];
    var colorBufferView = root.GetProperty("bufferViews")[colorAccessor.GetProperty("bufferView").GetInt32()];
    var colorOffset = colorBufferView.GetProperty("byteOffset").GetInt32() + colorAccessor.GetProperty("byteOffset").GetInt32();
    var indexAccessor = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
    var indexBufferView = root.GetProperty("bufferViews")[indexAccessor.GetProperty("bufferView").GetInt32()];
    var indexOffset = indexBufferView.GetProperty("byteOffset").GetInt32() + indexAccessor.GetProperty("byteOffset").GetInt32();
    var count = indexAccessor.GetProperty("count").GetInt32();
    var minRed = float.MaxValue;
    var maxBlue = float.MinValue;
    var maxDisplayBlue = float.MinValue;
    var hasBlueDominant = false;

    for (var i = 0; i < count; i++)
    {
        var vertexIndex = checked((int)BitConverter.ToUInt32(binBytes, indexOffset + (i * 4)));
        var vertexColorOffset = colorOffset + (vertexIndex * 16);
        var r = BitConverter.ToSingle(binBytes, vertexColorOffset);
        var g = BitConverter.ToSingle(binBytes, vertexColorOffset + 4);
        var b = BitConverter.ToSingle(binBytes, vertexColorOffset + 8);
        minRed = Math.Min(minRed, r);
        maxBlue = Math.Max(maxBlue, b);
        maxDisplayBlue = Math.Max(maxDisplayBlue, LinearToSrgb(b));
        hasBlueDominant |= b > r && b > g;
    }

    Expect(minRed < 0.1f, $"{label}: expected gouraud sky colors to include dark vertices");
    Expect(maxDisplayBlue > 0.35f, $"{label}: expected gouraud sky colors to include blue vertices after display conversion");
    Expect(maxBlue < maxDisplayBlue, $"{label}: expected gouraud sky COLOR_0 values to be stored as glTF linear colors");
    Expect(hasBlueDominant, $"{label}: expected gouraud sky colors to contain blue-dominant vertices");
}

static float LinearToSrgb(float channel)
{
    return channel <= 0.0031308f
        ? channel * 12.92f
        : (1.055f * MathF.Pow(channel, 1f / 2.4f)) - 0.055f;
}

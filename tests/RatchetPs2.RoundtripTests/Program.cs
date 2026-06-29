using System.Text.Json;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures.Png;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var cases = new[]
    {
        (Name: "UYA", Format: MobyAnimationFormat.Standard, Directory: Path.Combine(repoRoot, "test-assets", "UYA Mobys")),
        (Name: "DL", Format: MobyAnimationFormat.Compact, Directory: Path.Combine(repoRoot, "test-assets", "DL Mobys"))
    }
    .SelectMany(testCase => Directory.Exists(testCase.Directory)
        ? Directory.EnumerateFiles(testCase.Directory, "moby.bin", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new RoundtripCase(testCase.Name, testCase.Format, path))
        : [])
    .ToList();

if (cases.Count == 0)
{
    Console.WriteLine("No local reference moby.bin files found under test-assets/UYA Mobys or test-assets/DL Mobys; skipping moby roundtrip tests.");
    return 0;
}

var tempRoot = Path.Combine(Path.GetTempPath(), $"ratchet-ps2-moby-roundtrip-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempRoot);

try
{
    var failures = new List<string>();
    try
    {
        ValidateLod0Export(cases);
        Console.WriteLine("PASS moby LOD0 export filter");
    }
    catch (Exception ex)
    {
        failures.Add($"moby LOD0 export filter: {ex.Message}");
        Console.WriteLine("FAIL moby LOD0 export filter");
    }

    try
    {
        ValidateMaterialAlphaExport(cases);
        Console.WriteLine("PASS moby material alpha export");
    }
    catch (Exception ex)
    {
        failures.Add($"moby material alpha export: {ex.Message}");
        Console.WriteLine("FAIL moby material alpha export");
    }

    foreach (var testCase in cases)
    {
        var relativePath = Path.GetRelativePath(repoRoot, testCase.MobyPath);
        try
        {
            Roundtrip(testCase, tempRoot);
            Console.WriteLine($"PASS {testCase.Game} {relativePath}");
        }
        catch (Exception ex)
        {
            failures.Add($"{testCase.Game} {relativePath}: {ex.Message}");
            Console.WriteLine($"FAIL {testCase.Game} {relativePath}");
        }
    }

    if (failures.Count == 0)
    {
        Console.WriteLine($"All {cases.Count} moby glTF roundtrips were byte-identical.");
        return 0;
    }

    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} moby glTF roundtrip(s) failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"  {failure}");
    }

    return 1;
}
finally
{
    Directory.Delete(tempRoot, recursive: true);
}

static void ValidateLod0Export(IReadOnlyList<RoundtripCase> cases)
{
    foreach (var testCase in cases)
    {
        MobyModel model;
        using (var input = File.OpenRead(testCase.MobyPath))
        {
            model = MobyModelReader.Read(
                input,
                new MobyModelReadOptions
                {
                    AnimationFormat = testCase.Format,
                    SkipAnimationSequences = true
                });
        }

        if (model.LowLodMeshCount == 0 && model.MeshCountType2 == 0)
        {
            continue;
        }

        using var mobyInput = File.OpenRead(testCase.MobyPath);
        var export = MobyGltfExporter.Export(
            mobyInput,
            "moby-lod0.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = testCase.Format,
                SkipAnimationSequences = true,
                LodIndex = 0,
                BufferFileName = "moby-lod0.buffer.bin"
            });

        using var gltf = JsonDocument.Parse(export.GltfBytes);
        foreach (var node in gltf.RootElement.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString() ?? string.Empty;
            if (name.Contains("LowLod", StringComparison.Ordinal)
                || name.Contains("MeshType2", StringComparison.Ordinal)
                || name.Contains("low_lod", StringComparison.Ordinal)
                || name.Contains("mesh_type_2", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{Path.GetRelativePath(FindRepoRoot(AppContext.BaseDirectory), testCase.MobyPath)}: LOD0 export included filtered node '{name}'.");
            }
        }

        return;
    }
}

static void ValidateMaterialAlphaExport(IReadOnlyList<RoundtripCase> cases)
{
    foreach (var testCase in cases)
    {
        if (!TryExportFirstTexturedMaterial(testCase, new TextureAlphaInfo(0, 128, UsesBinaryAlpha: false), out var blendMaterial))
        {
            continue;
        }

        RequireAlphaMaterial(blendMaterial, "BLEND", expectCutoff: false);
        if (!TryExportFirstTexturedMaterial(testCase, new TextureAlphaInfo(0, 255, UsesBinaryAlpha: true), out var maskMaterial))
        {
            throw new InvalidDataException("mask alpha export did not produce a textured material.");
        }

        RequireAlphaMaterial(maskMaterial, "MASK", expectCutoff: true);
        return;
    }

    throw new InvalidDataException("No fixture produced a textured moby material.");
}

static bool TryExportFirstTexturedMaterial(RoundtripCase testCase, TextureAlphaInfo alpha, out JsonElement material)
{
    const int textureId = 7;
    var textureUris = new Dictionary<int, string> { [textureId] = $"textures/tex.{textureId:0000}.png" };
    var textureSizes = new Dictionary<int, TextureSize> { [textureId] = new(64, 32) };
    var textureAlpha = new Dictionary<int, TextureAlphaInfo> { [textureId] = alpha };
    var meshTextureOverrides = Enumerable.Range(0, 2048).ToDictionary(index => index, _ => textureId);

    using var input = File.OpenRead(testCase.MobyPath);
    var export = MobyGltfExporter.Export(
        input,
        "moby-alpha.gltf",
        new MobyGltfExportOptions
        {
            AnimationFormat = testCase.Format,
            SkipAnimationSequences = true,
            ExternalTextureUris = textureUris,
            ExternalTextureSizes = textureSizes,
            ExternalTextureAlpha = textureAlpha,
            MeshTextureOverrides = meshTextureOverrides,
            InferTextureIdsFromUvTiles = false,
            BufferFileName = "moby-alpha.buffer.bin"
        });

    using var gltf = JsonDocument.Parse(export.GltfBytes);
    if (!gltf.RootElement.TryGetProperty("materials", out var materials))
    {
        material = default;
        return false;
    }

    foreach (var candidate in materials.EnumerateArray())
    {
        if (candidate.TryGetProperty("pbrMetallicRoughness", out var pbr)
            && pbr.TryGetProperty("baseColorTexture", out _))
        {
            material = candidate.Clone();
            return true;
        }
    }

    material = default;
    return false;
}

static void RequireAlphaMaterial(JsonElement material, string expectedAlphaMode, bool expectCutoff)
{
    if (!material.TryGetProperty("alphaMode", out var alphaMode) || alphaMode.GetString() != expectedAlphaMode)
    {
        throw new InvalidDataException($"expected moby material alphaMode {expectedAlphaMode}.");
    }

    if (expectCutoff)
    {
        if (!material.TryGetProperty("alphaCutoff", out var cutoff) || Math.Abs(cutoff.GetSingle() - 0.5f) > 0.0001f)
        {
            throw new InvalidDataException("expected moby mask material alphaCutoff 0.5.");
        }
    }
    else if (material.TryGetProperty("alphaCutoff", out _))
    {
        throw new InvalidDataException("blend moby material should not emit alphaCutoff.");
    }

    var extras = material.GetProperty("extras");
    if (extras.GetProperty("AlphaMode").GetString() != (expectedAlphaMode == "MASK" ? TextureAlphaMode.Mask : TextureAlphaMode.Blend).ToString()
        || extras.GetProperty("TextureWidth").GetInt32() != 64
        || extras.GetProperty("TextureHeight").GetInt32() != 32)
    {
        throw new InvalidDataException("moby material extras did not include expected texture alpha/size metadata.");
    }
}

static void Roundtrip(RoundtripCase testCase, string tempRoot)
{
    var originalBytes = File.ReadAllBytes(testCase.MobyPath);
    var caseDirectory = Path.Combine(
        tempRoot,
        testCase.Game,
        Path.GetFileName(Path.GetDirectoryName(testCase.MobyPath)) ?? "moby");
    Directory.CreateDirectory(caseDirectory);

    var gltfPath = Path.Combine(caseDirectory, "moby.gltf");
    var bufferPath = Path.Combine(caseDirectory, "moby.buffer.bin");
    using (var input = File.OpenRead(testCase.MobyPath))
    {
        var export = MobyGltfExporter.Export(
            input,
            Path.GetFileName(gltfPath),
            new MobyGltfExportOptions
            {
                AnimationFormat = testCase.Format,
                BufferFileName = Path.GetFileName(bufferPath)
            });

        File.WriteAllBytes(gltfPath, export.GltfBytes);
        File.WriteAllBytes(bufferPath, export.BinBytes);
    }

    MobyGltfImportResult result;
    using (var template = File.OpenRead(testCase.MobyPath))
    using (var gltf = File.OpenRead(gltfPath))
    {
        result = MobyGltfImporter.ImportWithDiagnostics(
            template,
            gltf,
            bufferName => File.OpenRead(Path.Combine(caseDirectory, Uri.UnescapeDataString(bufferName))),
            new MobyGltfImportOptions
            {
                AnimationFormat = testCase.Format,
                PacketMode = MobyGltfImportPacketMode.Passthrough
            });
    }

    var roundtripBytes = MobyModelPacker.Build(result.Model);
    var mismatch = FindFirstMismatch(originalBytes, roundtripBytes);
    if (mismatch is null)
    {
        return;
    }

    var (offset, expected, actual) = mismatch.Value;
    throw new InvalidDataException(
        $"roundtrip bytes differ at 0x{offset:X}; expected {(expected.HasValue ? $"0x{expected.Value:X2}" : "EOF")}, actual {(actual.HasValue ? $"0x{actual.Value:X2}" : "EOF")}.");
}

static (int Offset, byte? Expected, byte? Actual)? FindFirstMismatch(byte[] expected, byte[] actual)
{
    var length = Math.Min(expected.Length, actual.Length);
    for (var i = 0; i < length; i++)
    {
        if (expected[i] != actual[i])
        {
            return (i, expected[i], actual[i]);
        }
    }

    return expected.Length == actual.Length
        ? null
        : (length, expected.Length > actual.Length ? expected[length] : null, actual.Length > expected.Length ? actual[length] : null);
}

static string FindRepoRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ratchet-ps2-cli.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find ratchet-ps2-cli.sln from the test output directory.");
}

internal sealed record RoundtripCase(string Game, MobyAnimationFormat Format, string MobyPath);

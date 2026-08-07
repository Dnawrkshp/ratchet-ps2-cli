using System.Text.Json;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Games.DL.Moby;

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
        ValidateUyaAnimationExport(cases);
        Console.WriteLine("PASS UYA standard animation export");
    }
    catch (Exception ex)
    {
        failures.Add($"UYA standard animation export: {ex.Message}");
        Console.WriteLine("FAIL UYA standard animation export");
    }

    try
    {
        ValidateDlAnimationExport(cases);
        Console.WriteLine("PASS DL compact animation export");
    }
    catch (Exception ex)
    {
        failures.Add($"DL compact animation export: {ex.Message}");
        Console.WriteLine("FAIL DL compact animation export");
    }

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

static void ValidateUyaAnimationExport(IReadOnlyList<RoundtripCase> cases)
{
    var testCase = cases.FirstOrDefault(test =>
        test.Game == "UYA"
        && test.MobyPath.Contains("04220_107C", StringComparison.OrdinalIgnoreCase));
    if (testCase is null)
    {
        throw new InvalidDataException("The 04220_107C standard animation fixture is missing.");
    }

    using var input = File.OpenRead(testCase.MobyPath);
    var export = ExportMobyGltf(input, "moby-uya-animation.gltf");
    using var gltf = JsonDocument.Parse(export.GltfBytes);
    var root = gltf.RootElement;
    var animations = root.GetProperty("animations");
    if (animations.GetArrayLength() != 1)
    {
        throw new InvalidDataException($"expected 1 animation, found {animations.GetArrayLength()}.");
    }

    var animation = animations[0];
    var timeAccessor = root.GetProperty("accessors")[animation.GetProperty("samplers")[0].GetProperty("input").GetInt32()];
    var duration = timeAccessor.GetProperty("max")[0].GetSingle();
    if (timeAccessor.GetProperty("count").GetInt32() != 52 || Math.Abs(duration - 1.7f) > 0.000001f)
    {
        throw new InvalidDataException($"animation duration was {duration}; expected 52 keys over 1.7 seconds.");
    }

    var bone3 = root.GetProperty("nodes")
        .EnumerateArray()
        .Single(node => node.TryGetProperty("name", out var name) && name.GetString() == "bone_0003");
    var bone3Translation = bone3.GetProperty("translation");
    if (Math.Abs(bone3Translation[0].GetSingle()) > 0.000001f
        || Math.Abs(bone3Translation[1].GetSingle() - 0.4160785f) > 0.000001f
        || Math.Abs(bone3Translation[2].GetSingle()) > 0.000001f)
    {
        throw new InvalidDataException("standard animation bind translations were refined away from the source common transforms.");
    }

    var bone11Node = root.GetProperty("nodes")
        .EnumerateArray()
        .Select((node, index) => (Node: node, Index: index))
        .Single(item => item.Node.TryGetProperty("name", out var name) && name.GetString() == "bone_0011")
        .Index;
    var rotationChannel = animation.GetProperty("channels")
        .EnumerateArray()
        .Single(channel =>
            channel.GetProperty("target").GetProperty("node").GetInt32() == bone11Node
            && channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
    var rotationSampler = animation.GetProperty("samplers")[rotationChannel.GetProperty("sampler").GetInt32()];
    var rotationAccessor = root.GetProperty("accessors")[rotationSampler.GetProperty("output").GetInt32()];
    var rotationView = root.GetProperty("bufferViews")[rotationAccessor.GetProperty("bufferView").GetInt32()];
    var rotationOffset = rotationView.GetProperty("byteOffset").GetInt32()
        + (rotationAccessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
    var actual = new[]
    {
        BitConverter.ToSingle(export.BinBytes, rotationOffset),
        BitConverter.ToSingle(export.BinBytes, rotationOffset + 4),
        BitConverter.ToSingle(export.BinBytes, rotationOffset + 8),
        BitConverter.ToSingle(export.BinBytes, rotationOffset + 12)
    };
    var expected = new[] { -0.04412901f, -0.04412901f, -0.70572847f, 0.70572847f };
    if (actual.Zip(expected).Any(pair => Math.Abs(pair.First - pair.Second) > 0.000001f))
    {
        throw new InvalidDataException($"bone 11 rotation was ({string.Join(", ", actual)}); expected the standard inverse rotation convention.");
    }

    var scaledBindCase = cases.Single(test =>
        test.Game == "UYA"
        && test.MobyPath.Contains("05916_171C", StringComparison.OrdinalIgnoreCase));
    using var scaledBindInput = File.OpenRead(scaledBindCase.MobyPath);
    var scaledBindExport = ExportMobyGltf(scaledBindInput, "moby-uya-scaled-bind.gltf");
    using var scaledBindGltf = JsonDocument.Parse(scaledBindExport.GltfBytes);
    var scaledBindRoot = scaledBindGltf.RootElement;
    var skin = scaledBindRoot.GetProperty("skins")[0];
    var inverseBindAccessor = scaledBindRoot.GetProperty("accessors")[skin.GetProperty("inverseBindMatrices").GetInt32()];
    var inverseBindView = scaledBindRoot.GetProperty("bufferViews")[inverseBindAccessor.GetProperty("bufferView").GetInt32()];
    var inverseBindOffset = inverseBindView.GetProperty("byteOffset").GetInt32()
        + (inverseBindAccessor.TryGetProperty("byteOffset", out var inverseBindAccessorOffset) ? inverseBindAccessorOffset.GetInt32() : 0)
        + 13 * 16 * sizeof(float);
    var m11 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset);
    var m12 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 4);
    var m13 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 8);
    var m21 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 16);
    var m22 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 20);
    var m23 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 24);
    var m31 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 32);
    var m32 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 36);
    var m33 = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 40);
    var determinant = m11 * (m22 * m33 - m23 * m32)
        - m12 * (m21 * m33 - m23 * m31)
        + m13 * (m21 * m32 - m22 * m31);
    var translationX = BitConverter.ToSingle(scaledBindExport.BinBytes, inverseBindOffset + 48);
    if (Math.Abs(determinant - 1.6666667f) > 0.00001f || Math.Abs(translationX + 0.9320556f) > 0.000001f)
    {
        throw new InvalidDataException("standard animation export discarded a non-rigid source inverse bind matrix.");
    }
}

static void ValidateDlAnimationExport(IReadOnlyList<RoundtripCase> cases)
{
    var testCase = cases.FirstOrDefault(test =>
        test.Game == "DL"
        && test.MobyPath.Contains("09500_251C", StringComparison.OrdinalIgnoreCase));
    if (testCase is null)
    {
        throw new InvalidDataException("The 09500_251C compact animation fixture is missing.");
    }

    MobyGltfExport export;
    using (var input = File.OpenRead(testCase.MobyPath))
    {
        export = ExportMobyGltf(
            input,
            "moby-animation.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                BufferFileName = "moby-animation.buffer.bin"
            });
    }

    using var gltf = JsonDocument.Parse(export.GltfBytes);
    var root = gltf.RootElement;
    var animations = root.GetProperty("animations");
    if (animations.GetArrayLength() != 98)
    {
        throw new InvalidDataException($"expected 98 animations, found {animations.GetArrayLength()}.");
    }

    var timedAnimation = animations[45];
    var timedSampler = timedAnimation.GetProperty("samplers")[0];
    var timedAccessor = root.GetProperty("accessors")[timedSampler.GetProperty("input").GetInt32()];
    var duration = timedAccessor.GetProperty("max")[0].GetSingle();
    if (Math.Abs(duration - 3f) > 0.000001f)
    {
        throw new InvalidDataException($"animation 45 duration was {duration}; expected 3 seconds.");
    }

    var bone4Node = root.GetProperty("nodes")
        .EnumerateArray()
        .Select((node, index) => (Node: node, Index: index))
        .Single(item => item.Node.TryGetProperty("name", out var name) && name.GetString() == "bone_0004")
        .Index;
    var animation = animations[0];
    var translationChannel = animation.GetProperty("channels")
        .EnumerateArray()
        .Single(channel =>
            channel.GetProperty("target").GetProperty("node").GetInt32() == bone4Node
            && channel.GetProperty("target").GetProperty("path").GetString() == "translation");
    var sampler = animation.GetProperty("samplers")[translationChannel.GetProperty("sampler").GetInt32()];
    var accessor = root.GetProperty("accessors")[sampler.GetProperty("output").GetInt32()];
    var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var byteOffset = bufferView.GetProperty("byteOffset").GetInt32()
        + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
    var actualX = BitConverter.ToSingle(export.BinBytes, byteOffset);
    var actualY = BitConverter.ToSingle(export.BinBytes, byteOffset + 4);
    var actualZ = BitConverter.ToSingle(export.BinBytes, byteOffset + 8);

    using var source = new BinaryReader(File.OpenRead(testCase.MobyPath));
    source.BaseStream.Seek(0x24, SeekOrigin.Begin);
    var modelScale = source.ReadSingle();
    var expectedX = -186f * modelScale / 1024f;
    var expectedY = -830f * modelScale / 1024f;
    if (Math.Abs(actualX - expectedX) > 0.000001f
        || Math.Abs(actualY - expectedY) > 0.000001f
        || Math.Abs(actualZ) > 0.000001f)
    {
        throw new InvalidDataException(
            $"animation 0 bone 4 translation was ({actualX}, {actualY}, {actualZ}); expected ({expectedX}, {expectedY}, 0).");
    }

    source.BaseStream.Seek(0x18, SeekOrigin.Begin);
    var commonTransformOffset = source.ReadInt32();
    source.BaseStream.Seek(commonTransformOffset + 13 * 0x10, SeekOrigin.Begin);
    var commonX = source.ReadSingle() * modelScale / 1024f;
    var commonY = source.ReadSingle() * modelScale / 1024f;
    var commonZ = source.ReadSingle() * modelScale / 1024f;
    var bone13 = root.GetProperty("nodes")
        .EnumerateArray()
        .Single(node => node.TryGetProperty("name", out var name) && name.GetString() == "bone_0013");
    var bone13Translation = bone13.GetProperty("translation");
    if (Math.Abs(bone13Translation[0].GetSingle() - commonX) > 0.000001f
        || Math.Abs(bone13Translation[1].GetSingle() - commonZ) > 0.000001f
        || Math.Abs(bone13Translation[2].GetSingle() + commonY) > 0.000001f)
    {
        throw new InvalidDataException("DL bind joint 13 did not retain its common transform.");
    }

    var bone15Node = root.GetProperty("nodes")
        .EnumerateArray()
        .Select((node, index) => (Node: node, Index: index))
        .Single(item => item.Node.TryGetProperty("name", out var name) && name.GetString() == "bone_0015")
        .Index;
    var walkRotationChannel = animations[5].GetProperty("channels")
        .EnumerateArray()
        .Single(channel =>
            channel.GetProperty("target").GetProperty("node").GetInt32() == bone15Node
            && channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
    var walkRotationSampler = animations[5].GetProperty("samplers")[walkRotationChannel.GetProperty("sampler").GetInt32()];
    var walkRotationAccessor = root.GetProperty("accessors")[walkRotationSampler.GetProperty("output").GetInt32()];
    var walkRotationBufferView = root.GetProperty("bufferViews")[walkRotationAccessor.GetProperty("bufferView").GetInt32()];
    var walkRotationOffset = walkRotationBufferView.GetProperty("byteOffset").GetInt32()
        + (walkRotationAccessor.TryGetProperty("byteOffset", out var walkAccessorOffset) ? walkAccessorOffset.GetInt32() : 0);
    var walkRotationX = BitConverter.ToSingle(export.BinBytes, walkRotationOffset);
    var walkRotationY = BitConverter.ToSingle(export.BinBytes, walkRotationOffset + 4);
    var walkRotationZ = BitConverter.ToSingle(export.BinBytes, walkRotationOffset + 8);
    var walkRotationW = BitConverter.ToSingle(export.BinBytes, walkRotationOffset + 12);
    if (walkRotationX > -0.4f || walkRotationY > -0.05f || walkRotationZ < 0.05f || walkRotationW < 0.8f)
    {
        throw new InvalidDataException(
            $"animation 5 bone 15 rotation was ({walkRotationX}, {walkRotationY}, {walkRotationZ}, {walkRotationW}); expected the DL inverse rotation convention.");
    }

    var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
    var parentFixture = Path.Combine(repoRoot, "test-assets", "extractions", "level01_iso_world01", "assets", "moby", "04231_1087", "moby.bin");
    using (var parentInput = File.OpenRead(parentFixture))
    {
        var parentExport = ExportMobyGltf(parentInput, options: new MobyGltfExportOptions { AnimationFormat = MobyAnimationFormat.Compact });
        using var parentGltf = JsonDocument.Parse(parentExport.GltfBytes);
        var nodes = parentGltf.RootElement.GetProperty("nodes");
        var bone1 = nodes.EnumerateArray().Select((node, index) => (Node: node, Index: index))
            .Single(item => item.Node.GetProperty("name").GetString() == "bone_0001");
        var bone4Index = nodes.EnumerateArray().Select((node, index) => (Node: node, Index: index))
            .Single(item => item.Node.GetProperty("name").GetString() == "bone_0004").Index;
        if (!bone1.Node.GetProperty("children").EnumerateArray().Any(child => child.GetInt32() == bone4Index))
        {
            throw new InvalidDataException("DL compact parent table did not attach bone 4 to bone 1.");
        }
    }

    var bindFixture = Path.Combine(repoRoot, "test-assets", "aug proto - dl missions", "level1", "mission_0", "24d3", "moby.bin");
    using (var bindInput = File.OpenRead(bindFixture))
    {
        var bindExport = ExportMobyGltf(bindInput, options: new MobyGltfExportOptions { AnimationFormat = MobyAnimationFormat.Compact });
        using var bindGltf = JsonDocument.Parse(bindExport.GltfBytes);
        var bindRoot = bindGltf.RootElement;
        var bindAccessor = bindRoot.GetProperty("accessors")[bindRoot.GetProperty("skins")[0].GetProperty("inverseBindMatrices").GetInt32()];
        var bindView = bindRoot.GetProperty("bufferViews")[bindAccessor.GetProperty("bufferView").GetInt32()];
        var bone3MatrixOffset = bindView.GetProperty("byteOffset").GetInt32() + 3 * 0x40;
        var actualInverseBindY = BitConverter.ToSingle(bindExport.BinBytes, bone3MatrixOffset + 13 * sizeof(float));

        using var bindSource = new BinaryReader(File.OpenRead(bindFixture));
        bindSource.BaseStream.Seek(0x14, SeekOrigin.Begin);
        var skeletonOffset = bindSource.ReadInt32();
        bindSource.BaseStream.Seek(0x24, SeekOrigin.Begin);
        var bindScale = bindSource.ReadSingle() / 1024f;
        bindSource.BaseStream.Seek(skeletonOffset + 3 * 0x30 + 0x2C, SeekOrigin.Begin);
        var expectedInverseBindY = bindSource.ReadSingle() * bindScale;
        if (Math.Abs(actualInverseBindY - expectedInverseBindY) > 0.000001f)
        {
            throw new InvalidDataException($"DL compact bone 3 inverse bind Y was {actualInverseBindY}; expected {expectedInverseBindY}.");
        }
    }

    var hierarchyFixture = Path.Combine(repoRoot, "test-assets", "aug proto - dl missions", "level1", "mission_9", "24f9", "moby.bin");
    using (var hierarchyInput = File.OpenRead(hierarchyFixture))
    {
        var hierarchyExport = ExportMobyGltf(hierarchyInput, options: new MobyGltfExportOptions { AnimationFormat = MobyAnimationFormat.Compact });
        using var hierarchyGltf = JsonDocument.Parse(hierarchyExport.GltfBytes);
        var bone35Translation = hierarchyGltf.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(node => node.TryGetProperty("name", out var name) && name.GetString() == "bone_0035")
            .GetProperty("translation");

        using var hierarchySource = new BinaryReader(File.OpenRead(hierarchyFixture));
        hierarchySource.BaseStream.Seek(0x18, SeekOrigin.Begin);
        var hierarchyCommonOffset = hierarchySource.ReadInt32();
        hierarchySource.BaseStream.Seek(0x24, SeekOrigin.Begin);
        var hierarchyScale = hierarchySource.ReadSingle() / 1024f;
        hierarchySource.BaseStream.Seek(hierarchyCommonOffset + 35 * 0x10, SeekOrigin.Begin);
        var hierarchyX = hierarchySource.ReadSingle() * hierarchyScale;
        var hierarchyY = hierarchySource.ReadSingle() * hierarchyScale;
        var hierarchyZ = hierarchySource.ReadSingle() * hierarchyScale;
        if (Math.Abs(bone35Translation[0].GetSingle() - hierarchyX) > 0.000001f
            || Math.Abs(bone35Translation[1].GetSingle() - hierarchyZ) > 0.000001f
            || Math.Abs(bone35Translation[2].GetSingle() + hierarchyY) > 0.000001f)
        {
            throw new InvalidDataException("DL compact bone 35 did not retain its parent-relative common translation.");
        }
    }

    using (var skinInput = File.OpenRead(testCase.MobyPath))
    {
        var skinModel = MobyModelReader.Read(
            skinInput,
            new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact, SkipAnimationSequences = true });
        var skinMesh = skinModel.MeshTable!.Entries.First(entry => entry.MeshType == MobyMeshType.HighLod);
        var vertexData = skinMesh.VertexData;
        var mainVertex = BitConverter.ToUInt16(vertexData, 0x02) + BitConverter.ToUInt16(vertexData, 0x04);
        var vertexOffset = BitConverter.ToUInt16(vertexData, 0x0C) + mainVertex * 0x10;
        if (BitConverter.ToUInt16(vertexData, 0x00) == 0 || vertexOffset + 0x10 > vertexData.Length)
        {
            throw new InvalidDataException("DL skin cache fixture has no usable rigid vertex.");
        }

        vertexData[0x10] = 0;
        vertexData[0x11] = 0xFC;
        BitConverter.GetBytes((ushort)((BitConverter.ToUInt16(vertexData, vertexOffset) & 0x01FF) | (3 << 9)))
            .CopyTo(vertexData, vertexOffset);
        vertexData[vertexOffset + 2] = 0xFC;
        vertexData[vertexOffset + 3] = 0xF8;

        var skinExport = DlMobyGltfExporter.Export(
            skinModel,
            options: new MobyGltfExportOptions { AnimationFormat = MobyAnimationFormat.Compact, SkipAnimationSequences = true });
        using var skinGltf = JsonDocument.Parse(skinExport.GltfBytes);
        var skinRoot = skinGltf.RootElement;
        var jointAccessorIndex = skinRoot.GetProperty("meshes")[0].GetProperty("primitives")[0]
            .GetProperty("attributes").GetProperty("JOINTS_0").GetInt32();
        var jointAccessor = skinRoot.GetProperty("accessors")[jointAccessorIndex];
        var jointView = skinRoot.GetProperty("bufferViews")[jointAccessor.GetProperty("bufferView").GetInt32()];
        var jointOffset = jointView.GetProperty("byteOffset").GetInt32() + mainVertex * 4 * sizeof(ushort);
        if (BitConverter.ToUInt16(skinExport.BinBytes, jointOffset) != 0)
        {
            throw new InvalidDataException("DL rigid vertex did not retain an explicitly cached joint 0.");
        }
    }

    foreach (var relativePath in new[]
    {
        Path.Combine("test-assets", "extractions", "level01_iso_world01", "assets", "moby", "04265_10A9", "moby.bin"),
        Path.Combine("test-assets", "extractions", "level01_iso_world01", "assets", "moby", "06295_1897", "moby.bin")
    })
    {
        var path = Path.Combine(repoRoot, relativePath);
        using var edgeInput = File.OpenRead(path);
        var edgeExport = ExportMobyGltf(
            edgeInput,
            "moby-animation-edge.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                BufferFileName = "moby-animation-edge.buffer.bin"
            });
        using var edgeGltf = JsonDocument.Parse(edgeExport.GltfBytes);
        using var edgeDiagnostics = JsonDocument.Parse(edgeExport.DiagnosticsBytes);
        if (edgeGltf.RootElement.GetProperty("animations").GetArrayLength() != 1
            || !edgeDiagnostics.RootElement.GetProperty("Animations")[0].GetProperty("Exported").GetBoolean())
        {
            throw new InvalidDataException($"{relativePath} compact animation was not exported.");
        }
    }
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
        var export = ExportMobyGltf(
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

        if (!TryExportFirstTexturedMaterial(
                testCase,
                new TextureAlphaInfo(39, 200, UsesBinaryAlpha: false),
                out var dlMaterial,
                textureFullOpacityAlpha: 128))
        {
            throw new InvalidDataException("PS2 raw alpha export did not produce a textured material.");
        }
        var dlExtras = dlMaterial.GetProperty("extras");
        if (dlExtras.GetProperty("MinAlpha").GetByte() != 39
            || dlExtras.GetProperty("MaxAlpha").GetByte() != 200
            || dlExtras.GetProperty("TextureFullOpacityAlpha").GetByte() != 128)
        {
            throw new InvalidDataException("moby material did not preserve raw PS2 alpha metadata for the renderer.");
        }
        return;
    }

    throw new InvalidDataException("No fixture produced a textured moby material.");
}

static bool TryExportFirstTexturedMaterial(
    RoundtripCase testCase,
    TextureAlphaInfo alpha,
    out JsonElement material,
    byte textureFullOpacityAlpha = byte.MaxValue)
{
    const int textureId = 7;
    var textureUris = new Dictionary<int, string> { [textureId] = $"textures/tex.{textureId:0000}.png" };
    var textureSizes = new Dictionary<int, TextureSize> { [textureId] = new(64, 32) };
    var textureAlpha = new Dictionary<int, TextureAlphaInfo> { [textureId] = alpha };
    var meshTextureOverrides = Enumerable.Range(0, 2048).ToDictionary(index => index, _ => textureId);

    using var input = File.OpenRead(testCase.MobyPath);
    var export = ExportMobyGltf(
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
            TextureFullOpacityAlpha = textureFullOpacityAlpha,
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
        var export = ExportMobyGltf(
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

static MobyGltfExport ExportMobyGltf(
    Stream input,
    string gltfFileName = "moby.gltf",
    MobyGltfExportOptions? options = null)
{
    return options?.AnimationFormat == MobyAnimationFormat.Compact
        ? DlMobyGltfExporter.Export(input, gltfFileName, options)
        : MobyGltfExporter.Export(input, gltfFileName, options);
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

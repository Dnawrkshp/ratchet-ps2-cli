using System.Buffers.Binary;
using System.Text.Json;
using System.Numerics;
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
        ValidateDlAnimationPacking(cases);
        Console.WriteLine("PASS DL compact animation packing");
    }
    catch (Exception ex)
    {
        failures.Add($"DL compact animation packing: {ex.Message}");
        Console.WriteLine("FAIL DL compact animation packing");
    }

    try
    {
        ValidateDlBangleTable(cases);
        Console.WriteLine("PASS DL bangle and corncob table layout");
    }
    catch (Exception ex)
    {
        failures.Add($"DL bangle and corncob table layout: {ex.Message}");
        Console.WriteLine("FAIL DL bangle and corncob table layout");
    }

    try
    {
        ValidateCommonTransformSizing(repoRoot);
        Console.WriteLine("PASS common transform sizing");
    }
    catch (Exception ex)
    {
        failures.Add($"Common transform sizing: {ex.Message}");
        Console.WriteLine("FAIL common transform sizing");
    }

    try
    {
        ValidateCompactSequenceMetadata(repoRoot);
        Console.WriteLine("PASS compact sequence pointers and format marker");
    }
    catch (Exception ex)
    {
        failures.Add($"Compact sequence pointers and format marker: {ex.Message}");
        Console.WriteLine("FAIL compact sequence pointers and format marker");
    }

    try
    {
        ValidateMeshTableOrdering();
        Console.WriteLine("PASS far LOD, metal, and bangle mesh layout");
    }
    catch (Exception ex)
    {
        failures.Add($"Far LOD, metal, and bangle mesh layout: {ex.Message}");
        Console.WriteLine("FAIL far LOD, metal, and bangle mesh layout");
    }

    try
    {
        ValidateUyaToDlConversion(cases);
        Console.WriteLine("PASS UYA 0x171C to DL conversion");
    }
    catch (Exception ex)
    {
        failures.Add($"UYA 0x171C to DL conversion: {ex.Message}");
        Console.WriteLine("FAIL UYA 0x171C to DL conversion");
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

    var attributes = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
    var positionAccessor = root.GetProperty("accessors")[attributes.GetProperty("POSITION").GetInt32()];
    var normalAccessor = root.GetProperty("accessors")[attributes.GetProperty("NORMAL").GetInt32()];
    if (normalAccessor.GetProperty("count").GetInt32() != positionAccessor.GetProperty("count").GetInt32())
    {
        throw new InvalidDataException("DL source normals were not exported for every vertex.");
    }

    var normalView = root.GetProperty("bufferViews")[normalAccessor.GetProperty("bufferView").GetInt32()];
    var positionView = root.GetProperty("bufferViews")[positionAccessor.GetProperty("bufferView").GetInt32()];
    var positionOffset = positionView.GetProperty("byteOffset").GetInt32()
        + (positionAccessor.TryGetProperty("byteOffset", out var positionAccessorOffset) ? positionAccessorOffset.GetInt32() : 0);
    var normalOffset = normalView.GetProperty("byteOffset").GetInt32()
        + (normalAccessor.TryGetProperty("byteOffset", out var normalAccessorOffset) ? normalAccessorOffset.GetInt32() : 0);
    var actualNormal = new Vector3(
        BitConverter.ToSingle(export.BinBytes, normalOffset),
        BitConverter.ToSingle(export.BinBytes, normalOffset + 4),
        BitConverter.ToSingle(export.BinBytes, normalOffset + 8));

    using (var normalSourceInput = File.OpenRead(testCase.MobyPath))
    {
        var normalSourceModel = MobyModelReader.Read(
            normalSourceInput,
            new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact, SkipAnimationSequences = true });
        var sourceMesh = normalSourceModel.MeshTable!.Entries.First(entry => entry.MeshType == MobyMeshType.HighLod);
        var sourceVertexOffset = BitConverter.ToUInt16(sourceMesh.VertexData, 0x0C);
        var azimuth = sourceMesh.VertexData[sourceVertexOffset + 0x08] * MathF.PI / 128f;
        var elevation = sourceMesh.VertexData[sourceVertexOffset + 0x09] * MathF.PI / 128f;
        var cosElevation = MathF.Cos(elevation);
        var expectedNormal = new Vector3(
            -MathF.Cos(azimuth) * cosElevation,
            -MathF.Sin(elevation),
            MathF.Sin(azimuth) * cosElevation);
        if (Vector3.Distance(actualNormal, expectedNormal) > 0.000001f)
        {
            throw new InvalidDataException($"DL source normal was {actualNormal}; expected {expectedNormal}.");
        }
    }

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var indexAccessor = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
        var indexView = root.GetProperty("bufferViews")[indexAccessor.GetProperty("bufferView").GetInt32()];
        var indexOffset = indexView.GetProperty("byteOffset").GetInt32()
            + (indexAccessor.TryGetProperty("byteOffset", out var indexAccessorOffset) ? indexAccessorOffset.GetInt32() : 0);
        for (var index = 0; index < indexAccessor.GetProperty("count").GetInt32(); index += 3)
        {
            var i0 = BitConverter.ToUInt32(export.BinBytes, indexOffset + index * sizeof(uint));
            var i1 = BitConverter.ToUInt32(export.BinBytes, indexOffset + (index + 1) * sizeof(uint));
            var i2 = BitConverter.ToUInt32(export.BinBytes, indexOffset + (index + 2) * sizeof(uint));
            var faceNormal = Vector3.Cross(
                ReadBufferVector3(export.BinBytes, positionOffset, i1) - ReadBufferVector3(export.BinBytes, positionOffset, i0),
                ReadBufferVector3(export.BinBytes, positionOffset, i2) - ReadBufferVector3(export.BinBytes, positionOffset, i0));
            var sourceNormal = ReadBufferVector3(export.BinBytes, normalOffset, i0)
                + ReadBufferVector3(export.BinBytes, normalOffset, i1)
                + ReadBufferVector3(export.BinBytes, normalOffset, i2);
            if (Vector3.Dot(faceNormal, sourceNormal) < 0f)
            {
                throw new InvalidDataException("DL triangle winding opposed its source normals.");
            }
        }
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

static void ValidateDlAnimationPacking(IReadOnlyList<RoundtripCase> cases)
{
    foreach (var compactCase in cases.Where(test => test.Game == "DL"))
    {
        var sourceBytes = File.ReadAllBytes(compactCase.MobyPath);
        MobyModel parsed;
        using (var input = new MemoryStream(sourceBytes, writable: false))
        {
            parsed = MobyModelReader.Read(input, new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact });
        }
        foreach (var sequence in parsed.Sequences)
        {
            sequence.RawData = null;
        }
        var structuredBytes = MobyModelPacker.Build(parsed);
        if (FindFirstMismatch(sourceBytes, structuredBytes) is { } structuredMismatch)
        {
            throw new InvalidDataException(
                $"{Path.GetRelativePath(FindRepoRoot(AppContext.BaseDirectory), compactCase.MobyPath)} parsed compact sequence packing differs at 0x{structuredMismatch.Offset:X}.");
        }
    }

    var testCase = cases.Single(test =>
        test.Game == "DL"
        && test.MobyPath.Contains("09500_251C", StringComparison.OrdinalIgnoreCase));
    var originalBytes = File.ReadAllBytes(testCase.MobyPath);
    const int editedAnimationIndex = 7;
    var editedSequenceOffset = BitConverter.ToInt32(originalBytes, 0x48 + editedAnimationIndex * 0x04);
    var editedAnimInfoOffset = BitConverter.ToInt32(originalBytes, editedSequenceOffset + 0x18);
    originalBytes[editedSequenceOffset + editedAnimInfoOffset] = 0x5A;
    MobyModel originalModel;
    using (var input = new MemoryStream(originalBytes, writable: false))
    {
        originalModel = MobyModelReader.Read(
            input,
            new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact });
    }

    MobyGltfExport export;
    using (var input = new MemoryStream(originalBytes, writable: false))
    {
        export = DlMobyGltfExporter.Export(
            input,
            "moby-packing.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                BufferFileName = "moby-packing.buffer.bin"
            });
    }

    var corruptTemplate = (byte[])originalBytes.Clone();
    var sequenceOffset = BitConverter.ToInt32(corruptTemplate, 0x48);
    var frameDataOffset = BitConverter.ToInt32(corruptTemplate, sequenceOffset + 0x1C);
    corruptTemplate[sequenceOffset + frameDataOffset + 0x10] ^= 0x40;
    var restored = ImportDl(corruptTemplate, export.GltfBytes, export.BinBytes);
    var restoredBytes = MobyModelPacker.Build(restored.Model);
    if (FindFirstMismatch(originalBytes, restoredBytes) is { } restoredMismatch)
    {
        throw new InvalidDataException(
            $"embedded compact source restoration differs at 0x{restoredMismatch.Offset:X}.");
    }

    using var gltf = JsonDocument.Parse(export.GltfBytes);
    var root = gltf.RootElement;
    var animation = root.GetProperty("animations")[editedAnimationIndex];
    var expectedScale = ReadAnimationVector3(root, export.BinBytes, animation, "scale", 1);
    var expectedTranslation = ReadAnimationVector3(root, export.BinBytes, animation, "translation", 1);
    var rotationChannel = animation.GetProperty("channels").EnumerateArray()
        .First(channel => channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
    var sampler = animation.GetProperty("samplers")[rotationChannel.GetProperty("sampler").GetInt32()];
    var accessor = root.GetProperty("accessors")[sampler.GetProperty("output").GetInt32()];
    var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var outputOffset = view.GetProperty("byteOffset").GetInt32()
        + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
    var editedBuffer = (byte[])export.BinBytes.Clone();
    var edited = Quaternion.Normalize(new Quaternion(
        BitConverter.ToSingle(editedBuffer, outputOffset + 0x10) + 0.01f,
        BitConverter.ToSingle(editedBuffer, outputOffset + 0x14),
        BitConverter.ToSingle(editedBuffer, outputOffset + 0x18),
        BitConverter.ToSingle(editedBuffer, outputOffset + 0x1C)));
    BitConverter.GetBytes(edited.X).CopyTo(editedBuffer, outputOffset + 0x10);
    BitConverter.GetBytes(edited.Y).CopyTo(editedBuffer, outputOffset + 0x14);
    BitConverter.GetBytes(edited.Z).CopyTo(editedBuffer, outputOffset + 0x18);
    BitConverter.GetBytes(edited.W).CopyTo(editedBuffer, outputOffset + 0x1C);

    var packed = ImportDl(originalBytes, export.GltfBytes, editedBuffer);
    if (packed.Model.Sequences[editedAnimationIndex].RawData is not null)
    {
        throw new InvalidDataException("edited glTF animation reused its embedded compact source payload.");
    }
    if (!packed.Model.Sequences[editedAnimationIndex].CompactAnimInfoData.SequenceEqual(
            originalModel.Sequences[editedAnimationIndex].CompactAnimInfoData))
    {
        throw new InvalidDataException("edited glTF animation discarded its compact metadata.");
    }
    if (!packed.Model.Sequences[editedAnimationIndex].CompactFrames.Select(frame => frame.FrameId).SequenceEqual(
            originalModel.Sequences[editedAnimationIndex].CompactFrames.Select(frame => frame.FrameId))
        || !packed.Model.Sequences[editedAnimationIndex].Triggers.Select(trigger => (trigger.Unknown00, trigger.Unknown02)).SequenceEqual(
            originalModel.Sequences[editedAnimationIndex].Triggers.Select(trigger => (trigger.Unknown00, trigger.Unknown02))))
    {
        throw new InvalidDataException("edited glTF animation discarded its frame or trigger metadata.");
    }

    var packedBytes = MobyModelPacker.Build(packed.Model);
    MobyGltfExport packedExport;
    using (var input = new MemoryStream(packedBytes, writable: false))
    {
        packedExport = DlMobyGltfExporter.Export(
            input,
            "moby-packed.gltf",
            new MobyGltfExportOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                BufferFileName = "moby-packed.buffer.bin"
            });
    }
    using var packedGltf = JsonDocument.Parse(packedExport.GltfBytes);
    var packedRoot = packedGltf.RootElement;
    var packedAnimation = packedRoot.GetProperty("animations")[editedAnimationIndex];
    var packedRotationChannel = packedAnimation.GetProperty("channels").EnumerateArray()
        .First(channel => channel.GetProperty("target").GetProperty("path").GetString() == "rotation");
    var packedSampler = packedAnimation.GetProperty("samplers")[packedRotationChannel.GetProperty("sampler").GetInt32()];
    var packedAccessor = packedRoot.GetProperty("accessors")[packedSampler.GetProperty("output").GetInt32()];
    var packedView = packedRoot.GetProperty("bufferViews")[packedAccessor.GetProperty("bufferView").GetInt32()];
    var packedOffset = packedView.GetProperty("byteOffset").GetInt32()
        + (packedAccessor.TryGetProperty("byteOffset", out var packedAccessorOffset) ? packedAccessorOffset.GetInt32() : 0)
        + 0x10;
    var actual = new Quaternion(
        BitConverter.ToSingle(packedExport.BinBytes, packedOffset),
        BitConverter.ToSingle(packedExport.BinBytes, packedOffset + 4),
        BitConverter.ToSingle(packedExport.BinBytes, packedOffset + 8),
        BitConverter.ToSingle(packedExport.BinBytes, packedOffset + 12));
    if (MathF.Abs(Quaternion.Dot(edited, actual)) < 0.99999f)
    {
        throw new InvalidDataException("edited compact rotation did not survive glTF packing.");
    }
    var actualScale = ReadAnimationVector3(packedRoot, packedExport.BinBytes, packedAnimation, "scale", 1);
    var actualTranslation = ReadAnimationVector3(packedRoot, packedExport.BinBytes, packedAnimation, "translation", 1);
    if (Vector3.Distance(expectedScale, actualScale) > 0.0003f
        || Vector3.Distance(expectedTranslation, actualTranslation) > 0.00001f)
    {
        throw new InvalidDataException("compact scale or translation tracks did not survive glTF packing.");
    }
}

static void ValidateUyaToDlConversion(IReadOnlyList<RoundtripCase> cases)
{
    var testCase = cases.Single(test =>
        test.Game == "UYA"
        && test.MobyPath.Contains("05916_171C", StringComparison.OrdinalIgnoreCase));
    var sourceBytes = File.ReadAllBytes(testCase.MobyPath);
    MobyModel model;
    using (var input = new MemoryStream(sourceBytes, writable: false))
    {
        model = MobyModelReader.Read(
            input,
            new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Standard });
    }

    var sourceAnimations = MobyStandardAnimationDecoder.Decode(model).Animations;
    var sourceModelSequences = model.Sequences.ToArray();
    var sourceMeshData = model.MeshTable!.Entries
        .Select(entry => (Vif: entry.VifData, Vertex: entry.VertexData, Texture: entry.VifTextureData))
        .ToArray();
    DlMobyConverter.ConvertFromUya(model);
    if (model.Sequences.Count != 69
        || model.Sequences.Any(sequence => sequence.Format != MobyAnimationFormat.Compact)
        || model.Sequences.Any(sequence => sequence.FormatMarker != 0)
        || model.SkeletonFormat != MobyAnimationFormat.Compact)
    {
        throw new InvalidDataException("0x171C did not produce loadable compact DL animations and skeleton.");
    }
    for (var sequenceIndex = 0; sequenceIndex < model.Sequences.Count; sequenceIndex++)
    {
        var sourceFrames = sourceModelSequences[sequenceIndex].Frames;
        var compactFrames = model.Sequences[sequenceIndex].CompactFrames;
        for (var frameIndex = 0; frameIndex < sourceFrames.Count; frameIndex++)
        {
            var expectedFrameId = unchecked((short)(ushort)(
                sourceFrames[frameIndex].Unknown04 | sourceFrames[frameIndex].Unknown05 << 8));
            if (compactFrames[frameIndex].FrameId != expectedFrameId)
            {
                throw new InvalidDataException(
                    $"0x171C animation {sequenceIndex} frame {frameIndex} lost source frame id 0x{unchecked((ushort)expectedFrameId):X4}.");
            }
        }
    }
    if (sourceMeshData.Where((payload, index) =>
            !payload.Vif.SequenceEqual(model.MeshTable.Entries[index].VifData)
            || !payload.Vertex.SequenceEqual(model.MeshTable.Entries[index].VertexData)
            || !(payload.Texture ?? []).SequenceEqual(model.MeshTable.Entries[index].VifTextureData ?? []))
        .Any())
    {
        throw new InvalidDataException("0x171C mesh payload changed during animation conversion.");
    }
    if (!model.Sequences.Any(sequence => UsesCompactOpcode(sequence, 0x00))
        || !model.Sequences.Any(sequence => UsesCompactOpcode(sequence, 0x30)))
    {
        throw new InvalidDataException("0x171C conversion did not use compact base and delta animation calls.");
    }

    var bytes = MobyModelPacker.Build(model);
    if (bytes.Length >= sourceBytes.Length)
    {
        throw new InvalidDataException(
            $"0x171C compact output was {bytes.Length} bytes; expected less than the {sourceBytes.Length}-byte UYA source.");
    }
    MobyModel packed;
    using (var input = new MemoryStream(bytes, writable: false))
    {
        packed = MobyModelReader.Read(
            input,
            new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact });
    }

    for (var i = 0; i < sourceAnimations.Count; i++)
    {
        if (!DlCompactAnimationDecoder.TryDecode(
                packed.Sequences[i], i, packed.JointCount, packed.Scale,
                out var converted, out var error))
        {
            throw new InvalidDataException($"0x171C compact animation {i} did not decode: {error}.");
        }
        RequireMatchingAnimation(sourceAnimations[i], converted);
    }
}

static void ValidateDlBangleTable(IReadOnlyList<RoundtripCase> cases)
{
    var testCase = cases.FirstOrDefault(test =>
        test.Game == "DL"
        && test.MobyPath.Contains("09500_251C", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException("The native DL player fixture is missing.");

    using var input = File.OpenRead(testCase.MobyPath);
    var model = MobyModelReader.Read(input, new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact });
    var table = model.BangleTable ?? throw new InvalidDataException("the native DL player has no bangle table.");
    var activeEntries = table.OffsetList
        .Where(entry => entry.HighLodMeshCount != 0 || entry.LowLodMeshCount != 0)
        .Select(entry => (
            entry.HighLodMeshTableIndex,
            entry.HighLodMeshCount,
            entry.LowLodMeshTableIndex,
            entry.LowLodMeshCount))
        .ToArray();
    var expectedEntries = new (byte HighIndex, byte HighCount, byte LowIndex, byte LowCount)[]
    {
        (23, 1, 0, 0), (24, 2, 0, 0), (26, 2, 0, 0), (28, 1, 0, 0), (29, 2, 0, 0)
    };
    if (table.MeshTableIndex != 23
        || table.MeshCount != 8
        || table.BangleMask != 0x1f
        || !activeEntries.SequenceEqual(expectedEntries)
        || table.DataList.Count != expectedEntries.Length)
    {
        throw new InvalidDataException("native DL player bangle metadata was decoded incorrectly.");
    }

    table.OffsetList[0].LowLodMeshTableIndex = table.OffsetList[0].HighLodMeshTableIndex;
    table.OffsetList[0].LowLodMeshCount = table.OffsetList[0].HighLodMeshCount;
    table.OffsetList[0].HighLodMeshCount = 0;
    var export = DlMobyGltfExporter.Export(
        model,
        options: new MobyGltfExportOptions { AnimationFormat = MobyAnimationFormat.Compact, SkipAnimationSequences = true });
    using var gltf = JsonDocument.Parse(export.GltfBytes);
    var nodes = gltf.RootElement.GetProperty("nodes");
    var bangle = nodes.EnumerateArray().Single(node =>
        node.TryGetProperty("name", out var name) && name.GetString() == "bangle_00");
    if (!bangle.GetProperty("children").EnumerateArray().Any(child =>
            nodes[child.GetInt32()].GetProperty("name").GetString() == "low_lod"))
    {
        throw new InvalidDataException("low-LOD bangle meshes were not grouped separately for export.");
    }

    var cornCob = model.CornCob ?? throw new InvalidDataException("the native DL player has no corncob table.");
    if (cornCob.KernelOffsets.Length != 0x10
        || cornCob.KernelOffsets[0] != 0xff
        || cornCob.Kernels.Count != 15
        || cornCob.Kernels[0] is null)
    {
        throw new InvalidDataException("native DL player corncob indices were decoded incorrectly.");
    }
}

static void ValidateCommonTransformSizing(string repoRoot)
{
    var path = Path.Combine(repoRoot, "test-assets", "Gleeman Vox", "moby.bin");
    using var input = File.OpenRead(path);
    var model = MobyModelReader.Read(input, new MobyModelReadOptions { SkipAnimationSequences = true });
    if (model.CommonTransforms?.Length != 0x170)
    {
        throw new InvalidDataException(
            $"static moby common transforms consumed 0x{model.CommonTransforms?.Length ?? 0:X} bytes; expected 0x170.");
    }
}

static void ValidateCompactSequenceMetadata(string repoRoot)
{
    var path = Path.Combine(
        repoRoot,
        "test-assets",
        "extractions",
        "level07_iso_world01",
        "assets",
        "moby",
        "08353_20A1",
        "moby.bin");
    using var input = File.OpenRead(path);
    var source = MobyModelReader.Read(
        input,
        new MobyModelReadOptions { AnimationFormat = MobyAnimationFormat.Compact });
    var sequence = source.Sequences.First(item =>
    {
        if (item.CompactAnimInfoData.Length < 0x08)
        {
            return false;
        }

        var first = BinaryPrimitives.ReadInt32LittleEndian(item.CompactAnimInfoData);
        var second = BinaryPrimitives.ReadInt32LittleEndian(item.CompactAnimInfoData.AsSpan(sizeof(int)));
        return first >= item.CompactAnimDataOffset
            && first < item.CompactFrameDataOffset
            && second >= item.CompactAnimDataOffset
            && second < item.CompactFrameDataOffset;
    });
    var oldAnimDataOffset = sequence.CompactAnimDataOffset;
    var oldPointers = new[]
    {
        BinaryPrimitives.ReadInt32LittleEndian(sequence.CompactAnimInfoData),
        BinaryPrimitives.ReadInt32LittleEndian(sequence.CompactAnimInfoData.AsSpan(sizeof(int)))
    };
    sequence.RawData = null;
    sequence.Triggers.Add(new MobyAnimationTrigger());

    var model = new MobyModel { AnimationFormat = MobyAnimationFormat.Compact };
    model.Sequences.Add(sequence);
    var packed = MobyModelPacker.Build(model);
    var sequenceOffset = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(0x48));
    var newAnimDataOffset = BinaryPrimitives.ReadInt32LittleEndian(packed.AsSpan(sequenceOffset + 0x18));
    if (newAnimDataOffset == oldAnimDataOffset)
    {
        throw new InvalidDataException("compact pointer regression did not move the animation info block.");
    }
    for (var i = 0; i < oldPointers.Length; i++)
    {
        var actual = BinaryPrimitives.ReadInt32LittleEndian(
            packed.AsSpan(sequenceOffset + newAnimDataOffset + i * sizeof(int)));
        var expected = oldPointers[i] + newAnimDataOffset - oldAnimDataOffset;
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"compact animation info pointer {i} was 0x{actual:X}; expected 0x{expected:X}.");
        }
    }

    var fallback = new MobyModel();
    MobyAnimationSlicer.ReplaceWithDefaultAnimation(fallback, MobyAnimationFormat.Compact);
    if (packed[sequenceOffset + 0x13] != 0 || fallback.Sequences[0].FormatMarker != 0)
    {
        throw new InvalidDataException("compact animation format marker was not zero.");
    }
}

static void ValidateMeshTableOrdering()
{
    var model = new MobyModel
    {
        HighLodMeshCount = 1,
        LowLodMeshCount = 1,
        FarLodMeshCount = 1,
        MetalCount = 1,
        MetalOffsets = 2,
        MeshTable = new MobyMeshTable(),
        ShadowPrefixData = Enumerable.Repeat((byte)0xa5, 0x10).ToArray(),
        ShadowData = Enumerable.Repeat((byte)0x5a, 0x10).ToArray(),
        Skeleton = new MobySkeleton(),
        BangleTable = new MobyBangleTable
        {
            MeshTableIndex = 4,
            MeshCount = 2,
            BangleMask = 0b101
        }
    };
    model.BangleTable.OffsetList.AddRange(Enumerable.Range(0, 15).Select(_ => new MobyBangleListEntry()));
    model.BangleTable.OffsetList[0].HighLodMeshTableIndex = 4;
    model.BangleTable.OffsetList[0].HighLodMeshCount = 1;
    model.BangleTable.OffsetList[2].LowLodMeshTableIndex = 5;
    model.BangleTable.OffsetList[2].LowLodMeshCount = 1;
    model.BangleTable.DataList.AddRange(new[]
    {
        new MobyBangleData { Unknown00 = 10 },
        new MobyBangleData { Unknown00 = 20 },
        new MobyBangleData { Unknown00 = 30 }
    });
    model.MeshTable.Entries.AddRange(new[]
    {
        new MobyMeshTableEntry { MeshType = MobyMeshType.HighLod },
        new MobyMeshTableEntry { MeshType = MobyMeshType.LowLod },
        new MobyMeshTableEntry { MeshType = MobyMeshType.Metal },
        new MobyMeshTableEntry { MeshType = MobyMeshType.FarLod },
        new MobyMeshTableEntry { MeshType = MobyMeshType.Bangle },
        new MobyMeshTableEntry { MeshType = MobyMeshType.Bangle }
    });
    model.Skeleton!.Bones.Add(new MobyMatrix4());

    using var input = new MemoryStream(MobyModelPacker.Build(model), writable: false);
    var packed = MobyModelReader.Read(input);
    var expected = new[]
    {
        MobyMeshType.HighLod,
        MobyMeshType.LowLod,
        MobyMeshType.Metal,
        MobyMeshType.FarLod,
        MobyMeshType.Bangle,
        MobyMeshType.Bangle
    };
    if (packed.MeshTable is null
        || !packed.MeshTable.Entries.Select(entry => entry.MeshType).SequenceEqual(expected)
        || packed.MetalOffsets != 2
        || packed.BangleTable?.OffsetList[2].LowLodMeshTableIndex != 5
        || packed.BangleTable.DataList.Count != 3
        || packed.BangleTable.DataList[2].Unknown00 != 30
        || packed.ShadowPrefixData is null
        || !packed.ShadowPrefixData.SequenceEqual(model.ShadowPrefixData!))
    {
        throw new InvalidDataException("mesh table was not decoded as base, metal, far LOD, then bangle meshes.");
    }
}

static bool UsesCompactOpcode(MobySequence sequence, byte expectedOpcode)
{
    var data = sequence.CompactFrameData;
    if (data.Length < 0x10)
    {
        return false;
    }

    var pairCount = data[8] + data[9] + data[10];
    var opcodeStart = 0x10 + data[3] * 0x10 + pairCount * 0x08;
    var callCount = pairCount + 2;
    return opcodeStart + callCount <= data.Length
        && data.AsSpan(opcodeStart, callCount).Contains(expectedOpcode);
}

static void RequireMatchingAnimation(MobyGltfAnimationClip source, MobyGltfAnimationClip converted)
{
    if (!source.Times.SequenceEqual(converted.Times)
        || !source.Rotations.Keys.SequenceEqual(converted.Rotations.Keys)
        || !source.Scales.Keys.SequenceEqual(converted.Scales.Keys)
        || !source.Translations.Keys.SequenceEqual(converted.Translations.Keys))
    {
        throw new InvalidDataException($"Animation {source.SourceIndex} track layout or timing changed during DL conversion.");
    }

    foreach (var joint in source.Rotations.Keys)
    {
        if (source.Rotations[joint].Zip(converted.Rotations[joint])
            .Any(pair => MathF.Abs(Quaternion.Dot(pair.First, pair.Second)) < 0.99999f))
        {
            throw new InvalidDataException($"Animation {source.SourceIndex} joint {joint} rotation changed during DL conversion.");
        }
    }
    foreach (var joint in source.Scales.Keys)
    {
        if (source.Scales[joint].Zip(converted.Scales[joint])
            .Any(pair => Vector3.Distance(pair.First, pair.Second) > 0.0003f))
        {
            throw new InvalidDataException($"Animation {source.SourceIndex} joint {joint} scale changed during DL conversion.");
        }
    }
    foreach (var joint in source.Translations.Keys)
    {
        if (source.Translations[joint].Zip(converted.Translations[joint])
            .Any(pair => Vector3.Distance(pair.First, pair.Second) > 0.0003f))
        {
            throw new InvalidDataException($"Animation {source.SourceIndex} joint {joint} translation changed during DL conversion.");
        }
    }
}

static Vector3 ReadAnimationVector3(
    JsonElement root,
    byte[] buffer,
    JsonElement animation,
    string path,
    int keyIndex)
{
    var channel = animation.GetProperty("channels").EnumerateArray()
        .First(item => item.GetProperty("target").GetProperty("path").GetString() == path);
    var sampler = animation.GetProperty("samplers")[channel.GetProperty("sampler").GetInt32()];
    var accessor = root.GetProperty("accessors")[sampler.GetProperty("output").GetInt32()];
    var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var offset = view.GetProperty("byteOffset").GetInt32()
        + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0)
        + keyIndex * 3 * sizeof(float);
    return new Vector3(
        BitConverter.ToSingle(buffer, offset),
        BitConverter.ToSingle(buffer, offset + 4),
        BitConverter.ToSingle(buffer, offset + 8));
}

static Vector3 ReadBufferVector3(byte[] buffer, int baseOffset, uint index)
{
    var offset = checked(baseOffset + (int)index * 3 * sizeof(float));
    return new Vector3(
        BitConverter.ToSingle(buffer, offset),
        BitConverter.ToSingle(buffer, offset + 4),
        BitConverter.ToSingle(buffer, offset + 8));
}

static MobyGltfImportResult ImportDl(byte[] templateBytes, byte[] gltfBytes, byte[] bufferBytes)
{
    using var template = new MemoryStream(templateBytes, writable: false);
    using var gltf = new MemoryStream(gltfBytes, writable: false);
    return DlMobyGltfImporter.ImportWithDiagnostics(
        template,
        gltf,
        _ => new MemoryStream(bufferBytes, writable: false),
        new MobyGltfImportOptions
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            PacketMode = MobyGltfImportPacketMode.Passthrough
        });
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

        if (model.LowLodMeshCount == 0 && model.FarLodMeshCount == 0)
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
                || name.Contains("FarLod", StringComparison.Ordinal)
                || name.Contains("MeshType2", StringComparison.Ordinal)
                || name.Contains("low_lod", StringComparison.Ordinal)
                || name.Contains("far_lod", StringComparison.Ordinal)
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
        var options = new MobyGltfImportOptions
        {
            AnimationFormat = testCase.Format,
            PacketMode = MobyGltfImportPacketMode.Passthrough
        };
        Func<string, Stream> openBuffer = bufferName =>
            File.OpenRead(Path.Combine(caseDirectory, Uri.UnescapeDataString(bufferName)));
        result = testCase.Format == MobyAnimationFormat.Compact
            ? DlMobyGltfImporter.ImportWithDiagnostics(template, gltf, openBuffer, options)
            : MobyGltfImporter.ImportWithDiagnostics(template, gltf, openBuffer, options);
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

using RatchetPs2.Core.Moby;

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

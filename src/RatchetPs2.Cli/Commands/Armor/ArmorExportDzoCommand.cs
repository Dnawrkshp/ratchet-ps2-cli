using System.Buffers.Binary;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.DL.Armor;

namespace RatchetPs2.Cli.Commands.Armor;

internal static class ArmorExportDzoCommand
{
    public static Command Build()
    {
        var inputOption = CommonOptions.InputFile("Path to a self-contained DL armor WAD.");
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write DZO armor GLB files.",
            Required = true
        };
        var jointHierarchyOption = new Option<string>("--joint-hierarchy")
        {
            Description = "Choose the exported joint hierarchy: flat or tree.",
            DefaultValueFactory = _ => "flat"
        };
        var armorOption = new Option<int[]>("--armor")
        {
            Description = "Armor slot index to export. Repeat the option or provide multiple values; omit it to export every populated slot.",
            AllowMultipleArgumentsPerToken = true
        };
        var command = CliCommandBuilder.Create(
            "export-dzo",
            "Export one or more player armors from a DL armor WAD as GLB files for DZO.",
            inputOption,
            outputRootOption,
            jointHierarchyOption,
            armorOption);

        command.SetAction(parseResult =>
        {
            var inputFile = parseResult.GetValue(inputOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            var jointHierarchy = parseResult.GetValue(jointHierarchyOption);
            var armorIndices = parseResult.GetValue(armorOption) ?? [];
            if (!string.Equals(jointHierarchy, "flat", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(jointHierarchy, "tree", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"Unsupported --joint-hierarchy value '{jointHierarchy}'. Expected flat or tree.");
                return 1;
            }

            if (inputFile is null || !inputFile.Exists)
            {
                Console.Error.WriteLine($"Input armor WAD '{inputFile?.FullName}' does not exist.");
                return 1;
            }

            if (outputRoot is null)
            {
                Console.Error.WriteLine("Missing required --output-root option.");
                return 1;
            }

            if (armorIndices.Any(index => index < 0 || index >= DlArmorWadReader.JapaneseArmorCount))
            {
                Console.Error.WriteLine(
                    $"Armor indices must be between 0 and {DlArmorWadReader.JapaneseArmorCount - 1}.");
                return 1;
            }

            var flattenJointHierarchy = string.Equals(
                jointHierarchy,
                "flat",
                StringComparison.OrdinalIgnoreCase);
            outputRoot.Create();

            try
            {
                using var wadStream = inputFile.OpenRead();
                var manifestEntries = new List<DzoArmorManifestEntry>();
                var exported = 0;
                var failed = 0;
                foreach (var result in DlDzoArmorExporter.ExportWad(
                             wadStream,
                             armorIndices.ToHashSet(),
                             flattenJointHierarchy))
                {
                    var name = result.ArmorIndex.ToString("0000", CultureInfo.InvariantCulture);
                    if (!result.Succeeded)
                    {
                        Console.Error.WriteLine(
                            $"Failed armor {result.ArmorIndex}: {result.Error}");
                        manifestEntries.Add(new DzoArmorManifestEntry(
                            name,
                            result.ArmorIndex,
                            null,
                            "error",
                            result.Error,
                            0,
                            0,
                            0,
                            0));
                        failed++;
                        continue;
                    }

                    var outputPath = Path.Combine(outputRoot.FullName, $"{name}.glb");
                    File.WriteAllBytes(outputPath, result.GlbBytes);
                    var modelInfo = InspectGlb(result.GlbBytes);
                    manifestEntries.Add(new DzoArmorManifestEntry(
                        name,
                        result.ArmorIndex,
                        Path.GetFileName(outputPath),
                        "written",
                        null,
                        modelInfo.MeshCount,
                        modelInfo.VertexCount,
                        modelInfo.TriangleCount,
                        modelInfo.TextureCount));
                    exported++;
                }

                File.WriteAllBytes(
                    Path.Combine(outputRoot.FullName, "manifest.json"),
                    JsonSerializer.SerializeToUtf8Bytes(
                        new { Format = "ratchet-ps2-dzo-armor-viewer-v1", Armors = manifestEntries },
                        new JsonSerializerOptions { WriteIndented = true }));

                Console.WriteLine(
                    $"Exported {exported} DZO armor GLBs from '{inputFile.FullName}' to '{outputRoot.FullName}' ({failed} failed).");
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex) when (IsExportFailure(ex))
            {
                Console.Error.WriteLine($"Failed to export DL armors: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private static GltfModelInfo InspectGlb(byte[] glbBytes)
    {
        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glbBytes.AsSpan(12)));
        using var document = JsonDocument.Parse(glbBytes.AsMemory(20, jsonLength));
        return GltfModelInspector.Inspect(document.RootElement);
    }

    private static bool IsExportFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException
            or OverflowException
            or UnauthorizedAccessException;
    }

    private sealed record DzoArmorManifestEntry(
        string Name,
        int ArmorIndex,
        string? Gltf,
        string Status,
        string? Error,
        int Meshes,
        int Vertices,
        int Triangles,
        int Images);
}

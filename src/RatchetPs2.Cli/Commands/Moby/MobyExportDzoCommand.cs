using System.Buffers.Binary;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Cli.Commands.Moby;

internal static class MobyExportDzoCommand
{
    public static Command Build()
    {
        var inputRootOption = new Option<DirectoryInfo>("--input-root")
        {
            Description = "Directory containing DL level*.wad files.",
            Required = true
        };
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write DZO moby GLB files.",
            Required = true
        };
        var command = CliCommandBuilder.Create(
            "export-dzo",
            "Export every main-level and mission moby from DL level WADs as GLB files for DZO.",
            inputRootOption,
            outputRootOption);

        command.SetAction(parseResult =>
        {
            var inputRoot = parseResult.GetValue(inputRootOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            if (inputRoot is null || !inputRoot.Exists)
            {
                Console.Error.WriteLine($"Input root '{inputRoot?.FullName}' does not exist.");
                return 1;
            }

            if (outputRoot is null)
            {
                Console.Error.WriteLine("Missing required --output-root option.");
                return 1;
            }

            var levelWads = inputRoot
                .EnumerateFiles("level*.wad", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (levelWads.Length == 0)
            {
                Console.Error.WriteLine($"No level*.wad files were found in '{inputRoot.FullName}'.");
                return 1;
            }

            outputRoot.Create();
            var exported = 0;
            var failed = 0;
            foreach (var levelWad in levelWads)
            {
                try
                {
                    var levelOutput = Path.Combine(
                        outputRoot.FullName,
                        Path.GetFileNameWithoutExtension(levelWad.Name));
                    var manifestEntries = new List<DzoViewerManifestEntry>();
                    foreach (var result in DlDzoMobyExporter.ExportLevel(File.ReadAllBytes(levelWad.FullName)))
                    {
                        var source = result.MissionIndex is { } missionIndex
                            ? Path.Combine("missions", missionIndex.ToString("0000", CultureInfo.InvariantCulture))
                            : "main";
                        var group = result.MissionIndex is { } groupMissionIndex
                            ? $"mission_{groupMissionIndex}"
                            : "main";
                        var name = result.ClassId.ToString("x4", CultureInfo.InvariantCulture);
                        if (!result.Succeeded)
                        {
                            Console.Error.WriteLine(
                                $"Failed {levelWad.Name} {source} moby 0x{result.ClassId:X4}: {result.Error}");
                            manifestEntries.Add(new DzoViewerManifestEntry(
                                group,
                                name,
                                result.ClassId,
                                null,
                                "error",
                                result.Error,
                                0,
                                0,
                                0,
                                0,
                                0));
                            failed++;
                            continue;
                        }

                        var outputDirectory = Path.Combine(levelOutput, source);
                        Directory.CreateDirectory(outputDirectory);
                        var outputPath = Path.Combine(outputDirectory, $"{name}.glb");
                        File.WriteAllBytes(outputPath, result.GlbBytes);
                        var modelInfo = InspectGlb(result.GlbBytes);
                        manifestEntries.Add(new DzoViewerManifestEntry(
                            group,
                            name,
                            result.ClassId,
                            Path.GetRelativePath(levelOutput, outputPath).Replace(Path.DirectorySeparatorChar, '/'),
                            "written",
                            null,
                            modelInfo.MeshCount,
                            modelInfo.VertexCount,
                            modelInfo.TriangleCount,
                            0,
                            modelInfo.TextureCount));
                        exported++;
                    }

                    Directory.CreateDirectory(levelOutput);
                    File.WriteAllBytes(
                        Path.Combine(levelOutput, "manifest.json"),
                        JsonSerializer.SerializeToUtf8Bytes(
                            new { Format = "ratchet-ps2-dzo-moby-viewer-v1", Mobys = manifestEntries },
                            new JsonSerializerOptions { WriteIndented = true }));
                }
                catch (Exception ex) when (IsExportFailure(ex))
                {
                    Console.Error.WriteLine($"Failed {levelWad.Name}: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine(
                $"Exported {exported} DZO moby GLBs from {levelWads.Length} DL level WADs to '{outputRoot.FullName}' ({failed} failed).");
            return failed == 0 ? 0 : 1;
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

    private sealed record DzoViewerManifestEntry(
        string Group,
        string Name,
        int ClassId,
        string? Gltf,
        string Status,
        string? Error,
        int Meshes,
        int Vertices,
        int Triangles,
        int InvalidMeshRecords,
        int Images);
}

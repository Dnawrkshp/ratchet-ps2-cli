using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Skyboxes;

namespace RatchetPs2.Cli.Abstractions;

internal sealed record SkyboxFileExport(
    Skybox Skybox,
    SkyboxGltfExport Export,
    FileInfo GltfFile,
    FileInfo BufferFile,
    FileInfo? DiagnosticsFile)
{
    public long OutputBytes => Export.GltfBytes.Length
        + Export.BinBytes.Length
        + Export.DiagnosticsBytes.Length
        + Export.Textures.Sum(texture => texture.PngBytes.Length);
}

internal static class SkyboxExportWriter
{
    public static SkyboxFileExport Export(
        FileInfo inputFile,
        FileInfo gltfFile,
        GameId gameId,
        int? levelNumber,
        bool minify = false)
    {
        ArgumentNullException.ThrowIfNull(inputFile);
        ArgumentNullException.ThrowIfNull(gltfFile);

        var outputDirectory = gltfFile.Directory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        outputDirectory.Create();

        var outputBaseName = Path.GetFileNameWithoutExtension(gltfFile.Name);
        var bufferFile = new FileInfo(Path.Combine(outputDirectory.FullName, $"{outputBaseName}.buffer.bin"));
        var diagnosticsFile = new FileInfo(Path.Combine(outputDirectory.FullName, $"{outputBaseName}.diagnostics.json"));

        using var input = inputFile.OpenRead();
        var skybox = SkyboxReader.Read(input);
        var profile = SkyboxGameFormats.ProfileFor(gameId);
        var export = SkyboxGltfExporter.Export(
            skybox,
            gltfFile.Name,
            profile.CreateExportOptions(
                bufferFile.Name,
                levelNumber,
                skybox.Shells.Count,
                includeDiagnostics: !minify,
                minify: minify,
                metadataMode: minify ? GltfExportMetadataMode.RuntimeOnly : GltfExportMetadataMode.Full));

        File.WriteAllBytes(gltfFile.FullName, export.GltfBytes);
        File.WriteAllBytes(bufferFile.FullName, export.BinBytes);
        if (export.DiagnosticsBytes.Length > 0)
        {
            File.WriteAllBytes(diagnosticsFile.FullName, export.DiagnosticsBytes);
        }
        WriteTextures(outputDirectory, export);

        return new SkyboxFileExport(
            skybox,
            export,
            gltfFile,
            bufferFile,
            export.DiagnosticsBytes.Length > 0 ? diagnosticsFile : null);
    }

    private static void WriteTextures(DirectoryInfo outputDirectory, SkyboxGltfExport export)
    {
        if (export.Textures.Count == 0)
        {
            return;
        }

        var textureDirectory = new DirectoryInfo(Path.Combine(outputDirectory.FullName, "textures"));
        textureDirectory.Create();
        foreach (var texture in export.Textures)
        {
            File.WriteAllBytes(Path.Combine(textureDirectory.FullName, texture.FileName), texture.PngBytes);
        }
    }
}

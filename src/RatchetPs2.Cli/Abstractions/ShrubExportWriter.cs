using RatchetPs2.Core.Games;
using RatchetPs2.Core.Gltf;
using RatchetPs2.Core.Shrubs;
using RatchetPs2.Core.Textures.Png;

namespace RatchetPs2.Cli.Abstractions;

internal sealed record ShrubFileExport(
    string SourceKind,
    int? Id,
    ShrubClass? PackedShrub,
    GltfModelInfo ModelInfo,
    FileInfo GltfFile,
    FileInfo BufferFile,
    FileInfo DiagnosticsFile,
    IReadOnlyList<TextureResourceEntry> Textures,
    ShrubExportBillboard? Billboard,
    string? BillboardTextureUri,
    long InputBytes,
    long OutputBytes);

internal sealed record ShrubExportBillboard(
    float FadeDistance,
    float Width,
    float Height,
    float ZOffset);

internal sealed record BillboardTextureResource(
    string Uri,
    TextureSize Size,
    TextureAlphaInfo Alpha,
    TextureResourceEntry Entry);

internal static class ShrubExportWriter
{
    public static ShrubFileExport Export(
        FileInfo inputFile,
        FileInfo gltfFile,
        GameId gameId,
        DirectoryInfo? textureDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(inputFile);
        ArgumentNullException.ThrowIfNull(gltfFile);

        if (!inputFile.Exists)
        {
            throw new FileNotFoundException("Shrub input file does not exist.", inputFile.FullName);
        }

        var extension = inputFile.Extension.ToLowerInvariant();
        if (extension is ".asset" or ".glb")
        {
            throw new NotSupportedException("Shrub glTF export now expects packed shrub class binaries such as core.bin.");
        }

        return ExportPackedBinary(inputFile, gltfFile, gameId, textureDirectory);
    }

    private static ShrubFileExport ExportPackedBinary(
        FileInfo inputFile,
        FileInfo gltfFile,
        GameId gameId,
        DirectoryInfo? textureDirectory)
    {
        var outputDirectory = PrepareOutputDirectory(gltfFile);
        var outputBaseName = Path.GetFileNameWithoutExtension(gltfFile.Name);
        var bufferFile = new FileInfo(Path.Combine(outputDirectory.FullName, $"{outputBaseName}.buffer.bin"));
        var diagnosticsFile = new FileInfo(Path.Combine(outputDirectory.FullName, $"{outputBaseName}.diagnostics.json"));
        var textureResources = TextureResourcePreparer.PrepareExternalTextures(
            textureDirectory ?? inputFile.Directory,
            outputDirectory);

        using var input = inputFile.OpenRead();
        var shrub = ShrubClassReader.Read(input);
        var billboardTexture = shrub.Billboard is null
            ? null
            : PreparePackedBillboardTexture(textureDirectory ?? inputFile.Directory, outputDirectory);
        var export = ShrubGltfExporter.Export(
            shrub,
            gltfFile.Name,
            new ShrubGltfExportOptions
            {
                BufferFileName = bufferFile.Name,
                GameLabel = gameId.ToString(),
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha,
                ExternalBillboardTextureUri = billboardTexture?.Uri,
                ExternalBillboardTextureSize = billboardTexture?.Size,
                ExternalBillboardTextureAlpha = billboardTexture?.Alpha
            });

        File.WriteAllBytes(gltfFile.FullName, export.GltfBytes);
        File.WriteAllBytes(bufferFile.FullName, export.BinBytes);
        File.WriteAllBytes(diagnosticsFile.FullName, export.DiagnosticsBytes);
        using var gltfInput = new MemoryStream(export.GltfBytes);
        var modelInfo = GltfModelInspector.Inspect(gltfInput);
        var textureEntries = InterpretShrubTextureEntries(textureResources?.Entries ?? []).ToList();
        if (billboardTexture is not null)
        {
            textureEntries.Add(billboardTexture.Entry);
        }

        return new ShrubFileExport(
            "packed",
            shrub.Header.OClass,
            shrub,
            modelInfo,
            gltfFile,
            bufferFile,
            diagnosticsFile,
            textureEntries,
            ToExportBillboard(shrub.Billboard),
            billboardTexture?.Uri,
            inputFile.Length,
            export.GltfBytes.Length
                + export.BinBytes.Length
                + export.DiagnosticsBytes.Length
                + SumTextureBytes(outputDirectory, textureEntries));
    }

    private static BillboardTextureResource? PreparePackedBillboardTexture(
        DirectoryInfo? sourceDirectory,
        DirectoryInfo outputDirectory)
    {
        if (sourceDirectory is null || !sourceDirectory.Exists)
        {
            return null;
        }

        var sourceFile = FindPackedBillboardTexture(sourceDirectory);
        if (sourceFile is null)
        {
            return null;
        }

        var textureOutputDirectory = new DirectoryInfo(Path.Combine(outputDirectory.FullName, "textures"));
        textureOutputDirectory.Create();
        var destinationFile = new FileInfo(Path.Combine(textureOutputDirectory.FullName, sourceFile.Name));
        if (!Path.GetFullPath(sourceFile.FullName).Equals(Path.GetFullPath(destinationFile.FullName), StringComparison.Ordinal))
        {
            sourceFile.CopyTo(destinationFile.FullName, overwrite: true);
        }

        var uri = CliPathUtils.ToUriPath(Path.GetRelativePath(outputDirectory.FullName, destinationFile.FullName));
        using var textureInput = sourceFile.OpenRead();
        var metadata = PngTextureMetadataReader.ReadPng(textureInput);
        var shrubAlpha = ShrubTextureAlpha.Interpret(metadata.Alpha);
        var entry = new TextureResourceEntry(
            -1,
            uri,
            metadata.Size.Width,
            metadata.Size.Height,
            shrubAlpha.HasAlpha,
            shrubAlpha.AlphaMode.ToString(),
            shrubAlpha.GltfAlphaMode,
            metadata.Alpha.MinAlpha,
            metadata.Alpha.MaxAlpha,
            shrubAlpha.UsesBinaryAlpha,
            ShrubTextureAlpha.FullOpacityAlpha);

        return new BillboardTextureResource(uri, metadata.Size, metadata.Alpha, entry);
    }

    private static FileInfo? FindPackedBillboardTexture(DirectoryInfo sourceDirectory)
    {
        var sourceId = sourceDirectory.Name;
        var searchDirectories = new[]
        {
            new DirectoryInfo(Path.Combine(sourceDirectory.FullName, "billboard")),
            new DirectoryInfo(Path.Combine(sourceDirectory.FullName, "Textures")),
            new DirectoryInfo(Path.Combine(sourceDirectory.FullName, "textures")),
            sourceDirectory
        };

        foreach (var directory in searchDirectories.Where(directory => directory.Exists))
        {
            var specific = directory.EnumerateFiles($"billboard-{sourceId}-*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (specific is not null)
            {
                return specific;
            }
        }

        foreach (var directory in searchDirectories.Where(directory => directory.Exists))
        {
            var fallback = directory.EnumerateFiles("billboard-*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fallback is not null)
            {
                return fallback;
            }
        }

        foreach (var directory in searchDirectories.Where(directory => directory.Exists))
        {
            var extracted = directory.EnumerateFiles("tex.*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return null;
    }

    private static ShrubExportBillboard? ToExportBillboard(ShrubBillboard? billboard)
    {
        return billboard is null
            ? null
            : new ShrubExportBillboard(
                billboard.FadeDistance,
                billboard.Width,
                billboard.Height,
                billboard.ZOffset);
    }

    private static DirectoryInfo PrepareOutputDirectory(FileInfo gltfFile)
    {
        var outputDirectory = gltfFile.Directory ?? new DirectoryInfo(Directory.GetCurrentDirectory());
        outputDirectory.Create();
        return outputDirectory;
    }

    private static IReadOnlyList<TextureResourceEntry> InterpretShrubTextureEntries(IReadOnlyList<TextureResourceEntry> textures)
    {
        if (textures.Count == 0)
        {
            return textures;
        }

        return textures.Select(InterpretShrubTextureEntry).ToArray();
    }

    private static TextureResourceEntry InterpretShrubTextureEntry(TextureResourceEntry texture)
    {
        var shrubAlpha = ShrubTextureAlpha.Interpret(new TextureAlphaInfo(
            texture.MinAlpha,
            texture.MaxAlpha,
            texture.UsesBinaryAlpha));

        return texture with
        {
            HasAlpha = shrubAlpha.HasAlpha,
            AlphaMode = shrubAlpha.AlphaMode.ToString(),
            GltfAlphaMode = shrubAlpha.GltfAlphaMode,
            UsesBinaryAlpha = shrubAlpha.UsesBinaryAlpha,
            FullOpacityAlpha = ShrubTextureAlpha.FullOpacityAlpha
        };
    }

    private static string UriToPath(string uri)
    {
        return Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar);
    }

    private static long SumTextureBytes(DirectoryInfo outputDirectory, IReadOnlyList<TextureResourceEntry> textures)
    {
        var total = 0L;
        foreach (var texture in textures)
        {
            var textureFile = new FileInfo(Path.Combine(outputDirectory.FullName, UriToPath(texture.Uri)));
            if (textureFile.Exists)
            {
                total += textureFile.Length;
            }
        }

        return total;
    }
}

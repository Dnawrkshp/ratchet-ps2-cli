using System.Text.Json;
using System.Text.Json.Nodes;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Games.DL.Level;

namespace RatchetPs2.Games.DL.Moby;

public sealed record DlDzoMobyExportResult(
    int? MissionIndex,
    int ClassId,
    byte[] GlbBytes,
    string? Error)
{
    public bool Succeeded => Error is null;
}

public static class DlDzoMobyExporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static IEnumerable<DlDzoMobyExportResult> ExportLevel(byte[] levelWadBytes)
    {
        ArgumentNullException.ThrowIfNull(levelWadBytes);

        var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
        var coreBytes = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data);
        if (coreBytes.Length == 0)
        {
            throw new InvalidDataException("DL level WAD does not contain a core level payload.");
        }

        var segments = DlCoreLevelSegmentReader.Read(coreBytes).ToDictionary(segment => segment.HeaderOffset);
        if (!segments.TryGetValue(0x10, out var assetHeader)
            || !segments.TryGetValue(0x18, out var palette)
            || !segments.TryGetValue(0x50, out var assetWad))
        {
            throw new InvalidDataException("DL level WAD is missing one or more required asset core segments.");
        }

        var headerBytes = assetHeader.PayloadBytes;
        var paletteBytes = palette.PayloadBytes;
        var assetBytes = assetWad.PayloadBytes;
        var header = DlAssetReader.ReadHeader(headerBytes);
        var mipmaps = DlAssetReader.ReadMipmapDefinitions(
            headerBytes,
            header.GsRamOffset,
            Math.Max(0, header.GsRamCount + header.ExtraMipmapCount));
        var gsStashDefinitions = mipmaps.Skip(header.GsRamCount).ToArray();
        var gsStashClassIds = DlAssetReader.ReadMobyGsStashClassIds(
            headerBytes,
            header.MobyGsStashListOffset);
        var mobyDefinitions = DlAssetReader.ReadModelDefinitions(
            headerBytes,
            header.MobyModelOffset,
            header.MobyModelCount);
        var tieDefinitions = DlAssetReader.ReadModelDefinitions(
            headerBytes,
            header.TieModelOffset,
            header.TieModelCount);
        var shrubDefinitions = DlAssetReader.ReadShrubDefinitions(
            headerBytes,
            header.ShrubModelOffset,
            header.ShrubModelCount);
        var textureDefinitions = DlAssetReader.ReadTextureDefinitions(
            headerBytes,
            header.MobyTextureOffset,
            header.MobyTextureCount);
        var knownOffsets = DlAssetReader.CollectKnownAssetOffsets(
            GameId.DL,
            header,
            assetBytes.Length,
            mobyDefinitions,
            tieDefinitions,
            shrubDefinitions);

        foreach (var definition in mobyDefinitions)
        {
            var modelBytes = DlAssetReader.ReadAssetSlice(assetBytes, definition.ModelOffset, knownOffsets);
            if (modelBytes.Length == 0)
            {
                continue;
            }

            DlDzoMobyExportResult result;
            try
            {
                var textures = new List<byte[]>();
                var isSwizzled = !gsStashClassIds.Contains(definition.ModelId);
                foreach (var textureId in definition.TextureIds)
                {
                    if (textureId == 0xff || textureId >= textureDefinitions.Count)
                    {
                        continue;
                    }

                    textures.Add(DlAssetReader.BuildAssetTexture(
                        "moby",
                        textures.Count,
                        textureDefinitions[textureId],
                        paletteBytes,
                        assetBytes,
                        header.TextureDataOffset,
                        gsStashDefinitions,
                        isSwizzled).PngBytes);
                }

                result = new DlDzoMobyExportResult(
                    null,
                    definition.ModelId,
                    ExportMoby(modelBytes, textures),
                    null);
            }
            catch (Exception ex) when (IsMobyExportFailure(ex))
            {
                result = new DlDzoMobyExportResult(
                    null,
                    definition.ModelId,
                    [],
                    ex.Message);
            }

            yield return result;
        }

        for (var missionIndex = 0; missionIndex < levelWad.GameplayMissionData.Count; missionIndex++)
        {
            var missionData = DlLevelWadReader.ReadSectorFileBlock(
                levelWadBytes,
                levelWad.GameplayMissionData[missionIndex]);
            var classes = DlMissionDataReader.ReadClasses(missionData);
            if (classes.Length == 0)
            {
                continue;
            }

            foreach (var moby in DlMissionMobyBankReader.Read(classes))
            {
                if (moby.ModelBytes.Length == 0)
                {
                    continue;
                }

                DlDzoMobyExportResult result;
                try
                {
                    var textures = moby.PifTextures
                        .Select(texture => PifAssetExporter.Export(texture).PngBytes)
                        .ToArray();
                    result = new DlDzoMobyExportResult(
                        missionIndex,
                        moby.Definition.ClassId,
                        ExportMoby(moby.ModelBytes, textures),
                        null);
                }
                catch (Exception ex) when (IsMobyExportFailure(ex))
                {
                    result = new DlDzoMobyExportResult(
                        missionIndex,
                        moby.Definition.ClassId,
                        [],
                        ex.Message);
                }

                yield return result;
            }
        }
    }

    public static byte[] ExportMoby(ReadOnlySpan<byte> modelBytes, IReadOnlyList<byte[]> pngTextures)
    {
        if (modelBytes.IsEmpty)
        {
            throw new ArgumentException("Moby model data cannot be empty.", nameof(modelBytes));
        }

        ArgumentNullException.ThrowIfNull(pngTextures);

        var textureUris = new Dictionary<int, string>(pngTextures.Count);
        var textureSizes = new Dictionary<int, TextureSize>(pngTextures.Count);
        var textureAlpha = new Dictionary<int, TextureAlphaInfo>(pngTextures.Count);
        var textureBytesByUri = new Dictionary<string, byte[]>(pngTextures.Count, StringComparer.Ordinal);
        for (var index = 0; index < pngTextures.Count; index++)
        {
            var pngBytes = pngTextures[index]
                ?? throw new ArgumentException("PNG texture list cannot contain null entries.", nameof(pngTextures));
            var uri = $"tex.{index:0000}.png";
            using var pngStream = new MemoryStream(pngBytes, writable: false);
            var metadata = PngTextureMetadataReader.ReadPng(pngStream);
            textureUris[index] = uri;
            textureSizes[index] = metadata.Size;
            textureAlpha[index] = metadata.Alpha;
            textureBytesByUri[uri] = pngBytes;
        }

        using var modelStream = new MemoryStream(modelBytes.ToArray(), writable: false);
        var options = new MobyGltfExportOptions
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            ExternalTextureUris = textureUris,
            ExternalTextureSizes = textureSizes,
            ExternalTextureAlpha = textureAlpha,
            BufferFileName = "moby.bin"
        };
        MobyGltfExport export;
        try
        {
            export = DlMobyGltfExporter.Export(modelStream, "moby.gltf", options);
        }
        catch (InvalidDataException)
        {
            modelStream.Position = 0;
            export = DlMobyGltfExporter.Export(
                modelStream,
                "moby.gltf",
                options with { SkipAnimationSequences = true });
        }

        return BuildGlb(export, textureBytesByUri);
    }

    private static byte[] BuildGlb(
        MobyGltfExport export,
        IReadOnlyDictionary<string, byte[]> textureBytesByUri)
    {
        var root = JsonNode.Parse(export.GltfBytes)?.AsObject()
            ?? throw new InvalidDataException("Moby exporter returned invalid glTF JSON.");
        if (root["buffers"] is not JsonArray buffers
            || buffers.Count != 1
            || buffers[0] is not JsonObject buffer
            || root["bufferViews"] is not JsonArray bufferViews)
        {
            throw new InvalidDataException("Moby exporter returned an unsupported glTF buffer layout.");
        }

        using var binStream = new MemoryStream();
        binStream.Write(export.BinBytes);
        if (root["images"] is JsonArray images)
        {
            foreach (var imageNode in images)
            {
                if (imageNode is not JsonObject image
                    || image["uri"]?.GetValue<string>() is not { } uri
                    || !textureBytesByUri.TryGetValue(uri, out var pngBytes))
                {
                    throw new InvalidDataException("Moby exporter returned an unknown external texture URI.");
                }

                Align(binStream, 4);
                var byteOffset = checked((int)binStream.Position);
                binStream.Write(pngBytes);
                image.Remove("uri");
                image["bufferView"] = bufferViews.Count;
                image["mimeType"] = "image/png";
                bufferViews.Add(new JsonObject
                {
                    ["buffer"] = 0,
                    ["byteOffset"] = byteOffset,
                    ["byteLength"] = pngBytes.Length
                });
            }
        }

        var binBytes = binStream.ToArray();
        buffer.Remove("uri");
        buffer["byteLength"] = binBytes.Length;
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(root);
        var jsonLength = AlignLength(jsonBytes.Length);
        var binLength = AlignLength(binBytes.Length);
        var totalLength = checked(12 + 8 + jsonLength + 8 + binLength);

        using var glbStream = new MemoryStream(totalLength);
        using var writer = new BinaryWriter(glbStream);
        writer.Write(GlbMagic);
        writer.Write(GlbVersion);
        writer.Write((uint)totalLength);
        writer.Write((uint)jsonLength);
        writer.Write(JsonChunkType);
        writer.Write(jsonBytes);
        WritePadding(writer, jsonLength - jsonBytes.Length, 0x20);
        writer.Write((uint)binLength);
        writer.Write(BinChunkType);
        writer.Write(binBytes);
        WritePadding(writer, binLength - binBytes.Length, 0x00);
        return glbStream.ToArray();
    }

    private static bool IsMobyExportFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException
            or OverflowException;
    }

    private static int AlignLength(int length)
    {
        return checked((length + 3) & ~3);
    }

    private static void Align(Stream stream, int alignment)
    {
        while (stream.Position % alignment != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WritePadding(BinaryWriter writer, int count, byte value)
    {
        for (var i = 0; i < count; i++)
        {
            writer.Write(value);
        }
    }
}

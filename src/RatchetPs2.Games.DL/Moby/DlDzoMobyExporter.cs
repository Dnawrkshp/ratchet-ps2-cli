using System.Text.Json;
using System.Text.Json.Nodes;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;
using RatchetPs2.Core.Textures;
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
    private static readonly string[] s_teamNames =
    [
        "Blue",
        "Red",
        "Green",
        "Orange",
        "Yellow",
        "Purple",
        "Aqua",
        "Pink",
        "Olive",
        "Maroon",
        "White"
    ];

    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static MobyGltfExport ExportGltf(
        MobyModel model,
        string gltfFileName = "moby.gltf",
        MobyDzoGltfExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        options ??= new MobyDzoGltfExportOptions();
        var jointCount = Math.Min(model.JointCount, model.Skeleton?.Bones.Count ?? 0);
        var inverseBindMatrices = model.Skeleton?.Bones
            .Take(jointCount)
            .Select(bone => DlMobyGltfExporter.DecodeInverseBindMatrix(bone, 1f / 1024f))
            .ToArray();
        var dzoOptions = options with
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            SkeletonParentMode = options.SkeletonParentMode == MobyGltfSkeletonParentMode.Auto
                ? MobyGltfSkeletonParentMode.SevenBitLow
                : options.SkeletonParentMode,
            HonorSkeletonParentRotationFlags = false,
            InverseBindMatrices = inverseBindMatrices,
            TextureFullOpacityAlpha = Ps2Color.FullOpacityAlpha
        };
        return MobyDzoGltfExporter.Export(model, gltfFileName, dzoOptions);
    }

    public static IEnumerable<DlDzoMobyExportResult> ExportLevel(
        byte[] levelWadBytes,
        bool flattenJointHierarchy = true,
        float nonOpaqueAlphaCoverageThreshold = MobyDzoGltfExportOptions.DefaultNonOpaqueAlphaCoverageThreshold)
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
                var textures = new List<PifTextureData>();
                var isSwizzled = !gsStashClassIds.Contains(definition.ModelId);
                foreach (var textureId in definition.TextureIds)
                {
                    if (textureId == 0xff || textureId >= textureDefinitions.Count)
                    {
                        continue;
                    }

                    var texture = DlAssetReader.BuildAssetTexture(
                        "moby",
                        textures.Count,
                        textureDefinitions[textureId],
                        paletteBytes,
                        assetBytes,
                        header.TextureDataOffset,
                        gsStashDefinitions,
                        isSwizzled);
                    textures.Add(PifReader.Read(texture.PifBytes));
                }

                result = new DlDzoMobyExportResult(
                    null,
                    definition.ModelId,
                    ExportMoby(modelBytes, textures, flattenJointHierarchy, nonOpaqueAlphaCoverageThreshold),
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
                        .Select(texture => PifAssetExporter.Export(texture).Texture)
                        .ToArray();
                    result = new DlDzoMobyExportResult(
                        missionIndex,
                        moby.Definition.ClassId,
                        ExportMoby(moby.ModelBytes, textures, flattenJointHierarchy, nonOpaqueAlphaCoverageThreshold),
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

    public static byte[] ExportMoby(
        ReadOnlySpan<byte> modelBytes,
        IReadOnlyList<byte[]> pngTextures,
        bool flattenJointHierarchy = true,
        float nonOpaqueAlphaCoverageThreshold = MobyDzoGltfExportOptions.DefaultNonOpaqueAlphaCoverageThreshold)
    {
        ArgumentNullException.ThrowIfNull(pngTextures);
        return ExportMobyCore(
            ReadModel(modelBytes),
            pngTextures.Select(png => CreateDzoTexture(png, null)).ToArray(),
            flattenJointHierarchy,
            nonOpaqueAlphaCoverageThreshold);
    }

    public static byte[] ExportMoby(
        ReadOnlySpan<byte> modelBytes,
        IReadOnlyList<PifTextureData> textures,
        bool flattenJointHierarchy = true,
        float nonOpaqueAlphaCoverageThreshold = MobyDzoGltfExportOptions.DefaultNonOpaqueAlphaCoverageThreshold)
    {
        ArgumentNullException.ThrowIfNull(textures);
        return ExportMobyCore(
            ReadModel(modelBytes),
            textures.Select(texture => CreateDzoTexture(
                ConvertPs2TextureToPng(texture),
                texture)).ToArray(),
            flattenJointHierarchy,
            nonOpaqueAlphaCoverageThreshold);
    }

    public static byte[] ExportMoby(
        MobyModel model,
        IReadOnlyList<PifTextureData> textures,
        bool flattenJointHierarchy = true,
        float nonOpaqueAlphaCoverageThreshold = MobyDzoGltfExportOptions.DefaultNonOpaqueAlphaCoverageThreshold)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(textures);
        return ExportMobyCore(
            model,
            textures.Select(texture => CreateDzoTexture(
                ConvertPs2TextureToPng(texture),
                texture)).ToArray(),
            flattenJointHierarchy,
            nonOpaqueAlphaCoverageThreshold);
    }

    private static byte[] ExportMobyCore(
        MobyModel model,
        IReadOnlyList<DzoTexture> sourceTextures,
        bool flattenJointHierarchy,
        float nonOpaqueAlphaCoverageThreshold)
    {
        var textureUris = new Dictionary<int, string>(sourceTextures.Count);
        var textureSizes = new Dictionary<int, TextureSize>(sourceTextures.Count);
        var textureAlpha = new Dictionary<int, TextureAlphaInfo>(sourceTextures.Count);
        var textureAlphaMaps = new Dictionary<int, MobyDzoGltfTextureAlphaMap>(sourceTextures.Count);
        var textureBytesByUri = new Dictionary<string, byte[]>(sourceTextures.Count, StringComparer.Ordinal);
        for (var index = 0; index < sourceTextures.Count; index++)
        {
            var pngBytes = sourceTextures[index].PngBytes
                ?? throw new ArgumentException("Texture list cannot contain null entries.", nameof(sourceTextures));
            var uri = $"tex.{index:0000}.png";
            using var pngStream = new MemoryStream(pngBytes, writable: false);
            var metadata = PngTextureMetadataReader.ReadPng(pngStream);
            textureUris[index] = uri;
            textureSizes[index] = metadata.Size;
            textureAlpha[index] = metadata.Alpha;
            textureAlphaMaps[index] = sourceTextures[index].AlphaMap;
            textureBytesByUri[uri] = pngBytes;
        }

        var teamTextures = BuildTeamTextures(model, sourceTextures, textureBytesByUri);
        var options = new MobyDzoGltfExportOptions
        {
            AnimationFormat = MobyAnimationFormat.Compact,
            FlattenJointHierarchy = flattenJointHierarchy,
            ExternalTextureUris = textureUris,
            ExternalTextureSizes = textureSizes,
            ExternalTextureAlpha = textureAlpha,
            ExternalTextureAlphaMaps = textureAlphaMaps,
            NonOpaqueAlphaCoverageThreshold = nonOpaqueAlphaCoverageThreshold,
            BufferFileName = "moby.bin"
        };
        var export = ExportGltf(model, "moby.gltf", options);

        return BuildGlb(export, textureBytesByUri, teamTextures);
    }

    private static MobyModel ReadModel(ReadOnlySpan<byte> modelBytes)
    {
        if (modelBytes.IsEmpty)
        {
            throw new ArgumentException("Moby model data cannot be empty.", nameof(modelBytes));
        }

        using var modelStream = new MemoryStream(modelBytes.ToArray(), writable: false);
        return MobyModelReader.Read(
            modelStream,
            new MobyModelReadOptions
            {
                AnimationFormat = MobyAnimationFormat.Compact,
                SkipAnimationSequences = true
            });
    }

    private static byte[] BuildGlb(
        MobyGltfExport export,
        IReadOnlyDictionary<string, byte[]> textureBytesByUri,
        IReadOnlyDictionary<int, IReadOnlyList<DzoTeamTexture>> teamTextures)
    {
        var root = JsonNode.Parse(export.GltfBytes)?.AsObject()
            ?? throw new InvalidDataException("Moby exporter returned invalid glTF JSON.");
        AddTeamMaterialVariants(root, teamTextures);
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

    private static IReadOnlyDictionary<int, IReadOnlyList<DzoTeamTexture>> BuildTeamTextures(
        MobyModel model,
        IReadOnlyList<DzoTexture> sourceTextures,
        IDictionary<string, byte[]> textureBytesByUri)
    {
        var result = new Dictionary<int, IReadOnlyList<DzoTeamTexture>>();
        foreach (var (textureId, palettes) in model.TeamPaletteData.OrderBy(pair => pair.Key))
        {
            if (textureId < 0
                || textureId >= sourceTextures.Count
                || sourceTextures[textureId].IndexedTexture is not { } sourceTexture)
            {
                continue;
            }

            var variants = new List<DzoTeamTexture>(Math.Min(palettes.Count, s_teamNames.Length));
            for (var teamId = 0; teamId < palettes.Count && teamId < s_teamNames.Length; teamId++)
            {
                var palette = palettes[teamId];
                if (palette.Length != sourceTexture.PaletteData.Length)
                {
                    continue;
                }

                var teamTexture = new PifTextureData(
                    sourceTexture.Header,
                    sourceTexture.Encoding,
                    palette,
                    sourceTexture.PixelData,
                    sourceTexture.MipPixelData);
                var pngBytes = ConvertPs2TextureToPng(teamTexture);
                var teamName = s_teamNames[teamId];
                var uri = $"tex.{textureId:0000}.team.{teamId:00}.{teamName.ToLowerInvariant()}.png";
                using var pngStream = new MemoryStream(pngBytes, writable: false);
                var metadata = PngTextureMetadataReader.ReadPng(pngStream);
                variants.Add(new DzoTeamTexture(teamId, teamName, uri, metadata.Size, metadata.Alpha));
                textureBytesByUri.Add(uri, pngBytes);
            }

            if (variants.Count > 0)
            {
                result.Add(textureId, variants);
            }
        }

        return result;
    }

    private static byte[] ConvertPs2TextureToPng(PifTextureData texture)
    {
        return TextureConverter.ConvertToPng(
            texture,
            new TextureConversionOptions { DoubleAlpha = true });
    }

    private static DzoTexture CreateDzoTexture(byte[] pngBytes, PifTextureData? indexedTexture)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        using var pngStream = new MemoryStream(pngBytes, writable: false);
        var image = PngTextureMetadataReader.ReadRgba32(pngStream);
        var alpha = new byte[checked(image.Width * image.Height)];
        for (var pixel = 0; pixel < alpha.Length; pixel++)
        {
            alpha[pixel] = image.PixelData[pixel * 4 + 3];
        }

        return new DzoTexture(
            pngBytes,
            indexedTexture,
            new MobyDzoGltfTextureAlphaMap(image.Width, image.Height, alpha));
    }

    private static void AddTeamMaterialVariants(
        JsonObject root,
        IReadOnlyDictionary<int, IReadOnlyList<DzoTeamTexture>> teamTextures)
    {
        if (teamTextures.Count == 0
            || root["materials"] is not JsonArray materials
            || root["images"] is not JsonArray images
            || root["textures"] is not JsonArray textures
            || root["meshes"] is not JsonArray meshes)
        {
            return;
        }

        var availableTeams = teamTextures.Values
            .SelectMany(variants => variants)
            .GroupBy(variant => variant.TeamId)
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .ToArray();
        var variantIndexByTeamId = availableTeams
            .Select((team, index) => (team.TeamId, Index: index))
            .ToDictionary(pair => pair.TeamId, pair => pair.Index);
        var usedMaterialIndices = meshes
            .OfType<JsonObject>()
            .SelectMany(mesh => (mesh["primitives"] as JsonArray)?.OfType<JsonObject>() ?? [])
            .Select(primitive => primitive["material"]?.GetValue<int>())
            .Where(index => index.HasValue)
            .Select(index => index!.Value)
            .ToHashSet();
        var baseMaterialCount = materials.Count;
        var usedMaterialCountByTextureId = Enumerable.Range(0, baseMaterialCount)
            .Where(usedMaterialIndices.Contains)
            .Select(index => materials[index] as JsonObject)
            .Where(material => material?["extras"]?["MobyTextureId"] is not null)
            .GroupBy(material => material!["extras"]!["MobyTextureId"]!.GetValue<int>())
            .ToDictionary(group => group.Key, group => group.Count());
        var variantMaterialsByBaseMaterial = new Dictionary<int, JsonArray>();

        for (var materialIndex = 0; materialIndex < baseMaterialCount; materialIndex++)
        {
            if (!usedMaterialIndices.Contains(materialIndex)
                || materials[materialIndex] is not JsonObject baseMaterial
                || baseMaterial["extras"]?["MobyTextureId"]?.GetValue<int>() is not { } textureId
                || !teamTextures.TryGetValue(textureId, out var variants))
            {
                continue;
            }

            var mappings = new JsonArray();
            foreach (var variant in variants)
            {
                var imageIndex = images.Count;
                images.Add(new JsonObject
                {
                    ["name"] = $"tex_{textureId:0000}_{variant.Name}",
                    ["uri"] = variant.Uri
                });
                var textureIndex = textures.Count;
                textures.Add(new JsonObject { ["source"] = imageIndex });

                var material = baseMaterial.DeepClone().AsObject();
                var alphaPass = material["extras"]?["MobyAlphaPass"]?.GetValue<string>();
                var materialName = teamTextures.Count == 1
                    ? variant.Name
                    : $"{variant.Name}_tex_{textureId:0000}";
                if (alphaPass is not null
                    && usedMaterialCountByTextureId.TryGetValue(textureId, out var passCount)
                    && passCount > 1)
                {
                    materialName += $"_{alphaPass.ToLowerInvariant()}";
                }
                material["name"] = materialName;
                if (material["pbrMetallicRoughness"] is JsonObject pbr
                    && pbr["baseColorTexture"] is JsonObject baseColorTexture)
                {
                    baseColorTexture["index"] = textureIndex;
                }
                if (material["emissiveTexture"] is JsonObject emissiveTexture)
                {
                    emissiveTexture["index"] = textureIndex;
                }
                if (material["extras"] is JsonObject extras)
                {
                    extras["MobyTeamId"] = variant.TeamId;
                    extras["MobyTeamName"] = variant.Name;
                    extras["MobyTextureUri"] = variant.Uri;
                    extras["TextureWidth"] = variant.Size.Width;
                    extras["TextureHeight"] = variant.Size.Height;
                    extras["HasAlpha"] = variant.Alpha.HasAlpha;
                    extras["AlphaMode"] = variant.Alpha.AlphaMode.ToString();
                    extras["GltfAlphaMode"] = variant.Alpha.GltfAlphaMode;
                    extras["MinAlpha"] = variant.Alpha.MinAlpha;
                    extras["MaxAlpha"] = variant.Alpha.MaxAlpha;
                    extras["UsesBinaryAlpha"] = variant.Alpha.UsesBinaryAlpha;
                }

                var teamMaterialIndex = materials.Count;
                materials.Add(material);
                mappings.Add(new JsonObject
                {
                    ["material"] = teamMaterialIndex,
                    ["variants"] = new JsonArray(variantIndexByTeamId[variant.TeamId])
                });
            }

            variantMaterialsByBaseMaterial.Add(materialIndex, mappings);
        }

        if (variantMaterialsByBaseMaterial.Count == 0)
        {
            return;
        }

        foreach (var primitive in meshes
                     .OfType<JsonObject>()
                     .SelectMany(mesh => (mesh["primitives"] as JsonArray)?.OfType<JsonObject>() ?? []))
        {
            if (primitive["material"]?.GetValue<int>() is not { } materialIndex
                || !variantMaterialsByBaseMaterial.TryGetValue(materialIndex, out var mappings))
            {
                continue;
            }

            var extensions = primitive["extensions"] as JsonObject ?? new JsonObject();
            primitive["extensions"] = extensions;
            extensions["KHR_materials_variants"] = new JsonObject
            {
                ["mappings"] = mappings.DeepClone()
            };
        }

        var extensionsUsed = root["extensionsUsed"] as JsonArray ?? new JsonArray();
        root["extensionsUsed"] = extensionsUsed;
        if (!extensionsUsed.Any(node => node?.GetValue<string>() == "KHR_materials_variants"))
        {
            extensionsUsed.Add("KHR_materials_variants");
        }

        var rootExtensions = root["extensions"] as JsonObject ?? new JsonObject();
        root["extensions"] = rootExtensions;
        rootExtensions["KHR_materials_variants"] = new JsonObject
        {
            ["variants"] = new JsonArray(availableTeams
                .Select(team => (JsonNode)new JsonObject { ["name"] = team.Name })
                .ToArray())
        };
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

    private sealed record DzoTexture(
        byte[] PngBytes,
        PifTextureData? IndexedTexture,
        MobyDzoGltfTextureAlphaMap AlphaMap);

    private sealed record DzoTeamTexture(
        int TeamId,
        string Name,
        string Uri,
        TextureSize Size,
        TextureAlphaInfo Alpha);
}

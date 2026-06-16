using RatchetPs2.Core.Textures;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static void ExtractHudBanks(
        string mapOutputDirectory,
        string outputDirectory,
        byte[] hudHeaderBytes,
        IReadOnlyList<byte[]> bankPayloads,
        IDictionary<string, object?> rootManifest)
    {
        CleanLegacySpriteArtifacts(Path.Combine(mapOutputDirectory, "sprites"));
        CleanOldHudArtifacts(outputDirectory);

        var hud = DlHudBankReader.Read(hudHeaderBytes, bankPayloads);
        var normalizedTextures = new List<object>(hud.Frames.Count);
        var writtenTextureCount = 0;

        foreach (var frame in hud.Frames)
        {
            var hasTexture = DlHudBankReader.TryGetTexture(hud, frame.TextureIndex, out var texture);
            var hasPalette = DlHudBankReader.TryGetPalette(hud, frame.PaletteIndex, out var palette);
            var relativeDirectory = hasTexture ? $"bank_{texture.BankIndex}" : "unknown";
            var baseName = $"tex.{frame.Index:0000}";
            string? pifPath = null;
            string? pngPath = null;
            var status = "skipped";
            string? note = null;

            if (hasTexture && hasPalette)
            {
                try
                {
                    var pifTexture = PifWriter.CreateIndexed8(
                        texture.Width,
                        texture.Height,
                        palette.PaletteBytes,
                        texture.PixelBytes,
                        isSwizzled: false);
                    var directory = CreateDirectory(outputDirectory, relativeDirectory);
                    pifPath = $"{relativeDirectory}/{baseName}.pif";
                    pngPath = $"{relativeDirectory}/{baseName}.png";
                    File.WriteAllBytes(Path.Combine(directory, $"{baseName}.pif"), PifWriter.Write(pifTexture));
                    File.WriteAllBytes(
                        Path.Combine(directory, $"{baseName}.png"),
                        TextureConverter.ConvertToPng(pifTexture, TexturePixelFormat.Rgba32));
                    status = "written";
                    writtenTextureCount++;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidDataException or NotSupportedException)
                {
                    status = "error";
                    note = ex.Message;
                }
            }
            else
            {
                note = hasTexture
                    ? $"palette index {frame.PaletteIndex} is missing or out of range"
                    : $"texture index {frame.TextureIndex} is missing or out of range";
            }

            normalizedTextures.Add(new
            {
                FrameIndex = frame.Index,
                frame.PaletteIndex,
                frame.TextureIndex,
                TextureBank = hasTexture ? texture.BankIndex : (int?)null,
                PaletteBank = hasPalette ? palette.BankIndex : (int?)null,
                Status = status,
                Note = note,
                PifPath = pifPath,
                PngPath = pngPath,
                Texture = hasTexture
                    ? new
                    {
                        texture.Index,
                        texture.BankIndex,
                        texture.Offset,
                        texture.EncodedOffset,
                        texture.GsRam,
                        texture.ULog,
                        texture.VLog,
                        texture.Width,
                        texture.Height,
                        texture.PixelLength,
                        texture.IsLengthValid
                    }
                    : null,
                Palette = hasPalette
                    ? new
                    {
                        palette.Index,
                        palette.BankIndex,
                        palette.Offset,
                        palette.EncodedOffset,
                        palette.GsRam,
                        palette.Padding,
                        palette.IsLengthValid
                    }
                    : null
            });
        }

        var bankRoutes = Enumerable.Range(0, DlHudBankReader.BankCount)
            .Select(index => new
            {
                BankIndex = index,
                Length = index < bankPayloads.Count ? bankPayloads[index].Length : 0,
                DeclaredDecompressedSize = index < hud.Header.BankSizes.Count ? hud.Header.BankSizes[index] : 0,
                PaletteStart = GetCumulativeStart(hud.Header.PaletteCumulativeCounts, index),
                PaletteEnd = GetCumulativeEnd(hud.Header.PaletteCumulativeCounts, index),
                TextureStart = GetCumulativeStart(hud.Header.TextureCumulativeCounts, index),
                TextureEnd = GetCumulativeEnd(hud.Header.TextureCumulativeCounts, index)
            })
            .ToArray();
        var hudManifest = new
        {
            OmittedRawPayloads = new[]
            {
                new
                {
                    Path = "hud_header.bin",
                    Replacement = "manifest Header plus icon/frame/palette/texture table metadata",
                    Reason = "The HUD header is represented by parsed fields and table entries verified against LoadHudBanks and LinkHudBank."
                },
                new
                {
                    Path = "hud_bank_*.wad",
                    Replacement = "bank_*/tex.*.pif + bank_*/tex.*.png plus manifest bank/table metadata",
                    Reason = "HUD bank texture and palette payloads are represented by normalized frame texture previews and source metadata."
                }
            },
            SourceNotes = new[]
            {
                "LoadHudBanks copies the header from core slot 0x20 and treats slots 0x28, 0x30, 0x38, 0x40, and 0x48 as HUD banks 0..4.",
                "LinkHudBank relocates palette and texture record offsets by clearing the high bit and adding aligned bank RAM.",
                "GetFrameTex indexes frame records as two signed shorts: palette index and texture index."
            },
            Header = hud.Header,
            Banks = bankRoutes,
            Icons = hud.Icons,
            Frames = hud.Frames,
            Palettes = hud.Palettes.Select(palette => new
            {
                palette.Index,
                palette.BankIndex,
                palette.EncodedOffset,
                palette.Offset,
                palette.GsRam,
                palette.Padding,
                palette.IsLengthValid,
                Length = palette.PaletteBytes.Length
            }),
            Textures = hud.Textures.Select(texture => new
            {
                texture.Index,
                texture.BankIndex,
                texture.EncodedOffset,
                texture.Offset,
                texture.GsRam,
                texture.ULog,
                texture.VLog,
                texture.Width,
                texture.Height,
                texture.PixelLength,
                texture.IsLengthValid,
                Length = texture.PixelBytes.Length
            }),
            NormalizedFrameTextures = normalizedTextures
        };

        WriteJson(Path.Combine(outputDirectory, "manifest.json"), hudManifest);
        rootManifest["Hud"] = hudManifest;
        rootManifest["HudFrameTextureCount"] = writtenTextureCount;
        AddOmittedRawPayload(
            rootManifest,
            "hud/hud_header.bin",
            "hud/manifest.json",
            "The HUD header is represented by parsed header fields and source table metadata.");
        AddOmittedRawPayload(
            rootManifest,
            "hud/hud_bank_*.wad",
            "hud/bank_*/tex.*.pif + hud/bank_*/tex.*.png + hud/manifest.json",
            "HUD bank texture and palette payloads are represented by normalized frame texture artifacts and manifest metadata.");
    }

    private static IReadOnlyList<byte[]> GetHudBankPayloads(IReadOnlyDictionary<int, DlCoreLevelSegment> coreSegmentByHeaderOffset)
    {
        var banks = new byte[DlHudBankReader.BankCount][];
        for (var i = 0; i < banks.Length; i++)
        {
            var headerOffset = 0x28 + (i * 8);
            banks[i] = TryGetCoreSegment(coreSegmentByHeaderOffset, headerOffset, out var segment)
                ? segment.PayloadBytes
                : [];
        }

        return banks;
    }

    private static int GetCumulativeStart(IReadOnlyList<int> cumulativeCounts, int index)
    {
        return index <= 0 || index > cumulativeCounts.Count ? 0 : cumulativeCounts[index - 1];
    }

    private static int GetCumulativeEnd(IReadOnlyList<int> cumulativeCounts, int index)
    {
        return index < cumulativeCounts.Count ? cumulativeCounts[index] : 0;
    }

    private static void CleanOldHudArtifacts(string outputDirectory)
    {
        DeleteIfExists(Path.Combine(outputDirectory, "hud_header.bin"));
        DeleteMatchingFiles(outputDirectory, "hud_bank_*.wad");

        for (var i = 0; i < DlHudBankReader.BankCount; i++)
        {
            DeleteMatchingFiles(Path.Combine(outputDirectory, $"bank_{i}"), "tex.*.pif", "tex.*.png");
        }
    }

    private static void CleanLegacySpriteArtifacts(string outputDirectory)
    {
        DeleteIfExists(Path.Combine(outputDirectory, "manifest.json"));
        DeleteIfExists(Path.Combine(outputDirectory, "sprite_header.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "sprite_wad_1.wad"));
        DeleteIfExists(Path.Combine(outputDirectory, "sprite_wad_2.wad"));
        DeleteIfExists(Path.Combine(outputDirectory, "hudwad_c3.wad"));
        DeleteIfExists(Path.Combine(outputDirectory, "hudwad_c4.wad"));
        DeleteIfExists(Path.Combine(outputDirectory, "hudwad_c5.wad"));
        DeleteMatchingFiles(Path.Combine(outputDirectory, "sprite1"), "tex.*.pif", "tex.*.png", "*.bin", "*.palette", "*.def");
        DeleteMatchingFiles(Path.Combine(outputDirectory, "sprite2"), "tex.*.pif", "tex.*.png", "*.bin", "*.palette", "*.def");
        TryDeleteEmptyDirectory(Path.Combine(outputDirectory, "sprite1"));
        TryDeleteEmptyDirectory(Path.Combine(outputDirectory, "sprite2"));
        TryDeleteEmptyDirectory(outputDirectory);
    }

    private static void CleanLegacyCoreSegmentArtifacts(string outputDirectory)
    {
        var coreSegmentsDirectory = Path.Combine(outputDirectory, "core_segments");
        var rawDirectory = Path.Combine(coreSegmentsDirectory, "raw");

        DeleteIfExists(Path.Combine(coreSegmentsDirectory, "manifest.json"));
        DeleteMatchingFiles(rawDirectory, "*.bin");
        TryDeleteEmptyDirectory(rawDirectory);
        TryDeleteEmptyDirectory(coreSegmentsDirectory);
    }

    private static void CleanOldTfragArtifacts(string outputDirectory)
    {
        DeleteIfExists(Path.Combine(outputDirectory, "tfrag.bin"));
        DeleteMatchingFiles(Path.Combine(outputDirectory, "textures"), "tex.*.pif", "tex.*.png");
    }

    private static void CleanLegacyTerrainArtifacts(string outputDirectory)
    {
        var terrainDirectory = Path.Combine(outputDirectory, "terrain");
        var textureDirectory = Path.Combine(terrainDirectory, "textures");

        DeleteIfExists(Path.Combine(terrainDirectory, "terrain.bin"));
        DeleteMatchingFiles(textureDirectory, "tex.*.pif", "tex.*.png", "*.bin", "*.palette", "*.def");
        TryDeleteEmptyDirectory(textureDirectory);
        TryDeleteEmptyDirectory(terrainDirectory);
    }

    private static void CleanLegacyAssetRootArtifacts(string outputDirectory)
    {
        DeleteIfExists(Path.Combine(outputDirectory, "asset_header.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "particle_def.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "sound_remap.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "moby_sound_remap.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "moby_gs_stash_list.bin"));
        DeleteIfExists(Path.Combine(outputDirectory, "sky.bin"));
        CleanOldAssetHeaderArtifacts(outputDirectory);
    }

    private static void CleanOldAssetHeaderArtifacts(string outputDirectory)
    {
        var headerDirectory = Path.Combine(outputDirectory, "header");
        var tablesDirectory = Path.Combine(headerDirectory, "tables");

        DeleteIfExists(Path.Combine(headerDirectory, "fixed.bin"));
        DeleteMatchingFiles(tablesDirectory, "*.bin");
        TryDeleteEmptyDirectory(tablesDirectory);
        TryDeleteEmptyDirectory(headerDirectory);
    }

    private static void CleanLegacyTableHeaderArtifacts(string outputDirectory, params string[] fileNames)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return;
        }

        foreach (var fileName in fileNames)
        {
            DeleteIfExists(Path.Combine(outputDirectory, fileName));
        }
    }
}

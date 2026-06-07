using RatchetPs2.Cli.Abstractions;
using RatchetPs2.Cli.GameSelection;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Ties;
using System.Diagnostics;
using System.Text.Json;
using System.CommandLine;

namespace RatchetPs2.Cli.Commands.Tie;

internal static class TieExportGltfBatchCommand
{
    public static Command Build()
    {
        var gameOption = CommonOptions.Game();
        var inputRootOption = new Option<DirectoryInfo>("--input-root")
        {
            Description = "Root directory to scan recursively for tie core.bin files.",
            Required = true
        };
        var outputRootOption = new Option<DirectoryInfo>("--output-root")
        {
            Description = "Directory to write exported glTFs and the viewer manifest.",
            Required = true
        };
        var coreFileNameOption = new Option<string>("--core-file-name")
        {
            Description = "Tie class binary file name to scan for. Defaults to core.bin.",
            DefaultValueFactory = _ => "core.bin"
        };
        var manifestNameOption = new Option<string>("--manifest-name")
        {
            Description = "Viewer manifest file name. Defaults to manifest.json.",
            DefaultValueFactory = _ => "manifest.json"
        };
        var lodOption = new Option<int>("--lod")
        {
            Description = "Tie LOD packet group to export for every model: 0, 1, or 2. Defaults to 0."
        };
        var limitOption = new Option<int?>("--limit")
        {
            Description = "Optional maximum number of ties to export."
        };

        var command = CliCommandBuilder.Create(
            "export-gltf-batch",
            "Export a directory of tie class binaries to glTF and write a viewer manifest.",
            gameOption,
            inputRootOption,
            outputRootOption,
            coreFileNameOption,
            manifestNameOption,
            lodOption,
            limitOption);

        command.SetAction(parseResult =>
        {
            var gameValue = parseResult.GetValue(gameOption);
            var inputRoot = parseResult.GetValue(inputRootOption);
            var outputRoot = parseResult.GetValue(outputRootOption);
            var coreFileName = parseResult.GetValue(coreFileNameOption);
            var manifestName = parseResult.GetValue(manifestNameOption);
            var lodIndex = parseResult.GetValue(lodOption);
            var limit = parseResult.GetValue(limitOption);

            if (string.IsNullOrWhiteSpace(gameValue) || !GameIdParser.TryParse(gameValue, out var gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Unsupported --game value '{gameValue}'. Expected {TieGameFormats.SupportedTieGames} for tie glTF batch export.");
                return;
            }

            if (!TieGameFormats.IsSupported(gameId))
            {
                parseResult.GetResult(gameOption)?.AddError(
                    $"Tie glTF batch export currently supports only {TieGameFormats.SupportedTieGames}. Received {gameId}.");
                return;
            }

            if (inputRoot is null)
            {
                parseResult.GetResult(inputRootOption)?.AddError("Missing required --input-root option.");
                return;
            }

            if (outputRoot is null)
            {
                parseResult.GetResult(outputRootOption)?.AddError("Missing required --output-root option.");
                return;
            }

            if (!inputRoot.Exists)
            {
                parseResult.GetResult(inputRootOption)?.AddError(
                    $"Input root '{inputRoot.FullName}' does not exist.");
                return;
            }

            if (string.IsNullOrWhiteSpace(coreFileName))
            {
                parseResult.GetResult(coreFileNameOption)?.AddError("--core-file-name cannot be empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(manifestName))
            {
                parseResult.GetResult(manifestNameOption)?.AddError("--manifest-name cannot be empty.");
                return;
            }

            if (lodIndex is < 0 or > 2)
            {
                parseResult.GetResult(lodOption)?.AddError(
                    $"Unsupported --lod value '{lodIndex}'. Expected 0, 1, or 2.");
                return;
            }

            if (limit is <= 0)
            {
                parseResult.GetResult(limitOption)?.AddError("--limit must be greater than zero when supplied.");
                return;
            }

            outputRoot.Create();
            var manifestFile = new FileInfo(Path.Combine(outputRoot.FullName, manifestName));
            var coreFiles = inputRoot
                .EnumerateFiles(coreFileName, SearchOption.AllDirectories)
                .OrderBy(file => Path.GetRelativePath(inputRoot.FullName, file.FullName), StringComparer.Ordinal)
                .Take(limit ?? int.MaxValue)
                .ToArray();

            var manifestDirectory = manifestFile.Directory ?? outputRoot;
            var entries = new List<TieBatchManifestEntry>(coreFiles.Length);
            var totalStopwatch = Stopwatch.StartNew();
            var successfulDurations = new List<double>();
            var totalInputBytes = 0L;
            var totalOutputBytes = 0L;
            var succeeded = 0;
            var failed = 0;

            for (var i = 0; i < coreFiles.Length; i++)
            {
                var coreFile = coreFiles[i];
                var relativeSourceDirectory = Path.GetRelativePath(inputRoot.FullName, coreFile.DirectoryName ?? inputRoot.FullName);
                var sourceDirectoryName = coreFile.Directory?.Name ?? $"tie_{i:0000}";
                var outputDirectory = new DirectoryInfo(Path.Combine(outputRoot.FullName, relativeSourceDirectory));
                outputDirectory.Create();

                var gltfFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tie.gltf"));
                var bufferFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tie.buffer.bin"));
                var diagnosticsFile = new FileInfo(Path.Combine(outputDirectory.FullName, "tie.diagnostics.json"));
                var itemStopwatch = Stopwatch.StartNew();

                try
                {
                    var gameProfile = TieGameProfile.ForGame(gameId);
                    using var input = coreFile.OpenRead();
                    var tie = TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(gameProfile));
                    var textureResources = TieTextureResourcePreparer.PrepareExternalTextures(coreFile.Directory, outputDirectory);
                    var export = TieGltfExporter.Export(
                        tie,
                        gltfFile.Name,
                        new TieGltfExportOptions
                        {
                            LodIndex = lodIndex,
                            BufferFileName = bufferFile.Name,
                            GameProfile = gameProfile,
                            ExternalTextureUris = textureResources?.Uris,
                            ExternalTextureSizes = textureResources?.Sizes,
                            ExternalTextureAlpha = textureResources?.Alpha
                        });

                    File.WriteAllBytes(gltfFile.FullName, export.GltfBytes);
                    File.WriteAllBytes(bufferFile.FullName, export.BinBytes);
                    File.WriteAllBytes(diagnosticsFile.FullName, export.DiagnosticsBytes);
                    itemStopwatch.Stop();

                    succeeded++;
                    successfulDurations.Add(itemStopwatch.Elapsed.TotalMilliseconds);
                    totalInputBytes += coreFile.Length;
                    totalOutputBytes += export.GltfBytes.Length + export.BinBytes.Length + export.DiagnosticsBytes.Length;
                    entries.Add(BuildSuccessEntry(
                        sourceDirectoryName,
                        coreFile,
                        gltfFile,
                        bufferFile,
                        diagnosticsFile,
                        manifestDirectory,
                        relativeSourceDirectory,
                        tie,
                        lodIndex,
                        gameId,
                        textureResources,
                        itemStopwatch.Elapsed.TotalMilliseconds,
                        export));
                }
                catch (Exception ex)
                {
                    itemStopwatch.Stop();
                    failed++;
                    entries.Add(new TieBatchManifestEntry(
                        new
                        {
                            Id = sourceDirectoryName,
                            SourceDirectory = TieTextureResourcePreparer.ToGltfUri(relativeSourceDirectory),
                            SourceCore = TieTextureResourcePreparer.ToGltfUri(Path.GetRelativePath(manifestDirectory.FullName, coreFile.FullName)),
                            Status = "failed",
                            ConversionMs = itemStopwatch.Elapsed.TotalMilliseconds,
                            Error = ex.Message
                        },
                        PacketMetadata: null));
                }

                if ((i + 1) % 50 == 0 || i + 1 == coreFiles.Length)
                {
                    Console.WriteLine(
                        $"Processed {i + 1}/{coreFiles.Length} ties ({succeeded} ok, {failed} failed) in {totalStopwatch.Elapsed.TotalSeconds:F1}s.");
                }
            }

            totalStopwatch.Stop();
            var manifest = new
            {
                Format = "ratchet-ps2-tie-viewer-manifest-v1",
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                Game = gameId.ToString(),
                LodIndex = lodIndex,
                SourceRoot = TieTextureResourcePreparer.ToGltfUri(inputRoot.FullName),
                OutputRoot = TieTextureResourcePreparer.ToGltfUri(outputRoot.FullName),
                CoreFileName = coreFileName,
                Totals = new
                {
                    Found = coreFiles.Length,
                    Succeeded = succeeded,
                    Failed = failed,
                    TotalMs = totalStopwatch.Elapsed.TotalMilliseconds,
                    AverageSuccessMs = successfulDurations.Count == 0 ? 0 : successfulDurations.Average(),
                    MedianSuccessMs = Median(successfulDurations),
                    TotalInputBytes = totalInputBytes,
                    TotalOutputBytes = totalOutputBytes
                },
                PacketMetadata = BuildAggregatePacketMetadata(entries),
                Entries = entries.Select(entry => entry.Entry).ToArray()
            };

            File.WriteAllBytes(
                manifestFile.FullName,
                JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Console.WriteLine(
                $"Wrote tie batch manifest '{manifestFile.FullName}' with {succeeded} successful export(s), {failed} failure(s), total {totalStopwatch.Elapsed.TotalSeconds:F2}s.");
        });

        return command;
    }

    private static TieBatchManifestEntry BuildSuccessEntry(
        string id,
        FileInfo coreFile,
        FileInfo gltfFile,
        FileInfo bufferFile,
        FileInfo diagnosticsFile,
        DirectoryInfo manifestDirectory,
        string relativeSourceDirectory,
        TieClass tie,
        int lodIndex,
        GameId gameId,
        TieTextureResources? textureResources,
        double conversionMs,
        TieGltfExport export)
    {
        var packetTable = tie.PacketTables.FirstOrDefault(table => table.LodIndex == lodIndex);
        var topology = tie.LodTopologies.FirstOrDefault(topology => topology.LodIndex == lodIndex);
        var packets = packetTable?.Packets ?? [];
        var shaderCountDistribution = Distribution(packets.Select(packet => (int)packet.ShaderCount));
        var multipassTypeDistribution = Distribution(packets.Select(packet => (int)packet.MultipassType));
        var setupTailWords = packets
            .Select(packet => GetSetupTailWord(tie, lodIndex, packet.PacketIndex))
            .Where(value => value.HasValue)
            .Select(value => $"0x{unchecked((uint)value!.Value):X8}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var populatedLods = tie.Header.Lods
            .Select((lod, index) => new
            {
                LodIndex = index,
                lod.VertexCount,
                lod.TriangleCount,
                lod.StripCount,
                PacketCount = tie.Header.PacketCounts[index],
                CacheSize = tie.Header.CacheSizes[index]
            })
            .Where(lod => lod.VertexCount > 0 || lod.TriangleCount > 0 || lod.StripCount > 0 || lod.PacketCount > 0)
            .ToArray();

        return new TieBatchManifestEntry(
            new
            {
                Id = id,
                Label = $"{id} / 0x{(ushort)tie.Header.OClass:X4}",
                Game = gameId.ToString(),
                Status = "ok",
                SourceDirectory = TieTextureResourcePreparer.ToGltfUri(relativeSourceDirectory),
                SourceCore = TieTextureResourcePreparer.ToGltfUri(Path.GetRelativePath(manifestDirectory.FullName, coreFile.FullName)),
                Gltf = TieTextureResourcePreparer.ToGltfUri(Path.GetRelativePath(manifestDirectory.FullName, gltfFile.FullName)),
                Buffer = TieTextureResourcePreparer.ToGltfUri(Path.GetRelativePath(manifestDirectory.FullName, bufferFile.FullName)),
                Diagnostics = TieTextureResourcePreparer.ToGltfUri(Path.GetRelativePath(manifestDirectory.FullName, diagnosticsFile.FullName)),
                ConversionMs = conversionMs,
                InputBytes = coreFile.Length,
                OutputBytes = export.GltfBytes.Length + export.BinBytes.Length + export.DiagnosticsBytes.Length,
                Header = new
                {
                    OClass = $"0x{(ushort)tie.Header.OClass:X4}",
                    TClass = $"0x{(ushort)tie.Header.TClass:X4}",
                    tie.Header.TextureCount,
                    ModeBits = $"0x{(ushort)tie.Header.ModeBits:X4}",
                    GlowRgba = $"0x{unchecked((uint)tie.Header.GlowRgba):X8}",
                    tie.Header.Scale,
                    tie.Header.MipmapDistance,
                    BoundingRadius = tie.Header.BoundingSphere.Radius,
                    AmbientRgbaOffset = tie.Header.AmbientRgbaOffset == 0 ? "none" : $"0x{tie.Header.AmbientRgbaOffset:X}",
                    tie.Header.AmbientSize,
                    tie.Header.InstanceIndex,
                    tie.Header.InstanceCount
                },
                Lods = populatedLods,
                Geometry = new
                {
                    VertexCount = topology?.LogicalVertexCount ?? 0,
                    TriangleCount = topology?.TriangleCount ?? 0,
                    StripCount = topology?.StripCount ?? 0,
                    PacketVertexRowCount = topology?.PacketVertexRowCount ?? 0,
                    UnresolvedLogicalVertexCount = topology?.UnresolvedLogicalVertexCount ?? 0
                },
                Packets = new
                {
                    Count = packets.Count,
                    RgbaSlotCount = packets.Sum(packet => packet.RgbaCount),
                    RgbaPacketCount = packets.Count(packet => packet.RgbaCount > 0),
                    MaxShaderCount = packets.Count == 0 ? 0 : packets.Max(packet => packet.ShaderCount),
                    ShaderCountDistribution = shaderCountDistribution,
                    MultipassTypeDistribution = multipassTypeDistribution,
                    MultipassPacketCount = packets.Count(packet => packet.MultipassType != 0),
                    SetupTailWords = setupTailWords
                },
                Glow = new
                {
                    RemapCount = tie.GlowRgbaRemaps.Count,
                    VertexCount = tie.GlowRgbaVertices.Count,
                    ResolvedPacketCount = tie.GlowRgbaVertices
                        .Where(vertex => vertex.LodIndex == lodIndex)
                        .Select(vertex => vertex.PacketIndex)
                        .Distinct()
                        .Count()
                },
                Textures = textureResources?.Entries ?? []
            },
            new TieBatchPacketMetadata(
                multipassTypeDistribution,
                shaderCountDistribution,
                setupTailWords));
    }

    private static object BuildAggregatePacketMetadata(IEnumerable<TieBatchManifestEntry> entries)
    {
        var multipassTypes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var setupTailWords = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var maxShaderCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.PacketMetadata is not { } packetMetadata)
            {
                continue;
            }

            AddDistribution(multipassTypes, packetMetadata.MultipassTypeDistribution);
            AddDistribution(maxShaderCounts, packetMetadata.ShaderCountDistribution);
            foreach (var word in packetMetadata.SetupTailWords)
            {
                setupTailWords[word] = setupTailWords.GetValueOrDefault(word) + 1;
            }
        }

        return new
        {
            MultipassTypes = multipassTypes,
            ShaderCounts = maxShaderCounts,
            SetupTailWords = setupTailWords
        };
    }

    private static void AddDistribution(IDictionary<string, int> target, IReadOnlyDictionary<string, int> distribution)
    {
        foreach (var (key, count) in distribution)
        {
            target[key] = (target.TryGetValue(key, out var existingCount) ? existingCount : 0)
                + count;
        }
    }

    private static int? GetSetupTailWord(TieClass tie, int lodIndex, int packetIndex)
    {
        return tie.PacketDataBlocks
            .FirstOrDefault(block => block.LodIndex == lodIndex && block.PacketIndex == packetIndex)?
            .SetupRows
            .FirstOrDefault(row => row.Index == 0)?
            .Words
            .FirstOrDefault(word => word.WordIndex == 3)?
            .Raw;
    }

    private static SortedDictionary<string, int> Distribution(IEnumerable<int> values)
    {
        var distribution = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = value.ToString();
            distribution[key] = distribution.GetValueOrDefault(key) + 1;
        }

        return distribution;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var midpoint = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[midpoint - 1] + sorted[midpoint]) / 2
            : sorted[midpoint];
    }

    private sealed record TieBatchManifestEntry(object Entry, TieBatchPacketMetadata? PacketMetadata);

    private sealed record TieBatchPacketMetadata(
        IReadOnlyDictionary<string, int> MultipassTypeDistribution,
        IReadOnlyDictionary<string, int> ShaderCountDistribution,
        IReadOnlyList<string> SetupTailWords);
}

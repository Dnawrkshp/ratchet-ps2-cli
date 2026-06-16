using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static IReadOnlyList<DlCoreLevelSegment> ReadCoreSegments(
        byte[] coreLevelBytes,
        IDictionary<string, object?> manifest)
    {
        var segments = DlCoreLevelSegmentReader.Read(coreLevelBytes);

        var segmentManifest = CreateCoreSegmentManifest(segments);
        manifest["CoreLevelLength"] = coreLevelBytes.Length;
        manifest["CoreLevelSegmentTableLength"] = DlLevelConstants.CoreLevelSegmentTableLength;
        manifest["CoreSegments"] = segmentManifest;
        return segments;
    }

    private static object[] CreateCoreSegmentManifest(IReadOnlyList<DlCoreLevelSegment> segments)
    {
        return segments.Select(segment => new
        {
            segment.Index,
            segment.HeaderOffset,
            segment.Offset,
            segment.Length,
            segment.Name,
            segment.SemanticName,
            segment.WasCompressedWad,
            segment.OutputExtension,
            RawLength = segment.RawBytes.Length,
            PayloadLength = segment.PayloadBytes.Length
        }).ToArray<object>();
    }

    private static IReadOnlyList<CorePayloadRoute> ExtractCorePayloads(
        string outputRoot,
        IReadOnlyList<DlCoreLevelSegment> segments)
    {
        var routes = new List<CorePayloadRoute>(segments.Count);
        foreach (var segment in segments)
        {
            routes.Add(segment.HeaderOffset switch
            {
                0x00 => WriteCorePayload(outputRoot, segment, "core_pvars/moby8355_pvars.bin"),
                0x08 => HandledCorePayload(segment, "code/manifest.json", "code patch extraction"),
                0x10 => HandledCorePayload(segment, "assets/manifest.json", "asset header extraction"),
                0x18 => HandledCorePayload(segment, "assets/**/tex.*.pif", "normalized texture extraction"),
                0x20 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x28 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x30 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x38 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x40 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x48 => HandledCorePayload(segment, "hud/manifest.json", "HUD bank extraction"),
                0x50 => HandledCorePayload(segment, "assets/manifest.json", "asset extraction"),
                0x58 => HandledCorePayload(segment, "world/manifest.json", "world instance extraction"),
                0x60 => WriteCorePayload(outputRoot, segment, "gameplay/gameplay_core.bin"),
                0x68 => WriteCorePayload(outputRoot, segment, "global_nav/global_nav_data.bin"),
                _ => WriteCorePayload(outputRoot, segment, $"core_unknown/{segment.Name}{segment.OutputExtension}")
            });
        }

        return routes;
    }

    private static CorePayloadRoute WriteCorePayload(
        string outputRoot,
        DlCoreLevelSegment segment,
        string relativePath)
    {
        if (segment.PayloadBytes.Length == 0)
        {
            return CreateCorePayloadRoute(segment, relativePath, "empty", "segment payload is empty");
        }

        var outputPath = CombineRelativePath(outputRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllBytes(outputPath, segment.PayloadBytes);
        return CreateCorePayloadRoute(segment, relativePath, "written", null);
    }

    private static CorePayloadRoute HandledCorePayload(
        DlCoreLevelSegment segment,
        string relativePath,
        string handledBy)
    {
        return CreateCorePayloadRoute(segment, relativePath, "handled", handledBy);
    }

    private static CorePayloadRoute CreateCorePayloadRoute(
        DlCoreLevelSegment segment,
        string relativePath,
        string status,
        string? note)
    {
        return new CorePayloadRoute(
            segment.Index,
            segment.HeaderOffset,
            segment.Name,
            segment.SemanticName,
            relativePath,
            status,
            note,
            segment.RawBytes.Length,
            segment.PayloadBytes.Length,
            segment.WasCompressedWad);
    }

    private static void ExtractCodeSegment(
        string outputDirectory,
        byte[] codeBytes,
        IDictionary<string, object?> rootManifest)
    {
        DeleteIfExists(Path.Combine(outputDirectory, "code.bin"));
        var code = DlCodeSegmentReader.Read(codeBytes);
        var recordRoutes = new List<object>(code.Records.Count);

        if (code.Records.Count > 0)
        {
            var patchesDirectory = CreateDirectory(outputDirectory, "patches");
            DeleteMatchingFiles(patchesDirectory, "*.bin");
            foreach (var record in code.Records)
            {
                var relativePath = $"patches/{record.Index:0000}.bin";
                File.WriteAllBytes(Path.Combine(patchesDirectory, $"{record.Index:0000}.bin"), record.PayloadBytes);
                recordRoutes.Add(new
                {
                    record.Index,
                    record.Offset,
                    record.InjectAddress,
                    record.PayloadSize,
                    record.Type,
                    record.EntrypointAddress,
                    record.HeaderBytes,
                    PayloadPath = relativePath
                });
            }
        }

        string? tailPath = null;
        if (code.UnparsedTail.Length > 0)
        {
            tailPath = "unknown_tail.bin";
            File.WriteAllBytes(Path.Combine(outputDirectory, tailPath), code.UnparsedTail);
        }

        var codeManifest = new
        {
            OmittedRawPayloads = new[]
            {
                new
                {
                    Path = "code.bin",
                    Replacement = "patches/*.bin plus manifest Records header metadata",
                    Reason = "The aggregate code segment is represented by ordered patch payloads and their 0x10-byte patch headers."
                }
            },
            code.Length,
            DlCodeSegmentReader.RecordHeaderLength,
            code.ParsedLength,
            UnparsedTailLength = code.UnparsedTail.Length,
            UnparsedTailPath = tailPath,
            RecordCount = code.Records.Count,
            Records = recordRoutes
        };

        WriteJson(Path.Combine(outputDirectory, "manifest.json"), codeManifest);
        rootManifest["Code"] = codeManifest;
        AddOmittedRawPayload(
            rootManifest,
            "code/code.bin",
            "code/patches/*.bin + code/manifest.json",
            "The aggregate code segment is represented by ordered patch payloads and patch header metadata.");
    }
}

namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private sealed record AssetWadCoverageSummary(
        int AssetWadLength,
        int SemanticCoveredByteCount,
        int SemanticCoverageRangeCount,
        int GapCount,
        int ZeroPaddingGapCount,
        int PreservedUnknownRangeCount,
        int PreservedUnknownByteCount,
        IReadOnlyList<AssetUnknownRange> PreservedUnknownRanges);

    private sealed record AssetCoverageRange(int Start, int End, string Label)
    {
        public int Length => End - Start;
    }

    private sealed record AssetCoverageGap(int Start, int End, int NonZeroByteCount)
    {
        public int Length => End - Start;
    }

    private sealed record AssetUnknownRange(
        int Start,
        int End,
        int Length,
        int NonZeroByteCount,
        string Path);

    private sealed record HeaderPayloadRoute(
        string SourcePath,
        string Path,
        int Offset,
        int Length,
        string Status);

    private sealed record GltfExportRoute(
        string Family,
        int? ModelId,
        string SourcePath,
        string GltfPath,
        string? BufferPath,
        string? DiagnosticsPath,
        string Status,
        string? Error)
    {
        public static GltfExportRoute Empty(string family, int? modelId, string sourcePath, string gltfPath)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "empty", null);
        }

        public static GltfExportRoute Written(
            string family,
            int? modelId,
            string sourcePath,
            string gltfPath,
            string bufferPath,
            string? diagnosticsPath)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, bufferPath, diagnosticsPath, "written", null);
        }

        public static GltfExportRoute Failed(
            string family,
            int? modelId,
            string sourcePath,
            string gltfPath,
            string error)
        {
            return new GltfExportRoute(family, modelId, sourcePath, gltfPath, null, null, "error", error);
        }
    }

    private sealed record MediaSource(
        int RequestedLevelIndex,
        int MediaLevelIndex,
        string Kind,
        bool IsInherited,
        string? InheritedRoot);

    private sealed record MediaPayloadSource(
        string Payload,
        string SourceKind,
        int RequestedLevelIndex,
        int MediaLevelIndex,
        bool IsInherited,
        string Directory);

    private sealed record CorePayloadRoute(
        int Index,
        int HeaderOffset,
        string Name,
        string SemanticName,
        string Path,
        string Status,
        string? Note,
        int RawLength,
        int PayloadLength,
        bool WasCompressedWad);

    private sealed record WorldSlotRoute(
        int Index,
        int HeaderOffset,
        int Pointer,
        int Length,
        string SemanticName,
        string? Path,
        string Status);

    public sealed record ExtractionSummary(string OutputDirectory, int CoreSegmentCount, int TextureCount);
}

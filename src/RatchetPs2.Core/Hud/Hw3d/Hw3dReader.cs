using RatchetPs2.Core.IO;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace RatchetPs2.Core.Hud.Hw3d;

public static class Hw3dReader
{
    private static readonly byte[] BeginMagicBytes = "BE_HW3D\0"u8.ToArray();
    private static readonly byte[] EndMagicBytes = "EN_HW3D"u8.ToArray();
    private static readonly byte[] HbnBeginMagicBytes = "HBN_BEG"u8.ToArray();
    private const uint HbnVersion = 0x00040502;

    public static Hw3dArchive Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The provided stream must be readable.", nameof(stream));
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Read(buffer.ToArray());
    }

    public static Hw3dArchive Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Hw3dHeader.SizeInBytes)
        {
            throw new InvalidDataException("HW3D data is too small to contain the outer header.");
        }

        if (data[..HbnBeginMagicBytes.Length].SequenceEqual(HbnBeginMagicBytes))
        {
            return ReadHbn(data);
        }

        var magic = Encoding.ASCII.GetString(data[..8]).TrimEnd('\0');
        if (!data[..8].SequenceEqual(BeginMagicBytes))
        {
            throw new InvalidDataException($"Invalid HW3D magic '{magic}'.");
        }

        var header = new Hw3dHeader(
            magic,
            ReadUInt32(data, 0x08),
            ReadUInt32(data, 0x0C),
            ReadUInt32(data, 0x10),
            ReadUInt32(data, 0x14),
            ReadUInt32(data, 0x18),
            ReadUInt32(data, 0x1C));

        var toc = ReadToc(data, header);
        var embeddedSections = FindEmbeddedSections(data);
        var endMagicOffset = data.IndexOf(EndMagicBytes);

        var archive = new Hw3dArchive(header, toc, embeddedSections, endMagicOffset, data.Length);
        s_archiveBytes.Add(archive, data.ToArray());
        return archive;
    }

    public static string Describe(Hw3dArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var builder = new StringBuilder();
        builder.AppendLine("HW3D archive summary");
        builder.AppendLine($"Length: {archive.Length} bytes (0x{archive.Length:X})");
        builder.AppendLine($"Magic: {archive.Header.Magic}");
        builder.AppendLine($"Version: {archive.Header.Version}");
        builder.AppendLine($"TOC entries: {archive.Header.TocEntryCount}");
        builder.AppendLine($"Header data-start field: 0x{archive.Header.DataStartOffset:X}");
        builder.AppendLine($"Header screen-count field: {archive.Header.ScreenCount}");
        builder.AppendLine($"Header unknown field: {archive.Header.UnknownValue}");
        builder.AppendLine($"Header reserved field: 0x{archive.Header.Reserved:X}");
        builder.AppendLine();

        if (archive.Header.Magic == "HBN_BEG")
        {
            AppendHbnDescription(builder, archive);
            return builder.ToString();
        }

        builder.AppendLine("Outer TOC entries (offset -> id):");
        foreach (var entry in archive.TocEntries)
        {
            builder.AppendLine($"  [{entry.Index:D2}] 0x{entry.Offset:X6} -> {entry.Id}");
        }

        builder.AppendLine();
        builder.AppendLine("Outer TOC entry field analysis:");
        foreach (var entry in archive.TocEntries)
        {
            AppendTocEntryAnalysis(builder, archive, entry);
        }

        builder.AppendLine();
        builder.AppendLine("Embedded HW3D begin markers:");
        foreach (var section in archive.EmbeddedSections)
        {
            builder.AppendLine($"  @0x{section.Offset:X}: {section.Magic}");
            if (section.HeaderWords.Count > 0)
            {
                builder.AppendLine($"    words: {string.Join(", ", section.HeaderWords.Select(word => $"0x{word:X8}"))}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Inner blob compatibility checks:");
        AppendInnerBlobAnalysis(builder, archive);

        builder.AppendLine();
        builder.AppendLine($"End marker offset: {(archive.EndMagicOffset >= 0 ? $"0x{archive.EndMagicOffset:X}" : "not found")}");
        builder.AppendLine();
        builder.AppendLine("Reverse-engineering notes:");
        builder.AppendLine("  - Inner widget deserializers in Ghidra clearly show menu records contain positions/scales/colors:");
        builder.AppendLine("      * Rectangle: pos @ +0x08/+0x0C, scale @ +0x10/+0x14, color @ +0x20");
        builder.AppendLine("      * TextArea: pos @ +0x08/+0x0C, scale @ +0x10/+0x14, color @ +0x20, text ptr @ +0x24");
        builder.AppendLine("      * Text: pos @ +0x08/+0x0C, scale @ +0x14/+0x18, color @ +0x24, text ptr @ +0x28");
        builder.AppendLine("      * Widget3d: pos @ +0x08/+0x0C, scale @ +0x10/+0x14, color @ +0x20, data pointers @ +0x24..+0x30");
        builder.AppendLine("  - Ghidra points to CreateScreen -> FindScreenParentContainer -> DeserializeHierarchy as the screen/widget parse path.");
        builder.AppendLine("  - ValidateIgeBinaryData expects an IGE blob version 0x40502 and returns pData + 0x14 after reading control flags at +0x10.");
        builder.AppendLine("  - The outer TOC blocks already show repeated color-like words such as 0x80808080 and dimension-like words such as 0x30, 0x50, 0x80, 0x98, 0x28C.");
        builder.AppendLine("  - The embedded BE_HW3D marker is likely a bank/container boundary rather than the first screen record itself.");

        return builder.ToString();
    }

    public static string? GenerateSvg(Hw3dArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (archive.Header.Magic != "HBN_BEG")
        {
            return null;
        }

        var bytes = GetArchiveBytes(archive);
        const int panelWidth = 360;
        const int panelHeight = 240;
        const int panelGap = 24;
        var screenCount = archive.TocEntries.Count;
        var totalWidth = panelWidth + (panelGap * 2);
        var totalHeight = checked(screenCount * (panelHeight + panelGap) + panelGap);

        var builder = new StringBuilder();
        builder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{totalWidth}\" height=\"{totalHeight}\" viewBox=\"0 0 {totalWidth} {totalHeight}\">");
        builder.AppendLine("  <style>text{font-family:monospace;font-size:12px;fill:#ddd}.small{font-size:11px;fill:#b7c3cf}.panel{fill:#101418;stroke:#2c3947;stroke-width:1}</style>");
        builder.AppendLine("  <rect width=\"100%\" height=\"100%\" fill=\"#0b0f14\"/>");

        for (var i = 0; i < screenCount; i++)
        {
            var screenOffset = checked((int)archive.TocEntries[i].Offset);
            var parsedNodes = ParseScreenNodes(archive, i);
            var xBase = panelGap;
            var yBase = panelGap + i * (panelHeight + panelGap);
            var clipId = $"screen-clip-{i:D2}";
            builder.AppendLine($"  <g transform=\"translate({xBase},{yBase})\">");
            builder.AppendLine($"    <rect class=\"panel\" x=\"0\" y=\"0\" width=\"{panelWidth}\" height=\"{panelHeight}\" rx=\"6\"/>");
            builder.AppendLine($"    <text x=\"8\" y=\"16\">screen {i:D2} @ 0x{screenOffset:X}</text>");

            if (screenOffset + 0x4C <= bytes.Length)
            {
                var rootControl = BitConverter.ToUInt32(bytes, screenOffset);
                var rootType = rootControl >> 16;
                var rootCount = rootControl & 0xffff;
                var topRectControl = BitConverter.ToUInt32(bytes, screenOffset + 0x24);
                var type = topRectControl >> 16;
                var count = topRectControl & 0xffff;
                var viewportX = 20f;
                var viewportY = 52f;
                var viewportWidth = panelWidth - 40f;
                var viewportHeight = 120f;
                builder.AppendLine("    <defs>");
                builder.AppendLine($"      <clipPath id=\"{clipId}\"><rect x=\"{FormatSvgFloat(viewportX)}\" y=\"{FormatSvgFloat(viewportY)}\" width=\"{FormatSvgFloat(viewportWidth)}\" height=\"{FormatSvgFloat(viewportHeight)}\" rx=\"2\"/></clipPath>");
                builder.AppendLine("    </defs>");
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(viewportX)}\" y=\"{FormatSvgFloat(viewportY)}\" width=\"{FormatSvgFloat(viewportWidth)}\" height=\"{FormatSvgFloat(viewportHeight)}\" fill=\"none\" stroke=\"#35506b\" stroke-dasharray=\"4 3\"/>");

                builder.AppendLine($"    <text class=\"small\" x=\"8\" y=\"20\">root type={rootType} count={rootCount}</text>");
                builder.AppendLine($"    <text class=\"small\" x=\"8\" y=\"34\">scan found {parsedNodes.Count} renderable nodes</text>");

                builder.AppendLine($"    <g clip-path=\"url(#{clipId})\">");
                foreach (var node in parsedNodes)
                {
                    RenderNodeSvg(builder, node, new SvgRect(viewportX, viewportY, viewportWidth, viewportHeight));
                }
                builder.AppendLine("    </g>");

                var summary = parsedNodes
                    .GroupBy(node => node.Type)
                    .OrderBy(group => group.Key)
                    .Select(group => $"t{group.Key}={group.Count()}");

                builder.AppendLine($"    <text class=\"small\" x=\"8\" y=\"{panelHeight - 12}\">{(parsedNodes.Count == 0 ? "no parsed widgets" : string.Join(", ", summary))}</text>");
            }

            builder.AppendLine("  </g>");
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static IReadOnlyList<Hw3dTocEntry> ReadToc(ReadOnlySpan<byte> data, Hw3dHeader header)
    {
        var toc = new List<Hw3dTocEntry>(checked((int)header.TocEntryCount));
        var tocOffset = Hw3dHeader.SizeInBytes;
        var tocLength = checked((int)header.TocEntryCount * 8);

        if (data.Length < tocOffset + tocLength)
        {
            throw new InvalidDataException("HW3D data is too small to contain the declared TOC.");
        }

        for (var i = 0; i < header.TocEntryCount; i++)
        {
            var entryOffset = tocOffset + (int)i * 8;
            toc.Add(new Hw3dTocEntry(
                (int)i,
                ReadUInt32(data, entryOffset),
                ReadUInt32(data, entryOffset + 4)));
        }

        return toc;
    }

    private static IReadOnlyList<Hw3dEmbeddedSection> FindEmbeddedSections(ReadOnlySpan<byte> data)
    {
        var sections = new List<Hw3dEmbeddedSection>();
        var searchStart = 0;

        while (searchStart <= data.Length - BeginMagicBytes.Length)
        {
            var relativeIndex = data[searchStart..].IndexOf(BeginMagicBytes);
            if (relativeIndex < 0)
            {
                break;
            }

            var offset = searchStart + relativeIndex;
            var words = new List<uint>();
            var wordStart = offset + BeginMagicBytes.Length;
            var availableWordBytes = Math.Min(0x18, data.Length - wordStart);

            for (var i = 0; i + 4 <= availableWordBytes; i += 4)
            {
                words.Add(ReadUInt32(data, wordStart + i));
            }

            sections.Add(new Hw3dEmbeddedSection(
                offset,
                Encoding.ASCII.GetString(data.Slice(offset, BeginMagicBytes.Length)).TrimEnd('\0'),
                words));

            searchStart = offset + BeginMagicBytes.Length;
        }

        return sections;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BitConverter.ToUInt32(data.Slice(offset, sizeof(uint)));
    }

    private static void AppendTocEntryAnalysis(StringBuilder builder, Hw3dArchive archive, Hw3dTocEntry entry)
    {
        if (entry.Offset >= archive.Length)
        {
            builder.AppendLine($"  [{entry.Index:D2}] invalid offset 0x{entry.Offset:X6}");
            return;
        }

        var blockLength = GetEntryLength(archive, entry.Index);
        var available = Math.Min(blockLength, archive.Length - (int)entry.Offset);
        var bytes = GetArchiveBytes(archive).AsSpan((int)entry.Offset, Math.Min(available, 0x40));

        var controlWord = ReadUInt32(bytes, 0);
        var controlType = controlWord >> 16;
        var controlCount = controlWord & 0xffff;

        builder.AppendLine($"  [{entry.Index:D2}] tocId={entry.Id} off=0x{entry.Offset:X6} len≈0x{blockLength:X}");
        builder.AppendLine($"       control block: type={controlType}, count={controlCount}, raw=0x{controlWord:X8}");

        if (bytes.Length >= 0x18)
        {
            var word08 = ReadUInt32(bytes, 0x08);
            var word0C = ReadUInt32(bytes, 0x0C);
            var word10 = ReadUInt32(bytes, 0x10);
            var word14 = ReadUInt32(bytes, 0x14);
            builder.AppendLine(
                $"       words: +0x08=0x{word08:X8}, +0x0C=float {FormatFloat(word0C)}, +0x10=0x{word10:X8}, +0x14=0x{word14:X8}");
        }

        if (bytes.Length >= 0x2C)
        {
            var colorWord = ReadUInt32(bytes, 0x28);
            builder.AppendLine($"       possible color @+0x28: 0x{colorWord:X8} ({FormatColor(colorWord)})");
        }

        if (bytes.Length >= 0x20)
        {
            var tailWords = new List<string>();
            for (var offset = 0x18; offset < Math.Min(bytes.Length, 0x30); offset += 4)
            {
                tailWords.Add($"+0x{offset:X2}=0x{ReadUInt32(bytes, offset):X8}");
            }

            if (tailWords.Count > 0)
            {
                builder.AppendLine($"       trailing words: {string.Join(", ", tailWords)}");
            }
        }

        if (entry.Index is 0 or 1 or 2)
        {
            builder.AppendLine("       note: large control types here do not match inner hierarchy widget types 1..10, so this is likely higher-level bank metadata.");
        }
        else if (controlType == 3)
        {
            builder.AppendLine("       note: control type 3 repeats across several entries and may represent a common resource/widget bucket rather than a final rectangle record.");
        }
    }

    private static int GetEntryLength(Hw3dArchive archive, int index)
    {
        if (index + 1 < archive.TocEntries.Count)
        {
            return checked((int)archive.TocEntries[index + 1].Offset - (int)archive.TocEntries[index].Offset);
        }

        return Math.Max(0, archive.EndMagicOffset - (int)archive.TocEntries[index].Offset);
    }

    private static byte[] GetArchiveBytes(Hw3dArchive archive)
    {
        return s_archiveBytes.TryGetValue(archive, out var bytes)
            ? bytes
            : throw new InvalidOperationException("Archive byte cache missing.");
    }

    private static string FormatFloat(uint value)
    {
        var result = BitConverter.Int32BitsToSingle(unchecked((int)value));
        return result.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatColor(uint value)
    {
        var a = (byte)(value >> 24);
        var r = (byte)(value >> 16);
        var g = (byte)(value >> 8);
        var b = (byte)value;
        return $"A={a:X2} R={r:X2} G={g:X2} B={b:X2}";
    }

    private static void AppendInnerBlobAnalysis(StringBuilder builder, Hw3dArchive archive)
    {
        var bytes = GetArchiveBytes(archive);
        var secondMarker = archive.EmbeddedSections.Skip(1).FirstOrDefault();
        if (secondMarker is null)
        {
            builder.AppendLine("  No embedded inner HW3D marker was found.");
            return;
        }

        var payloadOffset = secondMarker.Offset + BeginMagicBytes.Length;
        builder.AppendLine($"  Embedded marker offset: 0x{secondMarker.Offset:X}");
        builder.AppendLine($"  Payload directly after marker begins at: 0x{payloadOffset:X}");

        var hasHbnBeg = FindAscii(bytes, "HBN_BEG") >= 0;
        var hasHbdEnd = FindAscii(bytes, "HBD_END") >= 0;
        var hasEndWidgetPcon = FindAscii(bytes, "END_WIDGET_PCON") >= 0;
        var hasEndWidgetCont = FindAscii(bytes, "END_WIDGET_CONT") >= 0;

        builder.AppendLine($"  Contains expected IGE/HBN begin marker string: {hasHbnBeg}");
        builder.AppendLine($"  Contains expected hierarchy end string HBD_END: {hasHbdEnd}");
        builder.AppendLine($"  Contains END_WIDGET_PCON: {hasEndWidgetPcon}");
        builder.AppendLine($"  Contains END_WIDGET_CONT: {hasEndWidgetCont}");

        builder.AppendLine("  Ghidra expectation from ValidateIgeBinaryData:");
        builder.AppendLine("    - Binary should start with HBN_BEG + version text parsable to 0x40502");
        builder.AppendLine("    - Control flags are read at +0x10");
        builder.AppendLine("    - Screen count / screen offsets are read starting from returned pointer (base + 0x14)");

        var candidateWords = new List<string>();
        for (var i = 0; i < 8 && payloadOffset + (i * 4) + 4 <= bytes.Length; i++)
        {
            var value = BitConverter.ToUInt32(bytes, payloadOffset + (i * 4));
            candidateWords.Add($"+0x{i * 4:X2}=0x{value:X8}");
        }

        builder.AppendLine($"  First payload words after inner marker: {string.Join(", ", candidateWords)}");
        builder.AppendLine("  Current interpretation:");
        builder.AppendLine("    - This embedded section does not look like a plain ValidateIgeBinaryData-compatible HBN blob in-file.");
        builder.AppendLine("    - It is probably a custom HW3D wrapper/bank that the game converts into an IGE hierarchy at runtime, or the HBN content is packed indirectly via offsets/pointers.");
        builder.AppendLine("    - The repeated outer-entry fields still strongly suggest UI layout/style metadata: position-like float at +0x0C, width/size-like values at +0x10/+0x14, visibility-like 0x1, and grey ARGB color 0x80808080.");
    }

    private static int FindAscii(byte[] bytes, string value)
    {
        return Encoding.ASCII.GetString(bytes).IndexOf(value, StringComparison.Ordinal);
    }

    private static Hw3dArchive ReadHbn(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x18)
        {
            throw new InvalidDataException("HBN data is too small to contain the expected header.");
        }

        var bytes = data.ToArray();
        var controlFlags = ReadUInt32(data, 0x10);
        var screenCount = ReadUInt32(data, 0x14);
        var tocOffset = 0x18;
        var tocLength = checked((int)screenCount * 4);

        if (data.Length < tocOffset + tocLength)
        {
            throw new InvalidDataException("HBN data is too small to contain the declared screen offset table.");
        }

        var toc = new List<Hw3dTocEntry>((int)screenCount);
        for (var i = 0; i < screenCount; i++)
        {
            var entryOffset = tocOffset + (i * 4);
            toc.Add(new Hw3dTocEntry(i, ReadUInt32(data, entryOffset), (uint)i));
        }

        var header = new Hw3dHeader(
            "HBN_BEG",
            HbnVersion,
            screenCount,
            (uint)tocOffset,
            screenCount,
            controlFlags,
            0);

        var archive = new Hw3dArchive(
            header,
            toc,
            Array.Empty<Hw3dEmbeddedSection>(),
            FindAscii(bytes, "HBD_END"),
            bytes.Length);

        s_archiveBytes.Add(archive, bytes);
        return archive;
    }

    private static void AppendHbnDescription(StringBuilder builder, Hw3dArchive archive)
    {
        var bytes = GetArchiveBytes(archive);
        builder.AppendLine("Raw HBN / IGE hierarchy summary");
        builder.AppendLine($"Control flags: 0x{archive.Header.UnknownValue:X8}");
        builder.AppendLine($"Screen count: {archive.Header.ScreenCount}");
        builder.AppendLine();
        builder.AppendLine("Screen offset table:");
        foreach (var entry in archive.TocEntries)
        {
            builder.AppendLine($"  screen[{entry.Index:D2}] -> 0x{entry.Offset:X6}");
        }

        builder.AppendLine();
        builder.AppendLine("Marker presence:");
        builder.AppendLine($"  HBD_END: {FindAscii(bytes, "HBD_END"):X}");
        builder.AppendLine($"  END_WIDGET_PCON count: {CountAscii(bytes, "END_WIDGET_PCON")}");
        builder.AppendLine($"  END_WIDGET_CONT count: {CountAscii(bytes, "END_WIDGET_CONT")}");

        builder.AppendLine();
        builder.AppendLine("First-screen structural preview:");
        AppendHbnScreenPreview(builder, archive, 0);

        builder.AppendLine();
        builder.AppendLine("First-screen root-wrapper ownership preview:");
        AppendHbnRootOwnershipPreview(builder, archive, 0);

        builder.AppendLine();
        builder.AppendLine("Recurring screen signature families:");
        AppendHbnScreenFamilies(builder, archive);

        builder.AppendLine();
        builder.AppendLine("Function-backed template correlations:");
        AppendHbnFunctionCorrelations(builder, archive);

        builder.AppendLine();
        builder.AppendLine("Menu-specific candidate mappings:");
        AppendMenuSpecificCandidates(builder, archive);

        builder.AppendLine();
        builder.AppendLine("Preliminary parsed widget scan:");
        AppendHbnParsedWidgetSummary(builder, archive);

        builder.AppendLine();
        builder.AppendLine("Interpretation:");
        builder.AppendLine("  - This file matches the Ghidra ValidateIgeBinaryData/CreateScreen/DeserializeHierarchy path directly.");
        builder.AppendLine("  - The header is HBN_BEG + ASCII version text + control flags at +0x10 + screen count at +0x14 + screen offset table at +0x18.");
        builder.AppendLine("  - Unlike hudw3d.bin, this file stores the raw widget hierarchy markers in-file.");
        builder.AppendLine("  - The first screens begin with control block type 5 / count 1.");
        builder.AppendLine("  - Executable control flow confirms type 5 is special-cased in DeserializeHierarchy before normal jump-table dispatch.");
        builder.AppendLine("  - For the actual onlinew3d.bin layout, the root type-5 wrapper still behaves like a frame/container-shaped record in-file: a 0x20-byte body followed by child controls at screenOffset + 0x24.");
        builder.AppendLine("  - The parser currently keeps the earlier, more recognizable on-disk shape mapping for child records while treating the root type-5 wrapper as a special ownership/container boundary.");
    }

    private static void AppendHbnScreenPreview(StringBuilder builder, Hw3dArchive archive, int screenIndex)
    {
        if (screenIndex < 0 || screenIndex >= archive.TocEntries.Count)
        {
            builder.AppendLine("  Screen index out of range.");
            return;
        }

        var bytes = GetArchiveBytes(archive);
        var screenOffset = checked((int)archive.TocEntries[screenIndex].Offset);
        builder.AppendLine($"  screen[{screenIndex:D2}] @ 0x{screenOffset:X6}");

        for (var rel = 0; rel < 0x80 && screenOffset + rel + 4 <= bytes.Length; rel += 4)
        {
            var value = BitConverter.ToUInt32(bytes, screenOffset + rel);
            var type = value >> 16;
            var count = value & 0xffff;

            if (rel is 0x0 or 0x24 or 0x5C or 0x60)
            {
                builder.AppendLine(
                    $"    +0x{rel:X2}: raw=0x{value:X8} type={type} count={count} {DescribeKnownHbnControl(type, count, rel)}");
            }
        }

        builder.AppendLine("    notable decoded values from screen[00]:");
        builder.AppendLine($"      +0x04 relative/container id? = 0x{BitConverter.ToUInt32(bytes, screenOffset + 0x04):X8}");
        builder.AppendLine($"      +0x0C..+0x18 floats = {FormatFloat(BitConverter.ToUInt32(bytes, screenOffset + 0x0C))}, {FormatFloat(BitConverter.ToUInt32(bytes, screenOffset + 0x10))}, {FormatFloat(BitConverter.ToUInt32(bytes, screenOffset + 0x14))}, {FormatFloat(BitConverter.ToUInt32(bytes, screenOffset + 0x18))}");
        builder.AppendLine($"      +0x48 color-like word = 0x{BitConverter.ToUInt32(bytes, screenOffset + 0x48):X8}");
        builder.AppendLine($"      +0x64/+0x68 sentinel words = 0x{BitConverter.ToUInt32(bytes, screenOffset + 0x64):X8}, 0x{BitConverter.ToUInt32(bytes, screenOffset + 0x68):X8}");
    }

    private static void AppendHbnRootOwnershipPreview(StringBuilder builder, Hw3dArchive archive, int screenIndex)
    {
        if (screenIndex < 0 || screenIndex >= archive.TocEntries.Count)
        {
            builder.AppendLine("  Screen index out of range.");
            return;
        }

        var bytes = GetArchiveBytes(archive);
        var screenOffset = checked((int)archive.TocEntries[screenIndex].Offset);
        var rootControl = BitConverter.ToUInt32(bytes, screenOffset);
        var rootType = rootControl >> 16;
        var rootCount = rootControl & 0xffff;

        builder.AppendLine($"  screen[{screenIndex:D2}] root @ 0x{screenOffset:X6}: type={rootType} count={rootCount}");

        if (rootType != 5)
        {
            builder.AppendLine("  Root is not the expected type-5 wrapper; no ownership preview generated.");
            return;
        }

        // Current source-of-truth interpretation:
        // - DeserializeHierarchy special-cases type 5 before normal dispatch
        // - on non-root calls it immediately returns pData + 4
        // - despite that control-flow oddity, the on-disk root wrapper still exposes a consistent
        //   0x20-byte frame-like body and the first child begins at screenOffset + 0x24 in onlinew3d.bin
        var recordBase = screenOffset + 0x04;
        var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
        var relativeId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
        var posX = ReadFloat(bytes, recordBase + 0x08);
        var posY = ReadFloat(bytes, recordBase + 0x0C);
        var scaleX = ReadFloat(bytes, recordBase + 0x10);
        var scaleY = ReadFloat(bytes, recordBase + 0x14);
        var animId = BitConverter.ToInt32(bytes, recordBase + 0x18);
        var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
        var firstChildOffset = screenOffset + 0x24;

        builder.AppendLine($"    type-5/frame-container body @ +0x04: flags=0x{flags:X8} relId={relativeId}");
        builder.AppendLine($"    root container pos=({posX:0.###},{posY:0.###}) scale=({scaleX:0.###},{scaleY:0.###})");
        builder.AppendLine($"    root container anim={animId} visible={visible}");
        builder.AppendLine($"    first child after wrapper/event section: 0x{firstChildOffset:X}");

        if (firstChildOffset + 4 <= bytes.Length)
        {
            var firstChildControl = BitConverter.ToUInt32(bytes, firstChildOffset);
            builder.AppendLine($"    first child control raw=0x{firstChildControl:X8} type={firstChildControl >> 16} count={firstChildControl & 0xffff}");
        }

        builder.AppendLine("    note: this child offset is taken from repeated raw-data structure in onlinew3d.bin, while the executable treats type 5 as a special root-only control-flow case rather than a normal widget deserializer.");
    }

    private static void AppendHbnScreenFamilies(StringBuilder builder, Hw3dArchive archive)
    {
        var bytes = GetArchiveBytes(archive);
        var families = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var i = 0; i < archive.TocEntries.Count; i++)
        {
            var screenOffset = checked((int)archive.TocEntries[i].Offset);
            if (screenOffset + 0x64 > bytes.Length)
            {
                continue;
            }

            var sig0 = BitConverter.ToUInt32(bytes, screenOffset + 0x00);
            var sig24 = BitConverter.ToUInt32(bytes, screenOffset + 0x24);
            var sig5c = BitConverter.ToUInt32(bytes, screenOffset + 0x5C);
            var sig60 = BitConverter.ToUInt32(bytes, screenOffset + 0x60);
            var color = BitConverter.ToUInt32(bytes, screenOffset + 0x48);
            var key = $"sig=({sig0:X8},{sig24:X8},{sig5c:X8},{sig60:X8}) color=0x{color:X8}";

            if (!families.TryGetValue(key, out var screens))
            {
                screens = new List<int>();
                families.Add(key, screens);
            }

            screens.Add(i);
        }

        foreach (var family in families.OrderByDescending(x => x.Value.Count).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"  {family.Key}");
            builder.AppendLine($"    screens: {string.Join(", ", family.Value.Select(x => x.ToString("D2", CultureInfo.InvariantCulture)))}");
            builder.AppendLine($"    guess: {GuessFamilyMeaning(family.Key)}");
        }
    }

    private static void AppendHbnFunctionCorrelations(StringBuilder builder, Hw3dArchive archive)
    {
        builder.AppendLine("  GuiSupport_CreatePlayerFrames:");
        builder.AppendLine("    - Calls CreateScreen(GetDigeRam(), 0, 0, true, false, ...) and CreateScreen(..., 1, ...)");
        builder.AppendLine("    - Strong candidate mapping: screen[00] and screen[01]");
        builder.AppendLine("    - Evidence: both screens share the blue rectangle color 0x8009098E used directly in GuiSupport_CreatePlayerFrames");

        builder.AppendLine("  GuiSupport_CreatePlayerDFrames:");
        builder.AppendLine("    - Calls CreateScreen(GetDigeRam(), 0, 2, true, false, ...)");
        builder.AppendLine("    - Strong candidate mapping: screen[02]");
        builder.AppendLine("    - Evidence: screen[02] is the next raw HBN screen index used by GuiSupport and has a distinct neutral-grey family signature");

        builder.AppendLine("  GuiSupport_CreatePlayerDialogs:");
        builder.AppendLine("    - Manual runtime construction only; does not call CreateScreen");
        builder.AppendLine("    - Therefore dialog visuals are not directly loaded from onlinew3d.bin screens in this path");

        builder.AppendLine("  GuiSupport_CreatePlayerMenus:");
        builder.AppendLine("    - Manual runtime construction only; does not call CreateScreen");
        builder.AppendLine("    - Therefore menu button/title widgets here are not direct HBN screen loads in this helper");

        builder.AppendLine("  GuiSupport_CreatePlayerCursors:");
        builder.AppendLine("    - Manual runtime construction only; uses rectangles/hollow rectangles/textures without CreateScreen");
        builder.AppendLine("    - Cursor visuals are not direct HBN screen loads in this helper");

        builder.AppendLine("  Remaining screens in onlinew3d.bin:");
        builder.AppendLine("    - Likely consumed by other menu/hud state-specific CreateWidgets functions such as MenuLoad_CreateWidgets, MenuArena_CreateWidgets, MenuWeapon_CreateWidgets, etc.");
        builder.AppendLine("    - Example: screen[33] is notable because its top control at +0x24 is type 2 (WidgetText), making it a strong candidate for a text-centric menu template.");
    }

    private static void AppendMenuSpecificCandidates(StringBuilder builder, Hw3dArchive archive)
    {
        builder.AppendLine("  MenuLoad_CreateWidgets:");
        builder.AppendLine("    - Uses Panel::Create(&MenuLoad_Screen, 0x24, -1) and then edits panel elements 0x25, 0x26 and several slot image elements.");
        builder.AppendLine("    - This suggests a prebuilt panel template with a moderate element count rather than a tiny frame/cursor helper.");
        builder.AppendLine("    - Best current HBN candidates are the neutral-grey family screens with richer nested signatures, especially screen[02], screen[28], screen[30].");

        builder.AppendLine("  MenuArena_CreateWidgets:");
        builder.AppendLine("    - Uses Panel::Create(&MenuArena_Screen, 0x2f, -1) and then populates many text, texture, and reward-icon elements.");
        builder.AppendLine("    - This points to one of the denser large-panel HBN templates rather than the small blue frame family.");
        builder.AppendLine("    - Best current candidates are the large dark-overlay families used by many screens: screen[03]-[10], [14], [16]-[17], [22]-[23], [26], [35], [37].");

        builder.AppendLine("  MenuWeapon_CreateWidgets:");
        builder.AppendLine("    - Uses Panel::Create(&MenuWeapon_Screen, 0x1f, -1) plus Panel::Create(&MenuWeapon_Edit, 0x20, -1), with many text and text-area edits.");
        builder.AppendLine("    - Because it relies heavily on text/text-area elements, text-centric top controls are especially interesting here.");
        builder.AppendLine("    - Strong standout candidate: screen[33], whose top control at +0x24 is type 2 (WidgetText). Secondary candidates include grey list/text families like screen[12], [13], [24], [29], [32], [36], [38].");

        builder.AppendLine("  Confidence note:");
        builder.AppendLine("    - These menu-specific mappings are still candidate-level, but they are function-backed in the sense that they are constrained by which helpers use CreateScreen versus Panel::Create and by the observed top-node/control families in the HBN data.");
    }

    private static string DescribeKnownHbnControl(uint type, uint count, int rel)
    {
        return (type, count, rel) switch
        {
            (5, 1, 0x00) => "<- likely screen wrapper/control boundary",
            (3, 1, 0x24) => "<- likely first widget/control block following wrapper",
            (3, 4, 0x5C) => "<- likely nested rectangle/widget group",
            (1, 1, 0x60) => "<- likely frame-container block",
            _ => string.Empty
        };
    }

    private static string GuessFamilyMeaning(string familyKey)
    {
        if (familyKey.Contains("color=0x8009098E", StringComparison.Ordinal))
        {
            return "blue panel family; matches rectangle color used in GuiSupport_CreatePlayerFrames";
        }

        if (familyKey.Contains("color=0x80C8C8C8", StringComparison.Ordinal) ||
            familyKey.Contains("color=0x80606060", StringComparison.Ordinal))
        {
            return "neutral grey panel/text-list family";
        }

        if (familyKey.Contains("color=0x66070B54", StringComparison.Ordinal) ||
            familyKey.Contains("color=0x66060654", StringComparison.Ordinal))
        {
            return "translucent dark overlay/backing panel family";
        }

        return "unclassified recurring screen template";
    }

    private static void AppendHbnParsedWidgetSummary(StringBuilder builder, Hw3dArchive archive)
    {
        for (var i = 0; i < archive.TocEntries.Count; i++)
        {
            var nodes = FlattenNodes(ParseScreenNodes(archive, i)).ToList();
            var byType = nodes
                .GroupBy(node => node.Type)
                .OrderBy(group => group.Key)
                .Select(group => $"type {group.Key} x{group.Count()}");

            builder.AppendLine($"  screen[{i:D2}]: {(nodes.Count == 0 ? "no confirmed renderable nodes parsed yet" : string.Join(", ", byType))}");
        }
    }

    private static IReadOnlyList<ParsedNode> ParseScreenNodes(Hw3dArchive archive, int screenIndex)
    {
        if (screenIndex < 0 || screenIndex >= archive.TocEntries.Count)
        {
            return Array.Empty<ParsedNode>();
        }

        var bytes = GetArchiveBytes(archive);
        var screenStart = checked((int)archive.TocEntries[screenIndex].Offset);
        var screenEnd = screenIndex + 1 < archive.TocEntries.Count
            ? checked((int)archive.TocEntries[screenIndex + 1].Offset)
            : (archive.EndMagicOffset > 0 ? archive.EndMagicOffset : bytes.Length);

        var cursor = screenStart;
        if (cursor >= screenEnd)
        {
            return Array.Empty<ParsedNode>();
        }

        var nodes = new List<ParsedNode>();
        ParseNodeSequence(bytes, cursor, screenEnd, nodes);
        return nodes;
    }

    private static int ParseNodeSequence(byte[] bytes, int cursor, int limit, List<ParsedNode> nodes)
    {
        while (cursor + 4 <= limit)
        {
            if (IsAnyMarkerAt(bytes, cursor, out _))
            {
                break;
            }

            var control = BitConverter.ToUInt32(bytes, cursor);
            var type = (int)(control >> 16);
            var count = (int)(control & 0xffff);
            if (type == 9)
            {
                break;
            }

            if (count <= 0)
            {
                break;
            }

            var recordCursor = cursor + 4;
            for (var i = 0; i < count; i++)
            {
                if (!TryParseNodeRecord(bytes, type, recordCursor, limit, out var node, out var nextOffset))
                {
                    return cursor;
                }

                nodes.Add(node);
                recordCursor = nextOffset;
            }

            if (type == 5)
            {
                break;
            }

            cursor = FindNextNodeHeaderOffset(bytes, recordCursor, limit);
        }

        return cursor;
    }

    private static bool TryParseNodeRecord(byte[] bytes, int type, int recordBase, int limit, out ParsedNode node, out int nextOffset)
    {
        node = default!;
        nextOffset = recordBase;

        switch (type)
        {
            case 1:
            {
                // Earlier on-disk shape mapping kept here because it produces the recognizable layouts.
                if (recordBase + 0x28 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x14);
                var scaleY = ReadFloat(bytes, recordBase + 0x18);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x1C);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x20) != 0;
                var color = BitConverter.ToUInt32(bytes, recordBase + 0x24);
                var text = ReadLiteral(bytes, recordBase + 0x28, 0x20);
                var cursor = WalkLiteralOffset(bytes, recordBase + 0x28, 0x20);
                cursor = SkipEventStream(bytes, cursor, limit);

                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, color, text, Array.Empty<ParsedNode>(), 0, 0);
                nextOffset = cursor;
                return true;
            }
            case 2:
            {
                if (recordBase + 0x24 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x10);
                var scaleY = ReadFloat(bytes, recordBase + 0x14);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x18);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
                var color = BitConverter.ToUInt32(bytes, recordBase + 0x20);
                var cursor = SkipEventStream(bytes, recordBase + 0x24, limit);
                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, color, null, Array.Empty<ParsedNode>(), 3, 3);
                nextOffset = cursor;
                return true;
            }
            case 3:
            {
                if (recordBase + 0x44 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x10);
                var scaleY = ReadFloat(bytes, recordBase + 0x14);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x18);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
                var color = BitConverter.ToUInt32(bytes, recordBase + 0x20);
                var cursor = ComputeWidget3dDataEnd(bytes, recordBase, limit);
                cursor = SkipEventStream(bytes, cursor, limit);

                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, color, null, Array.Empty<ParsedNode>(), 3, 3);
                nextOffset = cursor;
                return true;
            }
            case 4:
            {
                if (recordBase + 0x20 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x10);
                var scaleY = ReadFloat(bytes, recordBase + 0x14);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x18);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
                var children = new List<ParsedNode>();
                var cursor = ParseNodeSequence(bytes, recordBase + 0x20, limit, children);
                cursor = SkipEventStream(bytes, cursor, limit);
                if (IsMarkerAt(bytes, cursor, "END_WIDGET_CONT"))
                {
                    cursor = WalkLiteralOffset(bytes, cursor, 0x1000);
                }

                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, 0, null, children, 3, 3);
                nextOffset = cursor;
                return true;
            }
            case 5:
            {
                // Source-of-truth control flow: type 5 is special-cased in DeserializeHierarchy.
                // For onlinew3d.bin root screens, the control word is followed by a consistent 0x20-byte
                // frame-like body and then the real child sequence begins at +0x24.
                if (recordBase + 0x20 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x10);
                var scaleY = ReadFloat(bytes, recordBase + 0x14);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x18);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
                var children = new List<ParsedNode>();
                var cursor = ParseNodeSequence(bytes, recordBase + 0x20, limit, children);

                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, 0, null, children, 3, 3);
                nextOffset = cursor;
                return true;
            }
            case 10:
            {
                if (recordBase + 0x24 > limit)
                {
                    return false;
                }

                var flags = BitConverter.ToUInt32(bytes, recordBase + 0x00);
                var widgetId = BitConverter.ToUInt32(bytes, recordBase + 0x04);
                var posX = ReadFloat(bytes, recordBase + 0x08);
                var posY = ReadFloat(bytes, recordBase + 0x0C);
                var scaleX = ReadFloat(bytes, recordBase + 0x10);
                var scaleY = ReadFloat(bytes, recordBase + 0x14);
                var anim = BitConverter.ToUInt32(bytes, recordBase + 0x18);
                var visible = BitConverter.ToUInt32(bytes, recordBase + 0x1C) != 0;
                var color = BitConverter.ToUInt32(bytes, recordBase + 0x20);
                var text = ReadLiteral(bytes, recordBase + 0x24, 0x20);
                var cursor = WalkLiteralOffset(bytes, recordBase + 0x24, 0x20);
                cursor = SkipEventStream(bytes, cursor, limit);

                node = new ParsedNode(type, recordBase - 4, widgetId, flags, anim, visible, posX, posY, scaleX, scaleY, color, text, Array.Empty<ParsedNode>(), 3, 3);
                nextOffset = cursor;
                return true;
            }
            default:
                return false;
        }
    }

    private static int SkipEventStream(byte[] bytes, int offset, int limit)
    {
        if (offset + 4 > limit)
        {
            return offset;
        }

        var control = BitConverter.ToUInt32(bytes, offset);
        var type = control >> 16;
        var responseListCount = (int)(control & 0xffff);
        if (type != 9)
        {
            return offset;
        }

        var cursor = offset + 4;
        for (var i = 0; i < responseListCount; i++)
        {
            if (cursor + 4 > limit)
            {
                return limit;
            }

            var listenHeader = BitConverter.ToUInt32(bytes, cursor);
            var listenEventCount = (int)(listenHeader & 0xffff);
            cursor += 4 + checked(listenEventCount * 0x0C);
            if (cursor + 4 > limit)
            {
                return limit;
            }

            var commandHeader = BitConverter.ToUInt32(bytes, cursor);
            var commandEventCount = (int)(commandHeader & 0xffff);
            cursor += 4 + checked(commandEventCount * 0x0C);
        }

        return Math.Min(cursor, limit);
    }

    private static int ComputeWidget3dDataEnd(byte[] bytes, int recordBase, int limit)
    {
        var width = BitConverter.ToUInt16(bytes, recordBase + 0x34);
        var height = BitConverter.ToUInt16(bytes, recordBase + 0x36);
        var format = bytes[recordBase + 0x38];
        var stride = BitConverter.ToUInt16(bytes, recordBase + 0x3A);
        var cursor = recordBase + 0x44;
        cursor += checked(width * stride * 4);

        if (format == 0)
        {
            cursor += checked(height * 4);
        }
        else if (format == 1)
        {
            var byteCount = height * 2;
            var remainder = byteCount % 4;
            cursor += byteCount;
            if (remainder > 0)
            {
                cursor += remainder;
            }
        }

        return Math.Min(cursor, limit);
    }

    private static int FindNextNodeHeaderOffset(byte[] bytes, int offset, int limit)
    {
        var cursor = Align4(offset);
        while (cursor + 4 <= limit)
        {
            if (IsAnyMarkerAt(bytes, cursor, out _))
            {
                return cursor;
            }

            if (LooksLikeNodeHeader(bytes, cursor, limit))
            {
                return cursor;
            }

            cursor += 4;
        }

        return limit;
    }

    private static bool LooksLikeNodeHeader(byte[] bytes, int offset, int limit)
    {
        var control = BitConverter.ToUInt32(bytes, offset);
        var type = (int)(control >> 16);
        var count = (int)(control & 0xffff);
        if (count <= 0 || count > 0x100)
        {
            return false;
        }
        var recordBase = offset + 4;
        return type switch
        {
            1 => recordBase + 0x28 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x20),
            2 => recordBase + 0x24 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x1C),
            3 => recordBase + 0x44 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x1C),
            4 => recordBase + 0x20 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x1C),
            5 => recordBase + 0x20 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x1C),
            9 => true,
            10 => recordBase + 0x24 <= limit && LooksLikeVisibilityWord(bytes, recordBase + 0x1C),
            _ => false,
        };
    }

    private static bool LooksLikeVisibilityWord(byte[] bytes, int offset)
    {
        var value = BitConverter.ToUInt32(bytes, offset);
        return value is 0 or 1;
    }

    private static int WalkLiteralOffset(byte[] bytes, int offset, int cap)
    {
        var end = offset;
        var max = Math.Min(bytes.Length, offset + cap);
        while (end < max)
        {
            if (bytes[end] == 0)
            {
                return Align4(end + 1);
            }

            end++;
        }

        return max;
    }

    private static int Align4(int value)
    {
        return (value + 3) & ~3;
    }

    private static bool IsAnyMarkerAt(byte[] bytes, int offset, out string marker)
    {
        foreach (var candidate in s_knownMarkers)
        {
            if (IsMarkerAt(bytes, offset, candidate))
            {
                marker = candidate;
                return true;
            }
        }

        marker = string.Empty;
        return false;
    }

    private static bool IsMarkerAt(byte[] bytes, int offset, string value)
    {
        if (offset < 0 || offset + value.Length > bytes.Length)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (bytes[offset + i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void RenderNodeSvg(StringBuilder builder, ParsedNode node, SvgRect parentRect)
    {
        if (!node.Visible)
        {
            return;
        }

        var nodeRect = ComputeNodeRect(node, parentRect, 8f, 8f);

        switch (node.Type)
        {
            case 1:
            {
                var textX = parentRect.X + (parentRect.Width * node.PosX);
                var textY = parentRect.Y + (parentRect.Height * node.PosY);
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(textX - 3)}\" y1=\"{FormatSvgFloat(textY)}\" x2=\"{FormatSvgFloat(textX + 3)}\" y2=\"{FormatSvgFloat(textY)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1\"/>");
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(textX)}\" y1=\"{FormatSvgFloat(textY - 3)}\" x2=\"{FormatSvgFloat(textX)}\" y2=\"{FormatSvgFloat(textY + 3)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1\"/>");
                break;
            }
            case 2:
            {
                var rect = nodeRect;
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(rect.X)}\" y=\"{FormatSvgFloat(rect.Y)}\" width=\"{FormatSvgFloat(rect.Width)}\" height=\"{FormatSvgFloat(rect.Height)}\" fill=\"{ToSvgColor(node.Color)}\" fill-opacity=\"{ToSvgOpacity(node.Color)}\" stroke=\"#ffffff55\" stroke-width=\"1\"/>");
                builder.AppendLine($"    <circle cx=\"{FormatSvgFloat(parentRect.X + (parentRect.Width * node.PosX))}\" cy=\"{FormatSvgFloat(parentRect.Y + (parentRect.Height * node.PosY))}\" r=\"2.5\" fill=\"#ffffff\" fill-opacity=\"0.75\"/>");
                break;
            }
            case 3:
            {
                var rect = ComputeNodeRect(node, parentRect, 10f, 10f);
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(rect.X)}\" y=\"{FormatSvgFloat(rect.Y)}\" width=\"{FormatSvgFloat(rect.Width)}\" height=\"{FormatSvgFloat(rect.Height)}\" fill=\"none\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1.5\" rx=\"2\"/>");
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(rect.X)}\" y1=\"{FormatSvgFloat(rect.Y)}\" x2=\"{FormatSvgFloat(rect.X + rect.Width)}\" y2=\"{FormatSvgFloat(rect.Y + rect.Height)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1\"/>");
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(rect.X + rect.Width)}\" y1=\"{FormatSvgFloat(rect.Y)}\" x2=\"{FormatSvgFloat(rect.X)}\" y2=\"{FormatSvgFloat(rect.Y + rect.Height)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1\"/>");
                break;
            }
            case 4:
            {
                var rect = ComputeNodeRect(node, parentRect, 16f, 12f);
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(rect.X)}\" y=\"{FormatSvgFloat(rect.Y)}\" width=\"{FormatSvgFloat(rect.Width)}\" height=\"{FormatSvgFloat(rect.Height)}\" fill=\"none\" stroke=\"#7fb3ff\" stroke-opacity=\"0.75\" stroke-dasharray=\"3 2\" stroke-width=\"1\"/>");
                foreach (var child in node.Children)
                {
                    RenderNodeSvg(builder, child, rect);
                }
                break;
            }
            case 5:
            {
                var rect = ComputeNodeRect(node, parentRect, 16f, 12f);
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(rect.X)}\" y=\"{FormatSvgFloat(rect.Y)}\" width=\"{FormatSvgFloat(rect.Width)}\" height=\"{FormatSvgFloat(rect.Height)}\" fill=\"none\" stroke=\"#7fb3ff\" stroke-opacity=\"0.75\" stroke-dasharray=\"3 2\" stroke-width=\"1\"/>");
                foreach (var child in node.Children)
                {
                    RenderNodeSvg(builder, child, rect);
                }
                break;
            }
            case 10:
            {
                var rect = ComputeNodeRect(node, parentRect, 32f, 16f);
                builder.AppendLine($"    <rect x=\"{FormatSvgFloat(rect.X)}\" y=\"{FormatSvgFloat(rect.Y)}\" width=\"{FormatSvgFloat(rect.Width)}\" height=\"{FormatSvgFloat(rect.Height)}\" fill=\"none\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"{ToSvgOpacity(node.Color)}\" stroke-width=\"1\"/>");
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(rect.X + 3)}\" y1=\"{FormatSvgFloat(rect.Y + 4)}\" x2=\"{FormatSvgFloat(rect.X + rect.Width - 3)}\" y2=\"{FormatSvgFloat(rect.Y + 4)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"0.6\" stroke-width=\"1\"/>");
                builder.AppendLine($"    <line x1=\"{FormatSvgFloat(rect.X + 3)}\" y1=\"{FormatSvgFloat(rect.Y + 8)}\" x2=\"{FormatSvgFloat(rect.X + rect.Width - 8)}\" y2=\"{FormatSvgFloat(rect.Y + 8)}\" stroke=\"{ToSvgColor(node.Color)}\" stroke-opacity=\"0.45\" stroke-width=\"1\"/>");
                break;
            }
        }
    }

    private static IEnumerable<ParsedNode> FlattenNodes(IEnumerable<ParsedNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in FlattenNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private static SvgRect ComputeNodeRect(ParsedNode node, SvgRect parentRect, float minWidth, float minHeight)
    {
        var width = Math.Max(minWidth, node.ScaleX * parentRect.Width);
        var height = Math.Max(minHeight, node.ScaleY * parentRect.Height);
        var x = parentRect.X + (parentRect.Width * node.PosX);
        var y = parentRect.Y + (parentRect.Height * node.PosY);

        if (node.AlignmentX == 2)
        {
            x -= width;
        }
        else if (node.AlignmentX == 3)
        {
            x -= width * 0.5f;
        }

        if (node.AlignmentY == 2)
        {
            y -= height;
        }
        else if (node.AlignmentY == 3)
        {
            y -= height * 0.5f;
        }

        return new SvgRect(x, y, width, height);
    }

    private static string ToSvgColor(uint argb)
    {
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static string ToSvgOpacity(uint argb)
    {
        var a = (byte)(argb >> 24);
        return (a / 255f).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatSvgFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float ReadFloat(byte[] bytes, int offset)
    {
        return BitConverter.Int32BitsToSingle(unchecked((int)BitConverter.ToUInt32(bytes, offset)));
    }

    private static string ReadLiteral(byte[] bytes, int offset, int cap)
    {
        var end = offset;
        while (end < bytes.Length && end - offset < cap && bytes[end] != 0)
        {
            end++;
        }

        var raw = Encoding.ASCII.GetString(bytes, offset, Math.Max(0, end - offset));
        var filtered = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is '\t' or '\n' or '\r' || (ch >= ' ' && ch <= '~'))
            {
                filtered.Append(ch);
            }
        }

        return filtered.ToString();
    }

    private static string EscapeSvg(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static int CountAscii(byte[] bytes, string value)
    {
        var count = 0;
        var start = 0;
        while (start < bytes.Length)
        {
            var index = Encoding.ASCII.GetString(bytes).IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            count++;
            start = index + value.Length;
        }

        return count;
    }

    private static readonly string[] s_knownMarkers = ["HBD_END", "END_WIDGET_PCON", "END_WIDGET_CONT"];
    private static readonly ConditionalWeakTable<Hw3dArchive, byte[]> s_archiveBytes = new();

    private sealed record ParsedNode(
        int Type,
        int ControlOffset,
        uint WidgetId,
        uint Flags,
        uint AnimId,
        bool Visible,
        float PosX,
        float PosY,
        float ScaleX,
        float ScaleY,
        uint Color,
        string? Text,
        IReadOnlyList<ParsedNode> Children,
        int AlignmentX,
        int AlignmentY);

    private readonly record struct SvgRect(float X, float Y, float Width, float Height);
}
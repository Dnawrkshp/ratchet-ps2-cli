using RatchetPs2.Core.Ties;
using RatchetPs2.Core.Textures.Png;
using System.Text.Json;
using System.Xml.Linq;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var tiesRoot = Path.Combine(repoRoot, "test-assets", "DL Ties");
var tiePaths = Directory.Exists(tiesRoot)
    ? Directory.EnumerateFiles(tiesRoot, "tie.bin", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .ToArray()
    : [];
var tiePath = Path.Combine(tiesRoot, "09907_26B3", "tie.bin");
if (tiePaths.Length == 0 || !File.Exists(tiePath))
{
    Console.WriteLine("No local DL tie.bin fixture found under test-assets/DL Ties; skipping tie reader tests.");
    return 0;
}

using var input = File.OpenRead(tiePath);
var tie = TieClassReader.Read(input);
var originalBytes = File.ReadAllBytes(tiePath);
var failures = new List<string>();

Expect(tie.ByteLength == 0x3350, $"expected tie size 0x3350, got 0x{tie.ByteLength:X}");
Expect(tie.Header.PacketTableOffsets[0] == 0x100, $"expected LOD0 packet table offset 0x100, got 0x{tie.Header.PacketTableOffsets[0]:X}");
Expect(tie.Header.PacketTableOffsets[1] == 0, $"expected LOD1 packet table offset 0, got 0x{tie.Header.PacketTableOffsets[1]:X}");
Expect(tie.Header.PacketCounts[0] == 8, $"expected 8 LOD0 packets, got {tie.Header.PacketCounts[0]}");
Expect(tie.Header.TextureCount == 5, $"expected 5 textures, got {tie.Header.TextureCount}");
Expect(tie.Header.ShadersOffset == 0x31C0, $"expected shader offset 0x31C0, got 0x{tie.Header.ShadersOffset:X}");
Expect(tie.Header.VertexNormalsOffset == 0x2030, $"expected vertex normals offset 0x2030, got 0x{tie.Header.VertexNormalsOffset:X}");
Expect(tie.Header.VertexNormalsCount == 334, $"expected 334 vertex normals, got {tie.Header.VertexNormalsCount}");
Expect(tie.VertexNormals.Count == tie.Header.VertexNormalsCount, $"expected {tie.Header.VertexNormalsCount} decoded vertex normals, got {tie.VertexNormals.Count}");
Expect(tie.VertexNormals[0].Offset == 0x2040, $"expected first vertex normal offset 0x2040, got 0x{tie.VertexNormals[0].Offset:X}");
Expect(tie.VertexNormals[0].X == 2, $"expected first vertex normal X 2, got {tie.VertexNormals[0].X}");
Expect(tie.VertexNormals[0].Y == 64, $"expected first vertex normal Y 64, got {tie.VertexNormals[0].Y}");
Expect(tie.VertexNormals[0].Z == 72, $"expected first vertex normal Z 72, got {tie.VertexNormals[0].Z}");
Expect(tie.VertexNormals[0].W == 0, $"expected first vertex normal W 0, got {tie.VertexNormals[0].W}");
Expect(tie.VertexNormalRemaps.Count > 0, "expected decoded vertex normal remaps");
Expect(
    tie.VertexNormalRemaps.Any(remap => remap.PacketIndex == 0 && remap.VertexRowIndex == 29 && remap.NormalIndex == 42),
    "expected a vertex-normal remap from source normal 42 to packet 0 vertex row 29");
var uyaTie7539Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "7539", "core.bin");
if (File.Exists(uyaTie7539Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie7539Input = File.OpenRead(uyaTie7539Path);
    var uyaTie7539 = TieClassReader.Read(uyaTie7539Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaNormalStart = checked((int)uyaTie7539.Header.VertexNormalsOffset);
    var uyaNormalEnd = checked(uyaNormalStart + uyaTie7539.Header.VertexNormalsCount * 8);
    Expect(
        uyaTie7539.VertexNormals.Count == uyaTie7539.Header.VertexNormalsCount,
        $"expected UYA 7539 to decode {uyaTie7539.Header.VertexNormalsCount} vertex normal records, got {uyaTie7539.VertexNormals.Count}");
    Expect(
        uyaTie7539.VertexNormals[0].Offset == uyaNormalStart,
        $"expected UYA 7539 vertex normals to start at 0x{uyaNormalStart:X}, got 0x{uyaTie7539.VertexNormals[0].Offset:X}");
    Expect(
        uyaTie7539.VertexNormals[^1].Offset + 8 == uyaNormalEnd,
        $"expected UYA 7539 vertex normals to end at 0x{uyaNormalEnd:X}, got 0x{uyaTie7539.VertexNormals[^1].Offset + 8:X}");
    Expect(
        uyaNormalStart + uyaTie7539.Header.RgbaRemapOffsets[0] == uyaNormalEnd,
        $"expected UYA 7539 first RGBA remap chunk to start after the unpadded normal table at 0x{uyaNormalEnd:X}");
    Expect(
        uyaTie7539.FileSections.Any(section => section.Offset == uyaNormalEnd && section.Name.Contains("rgba-remap-0", StringComparison.Ordinal)),
        "expected UYA 7539 raw sections to label rgba-remap-0 at the resolved normal-table offset");
}
var uyaTie6109Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "6109", "core.bin");
if (File.Exists(uyaTie6109Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie6109Input = File.OpenRead(uyaTie6109Path);
    var uyaTie6109 = TieClassReader.Read(uyaTie6109Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie6109Export = TieGltfExporter.Export(
        uyaTie6109,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    using var uyaTie6109DiagnosticsDocument = JsonDocument.Parse(uyaTie6109Export.DiagnosticsBytes);
    var uyaTie6109Diagnostics = uyaTie6109DiagnosticsDocument.RootElement;
    var uyaTie6109Topology = uyaTie6109.LodTopologies[0];
    Expect(
        uyaTie6109.GlowRgbaRemaps.Count == 1
            && uyaTie6109.GlowRgbaRemaps[0].ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && uyaTie6109.GlowRgbaRemaps[0].ResolvedPacketIndices.SequenceEqual(new[] { 2 })
            && uyaTie6109.GlowRgbaRemaps[0].ResolvedShaderIndex == 1
            && uyaTie6109.GlowRgbaVertices.Count == 20
            && uyaTie6109.GlowRgbaVertices.All(vertex =>
                vertex.StripIndex >= 0
                && vertex.StripIndex < uyaTie6109Topology.Strips.Count
                && vertex.PacketIndex == 2
                && uyaTie6109Topology.Strips[vertex.StripIndex].ShaderIndex == 1)
            && uyaTie6109Diagnostics.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 20
            && uyaTie6109Diagnostics.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 1,
        $"expected UYA 6109 glow marker to resolve to packet 2 shader 1 light panels, got remaps={string.Join("; ", uyaTie6109.GlowRgbaRemaps.Select(remap => $"{remap.Offset:X}/{remap.ResolutionKind}/shader={remap.ResolvedShaderIndex}/packets={string.Join(",", remap.ResolvedPacketIndices)}"))}, vertices={uyaTie6109.GlowRgbaVertices.Count}, export={uyaTie6109Diagnostics.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}");
    var uyaTie6109LightPanelMinimumNormalDot = ReadPrimitiveMinimumNormalFaceDot(
        uyaTie6109Export,
        packetIndex: 2,
        shaderIndex: 1);
    Expect(
        uyaTie6109LightPanelMinimumNormalDot is >= 0.8f,
        $"expected UYA 6109 packet 2 shader 1 light-panel normals to agree with exported faces, got minimum dot {uyaTie6109LightPanelMinimumNormalDot}");
}
var uyaTie591Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "591", "core.bin");
if (File.Exists(uyaTie591Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie591Input = File.OpenRead(uyaTie591Path);
    var uyaTie591 = TieClassReader.Read(uyaTie591Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    const int backfaceCullDistanceBucket = 1;
    var uyaTie591Export = TieGltfExporter.Export(
        uyaTie591,
        "tie.gltf",
        new TieGltfExportOptions
        {
            BufferFileName = "tie.buffer.bin",
            GameProfile = uyaProfile,
            BackfaceCullDistanceBucket = backfaceCullDistanceBucket
        });
    using var uyaTie591Document = JsonDocument.Parse(uyaTie591Export.GltfBytes);
    var uyaTie591Root = uyaTie591Document.RootElement;
    var uyaTie591Materials = uyaTie591Root.GetProperty("materials");
    var uyaTie591BfcByPacket = uyaTie591.PacketTables
        .Single(table => table.LodIndex == 0)
        .Packets
        .ToDictionary(packet => packet.PacketIndex, packet => packet.BfcDistance);
    var sawBackfaceCulledPrimitive = false;
    var sawDoubleSidedPrimitive = false;
    foreach (var primitive in uyaTie591Root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        var packetIndex = extras.GetProperty("PacketIndex").GetInt32();
        var bfcDistance = uyaTie591BfcByPacket[packetIndex];
        var material = uyaTie591Materials[primitive.GetProperty("material").GetInt32()];
        var doubleSided = material.TryGetProperty("doubleSided", out var doubleSidedProperty)
            && doubleSidedProperty.GetBoolean();
        Expect(
            doubleSided == (backfaceCullDistanceBucket >= bfcDistance)
                && extras.GetProperty("BfcDistance").GetInt32() == bfcDistance
                && extras.GetProperty("TieBackfaceCullDistanceBucket").GetInt32() == backfaceCullDistanceBucket
                && extras.GetProperty("TieUsesBackfaceCulling").GetBoolean() == !doubleSided,
            $"expected UYA 591 packet {packetIndex} BFC distance {bfcDistance} to control glTF backface culling");
        sawBackfaceCulledPrimitive |= !doubleSided;
        sawDoubleSidedPrimitive |= doubleSided;
    }

    Expect(
        sawBackfaceCulledPrimitive && sawDoubleSidedPrimitive,
        "expected UYA 591 to retain both backface-culled and double-sided packet materials");
}
var uyaTie6338Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "6338", "core.bin");
if (File.Exists(uyaTie6338Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie6338Input = File.OpenRead(uyaTie6338Path);
    var uyaTie6338 = TieClassReader.Read(uyaTie6338Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie6338Export = TieGltfExporter.Export(
        uyaTie6338,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    ExpectExportWindingMatchesDae(
        uyaTie6338Export,
        Path.Combine(Path.GetDirectoryName(uyaTie6338Path)!, "mesh.dae"),
        "UYA 0x18C2");
}
var uyaTie6055Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "6055", "core.bin");
if (File.Exists(uyaTie6055Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie6055Input = File.OpenRead(uyaTie6055Path);
    var uyaTie6055 = TieClassReader.Read(uyaTie6055Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie6055Export = TieGltfExporter.Export(
        uyaTie6055,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    ExpectExportWindingMatchesDae(
        uyaTie6055Export,
        Path.Combine(Path.GetDirectoryName(uyaTie6055Path)!, "mesh.dae"),
        "UYA 0x17A7");
    using var uyaTie6055DiagnosticsDocument = JsonDocument.Parse(uyaTie6055Export.DiagnosticsBytes);
    Expect(
        uyaTie6055DiagnosticsDocument.RootElement
            .GetProperty("SourceLightingRecipeNormalVertexCount").GetInt32()
            == uyaTie6055.LodTopologies[0].LogicalVertexCount,
        "expected UYA 0x17A7 to resolve every logical vertex from the authored lighting recipe");
    Expect(
        ReadMinimumDuplicatePositionNormalDot(uyaTie6055Export, shaderIndex: 0) is >= 0.999f,
        "expected UYA 0x17A7 trunk packet seams to preserve identical authored normals");
}
var uyaTie777Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "777", "core.bin");
if (File.Exists(uyaTie777Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie777Input = File.OpenRead(uyaTie777Path);
    var uyaTie777 = TieClassReader.Read(uyaTie777Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie777Export = TieGltfExporter.Export(
        uyaTie777,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    Expect(
        ReadPrimitiveMinimumNormalFaceDot(uyaTie777Export, packetIndex: 0, shaderIndex: 1) is >= 0.85f,
        "expected UYA 777 crystal normals to reject mismatched packet-row source normals");
}
var uyaTie623Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "623", "core.bin");
if (File.Exists(uyaTie623Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie623Input = File.OpenRead(uyaTie623Path);
    var uyaTie623 = TieClassReader.Read(uyaTie623Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie623Export = TieGltfExporter.Export(
        uyaTie623,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    Expect(
        ReadPrimitiveMinimumNormalFaceDot(uyaTie623Export, packetIndex: 0, shaderIndex: 0) is >= 0.9f,
        "expected UYA 623 outer arch normals to repair single-corner source-normal outliers");
    Expect(
        ReadPrimitiveMinimumNormalFaceDot(uyaTie623Export, packetIndex: 1, shaderIndex: 0) is >= 0.9f,
        "expected UYA 623 mirrored outer arch normals to repair single-corner source-normal outliers");
    Expect(
        ReadPrimitiveMinimumNormalFaceDot(uyaTie623Export, packetIndex: 2, shaderIndex: 1) is >= 0.65f,
        "expected UYA 623 upper arch normals to avoid dark source-normal patches");
}
var uyaTie472Path = Path.Combine(repoRoot, "test-assets", "UYA Ties", "unsorted", "472", "core.bin");
if (File.Exists(uyaTie472Path))
{
    var uyaProfile = TieGameProfile.Default.WithGameLabel("UYA");
    using var uyaTie472Input = File.OpenRead(uyaTie472Path);
    var uyaTie472 = TieClassReader.Read(uyaTie472Input, TieClassReadOptions.ForGameProfile(uyaProfile));
    var uyaTie472Export = TieGltfExporter.Export(
        uyaTie472,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = uyaProfile });
    Expect(
        ReadPrimitiveMinimumNormalFaceDot(uyaTie472Export, packetIndex: 3, shaderIndex: 2) is >= 0.75f,
        "expected UYA 472 packet 3 shell normals to repair severe single-corner source-normal outliers");
    Expect(
        ReadPrimitiveCopiedNormalMismatchCount(uyaTie472Export, packetIndex: 3, shaderIndex: 2) == 0,
        "expected UYA 472 packet 3 shell normals to repair copied source-normal panels that disagree with the face");
}
Expect(tie.Header.GlowRgba == unchecked((int)0x803360A3), $"expected glow RGBA 0x803360A3, got 0x{unchecked((uint)tie.Header.GlowRgba):X8}");
Expect(tie.GlowRgbaRemaps.Count == 1, $"expected one decoded 09907 glow RGBA remap, got {tie.GlowRgbaRemaps.Count}");
Expect(tie.GlowRgbaRemaps[0].Offset == 0x1150, $"expected 09907 glow RGBA remap offset 0x1150, got 0x{tie.GlowRgbaRemaps[0].Offset:X}");
Expect(tie.GlowRgbaRemaps[0].ResolvedStartOffset == 0x1A80, $"expected 09907 glow RGBA resolved start offset 0x1A80, got 0x{tie.GlowRgbaRemaps[0].ResolvedStartOffset:X}");
Expect(tie.GlowRgbaRemaps[0].EndOffset == 0x1C30, $"expected 09907 glow RGBA remap end offset 0x1C30, got 0x{tie.GlowRgbaRemaps[0].EndOffset:X}");
Expect(tie.GlowRgbaRemaps[0].ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketMultipassRange, $"expected 09907 glow remap to resolve from the multipass packet range, got {tie.GlowRgbaRemaps[0].ResolutionKind}");
Expect(tie.GlowRgbaRemaps[0].ResolvedPacketIndex == 6, $"expected 09907 glow remap to resolve to packet 6, got {tie.GlowRgbaRemaps[0].ResolvedPacketIndex}");
Expect(tie.GlowRgbaRemaps[0].ResolvedPacketCount == 1, $"expected 09907 glow remap to resolve across 1 packet, got {tie.GlowRgbaRemaps[0].ResolvedPacketCount}");
Expect(tie.GlowRgbaVertices.Count == 26, $"expected 09907 glow remap to resolve 26 vertices, got {tie.GlowRgbaVertices.Count}");
Expect(tie.GlowRgbaVertices.All(vertex => vertex.PacketIndex == 6), "expected 09907 glow vertices to be limited to the white stripe packet");
var tie9767Path = Path.Combine(tiesRoot, "09767_2627", "tie.bin");
if (File.Exists(tie9767Path))
{
    using var tie9767Input = File.OpenRead(tie9767Path);
    var tie9767 = TieClassReader.Read(tie9767Input);
    Expect(
        tie9767.GlowRgbaRemaps.Count == 2
            && tie9767.GlowRgbaRemaps.All(remap => remap.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketMultipassRange),
        "expected 09767 glow remaps inside the multipass packet to resolve as whole-packet glow ranges");
    Expect(
        tie9767.GlowRgbaVertices.Count == 22
            && tie9767.GlowRgbaVertices.All(vertex => vertex.PacketIndex == 1),
        $"expected 09767 glow remaps to cover the full packet 1 logical vertex set, got {tie9767.GlowRgbaVertices.Count} vertices");
}
var dlProfile = TieGameProfile.Default.WithGameLabel("DL");
var tie9638Path = Path.Combine(tiesRoot, "ALL DL", "9638", "core.bin");
if (File.Exists(tie9638Path))
{
    using var tie9638Input = File.OpenRead(tie9638Path);
    var tie9638 = TieClassReader.Read(tie9638Input, TieClassReadOptions.ForGameProfile(dlProfile));
    var tie9638Export = TieGltfExporter.Export(
        tie9638,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    ExpectExportWindingMatchesDae(
        tie9638Export,
        Path.Combine(Path.GetDirectoryName(tie9638Path)!, "mesh.dae"),
        "DL 0x25A6");
}
var tie9806Path = Path.Combine(tiesRoot, "ALL DL", "9806", "core.bin");
if (File.Exists(tie9806Path))
{
    using var tie9806Input = File.OpenRead(tie9806Path);
    var tie9806 = TieClassReader.Read(tie9806Input, TieClassReadOptions.ForGameProfile(dlProfile));
    var tie9806Export = TieGltfExporter.Export(
        tie9806,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    using var tie9806Document = JsonDocument.Parse(tie9806Export.GltfBytes);
    var tie9806Root = tie9806Document.RootElement;
    var tie9806Primitives = tie9806Root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray();
    var tie9806Materials = tie9806Root.GetProperty("materials");
    var tie9806MeshExtras = tie9806Root.GetProperty("meshes")[0].GetProperty("extras");
    Expect(
        tie9806Primitives.All(primitive =>
        {
            var extras = primitive.GetProperty("extras");
            var material = tie9806Materials[primitive.GetProperty("material").GetInt32()];
            return extras.GetProperty("BfcDistance").GetInt32() <= 3
                && extras.GetProperty("TieBackfaceCullDistanceBucket").GetInt32() == 3
                && !extras.GetProperty("TieUsesBackfaceCulling").GetBoolean()
                && material.GetProperty("doubleSided").GetBoolean();
        }),
        "expected DL 0x264E low-distance packets to disable culling at the static preview distance");
    Expect(
        MathF.Abs(
            tie9806MeshExtras.GetProperty("ScaledBoundingRadius").GetSingle()
            - tie9806.Header.Scale * tie9806.Header.BoundingSphere.Radius) < 0.0001f,
        "expected DL 0x264E to export the game's scaled culling radius");
}
var tie9312Path = Path.Combine(tiesRoot, "ALL DL", "9312", "core.bin");
if (File.Exists(tie9312Path))
{
    using var tie9312Input = File.OpenRead(tie9312Path);
    var tie9312 = TieClassReader.Read(tie9312Input, TieClassReadOptions.ForGameProfile(dlProfile));
    var tie9312Export = TieGltfExporter.Export(
        tie9312,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    using var tie9312Diagnostics = JsonDocument.Parse(tie9312Export.DiagnosticsBytes);
    var tie9312Root = tie9312Diagnostics.RootElement;
    var tie9312Topology = tie9312.LodTopologies[0];
    Expect(
        tie9312.GlowRgbaRemaps.Count == 2
            && tie9312.GlowRgbaRemaps[0].ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && tie9312.GlowRgbaRemaps[0].ResolvedPacketIndices.SequenceEqual(new[] { 0 })
            && tie9312.GlowRgbaRemaps[0].ResolvedShaderIndex == 0
            && tie9312.GlowRgbaRemaps[1].ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved
            && tie9312.GlowRgbaVertices.Count == 40
            && tie9312.GlowRgbaVertices.All(vertex =>
                vertex.StripIndex >= 0
                && vertex.StripIndex < tie9312Topology.Strips.Count
                && tie9312Topology.Strips[vertex.StripIndex].ShaderIndex == 0)
            && tie9312Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 40,
        $"expected DL 9312 primary glow marker to resolve to packet 0 shader 0, got remaps={string.Join("; ", tie9312.GlowRgbaRemaps.Select(remap => $"{remap.Offset:X}/{remap.ResolutionKind}/shader={remap.ResolvedShaderIndex}/packets={string.Join(",", remap.ResolvedPacketIndices)}"))}, source={tie9312.GlowRgbaVertices.Count}, export={tie9312Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}");
    Expect(
        tie9312Root.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() > 0
            && tie9312Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 1,
        $"expected DL 9312 texture 0 glow to export one emissive primitive, got emitted={tie9312Root.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()}, primitives={tie9312Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var tie9221Path = Path.Combine(tiesRoot, "ALL DL", "9221", "core.bin");
if (File.Exists(tie9221Path))
{
    using var tie9221Input = File.OpenRead(tie9221Path);
    var tie9221 = TieClassReader.Read(tie9221Input, TieClassReadOptions.ForGameProfile(dlProfile));
    var tie9221Topology = tie9221.LodTopologies[0];
    Expect(
        tie9221.GlowRgbaRemaps.Count == 2
            && tie9221.GlowRgbaRemaps.All(remap =>
                remap.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
                && remap.ResolvedPacketIndices.SequenceEqual(new[] { 0 })
                && remap.ResolvedShaderIndex == 1),
        $"expected DL 9221 glow remaps to resolve to packet 0 shader 1, got {string.Join("; ", tie9221.GlowRgbaRemaps.Select(remap => $"{remap.ResolutionKind}/shader={remap.ResolvedShaderIndex}/packets={string.Join(",", remap.ResolvedPacketIndices)}"))}");
    Expect(
        tie9221.GlowRgbaVertices.Count == 16
            && tie9221.GlowRgbaVertices.All(vertex =>
                vertex.StripIndex >= 0
                && vertex.StripIndex < tie9221Topology.Strips.Count
                && tie9221Topology.Strips[vertex.StripIndex].ShaderIndex == 1),
        $"expected DL 9221 glow vertices to stay on shader 1, got {tie9221.GlowRgbaVertices.Count} vertices across strips {string.Join(",", tie9221.GlowRgbaVertices.Select(vertex => vertex.StripIndex).Distinct().OrderBy(index => index))}");
    var tie9221Export = TieGltfExporter.Export(
        tie9221,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    using var tie9221Diagnostics = JsonDocument.Parse(tie9221Export.DiagnosticsBytes);
    var tie9221Root = tie9221Diagnostics.RootElement;
    Expect(
        tie9221Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 16
            && tie9221Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 1,
        $"expected DL 9221 export to emit only the shader 1 glow primitive, got source={tie9221Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, primitives={tie9221Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var tie9117Path = Path.Combine(tiesRoot, "ALL DL", "9117", "core.bin");
if (File.Exists(tie9117Path))
{
    using var tie9117Input = File.OpenRead(tie9117Path);
    var tie9117 = TieClassReader.Read(tie9117Input, TieClassReadOptions.ForGameProfile(dlProfile));
    var tie9117Topology = tie9117.LodTopologies[0];
    Expect(
        tie9117.GlowRgbaRemaps.Count == 1
            && tie9117.GlowRgbaRemaps[0].ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && tie9117.GlowRgbaRemaps[0].ResolvedPacketIndices.SequenceEqual(new[] { 3, 4 })
            && tie9117.GlowRgbaRemaps[0].ResolvedShaderIndex == 6
            && tie9117.GlowRgbaVertices.Count == 40
            && tie9117.GlowRgbaVertices.All(vertex =>
                vertex.PacketIndex is 3 or 4
                && vertex.StripIndex >= 0
                && vertex.StripIndex < tie9117Topology.Strips.Count
                && tie9117Topology.Strips[vertex.StripIndex].ShaderIndex == 6),
        $"expected DL 9117 glow marker to resolve to carried shader 6 on packets 3 and 4, got remaps={string.Join("; ", tie9117.GlowRgbaRemaps.Select(remap => $"{remap.ResolutionKind}/shader={remap.ResolvedShaderIndex}/packets={string.Join(",", remap.ResolvedPacketIndices)}"))}, vertices={tie9117.GlowRgbaVertices.Count}");
    var tie9117Export = TieGltfExporter.Export(
        tie9117,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    using var tie9117Diagnostics = JsonDocument.Parse(tie9117Export.DiagnosticsBytes);
    var tie9117Root = tie9117Diagnostics.RootElement;
    Expect(
        tie9117Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 40
            && tie9117Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 2,
        $"expected DL 9117 export to emit only the texture 6 glow primitives, got source={tie9117Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, primitives={tie9117Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var tie9487Path = Path.Combine(tiesRoot, "ALL DL", "9487", "core.bin");
if (File.Exists(tie9487Path))
{
    using var tie9487Input = File.OpenRead(tie9487Path);
    var tie9487 = TieClassReader.Read(tie9487Input, TieClassReadOptions.ForGameProfile(dlProfile));
    Expect(
        tie9487.GlowRgbaRemaps.Count == 3
            && tie9487.GlowRgbaRemaps.All(remap =>
                remap.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketMultipassSet
                && remap.ResolvedPacketIndices.SequenceEqual(new[] { 1, 27 }))
            && tie9487.GlowRgbaVertices.Count == 90,
        $"expected DL 9487 glow remaps to resolve to multipass glow packets 1 and 27, got remaps={string.Join("; ", tie9487.GlowRgbaRemaps.Select(remap => $"{remap.ResolutionKind}/packets={string.Join(",", remap.ResolvedPacketIndices)}"))}, vertices={tie9487.GlowRgbaVertices.Count}");
    var tie9487Export = TieGltfExporter.Export(
        tie9487,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
    using var tie9487Diagnostics = JsonDocument.Parse(tie9487Export.DiagnosticsBytes);
    var tie9487Root = tie9487Diagnostics.RootElement;
    using var tie9487Gltf = JsonDocument.Parse(tie9487Export.GltfBytes);
    var tie9487EmissiveGroups = tie9487Gltf.RootElement
        .GetProperty("meshes")[0]
        .GetProperty("primitives")
        .EnumerateArray()
        .Where(primitive =>
            primitive.GetProperty("extras").TryGetProperty("GlowRgbaUsesEmission", out var usesEmission)
            && usesEmission.GetBoolean())
        .Select(primitive => (
            PacketIndex: primitive.GetProperty("extras").GetProperty("PacketIndex").GetInt32(),
            ShaderIndex: primitive.GetProperty("extras").GetProperty("ShaderIndex").GetInt32()))
        .OrderBy(group => group.PacketIndex)
        .ThenBy(group => group.ShaderIndex)
        .ToArray();
    Expect(
        tie9487Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 90
            && tie9487Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 3
            && tie9487EmissiveGroups.SequenceEqual(new[] { (1, 1), (1, 6), (27, 6) }),
        $"expected DL 9487 export to emit both packet 1 glow shaders plus packet 27, got source={tie9487Root.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, primitives={tie9487Root.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}, groups={string.Join(",", tie9487EmissiveGroups.Select(group => $"{group.PacketIndex}/{group.ShaderIndex}"))}");
}
var gcPacketStartGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3575", "core.bin");
if (File.Exists(gcPacketStartGlowPath))
{
    using var gcPacketStartGlowInput = File.OpenRead(gcPacketStartGlowPath);
    var gcPacketStartGlow = TieClassReader.Read(gcPacketStartGlowInput);
    var gcPacketStartRemap = gcPacketStartGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3AA0);
    Expect(gcPacketStartRemap is not null, "expected GC 3575 packet-start glow remap 0x3AA0 to decode");
    Expect(
        gcPacketStartRemap!.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange,
        $"expected GC 3575 packet-start glow remap to clamp to packet vertex rows, got {gcPacketStartRemap.ResolutionKind}");
    Expect(
        gcPacketStartRemap.ResolvedStartOffset == 0x3AE0
            && gcPacketStartRemap.EndOffset == 0x3B40
            && gcPacketStartRemap.ResolvedLogicalVertexCount == 6,
        $"expected GC 3575 packet-start glow remap to resolve rows 0..5, got start=0x{gcPacketStartRemap.ResolvedStartOffset:X}, end=0x{gcPacketStartRemap.EndOffset:X}, vertices={gcPacketStartRemap.ResolvedLogicalVertexCount}");
    Expect(
        gcPacketStartGlow.GlowRgbaVertices.Count == 54,
        $"expected GC 3575 glow vertices to include only packet RGBA rows, got {gcPacketStartGlow.GlowRgbaVertices.Count}");
}
var gcCrossPacketGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3702", "core.bin");
if (File.Exists(gcCrossPacketGlowPath))
{
    using var gcCrossPacketGlowInput = File.OpenRead(gcCrossPacketGlowPath);
    var gcCrossPacketGlow = TieClassReader.Read(gcCrossPacketGlowInput);
    var gcCrossPacketRemap = gcCrossPacketGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x1C00);
    Expect(gcCrossPacketRemap is not null, "expected GC 3702 cross-packet glow remap 0x1C00 to decode");
    Expect(
        gcCrossPacketRemap!.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange,
        $"expected GC 3702 cross-packet glow remap to resolve from later packet vertex rows, got {gcCrossPacketRemap.ResolutionKind}");
    Expect(
        gcCrossPacketRemap.ResolvedPacketIndex == 6
            && gcCrossPacketRemap.ResolvedStartOffset == 0x1C80
            && gcCrossPacketRemap.EndOffset == 0x1CB0
            && gcCrossPacketRemap.ResolvedLogicalVertexCount == 3,
        $"expected GC 3702 cross-packet glow remap to resolve LOD0 packet 6 rows 0..2, got packet={gcCrossPacketRemap.ResolvedPacketIndex}, start=0x{gcCrossPacketRemap.ResolvedStartOffset:X}, end=0x{gcCrossPacketRemap.EndOffset:X}, vertices={gcCrossPacketRemap.ResolvedLogicalVertexCount}");
    Expect(
        gcCrossPacketGlow.GlowRgbaVertices.Count == 58,
        $"expected GC 3702 glow vertices to include only packet RGBA rows, got {gcCrossPacketGlow.GlowRgbaVertices.Count}");
}
var gcMultipassMarkerGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3492", "core.bin");
if (File.Exists(gcMultipassMarkerGlowPath))
{
    using var gcMultipassMarkerGlowInput = File.OpenRead(gcMultipassMarkerGlowPath);
    var gcMultipassMarkerGlow = TieClassReader.Read(gcMultipassMarkerGlowInput);
    var gcMultipassMarkerRemap = gcMultipassMarkerGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0xE60);
    Expect(gcMultipassMarkerRemap is not null, "expected GC 3492 multipass-tail glow remap 0xE60 to decode");
    Expect(
        gcMultipassMarkerGlow.GlowRgbaRemaps.All(remap => remap.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange),
        "expected GC 3492 multipass-tail glow remaps to resolve as packet marker ranges");
    Expect(
        gcMultipassMarkerRemap!.ResolvedPacketIndex == 2
            && gcMultipassMarkerRemap.ResolvedStartOffset == 0xA80
            && gcMultipassMarkerRemap.EndOffset == 0xD50
            && gcMultipassMarkerRemap.ResolvedLogicalVertexCount == 48,
        $"expected GC 3492 multipass-tail glow remap to resolve LOD0 packet 2, got packet={gcMultipassMarkerRemap.ResolvedPacketIndex}, start=0x{gcMultipassMarkerRemap.ResolvedStartOffset:X}, end=0x{gcMultipassMarkerRemap.EndOffset:X}, vertices={gcMultipassMarkerRemap.ResolvedLogicalVertexCount}");
    Expect(
        gcMultipassMarkerGlow.GlowRgbaVertices.Count == 48,
        $"expected GC 3492 multipass-tail glow vertices to resolve only packet RGBA rows, got {gcMultipassMarkerGlow.GlowRgbaVertices.Count}");
}
var gcPrePacketTableGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "2868", "core.bin");
if (File.Exists(gcPrePacketTableGlowPath))
{
    using var gcPrePacketTableGlowInput = File.OpenRead(gcPrePacketTableGlowPath);
    var gcPrePacketTableGlow = TieClassReader.Read(gcPrePacketTableGlowInput);
    var gcPrePacketTableRemap = gcPrePacketTableGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x100);
    Expect(gcPrePacketTableRemap is not null, "expected GC 2868 packet-table glow remap 0x100 to decode");
    Expect(
        gcPrePacketTableRemap!.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange
            && gcPrePacketTableRemap.ResolvedPacketIndex == 0
            && gcPrePacketTableGlow.GlowRgbaVertices.Count == 24,
        $"expected GC 2868 packet-table glow remap to resolve the first packet, got kind={gcPrePacketTableRemap.ResolutionKind}, packet={gcPrePacketTableRemap.ResolvedPacketIndex}, vertices={gcPrePacketTableGlow.GlowRgbaVertices.Count}");
}
var gcPreDataGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3169", "core.bin");
if (File.Exists(gcPreDataGlowPath))
{
    using var gcPreDataGlowInput = File.OpenRead(gcPreDataGlowPath);
    var gcPreDataGlow = TieClassReader.Read(gcPreDataGlowInput);
    var gcPreDataShortMarker = gcPreDataGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0xC0);
    var gcPreDataResolvedMarker = gcPreDataGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0xE0);
    Expect(
        gcPreDataShortMarker?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved,
        $"expected GC 3169 short pre-data marker to remain unresolved, got {gcPreDataShortMarker?.ResolutionKind}");
    Expect(
        gcPreDataResolvedMarker?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange
            && gcPreDataResolvedMarker.ResolvedPacketIndex == 0
            && gcPreDataResolvedMarker.EndOffset == 0x1E0
            && gcPreDataGlow.GlowRgbaVertices.Count == 8,
        $"expected GC 3169 pre-data marker to resolve the first packet, got kind={gcPreDataResolvedMarker?.ResolutionKind}, packet={gcPreDataResolvedMarker?.ResolvedPacketIndex}, vertices={gcPreDataGlow.GlowRgbaVertices.Count}");
}
var gcPlatformGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3947", "core.bin");
if (File.Exists(gcPlatformGlowPath))
{
    using var gcPlatformGlowInput = File.OpenRead(gcPlatformGlowPath);
    var gcPlatformGlow = TieClassReader.Read(gcPlatformGlowInput);
    var gcPlatformGlowRemap = gcPlatformGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x700);
    var gcPlatformGlowBoundary = gcPlatformGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x7A0);
    var gcPlatformGlowTopology = gcPlatformGlow.LodTopologies[0];
    Expect(
        gcPlatformGlowRemap is not null
            && gcPlatformGlowRemap.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcPlatformGlowRemap.ResolvedShaderIndex == 3
            && gcPlatformGlowRemap.ResolvedPacketIndices.SequenceEqual(new[] { 1, 2, 3 }),
        $"expected GC 3947 glow remap 0x700 to resolve to shader 3 packets 1,2,3, got kind={gcPlatformGlowRemap?.ResolutionKind}, shader={gcPlatformGlowRemap?.ResolvedShaderIndex}, packets={string.Join(",", gcPlatformGlowRemap?.ResolvedPacketIndices ?? [])}");
    Expect(
        gcPlatformGlowBoundary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved,
        $"expected GC 3947 glow boundary 0x7A0 to remain unresolved, got {gcPlatformGlowBoundary?.ResolutionKind}");
    Expect(
        gcPlatformGlow.GlowRgbaVertices.All(vertex =>
            vertex.StripIndex >= 0
            && vertex.StripIndex < gcPlatformGlowTopology.Strips.Count
            && gcPlatformGlowTopology.Strips[vertex.StripIndex].ShaderIndex == 3),
        "expected GC 3947 glow vertices to remain limited to shader 3 edge-light strips");
    var gcPlatformGlowExport = TieGltfExporter.Export(
        gcPlatformGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcPlatformGlowDiagnostics = JsonDocument.Parse(gcPlatformGlowExport.DiagnosticsBytes);
    var gcPlatformGlowRoot = gcPlatformGlowDiagnostics.RootElement;
    Expect(
        gcPlatformGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 72,
        $"expected GC 3947 to resolve 72 source glow vertices, got {gcPlatformGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}");
    var gcPlatformGlowEmissionVertexCount = gcPlatformGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32();
    Expect(
        gcPlatformGlowEmissionVertexCount >= 72
            && gcPlatformGlowEmissionVertexCount <= 144,
        $"expected GC 3947 export to preserve per-vertex glow without triangle-wide promotion, got {gcPlatformGlowEmissionVertexCount} emitted vertices");
}
var gcLampGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3938", "core.bin");
if (File.Exists(gcLampGlowPath))
{
    using var gcLampGlowInput = File.OpenRead(gcLampGlowPath);
    var gcLampGlow = TieClassReader.Read(gcLampGlowInput);
    var gcLampGlowPrimary = gcLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x1C50);
    var gcLampGlowBoundary = gcLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x1CC0);
    var gcLampGlowTailBridge = gcLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x1D00);
    Expect(
        gcLampGlowPrimary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved,
        $"expected GC 3938 source-packet tail marker 0x1C50 to be suppressed, got kind={gcLampGlowPrimary?.ResolutionKind}, packet={gcLampGlowPrimary?.ResolvedPacketIndex}");
    Expect(
        gcLampGlowBoundary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcLampGlowBoundary.ResolvedPacketIndices.SequenceEqual(new[] { 7 })
            && gcLampGlowBoundary.ResolvedShaderIndex == 4,
        $"expected GC 3938 mid-tail marker 0x1CC0 to bridge into packet 7 shader 4, got kind={gcLampGlowBoundary?.ResolutionKind}, packets={string.Join(",", gcLampGlowBoundary?.ResolvedPacketIndices ?? [])}, shader={gcLampGlowBoundary?.ResolvedShaderIndex}");
    Expect(
        gcLampGlowTailBridge?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcLampGlowTailBridge.ResolvedPacketIndices.SequenceEqual(new[] { 7 })
            && gcLampGlowTailBridge.ResolvedShaderIndex == 4,
        $"expected GC 3938 tail marker 0x1D00 to bridge into packet 7 shader 4, got kind={gcLampGlowTailBridge?.ResolutionKind}, packets={string.Join(",", gcLampGlowTailBridge?.ResolvedPacketIndices ?? [])}, shader={gcLampGlowTailBridge?.ResolvedShaderIndex}");
    Expect(
        gcLampGlow.GlowRgbaVertices.Count == 24
            && gcLampGlow.GlowRgbaVertices.All(vertex => vertex.PacketIndex == 7)
            && !gcLampGlow.GlowRgbaVertices.Any(vertex => vertex.PacketIndex == 6),
        $"expected GC 3938 glow vertices to stay on lamp-top packet 7, got {gcLampGlow.GlowRgbaVertices.Count} vertices across packets {string.Join(",", gcLampGlow.GlowRgbaVertices.Select(vertex => vertex.PacketIndex).Distinct().OrderBy(index => index))}");
    var gcLampGlowExport = TieGltfExporter.Export(
        gcLampGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcLampGlowDiagnostics = JsonDocument.Parse(gcLampGlowExport.DiagnosticsBytes);
    var gcLampGlowRoot = gcLampGlowDiagnostics.RootElement;
    Expect(
        gcLampGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 24,
        $"expected GC 3938 to resolve 24 source glow vertices, got {gcLampGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}");
    Expect(
        gcLampGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() == 40
            && gcLampGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 1,
        $"expected GC 3938 export to emit only the lamp-top primitive, got {gcLampGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()} emitted vertices across {gcLampGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()} primitives");
}
var gcMixedShaderLampGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3161", "core.bin");
if (File.Exists(gcMixedShaderLampGlowPath))
{
    using var gcMixedShaderLampGlowInput = File.OpenRead(gcMixedShaderLampGlowPath);
    var gcMixedShaderLampGlow = TieClassReader.Read(gcMixedShaderLampGlowInput);
    var gcMixedShaderLampPrimary = gcMixedShaderLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x43D0);
    var gcMixedShaderLampMiddle = gcMixedShaderLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x4770);
    var gcMixedShaderLampTail = gcMixedShaderLampGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x4930);
    var gcMixedShaderLampPackets = new[] { 5, 6, 8, 14, 18, 19, 25, 26 };
    Expect(
        gcMixedShaderLampPrimary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcMixedShaderLampPrimary.ResolvedPacketIndices.SequenceEqual(gcMixedShaderLampPackets)
            && gcMixedShaderLampPrimary.ResolvedShaderIndex == 3,
        $"expected GC 3161 glow marker 0x43D0 to resolve to repeated shader 3 lamp packets, got kind={gcMixedShaderLampPrimary?.ResolutionKind}, packets={string.Join(",", gcMixedShaderLampPrimary?.ResolvedPacketIndices ?? [])}, shader={gcMixedShaderLampPrimary?.ResolvedShaderIndex}");
    Expect(
        gcMixedShaderLampMiddle?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcMixedShaderLampMiddle.ResolvedPacketIndices.SequenceEqual(gcMixedShaderLampPackets)
            && gcMixedShaderLampMiddle.ResolvedShaderIndex == 3
            && gcMixedShaderLampTail?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcMixedShaderLampTail.ResolvedPacketIndices.SequenceEqual(gcMixedShaderLampPackets)
            && gcMixedShaderLampTail.ResolvedShaderIndex == 3,
        $"expected GC 3161 glow markers 0x4770/0x4930 to resolve to repeated shader 3 lamp packets, got middle kind={gcMixedShaderLampMiddle?.ResolutionKind}, packets={string.Join(",", gcMixedShaderLampMiddle?.ResolvedPacketIndices ?? [])}, shader={gcMixedShaderLampMiddle?.ResolvedShaderIndex}; tail kind={gcMixedShaderLampTail?.ResolutionKind}, packets={string.Join(",", gcMixedShaderLampTail?.ResolvedPacketIndices ?? [])}, shader={gcMixedShaderLampTail?.ResolvedShaderIndex}");
    var gcMixedShaderLampTopology = gcMixedShaderLampGlow.LodTopologies[0];
    var gcMixedShaderLampGlowPairs = gcMixedShaderLampGlow.GlowRgbaVertices
        .Where(vertex => vertex.LodIndex == 0 && vertex.StripIndex >= 0 && vertex.StripIndex < gcMixedShaderLampTopology.Strips.Count)
        .Select(vertex => (vertex.PacketIndex, ShaderIndex: gcMixedShaderLampTopology.Strips[vertex.StripIndex].ShaderIndex ?? -1))
        .Distinct()
        .OrderBy(pair => pair.PacketIndex)
        .ThenBy(pair => pair.ShaderIndex)
        .ToArray();
    Expect(
        gcMixedShaderLampGlow.GlowRgbaVertices.Count == 376
            && gcMixedShaderLampGlowPairs.SequenceEqual(gcMixedShaderLampPackets.Select(packetIndex => (packetIndex, ShaderIndex: 3))),
        $"expected GC 3161 glow vertices to stay on repeated shader 3 lamp packets, got {gcMixedShaderLampGlow.GlowRgbaVertices.Count} vertices across {string.Join(",", gcMixedShaderLampGlowPairs.Select(pair => $"{pair.PacketIndex}/{pair.ShaderIndex}"))}");
    var gcMixedShaderLampGlowExport = TieGltfExporter.Export(
        gcMixedShaderLampGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcMixedShaderLampGlowDiagnostics = JsonDocument.Parse(gcMixedShaderLampGlowExport.DiagnosticsBytes);
    var gcMixedShaderLampGlowRoot = gcMixedShaderLampGlowDiagnostics.RootElement;
    Expect(
        gcMixedShaderLampGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 376,
        $"expected GC 3161 to resolve 376 source glow vertices, got {gcMixedShaderLampGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}");
    Expect(
        gcMixedShaderLampGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() == 636
            && gcMixedShaderLampGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 8,
        $"expected GC 3161 export to emit only the repeated shader 3 lamp primitives, got {gcMixedShaderLampGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()} emitted vertices across {gcMixedShaderLampGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()} primitives");
}
var gcBroadShaderGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3064", "core.bin");
if (File.Exists(gcBroadShaderGlowPath))
{
    using var gcBroadShaderGlowInput = File.OpenRead(gcBroadShaderGlowPath);
    var gcBroadShaderGlow = TieClassReader.Read(gcBroadShaderGlowInput);
    var gcBroadShaderRemap = gcBroadShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3C80);
    var gcBroadShaderBoundary = gcBroadShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3CA0);
    Expect(
        gcBroadShaderRemap?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcBroadShaderRemap.ResolvedShaderIndex == 3
            && gcBroadShaderRemap.ResolvedPacketIndices.SequenceEqual(new[] { 13 })
            && gcBroadShaderRemap.ResolvedStartOffset == 0x3C80
            && gcBroadShaderRemap.EndOffset == 0x3CA0
            && gcBroadShaderRemap.ResolvedVertexRowCount == 2,
        $"expected GC 3064 primary shader glow to stay bounded to the source shader row window, got kind={gcBroadShaderRemap?.ResolutionKind}, shader={gcBroadShaderRemap?.ResolvedShaderIndex}, packets={string.Join(",", gcBroadShaderRemap?.ResolvedPacketIndices ?? [])}, start=0x{gcBroadShaderRemap?.ResolvedStartOffset:X}, end=0x{gcBroadShaderRemap?.EndOffset:X}, rows={gcBroadShaderRemap?.ResolvedVertexRowCount}");
    Expect(
        gcBroadShaderBoundary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved
            && gcBroadShaderGlow.GlowRgbaVertices.Count == 1,
        $"expected GC 3064 boundary marker to remain unresolved and one bounded glow vertex, got boundary={gcBroadShaderBoundary?.ResolutionKind}, vertices={gcBroadShaderGlow.GlowRgbaVertices.Count}");
}
var gcBoundedShaderGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "2786", "core.bin");
if (File.Exists(gcBoundedShaderGlowPath))
{
    using var gcBoundedShaderGlowInput = File.OpenRead(gcBoundedShaderGlowPath);
    var gcBoundedShaderGlow = TieClassReader.Read(gcBoundedShaderGlowInput);
    var gcBoundedShaderRemap = gcBoundedShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x21F0);
    var gcBoundedShaderBoundary = gcBoundedShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x2230);
    Expect(
        gcBoundedShaderRemap?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcBoundedShaderRemap.ResolvedShaderIndex == 2
            && gcBoundedShaderRemap.ResolvedPacketIndices.SequenceEqual(new[] { 7 })
            && gcBoundedShaderRemap.ResolvedStartOffset == 0x21F0
            && gcBoundedShaderRemap.EndOffset == 0x2230
            && gcBoundedShaderRemap.StartVertexRowIndex == 4
            && gcBoundedShaderRemap.EndVertexRowIndexExclusive == 8,
        $"expected GC 2786 primary shader glow to stay bounded to packet 7 rows 4..7, got kind={gcBoundedShaderRemap?.ResolutionKind}, shader={gcBoundedShaderRemap?.ResolvedShaderIndex}, packets={string.Join(",", gcBoundedShaderRemap?.ResolvedPacketIndices ?? [])}, start=0x{gcBoundedShaderRemap?.ResolvedStartOffset:X}, end=0x{gcBoundedShaderRemap?.EndOffset:X}, rows={gcBoundedShaderRemap?.StartVertexRowIndex}..{gcBoundedShaderRemap?.EndVertexRowIndexExclusive}");
    Expect(
        gcBoundedShaderBoundary?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved
            && gcBoundedShaderGlow.GlowRgbaVertices.Count == 8
            && gcBoundedShaderGlow.GlowRgbaVertices.All(vertex => vertex.PacketIndex == 7),
        $"expected GC 2786 boundary marker to remain unresolved and glow vertices to stay on packet 7, got boundary={gcBoundedShaderBoundary?.ResolutionKind}, vertices={gcBoundedShaderGlow.GlowRgbaVertices.Count}, packets={string.Join(",", gcBoundedShaderGlow.GlowRgbaVertices.Select(vertex => vertex.PacketIndex).Distinct().OrderBy(index => index))}");
    var gcBoundedShaderGlowExport = TieGltfExporter.Export(
        gcBoundedShaderGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcBoundedShaderGlowDiagnostics = JsonDocument.Parse(gcBoundedShaderGlowExport.DiagnosticsBytes);
    var gcBoundedShaderGlowRoot = gcBoundedShaderGlowDiagnostics.RootElement;
    Expect(
        gcBoundedShaderGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 8
            && gcBoundedShaderGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() == 16
            && gcBoundedShaderGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 0,
        $"expected GC 2786 export to keep glow as a sparse vertex attribute without whole-primitive emission, got source={gcBoundedShaderGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, emitted={gcBoundedShaderGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()}, primitives={gcBoundedShaderGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var gcWhiteGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "2823", "core.bin");
if (File.Exists(gcWhiteGlowPath))
{
    using var gcWhiteGlowInput = File.OpenRead(gcWhiteGlowPath);
    var gcWhiteGlow = TieClassReader.Read(gcWhiteGlowInput);
    var gcWhiteGlowRemap = gcWhiteGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x1750);
    Expect(
        gcWhiteGlow.Header.GlowRgba == unchecked((int)0x80FFFFFF)
            && gcWhiteGlowRemap?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcWhiteGlow.GlowRgbaVertices.Count == 216,
        $"expected GC 2823 to resolve white glow RGBA to 216 source vertices, got rgba=0x{gcWhiteGlow.Header.GlowRgba:X8}, kind={gcWhiteGlowRemap?.ResolutionKind}, vertices={gcWhiteGlow.GlowRgbaVertices.Count}");
    var gcWhiteGlowExport = TieGltfExporter.Export(
        gcWhiteGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcWhiteGlowDiagnostics = JsonDocument.Parse(gcWhiteGlowExport.DiagnosticsBytes);
    var gcWhiteGlowRoot = gcWhiteGlowDiagnostics.RootElement;
    Expect(
        gcWhiteGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 216
            && gcWhiteGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() > 0
            && gcWhiteGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 5,
        $"expected GC 2823 white glow not to collide with no-glow sentinel, got source={gcWhiteGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, emitted={gcWhiteGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()}, primitives={gcWhiteGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var gcTailBridgeSuppressGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3440", "core.bin");
if (File.Exists(gcTailBridgeSuppressGlowPath))
{
    using var gcTailBridgeSuppressGlowInput = File.OpenRead(gcTailBridgeSuppressGlowPath);
    var gcTailBridgeSuppressGlow = TieClassReader.Read(gcTailBridgeSuppressGlowInput);
    var gcTailBridgeSuppressLocal = gcTailBridgeSuppressGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x30B0);
    var gcTailBridgeSuppressFirstBridge = gcTailBridgeSuppressGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3190);
    var gcTailBridgeSuppressSecondBridge = gcTailBridgeSuppressGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x31F0);
    var gcTailBridgeSuppressPackets = new[] { 9, 10, 11, 12 };
    Expect(
        gcTailBridgeSuppressLocal?.ResolutionKind == TieGlowRgbaRemapResolutionKind.Unresolved,
        $"expected GC 3440 local source-packet shader marker to be suppressed before the tail bridge, got {gcTailBridgeSuppressLocal?.ResolutionKind}");
    Expect(
        gcTailBridgeSuppressFirstBridge?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcTailBridgeSuppressFirstBridge.ResolvedShaderIndex == 5
            && gcTailBridgeSuppressFirstBridge.ResolvedPacketIndices.SequenceEqual(gcTailBridgeSuppressPackets)
            && gcTailBridgeSuppressSecondBridge?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcTailBridgeSuppressSecondBridge.ResolvedShaderIndex == 5
            && gcTailBridgeSuppressSecondBridge.ResolvedPacketIndices.SequenceEqual(gcTailBridgeSuppressPackets),
        $"expected GC 3440 tail bridges to resolve to carried shader 5 yellow face packets, got first={string.Join(",", gcTailBridgeSuppressFirstBridge?.ResolvedPacketIndices ?? [])}/{gcTailBridgeSuppressFirstBridge?.ResolvedShaderIndex}, second={string.Join(",", gcTailBridgeSuppressSecondBridge?.ResolvedPacketIndices ?? [])}/{gcTailBridgeSuppressSecondBridge?.ResolvedShaderIndex}");
    Expect(
        gcTailBridgeSuppressGlow.GlowRgbaVertices.Count == 132
            && gcTailBridgeSuppressGlow.GlowRgbaVertices
                .Select(vertex => vertex.PacketIndex)
                .Distinct()
                .OrderBy(index => index)
                .SequenceEqual(gcTailBridgeSuppressPackets),
        $"expected GC 3440 glow vertices to stay on carried shader 5 yellow face packets, got {gcTailBridgeSuppressGlow.GlowRgbaVertices.Count} vertices across packets {string.Join(",", gcTailBridgeSuppressGlow.GlowRgbaVertices.Select(vertex => vertex.PacketIndex).Distinct().OrderBy(index => index))}");
    var gcTailBridgeSuppressGlowExport = TieGltfExporter.Export(
        gcTailBridgeSuppressGlow,
        "tie.gltf",
        new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
    using var gcTailBridgeSuppressGlowDiagnostics = JsonDocument.Parse(gcTailBridgeSuppressGlowExport.DiagnosticsBytes);
    var gcTailBridgeSuppressGlowRoot = gcTailBridgeSuppressGlowDiagnostics.RootElement;
    Expect(
        gcTailBridgeSuppressGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == 132
            && gcTailBridgeSuppressGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() == 140
            && gcTailBridgeSuppressGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32() == 4,
        $"expected GC 3440 export to emit the carried shader 5 yellow face primitives, got source={gcTailBridgeSuppressGlowRoot.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32()}, emitted={gcTailBridgeSuppressGlowRoot.GetProperty("GlowRgbaEmissionVertexCount").GetInt32()}, primitives={gcTailBridgeSuppressGlowRoot.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32()}");
}
var gcSparseShaderGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "3770", "core.bin");
if (File.Exists(gcSparseShaderGlowPath))
{
    using var gcSparseShaderGlowInput = File.OpenRead(gcSparseShaderGlowPath);
    var gcSparseShaderGlow = TieClassReader.Read(gcSparseShaderGlowInput);
    var gcSparseShaderRemap = gcSparseShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3FC0);
    Expect(
        gcSparseShaderRemap?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcSparseShaderRemap.ResolvedShaderIndex == 9
            && gcSparseShaderRemap.ResolvedPacketIndices.SequenceEqual(new[] { 13 })
            && gcSparseShaderGlow.GlowRgbaVertices.Count == 6,
        $"expected GC 3770 shader glow to stay on source packet 13, got kind={gcSparseShaderRemap?.ResolutionKind}, shader={gcSparseShaderRemap?.ResolvedShaderIndex}, packets={string.Join(",", gcSparseShaderRemap?.ResolvedPacketIndices ?? [])}, vertices={gcSparseShaderGlow.GlowRgbaVertices.Count}");
}
var gcLocalRepeatedShaderGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "4220", "core.bin");
if (File.Exists(gcLocalRepeatedShaderGlowPath))
{
    using var gcLocalRepeatedShaderGlowInput = File.OpenRead(gcLocalRepeatedShaderGlowPath);
    var gcLocalRepeatedShaderGlow = TieClassReader.Read(gcLocalRepeatedShaderGlowInput);
    var gcLocalRepeatedShaderFirst = gcLocalRepeatedShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x3E60);
    var gcLocalRepeatedShaderSecond = gcLocalRepeatedShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x4920);
    var gcLocalRepeatedShaderTail = gcLocalRepeatedShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x4CA0);
    Expect(
        gcLocalRepeatedShaderFirst?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcLocalRepeatedShaderFirst.ResolvedShaderIndex == 5
            && gcLocalRepeatedShaderFirst.ResolvedPacketIndices.SequenceEqual(new[] { 13 })
            && gcLocalRepeatedShaderSecond?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcLocalRepeatedShaderSecond.ResolvedShaderIndex == 5
            && gcLocalRepeatedShaderSecond.ResolvedPacketIndices.SequenceEqual(new[] { 15 }),
        $"expected GC 4220 same-shader local markers to stay local, got first={string.Join(",", gcLocalRepeatedShaderFirst?.ResolvedPacketIndices ?? [])}/{gcLocalRepeatedShaderFirst?.ResolvedShaderIndex}, second={string.Join(",", gcLocalRepeatedShaderSecond?.ResolvedPacketIndices ?? [])}/{gcLocalRepeatedShaderSecond?.ResolvedShaderIndex}");
    Expect(
        gcLocalRepeatedShaderTail?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketVertexRowRange
            && gcLocalRepeatedShaderTail.ResolvedPacketIndices.SequenceEqual(new[] { 16 })
            && gcLocalRepeatedShaderGlow.GlowRgbaVertices.Count == 149,
        $"expected GC 4220 tail range to stay on packet 16 and total 149 local glow vertices, got tail={gcLocalRepeatedShaderTail?.ResolutionKind}, packets={string.Join(",", gcLocalRepeatedShaderTail?.ResolvedPacketIndices ?? [])}, vertices={gcLocalRepeatedShaderGlow.GlowRgbaVertices.Count}");
}
var gcBaseShaderGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "4236", "core.bin");
if (File.Exists(gcBaseShaderGlowPath))
{
    using var gcBaseShaderGlowInput = File.OpenRead(gcBaseShaderGlowPath);
    var gcBaseShaderGlow = TieClassReader.Read(gcBaseShaderGlowInput);
    var gcBaseShaderFirst = gcBaseShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x5380);
    var gcBaseShaderMiddle = gcBaseShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x5690);
    var gcBaseShaderTail = gcBaseShaderGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == 0x57F0);
    Expect(
        gcBaseShaderFirst?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcBaseShaderFirst.ResolvedShaderIndex == 0
            && gcBaseShaderFirst.ResolvedPacketIndices.SequenceEqual(new[] { 17 })
            && gcBaseShaderTail?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketShaderRange
            && gcBaseShaderTail.ResolvedShaderIndex == 0
            && gcBaseShaderTail.ResolvedPacketIndices.SequenceEqual(new[] { 18 }),
        $"expected GC 4236 reused base shader glow to stay local, got first={string.Join(",", gcBaseShaderFirst?.ResolvedPacketIndices ?? [])}/{gcBaseShaderFirst?.ResolvedShaderIndex}, tail={string.Join(",", gcBaseShaderTail?.ResolvedPacketIndices ?? [])}/{gcBaseShaderTail?.ResolvedShaderIndex}");
    Expect(
        gcBaseShaderMiddle?.ResolutionKind == TieGlowRgbaRemapResolutionKind.PacketDataOffsetRange
            && gcBaseShaderMiddle.ResolvedPacketIndices.SequenceEqual(new[] { 18 })
            && gcBaseShaderGlow.GlowRgbaVertices.Count == 108,
        $"expected GC 4236 scissor marker to stay on packet 18 and total 108 local glow vertices, got middle={gcBaseShaderMiddle?.ResolutionKind}, packets={string.Join(",", gcBaseShaderMiddle?.ResolvedPacketIndices ?? [])}, vertices={gcBaseShaderGlow.GlowRgbaVertices.Count}");
}
foreach (var item in new[]
{
    (Id: "3544", Offset: 0x2E90),
    (Id: "3818", Offset: 0x54E0),
    (Id: "2703", Offset: 0x1740),
    (Id: "2705", Offset: 0x25A0)
})
{
    var gcBroadScissorGlowPath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", item.Id, "core.bin");
    if (!File.Exists(gcBroadScissorGlowPath))
    {
        continue;
    }

    using var gcBroadScissorGlowInput = File.OpenRead(gcBroadScissorGlowPath);
    var gcBroadScissorGlow = TieClassReader.Read(gcBroadScissorGlowInput);
    var gcBroadScissorRemap = gcBroadScissorGlow.GlowRgbaRemaps.FirstOrDefault(remap => remap.Offset == item.Offset);
    Expect(
        gcBroadScissorRemap is not null
            && gcBroadScissorRemap.ResolvedPacketCount < 5,
        $"expected GC {item.Id} scissor marker 0x{item.Offset:X} not to expand into a broad packet range, got kind={gcBroadScissorRemap?.ResolutionKind}, packets={string.Join(",", gcBroadScissorRemap?.ResolvedPacketIndices ?? [])}");
}
Expect((ushort)tie.Header.OClass == 0x26B3, $"expected o_class 0x26B3, got 0x{(ushort)tie.Header.OClass:X4}");
Expect(tie.Header.TClass == 0, $"expected t_class 0, got {tie.Header.TClass}");
Expect(tie.Header.Lods[0].VertexCount == 436, $"expected LOD0 vertex count 436, got {tie.Header.Lods[0].VertexCount}");
Expect(tie.Header.Lods[0].TriangleCount == 292, $"expected LOD0 triangle count 292, got {tie.Header.Lods[0].TriangleCount}");
Expect(tie.PacketTables[0].Packets.Count == 8, $"expected 8 parsed LOD0 packets, got {tie.PacketTables[0].Packets.Count}");
Expect(tie.PacketTables[0].Packets[0].DataOffset == 0x80, $"expected first packet data offset 0x80, got 0x{tie.PacketTables[0].Packets[0].DataOffset:X}");
Expect(tie.PacketTables[0].Packets[0].AbsoluteDataOffset == 0x180, $"expected first packet absolute data offset 0x180, got 0x{tie.PacketTables[0].Packets[0].AbsoluteDataOffset:X}");
Expect(tie.PacketDataBlocks.Count == 8, $"expected 8 packet data blocks, got {tie.PacketDataBlocks.Count}");
var firstPacketBlock = tie.PacketDataBlocks[0];
Expect(firstPacketBlock.Offset == 0x180, $"expected first packet data block offset 0x180, got 0x{firstPacketBlock.Offset:X}");
Expect(firstPacketBlock.Length == 0x400, $"expected first packet data block length 0x400, got 0x{firstPacketBlock.Length:X}");
Expect(firstPacketBlock.QwordCount == 64, $"expected first packet qword count 64, got {firstPacketBlock.QwordCount}");
Expect(firstPacketBlock.Regions.Count == 4, $"expected first packet to have 4 regions, got {firstPacketBlock.Regions.Count}");
Expect(firstPacketBlock.Regions[0].Name == "setup-rows", $"expected first region setup-rows, got {firstPacketBlock.Regions[0].Name}");
Expect(firstPacketBlock.Regions[1].Name == "control-region", $"expected second region control-region, got {firstPacketBlock.Regions[1].Name}");
Expect(firstPacketBlock.Regions[2].Name == "vertex-rows", $"expected third region vertex-rows, got {firstPacketBlock.Regions[2].Name}");
Expect(firstPacketBlock.Regions[3].Name == "scissor-rows", $"expected fourth region scissor-rows, got {firstPacketBlock.Regions[3].Name}");
Expect(firstPacketBlock.ControlRows.Count == 12, $"expected 12 first packet control rows, got {firstPacketBlock.ControlRows.Count}");
Expect(firstPacketBlock.ControlRows[3].Data0 == 0x04, $"expected first strip control token count 0x04, got 0x{firstPacketBlock.ControlRows[3].Data0:X2}");
Expect(firstPacketBlock.ControlRows[3].Data2 == 0x06, $"expected first strip control VU address 0x06, got 0x{firstPacketBlock.ControlRows[3].Data2:X2}");
Expect(firstPacketBlock.ControlRows[3].Data3 == 0x20, $"expected first strip control flags 0x20, got 0x{firstPacketBlock.ControlRows[3].Data3:X2}");
Expect(firstPacketBlock.UnpackHeader is not null, "expected first packet unpack header to decode from the three non-strip control rows");
Expect(firstPacketBlock.UnpackHeader!.StripCount == 9, $"expected first packet unpack header strip count 9, got {firstPacketBlock.UnpackHeader.StripCount}");
Expect(firstPacketBlock.UnpackHeader.DinkyVertexCount == 55, $"expected first packet unpack header dinky vertex count 55, got {firstPacketBlock.UnpackHeader.DinkyVertexCount}");
Expect(firstPacketBlock.VertexRows.Count(row => row.Kind == TiePacketVertexRowKind.DinkyVertex) == 55, "expected first packet vertex rows to classify as dinky vertices from the unpack header");
Expect(firstPacketBlock.DecodedVertices.Count == 55, $"expected first packet to decode 55 packet vertices, got {firstPacketBlock.DecodedVertices.Count}");
Expect(firstPacketBlock.Primitives.Count == 9, $"expected first packet to reconstruct 9 GS primitives, got {firstPacketBlock.Primitives.Count}");
Expect(firstPacketBlock.Primitives.All(primitive => primitive.Vertices.Count == firstPacketBlock.StripControls[primitive.PacketStripIndex].TokenCount), "expected first packet GS primitive vertex counts to match strip controls");
Expect(firstPacketBlock.StripControls.Count == 9, $"expected 9 first packet strip controls, got {firstPacketBlock.StripControls.Count}");
Expect(firstPacketBlock.StripControls.Sum(strip => strip.TokenCount) == 62, $"expected 62 first packet strip tokens, got {firstPacketBlock.StripControls.Sum(strip => strip.TokenCount)}");
Expect(firstPacketBlock.StripTokens.Count == 62, $"expected 62 first packet decoded strip tokens, got {firstPacketBlock.StripTokens.Count}");
Expect(firstPacketBlock.StripControls[0].TokenOffset == 0, $"expected first strip token offset 0, got {firstPacketBlock.StripControls[0].TokenOffset}");
Expect(firstPacketBlock.StripControls[0].ControlData1 == firstPacketBlock.ControlRows[3].Data1, "expected first strip control to preserve raw control row Data1");
Expect(firstPacketBlock.StripControls[0].Tokens.SequenceEqual(new byte[] { 0x07, 0x03, 0x03, 0x03 }), "expected first strip tokens to match fixture bytes");
Expect(firstPacketBlock.StripControls[0].DecodedTokens.Count == firstPacketBlock.StripControls[0].Tokens.Length, "expected first strip decoded tokens to match raw token count");
var firstDecodedStripToken = firstPacketBlock.StripControls[0].DecodedTokens[0];
Expect(firstDecodedStripToken.AddressMode == TiePacketStripTokenAddressMode.AbsoluteVertexWriteOffset, $"expected first strip token to decode as an absolute GS write offset, got {firstDecodedStripToken.AddressMode}");
Expect(firstDecodedStripToken.ResolvedGsPacketWriteOffset == 0x07, $"expected first strip token to resolve GS write offset 0x07, got {firstDecodedStripToken.ResolvedGsPacketWriteOffset}");
Expect(firstDecodedStripToken.ReferencedGsPacketWriteOffset == 0x07, $"expected first strip token to reference GS write offset 0x07, got {firstDecodedStripToken.ReferencedGsPacketWriteOffset}");
Expect(firstDecodedStripToken.ExpectedGsPacketWriteOffset == 0x07, $"expected first strip token expected GS write offset 0x07, got 0x{firstDecodedStripToken.ExpectedGsPacketWriteOffset:X}");
Expect(firstDecodedStripToken.MatchesExpectedGsPacketWriteOffset, "expected first strip token to match the strip control VU address");
var secondDecodedStripToken = firstPacketBlock.StripControls[0].DecodedTokens[1];
Expect(secondDecodedStripToken.AddressMode == TiePacketStripTokenAddressMode.ForwardVertexWriteOffsetStep, $"expected second strip token to decode as a forward GS write offset step, got {secondDecodedStripToken.AddressMode}");
Expect(secondDecodedStripToken.SignedValue == 3, $"expected second strip token signed step 3, got {secondDecodedStripToken.SignedValue}");
Expect(secondDecodedStripToken.ResolvedGsPacketWriteOffset == 0x0A, $"expected second strip token to resolve GS write offset 0x0A, got {secondDecodedStripToken.ResolvedGsPacketWriteOffset}");
Expect(secondDecodedStripToken.ReferencedGsPacketWriteOffset == 0x0A, $"expected second strip token to reference GS write offset 0x0A, got {secondDecodedStripToken.ReferencedGsPacketWriteOffset}");
Expect(secondDecodedStripToken.ExpectedGsPacketWriteOffset == 0x0A, $"expected second strip token expected GS write offset 0x0A, got 0x{secondDecodedStripToken.ExpectedGsPacketWriteOffset:X}");
Expect(secondDecodedStripToken.MatchesExpectedGsPacketWriteOffset, "expected second strip token to match the strip control VU address");
Expect(firstPacketBlock.ScissorTokens.Count == 63, $"expected 63 first packet scissor tokens including end token, got {firstPacketBlock.ScissorTokens.Count}");
Expect(firstPacketBlock.ScissorTokens[^1].IsEndToken, "expected first packet scissor token stream to end with 0xF6");
Expect(firstPacketBlock.ScissorTokens[^1].Offset == 0x57E, $"expected first packet scissor end token offset 0x57E, got 0x{firstPacketBlock.ScissorTokens[^1].Offset:X}");
Expect(firstPacketBlock.VertexRows.Count == 55, $"expected 55 first packet vertex rows, got {firstPacketBlock.VertexRows.Count}");
Expect(firstPacketBlock.VertexRows[0].Offset == 0x1D0, $"expected first vertex row offset 0x1D0, got 0x{firstPacketBlock.VertexRows[0].Offset:X}");
Expect(firstPacketBlock.VertexRows[0].X == 760, $"expected first vertex row X 760, got {firstPacketBlock.VertexRows[0].X}");
Expect(firstPacketBlock.VertexRows[0].Y == 16669, $"expected first vertex row Y 16669, got {firstPacketBlock.VertexRows[0].Y}");
Expect(firstPacketBlock.VertexRows[0].Z == 18654, $"expected first vertex row Z 18654, got {firstPacketBlock.VertexRows[0].Z}");
Expect(firstPacketBlock.VertexRows[0].W == 189, $"expected first vertex row W 189, got {firstPacketBlock.VertexRows[0].W}");
Expect(firstPacketBlock.VertexRows[0].PrimaryVuAddress == 189, $"expected first vertex row primary VU address 189, got {firstPacketBlock.VertexRows[0].PrimaryVuAddress}");
Expect(firstPacketBlock.VertexRows[0].Data2 == 4096, $"expected first vertex row Data2 4096, got {firstPacketBlock.VertexRows[0].Data2}");
foreach (var block in tie.PacketDataBlocks)
{
    var packet = tie.PacketTables[block.LodIndex].Packets[block.PacketIndex];
    var expectedEndTokenOffset = packet.AbsoluteDataOffset
        + packet.ScissorOffset * 0x10
        + block.StripControls.Sum(strip => strip.TokenCount);
    Expect(block.ScissorTokens[^1].Offset == expectedEndTokenOffset, $"expected LOD{block.LodIndex}[{block.PacketIndex}] scissor end token at 0x{expectedEndTokenOffset:X}, got 0x{block.ScissorTokens[^1].Offset:X}");
    Expect(block.ScissorTokens[^1].IsEndToken, $"expected LOD{block.LodIndex}[{block.PacketIndex}] scissor stream to end with 0xF6");
    Expect(block.StripTokens.Count == block.StripControls.Sum(strip => strip.Tokens.Length), $"expected LOD{block.LodIndex}[{block.PacketIndex}] decoded strip token count to match raw token count");
    Expect(block.StripTokens.All(token => token.MatchesExpectedGsPacketWriteOffset), $"expected LOD{block.LodIndex}[{block.PacketIndex}] decoded strip tokens to match strip control VU addresses");
}

Expect(tie.PacketDataBlocks.Sum(block => block.VertexRows.Count) == 417, $"expected 417 decoded packet vertex rows, got {tie.PacketDataBlocks.Sum(block => block.VertexRows.Count)}");
Expect(tie.PacketDataBlocks.Sum(block => block.VertexRows.Count(row => row.HasPrimaryVuAddress)) == 399, $"expected 399 primary-addressed packet vertex rows, got {tie.PacketDataBlocks.Sum(block => block.VertexRows.Count(row => row.HasPrimaryVuAddress))}");
Expect(tie.PacketDataBlocks.Sum(block => block.VertexRows.Count(row => row.HasSecondaryVuAddress)) == 43, $"expected 43 secondary-addressed packet vertex rows, got {tie.PacketDataBlocks.Sum(block => block.VertexRows.Count(row => row.HasSecondaryVuAddress))}");
Expect(tie.PacketDataBlocks.Sum(block => block.StripControls.Count) == tie.Header.Lods[0].StripCount, $"expected decoded strip controls to match header strip count {tie.Header.Lods[0].StripCount}, got {tie.PacketDataBlocks.Sum(block => block.StripControls.Count)}");
Expect(tie.LodTopologies.Count == 3, $"expected 3 LOD topologies, got {tie.LodTopologies.Count}");
var lod0Topology = tie.LodTopologies[0];
Expect(lod0Topology.LogicalVertexCount == tie.Header.Lods[0].VertexCount, $"expected LOD0 topology logical vertex count {tie.Header.Lods[0].VertexCount}, got {lod0Topology.LogicalVertexCount}");
Expect(lod0Topology.LogicalVertices.Count == tie.Header.Lods[0].VertexCount, $"expected LOD0 logical vertex records {tie.Header.Lods[0].VertexCount}, got {lod0Topology.LogicalVertices.Count}");
Expect(lod0Topology.PacketVertexRowCount == 417, $"expected LOD0 packet vertex row count 417, got {lod0Topology.PacketVertexRowCount}");
Expect(lod0Topology.PrimaryAddressMappedLogicalVertexCount == 408, $"expected 408 LOD0 logical vertices mapped by primary GS write offset, got {lod0Topology.PrimaryAddressMappedLogicalVertexCount}");
Expect(lod0Topology.SecondaryAddressMappedLogicalVertexCount == 28, $"expected 28 LOD0 logical vertices mapped by secondary GS write offset, got {lod0Topology.SecondaryAddressMappedLogicalVertexCount}");
Expect(lod0Topology.UnresolvedLogicalVertexCount == 0, $"expected 0 unresolved LOD0 logical vertices, got {lod0Topology.UnresolvedLogicalVertexCount}");
Expect(lod0Topology.LogicalVertices.All(vertex => vertex.MappingKind != TieLogicalVertexMappingKind.Unresolved), "expected every LOD0 logical vertex to resolve to a decoded vertex row");
Expect(lod0Topology.LogicalVertices.All(vertex => vertex.DecodedVertex is not null), "expected every LOD0 logical vertex to resolve through the decoded packet vertex stream");
Expect(lod0Topology.StripCount == tie.Header.Lods[0].StripCount, $"expected LOD0 topology strip count {tie.Header.Lods[0].StripCount}, got {lod0Topology.StripCount}");
Expect(lod0Topology.TriangleCount == tie.Header.Lods[0].TriangleCount, $"expected LOD0 topology triangle count {tie.Header.Lods[0].TriangleCount}, got {lod0Topology.TriangleCount}");
Expect(lod0Topology.Strips[0].LogicalVertexStartIndex == 0, $"expected first strip to start at logical vertex 0, got {lod0Topology.Strips[0].LogicalVertexStartIndex}");
Expect(lod0Topology.Strips[0].LogicalVertexCount == 4, $"expected first strip logical vertex count 4, got {lod0Topology.Strips[0].LogicalVertexCount}");
Expect(lod0Topology.Strips[0].LogicalVertices.Count == 4, $"expected first strip to expose 4 logical vertices, got {lod0Topology.Strips[0].LogicalVertices.Count}");
Expect(lod0Topology.Strips[0].TriangleStartIndex == 0, $"expected first strip triangle start 0, got {lod0Topology.Strips[0].TriangleStartIndex}");
Expect(lod0Topology.Strips[0].TriangleCount == 2, $"expected first strip triangle count 2, got {lod0Topology.Strips[0].TriangleCount}");
Expect(lod0Topology.Strips[1].LogicalVertexStartIndex == 4, $"expected second strip to start at logical vertex 4, got {lod0Topology.Strips[1].LogicalVertexStartIndex}");
Expect(lod0Topology.Strips[1].TriangleStartIndex == 2, $"expected second strip triangle start 2, got {lod0Topology.Strips[1].TriangleStartIndex}");
Expect(lod0Topology.LogicalVertices[0].VuAddress == 0x07, $"expected first logical vertex VU address 0x07, got 0x{lod0Topology.LogicalVertices[0].VuAddress:X}");
Expect(lod0Topology.LogicalVertices[0].MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress, $"expected first logical vertex to map by W, got {lod0Topology.LogicalVertices[0].MappingKind}");
Expect(lod0Topology.LogicalVertices[0].AddressRowIndex == 29, $"expected first logical vertex to map to packet row 29, got {lod0Topology.LogicalVertices[0].AddressRowIndex}");
var reusedStripVertex = lod0Topology.Strips[2].LogicalVertices[1];
Expect(reusedStripVertex.VuAddress == 0x42, $"expected third strip second logical vertex VU address 0x42, got 0x{reusedStripVertex.VuAddress:X}");
Expect(reusedStripVertex.MappingKind == TieLogicalVertexMappingKind.SecondaryRowAddress, $"expected third strip second logical vertex to map by Data3, got {reusedStripVertex.MappingKind}");
Expect(reusedStripVertex.AddressRowIndex == 49, $"expected third strip second logical vertex address row 49, got {reusedStripVertex.AddressRowIndex}");
var secondaryCarryVertex = lod0Topology.Strips.First(strip => strip.PacketIndex == 4 && strip.PacketStripIndex == 4).LogicalVertices[5];
Expect(secondaryCarryVertex.VuAddress == 0x68, $"expected packet 4 carried logical vertex VU address 0x68, got 0x{secondaryCarryVertex.VuAddress:X}");
Expect(secondaryCarryVertex.GsPacketWriteOffset == 0x68, $"expected packet 4 carried logical vertex GS write offset 0x68, got 0x{secondaryCarryVertex.GsPacketWriteOffset:X}");
Expect(secondaryCarryVertex.MappingKind == TieLogicalVertexMappingKind.PrimaryRowAddress, $"expected packet 4 carried logical vertex to map by primary GS write offset after packet unpacking, got {secondaryCarryVertex.MappingKind}");
Expect(secondaryCarryVertex.AddressRowIndex == 58, $"expected packet 4 carried logical vertex source row 58, got {secondaryCarryVertex.AddressRowIndex}");
Expect(secondaryCarryVertex.VertexRowIndex == 58, $"expected packet 4 carried logical vertex data row 58, got {secondaryCarryVertex.VertexRowIndex}");
Expect(lod0Topology.Triangles[0] == new TieTriangle(0, 0, 0, 0, 2, 1), $"expected first triangle (0,2,1), got ({lod0Topology.Triangles[0].A},{lod0Topology.Triangles[0].B},{lod0Topology.Triangles[0].C})");
Expect(lod0Topology.Triangles[1] == new TieTriangle(0, 0, 1, 1, 2, 3), $"expected second triangle (1,2,3), got ({lod0Topology.Triangles[1].A},{lod0Topology.Triangles[1].B},{lod0Topology.Triangles[1].C})");
Expect(tie.PacketDataBlocks[^1].Offset == 0x1C50, $"expected last packet data block offset 0x1C50, got 0x{tie.PacketDataBlocks[^1].Offset:X}");
Expect(tie.PacketDataBlocks[^1].Length == 0x3E0, $"expected last packet data block length 0x3E0, got 0x{tie.PacketDataBlocks[^1].Length:X}");
Expect(tie.PacketDataBlocks[^1].Offset + tie.PacketDataBlocks[^1].Length == tie.Header.VertexNormalsOffset, "expected last packet data block to end at vertex normals offset");
Expect(tie.Shaders.Count == 5, $"expected 5 shader records, got {tie.Shaders.Count}");
Expect(tie.Shaders[0].Offset == 0x31C0, $"expected first shader offset 0x31C0, got 0x{tie.Shaders[0].Offset:X}");
Expect(!tie.Shaders[0].ClampU && !tie.Shaders[0].ClampV, "expected 09907 shader 0 to repeat U and V");
Expect(tie.Shaders[2].ClampU && tie.Shaders[2].ClampV, "expected 09907 shader 2 to clamp U and V");
Expect(!tie.Shaders[3].ClampU && tie.Shaders[3].ClampV, "expected 09907 shader 3 to repeat U and clamp V");
Expect(!tie.Shaders[4].ClampU && tie.Shaders[4].ClampV, "expected 09907 shader 4 to repeat U and clamp V");
Expect(tie.Shaders[^1].Offset + TieShader.Size == tie.ByteLength, "expected shader table to reach the end of the tie file");
var multiShaderPacket = tie.PacketTables[0].Packets[3];
Expect(multiShaderPacket.ShaderReferences.Select(reference => reference.ShaderIndex).SequenceEqual(new[] { 0, 2, 3 }), "expected 09907 packet 3 shader references to decode as [0, 2, 3]");
Expect(multiShaderPacket.ShaderSwitchVuAddresses.SequenceEqual(new[] { 32, 90 }), "expected 09907 packet 3 shader switch VU addresses to decode as [32, 90]");

var report = TieClassDescriber.Describe(tie);
Expect(report.Contains("OClass: 0x26B3", StringComparison.Ordinal), "expected report to include o_class");
Expect(report.Contains("LOD 0: vertices=436", StringComparison.Ordinal), "expected report to include LOD0 summary");
Expect(report.Contains("mapped=436 (W=408, Data3=28, unresolved=0)", StringComparison.Ordinal), "expected report to include logical vertex mapping summary");
Expect(report.Contains("shaderSwitchVu=[32, 90]", StringComparison.Ordinal), "expected report to include shader switch VU addresses");
Expect(report.Contains("clampU=True, clampV=True", StringComparison.Ordinal), "expected report to include shader clamp flags");
Expect(report.Contains("Decoded vertex normals/remaps: 334", StringComparison.Ordinal), "expected report to include decoded vertex normals");
Expect(report.Contains($"Decoded glow RGBA remaps/vertices: 1 / {tie.GlowRgbaVertices.Count}", StringComparison.Ordinal), "expected report to include decoded glow RGBA remaps");
Expect(report.Contains("strip controls: 9", StringComparison.Ordinal), "expected report to include decoded strip controls");
Expect(report.Contains("setup row 0:", StringComparison.Ordinal), "expected report to include decoded packet setup rows");

var rebuiltBytes = TieClassWriter.Build(tie);
Expect(rebuiltBytes.SequenceEqual(originalBytes), "expected tie raw-section rebuild to be byte-identical");

var gltfExport = TieGltfExporter.Export(
    tie,
    "tie.gltf",
    new TieGltfExportOptions { BufferFileName = "tie.buffer.bin" });
Expect(gltfExport.GltfBytes.Length > 0, "expected tie glTF export to write glTF JSON bytes");
Expect(gltfExport.BinBytes.Length > 0, "expected tie glTF export to write buffer bytes");
Expect(gltfExport.DiagnosticsBytes.Length > 0, "expected tie glTF export to write diagnostics bytes");

using (var gltfDocument = JsonDocument.Parse(gltfExport.GltfBytes))
{
    var root = gltfDocument.RootElement;
    Expect(root.GetProperty("asset").GetProperty("generator").GetString() == "RatchetPs2 TIE tie glTF exporter", "expected neutral tie glTF generator metadata");
    Expect(root.GetProperty("buffers")[0].GetProperty("uri").GetString() == "tie.buffer.bin", "expected tie glTF buffer URI");

    var meshes = root.GetProperty("meshes");
    Expect(meshes.GetArrayLength() == 1, $"expected one tie glTF mesh, got {meshes.GetArrayLength()}");
    var primitives = meshes[0].GetProperty("primitives");
    var expectedPrimitiveCount = CountExpectedGltfPrimitiveGroups(tie, 0);
    Expect(primitives.GetArrayLength() == expectedPrimitiveCount, $"expected one tie glTF primitive per packet shader run, got {primitives.GetArrayLength()}");

    var accessors = root.GetProperty("accessors");
    var firstPrimitive = primitives[0];
    var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
    var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
    var texCoordAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
    var positionAccessorCount = accessors[positionAccessorIndex].GetProperty("count").GetInt32();
    var normalAccessorCount = accessors[normalAccessorIndex].GetProperty("count").GetInt32();
    var texCoordAccessorCount = accessors[texCoordAccessorIndex].GetProperty("count").GetInt32();
    Expect(positionAccessorCount >= tie.Header.Lods[0].VertexCount, $"expected tie glTF position count at least {tie.Header.Lods[0].VertexCount}, got {positionAccessorCount}");
    Expect(normalAccessorCount == positionAccessorCount, $"expected tie glTF normal count {positionAccessorCount}, got {normalAccessorCount}");
    Expect(texCoordAccessorCount == positionAccessorCount, $"expected tie glTF texture coordinate count {positionAccessorCount}, got {texCoordAccessorCount}");
    Expect(
        !firstPrimitive.GetProperty("attributes").TryGetProperty("COLOR_0", out _),
        "expected standalone 09907 tie glTF export not to emit COLOR_0");
    Expect(
        firstPrimitive.GetProperty("attributes").TryGetProperty("_TIE_GLOW_0", out var glowAttributeIndex)
        && accessors[glowAttributeIndex.GetInt32()].GetProperty("count").GetInt32() == positionAccessorCount,
        "expected 09907 glow RGBA emission to export the neutral per-vertex glow attribute");
    using var diagnosticsDocument = JsonDocument.Parse(gltfExport.DiagnosticsBytes);
    Expect(
        diagnosticsDocument.RootElement.GetProperty("SourceTableNormalVertexCount").GetInt32() > 0,
        "expected tie glTF export to apply at least one source normal table remap");
    Expect(
        diagnosticsDocument.RootElement.GetProperty("DecodedGlowRgbaRemapCount").GetInt32() == 1,
        "expected tie glTF diagnostics to include the 09907 glow remap");
    Expect(
        diagnosticsDocument.RootElement.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == tie.GlowRgbaVertices.Count,
        "expected tie glTF diagnostics to report resolved 09907 glow vertices");
    Expect(
        diagnosticsDocument.RootElement.GetProperty("PacketRgbaSlotCount").GetInt32() == CountPacketRgbaSlots(tie, 0),
        "expected tie glTF diagnostics to report whole-model packet RGBA slot count");
    Expect(
        diagnosticsDocument.RootElement.GetProperty("VertexColor0AccessorCount").GetInt32() == 0,
        "expected standalone tie glTF diagnostics not to report COLOR_0");

    var positionAccessor = accessors[positionAccessorIndex];
    var positionBufferView = root.GetProperty("bufferViews")[positionAccessor.GetProperty("bufferView").GetInt32()];
    var positionByteOffset = positionBufferView.GetProperty("byteOffset").GetInt32()
        + positionAccessor.GetProperty("byteOffset").GetInt32();
    var nearOriginPositionCount = 0;
    for (var i = 0; i < tie.Header.Lods[0].VertexCount; i++)
    {
        var offset = positionByteOffset + i * 3 * sizeof(float);
        var x = BitConverter.ToSingle(gltfExport.BinBytes, offset);
        var y = BitConverter.ToSingle(gltfExport.BinBytes, offset + sizeof(float));
        var z = BitConverter.ToSingle(gltfExport.BinBytes, offset + sizeof(float) * 2);
        if (MathF.Sqrt(x * x + y * y + z * z) < 0.1f)
        {
            nearOriginPositionCount++;
        }
    }

    Expect(nearOriginPositionCount == 0, $"expected no tie glTF positions near origin, got {nearOriginPositionCount}");

    var exportedIndexCount = 0;
    foreach (var primitive in primitives.EnumerateArray())
    {
        var indexAccessorIndex = primitive.GetProperty("indices").GetInt32();
        exportedIndexCount += accessors[indexAccessorIndex].GetProperty("count").GetInt32();
    }

    Expect(exportedIndexCount == tie.Header.Lods[0].TriangleCount * 3, $"expected tie glTF index count {tie.Header.Lods[0].TriangleCount * 3}, got {exportedIndexCount}");
}

foreach (var fixturePath in tiePaths)
{
    ValidateFixture(fixturePath);
}

ValidateReflectiveMaskFixture();
ValidateGeneratedEnvironmentNormalFixture();
ValidateSecondSlotQPivotFixture();
ValidateWideSecondSlotQPivotFixture();
ValidateZeroCoordinatePositionFixture();
ValidateEmbeddedQCoordinatePositionFixture();
ValidatePartialTileEdgeTextureFixture();
ValidateWidePanelTextureFixture();
ValidateOutwardWindingNormalFixture();
ValidateHardSurfaceTableNormalFixture();
ValidateMixedOrientationTableNormalFixture();
ValidateLowCoverageTableNormalFixture();
ValidateBroadSparseTableNormalFixture();
ValidateHighInvertedRatioTableNormalFixture();
ValidateUpperStrongDownTableNormalFixture();
ValidateDlLevel07AmbientRegressionFixture();
ValidateOrganicDuplicatePositionNormalWeldFixture();
ValidateLogicalNormalRemapMetadataFixture();
ValidateInvertedComponentWindingFixture();
ValidateGcFlatPlatformWindingFixture();

if (failures.Count == 0)
{
    Console.WriteLine($"PASS DL tie reader/rebuild/export {tiePaths.Length} fixture(s)");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} tie reader assertion(s) failed:");
foreach (var failure in failures)
{
    Console.Error.WriteLine($"  {failure}");
}

return 1;

void Expect(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

void ExpectExportWindingMatchesDae(TieGltfExport export, string daePath, string label)
{
    var exportedFaceKeys = BuildTriangleFaceKeyCounts(
        ReadExportedPositions(export),
        ReadExportedIndices(export));
    var referenceFaceKeys = ReadDaeTriangleFaceKeyCounts(daePath);
    var missingReferenceFaceCount = referenceFaceKeys.Sum(pair =>
    {
        exportedFaceKeys.TryGetValue(pair.Key, out var exportedCount);
        return Math.Max(0, pair.Value - exportedCount);
    });

    Expect(
        exportedFaceKeys.Values.Sum() == referenceFaceKeys.Values.Sum(),
        $"{label}: expected exported triangle count to match mesh.dae");
    Expect(
        missingReferenceFaceCount == 0,
        $"{label}: expected exported winding to match mesh.dae, got {missingReferenceFaceCount} missing/flipped face(s)");
}

void ValidateFixture(string fixturePath)
{
    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var fixtureInput = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(fixtureInput);
        var fixtureOriginalBytes = File.ReadAllBytes(fixturePath);
        var fixtureRebuiltBytes = TieClassWriter.Build(fixtureTie);
        var expectedVertexNormalCount = Math.Max(0, (int)fixtureTie.Header.VertexNormalsCount);
        Expect(
            fixtureTie.VertexNormals.Count == expectedVertexNormalCount,
            $"{relativePath}: expected {expectedVertexNormalCount} decoded vertex normals, got {fixtureTie.VertexNormals.Count}");
        if (fixtureTie.Header.GlowRgba != 0 && fixtureTie.Header.GlowRemapOffsets.Any(offset => offset > 0))
        {
            Expect(fixtureTie.GlowRgbaRemaps.Count > 0, $"{relativePath}: expected nonzero glow RGBA metadata to decode remap records");
            Expect(
                fixtureTie.GlowRgbaRemaps.All(remap => remap.RawRgba == fixtureTie.Header.GlowRgba),
                $"{relativePath}: expected glow RGBA remaps to preserve the class glow color");
            ValidateGlowMultipassResolution(fixtureTie, relativePath);
        }

        Expect(
            fixtureRebuiltBytes.SequenceEqual(fixtureOriginalBytes),
            $"{relativePath}: expected raw-section rebuild to be byte-identical");

        for (var lodIndex = 0; lodIndex < fixtureTie.Header.Lods.Length; lodIndex++)
        {
            var topology = fixtureTie.LodTopologies[lodIndex];
            var headerLod = fixtureTie.Header.Lods[lodIndex];
            Expect(topology.LogicalVertexCount == headerLod.VertexCount, $"{relativePath}: expected LOD{lodIndex} logical vertex count {headerLod.VertexCount}, got {topology.LogicalVertexCount}");
            Expect(topology.StripCount == headerLod.StripCount, $"{relativePath}: expected LOD{lodIndex} strip count {headerLod.StripCount}, got {topology.StripCount}");
            Expect(topology.TriangleCount == headerLod.TriangleCount, $"{relativePath}: expected LOD{lodIndex} triangle count {headerLod.TriangleCount}, got {topology.TriangleCount}");
            Expect(topology.UnresolvedLogicalVertexCount == 0, $"{relativePath}: expected all LOD{lodIndex} logical vertices to resolve, got {topology.UnresolvedLogicalVertexCount} unresolved");
            Expect(topology.PrimaryAddressMappedLogicalVertexCount + topology.SecondaryAddressMappedLogicalVertexCount == topology.LogicalVertexCount, $"{relativePath}: expected all LOD{lodIndex} logical vertices to map to decoded rows");
            ValidatePacketDataAccuracy(fixtureTie, relativePath, lodIndex);

            if (topology.LogicalVertexCount == 0)
            {
                continue;
            }

            var textureResources = BuildFixtureTextureResources(fixturePath);
            var fixtureExport = TieGltfExporter.Export(
                fixtureTie,
                $"tie-lod{lodIndex}.gltf",
                new TieGltfExportOptions
                {
                    LodIndex = lodIndex,
                    BufferFileName = $"tie-lod{lodIndex}.buffer.bin",
                    ExternalTextureUris = textureResources?.Uris,
                    ExternalTextureSizes = textureResources?.Sizes,
                    ExternalTextureAlpha = textureResources?.Alpha
                });
            ValidateGltfExport(
                fixtureTie,
                fixtureExport,
                relativePath,
                lodIndex,
                textureResources?.Uris,
                textureResources?.Alpha);

            if (lodIndex == 0)
            {
                ValidateAttributeAddressResolution(fixtureTie, fixtureExport, relativePath);
                ValidateTallCoordinateResolution(fixtureTie, fixtureExport, relativePath);
                ValidateSecondSlotAddressResolution(fixtureTie, fixtureExport, relativePath);
            }
        }
        Console.WriteLine($"PASS DL tie {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie {relativePath}");
    }
}

void ValidateReflectiveMaskFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9308", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        Expect(textureResources is not null, $"{relativePath}: expected local PNG textures for reflective alpha mask coverage");

        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
        var materials = gltfDocument.RootElement.GetProperty("materials").EnumerateArray().ToArray();
        var reflectiveMaskMaterials = materials
            .Where(material => material.TryGetProperty("extras", out var extras)
                && extras.TryGetProperty("TieTextureAlphaUsage", out var alphaUsage)
                && alphaUsage.GetString() == TieMaterialAlphaUsage.ReflectiveMask.ToString())
            .ToArray();

        Expect(
            reflectiveMaskMaterials.Length == 4,
            $"{relativePath}: expected four reflective-mask alpha materials for tie 9308, got {reflectiveMaskMaterials.Length}");
        foreach (var material in reflectiveMaskMaterials)
        {
            var name = material.GetProperty("name").GetString() ?? "unnamed";
            var extras = material.GetProperty("extras");
            Expect(
                !material.TryGetProperty("alphaMode", out _),
                $"{relativePath}: expected reflective-mask material {name} to stay opaque in glTF alphaMode");
            Expect(
                extras.GetProperty("TieTextureAlphaMode").GetString() == TextureAlphaMode.Blend.ToString(),
                $"{relativePath}: expected reflective-mask material {name} to preserve raw Blend alpha metadata");
            Expect(
                extras.GetProperty("TieTextureGltfAlphaMode").ValueKind == JsonValueKind.Null,
                $"{relativePath}: expected reflective-mask material {name} to suppress emitted glTF alpha metadata");
            Expect(
                extras.GetProperty("TieMaterialRole").GetString() == "ReflectiveOverlay",
                $"{relativePath}: expected reflective-mask material {name} to export the reflective overlay role");
            Expect(
                extras.GetProperty("TieTextureRgbUsage").GetString() == "ReflectivePreview",
                $"{relativePath}: expected reflective-mask material {name} to mark texture RGB as reflective preview data");
            Expect(
                extras.GetProperty("TieReflectiveMaskChannel").GetString() == "A",
                $"{relativePath}: expected reflective-mask material {name} to use alpha as the reflective mask channel");
            Expect(
                extras.GetProperty("TieReflectiveTintSource").GetString() == "DirectionalLightSelector",
                $"{relativePath}: expected reflective-mask material {name} to use the tie directional-light selector as tint source");
            Expect(
                extras.GetProperty("TieReflectiveEnvironmentSource").GetString() == "TieTexture",
                $"{relativePath}: expected reflective-mask material {name} to use the tie reflection texture as environment source");
            Expect(
                extras.GetProperty("TieReflectiveEnvironmentTextureRole").GetString() == "LastTieTexture",
                $"{relativePath}: expected reflective-mask material {name} to mark the environment texture as the last tie texture");
            Expect(
                extras.GetProperty("TieReflectiveEnvironmentShaderIndex").GetInt32() == textureResources!.Uris.Keys.Max(),
                $"{relativePath}: expected reflective-mask material {name} to point at the last tie texture shader");
            Expect(
                extras.GetProperty("TieReflectiveBlendMode").GetString() == "EnvironmentOverlay",
                $"{relativePath}: expected reflective-mask material {name} to export environment overlay blend semantics");
            Expect(
                extras.GetProperty("TieMultipassOffset").GetInt32() > 0,
                $"{relativePath}: expected reflective-mask material {name} to export the multipass UV offset");
            Expect(
                extras.GetProperty("TieMultipassType").GetInt32() == 10,
                $"{relativePath}: expected reflective-mask material {name} to export multipass type 10");
            Expect(
                extras.GetProperty("TieMultipassUvSize").GetInt32() > 0,
                $"{relativePath}: expected reflective-mask material {name} to export the multipass UV size");
            Expect(
                extras.GetProperty("TieMultipassTypeBits").GetString() == "0x0A",
                $"{relativePath}: expected reflective-mask material {name} to export multipass type bits 0x0A");
            var reflectivePreviewBase = extras.GetProperty("TieReflectivePreviewBaseColorFactor")
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray();
            Expect(
                reflectivePreviewBase.Length == 3
                && MathF.Abs(reflectivePreviewBase[0] - 0.035f) < 0.0001f
                && MathF.Abs(reflectivePreviewBase[1] - 0.045f) < 0.0001f
                && MathF.Abs(reflectivePreviewBase[2] - 0.06f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} preview base color factor");
            Expect(
                MathF.Abs(extras.GetProperty("TieReflectivePreviewTextureRgbScale").GetSingle() - 0.2f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} preview texture RGB scale 0.2");
            Expect(
                MathF.Abs(extras.GetProperty("TieReflectiveMaskFocusPower").GetSingle() - 1.2f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} reflective mask focus power 1.2");
            Expect(
                MathF.Abs(extras.GetProperty("TieReflectiveEnvironmentStrength").GetSingle() - 2.2f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} reflective environment strength 2.2");
            Expect(
                MathF.Abs(extras.GetProperty("TieReflectiveMaxBlend").GetSingle() - 0.82f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} reflective max blend 0.82");
            var pbr = material.GetProperty("pbrMetallicRoughness");
            Expect(
                MathF.Abs(pbr.GetProperty("metallicFactor").GetSingle() - 0.37f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} metallic factor 0.37");
            Expect(
                MathF.Abs(pbr.GetProperty("roughnessFactor").GetSingle() - 0.24f) < 0.0001f,
                $"{relativePath}: expected reflective-mask material {name} roughness factor 0.24");
        }

        using var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes);
        var diagnosticReflectiveMaskCount = diagnosticsDocument.RootElement
            .GetProperty("Materials")
            .EnumerateArray()
            .Count(material => material.TryGetProperty("TextureAlphaUsage", out var alphaUsage)
                && alphaUsage.GetString() == TieMaterialAlphaUsage.ReflectiveMask.ToString());
        Expect(
            diagnosticReflectiveMaskCount == 4,
            $"{relativePath}: expected diagnostics to report four reflective-mask alpha materials, got {diagnosticReflectiveMaskCount}");

        Console.WriteLine($"PASS DL tie reflective alpha mask {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie reflective alpha mask {relativePath}");
    }
}

void ValidateGeneratedEnvironmentNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9328", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(dlProfile));
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameProfile = dlProfile });
        using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
        var root = gltfDocument.RootElement;
        var attributes = root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
        var exported = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("_TIE_ENV_NORMAL").GetInt32())[0];

        var vertex = fixtureTie.LodTopologies[0].LogicalVertices[0];
        var packet = fixtureTie.PacketTables[0].Packets[vertex.PacketIndex];
        var block = fixtureTie.PacketDataBlocks.First(item => item.LodIndex == 0 && item.PacketIndex == vertex.PacketIndex);
        var packedOffset = (packet.MultipassOffset + 3) * 0x10
            + vertex.DecodedVertex!.Index * sizeof(uint);
        var packed = BitConverter.ToUInt32(block.Bytes, packedOffset);
        var x = SignExtend(packed, 11) / 1024f;
        var y = SignExtend(packed >> 11, 11) / 1024f;
        var z = SignExtend(packed >> 22, 10) / 512f;
        var length = MathF.Sqrt(x * x + y * y + z * z);
        var dot = exported.X * x / length + exported.Y * z / length - exported.Z * y / length;
        Expect(dot > 0.99999f, $"{relativePath}: expected exported normal to use the packed generated-env normal, dot={dot}");

        Console.WriteLine($"PASS DL tie generated environment normal {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie generated environment normal {relativePath}");
    }

    static int SignExtend(uint value, int bitCount)
    {
        var shift = 32 - bitCount;
        return (int)(value << shift) >> shift;
    }
}

void ValidateSecondSlotQPivotFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9196", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);

        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        foreach (var logicalVertexIndex in new[] { 256, 327, 336, 690, 721 })
        {
            var vertex = fixtureTie.LodTopologies[0].LogicalVertices[logicalVertexIndex];
            var row = vertex.VertexRow!;
            var exportedPosition = ReadExportedPosition(fixtureExport, logicalVertexIndex);
            var expectedPosition = ToGltfPosition(fixtureTie, row.Data0, row.Data1, row.Data2);
            var firstSlotPosition = ToGltfPosition(fixtureTie, row.X, row.Y, row.Z);
            var firstSlotLength = VectorLength(fixtureTie, row.X, row.Y, row.Z);

            Expect(
                row.Data2 != 4096,
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to exercise the second-slot Q pivot, got Data2 {row.Data2}");
            Expect(
                firstSlotLength > 3f,
                $"{relativePath}: expected logical vertex {logicalVertexIndex} first-slot vector length {firstSlotLength} to exercise a broad non-position first slot");
            Expect(
                PositionsNearlyEqual(exportedPosition, expectedPosition),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to export from second-slot position data");
            Expect(
                !PositionsNearlyEqual(exportedPosition, firstSlotPosition),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} not to export the first-slot direction vector");
        }

        var positions = ReadExportedPositions(fixtureExport);
        var indices = ReadExportedIndices(fixtureExport);
        var maxEdgeLength = 0f;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(a, b));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(b, c));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(c, a));
        }

        Expect(
            maxEdgeLength < 18f,
            $"{relativePath}: expected 9196 max triangle edge below 18 after second-slot Q-pivot decoding, got {maxEdgeLength}");

        Console.WriteLine($"PASS DL tie second-slot Q pivot {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie second-slot Q pivot {relativePath}");
    }
}

void ValidateWideSecondSlotQPivotFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9484", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);

        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        var adjacentMarkerVertex = fixtureTie.LodTopologies[0].LogicalVertices[491];
        Expect(adjacentMarkerVertex.DecodedVertex is not null, $"{relativePath}: expected logical vertex 491 to resolve from the decoded packet vertex stream");
        Expect(adjacentMarkerVertex.AddressRowIndex == 36, $"{relativePath}: expected logical vertex 491 decoded source row 36, got {adjacentMarkerVertex.AddressRowIndex}");
        Expect(adjacentMarkerVertex.VertexRowIndex == 36, $"{relativePath}: expected logical vertex 491 decoded data row 36, got {adjacentMarkerVertex.VertexRowIndex}");
        Expect(
            PositionsNearlyEqual(
                ReadExportedPosition(fixtureExport, 491),
                ToGltfPosition(fixtureTie, adjacentMarkerVertex.DecodedVertex!.X, adjacentMarkerVertex.DecodedVertex.Y, adjacentMarkerVertex.DecodedVertex.Z)),
            $"{relativePath}: expected logical vertex 491 to export from the decoded packet vertex");

        foreach (var logicalVertexIndex in new[] { 218, 493, 559, 560 })
        {
            var vertex = fixtureTie.LodTopologies[0].LogicalVertices[logicalVertexIndex];
            var row = vertex.VertexRow!;
            var exportedPosition = ReadExportedPosition(fixtureExport, logicalVertexIndex);
            var expectedPosition = ToGltfPosition(fixtureTie, row.Data0, row.Data1, row.Data2);
            var firstSlotPosition = ToGltfPosition(fixtureTie, row.X, row.Y, row.Z);

            Expect(
                row.Data2 != 4096,
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to exercise the second-slot Q pivot, got Data2 {row.Data2}");
            Expect(
                PositionsNearlyEqual(exportedPosition, expectedPosition),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to export from second-slot Q-pivot position data");
            Expect(
                !PositionsNearlyEqual(exportedPosition, firstSlotPosition),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} not to export the first-slot non-position vector");
        }

        var positions = ReadExportedPositions(fixtureExport);
        var indices = ReadExportedIndices(fixtureExport);
        var maxEdgeLength = 0f;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(a, b));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(b, c));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(c, a));
        }

        Expect(
            maxEdgeLength < 45f,
            $"{relativePath}: expected 9484 max triangle edge below 45 after second-slot Q-pivot decoding, got {maxEdgeLength}");

        Console.WriteLine($"PASS DL tie wide second-slot Q pivot {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie wide second-slot Q pivot {relativePath}");
    }
}

void ValidateZeroCoordinatePositionFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9303", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        foreach (var logicalVertexIndex in new[] { 3, 7, 11, 15 })
        {
            var vertex = fixtureTie.LodTopologies[0].LogicalVertices[logicalVertexIndex];
            var row = vertex.VertexRow!;
            var exportedPosition = ReadExportedPosition(fixtureExport, logicalVertexIndex);

            Expect(
                row.X == 0 && row.Y == 0 && row.Z == 0 && row.Data2 == 4096,
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to resolve to a zero center coordinate row");
            Expect(
                PositionsNearlyEqual(exportedPosition, (0f, 0f, 0f)),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to preserve the zero center coordinate");
        }

        var positions = ReadExportedPositions(fixtureExport);
        var indices = ReadExportedIndices(fixtureExport);
        var degenerateTriangleCount = 0;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];
            if (Distance(a, b) < 0.001f || Distance(b, c) < 0.001f || Distance(c, a) < 0.001f)
            {
                degenerateTriangleCount++;
            }
        }

        Expect(
            degenerateTriangleCount == 0,
            $"{relativePath}: expected zero-center coordinate decoding to avoid degenerate triangles, got {degenerateTriangleCount}");

        Console.WriteLine($"PASS DL tie zero-coordinate position {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie zero-coordinate position {relativePath}");
    }
}

void ValidateEmbeddedQCoordinatePositionFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "8461", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        var embeddedQVertex = fixtureTie.LodTopologies[0].LogicalVertices[1760];
        var row = embeddedQVertex.VertexRow!;
        var exportedPosition = ReadExportedPosition(fixtureExport, embeddedQVertex.LogicalVertexIndex);
        var expectedPosition = ToGltfPosition(fixtureTie, row.Data0, row.Data1, row.Data2);
        var markerPosition = ToGltfPosition(fixtureTie, row.X, row.Y, row.Z);

        Expect(row.Data2 == 4096, $"{relativePath}: expected 8461 fixture row to exercise a coordinate Z value of 4096");
        Expect(
            PositionsNearlyEqual(exportedPosition, expectedPosition),
            $"{relativePath}: expected logical vertex {embeddedQVertex.LogicalVertexIndex} to export from second-slot coordinate data even though Data2 is 4096");
        Expect(
            !PositionsNearlyEqual(exportedPosition, markerPosition),
            $"{relativePath}: expected logical vertex {embeddedQVertex.LogicalVertexIndex} not to export the first-slot address marker as a position");

        var positions = ReadExportedPositions(fixtureExport);
        var indices = ReadExportedIndices(fixtureExport);
        var maxEdgeLength = 0f;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = positions[indices[i]];
            var b = positions[indices[i + 1]];
            var c = positions[indices[i + 2]];
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(a, b));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(b, c));
            maxEdgeLength = MathF.Max(maxEdgeLength, Distance(c, a));
        }

        Expect(
            maxEdgeLength < 7f,
            $"{relativePath}: expected 8461 max triangle edge below 7 after embedded-Q coordinate decoding, got {maxEdgeLength}");

        Console.WriteLine($"PASS DL tie embedded-Q coordinate position {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie embedded-Q coordinate position {relativePath}");
    }
}

void ValidatePartialTileEdgeTextureFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9561", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
        var root = gltfDocument.RootElement;
        var matchedPartialTileEdge = false;
        foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            var extras = primitive.GetProperty("extras");
            if (extras.GetProperty("PacketIndex").GetInt32() != 1
                || extras.GetProperty("ShaderIndex").GetInt32() != 1)
            {
                continue;
            }

            var texCoordAccessorIndex = primitive
                .GetProperty("attributes")
                .GetProperty("TEXCOORD_0")
                .GetInt32();
            var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
            var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
            if (indices.Count >= 3)
            {
                var a = texCoords[indices[0]];
                var b = texCoords[indices[1]];
                var c = texCoords[indices[2]];
                matchedPartialTileEdge =
                    TexCoordsNearlyEqualModulo(a, 0f, 0f)
                    && TexCoordsNearlyEqualModulo(b, 0f, 0.25f)
                    && TexCoordsNearlyEqualModulo(c, 0.25f, 0f);
            }
        }

        Expect(
            matchedPartialTileEdge,
            $"{relativePath}: expected 9561 partial-tile cylinder UVs to preserve authored texture-edge coordinates without inward bias");

        Console.WriteLine($"PASS DL tie partial-tile texture edge {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie partial-tile texture edge {relativePath}");
    }
}

void ValidateWidePanelTextureFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9059", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
        var root = gltfDocument.RootElement;
        var checkedUpperPanelTriangles = 0;
        var collapsedUpperPanelTriangles = 0;
        foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            var extras = primitive.GetProperty("extras");
            if (extras.GetProperty("PacketIndex").GetInt32() != 0
                || extras.GetProperty("ShaderIndex").GetInt32() != 0)
            {
                continue;
            }

            var texCoordAccessorIndex = primitive
                .GetProperty("attributes")
                .GetProperty("TEXCOORD_0")
                .GetInt32();
            var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
            var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
            var triangleLimit = Math.Min(indices.Count / 3, 16);
            for (var triangleIndex = 0; triangleIndex < triangleLimit; triangleIndex++)
            {
                var indexOffset = triangleIndex * 3;
                var a = texCoords[indices[indexOffset]];
                var b = texCoords[indices[indexOffset + 1]];
                var c = texCoords[indices[indexOffset + 2]];
                var uSpan = Range(a.U, b.U, c.U);
                var vSpan = Range(a.V, b.V, c.V);
                checkedUpperPanelTriangles++;
                if (uSpan <= 1.4f || uSpan >= 1.6f || vSpan <= 0.9f || vSpan >= 1.1f)
                {
                    collapsedUpperPanelTriangles++;
                }
            }
        }

        Expect(
            checkedUpperPanelTriangles == 16 && collapsedUpperPanelTriangles == 0,
            $"{relativePath}: expected 9059 upper cylinder UVs to preserve all authored 1.5-tile panel spans, got {collapsedUpperPanelTriangles} collapsed triangle(s)");

        Console.WriteLine($"PASS DL tie wide-panel texture span {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie wide-panel texture span {relativePath}");
    }
}

void ValidateOutwardWindingNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "8802", "core.bin");
    var daePath = Path.Combine(tiesRoot, "ALL DL", "8802", "mesh.dae");
    if (!File.Exists(fixturePath) || !File.Exists(daePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        var positions = ReadExportedPositions(fixtureExport);
        var indices = ReadExportedIndices(fixtureExport);
        var exportedFaceKeys = BuildTriangleFaceKeyCounts(positions, indices);
        var referenceFaceKeys = ReadDaeTriangleFaceKeyCounts(daePath);

        var missingReferenceFaceCount = 0;
        foreach (var (key, count) in referenceFaceKeys)
        {
            exportedFaceKeys.TryGetValue(key, out var exportedCount);
            if (exportedCount < count)
            {
                missingReferenceFaceCount += count - exportedCount;
            }
        }

        Expect(
            exportedFaceKeys.Values.Sum() == referenceFaceKeys.Values.Sum(),
            $"{relativePath}: expected 8802 exported triangle count to match the DAE reference");
        Expect(
            missingReferenceFaceCount == 0,
            $"{relativePath}: expected 8802 exported triangle winding to match mesh.dae, got {missingReferenceFaceCount} missing/flipped reference face(s)");

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var topNormalCount = 0;
            var weakTopNormalCount = 0;
            for (var i = 0; i < Math.Min(positions.Count, normals.Count); i++)
            {
                if (positions[i].Y < 18f)
                {
                    continue;
                }

                topNormalCount++;
                if (normals[i].Y < 0.4f)
                {
                    weakTopNormalCount++;
                }
            }

            Expect(topNormalCount > 0, $"{relativePath}: expected to find 8802 authored top-band normals");
            Expect(
                weakTopNormalCount == 0,
                $"{relativePath}: expected 8802 authored top-band normals to point up/out after table layout selection, got {weakTopNormalCount} weak normal(s)");
        }

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            Expect(
                diagnosticsDocument.RootElement.GetProperty("SourceTableNormalVertexCount").GetInt32() >= 100,
                $"{relativePath}: expected 8802 to apply the dense decoded vertex-normal table");
        }

        Console.WriteLine($"PASS DL tie reference winding normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie reference winding normals {relativePath}");
    }
}

void ValidateHardSurfaceTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "8856", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            Expect(
                diagnostics.GetProperty("SourceTableNormalTargetMode").GetString() == "LogicalVertex",
                $"{relativePath}: expected hard-surface normal table remaps to target logical vertices");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected mixed-orientation hard-surface source normals to be reoriented to generated faces");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedUpperNormalCount = 0;
            var strongDownUpperNormalCount = 0;

            for (var i = 0; i < Math.Min(positions.Count, normals.Count); i++)
            {
                if (positions[i].Y < 0f)
                {
                    continue;
                }

                checkedUpperNormalCount++;
                if (normals[i].Y < -0.4f)
                {
                    strongDownUpperNormalCount++;
                }
            }

            Expect(checkedUpperNormalCount > 0, $"{relativePath}: expected to find upper hard-surface normals");
            Expect(
                strongDownUpperNormalCount == 0,
                $"{relativePath}: expected 8856 upper hard-surface normals not to point downward, got {strongDownUpperNormalCount}");
        }

        Console.WriteLine($"PASS DL tie hard-surface table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie hard-surface table normals {relativePath}");
    }
}

void ValidateMixedOrientationTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9038", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            Expect(
                diagnostics.GetProperty("SourceTableNormalTargetMode").GetString() == "LogicalVertex",
                $"{relativePath}: expected mixed-orientation normal table remaps to target logical vertices");
            Expect(
                diagnostics.GetProperty("SourceTableNormalInvertedAcceptedVertexCount").GetInt32()
                    > diagnostics.GetProperty("SourceTableNormalSignedAcceptedVertexCount").GetInt32(),
                $"{relativePath}: expected fixture to exercise mostly inverted accepted source normals");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected mostly inverted source normals to be reoriented instead of preserved directly");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedTriangleCount = 0;
            var invertedAverageNormalCount = 0;

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var aIndex = indices[i];
                    var bIndex = indices[i + 1];
                    var cIndex = indices[i + 2];
                    if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
                    {
                        continue;
                    }

                    var averageNormal = Normalize((
                        normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                        normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                        normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                    checkedTriangleCount++;
                    if (NormalDot(faceNormal, averageNormal) < 0f)
                    {
                        invertedAverageNormalCount++;
                    }
                }
            }

            Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find exported 9038 triangles");
            Expect(
                invertedAverageNormalCount == 0,
                $"{relativePath}: expected 9038 average vertex normals to face the same hemisphere as triangle normals, got {invertedAverageNormalCount} inverted triangle(s)");
        }

        Console.WriteLine($"PASS DL tie mixed-orientation table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie mixed-orientation table normals {relativePath}");
    }
}

void ValidateLowCoverageTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9032", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            Expect(
                diagnostics.GetProperty("SourceTableNormalTargetMode").GetString() == "LogicalVertex",
                $"{relativePath}: expected low-coverage normal table remaps to target logical vertices");
            Expect(
                diagnostics.GetProperty("SourceTableNormalCandidateVertexCount").GetInt32()
                    > diagnostics.GetProperty("SourceTableNormalAcceptedVertexCount").GetInt32() * 3,
                $"{relativePath}: expected fixture to exercise a sparse accepted subset of the selected source table");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected sparse source-table coverage not to preserve every table normal directly");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedTriangleCount = 0;
            var invertedAverageNormalCount = 0;

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var aIndex = indices[i];
                    var bIndex = indices[i + 1];
                    var cIndex = indices[i + 2];
                    if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
                    {
                        continue;
                    }

                    var averageNormal = Normalize((
                        normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                        normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                        normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                    checkedTriangleCount++;
                    if (NormalDot(faceNormal, averageNormal) < 0f)
                    {
                        invertedAverageNormalCount++;
                    }
                }
            }

            Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find exported 9032 triangles");
            Expect(
                invertedAverageNormalCount == 0,
                $"{relativePath}: expected 9032 low-coverage table normals not to invert side panels, got {invertedAverageNormalCount} inverted triangle(s)");
        }

        Console.WriteLine($"PASS DL tie low-coverage table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie low-coverage table normals {relativePath}");
    }
}

void ValidateBroadSparseTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "8563", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            var candidateCount = diagnostics.GetProperty("SourceTableNormalCandidateVertexCount").GetInt32();
            var acceptedCount = diagnostics.GetProperty("SourceTableNormalAcceptedVertexCount").GetInt32();
            Expect(
                candidateCount >= acceptedCount * 4,
                $"{relativePath}: expected fixture to exercise a broad table with sparse accepted normal coverage");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected broad sparse source-table coverage not to preserve every table normal directly");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedTriangleCount = 0;
            var invertedAverageNormalCount = 0;

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var aIndex = indices[i];
                    var bIndex = indices[i + 1];
                    var cIndex = indices[i + 2];
                    if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
                    {
                        continue;
                    }

                    var averageNormal = Normalize((
                        normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                        normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                        normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                    checkedTriangleCount++;
                    if (NormalDot(faceNormal, averageNormal) < 0f)
                    {
                        invertedAverageNormalCount++;
                    }
                }
            }

            Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find exported 8563 triangles");
            Expect(
                invertedAverageNormalCount == 0,
                $"{relativePath}: expected 8563 broad sparse table normals not to invert wall/arch lighting, got {invertedAverageNormalCount} inverted triangle(s)");
        }

        Console.WriteLine($"PASS DL tie broad sparse table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie broad sparse table normals {relativePath}");
    }
}

void ValidateHighInvertedRatioTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9198", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            var acceptedCount = diagnostics.GetProperty("SourceTableNormalAcceptedVertexCount").GetInt32();
            var invertedCount = diagnostics.GetProperty("SourceTableNormalInvertedAcceptedVertexCount").GetInt32();
            Expect(
                invertedCount * 10 > acceptedCount,
                $"{relativePath}: expected fixture to exercise a high inverted source-normal accepted ratio");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected high inverted source-normal ratio to be reoriented instead of preserved directly");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedTriangleCount = 0;
            var invertedAverageNormalCount = 0;

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var aIndex = indices[i];
                    var bIndex = indices[i + 1];
                    var cIndex = indices[i + 2];
                    if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
                    {
                        continue;
                    }

                    var averageNormal = Normalize((
                        normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                        normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                        normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                    checkedTriangleCount++;
                    if (NormalDot(faceNormal, averageNormal) < 0f)
                    {
                        invertedAverageNormalCount++;
                    }
                }
            }

            Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find exported 9198 triangles");
            Expect(
                invertedAverageNormalCount == 0,
                $"{relativePath}: expected 9198 high inverted-ratio table normals not to invert side/top lighting, got {invertedAverageNormalCount} inverted triangle(s)");
        }

        Console.WriteLine($"PASS DL tie high inverted-ratio table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie high inverted-ratio table normals {relativePath}");
    }
}

void ValidateUpperStrongDownTableNormalFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9468", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        Expect(
            fixtureTie.VertexNormalRemaps.Any(remap =>
                remap.Offset == 0xA0C8
                && remap.NormalIndex == 102
                && remap.VertexRowIndex == 23
                && (remap.RawVertex & 3) != 0),
            $"{relativePath}: expected low-bit flagged normal-table row target to decode as vertex row 23");

        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            var upperCount = diagnostics.GetProperty("SourceTableNormalUpperHemisphereVertexCount").GetInt32();
            var upperStrongDownCount = diagnostics.GetProperty("SourceTableNormalUpperHemisphereStrongDownVertexCount").GetInt32();
            Expect(
                upperStrongDownCount * 10 > upperCount,
                $"{relativePath}: expected fixture to exercise excessive downward source normals on the upper half");
            Expect(
                !diagnostics.GetProperty("SourceTableNormalPreserveSourceOrientation").GetBoolean(),
                $"{relativePath}: expected upper-half downward source normals to be reoriented instead of preserved directly");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var checkedCount = 0;
            var downwardCount = 0;
            var maxY = positions.Max(position => position.Y);
            var flatTopCornerCount = 0;
            var tiltedFlatTopCornerCount = 0;
            var worstFlatTopCornerDot = 1f;
            var sideOpeningTriangleCount = 0;
            var opposedSideOpeningTriangleCount = 0;
            var worstSideOpeningNormalDot = 1f;
            var outerSideTriangleCount = 0;
            var tiltedOuterSideTriangleCount = 0;
            var smoothedOuterSideCornerCount = 0;
            var worstOuterSideNormalDot = 1f;
            var worstOuterSideNormalY = 0f;

            for (var i = 0; i < Math.Min(positions.Count, normals.Count); i++)
            {
                var position = positions[i];
                var radius = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
                if (position.Y <= 1.5f || radius is <= 5.5f or >= 10.8f)
                {
                    continue;
                }

                checkedCount++;
                if (normals[i].Y < 0f)
                {
                    downwardCount++;
                }
            }

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                for (var i = 0; i + 2 < indices.Count; i += 3)
                {
                    var aIndex = indices[i];
                    var bIndex = indices[i + 1];
                    var cIndex = indices[i + 2];
                    var centerY = (positions[aIndex].Y + positions[bIndex].Y + positions[cIndex].Y) / 3f;
                    if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
                    {
                        continue;
                    }

                    if (centerY >= maxY - 0.08f && faceNormal.Y >= 0.999f)
                    {
                        CheckFlatTopCorner(aIndex);
                        CheckFlatTopCorner(bIndex);
                        CheckFlatTopCorner(cIndex);
                    }

                    var centerX = (positions[aIndex].X + positions[bIndex].X + positions[cIndex].X) / 3f;
                    var centerZ = (positions[aIndex].Z + positions[bIndex].Z + positions[cIndex].Z) / 3f;
                    var radius = MathF.Sqrt(centerX * centerX + centerZ * centerZ);
                    var angleDegrees = MathF.Atan2(centerZ, centerX) * 180f / MathF.PI;
                    if (centerY > 1f
                        && MathF.Abs(faceNormal.Y) < 0.25f
                        && radius is > 5f and < 26f
                        && angleDegrees is > -112f and < -68f)
                    {
                        var averageNormal = Normalize((
                            normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                            normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                            normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                        var dot = NormalDot(faceNormal, averageNormal);
                        sideOpeningTriangleCount++;
                        worstSideOpeningNormalDot = MathF.Min(worstSideOpeningNormalDot, dot);
                        if (dot < 0f)
                        {
                            opposedSideOpeningTriangleCount++;
                        }
                    }

                    if (centerY > 1f
                        && MathF.Abs(faceNormal.Y) < 0.05f
                        && radius > 22f)
                    {
                        var averageNormal = Normalize((
                            normals[aIndex].X + normals[bIndex].X + normals[cIndex].X,
                            normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y,
                            normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z));
                        var dot = NormalDot(faceNormal, averageNormal);
                        outerSideTriangleCount++;
                        worstOuterSideNormalDot = MathF.Min(worstOuterSideNormalDot, dot);
                        worstOuterSideNormalY = MathF.Max(worstOuterSideNormalY, MathF.Abs(averageNormal.Y));
                        if (dot < 0.75f)
                        {
                            tiltedOuterSideTriangleCount++;
                        }

                        CountSmoothedOuterSideCorner(aIndex);
                        CountSmoothedOuterSideCorner(bIndex);
                        CountSmoothedOuterSideCorner(cIndex);
                    }

                    void CountSmoothedOuterSideCorner(int normalIndex)
                    {
                        if (NormalDot(faceNormal, normals[normalIndex]) < 0.995f)
                        {
                            smoothedOuterSideCornerCount++;
                        }
                    }

                    void CheckFlatTopCorner(int normalIndex)
                    {
                        var dot = NormalDot(faceNormal, normals[normalIndex]);
                        flatTopCornerCount++;
                        worstFlatTopCornerDot = MathF.Min(worstFlatTopCornerDot, dot);
                        if (dot < 0.88f)
                        {
                            tiltedFlatTopCornerCount++;
                        }
                    }
                }
            }

            Expect(checkedCount > 0, $"{relativePath}: expected to find exported 9468 inner rim normals");
            Expect(
                downwardCount == 0,
                $"{relativePath}: expected 9468 inner rim normals not to point downward, got {downwardCount} downward normal(s)");
            Expect(flatTopCornerCount > 0, $"{relativePath}: expected to find exported 9468 flat top triangles");
            Expect(
                tiltedFlatTopCornerCount == 0,
                $"{relativePath}: expected 9468 flat top face normals to stay close to the face normal, got {tiltedFlatTopCornerCount} tilted corner(s), worst dot {worstFlatTopCornerDot}");
            Expect(sideOpeningTriangleCount > 0, $"{relativePath}: expected to find exported 9468 side-opening triangles");
            Expect(
                opposedSideOpeningTriangleCount == 0,
                $"{relativePath}: expected 9468 side-opening normals not to oppose their final glTF faces, got {opposedSideOpeningTriangleCount} opposed triangle(s), worst dot {worstSideOpeningNormalDot}");
            Expect(outerSideTriangleCount > 0, $"{relativePath}: expected to find exported 9468 outer side triangles");
            Expect(
                tiltedOuterSideTriangleCount == 0,
                $"{relativePath}: expected 9468 outer side normals not to inherit top-face lighting, got {tiltedOuterSideTriangleCount} tilted triangle(s), worst dot {worstOuterSideNormalDot}, worst Y {worstOuterSideNormalY}");
            Expect(
                worstOuterSideNormalY < 0.05f,
                $"{relativePath}: expected 9468 outer side normals to be vertically flattened, worst Y {worstOuterSideNormalY}");
            Expect(
                smoothedOuterSideCornerCount > 0,
                $"{relativePath}: expected 9468 outer side corners to retain smooth side-wall normals instead of all matching per-face normals");
        }

        Console.WriteLine($"PASS DL tie upper strong-down table normals {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie upper strong-down table normals {relativePath}");
    }
}

void ValidateOrganicDuplicatePositionNormalWeldFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9324", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            Expect(
                diagnostics.GetProperty("DuplicatePositionNormalWeldMode").GetString() == "Full",
                $"{relativePath}: expected organic duplicate-position normal seams to use the full weld path");
            Expect(
                diagnostics.GetProperty("DuplicatePositionIncompatibleNormalPairCount").GetInt32() > 100,
                $"{relativePath}: expected fixture to exercise many incompatible duplicate-position normal pairs");
        }

        using (var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes))
        {
            var root = gltfDocument.RootElement;
            var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
            var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
            var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
            var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
            var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
            var normalIndicesByPosition = new Dictionary<(int X, int Y, int Z), HashSet<int>>();

            foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
            {
                var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
                foreach (var index in indices)
                {
                    var key = QuantizedPositionKey(positions[index]);
                    if (!normalIndicesByPosition.TryGetValue(key, out var normalIndices))
                    {
                        normalIndices = [];
                        normalIndicesByPosition[key] = normalIndices;
                    }

                    normalIndices.Add(index);
                }
            }

            var duplicateNormalPairCount = 0;
            var worstDuplicateNormalDot = 1f;
            foreach (var normalIndices in normalIndicesByPosition.Values.Where(indices => indices.Count > 1))
            {
                var normalIndexList = normalIndices.ToArray();
                for (var i = 0; i < normalIndexList.Length; i++)
                {
                    for (var j = i + 1; j < normalIndexList.Length; j++)
                    {
                        duplicateNormalPairCount++;
                        worstDuplicateNormalDot = MathF.Min(
                            worstDuplicateNormalDot,
                            NormalDot(normals[normalIndexList[i]], normals[normalIndexList[j]]));
                    }
                }
            }

            Expect(duplicateNormalPairCount > 100, $"{relativePath}: expected to find duplicate 9324 organic surface normals");
            Expect(
                worstDuplicateNormalDot >= 0.995f,
                $"{relativePath}: expected duplicate 9324 organic surface positions to share welded normals, got worst dot {worstDuplicateNormalDot}");
        }

        Console.WriteLine($"PASS DL tie organic duplicate-position normal weld {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie organic duplicate-position normal weld {relativePath}");
    }
}

void ValidateDlLevel07AmbientRegressionFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9085", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(dlProfile));
        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                GameProfile = dlProfile,
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
        {
            var diagnostics = diagnosticsDocument.RootElement;
            Expect(
                diagnostics.GetProperty("AmbientIndexAccessorCount").GetInt32() > 0
                && diagnostics.GetProperty("ResolvedAmbientIndexVertexCount").GetInt32() > 0
                && diagnostics.GetProperty("AmbientColorRecipeCount").GetInt32() > 0,
                $"{relativePath}: expected DL level07 tie ambient color attributes to remain available");
            Expect(
                diagnostics.GetProperty("SourcePacketRowNormalVertexCount").GetInt32() == 0,
                $"{relativePath}: expected DL export not to apply UYA packet-row source normals");
        }

        Console.WriteLine($"PASS DL tie level07 ambient guard {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie level07 ambient guard {relativePath}");
    }
}

void ValidateLogicalNormalRemapMetadataFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9786", "core.bin");
    if (!File.Exists(fixturePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        Expect(
            fixtureTie.VertexNormalRemaps.Count >= 300,
            $"{relativePath}: expected 9786 to decode the dense logical normal remap chunks");
        Expect(
            fixtureTie.VertexNormalRemaps.Any(remap =>
                remap.LogicalVertexIndex == 70
                && remap.NormalIndex == 19
                && remap.Offset == 0x967C),
            $"{relativePath}: expected 9786 pipe-elbow logical vertex 70 to map to authored normal 19");

        var textureResources = BuildFixtureTextureResources(fixturePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions
            {
                BufferFileName = "tie.buffer.bin",
                ExternalTextureUris = textureResources?.Uris,
                ExternalTextureSizes = textureResources?.Sizes,
                ExternalTextureAlpha = textureResources?.Alpha
            });

        using var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes);
        var diagnostics = diagnosticsDocument.RootElement;
        Expect(
            diagnostics.GetProperty("SourceTableNormalTargetMode").GetString() == "LogicalVertex",
            $"{relativePath}: expected 9786 normal table remaps to target logical vertices");
        Expect(
            diagnostics.GetProperty("SourceTableNormalCandidateVertexCount").GetInt32() >= 100,
            $"{relativePath}: expected 9786 to expose broad logical normal-table coverage");
        Expect(
            diagnostics.GetProperty("SourceTableNormalVertexCount").GetInt32() >= 20,
            $"{relativePath}: expected 9786 to apply authored logical normal-table vertices");

        Console.WriteLine($"PASS DL tie logical normal remap metadata {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie logical normal remap metadata {relativePath}");
    }
}

void ValidateInvertedComponentWindingFixture()
{
    var fixturePath = Path.Combine(tiesRoot, "ALL DL", "9777", "core.bin");
    var daePath = Path.Combine(Path.GetDirectoryName(fixturePath)!, "mesh.dae");
    if (!File.Exists(fixturePath) || !File.Exists(daePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = TieClassReader.Read(input);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions { BufferFileName = "tie.buffer.bin" });
        ExpectExportWindingMatchesDae(fixtureExport, daePath, relativePath);
        Console.WriteLine($"PASS DL tie reference winding {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL DL tie reference winding {relativePath}");
    }
}

void ValidateGc336StripTokenSemantics(TieClass fixtureTie, string relativePath)
{
    Expect(
        fixtureTie.PacketDataBlocks.SelectMany(block => block.StripTokens).All(token => token.MatchesExpectedGsPacketWriteOffset),
        $"{relativePath}: expected decoded strip-token GS write offsets to match strip control VU address sequences");
    var semanticPacket = fixtureTie.PacketDataBlocks.FirstOrDefault(block => block.LodIndex == 0 && block.PacketIndex == 3);
    Expect(semanticPacket is not null, $"{relativePath}: expected LOD0 packet 3 for strip-token semantic coverage");
    if (semanticPacket is null)
    {
        return;
    }

    Expect(semanticPacket.StripControls.Count >= 3, $"{relativePath}: expected LOD0 packet 3 to have at least 3 strip controls");
    Expect(semanticPacket.StripControls.All(strip => strip.ControlData1 == 0), $"{relativePath}: expected GC 336 packet 3 strip control Data1 bytes to be zero");
    if (semanticPacket.StripControls.Count < 3
        || semanticPacket.StripControls[0].DecodedTokens.Count == 0
        || semanticPacket.StripControls[1].DecodedTokens.Count == 0
        || semanticPacket.StripControls[2].DecodedTokens.Count == 0)
    {
        return;
    }

    var gcStrip0FirstToken = semanticPacket.StripControls[0].DecodedTokens[0];
    Expect(gcStrip0FirstToken.Value == 0x07, $"{relativePath}: expected packet 3 strip 0 first token 0x07, got 0x{gcStrip0FirstToken.Value:X2}");
    Expect(gcStrip0FirstToken.AddressMode == TiePacketStripTokenAddressMode.AbsoluteVertexWriteOffset, $"{relativePath}: expected packet 3 strip 0 first token to decode as absolute, got {gcStrip0FirstToken.AddressMode}");
    Expect(gcStrip0FirstToken.ResolvedGsPacketWriteOffset == 0x07, $"{relativePath}: expected packet 3 strip 0 first token to resolve 0x07, got {gcStrip0FirstToken.ResolvedGsPacketWriteOffset}");
    Expect(gcStrip0FirstToken.ReferencedGsPacketWriteOffset == 0x07, $"{relativePath}: expected packet 3 strip 0 first token to reference 0x07, got {gcStrip0FirstToken.ReferencedGsPacketWriteOffset}");
    var gcStrip1FirstToken = semanticPacket.StripControls[1].DecodedTokens[0];
    Expect(gcStrip1FirstToken.Value == 0xF6, $"{relativePath}: expected packet 3 strip 1 first token 0xF6, got 0x{gcStrip1FirstToken.Value:X2}");
    Expect(gcStrip1FirstToken.SignedValue == -10, $"{relativePath}: expected packet 3 strip 1 first token signed value -10, got {gcStrip1FirstToken.SignedValue}");
    Expect(gcStrip1FirstToken.AddressMode == TiePacketStripTokenAddressMode.PreviousStripVertexReference, $"{relativePath}: expected packet 3 strip 1 first token to decode as a previous-strip reference, got {gcStrip1FirstToken.AddressMode}");
    Expect(gcStrip1FirstToken.ReferencesPreviousStripVertex, $"{relativePath}: expected packet 3 strip 1 first token to be marked as a previous-strip reference");
    Expect(gcStrip1FirstToken.RestartGap == 10, $"{relativePath}: expected packet 3 strip 1 restart gap 10, got {gcStrip1FirstToken.RestartGap}");
    Expect(gcStrip1FirstToken.ResolvedGsPacketWriteOffset == 0x56, $"{relativePath}: expected packet 3 strip 1 first token physical write offset 0x56, got {gcStrip1FirstToken.ResolvedGsPacketWriteOffset}");
    Expect(gcStrip1FirstToken.ReferencedGsPacketWriteOffset == 0x4C, $"{relativePath}: expected packet 3 strip 1 first token draw reference 0x4C, got {gcStrip1FirstToken.ReferencedGsPacketWriteOffset}");
    var gcStrip2FirstToken = semanticPacket.StripControls[2].DecodedTokens[0];
    Expect(gcStrip2FirstToken.Value == 0xFC, $"{relativePath}: expected packet 3 strip 2 first token 0xFC, got 0x{gcStrip2FirstToken.Value:X2}");
    Expect(gcStrip2FirstToken.SignedValue == -4, $"{relativePath}: expected packet 3 strip 2 first token signed value -4, got {gcStrip2FirstToken.SignedValue}");
    Expect(gcStrip2FirstToken.AddressMode == TiePacketStripTokenAddressMode.PreviousStripVertexReference, $"{relativePath}: expected packet 3 strip 2 first token to decode as a previous-strip reference, got {gcStrip2FirstToken.AddressMode}");
    Expect(gcStrip2FirstToken.ReferencesPreviousStripVertex, $"{relativePath}: expected packet 3 strip 2 first token to be marked as a previous-strip reference");
    Expect(gcStrip2FirstToken.RestartGap == 4, $"{relativePath}: expected packet 3 strip 2 restart gap 4, got {gcStrip2FirstToken.RestartGap}");
    Expect(gcStrip2FirstToken.ResolvedGsPacketWriteOffset == 0x63, $"{relativePath}: expected packet 3 strip 2 first token physical write offset 0x63, got {gcStrip2FirstToken.ResolvedGsPacketWriteOffset}");
    Expect(gcStrip2FirstToken.ReferencedGsPacketWriteOffset == 0x5F, $"{relativePath}: expected packet 3 strip 2 first token draw reference 0x5F, got {gcStrip2FirstToken.ReferencedGsPacketWriteOffset}");

    var topologyStrip1 = fixtureTie.LodTopologies
        .FirstOrDefault(topology => topology.LodIndex == 0)?
        .Strips
        .FirstOrDefault(strip => strip.PacketIndex == 3 && strip.PacketStripIndex == 1);
    Expect(topologyStrip1 is not null, $"{relativePath}: expected LOD0 packet 3 strip 1 topology coverage");
    if (topologyStrip1 is not null && topologyStrip1.LogicalVertices.Count >= 2)
    {
        Expect(topologyStrip1.LogicalVertices[0].GsPacketWriteOffset == gcStrip1FirstToken.ExpectedGsPacketWriteOffset, $"{relativePath}: expected topology to keep packet 3 strip 1 on the physical write sequence");
    }

    var topologyStrip2 = fixtureTie.LodTopologies
        .FirstOrDefault(topology => topology.LodIndex == 0)?
        .Strips
        .FirstOrDefault(strip => strip.PacketIndex == 3 && strip.PacketStripIndex == 2);
    Expect(topologyStrip2 is not null, $"{relativePath}: expected LOD0 packet 3 strip 2 topology coverage");
    if (topologyStrip2 is not null && topologyStrip2.LogicalVertices.Count >= 2)
    {
        Expect(topologyStrip2.LogicalVertices[0].GsPacketWriteOffset == gcStrip2FirstToken.ExpectedGsPacketWriteOffset, $"{relativePath}: expected topology to keep packet 3 strip 2 on the physical write sequence");
    }
}

void ValidateGcFlatPlatformWindingFixture()
{
    var fixturePath = Path.Combine(repoRoot, "test-assets", "GC Ties", "unsorted", "336", "core.bin");
    var daePath = Path.Combine(Path.GetDirectoryName(fixturePath)!, "mesh.dae");
    if (!File.Exists(fixturePath) || !File.Exists(daePath))
    {
        return;
    }

    var relativePath = Path.GetRelativePath(repoRoot, fixturePath);
    try
    {
        using var input = File.OpenRead(fixturePath);
        var fixtureTie = ReadGcTie(input);
        ValidateGc336StripTokenSemantics(fixtureTie, relativePath);
        var fixtureExport = TieGltfExporter.Export(
            fixtureTie,
            "tie.gltf",
            new TieGltfExportOptions { BufferFileName = "tie.buffer.bin", GameLabel = "GC" });
        ExpectExportWindingMatchesDae(fixtureExport, daePath, relativePath);
        Console.WriteLine($"PASS GC tie reference winding {relativePath}");
    }
    catch (Exception ex)
    {
        failures.Add($"{relativePath}: {ex.Message}");
        Console.WriteLine($"FAIL GC tie reference winding {relativePath}");
    }
}

void ValidateGlowMultipassResolution(TieClass fixtureTie, string relativePath)
{
    var resolvedGlowLods = fixtureTie.GlowRgbaRemaps
        .Where(remap => remap.LodIndex.HasValue)
        .Select(remap => remap.LodIndex!.Value)
        .Distinct()
        .ToArray();
    foreach (var lodIndex in resolvedGlowLods)
    {
        const byte glowPassFlags = 8;
        var multipassPacketIndices = fixtureTie.PacketTables
            .FirstOrDefault(table => table.LodIndex == lodIndex)?
            .Packets
            .Where(packet => packet.PassFlags == glowPassFlags)
            .Select(packet => packet.PacketIndex)
            .ToHashSet() ?? [];
        if (multipassPacketIndices.Count == 0)
        {
            continue;
        }

        var glowPacketIndices = fixtureTie.GlowRgbaVertices
            .Where(vertex => vertex.LodIndex == lodIndex)
            .Select(vertex => vertex.PacketIndex)
            .Distinct()
            .ToHashSet();
        Expect(
            glowPacketIndices.SetEquals(multipassPacketIndices),
            $"{relativePath}: expected LOD{lodIndex} glow RGBA coverage packets [{string.Join(", ", multipassPacketIndices.OrderBy(index => index))}], got [{string.Join(", ", glowPacketIndices.OrderBy(index => index))}]");

        var topology = fixtureTie.LodTopologies[lodIndex];
        foreach (var packetIndex in multipassPacketIndices)
        {
            var expectedVertexCount = topology.LogicalVertices.Count(vertex => vertex.PacketIndex == packetIndex);
            var glowVertexCount = fixtureTie.GlowRgbaVertices.Count(vertex =>
                vertex.LodIndex == lodIndex && vertex.PacketIndex == packetIndex);
            Expect(
                glowVertexCount == expectedVertexCount,
                $"{relativePath}: expected LOD{lodIndex} packet {packetIndex} glow coverage to include {expectedVertexCount} logical vertices, got {glowVertexCount}");
        }
    }
}

void ValidatePacketDataAccuracy(TieClass fixtureTie, string relativePath, int lodIndex)
{
    var packetTable = fixtureTie.PacketTables.FirstOrDefault(table => table.LodIndex == lodIndex);
    if (packetTable is null || packetTable.Packets.Count == 0)
    {
        return;
    }

    var blocksByPacketIndex = fixtureTie.PacketDataBlocks
        .Where(block => block.LodIndex == lodIndex)
        .ToDictionary(block => block.PacketIndex);
    foreach (var packet in packetTable.Packets)
    {
        if (!blocksByPacketIndex.TryGetValue(packet.PacketIndex, out var block))
        {
            continue;
        }

        Expect(
            block.SetupRows.Count == 2,
            $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} to decode two setup rows");
        foreach (var row in block.SetupRows)
        {
            Expect(
                row.Bytes.Length == 0x10,
                $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} setup row {row.Index} to preserve 16 raw bytes");
            Expect(
                row.Words.Count == 4,
                $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} setup row {row.Index} to decode four 32-bit words");
            foreach (var word in row.Words)
            {
                Expect(
                    word.Role == ExpectedSetupWordRole(row.Index, word.WordIndex, packet.ShaderCount),
                    $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} setup row {row.Index} word {word.WordIndex} role {ExpectedSetupWordRole(row.Index, word.WordIndex, packet.ShaderCount)}, got {word.Role}");
            }
        }

        var switchWords = block.SetupRows
            .First(row => row.Index == 0)
            .Words
            .Where(word => word.Role == TiePacketSetupWordRole.ShaderSwitchVuAddress)
            .Select(word => word.Raw)
            .ToArray();
        Expect(
            switchWords.SequenceEqual(packet.ShaderSwitchVuAddresses),
            $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} setup row 0 shader switch words to match decoded shader switches");

        var shaderWords = block.SetupRows
            .First(row => row.Index == 1)
            .Words
            .Where(word => word.Role == TiePacketSetupWordRole.ShaderByteOffset)
            .Select(word => word.Raw)
            .ToArray();
        Expect(
            shaderWords.SequenceEqual(packet.ShaderReferences.Select(reference => reference.ShaderByteOffset)),
            $"{relativePath}: expected LOD{lodIndex} packet {packet.PacketIndex} setup row 1 shader reference words to match decoded shader references");
    }
}

void ValidatePacketDiagnostics(TieClass fixtureTie, JsonElement diagnostics, string relativePath, int lodIndex)
{
    var packetTables = diagnostics.GetProperty("PacketTables");
    Expect(
        packetTables.GetArrayLength() == fixtureTie.PacketTables.Count,
        $"{relativePath}: expected diagnostics to include all packet tables");

    JsonElement? tableElement = null;
    foreach (var candidate in packetTables.EnumerateArray())
    {
        if (candidate.GetProperty("LodIndex").GetInt32() == lodIndex)
        {
            tableElement = candidate;
            break;
        }
    }

    Expect(tableElement.HasValue, $"{relativePath}: expected diagnostics to include LOD{lodIndex} packet table");
    if (!tableElement.HasValue)
    {
        return;
    }

    var packetTable = fixtureTie.PacketTables.First(table => table.LodIndex == lodIndex);
    var packetElements = tableElement.Value.GetProperty("Packets");
    Expect(
        packetElements.GetArrayLength() == packetTable.Packets.Count,
        $"{relativePath}: expected diagnostics LOD{lodIndex} packet count {packetTable.Packets.Count}, got {packetElements.GetArrayLength()}");

    foreach (var packetElement in packetElements.EnumerateArray())
    {
        var packetIndex = packetElement.GetProperty("PacketIndex").GetInt32();
        var packet = packetTable.Packets.First(item => item.PacketIndex == packetIndex);
        var setupRows = packetElement.GetProperty("SetupRows");
        Expect(
            setupRows.GetArrayLength() == 2,
            $"{relativePath}: expected diagnostics LOD{lodIndex} packet {packetIndex} to include two setup rows");

        var shaderSwitchWordCount = 0;
        var shaderReferenceWordCount = 0;
        foreach (var row in setupRows.EnumerateArray())
        {
            foreach (var word in row.GetProperty("Words").EnumerateArray())
            {
                var role = word.GetProperty("Role").GetString();
                if (role == nameof(TiePacketSetupWordRole.ShaderSwitchVuAddress))
                {
                    shaderSwitchWordCount++;
                }
                else if (role == nameof(TiePacketSetupWordRole.ShaderByteOffset))
                {
                    shaderReferenceWordCount++;
                }
            }
        }

        Expect(
            shaderSwitchWordCount == Math.Max(0, packet.ShaderCount - 1),
            $"{relativePath}: expected diagnostics LOD{lodIndex} packet {packetIndex} shader switch setup word count to match shader count");
        Expect(
            shaderReferenceWordCount == packet.ShaderCount,
            $"{relativePath}: expected diagnostics LOD{lodIndex} packet {packetIndex} shader reference setup word count to match shader count");

        var consistency = packetElement.GetProperty("Consistency");
        foreach (var property in consistency.EnumerateObject())
        {
            Expect(
                property.Value.ValueKind == JsonValueKind.True,
                $"{relativePath}: expected diagnostics LOD{lodIndex} packet {packetIndex} consistency {property.Name} to be true");
        }
    }
}

TiePacketSetupWordRole ExpectedSetupWordRole(int rowIndex, int wordIndex, int shaderCount)
{
    if (rowIndex == 0 && wordIndex < Math.Max(0, shaderCount - 1))
    {
        return TiePacketSetupWordRole.ShaderSwitchVuAddress;
    }

    if (rowIndex == 1 && wordIndex < shaderCount)
    {
        return TiePacketSetupWordRole.ShaderByteOffset;
    }

    return TiePacketSetupWordRole.Unknown;
}

void ValidateGltfExport(
    TieClass fixtureTie,
    TieGltfExport fixtureExport,
    string relativePath,
    int lodIndex,
    IReadOnlyDictionary<int, string>? textureUris,
    IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var meshes = root.GetProperty("meshes");
    Expect(meshes.GetArrayLength() == 1, $"{relativePath}: expected one tie glTF mesh, got {meshes.GetArrayLength()}");
    var primitives = meshes[0].GetProperty("primitives");
    var expectedPrimitiveCount = CountExpectedGltfPrimitiveGroups(fixtureTie, lodIndex);
    Expect(primitives.GetArrayLength() == expectedPrimitiveCount, $"{relativePath}: expected one tie glTF primitive per LOD{lodIndex} packet shader run, got {primitives.GetArrayLength()}");

    var accessors = root.GetProperty("accessors");
    var firstPrimitive = primitives[0];
    var positionAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
    var normalAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32();
    var texCoordAccessorIndex = firstPrimitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
    var positionAccessorCount = accessors[positionAccessorIndex].GetProperty("count").GetInt32();
    var normalAccessorCount = accessors[normalAccessorIndex].GetProperty("count").GetInt32();
    var texCoordAccessorCount = accessors[texCoordAccessorIndex].GetProperty("count").GetInt32();
    Expect(positionAccessorCount >= fixtureTie.Header.Lods[lodIndex].VertexCount, $"{relativePath}: expected LOD{lodIndex} tie glTF position count at least {fixtureTie.Header.Lods[lodIndex].VertexCount}, got {positionAccessorCount}");
    Expect(normalAccessorCount == positionAccessorCount, $"{relativePath}: expected LOD{lodIndex} tie glTF normal count {positionAccessorCount}, got {normalAccessorCount}");
    Expect(texCoordAccessorCount == positionAccessorCount, $"{relativePath}: expected LOD{lodIndex} tie glTF texture coordinate count {positionAccessorCount}, got {texCoordAccessorCount}");
    using (var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes))
    {
        var diagnostics = diagnosticsDocument.RootElement;
        Expect(
            diagnostics.GetProperty("PacketRgbaSlotCount").GetInt32() == CountPacketRgbaSlots(fixtureTie, lodIndex),
            $"{relativePath}: expected LOD{lodIndex} diagnostics to report packet RGBA slot count");
        Expect(
            diagnostics.GetProperty("VertexColor0AccessorCount").GetInt32() == 0,
            $"{relativePath}: expected LOD{lodIndex} standalone export not to attach COLOR_0");
        ValidatePacketDiagnostics(fixtureTie, diagnostics, relativePath, lodIndex);
    }
    ValidateGlowRgbaExport(
        fixtureTie,
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorCount);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var nonUnitNormalCount = normals.Count(normal => MathF.Abs(ExportedVectorLength(normal) - 1f) > 0.001f);
    Expect(nonUnitNormalCount == 0, $"{relativePath}: expected LOD{lodIndex} exported normals to stay unit length, got {nonUnitNormalCount} non-unit normal(s)");
    ValidateTopFaceNormalOrientation(
        fixtureTie,
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorIndex,
        normalAccessorIndex);
    ValidateIndentedRingNormalOrientation(
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorIndex,
        normalAccessorIndex);
    ValidateIndentedRingSideWallNormalOrientation(
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorIndex,
        normalAccessorIndex);
    ValidateArchedRoofNormalContinuity(
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorIndex,
        normalAccessorIndex);
    ValidateTallOrganicNormalContinuity(
        fixtureExport,
        root,
        relativePath,
        lodIndex,
        positionAccessorIndex,
        normalAccessorIndex);

    var exportedIndexCount = 0;
    var texturedPrimitiveCount = 0;
    var texturedMaterialIndices = new HashSet<int>();
    foreach (var primitive in primitives.EnumerateArray())
    {
        var indexAccessorIndex = primitive.GetProperty("indices").GetInt32();
        exportedIndexCount += accessors[indexAccessorIndex].GetProperty("count").GetInt32();
        var materialIndex = primitive.GetProperty("material").GetInt32();
        if (materialIndex != 0)
        {
            texturedPrimitiveCount++;
            texturedMaterialIndices.Add(materialIndex);
        }
    }

    Expect(exportedIndexCount == fixtureTie.Header.Lods[lodIndex].TriangleCount * 3, $"{relativePath}: expected LOD{lodIndex} tie glTF index count {fixtureTie.Header.Lods[lodIndex].TriangleCount * 3}, got {exportedIndexCount}");
    ValidateShaderSwitchMaterialResolution(fixtureExport, relativePath, lodIndex, textureUris is not null && textureUris.Count > 0);
    if (textureUris is not null && textureUris.Count > 0)
    {
        Expect(root.TryGetProperty("images", out var images) && images.GetArrayLength() > 0, $"{relativePath}: expected textured tie glTF to include images");
        Expect(root.TryGetProperty("textures", out var textures) && textures.GetArrayLength() > 0, $"{relativePath}: expected textured tie glTF to include textures");
        Expect(texturedPrimitiveCount > 0, $"{relativePath}: expected at least one tie glTF primitive to use a textured material");
        var materials = root.GetProperty("materials");
        foreach (var materialIndex in texturedMaterialIndices)
        {
            var material = materials[materialIndex];
            ValidateTexturedMaterialAlpha(material, relativePath, materialIndex, textureAlpha);
        }

        ValidateTextureSamplerWrapModes(root, relativePath, lodIndex);
        ValidateRepeatedTextureBoundaries(fixtureExport, root, relativePath, lodIndex);
        ValidateArchedRoofTextureCoordinates(fixtureExport, root, relativePath, lodIndex);
    }
}

void ValidateTexturedMaterialAlpha(
    JsonElement material,
    string relativePath,
    int materialIndex,
    IReadOnlyDictionary<int, TextureAlphaInfo>? textureAlpha)
{
    var materialName = material.TryGetProperty("name", out var nameElement)
        ? nameElement.GetString()
        : null;
    var alpha = materialName is not null
        && TryParseMaterialTextureId(materialName, out var textureId)
        && textureAlpha is not null
        && textureAlpha.TryGetValue(textureId, out var resolvedAlpha)
            ? resolvedAlpha
            : TextureAlphaInfo.Opaque;

    Expect(
        material.TryGetProperty("extras", out var extras),
        $"{relativePath}: expected textured material {materialIndex} to include tie extras");

    var alphaUsage = extras.ValueKind == JsonValueKind.Object
        && extras.TryGetProperty("TieTextureAlphaUsage", out var alphaUsageElement)
        ? alphaUsageElement.GetString()
        : alpha.HasAlpha
            ? TieMaterialAlphaUsage.Opacity.ToString()
            : TieMaterialAlphaUsage.Opaque.ToString();
    Expect(
        Enum.TryParse<TieMaterialAlphaUsage>(alphaUsage, out _),
        $"{relativePath}: expected textured material {materialIndex} alpha usage to be recognized, got {alphaUsage}");

    var expectedAlphaMode = alphaUsage == TieMaterialAlphaUsage.Opacity.ToString()
        ? alpha.GltfAlphaMode
        : null;
    if (expectedAlphaMode is { })
    {
        Expect(
            material.TryGetProperty("alphaMode", out var alphaMode)
            && alphaMode.GetString() == expectedAlphaMode,
            $"{relativePath}: expected textured material {materialIndex} alphaMode {expectedAlphaMode}");

        if (alpha.AlphaMode == TextureAlphaMode.Mask)
        {
            Expect(
                material.TryGetProperty("alphaCutoff", out var alphaCutoff)
                && MathF.Abs(alphaCutoff.GetSingle() - 0.5f) < 0.0001f,
                $"{relativePath}: expected textured material {materialIndex} alphaCutoff 0.5 for mask alpha");
        }
    }
    else
    {
        Expect(
            !material.TryGetProperty("alphaMode", out _),
            $"{relativePath}: expected textured material {materialIndex} to stay opaque");
    }

    if (extras.ValueKind == JsonValueKind.Object)
    {
        Expect(
            extras.GetProperty("TieTextureHasAlpha").GetBoolean() == alpha.HasAlpha,
            $"{relativePath}: expected textured material {materialIndex} alpha extras to report HasAlpha={alpha.HasAlpha}");
        Expect(
            extras.GetProperty("TieTextureAlphaMode").GetString() == alpha.AlphaMode.ToString(),
            $"{relativePath}: expected textured material {materialIndex} alpha extras to report {alpha.AlphaMode}");
        Expect(
            extras.GetProperty("TieTextureAlphaUsage").GetString() == alphaUsage,
            $"{relativePath}: expected textured material {materialIndex} alpha extras to report usage {alphaUsage}");
        if (expectedAlphaMode is not null)
        {
            Expect(
                extras.GetProperty("TieTextureGltfAlphaMode").GetString() == expectedAlphaMode,
                $"{relativePath}: expected textured material {materialIndex} alpha extras to report emitted glTF alpha {expectedAlphaMode}");
        }
        else
        {
            Expect(
                extras.GetProperty("TieTextureGltfAlphaMode").ValueKind == JsonValueKind.Null,
                $"{relativePath}: expected textured material {materialIndex} alpha extras to report no emitted glTF alpha mode");
        }
        Expect(
            extras.GetProperty("TieTextureMinAlpha").GetInt32() == alpha.MinAlpha,
            $"{relativePath}: expected textured material {materialIndex} alpha extras min {alpha.MinAlpha}");
        Expect(
            extras.GetProperty("TieTextureMaxAlpha").GetInt32() == alpha.MaxAlpha,
            $"{relativePath}: expected textured material {materialIndex} alpha extras max {alpha.MaxAlpha}");
    }
}

void ValidateGlowRgbaExport(
    TieClass fixtureTie,
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorCount)
{
    var expectedGlowVertices = fixtureTie.GlowRgbaVertices
        .Where(vertex => vertex.LodIndex == lodIndex)
        .ToArray();
    using var diagnosticsDocument = JsonDocument.Parse(fixtureExport.DiagnosticsBytes);
    var diagnostics = diagnosticsDocument.RootElement;
    Expect(
        diagnostics.GetProperty("DecodedGlowRgbaRemapCount").GetInt32() == fixtureTie.GlowRgbaRemaps.Count,
        $"{relativePath}: expected LOD{lodIndex} glow diagnostics to include {fixtureTie.GlowRgbaRemaps.Count} remap(s)");
    Expect(
        diagnostics.GetProperty("ResolvedGlowRgbaVertexCount").GetInt32() == expectedGlowVertices.Length,
        $"{relativePath}: expected LOD{lodIndex} diagnostics to report {expectedGlowVertices.Length} resolved glow vertices");

    if (expectedGlowVertices.Length == 0)
    {
        return;
    }

    Expect(
        diagnostics.GetProperty("GlowRgbaColorAccessorCount").GetInt32() == 0,
        $"{relativePath}: expected LOD{lodIndex} glow RGBA emission not to export COLOR_0");
    Expect(
        diagnostics.GetProperty("GlowRgbaEmissionVertexCount").GetInt32() > 0,
        $"{relativePath}: expected LOD{lodIndex} diagnostics to report glow emission vertices");
    Expect(
        diagnostics.GetProperty("GlowRgbaCustomAttributeCount").GetInt32() == positionAccessorCount,
        $"{relativePath}: expected LOD{lodIndex} diagnostics to report a tie glow custom attribute for each exported vertex");
    var primitiveWithColor0Count = root
        .GetProperty("meshes")[0]
        .GetProperty("primitives")
        .EnumerateArray()
        .Count(primitive => primitive.GetProperty("attributes").TryGetProperty("COLOR_0", out _));
    Expect(
        primitiveWithColor0Count == 0,
        $"{relativePath}: expected LOD{lodIndex} glow RGBA emission not to attach COLOR_0 attributes");
    var primitiveWithGlowAttributeCount = root
        .GetProperty("meshes")[0]
        .GetProperty("primitives")
        .EnumerateArray()
        .Count(primitive => primitive.GetProperty("attributes").TryGetProperty("_TIE_GLOW_0", out _));
    Expect(
        primitiveWithGlowAttributeCount == root.GetProperty("meshes")[0].GetProperty("primitives").GetArrayLength(),
        $"{relativePath}: expected LOD{lodIndex} primitives to attach _TIE_GLOW_0 for renderer-specific glow previews");

    var glowPrimitiveIndexCount = root
        .GetProperty("meshes")[0]
        .GetProperty("primitives")
        .EnumerateArray()
        .Sum(primitive => primitive.GetProperty("extras").GetProperty("GlowRgbaIndexCount").GetInt32());
    Expect(
        glowPrimitiveIndexCount > 0,
        $"{relativePath}: expected LOD{lodIndex} primitive extras to count glow RGBA indices");

    var emissivePrimitiveCount = 0;
    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        if (!extras.TryGetProperty("GlowRgbaUsesEmission", out var usesEmissionElement)
            || !usesEmissionElement.GetBoolean())
        {
            continue;
        }

        emissivePrimitiveCount++;
        var materialIndex = primitive.GetProperty("material").GetInt32();
        var material = root.GetProperty("materials")[materialIndex];
        var expectedEmissionFactor = GetExpectedGlowEmissionFactor(fixtureTie.Header.GlowRgba);
        Expect(
            material.TryGetProperty("emissiveFactor", out var emissiveFactor)
            && EmissiveFactorMatches(emissiveFactor, expectedEmissionFactor),
            $"{relativePath}: expected LOD{lodIndex} glow-emissive material to include normalized glow RGBA emissiveFactor");
        Expect(
            material.GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out var baseColorTexture)
            && material.TryGetProperty("emissiveTexture", out var emissiveTexture)
            && emissiveTexture.GetProperty("index").GetInt32() == baseColorTexture.GetProperty("index").GetInt32(),
            $"{relativePath}: expected LOD{lodIndex} glow-emissive material to modulate emission with the base texture");
        Expect(
            material.GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[0].GetSingle() == 1f,
            $"{relativePath}: expected LOD{lodIndex} glow-emissive material to keep the textured base color visible");
        Expect(
            material.TryGetProperty("extensions", out var materialExtensions)
            && materialExtensions.TryGetProperty("KHR_materials_emissive_strength", out var emissiveStrengthExtension)
            && emissiveStrengthExtension.GetProperty("emissiveStrength").GetSingle() > 0f,
            $"{relativePath}: expected LOD{lodIndex} glow-emissive material to include KHR_materials_emissive_strength");
        Expect(
            root.TryGetProperty("extensionsUsed", out var extensionsUsed)
            && extensionsUsed.EnumerateArray().Any(extension => extension.GetString() == "KHR_materials_emissive_strength"),
            $"{relativePath}: expected LOD{lodIndex} glTF to declare KHR_materials_emissive_strength when glow emission is used");
    }

    var expectedEmissivePrimitiveCount = diagnostics.GetProperty("GlowRgbaEmissivePrimitiveCount").GetInt32();
    Expect(
        emissivePrimitiveCount == expectedEmissivePrimitiveCount,
        $"{relativePath}: expected LOD{lodIndex} glow-emissive primitive count {expectedEmissivePrimitiveCount}, got {emissivePrimitiveCount}");

    if (lodIndex == 0 && relativePath.Contains("09907_26B3", StringComparison.Ordinal))
    {
        var nonStripeGlowPrimitiveCount = 0;
        var nonStripeEmissivePrimitiveCount = 0;
        var stripeGlowIndexCount = 0;
        var stripeEmissivePrimitiveCount = 0;
        foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            var extras = primitive.GetProperty("extras");
            var glowIndexCount = extras.GetProperty("GlowRgbaIndexCount").GetInt32();
            var usesEmission = extras.TryGetProperty("GlowRgbaUsesEmission", out var usesEmissionElement)
                && usesEmissionElement.GetBoolean();
            var isStripePrimitive = extras.GetProperty("PacketIndex").GetInt32() == 6
                && extras.GetProperty("ShaderIndex").GetInt32() == 4;
            if (isStripePrimitive)
            {
                stripeGlowIndexCount += glowIndexCount;
                if (usesEmission)
                {
                    stripeEmissivePrimitiveCount++;
                }
            }
            else if (glowIndexCount > 0)
            {
                nonStripeGlowPrimitiveCount++;
            }
            else if (usesEmission)
            {
                nonStripeEmissivePrimitiveCount++;
            }
        }

        Expect(
            stripeGlowIndexCount > 0,
            $"{relativePath}: expected 09907 packet 6 tex_0004 white stripe primitive to carry glow RGBA indices");
        Expect(
            stripeEmissivePrimitiveCount > 0,
            $"{relativePath}: expected 09907 packet 6 tex_0004 white stripe primitive to use glow emission");
        Expect(
            nonStripeGlowPrimitiveCount == 0,
            $"{relativePath}: expected 09907 glow RGBA to stay limited to the packet 6 tex_0004 white stripe primitive");
        Expect(
            nonStripeEmissivePrimitiveCount == 0,
            $"{relativePath}: expected 09907 glow emission to stay limited to the packet 6 tex_0004 white stripe primitive");
    }
}

void ValidateRepeatedTextureBoundaries(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex)
{
    const int repeat = 10497;

    if (!root.TryGetProperty("materials", out var materials)
        || !root.TryGetProperty("textures", out var textures)
        || !root.TryGetProperty("samplers", out var samplers))
    {
        return;
    }

    var primitives = root.GetProperty("meshes")[0].GetProperty("primitives");
    var primitiveIndex = 0;
    foreach (var primitive in primitives.EnumerateArray())
    {
        var materialIndex = primitive.GetProperty("material").GetInt32();
        var material = materials[materialIndex];
        if (!material.TryGetProperty("pbrMetallicRoughness", out var pbr)
            || !pbr.TryGetProperty("baseColorTexture", out var baseColorTexture))
        {
            primitiveIndex++;
            continue;
        }

        var texture = textures[baseColorTexture.GetProperty("index").GetInt32()];
        var sampler = samplers[texture.GetProperty("sampler").GetInt32()];
        var repeatU = !sampler.TryGetProperty("wrapS", out var wrapS) || wrapS.GetInt32() == repeat;
        var repeatV = !sampler.TryGetProperty("wrapT", out var wrapT) || wrapT.GetInt32() == repeat;
        if (!repeatU && !repeatV)
        {
            primitiveIndex++;
            continue;
        }

        var texCoordAccessorIndex = primitive
            .GetProperty("attributes")
            .GetProperty("TEXCOORD_0")
            .GetInt32();
        var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = texCoords[indices[i]];
            var b = texCoords[indices[i + 1]];
            var c = texCoords[indices[i + 2]];
            var reducibleInteriorU = repeatU && HasReducibleInteriorIntegerBoundary(a.U, b.U, c.U);
            var reducibleInteriorV = repeatV && HasReducibleInteriorIntegerBoundary(a.V, b.V, c.V);

            Expect(
                !reducibleInteriorU && !reducibleInteriorV,
                $"{relativePath}: expected LOD{lodIndex} primitive {primitiveIndex} triangle {i / 3} not to leave fixable repeated texture boundaries inside the face");
        }

        primitiveIndex++;
    }
}

void ValidateTopFaceNormalOrientation(
    TieClass fixtureTie,
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorIndex,
    int normalAccessorIndex)
{
    if (!relativePath.Contains("09905_26B1", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var maxY = positions.Max(position => position.Y);
    var centerX = fixtureTie.Header.BoundingSphere.X;
    var centerZ = -fixtureTie.Header.BoundingSphere.Y;
    var checkedCount = 0;
    var downwardCount = 0;
    var flatBandCheckedCount = 0;
    var tiltedFlatBandNormalCount = 0;
    var normalIndicesByPosition = new Dictionary<(int X, int Y, int Z), List<int>>();

    for (var i = 0; i < Math.Min(positions.Count, normals.Count); i++)
    {
        var position = positions[i];
        var radius = MathF.Sqrt(
            (position.X - centerX) * (position.X - centerX)
            + (position.Z - centerZ) * (position.Z - centerZ));
        if (position.Y is >= 1.35f and <= 1.45f && radius <= 7f)
        {
            flatBandCheckedCount++;
            var horizontalNormalLength = MathF.Sqrt(normals[i].X * normals[i].X + normals[i].Z * normals[i].Z);
            if (normals[i].Y < 0.999f || horizontalNormalLength > 0.001f)
            {
                tiltedFlatBandNormalCount++;
            }
        }

        if (position.Y < maxY - 0.05f || radius > 16f)
        {
            continue;
        }

        checkedCount++;
        var key = QuantizedPositionKey(position);
        if (!normalIndicesByPosition.TryGetValue(key, out var normalIndices))
        {
            normalIndices = [];
            normalIndicesByPosition[key] = normalIndices;
        }

        normalIndices.Add(i);
        if (normals[i].Y < 0.5f)
        {
            downwardCount++;
        }
    }

    Expect(checkedCount > 0, $"{relativePath}: expected to find top face normals on 09905");
    Expect(downwardCount == 0, $"{relativePath}: expected 09905 top face normals to point outward/up, got {downwardCount} downward normal(s)");
    Expect(flatBandCheckedCount > 0, $"{relativePath}: expected to find 09905 flat red-band normals");
    Expect(tiltedFlatBandNormalCount == 0, $"{relativePath}: expected 09905 flat red-band normals to stay vertical, got {tiltedFlatBandNormalCount} tilted normal(s)");

    var indices = ReadExportedIndices(fixtureExport);
    var backfaceLitTriangleCount = 0;
    var checkedTriangleCount = 0;
    for (var i = 0; i + 2 < indices.Count; i += 3)
    {
        var aIndex = indices[i];
        var bIndex = indices[i + 1];
        var cIndex = indices[i + 2];
        if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
        {
            continue;
        }

        var averageNormal = Normalize((
            (normals[aIndex].X + normals[bIndex].X + normals[cIndex].X) / 3f,
            (normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y) / 3f,
            (normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z) / 3f));
        checkedTriangleCount++;
        if (NormalDot(faceNormal, averageNormal) < 0.5f)
        {
            backfaceLitTriangleCount++;
        }
    }

    Expect(checkedTriangleCount > 0, $"{relativePath}: expected to check 09905 triangle winding against exported normals");
    Expect(backfaceLitTriangleCount == 0, $"{relativePath}: expected 09905 triangle winding to agree with exported normals, got {backfaceLitTriangleCount} back-facing triangle(s)");

    var mismatchedDuplicateNormalCount = 0;
    foreach (var group in normalIndicesByPosition.Values.Where(group => group.Count > 1))
    {
        var firstNormal = normals[group[0]];
        for (var i = 1; i < group.Count; i++)
        {
            if (NormalDot(firstNormal, normals[group[i]]) < 0.98f)
            {
                mismatchedDuplicateNormalCount++;
            }
        }
    }

    Expect(mismatchedDuplicateNormalCount == 0, $"{relativePath}: expected duplicate 09905 top face positions to share normals, got {mismatchedDuplicateNormalCount} mismatch(es)");
}

void ValidateArchedRoofNormalContinuity(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorIndex,
    int normalAccessorIndex)
{
    if (!relativePath.Contains("08749_222D", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var topRoofNormalIndicesByPosition = new Dictionary<(int X, int Y, int Z), HashSet<int>>();

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var packetIndex = primitive.GetProperty("extras").GetProperty("PacketIndex").GetInt32();
        if (packetIndex is < 5 or > 8)
        {
            continue;
        }

        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        foreach (var index in indices)
        {
            var position = positions[index];
            if (position.Y < 13f)
            {
                continue;
            }

            var key = QuantizedPositionKey(position);
            if (!topRoofNormalIndicesByPosition.TryGetValue(key, out var normalIndices))
            {
                normalIndices = [];
                topRoofNormalIndicesByPosition[key] = normalIndices;
            }

            normalIndices.Add(index);
        }
    }

    var duplicateNormalPairCount = 0;
    var worstDuplicateNormalDot = 1f;
    foreach (var normalIndices in topRoofNormalIndicesByPosition.Values.Where(indices => indices.Count > 1))
    {
        var normalIndexList = normalIndices.ToArray();
        for (var i = 0; i < normalIndexList.Length; i++)
        {
            for (var j = i + 1; j < normalIndexList.Length; j++)
            {
                var dot = NormalDot(normals[normalIndexList[i]], normals[normalIndexList[j]]);
                if (dot < 0.8f)
                {
                    continue;
                }

                duplicateNormalPairCount++;
                worstDuplicateNormalDot = MathF.Min(worstDuplicateNormalDot, dot);
            }
        }
    }

    Expect(duplicateNormalPairCount > 0, $"{relativePath}: expected to find compatible duplicate 08749 roof normals");
    Expect(worstDuplicateNormalDot >= 0.995f, $"{relativePath}: expected compatible duplicate 08749 roof normals to be smoothed, got worst dot {worstDuplicateNormalDot}");
}

void ValidateTallOrganicNormalContinuity(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorIndex,
    int normalAccessorIndex)
{
    if (!relativePath.Contains("08314_207A", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var normalIndicesByPosition = new Dictionary<(int X, int Y, int Z), HashSet<int>>();

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        foreach (var index in indices)
        {
            var key = QuantizedPositionKey(positions[index]);
            if (!normalIndicesByPosition.TryGetValue(key, out var normalIndices))
            {
                normalIndices = [];
                normalIndicesByPosition[key] = normalIndices;
            }

            normalIndices.Add(index);
        }
    }

    var duplicateNormalPairCount = 0;
    var worstDuplicateNormalDot = 1f;
    foreach (var normalIndices in normalIndicesByPosition.Values.Where(indices => indices.Count > 1))
    {
        var normalIndexList = normalIndices.ToArray();
        for (var i = 0; i < normalIndexList.Length; i++)
        {
            for (var j = i + 1; j < normalIndexList.Length; j++)
            {
                duplicateNormalPairCount++;
                worstDuplicateNormalDot = MathF.Min(
                    worstDuplicateNormalDot,
                    NormalDot(normals[normalIndexList[i]], normals[normalIndexList[j]]));
            }
        }
    }

    Expect(duplicateNormalPairCount > 0, $"{relativePath}: expected to find duplicate 8314 organic surface normals");
    Expect(
        worstDuplicateNormalDot >= 0.995f,
        $"{relativePath}: expected duplicate 8314 organic surface positions to share FBX-style welded normals, got worst dot {worstDuplicateNormalDot}");
}

void ValidateArchedRoofTextureCoordinates(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex)
{
    if (!relativePath.Contains("08749_222D", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var checkedTriangleCount = 0;
    var maxUspan = 0f;
    var maxVspan = 0f;
    var matchedReferenceShader2Start = false;
    var matchedReferenceShader3Start = false;
    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        var packetIndex = extras.GetProperty("PacketIndex").GetInt32();
        if (packetIndex is < 5 or > 8)
        {
            continue;
        }

        var texCoordAccessorIndex = primitive
            .GetProperty("attributes")
            .GetProperty("TEXCOORD_0")
            .GetInt32();
        var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = texCoords[indices[i]];
            var b = texCoords[indices[i + 1]];
            var c = texCoords[indices[i + 2]];
            maxUspan = MathF.Max(maxUspan, Range(a.U, b.U, c.U));
            maxVspan = MathF.Max(maxVspan, Range(a.V, b.V, c.V));
            checkedTriangleCount++;
        }

        if (packetIndex == 5
            && extras.GetProperty("ShaderIndex").GetInt32() == 2
            && indices.Count >= 3)
        {
            var a = texCoords[indices[0]];
            var b = texCoords[indices[1]];
            var c = texCoords[indices[2]];
            matchedReferenceShader2Start =
                TexCoordsNearlyEqualModulo(a, -2f, 0.5f)
                && TexCoordsNearlyEqualModulo(b, -2f, 0.98999023f)
                && TexCoordsNearlyEqualModulo(c, -1.5f, 0.5f);
        }

        if (packetIndex == 6
            && extras.GetProperty("ShaderIndex").GetInt32() == 3
            && indices.Count >= 3)
        {
            var a = texCoords[indices[0]];
            var b = texCoords[indices[1]];
            var c = texCoords[indices[2]];
            matchedReferenceShader3Start =
                TexCoordsNearlyEqualModulo(a, -2f, 0.5f)
                && TexCoordsNearlyEqualModulo(b, -2f, 1f)
                && TexCoordsNearlyEqualModulo(c, -1.5f, 0.5f);
        }
    }

    Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find 08749 arched-roof textured triangles");
    Expect(maxUspan <= 0.51f, $"{relativePath}: expected 08749 arched-roof U spans to stay within reference half-tile strips, got {maxUspan}");
    Expect(maxVspan <= 0.51f, $"{relativePath}: expected 08749 arched-roof V spans to stay within reference half-tile strips, got {maxVspan}");
    Expect(matchedReferenceShader2Start, $"{relativePath}: expected 08749 arched-roof shader 2 UVs to match the FBX reference layout");
    Expect(matchedReferenceShader3Start, $"{relativePath}: expected 08749 arched-roof shader 3 UVs to match the FBX reference layout");
}

void ValidateIndentedRingNormalOrientation(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorIndex,
    int normalAccessorIndex)
{
    if (!relativePath.Contains("08799_225F", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var checkedTriangleCount = 0;
    var worstNormalDot = 1f;

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var packetIndex = primitive.GetProperty("extras").GetProperty("PacketIndex").GetInt32();
        if (packetIndex is < 4 or > 6)
        {
            continue;
        }

        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            var centerY = (positions[aIndex].Y + positions[bIndex].Y + positions[cIndex].Y) / 3f;
            if (centerY is < 18f or > 19f
                || !TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal)
                || faceNormal.Y > -0.7f)
            {
                continue;
            }

            var averageNormal = Normalize((
                (normals[aIndex].X + normals[bIndex].X + normals[cIndex].X) / 3f,
                (normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y) / 3f,
                (normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z) / 3f));
            checkedTriangleCount++;
            worstNormalDot = MathF.Min(worstNormalDot, NormalDot(faceNormal, averageNormal));
        }
    }

    Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find 08799 indented-ring bevel triangles");
    Expect(worstNormalDot >= 0.93f, $"{relativePath}: expected 08799 indented-ring bevel normals to follow the surface, got worst dot {worstNormalDot}");
}

void ValidateIndentedRingSideWallNormalOrientation(
    TieGltfExport fixtureExport,
    JsonElement root,
    string relativePath,
    int lodIndex,
    int positionAccessorIndex,
    int normalAccessorIndex)
{
    if (!relativePath.Contains("08799_225F", StringComparison.Ordinal) || lodIndex != 0)
    {
        return;
    }

    var positions = ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
    var normals = ReadExportedVec3Accessor(fixtureExport, root, normalAccessorIndex);
    var checkedTriangleCount = 0;
    var worstNormalDot = 1f;
    var worstNormalY = 0f;
    var sideWallNormalIndicesByPosition = new Dictionary<(int X, int Y, int Z), HashSet<int>>();

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var packetIndex = primitive.GetProperty("extras").GetProperty("PacketIndex").GetInt32();
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        foreach (var index in indices)
        {
            var position = positions[index];
            var normal = normals[index];
            if (position.Y is < 14f or > 26f || MathF.Abs(normal.Y) > 0.2f)
            {
                continue;
            }

            var key = QuantizedPositionKey(position);
            if (!sideWallNormalIndicesByPosition.TryGetValue(key, out var normalIndices))
            {
                normalIndices = [];
                sideWallNormalIndicesByPosition[key] = normalIndices;
            }

            normalIndices.Add(index);
        }

        if (packetIndex is not (4 or 5))
        {
            continue;
        }

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            var centerY = (positions[aIndex].Y + positions[bIndex].Y + positions[cIndex].Y) / 3f;
            if (centerY is < 17f or > 18f
                || !TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal)
                || MathF.Abs(faceNormal.Y) > 0.05f)
            {
                continue;
            }

            var averageNormal = Normalize((
                (normals[aIndex].X + normals[bIndex].X + normals[cIndex].X) / 3f,
                (normals[aIndex].Y + normals[bIndex].Y + normals[cIndex].Y) / 3f,
                (normals[aIndex].Z + normals[bIndex].Z + normals[cIndex].Z) / 3f));
            checkedTriangleCount++;
            worstNormalDot = MathF.Min(worstNormalDot, NormalDot(faceNormal, averageNormal));
            worstNormalY = MathF.Max(worstNormalY, MathF.Abs(averageNormal.Y));
        }
    }

    Expect(checkedTriangleCount > 0, $"{relativePath}: expected to find 08799 indented-ring side-wall triangles");
    Expect(worstNormalDot >= 0.98f, $"{relativePath}: expected 08799 indented-ring side-wall normals to avoid bevel bleed, got worst dot {worstNormalDot}");
    Expect(worstNormalY <= 0.15f, $"{relativePath}: expected 08799 indented-ring side-wall normals to stay horizontal, got Y component {worstNormalY}");

    var compatibleDuplicateNormalPairCount = 0;
    var worstCompatibleDuplicateNormalDot = 1f;
    foreach (var normalIndices in sideWallNormalIndicesByPosition.Values.Where(indices => indices.Count > 1))
    {
        var normalIndexList = normalIndices.ToArray();
        for (var i = 0; i < normalIndexList.Length; i++)
        {
            for (var j = i + 1; j < normalIndexList.Length; j++)
            {
                var dot = NormalDot(normals[normalIndexList[i]], normals[normalIndexList[j]]);
                if (dot < 0.95f)
                {
                    continue;
                }

                compatibleDuplicateNormalPairCount++;
                worstCompatibleDuplicateNormalDot = MathF.Min(worstCompatibleDuplicateNormalDot, dot);
            }
        }
    }

    Expect(compatibleDuplicateNormalPairCount > 0, $"{relativePath}: expected to find compatible duplicate 08799 side-wall normals");
    Expect(worstCompatibleDuplicateNormalDot >= 0.995f, $"{relativePath}: expected compatible duplicate 08799 side-wall normals to be smoothed, got worst dot {worstCompatibleDuplicateNormalDot}");
}

void ValidateAttributeAddressResolution(TieClass fixtureTie, TieGltfExport fixtureExport, string relativePath)
{
    if (!relativePath.Contains("09905_26B1", StringComparison.Ordinal))
    {
        return;
    }

    if (fixtureTie.LodTopologies[0].LogicalVertices.All(vertex => vertex.DecodedVertex is not null))
    {
        foreach (var logicalVertexIndex in new[] { 129, 159, 232 })
        {
            var vertex = fixtureTie.LodTopologies[0].LogicalVertices[logicalVertexIndex];
            var decodedVertex = vertex.DecodedVertex!;
            Expect(
                PositionsNearlyEqual(
                    ReadExportedPosition(fixtureExport, logicalVertexIndex),
                    ToGltfPosition(fixtureTie, decodedVertex.X, decodedVertex.Y, decodedVertex.Z)),
                $"{relativePath}: expected logical vertex {logicalVertexIndex} to export from decoded packet vertex data");
        }

        var decodedMaxY = Enumerable.Range(0, fixtureTie.Header.Lods[0].VertexCount)
            .Select(index => ReadExportedPosition(fixtureExport, index).Y)
            .Max();
        Expect(decodedMaxY < 1.7f, $"{relativePath}: expected 9905 exported high LOD max Y below 1.7 after packet unpacking, got {decodedMaxY}");

        var decodedTexCoords = ReadExportedIndexedTexCoords(fixtureExport);
        Expect(
            TexCoordsNearlyEqualModulo(decodedTexCoords[0], 0.5f, TileEdgeBias(64)),
            $"{relativePath}: expected first exported UV to match reference ST scale and V orientation with outer edge bias");
        Expect(
            TexCoordsNearlyEqualModulo(decodedTexCoords[2], 0.89990234f, TileEdgeBias(64)),
            $"{relativePath}: expected third exported UV to match reference ST scale and V orientation with outer edge bias");
        Validate09905OuterTextureEdgeBias(fixtureExport, relativePath);
        return;
    }

    var attributeAddressVertex = fixtureTie.LodTopologies[0].LogicalVertices.FirstOrDefault(vertex =>
        vertex.AddressRow is not null
        && vertex.VertexRow is not null
        && vertex.AddressRowIndex != vertex.VertexRowIndex
        && vertex.AddressRow.Z == 4096
        && Math.Abs(vertex.AddressRow.X) <= 4096
        && Math.Abs(vertex.AddressRow.Y) <= 4096);
    Expect(attributeAddressVertex is not null, $"{relativePath}: expected at least one logical vertex to resolve past an attribute qword");
    if (attributeAddressVertex is null)
    {
        return;
    }

    var addressRow = attributeAddressVertex.AddressRow!;
    var vertexRow = attributeAddressVertex.VertexRow!;
    var exportedPosition = ReadExportedPosition(fixtureExport, attributeAddressVertex.LogicalVertexIndex);
    var expectedPosition = ToGltfPosition(fixtureTie, vertexRow.X, vertexRow.Y, vertexRow.Z);
    var attributePosition = ToGltfPosition(fixtureTie, addressRow.X, addressRow.Y, addressRow.Z);

    Expect(
        PositionsNearlyEqual(exportedPosition, expectedPosition),
        $"{relativePath}: expected logical vertex {attributeAddressVertex.LogicalVertexIndex} to export from resolved coordinate row {vertexRow.Index}");
    Expect(
        !PositionsNearlyEqual(exportedPosition, attributePosition),
        $"{relativePath}: expected logical vertex {attributeAddressVertex.LogicalVertexIndex} not to export the addressed attribute qword");

    var normalSlotVertex = fixtureTie.LodTopologies[0].LogicalVertices.FirstOrDefault(vertex =>
        vertex.VertexRow is not null
        && IsNormalLengthVector(fixtureTie, vertex.VertexRow.X, vertex.VertexRow.Y, vertex.VertexRow.Z)
        && !IsAttributeVector(vertex.VertexRow.Data0, vertex.VertexRow.Data1, vertex.VertexRow.Data2)
        && VectorLength(fixtureTie, vertex.VertexRow.Data0, vertex.VertexRow.Data1, vertex.VertexRow.Data2) > 5f);
    Expect(normalSlotVertex is not null, $"{relativePath}: expected at least one logical vertex with first-slot normal data and second-slot position data");
    if (normalSlotVertex is null)
    {
        return;
    }

    var normalSlotRow = normalSlotVertex.VertexRow!;
    var normalSlotExportedPosition = ReadExportedPosition(fixtureExport, normalSlotVertex.LogicalVertexIndex);
    var normalSlotExpectedPosition = ToGltfPosition(fixtureTie, normalSlotRow.Data0, normalSlotRow.Data1, normalSlotRow.Data2);
    var normalSlotFirstVector = ToGltfPosition(fixtureTie, normalSlotRow.X, normalSlotRow.Y, normalSlotRow.Z);

    Expect(
        PositionsNearlyEqual(normalSlotExportedPosition, normalSlotExpectedPosition),
        $"{relativePath}: expected logical vertex {normalSlotVertex.LogicalVertexIndex} to export from the second-slot position vector");
    Expect(
        !PositionsNearlyEqual(normalSlotExportedPosition, normalSlotFirstVector),
        $"{relativePath}: expected logical vertex {normalSlotVertex.LogicalVertexIndex} not to export the first-slot normal vector");

    var secondaryMarkerVertex = fixtureTie.LodTopologies[0].LogicalVertices[129];
    Expect(secondaryMarkerVertex.AddressRowIndex == 34, $"{relativePath}: expected logical vertex 129 address row 34, got {secondaryMarkerVertex.AddressRowIndex}");
    Expect(secondaryMarkerVertex.VertexRowIndex == 35, $"{relativePath}: expected logical vertex 129 to resolve marker Data3 address to following row 35, got {secondaryMarkerVertex.VertexRowIndex}");
    Expect(
        PositionsNearlyEqual(
            ReadExportedPosition(fixtureExport, 129),
            ToGltfPosition(fixtureTie, secondaryMarkerVertex.VertexRow!.X, secondaryMarkerVertex.VertexRow.Y, secondaryMarkerVertex.VertexRow.Z)),
        $"{relativePath}: expected logical vertex 129 to export the following marker coordinate row");

    var primaryMarkerVertex = fixtureTie.LodTopologies[0].LogicalVertices[159];
    Expect(primaryMarkerVertex.AddressRowIndex == 49, $"{relativePath}: expected logical vertex 159 address row 49, got {primaryMarkerVertex.AddressRowIndex}");
    Expect(primaryMarkerVertex.VertexRowIndex == 48, $"{relativePath}: expected logical vertex 159 to resolve marker W address to previous row 48, got {primaryMarkerVertex.VertexRowIndex}");
    Expect(
        PositionsNearlyEqual(
            ReadExportedPosition(fixtureExport, 159),
            ToGltfPosition(fixtureTie, primaryMarkerVertex.VertexRow!.Data0, primaryMarkerVertex.VertexRow.Data1, primaryMarkerVertex.VertexRow.Data2)),
        $"{relativePath}: expected logical vertex 159 to export the previous marker coordinate row second slot");

    var smallDirectionVertex = fixtureTie.LodTopologies[0].LogicalVertices[232];
    Expect(
        PositionsNearlyEqual(
            ReadExportedPosition(fixtureExport, 232),
            ToGltfPosition(fixtureTie, smallDirectionVertex.VertexRow!.Data0, smallDirectionVertex.VertexRow.Data1, smallDirectionVertex.VertexRow.Data2)),
        $"{relativePath}: expected logical vertex 232 to export from second-slot position data instead of first-slot direction data");

    var maxY = Enumerable.Range(0, fixtureTie.Header.Lods[0].VertexCount)
        .Select(index => ReadExportedPosition(fixtureExport, index).Y)
        .Max();
    Expect(maxY < 1.7f, $"{relativePath}: expected 9905 exported high LOD max Y below 1.7 after marker resolution, got {maxY}");

    var texCoords = ReadExportedIndexedTexCoords(fixtureExport);
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[0], 0.5f, TileEdgeBias(64)),
        $"{relativePath}: expected first exported UV to match reference ST scale and V orientation with outer edge bias");
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[2], 0.89990234f, TileEdgeBias(64)),
        $"{relativePath}: expected third exported UV to match reference ST scale and V orientation with outer edge bias");
    Validate09905OuterTextureEdgeBias(fixtureExport, relativePath);

}

void Validate09905OuterTextureEdgeBias(TieGltfExport fixtureExport, string relativePath)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var exactOuterVCount = 0;
    var nearOuterVCount = 0;

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var texCoordAccessorIndex = primitive
            .GetProperty("attributes")
            .GetProperty("TEXCOORD_0")
            .GetInt32();
        var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        foreach (var index in indices)
        {
            var v = texCoords[index].V;
            if (v < 0.02f || v > 0.98f)
            {
                nearOuterVCount++;
            }

            if (MathF.Abs(v) < 0.000001f || MathF.Abs(v - 1f) < 0.000001f)
            {
                exactOuterVCount++;
            }
        }
    }

    Expect(nearOuterVCount > 0, $"{relativePath}: expected to find 09905 outer texture V edge coordinates");
    Expect(exactOuterVCount == 0, $"{relativePath}: expected 09905 outer texture V edges to be biased inward, got {exactOuterVCount} exact edge coordinate(s)");
}

void ValidateTallCoordinateResolution(TieClass fixtureTie, TieGltfExport fixtureExport, string relativePath)
{
    if (!relativePath.Contains("08314_207A", StringComparison.Ordinal))
    {
        return;
    }

    var tinyDirectionVertex = fixtureTie.LodTopologies[0].LogicalVertices[244];
    var tinyDirectionRow = tinyDirectionVertex.VertexRow!;
    Expect(tinyDirectionVertex.VertexRowIndex == 23, $"{relativePath}: expected logical vertex 244 to resolve to vertex row 23, got {tinyDirectionVertex.VertexRowIndex}");
    Expect(
        PositionsNearlyEqual(
            ReadExportedPosition(fixtureExport, 244),
            ToGltfPosition(fixtureTie, tinyDirectionRow.Data0, tinyDirectionRow.Data1, tinyDirectionRow.Data2)),
        $"{relativePath}: expected logical vertex 244 to export tall second-slot position data");

    var positions = ReadExportedPositions(fixtureExport);
    var maxY = positions.Max(position => position.Y);
    Expect(maxY > 51f, $"{relativePath}: expected 8314 exported high LOD to preserve tall peak vertices, got max Y {maxY}");

    var indices = ReadExportedIndices(fixtureExport);
    var maxEdgeLength = 0f;
    for (var i = 0; i + 2 < indices.Count; i += 3)
    {
        var a = positions[indices[i]];
        var b = positions[indices[i + 1]];
        var c = positions[indices[i + 2]];
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(a, b));
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(b, c));
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(c, a));
    }

    Expect(maxEdgeLength < 9.1f, $"{relativePath}: expected 8314 max triangle edge below 9.1 after tall-coordinate decoding, got {maxEdgeLength}");

    var texCoords = ReadExportedIndexedTexCoords(fixtureExport);
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[2], 0.5f, 0.5f),
        $"{relativePath}: expected logical vertex 2 UV to be decoded from the adjacent texture row");
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[6], 0.5f, 0.5f),
        $"{relativePath}: expected logical vertex 6 UV to be decoded from the adjacent texture row");
}

void ValidateSecondSlotAddressResolution(TieClass fixtureTie, TieGltfExport fixtureExport, string relativePath)
{
    if (!relativePath.Contains("09907_26B3", StringComparison.Ordinal))
    {
        return;
    }

    var secondSlotAddressVertex = fixtureTie.LodTopologies[0].LogicalVertices[136];
    var secondSlotRow = secondSlotAddressVertex.VertexRow!;
    Expect(secondSlotAddressVertex.AddressRowIndex == 59, $"{relativePath}: expected logical vertex 136 address row 59, got {secondSlotAddressVertex.AddressRowIndex}");
    Expect(secondSlotAddressVertex.VertexRowIndex == 59, $"{relativePath}: expected logical vertex 136 to resolve to its own second-slot row, got {secondSlotAddressVertex.VertexRowIndex}");
    Expect(
        PositionsNearlyEqual(
            ReadExportedPosition(fixtureExport, 136),
            ToGltfPosition(fixtureTie, secondSlotRow.Data0, secondSlotRow.Data1, secondSlotRow.Data2)),
        $"{relativePath}: expected logical vertex 136 to export from second-slot position data");

    var positions = ReadExportedPositions(fixtureExport);
    var indices = ReadExportedIndices(fixtureExport);
    var maxEdgeLength = 0f;
    for (var i = 0; i + 2 < indices.Count; i += 3)
    {
        var a = positions[indices[i]];
        var b = positions[indices[i + 1]];
        var c = positions[indices[i + 2]];
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(a, b));
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(b, c));
        maxEdgeLength = MathF.Max(maxEdgeLength, Distance(c, a));
    }

    Expect(maxEdgeLength < 14.7f, $"{relativePath}: expected 9907 max triangle edge below 14.7 after second-slot address resolution, got {maxEdgeLength}");

    var texCoords = ReadExportedIndexedTexCoords(fixtureExport);
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[0], TileEdgeBias(64), TileEdgeBias(64)),
        $"{relativePath}: expected first exported UV to use 1/4096 ST scale with edge texel bias");
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[1], TileEdgeBias(64), 1f - TileEdgeBias(64)),
        $"{relativePath}: expected second exported UV to use 1/4096 ST scale with edge texel bias");
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[2], 1f - TileEdgeBias(64), TileEdgeBias(64)),
        $"{relativePath}: expected third exported UV to use 1/4096 ST scale with edge texel bias");
    Expect(
        TexCoordsNearlyEqualModulo(texCoords[5], 1f - TileEdgeBias(64), 1f - TileEdgeBias(64)),
        $"{relativePath}: expected sixth exported UV to use 1/4096 ST scale with edge texel bias");

    ValidateSlantedPostTextureTiling(fixtureExport, relativePath);
}

void ValidateSlantedPostTextureTiling(TieGltfExport fixtureExport, string relativePath)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        if (extras.GetProperty("PacketIndex").GetInt32() != 3
            || extras.GetProperty("ShaderIndex").GetInt32() != 3)
        {
            continue;
        }

        var texCoordAccessorIndex = primitive
            .GetProperty("attributes")
            .GetProperty("TEXCOORD_0")
            .GetInt32();
        var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        var narrowTriangleCount = 0;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = texCoords[indices[i]];
            var b = texCoords[indices[i + 1]];
            var c = texCoords[indices[i + 2]];
            var uSpan = Range(a.U, b.U, c.U);
            if (uSpan < 2.5f)
            {
                narrowTriangleCount++;
            }
        }

        Expect(
            narrowTriangleCount == 0,
            $"{relativePath}: expected 9907 post texture U coordinates to preserve multi-tile spans");
        return;
    }

    Expect(false, $"{relativePath}: expected to find 9907 post primitive packet 3 shader 3");
}

void ValidateShaderSwitchMaterialResolution(
    TieGltfExport fixtureExport,
    string relativePath,
    int lodIndex,
    bool expectTexturedMaterials)
{
    if (lodIndex != 0)
    {
        return;
    }

    var primitiveSummaries = ReadPrimitiveSummaries(fixtureExport);
    var materialNamePrefix = expectTexturedMaterials ? "tex_" : null;

    if (relativePath.Contains("08314_207A", StringComparison.Ordinal))
    {
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 13, 0, 40, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 13, 1, 16, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 17, 1, 8, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 18, 1, 8, materialNamePrefix);
    }
    else if (relativePath.Contains("09905_26B1", StringComparison.Ordinal))
    {
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 3, 1, 32, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 3, 0, 8, materialNamePrefix);
    }
    else if (relativePath.Contains("09907_26B3", StringComparison.Ordinal))
    {
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 3, 0, 4, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 3, 2, 8, materialNamePrefix);
        ExpectPrimitiveTriangleCount(primitiveSummaries, relativePath, 3, 3, 14, materialNamePrefix);
    }
}

void ValidateTextureSamplerWrapModes(JsonElement root, string relativePath, int lodIndex)
{
    if (lodIndex != 0)
    {
        return;
    }

    if (relativePath.Contains("08314_207A", StringComparison.Ordinal))
    {
        ExpectMaterialWrapMode(root, relativePath, "tex_0000", repeatU: true, repeatV: true);
        ExpectMaterialWrapMode(root, relativePath, "tex_0001", repeatU: true, repeatV: true);
    }
    else if (relativePath.Contains("09905_26B1", StringComparison.Ordinal))
    {
        ExpectMaterialWrapMode(root, relativePath, "tex_0000", repeatU: true, repeatV: false);
        ExpectMaterialWrapMode(root, relativePath, "tex_0001", repeatU: false, repeatV: false);
    }
    else if (relativePath.Contains("09907_26B3", StringComparison.Ordinal))
    {
        ExpectMaterialWrapMode(root, relativePath, "tex_0000", repeatU: true, repeatV: true);
        ExpectMaterialWrapMode(root, relativePath, "tex_0002", repeatU: false, repeatV: false);
        ExpectMaterialWrapMode(root, relativePath, "tex_0003", repeatU: true, repeatV: false);
        ExpectMaterialWrapMode(root, relativePath, "tex_0004", repeatU: true, repeatV: false);
    }
}

void ExpectMaterialWrapMode(
    JsonElement root,
    string relativePath,
    string materialName,
    bool repeatU,
    bool repeatV)
{
    const int repeat = 10497;
    const int clampToEdge = 33071;
    const int linear = 9729;

    var materials = root.GetProperty("materials");
    JsonElement? material = null;
    foreach (var candidate in materials.EnumerateArray())
    {
        if (candidate.TryGetProperty("name", out var nameElement)
            && nameElement.GetString() == materialName)
        {
            material = candidate;
            break;
        }
    }

    Expect(material is not null, $"{relativePath}: expected material {materialName}");
    var textureIndex = material!.Value
        .GetProperty("pbrMetallicRoughness")
        .GetProperty("baseColorTexture")
        .GetProperty("index")
        .GetInt32();
    var texture = root.GetProperty("textures")[textureIndex];
    var samplerIndex = texture.GetProperty("sampler").GetInt32();
    var sampler = root.GetProperty("samplers")[samplerIndex];
    var expectedWrapS = repeatU ? repeat : clampToEdge;
    var expectedWrapT = repeatV ? repeat : clampToEdge;

    Expect(
        sampler.GetProperty("wrapS").GetInt32() == expectedWrapS,
        $"{relativePath}: expected {materialName} wrapS {expectedWrapS}, got {sampler.GetProperty("wrapS").GetInt32()}");
    Expect(
        sampler.GetProperty("wrapT").GetInt32() == expectedWrapT,
        $"{relativePath}: expected {materialName} wrapT {expectedWrapT}, got {sampler.GetProperty("wrapT").GetInt32()}");
    Expect(
        sampler.GetProperty("minFilter").GetInt32() == linear,
        $"{relativePath}: expected {materialName} minFilter {linear}, got {sampler.GetProperty("minFilter").GetInt32()}");
    Expect(
        sampler.GetProperty("magFilter").GetInt32() == linear,
        $"{relativePath}: expected {materialName} magFilter {linear}, got {sampler.GetProperty("magFilter").GetInt32()}");
}

void ExpectPrimitiveTriangleCount(
    IReadOnlyList<(int PacketIndex, int ShaderIndex, int TriangleCount, string? MaterialName)> primitiveSummaries,
    string relativePath,
    int packetIndex,
    int shaderIndex,
    int expectedTriangleCount,
    string? expectedMaterialNamePrefix)
{
    var matches = primitiveSummaries
        .Where(summary => summary.PacketIndex == packetIndex && summary.ShaderIndex == shaderIndex)
        .ToArray();
    var triangleCount = matches.Sum(summary => summary.TriangleCount);
    Expect(
        triangleCount == expectedTriangleCount,
        $"{relativePath}: expected packet {packetIndex} shader {shaderIndex} to export {expectedTriangleCount} triangles, got {triangleCount}");

    if (expectedMaterialNamePrefix is not null)
    {
        var expectedMaterialName = $"{expectedMaterialNamePrefix}{shaderIndex:0000}";
        Expect(
            matches.Length > 0 && matches.All(summary => summary.MaterialName == expectedMaterialName),
            $"{relativePath}: expected packet {packetIndex} shader {shaderIndex} primitives to use material {expectedMaterialName}");
    }
}

static int CountExpectedGltfPrimitiveGroups(TieClass tie, int lodIndex)
{
    var topology = tie.LodTopologies[lodIndex];
    var packetsByIndex = tie.PacketTables
        .FirstOrDefault(table => table.LodIndex == lodIndex)?
        .Packets
        .ToDictionary(packet => packet.PacketIndex) ?? [];
    var primitiveCount = 0;
    int? currentPacketIndex = null;
    int? currentShaderIndex = null;
    bool? currentUsesGlowEmission = null;
    var glowLogicalVertexIndices = new HashSet<int>(tie.GlowRgbaVertices
        .Where(vertex => vertex.LodIndex == lodIndex)
        .Select(vertex => vertex.LogicalVertexIndex));

    foreach (var triangle in topology.Triangles)
    {
        var strip = topology.Strips[triangle.StripIndex];
        packetsByIndex.TryGetValue(strip.PacketIndex, out var packet);
        var shaderIndex = SelectExpectedShaderIndex(packet, strip);
        var usesGlowEmission = glowLogicalVertexIndices.Contains(triangle.A)
            || glowLogicalVertexIndices.Contains(triangle.B)
            || glowLogicalVertexIndices.Contains(triangle.C);
        if (currentPacketIndex != strip.PacketIndex
            || currentShaderIndex != shaderIndex
            || currentUsesGlowEmission != usesGlowEmission)
        {
            primitiveCount++;
            currentPacketIndex = strip.PacketIndex;
            currentShaderIndex = shaderIndex;
            currentUsesGlowEmission = usesGlowEmission;
        }
    }

    return primitiveCount;
}

static int SelectExpectedShaderIndex(TiePacket? packet, TieTriangleStrip strip)
{
    if (packet is null || packet.ShaderReferences.Count == 0)
    {
        return -1;
    }

    var shaderReferenceIndex = 0;
    var switchCount = Math.Min(packet.ShaderSwitchVuAddresses.Count, packet.ShaderReferences.Count - 1);
    for (var i = 0; i < switchCount; i++)
    {
        if (packet.ShaderSwitchVuAddresses[i] > 0 && strip.VuAddress >= packet.ShaderSwitchVuAddresses[i])
        {
            shaderReferenceIndex = i + 1;
        }
    }

    return packet.ShaderReferences[shaderReferenceIndex].ShaderIndex;
}

static List<(int PacketIndex, int ShaderIndex, int TriangleCount, string? MaterialName)> ReadPrimitiveSummaries(
    TieGltfExport fixtureExport)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var accessors = root.GetProperty("accessors");
    var materials = root.GetProperty("materials");
    var summaries = new List<(int PacketIndex, int ShaderIndex, int TriangleCount, string? MaterialName)>();

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        var indexAccessorIndex = primitive.GetProperty("indices").GetInt32();
        var materialIndex = primitive.GetProperty("material").GetInt32();
        var material = materials[materialIndex];
        summaries.Add((
            extras.GetProperty("PacketIndex").GetInt32(),
            extras.GetProperty("ShaderIndex").GetInt32(),
            accessors[indexAccessorIndex].GetProperty("count").GetInt32() / 3,
            material.TryGetProperty("name", out var name) ? name.GetString() : null));
    }

    return summaries;
}

static float? ReadPrimitiveMinimumNormalFaceDot(TieGltfExport fixtureExport, int packetIndex, int shaderIndex)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        if (extras.GetProperty("PacketIndex").GetInt32() != packetIndex
            || extras.GetProperty("ShaderIndex").GetInt32() != shaderIndex)
        {
            continue;
        }

        var attributes = primitive.GetProperty("attributes");
        var positions = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("POSITION").GetInt32());
        var normals = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("NORMAL").GetInt32());
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        var minimumDot = 1f;
        var triangleCount = 0;
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            if (!TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
            {
                continue;
            }

            minimumDot = MathF.Min(
                minimumDot,
                MathF.Min(
                    NormalDot(Normalize(normals[aIndex]), faceNormal),
                    MathF.Min(
                        NormalDot(Normalize(normals[bIndex]), faceNormal),
                        NormalDot(Normalize(normals[cIndex]), faceNormal))));
            triangleCount++;
        }

        return triangleCount > 0 ? minimumDot : null;
    }

    return null;
}

static float? ReadMinimumDuplicatePositionNormalDot(TieGltfExport fixtureExport, int shaderIndex)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var firstPrimitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var attributes = firstPrimitive.GetProperty("attributes");
    var positions = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("POSITION").GetInt32());
    var normals = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("NORMAL").GetInt32());
    var firstByPosition = new Dictionary<(float X, float Y, float Z), (int Index, (float X, float Y, float Z) Normal)>();
    var minimumDot = 1f;
    var duplicateCount = 0;

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        if (primitive.GetProperty("extras").GetProperty("ShaderIndex").GetInt32() != shaderIndex)
        {
            continue;
        }

        foreach (var index in ReadExportedPrimitiveIndices(fixtureExport, root, primitive).Distinct())
        {
            var position = positions[index];
            if (!firstByPosition.TryGetValue(position, out var first))
            {
                firstByPosition[position] = (index, normals[index]);
                continue;
            }

            if (first.Index != index)
            {
                minimumDot = MathF.Min(
                    minimumDot,
                    NormalDot(Normalize(first.Normal), Normalize(normals[index])));
                duplicateCount++;
            }
        }
    }

    return duplicateCount > 0 ? minimumDot : null;
}

static int ReadPrimitiveCopiedNormalMismatchCount(TieGltfExport fixtureExport, int packetIndex, int shaderIndex)
{
    const float copiedNormalMinimumDot = 0.999f;
    const float copiedNormalMaximumAverageFaceDot = 0.93f;

    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var mismatchCount = 0;
    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var extras = primitive.GetProperty("extras");
        if (extras.GetProperty("PacketIndex").GetInt32() != packetIndex
            || extras.GetProperty("ShaderIndex").GetInt32() != shaderIndex)
        {
            continue;
        }

        var attributes = primitive.GetProperty("attributes");
        var positions = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("POSITION").GetInt32());
        var normals = ReadExportedVec3Accessor(fixtureExport, root, attributes.GetProperty("NORMAL").GetInt32());
        var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            var aNormal = Normalize(normals[aIndex]);
            var bNormal = Normalize(normals[bIndex]);
            var cNormal = Normalize(normals[cIndex]);
            if (NormalDot(aNormal, bNormal) < copiedNormalMinimumDot
                || NormalDot(aNormal, cNormal) < copiedNormalMinimumDot
                || NormalDot(bNormal, cNormal) < copiedNormalMinimumDot
                || !TryFaceNormal(positions[aIndex], positions[bIndex], positions[cIndex], out var faceNormal))
            {
                continue;
            }

            var averageFaceDot = (
                NormalDot(aNormal, faceNormal)
                + NormalDot(bNormal, faceNormal)
                + NormalDot(cNormal, faceNormal)) / 3f;
            if (averageFaceDot < copiedNormalMaximumAverageFaceDot)
            {
                mismatchCount++;
            }
        }
    }

    return mismatchCount;
}

static (float X, float Y, float Z) ReadExportedPosition(TieGltfExport fixtureExport, int logicalVertexIndex)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var positionAccessorIndex = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
    var positionAccessor = root.GetProperty("accessors")[positionAccessorIndex];
    var positionBufferView = root.GetProperty("bufferViews")[positionAccessor.GetProperty("bufferView").GetInt32()];
    var accessorByteOffset = positionAccessor.TryGetProperty("byteOffset", out var accessorOffsetElement)
        ? accessorOffsetElement.GetInt32()
        : 0;
    var byteOffset = positionBufferView.GetProperty("byteOffset").GetInt32()
        + accessorByteOffset
        + logicalVertexIndex * 3 * sizeof(float);

    return (
        BitConverter.ToSingle(fixtureExport.BinBytes, byteOffset),
        BitConverter.ToSingle(fixtureExport.BinBytes, byteOffset + sizeof(float)),
        BitConverter.ToSingle(fixtureExport.BinBytes, byteOffset + sizeof(float) * 2));
}

static List<(float X, float Y, float Z)> ReadExportedPositions(TieGltfExport fixtureExport)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var positionAccessorIndex = primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32();
    return ReadExportedVec3Accessor(fixtureExport, root, positionAccessorIndex);
}

static List<(float X, float Y, float Z)> ReadExportedVec3Accessor(
    TieGltfExport fixtureExport,
    JsonElement root,
    int accessorIndex)
{
    var accessor = root.GetProperty("accessors")[accessorIndex];
    var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var accessorByteOffset = accessor.TryGetProperty("byteOffset", out var accessorOffsetElement)
        ? accessorOffsetElement.GetInt32()
        : 0;
    var byteOffset = bufferView.GetProperty("byteOffset").GetInt32() + accessorByteOffset;
    var count = accessor.GetProperty("count").GetInt32();
    var positions = new List<(float X, float Y, float Z)>(count);

    for (var i = 0; i < count; i++)
    {
        var offset = byteOffset + i * 3 * sizeof(float);
        positions.Add((
            BitConverter.ToSingle(fixtureExport.BinBytes, offset),
            BitConverter.ToSingle(fixtureExport.BinBytes, offset + sizeof(float)),
            BitConverter.ToSingle(fixtureExport.BinBytes, offset + sizeof(float) * 2)));
    }

    return positions;
}

static Dictionary<string, int> ReadDaeTriangleFaceKeyCounts(string daePath)
{
    var document = XDocument.Load(daePath);
    var ns = document.Root?.Name.Namespace ?? XNamespace.None;
    var positionArray = document
        .Descendants(ns + "float_array")
        .First(element => element.Attribute("id")?.Value.EndsWith("_positions_array", StringComparison.Ordinal) == true)
        .Value
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
    var positions = new List<(float X, float Y, float Z)>(positionArray.Length / 3);
    for (var i = 0; i + 2 < positionArray.Length; i += 3)
    {
        positions.Add((positionArray[i], positionArray[i + 2], -positionArray[i + 1]));
    }

    var indices = document
        .Descendants(ns + "triangles")
        .SelectMany(triangles => (triangles.Element(ns + "p")?.Value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse))
        .ToList();

    return BuildTriangleFaceKeyCounts(positions, indices);
}

static Dictionary<string, int> BuildTriangleFaceKeyCounts(
    IReadOnlyList<(float X, float Y, float Z)> positions,
    IReadOnlyList<int> indices)
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i + 2 < indices.Count; i += 3)
    {
        var key = BuildTriangleFaceKey(
            positions[indices[i]],
            positions[indices[i + 1]],
            positions[indices[i + 2]]);
        if (key is null)
        {
            continue;
        }

        counts.TryGetValue(key, out var count);
        counts[key] = count + 1;
    }

    return counts;
}

static string? BuildTriangleFaceKey(
    (float X, float Y, float Z) a,
    (float X, float Y, float Z) b,
    (float X, float Y, float Z) c)
{
    if (!TryFaceNormal(a, b, c, out var faceNormal))
    {
        return null;
    }

    var positionKeys = new[]
    {
        FormatPositionKey(a),
        FormatPositionKey(b),
        FormatPositionKey(c)
    };
    Array.Sort(positionKeys, StringComparer.Ordinal);

    return $"{positionKeys[0]}|{positionKeys[1]}|{positionKeys[2]}|{FormatNormalKey(faceNormal)}";
}

static string FormatPositionKey((float X, float Y, float Z) position)
{
    var key = QuantizedPositionKey(position);
    return $"{key.X},{key.Y},{key.Z}";
}

static string FormatNormalKey((float X, float Y, float Z) normal)
{
    const float scale = 10000f;
    return $"{(int)MathF.Round(normal.X * scale)},{(int)MathF.Round(normal.Y * scale)},{(int)MathF.Round(normal.Z * scale)}";
}

static int CountPacketRgbaSlots(TieClass tie, int lodIndex)
{
    return tie.PacketTables
        .FirstOrDefault(table => table.LodIndex == lodIndex)?
        .Packets
        .Sum(packet => packet.RgbaCount) ?? 0;
}

static List<(float U, float V)> ReadExportedIndexedTexCoords(TieGltfExport fixtureExport)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
    var texCoordAccessorIndex = primitive.GetProperty("attributes").GetProperty("TEXCOORD_0").GetInt32();
    var texCoords = ReadExportedVec2Accessor(fixtureExport, root, texCoordAccessorIndex);
    var indices = ReadExportedPrimitiveIndices(fixtureExport, root, primitive);
    return indices.Select(index => texCoords[index]).ToList();
}

static List<(float U, float V)> ReadExportedVec2Accessor(
    TieGltfExport fixtureExport,
    JsonElement root,
    int accessorIndex)
{
    var accessor = root.GetProperty("accessors")[accessorIndex];
    var bufferView = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
    var accessorByteOffset = accessor.TryGetProperty("byteOffset", out var accessorOffsetElement)
        ? accessorOffsetElement.GetInt32()
        : 0;
    var byteOffset = bufferView.GetProperty("byteOffset").GetInt32() + accessorByteOffset;
    var count = accessor.GetProperty("count").GetInt32();
    var values = new List<(float U, float V)>(count);

    for (var i = 0; i < count; i++)
    {
        var offset = byteOffset + i * 2 * sizeof(float);
        values.Add((
            BitConverter.ToSingle(fixtureExport.BinBytes, offset),
            BitConverter.ToSingle(fixtureExport.BinBytes, offset + sizeof(float))));
    }

    return values;
}

static bool TexCoordsNearlyEqualModulo((float U, float V) actual, float expectedU, float expectedV)
{
    const float epsilon = 0.0001f;
    return ModuloDistance(actual.U, expectedU) < epsilon
        && ModuloDistance(actual.V, expectedV) < epsilon;
}

static float TileEdgeBias(int textureSize)
{
    return 1f / textureSize;
}

static float ModuloDistance(float actual, float expected)
{
    var difference = MathF.Abs(actual - expected);
    return MathF.Min(difference, MathF.Abs(difference - MathF.Round(difference)));
}

static List<int> ReadExportedIndices(TieGltfExport fixtureExport)
{
    using var gltfDocument = JsonDocument.Parse(fixtureExport.GltfBytes);
    var root = gltfDocument.RootElement;
    var accessors = root.GetProperty("accessors");
    var bufferViews = root.GetProperty("bufferViews");
    var indices = new List<int>();

    foreach (var primitive in root.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
    {
        var indexAccessor = accessors[primitive.GetProperty("indices").GetInt32()];
        var indexBufferView = bufferViews[indexAccessor.GetProperty("bufferView").GetInt32()];
        var accessorByteOffset = indexAccessor.TryGetProperty("byteOffset", out var accessorOffsetElement)
            ? accessorOffsetElement.GetInt32()
            : 0;
        var byteOffset = indexBufferView.GetProperty("byteOffset").GetInt32() + accessorByteOffset;
        var count = indexAccessor.GetProperty("count").GetInt32();
        var componentType = indexAccessor.GetProperty("componentType").GetInt32();

        for (var i = 0; i < count; i++)
        {
            indices.Add(componentType switch
            {
                5123 => BitConverter.ToUInt16(fixtureExport.BinBytes, byteOffset + i * sizeof(ushort)),
                5125 => checked((int)BitConverter.ToUInt32(fixtureExport.BinBytes, byteOffset + i * sizeof(uint))),
                _ => throw new InvalidDataException($"Unsupported glTF index component type {componentType}.")
            });
        }
    }

    return indices;
}

static List<int> ReadExportedPrimitiveIndices(
    TieGltfExport fixtureExport,
    JsonElement root,
    JsonElement primitive)
{
    var accessors = root.GetProperty("accessors");
    var bufferViews = root.GetProperty("bufferViews");
    var indexAccessor = accessors[primitive.GetProperty("indices").GetInt32()];
    var indexBufferView = bufferViews[indexAccessor.GetProperty("bufferView").GetInt32()];
    var accessorByteOffset = indexAccessor.TryGetProperty("byteOffset", out var accessorOffsetElement)
        ? accessorOffsetElement.GetInt32()
        : 0;
    var byteOffset = indexBufferView.GetProperty("byteOffset").GetInt32() + accessorByteOffset;
    var count = indexAccessor.GetProperty("count").GetInt32();
    var componentType = indexAccessor.GetProperty("componentType").GetInt32();
    var indices = new List<int>(count);

    for (var i = 0; i < count; i++)
    {
        indices.Add(componentType switch
        {
            5121 => fixtureExport.BinBytes[byteOffset + i],
            5123 => BitConverter.ToUInt16(fixtureExport.BinBytes, byteOffset + i * sizeof(ushort)),
            5125 => checked((int)BitConverter.ToUInt32(fixtureExport.BinBytes, byteOffset + i * sizeof(uint))),
            _ => throw new InvalidDataException($"Unsupported glTF index component type {componentType}.")
        });
    }

    return indices;
}

static bool HasReducibleInteriorIntegerBoundary(float a, float b, float c)
{
    var bestRange = Range(a, b, c);
    var originalRange = bestRange;
    var bestInteriorBoundaryCount = CountInteriorIntegerBoundaries(a, b, c);
    if (bestInteriorBoundaryCount == 0)
    {
        return false;
    }

    var centeredBOffset = -(int)MathF.Round(b - a);
    var centeredCOffset = -(int)MathF.Round(c - a);
    for (var bOffset = centeredBOffset - 2; bOffset <= centeredBOffset + 2; bOffset++)
    {
        for (var cOffset = centeredCOffset - 2; cOffset <= centeredCOffset + 2; cOffset++)
        {
            var candidateB = b + bOffset;
            var candidateC = c + cOffset;
            var candidateRange = Range(a, candidateB, candidateC);
            if (CollapsesRepeatedTileSpan(originalRange, candidateRange))
            {
                continue;
            }

            if (CountInteriorIntegerBoundaries(a, candidateB, candidateC) < bestInteriorBoundaryCount)
            {
                return true;
            }
        }
    }

    return false;
}

static float Range(float a, float b, float c)
{
    return MathF.Max(a, MathF.Max(b, c)) - MathF.Min(a, MathF.Min(b, c));
}

static bool CollapsesRepeatedTileSpan(float originalRange, float candidateRange)
{
    const float minimumMeaningfulOriginalSpan = 0.5f;
    const float collapsedCandidateSpan = 0.0001f;
    const float deliberateMultiTileSpan = 1.5f;
    const float shrinkTolerance = 0.01f;

    return (originalRange >= minimumMeaningfulOriginalSpan
            && candidateRange <= collapsedCandidateSpan)
        || (originalRange >= deliberateMultiTileSpan
            && candidateRange < originalRange - shrinkTolerance);
}

static int CountInteriorIntegerBoundaries(float a, float b, float c)
{
    const float epsilon = 0.000001f;

    var min = MathF.Min(a, MathF.Min(b, c));
    var max = MathF.Max(a, MathF.Max(b, c));
    var count = 0;
    for (var boundary = MathF.Floor(min) + 1f; boundary < max; boundary += 1f)
    {
        if (boundary > min + epsilon && boundary < max - epsilon)
        {
            count++;
        }
    }

    return count;
}

static TextureResources? BuildFixtureTextureResources(string fixturePath)
{
    var directory = Path.GetDirectoryName(fixturePath);
    if (directory is null || !Directory.Exists(directory))
    {
        return null;
    }

    var textures = Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
        .Select(path => (Path: path, TextureId: TryParseTextureId(Path.GetFileName(path), out var textureId) ? textureId : (int?)null))
        .Where(item => item.TextureId.HasValue)
        .ToArray();
    var uris = textures.ToDictionary(
        item => item.TextureId!.Value,
        item => $"textures/{Path.GetFileName(item.Path)}");
    var metadata = textures.ToDictionary(
        item => item.TextureId!.Value,
        item =>
        {
            using var input = File.OpenRead(item.Path);
            return PngTextureMetadataReader.ReadPng(input);
        });
    var sizes = metadata.ToDictionary(item => item.Key, item => item.Value.Size);
    var alpha = metadata.ToDictionary(item => item.Key, item => item.Value.Alpha);

    return uris.Count == 0 ? null : new TextureResources(uris, sizes, alpha);
}

static bool TryParseTextureId(string fileName, out int textureId)
{
    textureId = 0;
    if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out textureId)
        && textureId >= 0)
    {
        return true;
    }

    var parts = fileName.Split('.');
    return parts.Length == 4
        && parts[0] == "tex"
        && parts[2] == "0"
        && parts[3].Equals("png", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(parts[1], out textureId)
        && textureId >= 0;
}

static bool TryParseMaterialTextureId(string materialName, out int textureId)
{
    textureId = 0;
    if (!materialName.StartsWith("tex_", StringComparison.Ordinal))
    {
        return false;
    }

    var digits = new string(materialName["tex_".Length..].TakeWhile(char.IsDigit).ToArray());
    return digits.Length > 0
        && int.TryParse(digits, out textureId)
        && textureId >= 0;
}

static (float X, float Y, float Z) ToGltfPosition(TieClass tie, short sourceX, short sourceY, short sourceZ)
{
    var scale = tie.Header.Scale / 1024f;
    return (sourceX * scale, sourceZ * scale, -sourceY * scale);
}

static bool PositionsNearlyEqual((float X, float Y, float Z) left, (float X, float Y, float Z) right)
{
    const float epsilon = 0.0001f;
    return MathF.Abs(left.X - right.X) < epsilon
        && MathF.Abs(left.Y - right.Y) < epsilon
        && MathF.Abs(left.Z - right.Z) < epsilon;
}

static (float R, float G, float B) GetExpectedGlowEmissionFactor(int rawRgba)
{
    var r = (byte)rawRgba;
    var g = (byte)(rawRgba >> 8);
    var b = (byte)(rawRgba >> 16);
    var max = Math.Max(r, Math.Max(g, b));
    return max == 0
        ? (0f, 0f, 0f)
        : (r / (float)max, g / (float)max, b / (float)max);
}

static bool EmissiveFactorMatches(JsonElement emissiveFactor, (float R, float G, float B) expected)
{
    const float epsilon = 0.0001f;
    return emissiveFactor.GetArrayLength() == 3
        && MathF.Abs(emissiveFactor[0].GetSingle() - expected.R) < epsilon
        && MathF.Abs(emissiveFactor[1].GetSingle() - expected.G) < epsilon
        && MathF.Abs(emissiveFactor[2].GetSingle() - expected.B) < epsilon;
}

static float Distance((float X, float Y, float Z) left, (float X, float Y, float Z) right)
{
    var dx = right.X - left.X;
    var dy = right.Y - left.Y;
    var dz = right.Z - left.Z;
    return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
}

static float ExportedVectorLength((float X, float Y, float Z) vector)
{
    return MathF.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
}

static float NormalDot((float X, float Y, float Z) left, (float X, float Y, float Z) right)
{
    return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
}

static bool TryFaceNormal(
    (float X, float Y, float Z) a,
    (float X, float Y, float Z) b,
    (float X, float Y, float Z) c,
    out (float X, float Y, float Z) normal)
{
    var ab = (X: b.X - a.X, Y: b.Y - a.Y, Z: b.Z - a.Z);
    var ac = (X: c.X - a.X, Y: c.Y - a.Y, Z: c.Z - a.Z);
    return TryNormalize((
        ab.Y * ac.Z - ab.Z * ac.Y,
        ab.Z * ac.X - ab.X * ac.Z,
        ab.X * ac.Y - ab.Y * ac.X),
        out normal);
}

static (float X, float Y, float Z) Normalize((float X, float Y, float Z) value)
{
    return TryNormalize(value, out var normal) ? normal : value;
}

static bool TryNormalize((float X, float Y, float Z) value, out (float X, float Y, float Z) normal)
{
    var length = ExportedVectorLength(value);
    if (length <= 1e-6f)
    {
        normal = default;
        return false;
    }

    normal = (value.X / length, value.Y / length, value.Z / length);
    return true;
}

static (int X, int Y, int Z) QuantizedPositionKey((float X, float Y, float Z) position)
{
    const float scale = 100000f;
    return (
        (int)MathF.Round(position.X * scale),
        (int)MathF.Round(position.Y * scale),
        (int)MathF.Round(position.Z * scale));
}

static bool IsNormalLengthVector(TieClass tie, short x, short y, short z)
{
    var length = VectorLength(tie, x, y, z);
    return length is >= 0.5f and <= 1.9f;
}

static bool IsAttributeVector(short x, short y, short z)
{
    return z == 4096 && Math.Abs(x) <= 4096 && Math.Abs(y) <= 4096;
}

static float VectorLength(TieClass tie, short x, short y, short z)
{
    var scale = tie.Header.Scale / 1024f;
    var scaledX = x * scale;
    var scaledY = y * scale;
    var scaledZ = z * scale;
    return MathF.Sqrt(scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ);
}

static TieClass ReadGcTie(Stream input)
{
    var profile = TieGameProfile.Default.WithGameLabel("GC");
    return TieClassReader.Read(input, TieClassReadOptions.ForGameProfile(profile));
}

static string FindRepoRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ratchet-ps2-cli.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
}

sealed record TextureResources(
    IReadOnlyDictionary<int, string> Uris,
    IReadOnlyDictionary<int, TextureSize> Sizes,
    IReadOnlyDictionary<int, TextureAlphaInfo> Alpha);

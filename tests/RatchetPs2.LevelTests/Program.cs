using System.Buffers.Binary;
using System.Text.Json;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Wad;
using RatchetPs2.Games.DL.Level;

ValidateLevelInfoLookup();
ValidateLevelWadParsing();
ValidateLooseLevelWadExtraction();
ValidateLooseLevelWadUnpacking();
ValidateLooseLevelWadRenderPackageWhenAvailable();
ValidateLooseLevelWadFailures();
ValidateMissionPlaceholderDetection();
ValidateLevelSceneWadEmptyDetection();
ValidateCoreLevelSegments();
ValidateCodeSegmentParsing();
ValidateHudBankParsing();
ValidateWorldInstanceParsing();
ValidateAssetSlicing();
ValidatePifMipRoundtrip();
ValidateNormalizedTextureArtifacts();

Console.WriteLine("DL level extraction tests passed.");

static void ValidateLevelInfoLookup()
{
    var iso = new byte[DlLevelConstants.RetailLevelInfoTableOffset
        + (DlLevelConstants.LevelInfoCount * DlLevelConstants.LevelInfoSize)
        + DlLevelConstants.SectorSize];

    WriteLevelInfoEntry(
        iso,
        1,
        audio: new DlFileBlock(20, 1),
        level: new DlFileBlock(21, 1),
        scene: new DlFileBlock(22, 1));
    WriteLevelInfoEntry(
        iso,
        0x15,
        audio: new DlFileBlock(30, 1),
        level: new DlFileBlock(10, 2),
        scene: new DlFileBlock(31, 1));

    iso[10 * DlLevelConstants.SectorSize] = 0x42;

    using var stream = new MemoryStream(iso, writable: false);
    var levelSet = DlLevelInfoReader.ReadLevelSet(stream, 0x15);

    Expect(levelSet.RequestedLevelIndex == 0x15, "requested level index should be preserved");
    Expect(levelSet.MediaLevelIndex == 1, "level 0x15 should normalize to media level 1");
    Expect(levelSet.RequestedLevel.LevelWad == new DlFileBlock(10, 2), "requested level WAD block should come from requested levelinfo");
    Expect(levelSet.MediaLevel.LevelAudioWad == new DlFileBlock(20, 1), "audio WAD block should come from normalized media level");
    Expect(levelSet.MediaLevel.LevelSceneWad == new DlFileBlock(22, 1), "scene WAD block should come from normalized media level");

    stream.Position = 0;
    var levelWadBytes = DlLevelInfoReader.ReadSectorBlock(stream, levelSet.RequestedLevel.LevelWad);
    Expect(levelWadBytes.Length == DlLevelConstants.SectorSize * 2, "sector block read should return sector-scaled length");
    Expect(levelWadBytes[0] == 0x42, "sector block read should seek to the requested sector");

    stream.Position = 0;
    var levelWadHeaderBytes = DlLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 1);
    Expect(levelWadHeaderBytes.Length == DlLevelConstants.SectorSize, "fixed sector header read should ignore fileblock length");
    Expect(levelWadHeaderBytes[0] == 0x42, "fixed sector header read should seek to the requested sector");

    ExpectThrows<ArgumentOutOfRangeException>(() => DlLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 0));
    ExpectThrows<InvalidDataException>(() => DlLevelInfoReader.ReadSectorHeader(stream, new DlFileBlock(int.MaxValue, 1), 1));
    ExpectThrows<ArgumentOutOfRangeException>(() => DlLevelInfoReader.ReadLevelSet(stream, DlLevelConstants.LevelInfoCount));
}

static void ValidateLevelWadParsing()
{
    var levelWadBytes = new byte[DlLevelConstants.SectorSize * 5];
    WriteInt32(levelWadBytes, 0x00, DlLevelConstants.LevelWadHeaderSize);
    WriteInt32(levelWadBytes, 0x04, 0x1234);
    WriteInt32(levelWadBytes, 0x08, 7);
    WriteInt32(levelWadBytes, 0x0c, 2);
    WriteInt32(levelWadBytes, 0x10, 0x1111);
    WriteInt32(levelWadBytes, 0x14, 0x2222);
    WriteFileBlock(levelWadBytes, 0x18, new DlFileBlock(2, 1));
    WriteFileBlock(levelWadBytes, 0x20, new DlFileBlock(3, 1));
    WriteFileBlock(levelWadBytes, 0x28, new DlFileBlock(4, 1));
    levelWadBytes[2 * DlLevelConstants.SectorSize] = 0xaa;
    levelWadBytes[3 * DlLevelConstants.SectorSize] = 0xbb;

    var levelWad = DlLevelWadReader.ReadLevelWad(levelWadBytes);
    Expect(levelWad.HeaderSize == DlLevelConstants.LevelWadHeaderSize, "level WAD header size should be parsed");
    Expect(levelWad.Sector == 0x1234, "level WAD sector should be parsed");
    Expect(levelWad.Level == 7, "level WAD level id should be parsed");
    Expect(levelWad.Data == new DlFileBlock(2, 1), "core level fileblock should be parsed");
    Expect(levelWad.CoreBank == new DlFileBlock(3, 1), "core bank fileblock should be parsed");
    Expect(levelWad.Chunks[0] == new DlFileBlock(4, 1), "first chunk fileblock should be parsed");
    Expect(levelWad.HeaderBytes.Length == DlLevelConstants.LevelWadHeaderSize, "level WAD header bytes should be preserved");

    var coreLevel = DlLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data);
    Expect(coreLevel.Length == DlLevelConstants.SectorSize, "sector fileblock should read sector-scaled length");
    Expect(coreLevel[0] == 0xaa, "sector fileblock should read from the requested sector");

    var byteLengthBlock = DlLevelWadReader.ReadByteLengthFileBlock(levelWadBytes, new DlFileBlock(3, 17));
    Expect(byteLengthBlock.Length == 17, "byte-length fileblock should not sector-scale length");
    Expect(byteLengthBlock[0] == 0xbb, "byte-length fileblock should seek by sector offset");

    var isoBytes = new byte[DlLevelConstants.SectorSize * 8];
    isoBytes[(4 + 2) * DlLevelConstants.SectorSize] = 0xcc;
    isoBytes[(4 + 3) * DlLevelConstants.SectorSize] = 0xdd;
    using var isoStream = new MemoryStream(isoBytes, writable: false);

    var relativeSectorBlock = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, 4, new DlFileBlock(2, 1));
    Expect(relativeSectorBlock.Length == DlLevelConstants.SectorSize, "relative sector fileblock should read sector-scaled length");
    Expect(relativeSectorBlock[0] == 0xcc, "relative sector fileblock should add the WAD base sector");

    var relativeByteBlock = DlLevelInfoReader.ReadByteLengthSectorRelativeBlock(isoStream, 4, new DlFileBlock(3, 17));
    Expect(relativeByteBlock.Length == 17, "relative byte-length fileblock should not sector-scale length");
    Expect(relativeByteBlock[0] == 0xdd, "relative byte-length fileblock should add the WAD base sector");
}

static void ValidateLooseLevelWadExtraction()
{
    const int levelIndex = 3;
    const int headerSector = 20;
    const int payloadBaseSector = 60;
    var looseWadBytes = CreateSyntheticLooseLevelWad(payloadBaseSector);
    var iso = CreateSyntheticIso(levelIndex, headerSector, payloadBaseSector, looseWadBytes);

    using var stream = new MemoryStream(iso, writable: false);
    var extracted = DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex);

    Expect(extracted.LevelIndex == levelIndex, "loose WAD extraction should preserve requested level index");
    Expect(extracted.HeaderSector == headerSector, "loose WAD extraction should report the header sector");
    Expect(extracted.PayloadBaseSector == payloadBaseSector, "loose WAD extraction should report the payload base sector");
    Expect(extracted.SectorCount == looseWadBytes.Length / DlLevelConstants.SectorSize, "loose WAD extraction should copy through the last referenced sector");
    Expect(extracted.Bytes.SequenceEqual(looseWadBytes), "loose WAD extraction should preserve referenced WAD bytes in a self-contained layout");
}

static void ValidateLooseLevelWadUnpacking()
{
    var looseWadBytes = CreateSyntheticLooseLevelWad(payloadBaseSector: 20);
    var package = DlLevelWadUnpacker.Unpack(looseWadBytes);
    var files = package.Files.ToDictionary(file => file.Path);

    Expect(files.ContainsKey("level_wad/header.bin"), "loose WAD unpack should include the level WAD header");
    Expect(files["level_wad/core_sound.bnk"].Bytes[0] == 0x41, "loose WAD unpack should include core sound bank bytes");
    Expect(files["level_wad/chunks/chunk0.wad"].Bytes[0] == 0x51, "loose WAD unpack should include chunk bytes");
    Expect(files["missions/0000/mission.wad"].Bytes[0x40] == 0xA1, "loose WAD unpack should include mission WAD bytes");
    Expect(files["missions/0000/gameplay.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2, 0xA3, 0xA4 }), "loose WAD unpack should slice mission gameplay bytes");
    Expect(files["missions/0000/classes.bin"].Bytes.SequenceEqual(new byte[] { 0xB1, 0xB2, 0xB3, 0xB4 }), "loose WAD unpack should slice mission classes bytes");
    Expect(files["missions/0000/gameplay_instances.bin"].Bytes[0] == 0x81, "loose WAD unpack should include mission instance bytes");
    Expect(!files.ContainsKey("missions/0001/mission.wad"), "loose WAD unpack should skip placeholder missions");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "loose WAD unpack should expose core asset header payload");
    Expect(files["world/lighting/directional_lights.bin"].Bytes[0] == 0xD1, "loose WAD unpack should expose parsed world slot payloads");

    var packed = package.ToPackedPackage();
    Expect(packed.Entries.Count == package.Files.Count, "packed package entry count should match loose file count");
    var packedMissionEntry = packed.Entries.Single(entry => entry.Path == "missions/0000/mission.wad");
    var packedMissionBytes = packed.PackedBytes.AsSpan(packedMissionEntry.Offset, packedMissionEntry.Length).ToArray();
    Expect(packedMissionBytes.SequenceEqual(files["missions/0000/mission.wad"].Bytes), "packed package offsets should round-trip entry bytes");
}

static void ValidateLooseLevelWadRenderPackageWhenAvailable()
{
    var wadPath = Environment.GetEnvironmentVariable("RATCHET_PS2_DL_LEVEL_WAD")
        ?? "/tmp/ratchet-dl-wad-realdata-44-v2/level44.wad";
    if (!File.Exists(wadPath))
    {
        return;
    }

    var renderPackage = DlLevelWadRenderPackageBuilder.BuildPacked(
        File.ReadAllBytes(wadPath),
        DlLevelWadRenderPackageBuildOptions.Browser);
    var entries = renderPackage.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

    Expect(entries.ContainsKey("manifest.json"), "render package should include the root viewer manifest");
    Expect(entries.ContainsKey("assets/manifest.json"), "render package should include the asset viewer manifest");
    Expect(entries.ContainsKey("world/manifest.json"), "render package should include the world viewer manifest");
    Expect(entries.ContainsKey("assets/tfrag/tfrag.gltf"), "render package should include the terrain glTF");
    Expect(entries.ContainsKey("assets/tfrag/tfrag.buffer.bin"), "render package should include the terrain glTF buffer");
    Expect(entries.ContainsKey("world/lighting/directional_lights.bin"), "render package should include directional light sidecars");
    Expect(!entries.ContainsKey("assets/tfrag/tfrag.bin"), "browser render package should omit source terrain bytes");
    Expect(
        entries.Keys.All(path => !path.EndsWith(".diagnostics.json", StringComparison.Ordinal)),
        "browser render package should omit glTF diagnostics");
    Expect(
        entries.Keys.All(path =>
            !path.EndsWith("/tie.bin", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.bin", StringComparison.Ordinal)
            && !path.EndsWith("/tie.json", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.json", StringComparison.Ordinal)),
        "browser render package should omit source tie and shrub sidecars");

    using var rootManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["manifest.json"]));
    var performanceTimings = rootManifest.RootElement.GetProperty("PerformanceTimings").EnumerateArray().ToArray();
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.tfrag"),
        "render package manifest should include top-level terrain timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tfrag.decode"),
        "render package manifest should include terrain exporter subphase timing");

    using var assetManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/manifest.json"]));
    var gltfExports = assetManifest.RootElement.GetProperty("GltfExports").EnumerateArray().ToArray();
    Expect(
        gltfExports.Any(entry =>
            entry.GetProperty("Family").GetString() == "tfrag"
            && entry.GetProperty("Status").GetString() == "written"),
        "render package asset manifest should contain a written tfrag export");

    if (gltfExports.Any(entry =>
        entry.GetProperty("Family").GetString() == "tie"
        && entry.GetProperty("Status").GetString() == "written"))
    {
        Expect(
            performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tie.document"),
            "render package manifest should include aggregated tie document timing when ties are written");
    }
}

static void ValidateLooseLevelWadFailures()
{
    const int levelIndex = 4;
    const int headerSector = 20;
    const int payloadBaseSector = 60;

    var negativeBlockWad = CreateSyntheticLooseLevelWad(payloadBaseSector, negativeBlock: true);
    var negativeBlockIso = CreateSyntheticIso(levelIndex, headerSector, payloadBaseSector, negativeBlockWad);
    using (var stream = new MemoryStream(negativeBlockIso, writable: false))
    {
        ExpectThrows<InvalidDataException>(() => DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex));
    }

    var outOfRangePayloadBaseSector = 2000;
    var outOfRangeWad = CreateSyntheticLooseLevelWad(outOfRangePayloadBaseSector);
    var outOfRangeIso = CreateSyntheticIso(
        levelIndex,
        headerSector,
        outOfRangePayloadBaseSector,
        outOfRangeWad,
        includePayloads: false);
    using (var stream = new MemoryStream(outOfRangeIso, writable: false))
    {
        ExpectThrows<InvalidDataException>(() => DlLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex));
    }

    var badHeader = CreateSyntheticLooseLevelWad(payloadBaseSector);
    WriteInt32(badHeader, 0x00, DlLevelConstants.LevelWadHeaderSize - 1);
    ExpectThrows<InvalidDataException>(() => DlLevelWadUnpacker.Unpack(badHeader));
}

static byte[] ReadPackedEntryBytes(PackedFilePackage package, PackedFileEntry entry)
{
    return package.PackedBytes.AsSpan(entry.Offset, entry.Length).ToArray();
}

static void ValidateMissionPlaceholderDetection()
{
    var placeholder = new byte[DlLevelConstants.SectorSize];
    WriteInt32(placeholder, 0x00, -1);
    WriteInt32(placeholder, 0x04, 0);
    WriteInt32(placeholder, 0x08, -1);
    WriteInt32(placeholder, 0x0c, 0);

    Expect(DlMissionDataReader.IsPlaceholderMissionData(placeholder), "mission placeholder sentinel should be detected");
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(placeholder[..(DlLevelConstants.SectorSize - 1)]), "mission placeholder should require one sector");

    var nonZeroPayload = placeholder.ToArray();
    nonZeroPayload[0x20] = 1;
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(nonZeroPayload), "mission placeholder should reject non-zero payload bytes");

    var realMissionHeader = new byte[DlLevelConstants.SectorSize];
    WriteInt32(realMissionHeader, 0x00, 0x40);
    WriteInt32(realMissionHeader, 0x04, 0x20);
    WriteInt32(realMissionHeader, 0x08, 0x60);
    WriteInt32(realMissionHeader, 0x0c, 0x10);
    Expect(!DlMissionDataReader.IsPlaceholderMissionData(realMissionHeader), "mission placeholder should reject real mission table headers");
}

static void ValidateLevelSceneWadEmptyDetection()
{
    var sceneWadBytes = new byte[DlLevelConstants.SectorSize * DlLevelConstants.LevelSceneWadHeaderSectorCount];
    WriteInt32(sceneWadBytes, 0x00, DlLevelConstants.LevelSceneWadHeaderSize);
    WriteInt32(sceneWadBytes, 0x04, 0x1234);

    var sceneWad = DlLevelWadReader.ReadLevelSceneWad(sceneWadBytes);
    Expect(DlLevelWadReader.IsHeaderOnlyLevelSceneWad(sceneWadBytes, sceneWad), "header-only level scene WAD should be detected");
    Expect(sceneWad.Scenes.All(DlLevelWadReader.IsEmptyScene), "zeroed scene records should be treated as empty");

    var nonZeroPadding = sceneWadBytes.ToArray();
    nonZeroPadding[DlLevelConstants.LevelSceneWadHeaderSize] = 1;
    Expect(
        !DlLevelWadReader.IsHeaderOnlyLevelSceneWad(nonZeroPadding, DlLevelWadReader.ReadLevelSceneWad(nonZeroPadding)),
        "level scene WAD with non-zero padding should not be treated as header-only");

    var realSpeechOffset = sceneWadBytes.ToArray();
    WriteInt32(realSpeechOffset, 0x08, 5);
    var speechSceneWad = DlLevelWadReader.ReadLevelSceneWad(realSpeechOffset);
    Expect(!DlLevelWadReader.IsEmptyScene(speechSceneWad.Scenes[0]), "scene speech offsets should make a scene non-empty");
    Expect(!DlLevelWadReader.IsHeaderOnlyLevelSceneWad(realSpeechOffset, speechSceneWad), "level scene WAD with scene metadata should not be treated as header-only");

    var realSubtitles = sceneWadBytes.ToArray();
    WriteFileBlock(realSubtitles, 0x10, new DlFileBlock(2, 1));
    var subtitlesSceneWad = DlLevelWadReader.ReadLevelSceneWad(realSubtitles);
    Expect(!DlLevelWadReader.IsEmptyScene(subtitlesSceneWad.Scenes[0]), "scene subtitle fileblocks should make a scene non-empty");
    Expect(!DlLevelWadReader.IsHeaderOnlyLevelSceneWad(realSubtitles, subtitlesSceneWad), "level scene WAD with subtitle metadata should not be treated as header-only");
}

static void ValidateCoreLevelSegments()
{
    var uncompressed = new byte[] { 1, 2, 3, 4 };
    var decompressed = Enumerable.Range(0, 0x400).Select(value => (byte)(value & 0xff)).ToArray();
    var compressed = WadCompression.Compress(decompressed);
    var coreLevelBytes = new byte[0x200 + compressed.Length];

    WriteFileBlock(coreLevelBytes, 0x10, new DlFileBlock(0x100, uncompressed.Length));
    WriteFileBlock(coreLevelBytes, 0x18, new DlFileBlock(0x180, compressed.Length));
    uncompressed.CopyTo(coreLevelBytes.AsSpan(0x100));
    compressed.CopyTo(coreLevelBytes.AsSpan(0x180));

    var segments = DlCoreLevelSegmentReader.Read(coreLevelBytes);
    var assetHeader = segments.Single(segment => segment.HeaderOffset == 0x10);
    var palette = segments.Single(segment => segment.HeaderOffset == 0x18);

    Expect(assetHeader.SemanticName == "asset_header", "segment 0x10 should be named asset_header");
    Expect(assetHeader.RawBytes.SequenceEqual(uncompressed), "uncompressed segment raw bytes should be preserved");
    Expect(assetHeader.PayloadBytes.SequenceEqual(uncompressed), "uncompressed segment payload should match raw bytes");
    Expect(!assetHeader.WasCompressedWad, "uncompressed segment should not be marked compressed");
    Expect(palette.SemanticName == "palette", "segment 0x18 should be named palette");
    Expect(palette.RawBytes.SequenceEqual(compressed), "compressed segment raw bytes should be preserved");
    Expect(palette.PayloadBytes.SequenceEqual(decompressed), "compressed segment payload should be decompressed");
    Expect(palette.WasCompressedWad, "compressed segment should be marked as compressed WAD");
}

static void ValidateCodeSegmentParsing()
{
    var data = new byte[0x10 + 4 + 0x10 + 2 + 3];
    WriteUInt32(data, 0x00, 0x12345678);
    WriteInt32(data, 0x04, 4);
    WriteInt32(data, 0x08, 2);
    WriteUInt32(data, 0x0c, 0x87654321);
    data[0x10] = 1;
    data[0x11] = 2;
    data[0x12] = 3;
    data[0x13] = 4;

    var secondOffset = 0x14;
    WriteUInt32(data, secondOffset, 0x11111111);
    WriteInt32(data, secondOffset + 0x04, 2);
    WriteInt32(data, secondOffset + 0x08, 7);
    WriteUInt32(data, secondOffset + 0x0c, 0x22222222);
    data[secondOffset + 0x10] = 0xaa;
    data[secondOffset + 0x11] = 0xbb;
    data[^3] = 0xfe;
    data[^2] = 0xed;
    data[^1] = 0xfa;

    var code = DlCodeSegmentReader.Read(data);
    Expect(code.Records.Count == 2, "DL code segment should parse complete patch records");
    Expect(code.Records[0].InjectAddress == 0x12345678, "DL code patch inject address should be parsed");
    Expect(code.Records[0].PayloadBytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "DL code patch payload bytes should be sliced");
    Expect(code.Records[1].Offset == secondOffset, "DL code patch offsets should be tracked");
    Expect(code.Records[1].EntrypointAddress == 0x22222222, "DL code patch entrypoint should be parsed");
    Expect(code.UnparsedTail.SequenceEqual(new byte[] { 0xfe, 0xed, 0xfa }), "DL code segment should preserve incomplete trailing bytes");
}

static void ValidateHudBankParsing()
{
    var header = new byte[0xd8];
    WriteUInt16(header, 0x00, 2);
    WriteUInt16(header, 0x02, 1);
    WriteInt32(header, 0x04, 0xb4);
    WriteInt32(header, 0x08, 0xc4);
    WriteInt32(header, 0x0c, 0xc8);
    WriteInt32(header, 0x10, 0xd0);
    WriteInt32(header, 0x14, 0);
    WriteInt32(header, 0x18, 1);
    WriteInt32(header, 0x1c, 1);
    WriteInt32(header, 0x20, 1);
    WriteInt32(header, 0x24, 1);
    WriteInt32(header, 0x34, 1);
    WriteInt32(header, 0x38, 1);
    WriteInt32(header, 0x3c, 1);
    WriteInt32(header, 0x40, 1);
    WriteInt32(header, 0x44, 1);
    WriteInt32(header, 0x54, 0x10);
    WriteInt32(header, 0x58, 0x400);

    WriteUInt16(header, 0xb4, 0x1234);
    WriteUInt16(header, 0xb6, 1);
    WriteUInt16(header, 0xb8, 0);
    WriteUInt16(header, 0xbc, 0xffff);

    WriteInt16(header, 0xc4, 0);
    WriteInt16(header, 0xc6, 0);
    WriteUInt32(header, 0xc8, 0x80000000);
    WriteUInt32(header, 0xd0, 0x80000000);
    header[0xd6] = 2;
    header[0xd7] = 2;

    var bank0 = Enumerable.Range(0, 0x10).Select(value => (byte)value).ToArray();
    var bank1 = CreatePalette();

    var hud = DlHudBankReader.Read(header, [bank0, bank1]);
    Expect(hud.Header.IconCount == 2, "DL HUD icon count should be parsed");
    Expect(hud.Header.FrameCount == 1, "DL HUD frame count should be parsed");
    Expect(hud.Icons[0].IconId == 0x1234, "DL HUD icon id should be parsed");
    Expect(hud.Icons[0].FrameCount == 1 && hud.Icons[0].FirstFrameIndex == 0, "DL HUD icon frame range should be parsed");
    Expect(hud.Icons[1].IconId == 0xffff, "DL HUD icon terminator should be preserved");
    Expect(hud.Frames[0].PaletteIndex == 0 && hud.Frames[0].TextureIndex == 0, "DL HUD frame palette/texture handles should be parsed");
    Expect(DlHudBankReader.TryGetPalette(hud, 0, out var palette), "DL HUD palette should be addressable by id");
    Expect(DlHudBankReader.TryGetTexture(hud, 0, out var texture), "DL HUD texture should be addressable by id");
    Expect(palette.Offset == 0 && palette.BankIndex == 1, "DL HUD high-bit palette offset and bank should be decoded");
    Expect(texture.Offset == 0 && texture.BankIndex == 0, "DL HUD texture bank should be parsed from cumulative counts");
    Expect(texture.Width == 4 && texture.Height == 4, "DL HUD dimensions should be powers of two from u/v log metadata");
    Expect(texture.PixelBytes.SequenceEqual(bank0), "DL HUD texture bytes should be sliced from the source bank");
}

static void ValidateWorldInstanceParsing()
{
    var directionalLights = new byte[0x10 + (2 * DlWorldInstanceReader.DirectionalLightRecordSize)];
    WriteInt32(directionalLights, 0, 2);
    WriteSingle(directionalLights, 0x10, 1.25f);
    WriteSingle(directionalLights, 0x20, 2.5f);

    var tieClassIds = new byte[0x10];
    WriteInt32(tieClassIds, 0, 2);
    WriteInt32(tieClassIds, 4, 0x2132);
    WriteInt32(tieClassIds, 8, 0x21e2);

    var tieInstances = new byte[0x10 + DlWorldInstanceReader.TieInstanceRecordSize];
    WriteInt32(tieInstances, 0, 1);

    var tieGroups = new byte[0x30];
    WriteInt32(tieGroups, 0, 1);
    WriteInt32(tieGroups, 4, 4);

    var shrubClassIds = new byte[0x08];
    WriteInt32(shrubClassIds, 0, 1);
    WriteInt32(shrubClassIds, 4, 0x20f0);

    var shrubInstances = new byte[0x10 + (2 * DlWorldInstanceReader.ShrubInstanceRecordSize)];
    WriteInt32(shrubInstances, 0, 2);

    var shrubGroups = new byte[0x30];
    WriteInt32(shrubGroups, 0, 1);
    WriteInt32(shrubGroups, 4, 2);

    var occlusionMapping = new byte[0x30];
    WriteInt32(occlusionMapping, 0, 1);
    WriteInt32(occlusionMapping, 4, 2);
    WriteInt32(occlusionMapping, 8, 3);

    var tieColors = new byte[]
    {
        0x02, 0x00, 0x02, 0x00, 0xaa, 0xbb, 0xcc, 0xdd,
        0xff, 0xff, 0x00, 0x00,
        0x02, 0x00, 0x00, 0x00
    };
    var worldBytes = BuildWorldInstanceData(
        (0x00, directionalLights),
        (0x04, tieClassIds),
        (0x08, tieInstances),
        (0x0c, tieGroups),
        (0x10, shrubClassIds),
        (0x14, shrubInstances),
        (0x18, shrubGroups),
        (0x1c, occlusionMapping),
        (0x20, tieColors));

    var world = DlWorldInstanceReader.Read(worldBytes);
    Expect(world.Length == worldBytes.Length, "world instance reader should preserve aggregate length");
    Expect(world.Slots.Count == 16, "world instance pointer table should contain 16 slots");
    Expect(world.Slots[0].SemanticName == "directional_lights", "slot 0x00 should be directional lights");
    var lighting = world.DirectionalLights ?? throw new InvalidOperationException("directional light table missing");
    var parsedTieClasses = world.TieClasses ?? throw new InvalidOperationException("tie class id list missing");
    var parsedTieInstances = world.TieInstances ?? throw new InvalidOperationException("tie instance table missing");
    var parsedTieGroups = world.TieGroups ?? throw new InvalidOperationException("tie group table missing");
    var parsedShrubClasses = world.ShrubClasses ?? throw new InvalidOperationException("shrub class id list missing");
    var parsedShrubInstances = world.ShrubInstances ?? throw new InvalidOperationException("shrub instance table missing");
    var parsedOcclusionMapping = world.OcclusionMapping ?? throw new InvalidOperationException("occlusion mapping table missing");
    var parsedTieColors = world.TieInstanceColors ?? throw new InvalidOperationException("tie instance colors missing");

    Expect(lighting.Count == 2, "directional light count should be parsed");
    Expect(lighting.RecordSize == 0x40, "directional light records should be 0x40 bytes");
    Expect(Math.Abs(lighting.Records[0].Vectors[0][0] - 1.25f) < 0.001f, "directional light vector floats should be parsed");
    Expect(parsedTieClasses.ClassIds.SequenceEqual([0x2132, 0x21e2]), "tie class ids should be parsed");
    Expect(parsedTieClasses.PaddingLength == 4, "tie class id padding should be tracked");
    Expect(parsedTieInstances.Count == 1, "tie instance count should be parsed");
    Expect(parsedTieInstances.RecordSize == 0x60, "tie instance records should be 0x60 bytes");
    Expect(parsedTieGroups.GroupCount == 1, "tie group count should be parsed");
    Expect(parsedTieGroups.GroupDataStartOffset == 0x20, "tie group data should start after aligned group offsets");
    Expect(parsedShrubClasses.ClassIds.SequenceEqual([0x20f0]), "shrub class ids should be parsed");
    Expect(parsedShrubInstances.Count == 2, "shrub instance count should be parsed");
    Expect(parsedShrubInstances.RecordSize == 0x70, "shrub instance records should be 0x70 bytes");
    Expect(parsedOcclusionMapping.TfragCount == 1, "occlusion tfrag mapping count should be parsed");
    Expect(parsedOcclusionMapping.TieCount == 2, "occlusion tie mapping count should be parsed");
    Expect(parsedOcclusionMapping.MobyCount == 3, "occlusion moby mapping count should be parsed");
    Expect(parsedTieColors.Length == tieColors.Length, "tie instance color payload length should be preserved");
    Expect(parsedTieColors.IsLengthValid, "tie instance color entries should consume the full payload");
    Expect(parsedTieColors.EntryCount == 3, "tie instance color entry count should be parsed");
    Expect(parsedTieColors.MappedInstanceCount == 1, "tie instance color ids should be mapped once");
    Expect(parsedTieColors.SentinelCount == 1, "tie instance color sentinel entries should be counted");
    Expect(parsedTieColors.DuplicateIdCount == 1, "tie instance color duplicate ids should be counted");
    Expect(parsedTieColors.MinInstanceId == 2 && parsedTieColors.MaxInstanceId == 2, "tie instance color id range should be tracked");

    var invalidPointer = new byte[DlWorldInstanceReader.PointerTableLength];
    WriteInt32(invalidPointer, 0, invalidPointer.Length + 1);
    ExpectThrows<InvalidDataException>(() => DlWorldInstanceReader.Read(invalidPointer));
}

static void ValidateAssetSlicing()
{
    var assetData = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    var knownOffsets = new[] { 0, 10, 20, assetData.Length };

    var defaultZeroOffsetSlice = DlAssetReader.ReadAssetSlice(assetData, 0, knownOffsets);
    Expect(defaultZeroOffsetSlice.Length == 0, "asset offset zero should be treated as absent by default");

    var tfragSlice = DlAssetReader.ReadAssetSlice(assetData, 0, knownOffsets, allowZeroOffset: true);
    Expect(tfragSlice.SequenceEqual(assetData[..10]), "tfrag asset slices should allow offset zero and stop at the next known asset offset");

    var nonZeroSlice = DlAssetReader.ReadAssetSlice(assetData, 10, knownOffsets);
    Expect(nonZeroSlice.SequenceEqual(assetData[10..20]), "non-zero asset slices should stop at the next known asset offset");
}

static void ValidatePifMipRoundtrip()
{
    var palette = CreatePalette();
    var basePixels = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray();
    var mip1 = new byte[] { 1, 2, 3, 4 };
    var mip2 = new byte[] { 5 };

    var texture = PifWriter.CreateIndexed8(
        4,
        4,
        palette,
        basePixels,
        [mip1, mip2],
        isSwizzled: true);
    var pifBytes = PifWriter.Write(texture);
    var roundtrip = PifReader.Read(pifBytes);

    Expect(roundtrip.Header.FileSize == pifBytes.Length, "PIF header file size should match serialized size");
    Expect(roundtrip.Header.USize == 4 && roundtrip.Header.VSize == 4, "PIF dimensions should roundtrip");
    Expect(roundtrip.Header.MipLevels == 3, "PIF mip level count should include base mip");
    Expect(roundtrip.IsSwizzled, "PIF swizzle flag should roundtrip");
    Expect(roundtrip.PaletteData.SequenceEqual(palette), "PIF palette bytes should roundtrip");
    Expect(roundtrip.PixelData.SequenceEqual(basePixels), "PIF base pixel bytes should roundtrip");
    Expect(roundtrip.MipPixelData.Count == 2, "PIF should retain two mip payloads");
    Expect(roundtrip.MipPixelData[0].SequenceEqual(mip1), "PIF mip 1 bytes should roundtrip");
    Expect(roundtrip.MipPixelData[1].SequenceEqual(mip2), "PIF mip 2 bytes should roundtrip");

    var pngBytes = RatchetPs2.Core.Textures.TextureConverter.ConvertToPng(roundtrip);
    using var pngStream = new MemoryStream(pngBytes, writable: false);
    var metadata = PngTextureMetadataReader.ReadPng(pngStream);
    Expect(metadata.Size.Width == 4 && metadata.Size.Height == 4, "PNG preview should use base mip dimensions");

    var halfPaletteTexture = PifWriter.CreateIndexed8(
        2,
        2,
        palette[..0x200],
        [0, 1, 2, 3]);
    var halfPaletteRoundtrip = PifReader.Read(PifWriter.Write(halfPaletteTexture));
    Expect(halfPaletteRoundtrip.Header.PaletteFormat != 0, "0x200-byte PIF palettes should use a non-zero palette format");
    Expect(halfPaletteRoundtrip.PaletteData.Length == 0x200, "0x200-byte PIF palettes should roundtrip with the expected size");
    ExpectThrows<ArgumentException>(() => PifWriter.CreateIndexed8(
        2,
        2,
        palette,
        [0, 1, 2, 3],
        paletteFormat: 1));
}

static void ValidateNormalizedTextureArtifacts()
{
    var palette = CreatePalette();
    var assetData = new byte[0x100];
    for (var i = 0; i < 16; i++)
    {
        assetData[0x60 + i] = (byte)i;
    }

    for (var i = 0; i < 4; i++)
    {
        assetData[0x70 + i] = (byte)(0x80 + i);
    }

    var definition = new DlAssetTextureDefinition(
        Index: 7,
        TextureOffset: 0x20,
        Width: 4,
        Height: 4,
        Type: 3,
        PaletteId: 0,
        MipmapPaletteId: 1,
        Padding: 0);

    var texture = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetData,
        textureDataOffset: 0x40);

    var pif = PifReader.Read(texture.PifBytes);
    Expect(pif.TotalMipLevels == 3, "DL normalized asset texture should store base mip plus mipmaps in PIF");
    Expect(texture.PngBytes.Length > 0, "DL normalized asset texture should generate a PNG preview");
    Expect(texture.Metadata.SourceDefinition is DlAssetTextureDefinition, "texture manifest metadata should retain source table definition");
    Expect(texture.Metadata.MipPixelOffsets.SequenceEqual([0x70, 0x100]), "texture manifest metadata should retain mip source offsets");

    var overlappingPaletteData = new byte[0x500];
    for (var i = 0; i < overlappingPaletteData.Length; i++)
    {
        overlappingPaletteData[i] = (byte)(i & 0xff);
    }

    var paletteStrideTexture = DlAssetReader.BuildAssetTexture(
        "tie",
        0,
        definition with { PaletteId = 1, MipmapPaletteId = -1 },
        overlappingPaletteData,
        assetData,
        textureDataOffset: 0x40);
    var paletteStridePif = PifReader.Read(paletteStrideTexture.PifBytes);
    Expect(paletteStrideTexture.Metadata.PaletteOffset == 0x100, "DL asset palette ids should use 0x100-byte palette WAD stride");
    Expect(
        paletteStridePif.PaletteData.SequenceEqual(overlappingPaletteData.AsSpan(0x100, 0x400).ToArray()),
        "DL asset PIF palette bytes should come from paletteId * 0x100, not paletteId * 0x400");

    var outputDirectory = Path.Combine(Path.GetTempPath(), $"ratchet-ps2-level-texture-{Guid.NewGuid():N}");
    Directory.CreateDirectory(outputDirectory);
    try
    {
        File.WriteAllBytes(Path.Combine(outputDirectory, "tex.0000.pif"), texture.PifBytes);
        File.WriteAllBytes(Path.Combine(outputDirectory, "tex.0000.png"), texture.PngBytes);
        File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"), JsonSerializer.Serialize(new[] { texture.Metadata }));

        var primaryFiles = Directory.EnumerateFiles(outputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Expect(primaryFiles.SequenceEqual(["manifest.json", "tex.0000.pif", "tex.0000.png"]), "normalized texture output should only create PIF, PNG, and manifest artifacts");
        Expect(primaryFiles.All(name => name is not null
            && !name.EndsWith(".def", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".palette", StringComparison.OrdinalIgnoreCase)
            && !name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)), "normalized texture output should not use def, palette, or numbered mip bin sidecars");

        var manifestJson = File.ReadAllText(Path.Combine(outputDirectory, "manifest.json"));
        Expect(manifestJson.Contains("\"TextureOffset\":32", StringComparison.Ordinal), "manifest should retain original texture table offset");
        Expect(manifestJson.Contains("\"MipmapPaletteId\":1", StringComparison.Ordinal), "manifest should retain mipmap table metadata");
    }
    finally
    {
        Directory.Delete(outputDirectory, recursive: true);
    }
}

static byte[] CreateSyntheticIso(
    int levelIndex,
    int headerSector,
    int payloadBaseSector,
    byte[] looseWadBytes,
    bool includePayloads = true)
{
    var iso = new byte[Math.Max(
        DlLevelConstants.RetailLevelInfoTableOffset + (DlLevelConstants.LevelInfoCount * DlLevelConstants.LevelInfoSize),
        ((includePayloads ? payloadBaseSector : headerSector) * DlLevelConstants.SectorSize)
            + (includePayloads ? looseWadBytes.Length : DlLevelConstants.LevelWadHeaderSectorCount * DlLevelConstants.SectorSize))];

    WriteLevelInfoEntry(
        iso,
        levelIndex,
        audio: new DlFileBlock(0, 0),
        level: new DlFileBlock(headerSector, 1),
        scene: new DlFileBlock(0, 0));

    var headerLength = DlLevelConstants.LevelWadHeaderSectorCount * DlLevelConstants.SectorSize;
    looseWadBytes.AsSpan(0, headerLength).CopyTo(iso.AsSpan(headerSector * DlLevelConstants.SectorSize));
    if (includePayloads)
    {
        looseWadBytes.CopyTo(iso.AsSpan(payloadBaseSector * DlLevelConstants.SectorSize));
    }

    return iso;
}

static byte[] CreateSyntheticLooseLevelWad(int payloadBaseSector, bool negativeBlock = false)
{
    var data = new byte[DlLevelConstants.SectorSize * 11];
    WriteInt32(data, 0x00, DlLevelConstants.LevelWadHeaderSize);
    WriteInt32(data, 0x04, payloadBaseSector);
    WriteInt32(data, 0x08, 7);
    WriteInt32(data, 0x0c, 2);
    WriteInt32(data, 0x10, 0x1111);
    WriteInt32(data, 0x14, 0x2222);
    WriteFileBlock(data, 0x18, negativeBlock ? new DlFileBlock(-1, 1) : new DlFileBlock(2, 2));
    WriteFileBlock(data, 0x20, new DlFileBlock(4, 1));
    WriteFileBlock(data, 0x28, new DlFileBlock(5, 1));
    WriteFileBlock(data, 0x40, new DlFileBlock(6, 1));
    WriteFileBlock(data, 0x460, new DlFileBlock(7, 1));
    WriteFileBlock(data, 0x60, new DlFileBlock(8, 1));
    WriteFileBlock(data, 0x468, new DlFileBlock(10, 1));
    WriteFileBlock(data, 0xc60, new DlFileBlock(9, 1));

    var coreLevelBytes = CreateSyntheticCoreLevel();
    coreLevelBytes.CopyTo(data.AsSpan(2 * DlLevelConstants.SectorSize));

    data[4 * DlLevelConstants.SectorSize] = 0x41;
    data[5 * DlLevelConstants.SectorSize] = 0x51;
    data[6 * DlLevelConstants.SectorSize] = 0x61;

    var mission = data.AsSpan(7 * DlLevelConstants.SectorSize, DlLevelConstants.SectorSize);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x00, 0x40);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x04, 4);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x08, 0x44);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x0c, 4);
    mission[0x40] = 0xA1;
    mission[0x41] = 0xA2;
    mission[0x42] = 0xA3;
    mission[0x43] = 0xA4;
    mission[0x44] = 0xB1;
    mission[0x45] = 0xB2;
    mission[0x46] = 0xB3;
    mission[0x47] = 0xB4;

    data[8 * DlLevelConstants.SectorSize] = 0x81;
    data[9 * DlLevelConstants.SectorSize] = 0x91;

    var placeholderOffset = 10 * DlLevelConstants.SectorSize;
    WriteInt32(data, placeholderOffset + 0x00, -1);
    WriteInt32(data, placeholderOffset + 0x04, 0);
    WriteInt32(data, placeholderOffset + 0x08, -1);
    WriteInt32(data, placeholderOffset + 0x0c, 0);

    return data;
}

static byte[] CreateSyntheticCoreLevel()
{
    var world = BuildWorldInstanceData((0x00, new byte[] { 0xD1, 0xD2, 0xD3, 0xD4 }));
    var data = new byte[DlLevelConstants.SectorSize * 2];
    WriteFileBlock(data, 0x10, new DlFileBlock(0x100, 4));
    WriteFileBlock(data, 0x58, new DlFileBlock(0x180, world.Length));
    data[0x100] = 1;
    data[0x101] = 2;
    data[0x102] = 3;
    data[0x103] = 4;
    world.CopyTo(data.AsSpan(0x180));
    return data;
}

static byte[] CreatePalette()
{
    var palette = new byte[0x400];
    for (var i = 0; i < 256; i++)
    {
        palette[(i * 4) + 0] = (byte)i;
        palette[(i * 4) + 1] = (byte)(255 - i);
        palette[(i * 4) + 2] = (byte)(i / 2);
        palette[(i * 4) + 3] = 0x80;
    }

    return palette;
}

static void WriteLevelInfoEntry(byte[] data, int levelIndex, DlFileBlock audio, DlFileBlock level, DlFileBlock scene)
{
    var offset = DlLevelConstants.RetailLevelInfoTableOffset + (levelIndex * DlLevelConstants.LevelInfoSize);
    WriteFileBlock(data, offset + 0x00, audio);
    WriteFileBlock(data, offset + 0x08, level);
    WriteFileBlock(data, offset + 0x10, scene);
}

static void WriteFileBlock(byte[] data, int offset, DlFileBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteInt32(byte[] data, int offset, int value)
{
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
}

static void WriteUInt32(byte[] data, int offset, uint value)
{
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)), value);
}

static void WriteInt16(byte[] data, int offset, short value)
{
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, sizeof(short)), value);
}

static void WriteUInt16(byte[] data, int offset, ushort value)
{
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)), value);
}

static void WriteSingle(byte[] data, int offset, float value)
{
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(float)), BitConverter.SingleToInt32Bits(value));
}

static byte[] BuildWorldInstanceData(params (int HeaderOffset, byte[] Payload)[] slots)
{
    var length = DlWorldInstanceReader.PointerTableLength + slots.Sum(slot => slot.Payload.Length);
    var data = new byte[length];
    var offset = DlWorldInstanceReader.PointerTableLength;

    foreach (var slot in slots)
    {
        WriteInt32(data, slot.HeaderOffset, offset);
        slot.Payload.CopyTo(data.AsSpan(offset));
        offset += slot.Payload.Length;
    }

    return data;
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

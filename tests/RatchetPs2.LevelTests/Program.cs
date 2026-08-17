using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using RatchetPs2.Core.Games;
using RatchetPs2.Core.Textures.Pif;
using RatchetPs2.Core.Textures.Png;
using RatchetPs2.Core.Tfrags;
using RatchetPs2.Core.Wad;
using RatchetPs2.Core.Wad.Models;
using RatchetPs2.Games.DL.Gameplay;
using RatchetPs2.Games.DL.Level;
using RatchetPs2.Games.DL.Moby;
using RatchetPs2.Games.GC.Level;
using RatchetPs2.Games.UYA.Gameplay;
using RatchetPs2.Games.UYA.Level;

ValidateLevelInfoLookup();
ValidateLevelWadParsing();
ValidateLooseLevelWadExtraction();
ValidateLooseLevelWadUnpacking();
ValidateGcLevelInfoLookup();
ValidateUyaLevelInfoLookup();
ValidateUyaLevelWadParsing();
ValidateUyaLooseLevelWadExtraction();
ValidateUyaDetachedWadExtraction();
ValidateUyaLooseLevelWadUnpacking();
ValidateUyaStandaloneLevelDataUnpacking();
ValidateUyaStandaloneGameplayUnpacking();
ValidateUyaCustomMapZipUnpacking();
ValidateUyaGameplayTypedParsing();
ValidateUyaAssetRenderPackageBuild();
ValidateChunkTfragAssetRenderPackageWhenAvailable();
ValidateChunkTfragWadReaderWhenAvailable();
ValidateLooseLevelWadRenderPackageWhenAvailable();
ValidateUyaLooseLevelWadRenderPackageWhenAvailable();
ValidateLooseLevelWadFailures();
ValidateMissionPlaceholderDetection();
ValidateMissionMobyBankParsing();
ValidateLevelSceneWadEmptyDetection();
ValidateCoreLevelSegments();
ValidateGameplayLevelSettingsParsing();
ValidateGameplayMobyInstancesParsing();
ValidateCodeSegmentParsing();
ValidateHudBankParsing();
ValidateWorldInstanceParsing();
ValidateAssetSlicing();
ValidateEnvironmentTextureRenderPackage();
ValidateMobyGsStashTextures();
ValidateDzoGlbExportWhenAvailable();
ValidatePifMipRoundtrip();
ValidateNormalizedTextureArtifacts();

Console.WriteLine("Level extraction tests passed.");

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
    Expect(files["missions/0000/mission.wad"].Bytes[0x60] == 0xA1, "loose WAD unpack should include mission WAD bytes");
    Expect(files["missions/0000/gameplay.bin"].Bytes[0x20] == 0xA1, "loose WAD unpack should slice mission gameplay bytes");
    Expect(files["missions/0000/gameplay/moby_classes.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "loose WAD unpack should split mission gameplay moby classes");
    Expect(files["missions/0000/gameplay/moby_instances.bin"].Bytes.SequenceEqual(new byte[] { 0xA3, 0xA4 }), "loose WAD unpack should split mission gameplay moby instances");
    Expect(files["missions/0000/classes.bin"].Bytes.SequenceEqual(new byte[] { 0xB1, 0xB2, 0xB3, 0xB4 }), "loose WAD unpack should slice mission classes bytes");
    Expect(files["missions/0000/gameplay_instances.bin"].Bytes[0] == 0x81, "loose WAD unpack should include mission instance bytes");
    Expect(!files.ContainsKey("missions/0001/mission.wad"), "loose WAD unpack should skip placeholder missions");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "loose WAD unpack should expose core asset header payload");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xC1, 0xC2, 0xC3 }), "loose WAD unpack should split core gameplay level settings");
    Expect(files["gameplay/core/cameras.bin"].Bytes.SequenceEqual(new byte[] { 0xD1, 0xD2 }), "loose WAD unpack should split core gameplay cameras");
    Expect(files["world/lighting/directional_lights.bin"].Bytes[0] == 0xD1, "loose WAD unpack should expose parsed world slot payloads");

    var packed = package.ToPackedPackage();
    Expect(packed.Entries.Count == package.Files.Count, "packed package entry count should match loose file count");
    var packedMissionEntry = packed.Entries.Single(entry => entry.Path == "missions/0000/mission.wad");
    var packedMissionBytes = packed.PackedBytes.AsSpan(packedMissionEntry.Offset, packedMissionEntry.Length).ToArray();
    Expect(packedMissionBytes.SequenceEqual(files["missions/0000/mission.wad"].Bytes), "packed package offsets should round-trip entry bytes");
}

static void ValidateUyaLevelInfoLookup()
{
    var iso = new byte[Math.Max(
        UyaLevelConstants.RetailLevelInfoTableOffset
            + (UyaLevelConstants.LevelInfoCount * UyaLevelConstants.LevelInfoSize),
        UyaLevelConstants.SectorSize * 40)];

    WriteUyaLevelInfoEntry(
        iso,
        3,
        audio: new UyaFileBlock(30, 1),
        level: new UyaFileBlock(31, 1),
        scene: new UyaFileBlock(32, 2));

    iso[31 * UyaLevelConstants.SectorSize] = 0x42;

    using var stream = new MemoryStream(iso, writable: false);
    var levelSet = UyaLevelInfoReader.ReadLevelSet(stream, 3);

    Expect(levelSet.RequestedLevelIndex == 3, "UYA requested level index should be preserved");
    Expect(levelSet.RequestedLevel.LevelAudioWad == new UyaFileBlock(30, 1), "UYA audio WAD block should be parsed");
    Expect(levelSet.RequestedLevel.LevelWad == new UyaFileBlock(31, 1), "UYA level WAD block should be parsed");
    Expect(levelSet.RequestedLevel.LevelSceneWad == new UyaFileBlock(32, 2), "UYA scene WAD block should be parsed");

    stream.Position = 0;
    var levelWadBytes = UyaLevelInfoReader.ReadSectorBlock(stream, levelSet.RequestedLevel.LevelWad);
    Expect(levelWadBytes.Length == UyaLevelConstants.SectorSize, "UYA sector block read should return sector-scaled length");
    Expect(levelWadBytes[0] == 0x42, "UYA sector block read should seek to the requested sector");

    stream.Position = 0;
    var levelWadHeaderBytes = UyaLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 1);
    Expect(levelWadHeaderBytes.Length == UyaLevelConstants.SectorSize, "UYA fixed sector header read should ignore fileblock length");
    Expect(levelWadHeaderBytes[0] == 0x42, "UYA fixed sector header read should seek to the requested sector");

    ExpectThrows<ArgumentOutOfRangeException>(() => UyaLevelInfoReader.ReadSectorHeader(stream, levelSet.RequestedLevel.LevelWad, 0));
    ExpectThrows<InvalidDataException>(() => UyaLevelInfoReader.ReadSectorHeader(stream, new UyaFileBlock(int.MaxValue, 1), 1));
    ExpectThrows<ArgumentOutOfRangeException>(() => UyaLevelInfoReader.ReadLevelSet(stream, UyaLevelConstants.LevelInfoCount));
}

static void ValidateGcLevelInfoLookup()
{
    var iso = new byte[GcLevelCatalog.RetailLevelInfoTableOffset
        + ((GcLevelCatalog.Levels.Max(level => level.TableIndex) + 1) * GcLevelCatalog.LevelInfoSize)];
    var museum = GcLevelCatalog.GetById(30);
    var offset = GcLevelCatalog.RetailLevelInfoTableOffset + (museum.TableIndex * GcLevelCatalog.LevelInfoSize);
    WriteInt32(iso, offset + 0x00, 31);
    WriteInt32(iso, offset + 0x04, 2);
    WriteInt32(iso, offset + 0x08, 32);
    WriteInt32(iso, offset + 0x0c, 3);
    WriteInt32(iso, offset + 0x10, 33);
    WriteInt32(iso, offset + 0x14, 4);

    using var stream = new MemoryStream(iso, writable: false);
    var levelInfo = GcLevelInfoReader.ReadLevel(stream, 30);

    Expect(GcLevelCatalog.Levels.Count == 27, "GC level catalog should include every Wrench level");
    Expect(levelInfo.Level.TableIndex == 21, "GC Museum level id 30 should map to table index 21");
    Expect(levelInfo.LevelWad == new GcFileBlock(31, 2), "GC level WAD should be the first table block");
    Expect(levelInfo.LevelAudioWad == new GcFileBlock(32, 3), "GC audio WAD should be the second table block");
    Expect(levelInfo.LevelSceneWad == new GcFileBlock(33, 4), "GC scene WAD should be the third table block");
    ExpectThrows<ArgumentOutOfRangeException>(() => GcLevelCatalog.GetById(21));
}

static void ValidateUyaLevelWadParsing()
{
    var levelWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 0x1234);
    var levelWad = UyaLevelWadReader.ReadLevelWad(levelWadBytes);

    Expect(levelWad.HeaderSize == UyaLevelConstants.LevelWadHeaderSize, "UYA level WAD header size should be parsed");
    Expect(levelWad.Sector == 0x1234, "UYA level WAD sector should be parsed");
    Expect(levelWad.Level == 7, "UYA level WAD level id should be parsed");
    Expect(levelWad.ReverbType == 2, "UYA level WAD reverb should be parsed");
    Expect(levelWad.Data == new UyaFileBlock(3, 2), "UYA level data fileblock should be parsed");
    Expect(levelWad.SoundBank == new UyaFileBlock(1, 1), "UYA sound bank fileblock should be parsed");
    Expect(levelWad.Gameplay == new UyaFileBlock(5, 1), "UYA gameplay fileblock should be parsed");
    Expect(levelWad.Occlusion == new UyaFileBlock(6, 1), "UYA occlusion fileblock should be parsed");
    Expect(levelWad.Chunks[0] == new UyaFileBlock(7, 1), "UYA first chunk fileblock should be parsed");
    Expect(levelWad.ChunkBanks[0] == new UyaFileBlock(8, 1), "UYA first chunk bank fileblock should be parsed");
    Expect(levelWad.HeaderBytes.Length == UyaLevelConstants.LevelWadHeaderSize, "UYA level WAD header bytes should be preserved");

    var soundBank = UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.SoundBank);
    Expect(soundBank.Length == UyaLevelConstants.SectorSize, "UYA sector fileblock should read sector-scaled length");
    Expect(soundBank[0] == 0x41, "UYA sector fileblock should read from the requested sector");

    var levelData = UyaLevelWadReader.ReadLevelDataWad(UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data));
    Expect(levelData.HeaderSize == UyaLevelConstants.LevelDataHeaderSize, "UYA level data fixed header size should be reported");
    Expect(levelData.Overlay == new UyaByteBlock(0x80, 4), "UYA level data overlay byte block should be parsed");
    Expect(levelData.CoreIndex == new UyaByteBlock(0x90, 4), "UYA level data core index byte block should be parsed");
    Expect(levelData.GsRam == new UyaByteBlock(0xa0, 4), "UYA level data GS RAM byte block should be parsed");
    Expect(levelData.HudHeader == new UyaByteBlock(0xb0, 4), "UYA level data HUD header byte block should be parsed");
    Expect(levelData.HudBanks[0] == new UyaByteBlock(0xc0, 4), "UYA level data first HUD bank byte block should be parsed");
    Expect(levelData.CoreData == new UyaByteBlock(0xd0, 4), "UYA level data core payload byte block should be parsed");
    Expect(levelData.TransitionTextures == new UyaByteBlock(0xe0, 4), "UYA level data transition texture byte block should be parsed");

    var code = UyaLevelWadReader.ReadByteFileBlock(UyaLevelWadReader.ReadSectorFileBlock(levelWadBytes, levelWad.Data), levelData.Overlay);
    Expect(code.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA byte fileblock should read exact byte length");
}

static void ValidateUyaLooseLevelWadExtraction()
{
    const int levelIndex = 3;
    const int headerSector = 20;
    const int payloadBaseSector = 60;
    var looseWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector);
    var iso = CreateSyntheticUyaIso(levelIndex, headerSector, payloadBaseSector, looseWadBytes);

    using var stream = new MemoryStream(iso, writable: false);
    var extracted = UyaLooseLevelWadExtractor.ExtractPrimary(stream, levelIndex);

    Expect(extracted.LevelIndex == levelIndex, "UYA loose WAD extraction should preserve requested level index");
    Expect(extracted.HeaderSector == headerSector, "UYA loose WAD extraction should report the header sector");
    Expect(extracted.PayloadBaseSector == payloadBaseSector, "UYA loose WAD extraction should report the payload base sector");
    Expect(extracted.SectorCount == looseWadBytes.Length / UyaLevelConstants.SectorSize, "UYA loose WAD extraction should copy through the last referenced sector");
    Expect(extracted.Bytes.SequenceEqual(looseWadBytes), "UYA loose WAD extraction should preserve referenced WAD bytes in a self-contained layout");
}

static void ValidateUyaDetachedWadExtraction()
{
    const int headerSector = 2;
    const int payloadBaseSector = 8;
    var iso = new byte[11 * UyaLevelConstants.SectorSize];
    var headerOffset = headerSector * UyaLevelConstants.SectorSize;
    WriteInt32(iso, headerOffset, UyaLevelConstants.SectorSize * 2);
    WriteInt32(iso, headerOffset + sizeof(int), payloadBaseSector);
    iso[headerOffset + UyaLevelConstants.SectorSize] = 0x42;
    iso[(payloadBaseSector + 2) * UyaLevelConstants.SectorSize] = 0x73;

    using var stream = new MemoryStream(iso, writable: false);
    var wad = UyaLooseLevelWadExtractor.ExtractDetached(stream, new UyaFileBlock(headerSector, 3));

    Expect(wad.Length == 3 * UyaLevelConstants.SectorSize, "detached WAD extraction should preserve the table size");
    Expect(wad[UyaLevelConstants.SectorSize] == 0x42, "detached WAD extraction should copy the full detached header");
    Expect(wad[2 * UyaLevelConstants.SectorSize] == 0x73, "detached WAD extraction should preserve payload sectors");
}

static void ValidateUyaLooseLevelWadUnpacking()
{
    var looseWadBytes = CreateSyntheticUyaLooseLevelWad(payloadBaseSector: 20);
    var package = UyaLevelWadUnpacker.Unpack(looseWadBytes);
    var files = package.Files.ToDictionary(file => file.Path);

    Expect(files.ContainsKey("level_wad/header.bin"), "UYA loose WAD unpack should include the level WAD header");
    Expect(files["level_wad/level_data.wad"].Bytes[0x80] == 0x11, "UYA loose WAD unpack should include level data bytes");
    Expect(files["level_wad/sound.bnk"].Bytes[0] == 0x41, "UYA loose WAD unpack should include sound bank bytes");
    Expect(files["gameplay/gameplay.bin"].Bytes[0] == UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should include gameplay bytes");
    Expect(files["gameplay/gameplay_core.bin"].Bytes.Length >= UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should decompress gameplay bytes");
    Expect(files["gameplay/core/header.bin"].Bytes.Length == UyaGameplayBlockReader.CoreHeaderSize, "UYA loose WAD unpack should expose the gameplay pointer table");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "UYA loose WAD unpack should split gameplay level settings");
    Expect(files["gameplay/core/directional_lights.bin"].Bytes.SequenceEqual(new byte[] { 0xB1, 0xB2 }), "UYA loose WAD unpack should split gameplay directional lights");
    Expect(files["gameplay/core/us_english_strings.bin"].Bytes.SequenceEqual(new byte[] { 0xD1, 0xD2 }), "UYA loose WAD unpack should name language blocks as strings");
    Expect(files["gameplay/core/splines.bin"].Bytes.SequenceEqual(new byte[] { 0xE1, 0xE2 }), "UYA loose WAD unpack should name path blocks as splines");
    Expect(files["gameplay/core/grind_splines.bin"].Bytes[..2].SequenceEqual(new byte[] { 0xF1, 0xF2 }), "UYA loose WAD unpack should name grind path blocks as grind splines");
    Expect(files["gameplay/core/moby_instances.bin"].Bytes[..2].SequenceEqual(new byte[] { 0xC1, 0xC2 }), "UYA loose WAD unpack should split gameplay moby instances");
    Expect(files["occlusion/occlusion.bin"].Bytes[0] == 0x61, "UYA loose WAD unpack should include occlusion bytes");
    Expect(files["level_wad/chunks/chunk0.wad"].Bytes[0] == 0x71, "UYA loose WAD unpack should include chunk bytes");
    Expect(files["level_wad/chunks/chunk0_bank.wad"].Bytes[0] == 0x81, "UYA loose WAD unpack should include chunk bank bytes");
    Expect(files["code/code.bin"].Bytes.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA loose WAD unpack should expose code payload");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 0x21, 0x22, 0x23, 0x24 }), "UYA loose WAD unpack should expose asset header payload");
    Expect(files["assets/palette.bin"].Bytes.SequenceEqual(new byte[] { 0x31, 0x32, 0x33, 0x34 }), "UYA loose WAD unpack should expose palette payload");
    Expect(files["hud/header.bin"].Bytes.SequenceEqual(new byte[] { 0x41, 0x42, 0x43, 0x44 }), "UYA loose WAD unpack should expose HUD header payload");
    Expect(files["hud/bank0.bin"].Bytes.SequenceEqual(new byte[] { 0x51, 0x52, 0x53, 0x54 }), "UYA loose WAD unpack should expose HUD bank payload");
    Expect(files["assets/asset_wad.bin"].Bytes.SequenceEqual(new byte[] { 0x61, 0x62, 0x63, 0x64 }), "UYA loose WAD unpack should expose asset WAD payload");
    Expect(files["transition_textures/transition_textures.bin"].Bytes.SequenceEqual(new byte[] { 0x71, 0x72, 0x73, 0x74 }), "UYA loose WAD unpack should expose transition texture payload");

    var packed = package.ToPackedPackage();
    Expect(packed.Entries.Count == package.Files.Count, "UYA packed package entry count should match loose file count");
    var packedCodeEntry = packed.Entries.Single(entry => entry.Path == "code/code.bin");
    var packedCodeBytes = packed.PackedBytes.AsSpan(packedCodeEntry.Offset, packedCodeEntry.Length).ToArray();
    Expect(packedCodeBytes.SequenceEqual(files["code/code.bin"].Bytes), "UYA packed package offsets should round-trip entry bytes");
}

static void ValidateUyaStandaloneGameplayUnpacking()
{
    var files = UyaLevelWadUnpacker
        .UnpackGameplay(CreateSyntheticUyaGameplay())
        .ToDictionary(file => file.Path);

    Expect(files["gameplay/gameplay.bin"].Bytes[0] == UyaGameplayBlockReader.CoreHeaderSize, "UYA standalone gameplay unpack should include raw gameplay bytes");
    Expect(files["gameplay/gameplay_core.bin"].Bytes.Length >= UyaGameplayBlockReader.CoreHeaderSize, "UYA standalone gameplay unpack should expose core gameplay bytes");
    Expect(files["gameplay/core/level_settings.bin"].Bytes.SequenceEqual(new byte[] { 0xA1, 0xA2 }), "UYA standalone gameplay unpack should split level settings");
    Expect(files["gameplay/core/splines.bin"].Bytes.SequenceEqual(new byte[] { 0xE1, 0xE2 }), "UYA standalone gameplay unpack should split splines");
}

static void ValidateUyaStandaloneLevelDataUnpacking()
{
    var files = UyaLevelWadUnpacker
        .UnpackLevelData(CreateSyntheticUyaLevelData())
        .ToDictionary(file => file.Path);

    Expect(files["level_wad/level_data.wad"].Bytes[0x80] == 0x11, "UYA standalone level data unpack should include raw level data bytes");
    Expect(files["code/code.bin"].Bytes.SequenceEqual(new byte[] { 0x11, 0x12, 0x13, 0x14 }), "UYA standalone level data unpack should expose code payload");
    Expect(files["assets/asset_header.bin"].Bytes.SequenceEqual(new byte[] { 0x21, 0x22, 0x23, 0x24 }), "UYA standalone level data unpack should expose asset header payload");
    Expect(files["assets/asset_wad.bin"].Bytes.SequenceEqual(new byte[] { 0x61, 0x62, 0x63, 0x64 }), "UYA standalone level data unpack should expose asset WAD payload");
}

static void ValidateUyaCustomMapZipUnpacking()
{
    using var zipStream = new MemoryStream();
    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
    {
        AddZipEntry(archive, "maps/example.wad", CreateSyntheticUyaLevelData());
        AddZipEntry(archive, "maps/example.world", CreateSyntheticUyaGameplay());
    }

    var package = UyaCustomMapZipUnpacker.Unpack(zipStream.ToArray());

    Expect(package.LevelDataWadEntryName == "maps/example.wad", "UYA custom map zip unpack should report the level data entry name");
    Expect(package.WorldEntryName == "maps/example.world", "UYA custom map zip unpack should report the world entry name");
    Expect(package.LevelDataFiles.Any(file => file.Path == "assets/asset_header.bin"), "UYA custom map zip unpack should expose level-data files");
    Expect(package.GameplayFiles.Any(file => file.Path == "gameplay/core/splines.bin"), "UYA custom map zip unpack should expose gameplay files");
    Expect(package.Files.Count == package.LevelDataFiles.Count + package.GameplayFiles.Count, "UYA custom map zip package should combine level-data and gameplay files");
}

static void ValidateUyaGameplayTypedParsing()
{
    var levelSettingsBytes = new byte[UyaLevelSettingsReader.MinimumSize + 2];
    WriteInt32(levelSettingsBytes, 0x00, 57);
    WriteInt32(levelSettingsBytes, 0x04, 65);
    WriteInt32(levelSettingsBytes, 0x08, 50);
    WriteInt32(levelSettingsBytes, 0x0c, 40);
    WriteInt32(levelSettingsBytes, 0x10, 50);
    WriteInt32(levelSettingsBytes, 0x14, 40);
    WriteSingle(levelSettingsBytes, 0x18, 61440);
    WriteSingle(levelSettingsBytes, 0x1c, 179200);
    WriteSingle(levelSettingsBytes, 0x20, 255);
    WriteSingle(levelSettingsBytes, 0x24, 63.75f);
    WriteSingle(levelSettingsBytes, 0x28, -100);
    WriteInt32(levelSettingsBytes, 0x2c, 1);
    WriteSingle(levelSettingsBytes, 0x30, 1);
    WriteSingle(levelSettingsBytes, 0x34, 2);
    WriteSingle(levelSettingsBytes, 0x38, 3);
    WriteSingle(levelSettingsBytes, 0x3c, 20);
    WriteSingle(levelSettingsBytes, 0x40, 21);
    WriteSingle(levelSettingsBytes, 0x44, 22);
    WriteSingle(levelSettingsBytes, 0x48, 0.5f);
    WriteInt32(levelSettingsBytes, 0x4c, -1);
    WriteInt32(levelSettingsBytes, 0x50, 2);
    WriteInt32(levelSettingsBytes, 0x54, 3);
    WriteUInt32(levelSettingsBytes, 0x58, 0x12345678);
    WriteInt32(levelSettingsBytes, 0x7c, 59);
    WriteInt32(levelSettingsBytes, 0x80, 1234);
    levelSettingsBytes[^2] = 0xaa;
    levelSettingsBytes[^1] = 0xbb;

    var mobyBytes = new byte[UyaMobyInstancesReader.HeaderSize + UyaMobyInstancesReader.RecordSize];
    WriteInt32(mobyBytes, 0x00, 1);
    WriteInt32(mobyBytes, 0x04, 400);
    WriteInt32(mobyBytes, 0x08, 8);
    WriteInt32(mobyBytes, 0x0c, 9);

    const int mobyOffset = UyaMobyInstancesReader.HeaderSize;
    WriteInt32(mobyBytes, mobyOffset, UyaMobyInstancesReader.RecordSize);
    WriteInt32(mobyBytes, mobyOffset + 0x04, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x08, 8);
    WriteInt32(mobyBytes, mobyOffset + 0x0c, 12);
    WriteInt32(mobyBytes, mobyOffset + 0x10, 0x78);
    WriteInt32(mobyBytes, mobyOffset + 0x14, 4);
    WriteInt32(mobyBytes, mobyOffset + 0x18, 18);
    WriteInt32(mobyBytes, mobyOffset + 0x1c, 28);
    WriteInt32(mobyBytes, mobyOffset + 0x20, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x24, 36);
    WriteInt32(mobyBytes, mobyOffset + 0x28, 0x107c);
    WriteSingle(mobyBytes, mobyOffset + 0x2c, 1.5f);
    WriteInt32(mobyBytes, mobyOffset + 0x30, 64);
    WriteInt32(mobyBytes, mobyOffset + 0x34, 80);
    WriteInt32(mobyBytes, mobyOffset + 0x38, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x3c, 64);
    WriteSingle(mobyBytes, mobyOffset + 0x40, 10);
    WriteSingle(mobyBytes, mobyOffset + 0x44, 20);
    WriteSingle(mobyBytes, mobyOffset + 0x48, 30);
    WriteSingle(mobyBytes, mobyOffset + 0x4c, 0.25f);
    WriteSingle(mobyBytes, mobyOffset + 0x50, 0.5f);
    WriteSingle(mobyBytes, mobyOffset + 0x54, 0.75f);
    WriteInt32(mobyBytes, mobyOffset + 0x58, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x5c, 1);
    WriteSingle(mobyBytes, mobyOffset + 0x60, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x64, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x68, 10);
    WriteInt32(mobyBytes, mobyOffset + 0x6c, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x70, 0x54);
    WriteInt32(mobyBytes, mobyOffset + 0x74, 86);
    WriteInt32(mobyBytes, mobyOffset + 0x78, 77);
    WriteInt32(mobyBytes, mobyOffset + 0x7c, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x80, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x84, -1);

    var gameplay = UyaGameplayBlockReader.ReadCore(BuildGameplayData(
        UyaGameplayBlockReader.CoreHeaderSize,
        (0x00, levelSettingsBytes),
        (0x4c, mobyBytes),
        (0x58, [0x01, 0x02]),
        (0x5c, [0x00, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]),
        (0x60, [0xde, 0xad, 0xbe, 0xef]),
        (0x64, [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff])));
    var settings = gameplay.Blocks.Single(block => block.SemanticName == "level_settings").LevelSettings;
    var mobyInstances = gameplay.Blocks.Single(block => block.SemanticName == "moby_instances").MobyInstances;

    Expect(settings is not null, "UYA core level_settings block should be parsed into a typed model");
    Expect(settings!.BackgroundColor == new UyaRgb96(57, 65, 50), "UYA level settings background color should be parsed");
    Expect(settings.FogColor == new UyaRgb96(40, 50, 40), "UYA level settings fog color should be parsed");
    Expect(settings.FogFarDistance == 179200, "UYA level settings fog far distance should be parsed");
    Expect(settings.IsSphericalWorld, "UYA level settings spherical world flag should be parsed");
    Expect(settings.SphereCenter == new UyaVector3(1, 2, 3), "UYA level settings sphere center should be parsed");
    Expect(settings.ShipPosition == new UyaVector3(20, 21, 22), "UYA level settings ship position should use DL axis order");
    Expect(settings.ShipPath == -1, "UYA level settings ship path should be parsed");
    Expect(settings.ChunkPlanes.Count == 0, "UYA empty level settings chunk plane terminator should be skipped");
    Expect(settings.CoreSoundsCount == 59, "UYA level settings core sound count should be parsed");
    Expect(settings.Rac3ThirdPart == 1234, "UYA level settings R&C3 tail field should be parsed");
    Expect(settings.TrailingBytes.SequenceEqual(new byte[] { 0xaa, 0xbb }), "UYA level settings trailing bytes should be preserved");

    var gcLevelSettingsBytes = new byte[0xa0];
    WriteInt32(gcLevelSettingsBytes, 0x68, 2);
    WriteInt32(gcLevelSettingsBytes, 0x9c, 59);
    var gcSettings = UyaLevelSettingsReader.Read(gcLevelSettingsBytes);
    Expect(gcSettings.ChunkPlanes.Count == 2, "GC level settings chunk planes should be parsed");
    Expect(gcSettings.CoreSoundsCount == 59 && gcSettings.Rac3ThirdPart == 0, "GC level settings should allow the UYA-only tail field to be absent");

    Expect(mobyInstances is not null, "UYA core moby_instances block should be parsed into a typed model");
    Expect(mobyInstances!.StaticCount == 1, "UYA moby instance static count should be parsed");
    Expect(mobyInstances.SpawnableMobyCount == 400, "UYA moby instance spawnable count should be parsed");
    Expect(mobyInstances.Pad8 == 8 && mobyInstances.PadC == 9, "UYA moby instance header padding should be parsed");

    var moby = mobyInstances.Instances.Single();
    Expect(moby.Size == UyaMobyInstancesReader.RecordSize, "UYA moby instance size field should be parsed");
    Expect(moby.Mission == -1, "UYA moby instance mission should be parsed");
    Expect(moby.Uid == 0x78, "UYA moby instance uid should be parsed");
    Expect(moby.Bolts == 4, "UYA moby instance bolts should be parsed");
    Expect(moby.ClassId == 0x107c, "UYA moby instance class id should be parsed");
    Expect(moby.Scale == 1.5f, "UYA moby instance scale should be parsed");
    Expect(moby.Position == new UyaVector3(10, 20, 30), "UYA moby instance position should use DL axis order");
    Expect(moby.Rotation == new UyaVector3(0.25f, 0.5f, 0.75f), "UYA moby instance rotation should use DL axis order");
    Expect(moby.PvarIndex == 10, "UYA moby instance pvar index should be parsed");
    Expect(moby.Color == new UyaRgb96(86, 77, 2), "UYA moby instance color should be parsed");
    Expect(moby.Unknown84 == -1, "UYA moby instance 0x84 field should be parsed");
    Expect(gameplay.Blocks.Single(block => block.SemanticName == "pvar_data").PayloadBytes.SequenceEqual(new byte[] { 0xde, 0xad, 0xbe, 0xef }), "UYA pvar data payload should be exposed");
}

static void ValidateUyaAssetRenderPackageBuild()
{
    var rawAssetBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    var compressedAssetBytes = CreateLiteralWad(rawAssetBytes);
    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.UYA,
        levelIndex: 41,
        headerBytes: new byte[0xc0],
        paletteBytes: [],
        assetBytes: compressedAssetBytes);
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);

    Expect(byPath.ContainsKey("assets/manifest.json"), "UYA asset render package should include an asset manifest");
    Expect(byPath.ContainsKey("assets/render_manifest.json"), "UYA asset render package should include a render manifest");

    using var assetManifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    Expect(assetManifest.RootElement.GetProperty("Game").GetString() == "UYA", "UYA asset manifest should preserve the game id");
    Expect(!assetManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA asset textures should be exported without swizzle");
    Expect(assetManifest.RootElement.GetProperty("GltfExportCount").GetInt32() == 0, "empty UYA asset package should not report written glTFs");

    using var renderManifest = JsonDocument.Parse(byPath["assets/render_manifest.json"].Bytes);
    Expect(!renderManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA render manifest should report unswizzled asset textures");
    Expect(renderManifest.RootElement.GetProperty("AssetWadWasCompressed").GetBoolean(), "UYA asset render package should decompress compressed asset WAD input");
    Expect(
        renderManifest.RootElement.GetProperty("AssetWadPayloadLength").GetInt32() == rawAssetBytes.Length,
        "UYA asset render package should report the decompressed asset WAD length");
}

static void ValidateChunkTfragAssetRenderPackageWhenAvailable()
{
    var tfragPath = Path.Combine("test-assets", "tfrags", "DL", "level1", "terrain", "terrain.bin");
    if (!File.Exists(tfragPath))
    {
        return;
    }

    var chunkWad = CreateChunkWad(File.ReadAllBytes(tfragPath));
    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes: new byte[0xc0],
        paletteBytes: [],
        assetBytes: [],
        options: DlLevelWadRenderPackageBuildOptions.Browser,
        chunkWads: new Dictionary<int, byte[]>
        {
            [0] = chunkWad,
            [1] = chunkWad
        });
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);

    Expect(!byPath.ContainsKey("assets/tfrag/chunks/chunk0/tfrag.gltf"), "chunk0 tfrag should not be exported");
    Expect(byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.gltf"), "chunk1 tfrag glTF should be exported");
    Expect(byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.buffer.bin"), "chunk1 tfrag buffer should be exported");
    Expect(!byPath.ContainsKey("assets/tfrag/chunks/chunk1/tfrag.bin"), "browser package should omit chunk tfrag source bytes");

    using var assetManifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    var chunkRoute = assetManifest.RootElement
        .GetProperty("GltfExports")
        .EnumerateArray()
        .SingleOrDefault(entry =>
            entry.GetProperty("Family").GetString() == "tfrag"
            && entry.GetProperty("ModelId").ValueKind == JsonValueKind.Number
            && entry.GetProperty("ModelId").GetInt32() == 1);
    Expect(chunkRoute.ValueKind == JsonValueKind.Object, "asset manifest should contain the chunk1 tfrag route");
    Expect(chunkRoute.GetProperty("Status").GetString() == "written", "chunk1 tfrag route should be written");
    Expect(
        chunkRoute.GetProperty("GltfPath").GetString() == "tfrag/chunks/chunk1/tfrag.gltf",
        "chunk1 tfrag route should point at the chunk glTF");
}

static void ValidateChunkTfragWadReaderWhenAvailable()
{
    var chunkPath = Path.Combine(
        "test-assets",
        "extractions_uya",
        "level04_iso_world01",
        "level_wad",
        "chunks",
        "chunk1.wad");
    if (!File.Exists(chunkPath))
    {
        return;
    }

    var terrainBytes = TfragChunkWadReader.ReadTerrainPayload(File.ReadAllBytes(chunkPath));
    var terrain = TfragTerrainReader.Read(terrainBytes);
    Expect(terrain.Chunks.Count > 0, "chunk tfrag WAD reader should decode the first chunk payload as terrain");
}

static void ValidateLooseLevelWadRenderPackageWhenAvailable()
{
    var wadPath = Environment.GetEnvironmentVariable("RATCHET_PS2_DL_LEVEL_WAD")
        ?? "/tmp/ratchet-dl-wad-realdata-44-v2/level44.wad";
    if (!File.Exists(wadPath))
    {
        return;
    }

    var wadBytes = File.ReadAllBytes(wadPath);
    var levelWad = DlLevelWadReader.ReadLevelWad(wadBytes);
    var renderPackage = DlLevelWadRenderPackageBuilder.BuildPacked(
        wadBytes,
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
        DlLevelWadRenderPackageBuildOptions.Browser.IncludeMissionMobys,
        "browser render packages should include mission mobys when present");
    if (levelWad.GameplayMissionData.Any(block => !block.IsEmpty))
    {
        Expect(
            entries.Keys.Any(path => path.StartsWith("missions/", StringComparison.Ordinal)),
            "browser render package should include available mission mobys");
    }
    Expect(
        entries.Keys.All(path =>
            !path.EndsWith("/tie.bin", StringComparison.Ordinal)
            && !path.EndsWith("/moby.bin", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.bin", StringComparison.Ordinal)
            && !path.EndsWith("/tie.json", StringComparison.Ordinal)
            && !path.EndsWith("/moby.json", StringComparison.Ordinal)
            && !path.EndsWith("/shrub.json", StringComparison.Ordinal)),
        "browser render package should omit source moby, tie, and shrub sidecars");

    using var rootManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["manifest.json"]));
    var writtenMobys = rootManifest.RootElement.GetProperty("Mobys").EnumerateArray()
        .Where(entry => entry.GetProperty("Status").GetString() == "written")
        .ToArray();
    Expect(
        writtenMobys.Select(entry => entry.GetProperty("ClassId").GetInt32()).Distinct().Count() == writtenMobys.Length,
        "render package should contain only one written moby per class");
    var performanceTimings = rootManifest.RootElement.GetProperty("PerformanceTimings").EnumerateArray().ToArray();
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.tfrag"),
        "render package manifest should include top-level terrain timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.mobys"),
        "render package manifest should include top-level moby timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.assets.fx-textures"),
        "render package manifest should include FX texture timing");
    Expect(
        performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tfrag.decode"),
        "render package manifest should include terrain exporter subphase timing");
    using (var tfragGltf = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/tfrag/tfrag.gltf"])))
    {
        Expect(
            tfragGltf.RootElement.GetProperty("nodes").EnumerateArray().All(node =>
                !node.TryGetProperty("name", out var name)
                || name.GetString()?.Contains("lod_1", StringComparison.Ordinal) != true
                && name.GetString()?.Contains("lod_2", StringComparison.Ordinal) != true),
            "browser render package terrain glTF should include only LOD0");
    }

    using var assetManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/manifest.json"]));
    var assetHeader = assetManifest.RootElement.GetProperty("Header");
    var gltfExports = assetManifest.RootElement.GetProperty("GltfExports").EnumerateArray().ToArray();
    Expect(
        gltfExports.Any(entry =>
            entry.GetProperty("Family").GetString() == "tfrag"
            && entry.GetProperty("Status").GetString() == "written"),
        "render package asset manifest should contain a written tfrag export");

    if (assetHeader.GetProperty("MobyModelCount").GetInt32() > 0)
    {
        Expect(
            gltfExports.Any(entry =>
                entry.GetProperty("Family").GetString() == "moby"
                && entry.GetProperty("Status").GetString() == "written"),
            "render package asset manifest should contain a written moby export");
        Expect(
            entries.Keys.Any(path =>
                path.StartsWith("assets/moby/", StringComparison.Ordinal)
                && path.EndsWith("/moby.gltf", StringComparison.Ordinal)),
            "render package should include moby glTF files");
        var mobyGltfPath = entries.Keys.First(path =>
            path.StartsWith("assets/moby/", StringComparison.Ordinal)
            && path.EndsWith("/moby.gltf", StringComparison.Ordinal));
        using var mobyGltf = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries[mobyGltfPath]));
        Expect(
            !mobyGltf.RootElement.GetProperty("nodes").EnumerateArray().Any(node =>
                node.TryGetProperty("name", out var name)
                && (name.GetString()?.Contains("low_lod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("far_lod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("mesh_type_2", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("LowLod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("FarLod", StringComparison.Ordinal) == true
                    || name.GetString()?.Contains("MeshType2", StringComparison.Ordinal) == true)),
            "browser render package moby glTF should include only LOD0 render mesh groups");
    }

    var fxTextureCount = assetHeader.GetProperty("FxTextureCount").GetInt32();
    if (fxTextureCount > 0)
    {
        Expect(entries.ContainsKey("assets/fx/manifest.json"), "render package should include the FX texture manifest");
        Expect(
            entries.Keys.Count(path =>
                path.StartsWith("assets/fx/textures/", StringComparison.Ordinal)
                && path.EndsWith(".png", StringComparison.Ordinal)) == fxTextureCount,
            "render package should include one PNG per FX texture");
    }

    if (gltfExports.Any(entry =>
        entry.GetProperty("Family").GetString() == "tie"
        && entry.GetProperty("Status").GetString() == "written"))
    {
        Expect(
            performanceTimings.Any(entry => entry.GetProperty("Key").GetString() == "managed.tie.document"),
            "render package manifest should include aggregated tie document timing when ties are written");
    }
}

static void ValidateUyaLooseLevelWadRenderPackageWhenAvailable()
{
    var wadPath = Environment.GetEnvironmentVariable("RATCHET_PS2_UYA_LEVEL_WAD")
        ?? Path.Combine("test-assets", "extractions_uya", "level41.wad");
    if (!File.Exists(wadPath))
    {
        return;
    }

    var package = UyaLevelWadUnpacker.Unpack(File.ReadAllBytes(wadPath));
    var renderPackage = UyaLevelWadRenderPackageBuilder.BuildPacked(
        package.LevelWad.Level,
        package.Files,
        assetFiles => DlLevelWadRenderPackageBuilder.BuildAssetFiles(
            GameId.UYA,
            package.LevelWad.Level,
            assetFiles.HeaderBytes,
            assetFiles.PaletteBytes,
            assetFiles.AssetWadBytes,
            DlLevelWadRenderPackageBuildOptions.Browser));
    var entries = renderPackage.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

    Expect(entries.ContainsKey("manifest.json"), "UYA render package should include the root viewer manifest");
    Expect(entries.ContainsKey("assets/manifest.json"), "UYA render package should include the asset viewer manifest");
    Expect(entries.ContainsKey("world/manifest.json"), "UYA render package should include the world viewer manifest");
    Expect(entries.ContainsKey("world/lighting/directional_lights.bin"), "UYA render package should expose directional lights through the world path");
    Expect(entries.ContainsKey("world/tie/instances.bin"), "UYA render package should expose tie instances through the world path");
    Expect(entries.ContainsKey("world/shrub/instances.bin"), "UYA render package should expose shrub instances through the world path");

    using var rootManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["manifest.json"]));
    Expect(rootManifest.RootElement.GetProperty("Game").GetString() == "UYA", "UYA render package root manifest should preserve the game id");

    using var assetManifest = JsonDocument.Parse(ReadPackedEntryBytes(renderPackage, entries["assets/manifest.json"]));
    Expect(!assetManifest.RootElement.GetProperty("TextureIsSwizzled").GetBoolean(), "UYA render package should keep asset textures unswizzled");
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

static void ValidateMissionMobyBankParsing()
{
    var pif = PifWriter.Write(PifWriter.CreateIndexed8(
        2,
        2,
        new byte[0x400],
        [0, 1, 2, 3]));
    var bank = new byte[0x50 + pif.Length];
    WriteInt32(bank, 0x00, 1);
    WriteInt32(bank, 0x10, 0x24f9);
    WriteInt32(bank, 0x14, 0x30);
    WriteInt32(bank, 0x18, 0x40);
    for (var i = 0; i < 0x10; i++)
    {
        bank[0x30 + i] = (byte)i;
    }
    WriteInt32(bank, 0x40, 1);
    WriteInt32(bank, 0x44, 0x10);
    pif.CopyTo(bank, 0x50);

    var mission = new byte[0x80 + bank.Length];
    WriteInt32(mission, 0x00, 0x40);
    WriteInt32(mission, 0x08, 0x80);
    WriteInt32(mission, 0x0c, bank.Length);
    bank.CopyTo(mission, 0x80);

    var mobys = DlMissionMobyBankReader.Read(DlMissionDataReader.ReadClasses(mission));
    Expect(mobys.Count == 1, "mission moby bank should read its definition count");
    Expect(mobys[0].Definition.ClassId == 0x24f9, "mission moby bank should read the class id");
    Expect(mobys[0].ModelBytes.Length == 0x10, "mission moby bank should slice model bytes at the texture boundary");
    Expect(mobys[0].PifTextures.Count == 1, "mission moby bank should read embedded PIF textures");
    Expect(mobys[0].PifTextures[0].SequenceEqual(pif), "mission moby bank should preserve the complete PIF payload");
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

static void ValidateGameplayLevelSettingsParsing()
{
    var levelSettingsBytes = new byte[0xb8];
    WriteInt32(levelSettingsBytes, 0x00, 57);
    WriteInt32(levelSettingsBytes, 0x04, 65);
    WriteInt32(levelSettingsBytes, 0x08, 50);
    WriteInt32(levelSettingsBytes, 0x0c, 40);
    WriteInt32(levelSettingsBytes, 0x10, 50);
    WriteInt32(levelSettingsBytes, 0x14, 40);
    WriteSingle(levelSettingsBytes, 0x18, 61440);
    WriteSingle(levelSettingsBytes, 0x1c, 179200);
    WriteSingle(levelSettingsBytes, 0x20, 255);
    WriteSingle(levelSettingsBytes, 0x24, 63.75f);
    WriteSingle(levelSettingsBytes, 0x3c, 20);
    WriteSingle(levelSettingsBytes, 0x40, 20);
    WriteSingle(levelSettingsBytes, 0x44, 20);
    WriteInt32(levelSettingsBytes, 0x4c, -1);
    WriteInt32(levelSettingsBytes, 0x7c, 59);

    var gameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x00, levelSettingsBytes),
        (0x08, [0xaa]),
        (0x0c, [0xbb]),
        (0x5c, [0xcc]),
        (0x60, [0xdd])));
    var settings = gameplay.Blocks.Single(block => block.SemanticName == "level_settings").LevelSettings;

    Expect(settings is not null, "core level_settings block should be parsed into a typed model");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "ambient_sound_instances"), "gameplay sound instances should use the ambient sound name");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "us_english_strings"), "gameplay language string blocks should use string names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "splines"), "gameplay path blocks should use spline names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "grind_splines"), "gameplay grind path blocks should use grind spline names");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "pad_78"), "gameplay 0x78 slot should be named padding");
    Expect(gameplay.Blocks.Any(block => block.SemanticName == "pad_7c"), "gameplay 0x7c slot should be named padding");
    Expect(settings!.BackgroundColor == new DlRgb96(57, 65, 50), "level settings background color should be parsed");
    Expect(settings.FogColor == new DlRgb96(40, 50, 40), "level settings fog color should be parsed");
    Expect(settings.FogFarDistance == 179200, "level settings fog far distance should be parsed");
    Expect(settings.ShipPosition == new DlVector3(20, 20, 20), "level settings ship position should be parsed");
    Expect(settings.ShipPath == -1, "level settings ship path should be parsed");
    Expect(settings.ChunkPlanes.Count == 0, "empty level settings chunk plane terminator should be skipped");
    Expect(settings.CoreSoundsCount == 59, "level settings core sound count should be parsed");
    Expect(settings.ThirdPartCount == 0, "level settings DL third part count should be parsed");
    Expect(settings.FifthPart is not null, "level settings DL fifth part should be parsed");
    Expect(settings.DebugAttackDamage.Length == 0, "empty level settings debug attack damage array should be parsed");
}

static void ValidateGameplayMobyInstancesParsing()
{
    var mobyBytes = new byte[DlMobyInstancesReader.HeaderSize + DlMobyInstancesReader.RecordSize];
    WriteInt32(mobyBytes, 0x00, 1);
    WriteInt32(mobyBytes, 0x04, 400);

    const int mobyOffset = DlMobyInstancesReader.HeaderSize;
    WriteInt32(mobyBytes, mobyOffset, DlMobyInstancesReader.RecordSize);
    WriteInt32(mobyBytes, mobyOffset + 0x04, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x08, 0x78);
    WriteInt32(mobyBytes, mobyOffset + 0x0c, 4);
    WriteInt32(mobyBytes, mobyOffset + 0x10, 0x0b37);
    WriteSingle(mobyBytes, mobyOffset + 0x14, 1.5f);
    WriteInt32(mobyBytes, mobyOffset + 0x18, 64);
    WriteInt32(mobyBytes, mobyOffset + 0x1c, 80);
    WriteInt32(mobyBytes, mobyOffset + 0x20, 32);
    WriteInt32(mobyBytes, mobyOffset + 0x24, 64);
    WriteSingle(mobyBytes, mobyOffset + 0x28, 10);
    WriteSingle(mobyBytes, mobyOffset + 0x2c, 20);
    WriteSingle(mobyBytes, mobyOffset + 0x30, 30);
    WriteSingle(mobyBytes, mobyOffset + 0x34, 0.25f);
    WriteSingle(mobyBytes, mobyOffset + 0x38, 0.5f);
    WriteSingle(mobyBytes, mobyOffset + 0x3c, 0.75f);
    WriteInt32(mobyBytes, mobyOffset + 0x40, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x44, 1);
    WriteSingle(mobyBytes, mobyOffset + 0x48, -1);
    WriteInt32(mobyBytes, mobyOffset + 0x4c, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x50, 10);
    WriteInt32(mobyBytes, mobyOffset + 0x54, 1);
    WriteInt32(mobyBytes, mobyOffset + 0x58, 0x54);
    WriteInt32(mobyBytes, mobyOffset + 0x5c, 86);
    WriteInt32(mobyBytes, mobyOffset + 0x60, 77);
    WriteInt32(mobyBytes, mobyOffset + 0x64, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x68, 2);
    WriteInt32(mobyBytes, mobyOffset + 0x6c, -1);

    var gameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x30, mobyBytes)));
    var mobyInstances = gameplay.Blocks.Single(block => block.SemanticName == "moby_instances").MobyInstances;

    Expect(mobyInstances is not null, "core moby_instances block should be parsed into a typed model");
    Expect(mobyInstances!.StaticCount == 1, "moby instance static count should be parsed");
    Expect(mobyInstances.SpawnableMobyCount == 400, "moby instance spawnable count should be parsed");
    Expect(mobyInstances.TrailingBytes.Length == 0, "moby instance fixed records should consume the payload");

    var moby = mobyInstances.Instances.Single();
    Expect(moby.Size == DlMobyInstancesReader.RecordSize, "moby instance size field should be parsed");
    Expect(moby.Mission == -1, "moby instance mission should be parsed");
    Expect(moby.Uid == 0x78, "moby instance uid should be parsed");
    Expect(moby.Bolts == 4, "moby instance bolts should be parsed");
    Expect(moby.ClassId == 0x0b37, "moby instance class id should be parsed");
    Expect(moby.Scale == 1.5f, "moby instance scale should be parsed");
    Expect(moby.DrawDistance == 64, "moby instance draw distance should be parsed");
    Expect(moby.UpdateDistance == 80, "moby instance update distance should be parsed");
    Expect(moby.Unused20 == 32, "moby instance unused 0x20 sentinel should be parsed");
    Expect(moby.Unused24 == 64, "moby instance unused 0x24 sentinel should be parsed");
    Expect(moby.Position == new DlVector3(10, 20, 30), "moby instance position should be parsed");
    Expect(moby.Rotation == new DlVector3(0.25f, 0.5f, 0.75f), "moby instance rotation should be parsed");
    Expect(moby.Group == -1, "moby instance group should be parsed");
    Expect(moby.IsRooted == 1, "moby instance rooted flag should be parsed");
    Expect(moby.RootedDistance == -1, "moby instance rooted distance should be parsed");
    Expect(moby.Unused4C == 1, "moby instance unused 0x4c sentinel should be parsed");
    Expect(moby.PvarIndex == 10, "moby instance pvar index should be parsed");
    Expect(moby.Occlusion == 1, "moby instance occlusion should be parsed");
    Expect(moby.ModeBits == 0x54, "moby instance mode bits should be parsed");
    Expect(moby.Color == new DlRgb96(86, 77, 2), "moby instance color should be parsed");
    Expect(moby.Light == 2, "moby instance light should be parsed");
    Expect(moby.Unused6C == -1, "moby instance unused 0x6c sentinel should be parsed");

    var truncatedMobyBytes = new byte[0xf0];
    WriteInt32(truncatedMobyBytes, 0x00, 7);
    var truncatedGameplay = DlGameplayBlockReader.ReadCore(BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x30, truncatedMobyBytes)));
    var truncatedMobyInstances = truncatedGameplay.Blocks
        .Single(block => block.SemanticName == "moby_instances")
        .MobyInstances;

    Expect(truncatedMobyInstances is null, "short moby instance payloads should not fail gameplay parsing");
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

    var headerBytes = new byte[0xc0];
    WriteInt32(headerBytes, 0x10, 0x100);
    WriteInt32(headerBytes, 0x14, 0x1000);
    WriteInt32(headerBytes, 0x78, 0x200);
    var header = DlAssetReader.ReadHeader(headerBytes);
    var gcOffsets = DlAssetReader.CollectKnownAssetOffsets(GameId.GC, header, 0x2000, [], [], []);
    var dlOffsets = DlAssetReader.CollectKnownAssetOffsets(GameId.DL, header, 0x2000, [], [], []);
    Expect(!gcOffsets.Contains(0x200), "GC ratchet sequence table pointers should not truncate asset slices");
    Expect(dlOffsets.Contains(0x200), "DL light cuboid offsets should remain asset slice boundaries");
}

static void ValidateMobyGsStashTextures()
{
    const int classId = 0x251c;
    var headerBytes = new byte[0x100];
    WriteInt32(headerBytes, 0x18, 1);
    WriteInt32(headerBytes, 0x1c, 0xc0);
    WriteInt32(headerBytes, 0x38, 1);
    WriteInt32(headerBytes, 0x3c, 0xe0);
    WriteInt32(headerBytes, 0xac, 0xf0);
    WriteInt32(headerBytes, 0xc0, 0x200);
    WriteInt32(headerBytes, 0xc4, classId);
    headerBytes.AsSpan(0xd0, 0x10).Fill(0xff);
    headerBytes[0xd0] = 0;
    WriteInt32(headerBytes, 0xe0, 0x100);
    WriteInt16(headerBytes, 0xe4, 16);
    WriteInt16(headerBytes, 0xe6, 16);
    WriteInt16(headerBytes, 0xe8, 1);
    WriteInt16(headerBytes, 0xea, 0);
    WriteInt16(headerBytes, 0xec, -1);
    WriteInt16(headerBytes, 0xee, -1);
    WriteInt16(headerBytes, 0xf0, classId);
    WriteInt16(headerBytes, 0xf2, -1);

    var classIds = DlAssetReader.ReadMobyGsStashClassIds(headerBytes, 0xf0);
    Expect(classIds.SequenceEqual([classId]), "moby GS stash class ids should be read through the -1 terminator");

    var palette = CreatePalette();
    var assetBytes = new byte[0x201];
    for (var i = 0; i < 0x100; i++)
    {
        assetBytes[0x100 + i] = (byte)i;
    }
    assetBytes[0x200] = 1;

    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes,
        palette,
        assetBytes);
    var exportedPng = files.Single(file =>
        file.Path == "assets/moby/09500_251C/textures/tex.0000.png").Bytes;
    var definition = DlAssetReader.ReadTextureDefinitions(headerBytes, 0xe0, 1).Single();
    var unswizzledPng = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetBytes,
        textureDataOffset: 0,
        isSwizzled: false).PngBytes;
    var swizzledPng = DlAssetReader.BuildAssetTexture(
        "moby",
        0,
        definition,
        palette,
        assetBytes,
        textureDataOffset: 0,
        isSwizzled: true).PngBytes;

    Expect(exportedPng.SequenceEqual(unswizzledPng), "GS-stashed moby textures should be exported without swizzle");
    Expect(!exportedPng.SequenceEqual(swizzledPng), "GS-stashed moby textures should not use the normal DL swizzle");
}

static void ValidateEnvironmentTextureRenderPackage()
{
    var headerBytes = new byte[0xe0];
    WriteInt32(headerBytes, 0x04, 0xc0);
    WriteInt32(headerBytes, 0x84, 2);
    WriteInt32(headerBytes, 0x90, 0);
    WriteInt32(headerBytes, 0x94, 0);
    WriteInt32(headerBytes, 0x98, 0x222);
    WriteInt32(headerBytes, 0x9c, 0x400);

    WriteInt16(headerBytes, 0xc4, 2);
    WriteInt16(headerBytes, 0xc6, 2);
    WriteInt32(headerBytes, 0xc8, 0x800);
    WriteInt32(headerBytes, 0xcc, 0);
    WriteInt16(headerBytes, 0xd4, 2);
    WriteInt16(headerBytes, 0xd6, 2);
    WriteInt32(headerBytes, 0xd8, 0x804);
    WriteInt32(headerBytes, 0xdc, 0x222);

    var paletteBytes = new byte[0x808];
    CreatePalette().CopyTo(paletteBytes, 0);
    CreatePalette().CopyTo(paletteBytes, 0x400);
    new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.CopyTo(paletteBytes, 0x800);

    var files = DlLevelWadRenderPackageBuilder.BuildAssetFiles(
        GameId.DL,
        levelIndex: 1,
        headerBytes,
        paletteBytes,
        assetBytes: []);
    var byPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
    Expect(byPath.ContainsKey("assets/environment/chrome.png"), "render package should export the level chrome texture");
    Expect(byPath.ContainsKey("assets/environment/glass.png"), "render package should export the level glass texture");

    using var manifest = JsonDocument.Parse(byPath["assets/manifest.json"].Bytes);
    var textures = manifest.RootElement.GetProperty("EnvironmentTextures");
    Expect(textures.GetProperty("chrome").GetString() == "environment/chrome.png", "asset manifest should locate chrome");
    Expect(textures.GetProperty("glass").GetString() == "environment/glass.png", "asset manifest should locate glass");
}

static void ValidateDzoGlbExportWhenAvailable()
{
    var fixtureRoot = Path.Combine("test-assets", "DL Mobys", "09500_251C");
    var modelPath = Path.Combine(fixtureRoot, "moby.bin");
    var texturePath = Path.Combine(fixtureRoot, "tex.0000.0.png");
    if (!File.Exists(modelPath) || !File.Exists(texturePath))
    {
        return;
    }

    var textureBytes = File.ReadAllBytes(texturePath);
    var glb = DlDzoMobyExporter.ExportMoby(
        File.ReadAllBytes(modelPath),
        [textureBytes]);
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb) == 0x46546c67, "DZO export should write the GLB magic");
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)) == 2, "DZO export should write GLB version 2");
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8)) == glb.Length, "DZO GLB length should match its header");

    var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12)));
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)) == 0x4e4f534a, "DZO GLB should start with a JSON chunk");
    using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
    var root = json.RootElement;
    Expect(!root.GetProperty("buffers")[0].TryGetProperty("uri", out _), "DZO GLB buffer should be embedded");
    var image = root.GetProperty("images")[0];
    Expect(!image.TryGetProperty("uri", out _), "DZO GLB image should be embedded");
    Expect(image.GetProperty("mimeType").GetString() == "image/png", "DZO GLB image should retain its PNG media type");

    var binHeaderOffset = checked(20 + jsonLength);
    Expect(BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binHeaderOffset + 4)) == 0x004e4942, "DZO GLB should contain a BIN chunk");
    var imageView = root.GetProperty("bufferViews")[image.GetProperty("bufferView").GetInt32()];
    var imageOffset = checked(binHeaderOffset + 8 + imageView.GetProperty("byteOffset").GetInt32());
    Expect(
        glb.AsSpan(imageOffset, 8).SequenceEqual(textureBytes.AsSpan(0, 8)),
        "DZO GLB should contain the source PNG bytes in its BIN chunk");
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
    var missionGameplay = BuildGameplayData(
        DlGameplayBlockReader.MissionHeaderSize,
        (0x00, new byte[] { 0xA1, 0xA2 }),
        (0x04, new byte[] { 0xA3, 0xA4 }));
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x04, missionGameplay.Length);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x08, 0x40 + missionGameplay.Length);
    WriteInt32(data, (7 * DlLevelConstants.SectorSize) + 0x0c, 4);
    missionGameplay.CopyTo(mission[0x40..]);
    mission[0x40 + missionGameplay.Length] = 0xB1;
    mission[0x41 + missionGameplay.Length] = 0xB2;
    mission[0x42 + missionGameplay.Length] = 0xB3;
    mission[0x43 + missionGameplay.Length] = 0xB4;

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
    var gameplay = BuildGameplayData(
        DlGameplayBlockReader.CoreHeaderSize,
        (0x00, new byte[] { 0xC1, 0xC2, 0xC3 }),
        (0x04, new byte[] { 0xD1, 0xD2 }));
    var data = new byte[DlLevelConstants.SectorSize * 2];
    WriteFileBlock(data, 0x10, new DlFileBlock(0x100, 4));
    WriteFileBlock(data, 0x58, new DlFileBlock(0x180, world.Length));
    WriteFileBlock(data, 0x60, new DlFileBlock(0x200, gameplay.Length));
    data[0x100] = 1;
    data[0x101] = 2;
    data[0x102] = 3;
    data[0x103] = 4;
    world.CopyTo(data.AsSpan(0x180));
    gameplay.CopyTo(data.AsSpan(0x200));
    return data;
}

static byte[] CreateSyntheticUyaIso(
    int levelIndex,
    int headerSector,
    int payloadBaseSector,
    byte[] looseWadBytes,
    bool includePayloads = true)
{
    var iso = new byte[Math.Max(
        UyaLevelConstants.RetailLevelInfoTableOffset + (UyaLevelConstants.LevelInfoCount * UyaLevelConstants.LevelInfoSize),
        ((includePayloads ? payloadBaseSector : headerSector) * UyaLevelConstants.SectorSize)
            + (includePayloads ? looseWadBytes.Length : UyaLevelConstants.LevelWadHeaderSectorCount * UyaLevelConstants.SectorSize))];

    WriteUyaLevelInfoEntry(
        iso,
        levelIndex,
        audio: new UyaFileBlock(0, 0),
        level: new UyaFileBlock(headerSector, 1),
        scene: new UyaFileBlock(0, 0));

    var headerLength = UyaLevelConstants.LevelWadHeaderSectorCount * UyaLevelConstants.SectorSize;
    looseWadBytes.AsSpan(0, headerLength).CopyTo(iso.AsSpan(headerSector * UyaLevelConstants.SectorSize));
    if (includePayloads)
    {
        looseWadBytes.CopyTo(iso.AsSpan(payloadBaseSector * UyaLevelConstants.SectorSize));
    }

    return iso;
}

static byte[] CreateSyntheticUyaLooseLevelWad(int payloadBaseSector)
{
    var data = new byte[UyaLevelConstants.SectorSize * 9];
    WriteInt32(data, 0x00, UyaLevelConstants.LevelWadHeaderSize);
    WriteInt32(data, 0x04, payloadBaseSector);
    WriteInt32(data, 0x08, 7);
    WriteInt32(data, 0x0c, 2);
    WriteUyaFileBlock(data, 0x10, new UyaFileBlock(3, 2));
    WriteUyaFileBlock(data, 0x18, new UyaFileBlock(1, 1));
    WriteUyaFileBlock(data, 0x20, new UyaFileBlock(5, 1));
    WriteUyaFileBlock(data, 0x28, new UyaFileBlock(6, 1));
    WriteUyaFileBlock(data, 0x30, new UyaFileBlock(7, 1));
    WriteUyaFileBlock(data, 0x48, new UyaFileBlock(8, 1));

    CreateSyntheticUyaLevelData().CopyTo(data.AsSpan(3 * UyaLevelConstants.SectorSize));
    data[1 * UyaLevelConstants.SectorSize] = 0x41;
    CreateSyntheticUyaGameplay().CopyTo(data.AsSpan(5 * UyaLevelConstants.SectorSize));
    data[6 * UyaLevelConstants.SectorSize] = 0x61;
    data[7 * UyaLevelConstants.SectorSize] = 0x71;
    data[8 * UyaLevelConstants.SectorSize] = 0x81;

    return data;
}

static byte[] CreateSyntheticUyaGameplay()
{
    return BuildGameplayData(
        UyaGameplayBlockReader.CoreHeaderSize,
        (0x00, [0xA1, 0xA2]),
        (0x04, [0xB1, 0xB2]),
        (0x10, [0xD1, 0xD2]),
        (0x4c, [0xC1, 0xC2]),
        (0x78, [0xE1, 0xE2]),
        (0x7c, [0xF1, 0xF2]));
}

static byte[] CreateSyntheticUyaLevelData()
{
    var data = new byte[UyaLevelConstants.SectorSize * 2];
    WriteByteBlock(data, 0x00, new UyaByteBlock(0x80, 4));
    WriteByteBlock(data, 0x08, new UyaByteBlock(0x90, 4));
    WriteByteBlock(data, 0x10, new UyaByteBlock(0xa0, 4));
    WriteByteBlock(data, 0x18, new UyaByteBlock(0xb0, 4));
    WriteByteBlock(data, 0x20, new UyaByteBlock(0xc0, 4));
    WriteByteBlock(data, 0x48, new UyaByteBlock(0xd0, 4));
    WriteByteBlock(data, 0x50, new UyaByteBlock(0xe0, 4));

    new byte[] { 0x11, 0x12, 0x13, 0x14 }.CopyTo(data.AsSpan(0x80));
    new byte[] { 0x21, 0x22, 0x23, 0x24 }.CopyTo(data.AsSpan(0x90));
    new byte[] { 0x31, 0x32, 0x33, 0x34 }.CopyTo(data.AsSpan(0xa0));
    new byte[] { 0x41, 0x42, 0x43, 0x44 }.CopyTo(data.AsSpan(0xb0));
    new byte[] { 0x51, 0x52, 0x53, 0x54 }.CopyTo(data.AsSpan(0xc0));
    new byte[] { 0x61, 0x62, 0x63, 0x64 }.CopyTo(data.AsSpan(0xd0));
    new byte[] { 0x71, 0x72, 0x73, 0x74 }.CopyTo(data.AsSpan(0xe0));

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

static void WriteUyaLevelInfoEntry(byte[] data, int levelIndex, UyaFileBlock audio, UyaFileBlock level, UyaFileBlock scene)
{
    var offset = UyaLevelConstants.RetailLevelInfoTableOffset + (levelIndex * UyaLevelConstants.LevelInfoSize);
    WriteUyaFileBlock(data, offset + 0x00, audio);
    WriteUyaFileBlock(data, offset + 0x08, level);
    WriteUyaFileBlock(data, offset + 0x10, scene);
}

static void WriteFileBlock(byte[] data, int offset, DlFileBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteUyaFileBlock(byte[] data, int offset, UyaFileBlock block)
{
    WriteInt32(data, offset, block.Offset);
    WriteInt32(data, offset + 4, block.Length);
}

static void WriteByteBlock(byte[] data, int offset, UyaByteBlock block)
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

static byte[] BuildGameplayData(int headerSize, params (int HeaderOffset, byte[] Payload)[] blocks)
{
    var length = headerSize + blocks.Sum(block => block.Payload.Length);
    var data = new byte[length];
    var offset = headerSize;

    foreach (var block in blocks)
    {
        WriteInt32(data, block.HeaderOffset, offset);
        block.Payload.CopyTo(data.AsSpan(offset));
        offset += block.Payload.Length;
    }

    return data;
}

static byte[] CreateLiteralWad(byte[] payload)
{
    var data = new byte[0x10 + 1 + payload.Length];
    data[0] = 0x57;
    data[1] = 0x41;
    data[2] = 0x44;
    WriteInt32(data, 3, data.Length);
    data[0x10] = (byte)(payload.Length - 3);
    payload.CopyTo(data.AsSpan(0x11));
    return data;
}

static byte[] CreateChunkWad(byte[] terrainPayload)
{
    var data = new byte[0x10 + terrainPayload.Length];
    WriteInt32(data, 0x00, 0x10);
    terrainPayload.CopyTo(data.AsSpan(0x10));
    return data;
}

static void AddZipEntry(ZipArchive archive, string path, byte[] bytes)
{
    var entry = archive.CreateEntry(path);
    using var output = entry.Open();
    output.Write(bytes);
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

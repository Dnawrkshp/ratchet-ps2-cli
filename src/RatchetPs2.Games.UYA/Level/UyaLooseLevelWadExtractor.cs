namespace RatchetPs2.Games.UYA.Level;

public static class UyaLooseLevelWadExtractor
{
    public static UyaLooseLevelWad ExtractPrimary(Stream isoStream, int levelIndex)
    {
        ArgumentNullException.ThrowIfNull(isoStream);

        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The provided ISO stream must be readable and seekable.", nameof(isoStream));
        }

        var levelInfo = UyaLevelInfoReader.ReadLevelSet(isoStream, levelIndex);
        var headerSector = levelInfo.RequestedLevel.LevelWad.Offset;
        var headerBytes = UyaLevelInfoReader.ReadSectorHeader(
            isoStream,
            levelInfo.RequestedLevel.LevelWad,
            UyaLevelConstants.LevelWadHeaderSectorCount);

        var levelWad = UyaLevelWadReader.ReadLevelWad(headerBytes);
        if (levelWad.Sector < 0)
        {
            throw new InvalidDataException("UYA primary level WAD payload base sector cannot be negative.");
        }

        var sectorCount = CalculatePrimarySectorCount(levelWad);
        var bytes = new byte[checked(sectorCount * UyaLevelConstants.SectorSize)];
        headerBytes.CopyTo(bytes.AsSpan());
        CopyPrimaryPayloads(isoStream, levelWad, bytes);

        return new UyaLooseLevelWad(
            levelIndex,
            headerSector,
            levelWad.Sector,
            sectorCount,
            levelInfo,
            levelWad,
            bytes);
    }

    private static int CalculatePrimarySectorCount(UyaLevelWad levelWad)
    {
        var sectorCount = AlignToSectorCount(levelWad.HeaderSize);

        AddSectorBlock(ref sectorCount, levelWad.Data, nameof(levelWad.Data));
        AddSectorBlock(ref sectorCount, levelWad.SoundBank, nameof(levelWad.SoundBank));
        AddSectorBlock(ref sectorCount, levelWad.Gameplay, nameof(levelWad.Gameplay));
        AddSectorBlock(ref sectorCount, levelWad.Occlusion, nameof(levelWad.Occlusion));
        AddSectorBlocks(ref sectorCount, levelWad.Chunks, nameof(levelWad.Chunks));
        AddSectorBlocks(ref sectorCount, levelWad.ChunkBanks, nameof(levelWad.ChunkBanks));

        return sectorCount;
    }

    private static void CopyPrimaryPayloads(Stream isoStream, UyaLevelWad levelWad, byte[] destination)
    {
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.Data, nameof(levelWad.Data), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.SoundBank, nameof(levelWad.SoundBank), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.Gameplay, nameof(levelWad.Gameplay), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.Occlusion, nameof(levelWad.Occlusion), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.Chunks, nameof(levelWad.Chunks), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.ChunkBanks, nameof(levelWad.ChunkBanks), destination);
    }

    private static void CopySectorBlocks(
        Stream isoStream,
        int payloadBaseSector,
        IReadOnlyList<UyaFileBlock> blocks,
        string name,
        byte[] destination)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            CopySectorBlock(isoStream, payloadBaseSector, blocks[i], $"{name}[{i}]", destination);
        }
    }

    private static void CopySectorBlock(
        Stream isoStream,
        int payloadBaseSector,
        UyaFileBlock block,
        string name,
        byte[] destination)
    {
        if (block.IsEmpty)
        {
            return;
        }

        var destinationOffset = checked((long)block.Offset * UyaLevelConstants.SectorSize);
        var destinationLength = checked((long)block.Length * UyaLevelConstants.SectorSize);
        if (destinationOffset < AlignToSectorCount(UyaLevelConstants.LevelWadHeaderSize) * UyaLevelConstants.SectorSize)
        {
            throw new InvalidDataException($"{name} overlaps the UYA level WAD header.");
        }

        if (destinationOffset + destinationLength > destination.Length)
        {
            throw new InvalidDataException($"{name} exceeds the extracted UYA loose WAD length.");
        }

        var bytes = UyaLevelInfoReader.ReadSectorRelativeBlock(isoStream, payloadBaseSector, block);
        bytes.CopyTo(destination.AsSpan((int)destinationOffset));
    }

    private static int AlignToSectorCount(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new InvalidDataException("UYA byte ranges cannot have a negative length.");
        }

        return checked((byteLength + UyaLevelConstants.SectorSize - 1) / UyaLevelConstants.SectorSize);
    }

    private static void AddSectorBlocks(ref int sectorCount, IReadOnlyList<UyaFileBlock> blocks, string name)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            AddSectorBlock(ref sectorCount, blocks[i], $"{name}[{i}]");
        }
    }

    private static void AddSectorBlock(ref int sectorCount, UyaFileBlock block, string name)
    {
        if (block.Offset < 0 || block.Length < 0)
        {
            throw new InvalidDataException($"{name} cannot contain negative sector values.");
        }

        if (block.IsEmpty)
        {
            return;
        }

        sectorCount = Math.Max(sectorCount, checked(block.Offset + block.Length));
    }
}

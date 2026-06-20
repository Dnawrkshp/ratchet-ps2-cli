namespace RatchetPs2.Games.DL.Level;

public static class DlLooseLevelWadExtractor
{
    public static DlLooseLevelWad ExtractPrimary(Stream isoStream, int levelIndex)
    {
        ArgumentNullException.ThrowIfNull(isoStream);

        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The provided ISO stream must be readable and seekable.", nameof(isoStream));
        }

        var levelInfo = DlLevelInfoReader.ReadLevelSet(isoStream, levelIndex);
        var headerSector = levelInfo.RequestedLevel.LevelWad.Offset;
        var headerBytes = DlLevelInfoReader.ReadSectorHeader(
            isoStream,
            levelInfo.RequestedLevel.LevelWad,
            DlLevelConstants.LevelWadHeaderSectorCount);

        var levelWad = DlLevelWadReader.ReadLevelWad(headerBytes);
        if (levelWad.Sector < 0)
        {
            throw new InvalidDataException("DL primary level WAD payload base sector cannot be negative.");
        }

        var sectorCount = CalculatePrimarySectorCount(levelWad);
        var bytes = new byte[checked(sectorCount * DlLevelConstants.SectorSize)];
        headerBytes.CopyTo(bytes.AsSpan());
        CopyPrimaryPayloads(isoStream, levelWad, bytes);

        return new DlLooseLevelWad(
            levelIndex,
            headerSector,
            levelWad.Sector,
            sectorCount,
            levelInfo,
            levelWad,
            bytes);
    }

    private static int CalculatePrimarySectorCount(DlLevelWad levelWad)
    {
        var sectorCount = AlignToSectorCount(levelWad.HeaderSize);

        AddSectorBlock(ref sectorCount, levelWad.Data, nameof(levelWad.Data));
        AddSectorBlock(ref sectorCount, levelWad.CoreBank, nameof(levelWad.CoreBank));
        AddSectorBlocks(ref sectorCount, levelWad.Chunks, nameof(levelWad.Chunks));
        AddSectorBlocks(ref sectorCount, levelWad.ChunkBanks, nameof(levelWad.ChunkBanks));
        AddSectorBlock(ref sectorCount, levelWad.GameplayCore, nameof(levelWad.GameplayCore));
        AddSectorBlocks(ref sectorCount, levelWad.GameplayMissionInstances, nameof(levelWad.GameplayMissionInstances));
        AddSectorBlocks(ref sectorCount, levelWad.GameplayMissionData, nameof(levelWad.GameplayMissionData));
        AddSectorBlocks(ref sectorCount, levelWad.MissionBanks, nameof(levelWad.MissionBanks));
        AddSectorBlock(ref sectorCount, levelWad.ArtInstances, nameof(levelWad.ArtInstances));

        return sectorCount;
    }

    private static void CopyPrimaryPayloads(Stream isoStream, DlLevelWad levelWad, byte[] destination)
    {
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.Data, nameof(levelWad.Data), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.CoreBank, nameof(levelWad.CoreBank), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.Chunks, nameof(levelWad.Chunks), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.ChunkBanks, nameof(levelWad.ChunkBanks), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.GameplayCore, nameof(levelWad.GameplayCore), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.GameplayMissionInstances, nameof(levelWad.GameplayMissionInstances), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.GameplayMissionData, nameof(levelWad.GameplayMissionData), destination);
        CopySectorBlocks(isoStream, levelWad.Sector, levelWad.MissionBanks, nameof(levelWad.MissionBanks), destination);
        CopySectorBlock(isoStream, levelWad.Sector, levelWad.ArtInstances, nameof(levelWad.ArtInstances), destination);
    }

    private static void CopySectorBlocks(
        Stream isoStream,
        int payloadBaseSector,
        IReadOnlyList<DlFileBlock> blocks,
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
        DlFileBlock block,
        string name,
        byte[] destination)
    {
        if (block.IsEmpty)
        {
            return;
        }

        var destinationOffset = checked((long)block.Offset * DlLevelConstants.SectorSize);
        var destinationLength = checked((long)block.Length * DlLevelConstants.SectorSize);
        if (destinationOffset < AlignToSectorCount(DlLevelConstants.LevelWadHeaderSize) * DlLevelConstants.SectorSize)
        {
            throw new InvalidDataException($"{name} overlaps the DL level WAD header.");
        }

        if (destinationOffset + destinationLength > destination.Length)
        {
            throw new InvalidDataException($"{name} exceeds the extracted DL loose WAD length.");
        }

        var bytes = DlLevelInfoReader.ReadSectorRelativeBlock(isoStream, payloadBaseSector, block);
        bytes.CopyTo(destination.AsSpan((int)destinationOffset));
    }

    private static int AlignToSectorCount(int byteLength)
    {
        if (byteLength < 0)
        {
            throw new InvalidDataException("DL byte ranges cannot have a negative length.");
        }

        return checked((byteLength + DlLevelConstants.SectorSize - 1) / DlLevelConstants.SectorSize);
    }

    private static void AddSectorBlocks(ref int sectorCount, IReadOnlyList<DlFileBlock> blocks, string name)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            AddSectorBlock(ref sectorCount, blocks[i], $"{name}[{i}]");
        }
    }

    private static void AddSectorBlock(ref int sectorCount, DlFileBlock block, string name)
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

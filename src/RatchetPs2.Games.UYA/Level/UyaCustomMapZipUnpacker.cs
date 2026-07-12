using System.IO.Compression;
using RatchetPs2.Core.Wad.Models;

namespace RatchetPs2.Games.UYA.Level;

public static class UyaCustomMapZipUnpacker
{
    public static UyaCustomMapZipPackage Unpack(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        using var archive = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
        return Unpack(archive);
    }

    public static UyaCustomMapZipPackage Unpack(Stream zipStream)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        return Unpack(archive);
    }

    private static UyaCustomMapZipPackage Unpack(ZipArchive archive)
    {
        var levelDataWadEntry = GetSingleEntryByExtension(archive, ".wad");
        var worldEntry = GetSingleEntryByExtension(archive, ".world");
        var levelDataWadBytes = ReadEntryBytes(levelDataWadEntry);
        var worldBytes = ReadEntryBytes(worldEntry);

        return new UyaCustomMapZipPackage(
            levelDataWadEntry.FullName,
            worldEntry.FullName,
            levelDataWadBytes.Length,
            worldBytes.Length,
            UyaLevelWadUnpacker.UnpackLevelData(levelDataWadBytes),
            UyaLevelWadUnpacker.UnpackGameplay(worldBytes));
    }

    private static ZipArchiveEntry GetSingleEntryByExtension(ZipArchive archive, string extension)
    {
        var entries = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)
                && string.Equals(Path.GetExtension(entry.FullName), extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return entries.Length switch
        {
            1 => entries[0],
            0 => throw new InvalidDataException($"Custom UYA map zip is missing a '{extension}' file."),
            _ => throw new InvalidDataException(
                $"Custom UYA map zip contains multiple '{extension}' files: {string.Join(", ", entries.Select(entry => entry.FullName))}.")
        };
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

public sealed record UyaCustomMapZipPackage(
    string LevelDataWadEntryName,
    string WorldEntryName,
    int LevelDataWadByteLength,
    int WorldByteLength,
    IReadOnlyList<PackedFile> LevelDataFiles,
    IReadOnlyList<PackedFile> GameplayFiles)
{
    public IReadOnlyList<PackedFile> Files { get; } = LevelDataFiles.Concat(GameplayFiles).ToArray();
}

using RatchetPs2.Core.IO;

namespace RatchetPs2.Games.GC.Level;

public sealed record GcLevel(int Id, int TableIndex, string Name);

public readonly record struct GcFileBlock(int Offset, int Length);

public sealed record GcLevelInfoEntry(
    GcLevel Level,
    GcFileBlock LevelWad,
    GcFileBlock LevelAudioWad,
    GcFileBlock LevelSceneWad);

public static class GcLevelCatalog
{
    public const int RetailLevelInfoTableOffset = 0x1F97F8;
    public const int LevelInfoSize = 0x18;

    public static IReadOnlyList<GcLevel> Levels { get; } = Array.AsReadOnly<GcLevel>(
    [
        new(0, 0, "Aranos Tutorial"),
        new(1, 1, "Oozla"),
        new(2, 2, "Maktar Nebula"),
        new(3, 3, "Endako"),
        new(4, 4, "Barlow"),
        new(5, 5, "Feltzin System"),
        new(6, 6, "Notak"),
        new(7, 7, "Siberius"),
        new(8, 8, "Tabora"),
        new(9, 9, "Dobbo"),
        new(10, 10, "Hrugis Cloud"),
        new(11, 11, "Joba"),
        new(12, 12, "Todano"),
        new(13, 13, "Boldan"),
        new(14, 14, "Aranos Prison"),
        new(15, 15, "Gorn"),
        new(16, 16, "Snivelak"),
        new(17, 17, "Smolg"),
        new(18, 18, "Damosel"),
        new(19, 19, "Grelbin"),
        new(20, 20, "Yeedil"),
        new(22, 22, "Dobbo Orbit"),
        new(23, 23, "Damosel Orbit"),
        new(24, 24, "Ship Shack"),
        new(25, 25, "Wupash Nebula"),
        new(26, 26, "Jamming Array"),
        new(30, 21, "Insomniac Museum")
    ]);

    public static GcLevel GetById(int levelId)
    {
        return Levels.FirstOrDefault(level => level.Id == levelId)
            ?? throw new ArgumentOutOfRangeException(
                nameof(levelId),
                "GC level id must be 0-20, 22-26, or 30.");
    }
}

public static class GcLevelInfoReader
{
    public static GcLevelInfoEntry ReadLevel(Stream isoStream, int levelId)
    {
        ArgumentNullException.ThrowIfNull(isoStream);

        if (!isoStream.CanRead || !isoStream.CanSeek)
        {
            throw new ArgumentException("The provided ISO stream must be readable and seekable.", nameof(isoStream));
        }

        var level = GcLevelCatalog.GetById(levelId);
        var entryOffset = checked(
            GcLevelCatalog.RetailLevelInfoTableOffset
            + (level.TableIndex * GcLevelCatalog.LevelInfoSize));
        if (entryOffset + GcLevelCatalog.LevelInfoSize > isoStream.Length)
        {
            throw new InvalidDataException(
                $"GC level info entry {level.TableIndex} at 0x{entryOffset:X} exceeds ISO stream length.");
        }

        isoStream.Position = entryOffset;
        return new GcLevelInfoEntry(
            level,
            ReadBlock(isoStream),
            ReadBlock(isoStream),
            ReadBlock(isoStream));
    }

    private static GcFileBlock ReadBlock(Stream stream)
    {
        return new GcFileBlock(stream.ReadInt32LittleEndian(), stream.ReadInt32LittleEndian());
    }
}

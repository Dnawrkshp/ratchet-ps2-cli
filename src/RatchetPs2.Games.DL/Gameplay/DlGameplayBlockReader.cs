using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.DL.Gameplay;

public static class DlGameplayBlockReader
{
    public const int CoreHeaderSize = 0x80;
    public const int MissionHeaderSize = 0x20;

    private static readonly DlGameplayBlockDescription[] CoreBlocks =
    [
        new(0x00, "level_settings"),
        new(0x04, "cameras"),
        new(0x08, "ambient_sound_instances"),
        new(0x0c, "us_english_strings"),
        new(0x10, "uk_english_strings"),
        new(0x14, "french_strings"),
        new(0x18, "german_strings"),
        new(0x1c, "spanish_strings"),
        new(0x20, "italian_strings"),
        new(0x24, "japanese_strings"),
        new(0x28, "korean_strings"),
        new(0x2c, "moby_classes"),
        new(0x30, "moby_instances"),
        new(0x34, "moby_groups"),
        new(0x38, "shared_data"),
        new(0x3c, "pvar_moby_links"),
        new(0x40, "pvar_table"),
        new(0x44, "pvar_data"),
        new(0x48, "pvar_relative_pointers"),
        new(0x4c, "cuboids"),
        new(0x50, "spheres"),
        new(0x54, "cylinders"),
        new(0x58, "pills"),
        new(0x5c, "splines"),
        new(0x60, "grind_splines"),
        new(0x64, "point_lights"),
        new(0x68, "pad_68"),
        new(0x6c, "camera_collision_grid"),
        new(0x70, "env_sample_points"),
        new(0x74, "areas"),
        new(0x78, "pad_78"),
        new(0x7c, "pad_7c")
    ];

    private static readonly DlGameplayBlockDescription[] MissionBlocks =
    [
        new(0x00, "moby_classes"),
        new(0x04, "moby_instances"),
        new(0x08, "moby_groups"),
        new(0x0c, "shared_data"),
        new(0x10, "pvar_moby_links"),
        new(0x14, "pvar_table"),
        new(0x18, "pvar_data"),
        new(0x1c, "pvar_relative_pointers")
    ];

    public static DlGameplayBlocks ReadCore(ReadOnlySpan<byte> data)
    {
        return Read(data, "core", CoreHeaderSize, CoreBlocks);
    }

    public static DlGameplayBlocks ReadMission(ReadOnlySpan<byte> data)
    {
        return Read(data, "mission", MissionHeaderSize, MissionBlocks);
    }

    private static DlGameplayBlocks Read(
        ReadOnlySpan<byte> data,
        string kind,
        int headerSize,
        IReadOnlyList<DlGameplayBlockDescription> descriptions)
    {
        if (data.Length < headerSize)
        {
            throw new InvalidDataException(
                $"DL {kind} gameplay data is too small to contain the 0x{headerSize:X}-byte pointer table.");
        }

        var pointers = new DlGameplayPointer[descriptions.Count];
        var sortedPointers = new List<int>(descriptions.Count);
        for (var i = 0; i < descriptions.Count; i++)
        {
            var description = descriptions[i];
            var pointer = ReadInt32LittleEndian(data, description.HeaderOffset);
            pointers[i] = new DlGameplayPointer(i, description.HeaderOffset, description.SemanticName, pointer);
            if (pointer > 0 && pointer <= data.Length)
            {
                sortedPointers.Add(pointer);
            }
        }

        sortedPointers.Sort();
        var blocks = new List<DlGameplayBlock>(pointers.Length);

        foreach (var pointer in pointers)
        {
            if (pointer.Pointer < 0 || pointer.Pointer > data.Length)
            {
                throw new InvalidDataException(
                    $"DL {kind} gameplay slot 0x{pointer.HeaderOffset:X2} points outside gameplay bounds.");
            }

            byte[] payload = [];
            if (pointer.Pointer > 0)
            {
                var nextPointer = data.Length;
                foreach (var candidate in sortedPointers)
                {
                    if (candidate > pointer.Pointer)
                    {
                        nextPointer = candidate;
                        break;
                    }
                }

                payload = SliceToArray(
                    data,
                    pointer.Pointer,
                    nextPointer - pointer.Pointer,
                    $"DL {kind} gameplay slot 0x{pointer.HeaderOffset:X2}");
            }

            DlLevelSettings? levelSettings = null;
            if (kind == "core"
                && pointer.HeaderOffset == 0x00
                && DlLevelSettingsReader.TryRead(payload, out var parsedLevelSettings))
            {
                levelSettings = parsedLevelSettings;
            }

            DlMobyInstances? mobyInstances = null;
            if (pointer.SemanticName == "moby_instances"
                && DlMobyInstancesReader.TryRead(payload, out var parsedMobyInstances))
            {
                mobyInstances = parsedMobyInstances;
            }

            blocks.Add(new DlGameplayBlock(
                pointer.Index,
                pointer.HeaderOffset,
                pointer.Pointer,
                pointer.SemanticName,
                payload,
                levelSettings,
                mobyInstances));
        }

        return new DlGameplayBlocks(
            kind,
            headerSize,
            data.Slice(0, headerSize).ToArray(),
            blocks);
    }

    private readonly record struct DlGameplayPointer(
        int Index,
        int HeaderOffset,
        string SemanticName,
        int Pointer);

    private readonly record struct DlGameplayBlockDescription(
        int HeaderOffset,
        string SemanticName);
}

using static RatchetPs2.Core.IO.BinarySpanReader;

namespace RatchetPs2.Games.UYA.Gameplay;

public static class UyaGameplayBlockReader
{
    public const int CoreHeaderSize = 0x9c;

    private static readonly UyaGameplayBlockDescription[] CoreBlocks =
    [
        new(0x00, "level_settings"),
        new(0x04, "directional_lights"),
        new(0x08, "cameras"),
        new(0x0c, "sound_instances"),
        new(0x10, "us_english_strings"),
        new(0x14, "uk_english_strings"),
        new(0x18, "french_strings"),
        new(0x1c, "german_strings"),
        new(0x20, "spanish_strings"),
        new(0x24, "italian_strings"),
        new(0x28, "japanese_strings"),
        new(0x2c, "korean_strings"),
        new(0x30, "tie_classes"),
        new(0x34, "tie_instances"),
        new(0x38, "tie_groups"),
        new(0x3c, "shrub_classes"),
        new(0x40, "shrub_instances"),
        new(0x44, "shrub_groups"),
        new(0x48, "moby_classes"),
        new(0x4c, "moby_instances"),
        new(0x50, "moby_groups"),
        new(0x54, "shared_data"),
        new(0x58, "pvar_moby_links"),
        new(0x5c, "pvar_table"),
        new(0x60, "pvar_data"),
        new(0x64, "pvar_relative_pointers"),
        new(0x68, "cuboids"),
        new(0x6c, "spheres"),
        new(0x70, "cylinders"),
        new(0x74, "pills"),
        new(0x78, "splines"),
        new(0x7c, "grind_splines"),
        new(0x80, "point_lights"),
        new(0x84, "env_transitions"),
        new(0x88, "camera_collision_grid"),
        new(0x8c, "env_sample_points"),
        new(0x90, "occlusion"),
        new(0x94, "tie_ambient_rgbas"),
        new(0x98, "areas")
    ];

    public static UyaGameplayBlocks ReadCore(ReadOnlySpan<byte> data)
    {
        if (data.Length < CoreHeaderSize)
        {
            throw new InvalidDataException(
                $"UYA core gameplay data is too small to contain the 0x{CoreHeaderSize:X}-byte pointer table.");
        }

        var pointers = new UyaGameplayPointer[CoreBlocks.Length];
        var sortedPointers = new List<int>(CoreBlocks.Length);
        for (var i = 0; i < CoreBlocks.Length; i++)
        {
            var description = CoreBlocks[i];
            var pointer = ReadInt32LittleEndian(data, description.HeaderOffset);
            pointers[i] = new UyaGameplayPointer(i, description.HeaderOffset, description.SemanticName, pointer);
            if (pointer > 0 && pointer <= data.Length)
            {
                sortedPointers.Add(pointer);
            }
        }

        sortedPointers.Sort();

        var blocks = new List<UyaGameplayBlock>(pointers.Length);
        foreach (var pointer in pointers)
        {
            if (pointer.Pointer < 0 || pointer.Pointer > data.Length)
            {
                throw new InvalidDataException(
                    $"UYA core gameplay slot 0x{pointer.HeaderOffset:X2} points outside gameplay bounds.");
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
                    $"UYA core gameplay slot 0x{pointer.HeaderOffset:X2}");
            }

            UyaLevelSettings? levelSettings = null;
            if (pointer.HeaderOffset == 0x00
                && UyaLevelSettingsReader.TryRead(payload, out var parsedLevelSettings))
            {
                levelSettings = parsedLevelSettings;
            }

            UyaMobyInstances? mobyInstances = null;
            if (pointer.SemanticName == "moby_instances"
                && UyaMobyInstancesReader.TryRead(payload, out var parsedMobyInstances))
            {
                mobyInstances = parsedMobyInstances;
            }

            blocks.Add(new UyaGameplayBlock(
                pointer.Index,
                pointer.HeaderOffset,
                pointer.Pointer,
                pointer.SemanticName,
                payload,
                levelSettings,
                mobyInstances));
        }

        return new UyaGameplayBlocks(
            "core",
            CoreHeaderSize,
            data.Slice(0, CoreHeaderSize).ToArray(),
            blocks);
    }

    private readonly record struct UyaGameplayPointer(
        int Index,
        int HeaderOffset,
        string SemanticName,
        int Pointer);

    private readonly record struct UyaGameplayBlockDescription(
        int HeaderOffset,
        string SemanticName);
}

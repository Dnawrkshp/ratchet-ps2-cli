using RatchetPs2.Games.DL.Level;
namespace RatchetPs2.Cli.Abstractions;

internal static partial class DlMapExtractionWriter
{
    private static void ExtractWorldInstances(
        string outputDirectory,
        byte[] worldBytes,
        IDictionary<string, object?> manifest)
    {
        var world = DlWorldInstanceReader.Read(worldBytes);
        var slotRoutes = new List<WorldSlotRoute>(world.Slots.Count);

        foreach (var slot in world.Slots)
        {
            if (slot.PayloadBytes.Length == 0)
            {
                slotRoutes.Add(CreateWorldSlotRoute(slot, null, "empty"));
                continue;
            }

            var relativePath = GetWorldSlotRelativePath(slot);
            var outputPath = CombineRelativePath(outputDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, slot.PayloadBytes);
            slotRoutes.Add(CreateWorldSlotRoute(slot, relativePath, IsKnownWorldSlot(slot.HeaderOffset) ? "mapped" : "unknown"));
        }

        var worldManifest = new Dictionary<string, object?>
        {
            ["OmittedRawPayloads"] = new[]
            {
                new
                {
                    Path = "art_instances.wad",
                    Replacement = "lighting, tie, shrub, occlusion, and unknown slot payloads plus slot pointer metadata",
                    Reason = "The aggregate world instance payload is represented by named semantic files and the 0x40-byte slot table metadata."
                }
            },
            ["Length"] = world.Length,
            ["PointerTableLength"] = DlWorldInstanceReader.PointerTableLength,
            ["Slots"] = slotRoutes,
            ["DirectionalLightCount"] = world.DirectionalLights?.Count ?? 0,
            ["TieClassCount"] = world.TieClasses?.Count ?? 0,
            ["TieInstanceCount"] = world.TieInstances?.Count ?? 0,
            ["ShrubClassCount"] = world.ShrubClasses?.Count ?? 0,
            ["ShrubInstanceCount"] = world.ShrubInstances?.Count ?? 0,
            ["OcclusionMapping"] = world.OcclusionMapping
        };

        WriteWorldLightingManifest(outputDirectory, world, slotRoutes);
        WriteWorldTieManifest(outputDirectory, world, slotRoutes);
        WriteWorldShrubManifest(outputDirectory, world, slotRoutes);
        WriteWorldOcclusionManifest(outputDirectory, world, slotRoutes);

        WriteJson(Path.Combine(outputDirectory, "manifest.json"), worldManifest);
        manifest["World"] = worldManifest;
    }

    private static void WriteWorldLightingManifest(
        string outputDirectory,
        DlWorldInstances world,
        IReadOnlyList<WorldSlotRoute> slotRoutes)
    {
        if (world.DirectionalLights is null)
        {
            return;
        }

        var lightingDirectory = CreateDirectory(outputDirectory, "lighting");
        WriteJson(
            Path.Combine(lightingDirectory, "manifest.json"),
            new
            {
                Path = FindWorldSlotPath(slotRoutes, 0x00),
                world.DirectionalLights.Count,
                world.DirectionalLights.RecordSize,
                world.DirectionalLights.DataOffset,
                world.DirectionalLights.IsLengthValid,
                world.DirectionalLights.PaddingLength,
                SourceRuntimeGlobal = "DirLights",
                GhidraNotes = new[]
                {
                    "LightTfrags indexes the directional-light table as lightIndex * 0x40.",
                    "Each record is preserved as four 16-byte float vectors; vector component names are intentionally not inferred yet."
                },
                world.DirectionalLights.Records
            });
    }

    private static void WriteWorldTieManifest(
        string outputDirectory,
        DlWorldInstances world,
        IReadOnlyList<WorldSlotRoute> slotRoutes)
    {
        if (world.TieClasses is null
            && world.TieInstances is null
            && world.TieGroups is null
            && world.TieInstanceColors is null)
        {
            return;
        }

        var tieDirectory = CreateDirectory(outputDirectory, "tie");
        WriteJson(
            Path.Combine(tieDirectory, "manifest.json"),
            new
            {
                ClassIdsPath = FindWorldSlotPath(slotRoutes, 0x04),
                InstancesPath = FindWorldSlotPath(slotRoutes, 0x08),
                GroupsPath = FindWorldSlotPath(slotRoutes, 0x0c),
                ColorsPath = FindWorldSlotPath(slotRoutes, 0x20),
                Classes = world.TieClasses,
                Instances = world.TieInstances,
                Groups = world.TieGroups,
                Colors = world.TieInstanceColors,
                SourceNotes = new[]
                {
                    "DL tie instances are counted records with a 0x10-byte header and 0x60-byte records.",
                    "DL tie groups and colors match deadlocked-level-packer slots 12.bin and 32.bin."
                }
            });
    }

    private static void WriteWorldShrubManifest(
        string outputDirectory,
        DlWorldInstances world,
        IReadOnlyList<WorldSlotRoute> slotRoutes)
    {
        if (world.ShrubClasses is null && world.ShrubInstances is null && world.ShrubGroups is null)
        {
            return;
        }

        var shrubDirectory = CreateDirectory(outputDirectory, "shrub");
        WriteJson(
            Path.Combine(shrubDirectory, "manifest.json"),
            new
            {
                ClassIdsPath = FindWorldSlotPath(slotRoutes, 0x10),
                InstancesPath = FindWorldSlotPath(slotRoutes, 0x14),
                GroupsPath = FindWorldSlotPath(slotRoutes, 0x18),
                Classes = world.ShrubClasses,
                Instances = world.ShrubInstances,
                Groups = world.ShrubGroups,
                SourceNotes = new[]
                {
                    "DL shrub instances are counted records with a 0x10-byte header and 0x70-byte records.",
                    "DL shrub groups match deadlocked-level-packer slot 24.bin."
                }
            });
    }

    private static void WriteWorldOcclusionManifest(
        string outputDirectory,
        DlWorldInstances world,
        IReadOnlyList<WorldSlotRoute> slotRoutes)
    {
        if (world.OcclusionMapping is null)
        {
            return;
        }

        var occlusionDirectory = CreateDirectory(outputDirectory, "occlusion");
        WriteJson(
            Path.Combine(occlusionDirectory, "manifest.json"),
            new
            {
                MappingPath = FindWorldSlotPath(slotRoutes, 0x1c),
                Mapping = world.OcclusionMapping,
                SourceNotes = new[]
                {
                    "The mapping table stores tfrag, tie, and moby instance-to-occlusion pairs after the 0x10-byte count header.",
                    "The source occlusion mask payload is assets/occlusion.bin."
                }
            });
    }

    private static WorldSlotRoute CreateWorldSlotRoute(DlWorldInstanceSlot slot, string? relativePath, string status)
    {
        return new WorldSlotRoute(
            slot.Index,
            slot.HeaderOffset,
            slot.Pointer,
            slot.Length,
            slot.SemanticName,
            relativePath,
            status);
    }

    private static string? FindWorldSlotPath(IReadOnlyList<WorldSlotRoute> slotRoutes, int headerOffset)
    {
        return slotRoutes.FirstOrDefault(route => route.HeaderOffset == headerOffset)?.Path;
    }

    private static string GetWorldSlotRelativePath(DlWorldInstanceSlot slot)
    {
        return slot.HeaderOffset switch
        {
            0x00 => "lighting/directional_lights.bin",
            0x04 => "tie/class_ids.bin",
            0x08 => "tie/instances.bin",
            0x0c => "tie/groups.bin",
            0x10 => "shrub/class_ids.bin",
            0x14 => "shrub/instances.bin",
            0x18 => "shrub/groups.bin",
            0x1c => "occlusion/instance_mapping.bin",
            0x20 => "tie/colors.bin",
            _ => $"unknown/slot_{slot.HeaderOffset:X2}.bin"
        };
    }

    private static bool IsKnownWorldSlot(int headerOffset)
    {
        return headerOffset is 0x00 or 0x04 or 0x08 or 0x0c or 0x10 or 0x14 or 0x18 or 0x1c or 0x20;
    }
}

using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Games.DL.Armor;

public sealed record DlDzoArmorExportResult(
    int ArmorIndex,
    byte[] GlbBytes,
    string? Error)
{
    public bool Succeeded => Error is null;
}

public static class DlDzoArmorExporter
{
    public static IEnumerable<DlDzoArmorExportResult> ExportWad(
        Stream wadStream,
        IReadOnlySet<int>? armorIndices = null,
        bool flattenJointHierarchy = true)
    {
        ArgumentNullException.ThrowIfNull(wadStream);

        var armorWad = DlArmorWadReader.ReadWad(wadStream);
        var selectedArmors = armorIndices is null || armorIndices.Count == 0
            ? armorWad.Armors
            : armorWad.Armors.Where(armor => armorIndices.Contains(armor.Index)).ToArray();
        foreach (var armor in selectedArmors)
        {
            DlDzoArmorExportResult result;
            try
            {
                result = new DlDzoArmorExportResult(
                    armor.Index,
                    DlDzoMobyExporter.ExportMoby(
                        armor.ModelBytes,
                        armor.PifTextures,
                        flattenJointHierarchy),
                    null);
            }
            catch (Exception ex) when (IsArmorExportFailure(ex))
            {
                result = new DlDzoArmorExportResult(armor.Index, [], ex.Message);
            }

            yield return result;
        }

        if (armorIndices is not null)
        {
            var populatedIndices = armorWad.Armors.Select(armor => armor.Index).ToHashSet();
            foreach (var missingIndex in armorIndices.Where(index => !populatedIndices.Contains(index)).Order())
            {
                yield return new DlDzoArmorExportResult(
                    missingIndex,
                    [],
                    $"Armor slot {missingIndex} is empty or absent from this armor WAD.");
            }
        }
    }

    private static bool IsArmorExportFailure(Exception ex)
    {
        return ex is ArgumentException
            or InvalidDataException
            or IOException
            or NotSupportedException
            or OverflowException;
    }
}

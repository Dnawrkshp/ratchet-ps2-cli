using RatchetPs2.Games.DL.Moby;

namespace RatchetPs2.Games.DL.Online;

public sealed record DlDzoOnlineArmorExportResult(
    int ArmorIndex,
    int ClassId,
    byte[] GlbBytes,
    string? Error)
{
    public bool Succeeded => Error is null;
}

public static class DlDzoOnlineArmorExporter
{
    public static IEnumerable<DlDzoOnlineArmorExportResult> ExportWad(
        Stream wadStream,
        IReadOnlySet<int>? armorIndices = null,
        bool flattenJointHierarchy = true)
    {
        ArgumentNullException.ThrowIfNull(wadStream);

        var onlineWad = DlOnlineArmorWadReader.ReadWad(wadStream);
        var selectedArmors = armorIndices is null || armorIndices.Count == 0
            ? onlineWad.Armors
            : onlineWad.Armors.Where(armor => armorIndices.Contains(armor.Index)).ToArray();
        foreach (var armor in selectedArmors)
        {
            DlDzoOnlineArmorExportResult result;
            try
            {
                result = new DlDzoOnlineArmorExportResult(
                    armor.Index,
                    armor.ClassId,
                    DlDzoMobyExporter.ExportMoby(
                        armor.ModelBytes,
                        armor.PifTextures,
                        flattenJointHierarchy),
                    null);
            }
            catch (Exception ex) when (IsArmorExportFailure(ex))
            {
                result = new DlDzoOnlineArmorExportResult(
                    armor.Index,
                    armor.ClassId,
                    [],
                    ex.Message);
            }

            yield return result;
        }

        if (armorIndices is not null)
        {
            var populatedIndices = onlineWad.Armors.Select(armor => armor.Index).ToHashSet();
            foreach (var missingIndex in armorIndices.Where(index => !populatedIndices.Contains(index)).Order())
            {
                yield return new DlDzoOnlineArmorExportResult(
                    missingIndex,
                    -1,
                    [],
                    $"Online armor slot {missingIndex} is empty or absent from this multiplayer WAD.");
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

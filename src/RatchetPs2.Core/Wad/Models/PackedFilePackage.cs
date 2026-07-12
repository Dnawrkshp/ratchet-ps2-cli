namespace RatchetPs2.Core.Wad.Models;

public sealed record PackedFile(
    string Path,
    byte[] Bytes,
    string ContentType);

public sealed record PackedFilePackage(
    byte[] PackedBytes,
    IReadOnlyList<PackedFileEntry> Entries);

public sealed record PackedFileEntry(
    string Path,
    int Offset,
    int Length,
    string ContentType);

public static class PackedFilePackageBuilder
{
    public static PackedFilePackage Pack(IReadOnlyList<PackedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var entries = new PackedFileEntry[files.Count];
        var totalLength = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            entries[i] = new PackedFileEntry(file.Path, totalLength, file.Bytes.Length, file.ContentType);
            totalLength = checked(totalLength + file.Bytes.Length);
        }

        var packedBytes = new byte[totalLength];
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            file.Bytes.AsSpan().CopyTo(packedBytes.AsSpan(entries[i].Offset, file.Bytes.Length));
        }

        return new PackedFilePackage(packedBytes, entries);
    }
}

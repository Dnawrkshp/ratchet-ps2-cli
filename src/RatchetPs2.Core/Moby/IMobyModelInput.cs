namespace RatchetPs2.Core.Moby;

public interface IMobyModelInput
{
    bool FileExists(string relativePath);
    bool DirectoryExists(string relativePath);
    byte[] ReadBytes(string relativePath);
    IReadOnlyList<string> EnumerateDirectories(string relativePath);
    IReadOnlyList<string> EnumerateFiles(string relativePath, string searchPattern = "*");
}

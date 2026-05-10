using RatchetPs2.Games.UYA.Moby;

namespace RatchetPs2.Cli.Commands.Moby;

internal sealed class FileSystemMobyModelOutput : IMobyModelOutput
{
    private readonly string outputDirectory;

    public FileSystemMobyModelOutput(string outputDirectory)
    {
        this.outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
    }

    public void WriteBytes(string relativePath, ReadOnlySpan<byte> bytes)
    {
        var fullPath = Path.Combine(outputDirectory, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(fullPath, bytes.ToArray());
    }
}

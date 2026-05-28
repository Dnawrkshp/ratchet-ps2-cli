namespace RatchetPs2.Core.Moby;

public interface IMobyModelOutput
{
    void WriteBytes(string relativePath, ReadOnlySpan<byte> bytes);
}

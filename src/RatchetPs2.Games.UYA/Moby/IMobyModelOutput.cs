namespace RatchetPs2.Games.UYA.Moby;

public interface IMobyModelOutput
{
    void WriteBytes(string relativePath, ReadOnlySpan<byte> bytes);
}

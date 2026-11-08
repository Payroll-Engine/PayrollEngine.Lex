namespace PayrollEngine.Lex.Tests;

internal sealed class LengthStream(long length) : MemoryStream
{
    public override long Length => length;
}

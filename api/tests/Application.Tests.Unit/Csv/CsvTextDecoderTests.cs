using System.Text;
using Application.Common.Csv;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Csv;

// Excel saves "CSV UTF-8" with a BOM and plain "CSV (Comma delimited)" as Windows-1252 with no marker; both must read
// back as the text the person typed (BLUM-8310).
public class CsvTextDecoderTests
{
    [Fact]
    public void ShouldDropAUtf8ByteOrderMark()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("£100 ‘quoted’")];

        CsvTextDecoder.Decode(content).Should().Be("£100 ‘quoted’");
    }

    [Fact]
    public void ShouldReadUtf8WithoutAByteOrderMark()
    {
        CsvTextDecoder.Decode(Encoding.UTF8.GetBytes("£100 ‘quoted’")).Should().Be("£100 ‘quoted’");
    }

    [Fact]
    public void ShouldFallBackToWindows1252_ForExcelsPlainCsv()
    {
        // 0xA3 is "£" and 0x92 is "’" in Windows-1252; neither is valid UTF-8 on its own.
        byte[] content = [0xA3, (byte)'5', (byte)' ', (byte)'i', (byte)'t', 0x92, (byte)'s'];

        CsvTextDecoder.Decode(content).Should().Be("£5 it’s");
    }

    [Fact]
    public void ShouldReadUtf16WithAByteOrderMark()
    {
        byte[] content = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("a,b")];

        CsvTextDecoder.Decode(content).Should().Be("a,b");
    }
}
using System.Text;
using Application.Common.Csv;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Csv;

public class CsvWriterTests
{
    [Fact]
    public void ShouldWriteNoByteOrderMark_ByDefault()
    {
        // Every export is pinned byte for byte elsewhere; the BLUM-8310 overload must not change them.
        var content = CsvWriter.Build(["a", "b"], [["1", "2"]]);

        content.Should().Equal(Encoding.UTF8.GetBytes("a,b\r\n1,2"));
    }

    [Fact]
    public void ShouldPrefixAByteOrderMark_WhenAsked()
    {
        var content = CsvWriter.Build(["a"], [["£1"]], byteOrderMark: true);

        content.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(content, 3, content.Length - 3).Should().Be("a\r\n£1");
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ShouldNameSpreadsheetColumns(int index, string letter)
    {
        SpreadsheetColumn.Letter(index).Should().Be(letter);
    }
}
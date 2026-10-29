using Application.Common.Csv;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Csv;

// The RFC-4180 reader behind the offline quarterly report upload (BLUM-8310). RowNumber must be the row a spreadsheet
// shows, because that is what the upload's error list names.
public class CsvRecordReaderTests
{
    [Fact]
    public void ShouldReadQuotedDelimitersAndDoubledQuotes()
    {
        var records = CsvRecordReader.Read("a,\"b, c\",\"say \"\"hi\"\"\"\r\nd,e,f");

        records.Should().HaveCount(2);
        records[0].Cells.Should().Equal("a", "b, c", "say \"hi\"");
        records[1].Cells.Should().Equal("d", "e", "f");
    }

    [Fact]
    public void ShouldKeepAMultiLineCellInOneRecord_AndNumberRowsAsTheSpreadsheetDoes()
    {
        var records = CsvRecordReader.Read("h1,h2\r\n\"line one\r\nline two\",x\r\nlast,y");

        records.Select(record => record.RowNumber).Should().Equal(1, 2, 3);
        records[1].Cells[0].Should().Be("line one\nline two", "a line break inside an answer is stored as LF");
        records[2].Cells.Should().Equal("last", "y");
    }

    [Fact]
    public void ShouldCountAnEmptyLineAsARow()
    {
        var records = CsvRecordReader.Read("a\r\n\r\nb");

        records.Should().HaveCount(3);
        records[1].IsBlank.Should().BeTrue();
        records[2].RowNumber.Should().Be(3);
    }

    [Theory]
    [InlineData("a,b\nc,d")]
    [InlineData("a,b\rc,d")]
    [InlineData("a,b\r\nc,d")]
    [InlineData("a,b\r\nc,d\r\n")]
    public void ShouldAcceptEveryLineEnding_AndNotAddARecordForATrailingOne(string text)
    {
        var records = CsvRecordReader.Read(text);

        records.Should().HaveCount(2);
        records[1].Cells.Should().Equal("c", "d");
    }

    [Fact]
    public void ShouldKeepATrailingRecordThatIsOnlyAQuotedEmptyCell()
    {
        CsvRecordReader.Read("a\r\n\"\"").Should().HaveCount(2);
    }

    [Fact]
    public void ShouldReadWithAnotherDelimiter()
    {
        CsvRecordReader.Read("a;\"b;c\";d", ';')[0].Cells.Should().Equal("a", "b;c", "d");
    }

    [Fact]
    public void ShouldRefuseAFileThatEndsInsideAQuotedCell()
    {
        var read = () => CsvRecordReader.Read("a,\"unterminated");

        read.Should().Throw<FormatException>();
    }
}
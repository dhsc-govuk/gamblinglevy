using Api.Dtos;
using Api.Services.ReportSnapshot;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Pure unit tests for the shared answer presenter — the C# port of the frontend renderReportAnswer that the export
// relies on. These pin the rules the export must replicate (checkbox multi-label, unit suffix) so the TS and C# sides
// cannot silently drift; the visibility filter is AnswerVisibility's, pinned by AnswerVisibilityTests. No database, so
// they run without Docker.
public class ReportAnswerPresenterTests
{
    private static ReportQuestionDto Question(
        string code,
        string questionType = "Text",
        string? conditionalQuestionCode = null,
        string? conditionalValue = null,
        string? suffix = null,
        params (string Value, string Label)[] options) =>
        new(
            Guid.NewGuid(),
            code,
            $"{code} label",
            Hint: null,
            questionType,
            IsRequired: true,
            DisplayOrder: 0,
            conditionalQuestionCode,
            conditionalValue,
            options.Select(o => new ReportQuestionOptionDto(o.Value, o.Label)).ToList(),
            MinValue: null,
            MaxValue: null,
            suffix,
            Section: "results");

    [Fact]
    public void RenderAnswer_WhenEmpty_IsBlank()
    {
        ReportAnswerPresenter.RenderAnswer(Question("Q"), null).Should().BeEmpty();
        ReportAnswerPresenter.RenderAnswer(Question("Q"), "   ").Should().BeEmpty();
    }

    [Fact]
    public void RenderAnswer_RadioValue_MapsToOptionLabel()
    {
        var question = Question("Q", "Radio", options: [("yes", "Yes"), ("no", "No")]);

        ReportAnswerPresenter.RenderAnswer(question, "yes").Should().Be("Yes");
    }

    [Fact]
    public void RenderAnswer_CheckboxMultiValue_MapsEachPartToItsLabel()
    {
        var question = Question("Q", "Checkbox", options: [("a", "Apple"), ("b", "Banana"), ("c", "Cherry")]);

        ReportAnswerPresenter.RenderAnswer(question, "a,c").Should().Be("Apple, Cherry");
    }

    [Fact]
    public void RenderAnswer_NumberWithSuffix_AppendsUnit()
    {
        var question = Question("Q", "Number", suffix: "%");

        ReportAnswerPresenter.RenderAnswer(question, "40").Should().Be("40%");
    }

    [Fact]
    public void RenderAnswer_UnknownOptionValue_FallsBackToRawValue()
    {
        var question = Question("Q", "Radio", options: [("yes", "Yes")]);

        ReportAnswerPresenter.RenderAnswer(question, "maybe").Should().Be("maybe");
    }

    // Currency (BLUM-7860) is rendered the way the activity page's resolveAnswerDisplay shows it — pinned here because
    // the activity-record export (BLUM-7648) is the first file to carry one.
    [Theory]
    [InlineData("25000", "£25,000")]
    [InlineData("1000000000", "£1,000,000,000")]
    [InlineData("0", "£0")]
    [InlineData("£1,500", "£1,500")]
    [InlineData("25 000", "£25,000")]
    [InlineData("12.5", "£12.5")]
    [InlineData("-40", "-£40")]
    [InlineData("not a number", "not a number")]

    // BLUM-8955: a separator that is not a thousands separator is not read through. These were stored while every
    // comma was stripped, and used to come out as £510 and £10.
    [InlineData("5,10", "5,10")]
    [InlineData("0,10", "0,10")]

    // A thousands separator is still one beside a sign or pence: these are amounts, and were shown as amounts.
    [InlineData("-1,500", "-£1,500")]
    [InlineData("1,500.50", "£1,500.5")]
    [InlineData("-£1 500.25", "-£1,500.25")]
    public void RenderAnswer_Currency_ShowsPoundsWithGrouping(string value, string expected)
    {
        var question = Question("Q", "Currency");

        ReportAnswerPresenter.RenderAnswer(question, value).Should().Be(expected);
    }
}
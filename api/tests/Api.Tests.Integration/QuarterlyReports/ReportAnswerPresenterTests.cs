using Api.Dtos;
using Api.Services.ReportSnapshot;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Pure unit tests for the shared answer presenter — the C# port of the frontend renderReportAnswer / isQuestionVisible
// that the export relies on. These pin the rules the export must replicate (visibility filter, checkbox multi-label,
// unit suffix) so the TS and C# sides cannot silently drift. No database, so they run without Docker.
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

    private static Dictionary<string, string?> Answers(params (string Code, string? Value)[] answers) =>
        answers.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal);

    [Fact]
    public void IsVisible_WhenNoConditionalRule_IsAlwaysVisible()
    {
        var question = Question("Q");

        ReportAnswerPresenter.IsVisible(question, Answers()).Should().BeTrue();
    }

    [Fact]
    public void IsVisible_WhenParentUnanswered_IsHidden()
    {
        var question = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "yes");

        ReportAnswerPresenter.IsVisible(question, Answers()).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WhenParentValueNotInAllowList_IsHidden()
    {
        var question = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "yes");

        ReportAnswerPresenter.IsVisible(question, Answers(("PARENT", "no"))).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_WhenParentValueMatches_IsVisible()
    {
        var question = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "yes");

        ReportAnswerPresenter.IsVisible(question, Answers(("PARENT", "yes"))).Should().BeTrue();
    }

    [Fact]
    public void IsVisible_WhenCheckboxParentSharesOneAllowedValue_IsVisible()
    {
        // conditionalValue is a comma-separated allow-list; a checkbox parent holds several comma-joined values.
        var question = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "other,extra");

        ReportAnswerPresenter.IsVisible(question, Answers(("PARENT", "main, other"))).Should().BeTrue();
    }

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
}
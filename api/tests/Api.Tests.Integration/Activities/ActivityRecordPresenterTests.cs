using Api.Dtos;
using Api.Services.ActivityRecord;
using Application.Common.Interfaces.Queries;
using Domain.Categories;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Pure tests for the activity-record export's port of the read-only activity page (BLUM-7648) — the rules in
// client/src/views/Activities/display.tsx and View/index.tsx that decide which rows a record has and how each reads.
// No database, so they run without Docker. The seeded-schema end-to-end cases live in ActivityRecordExportTests.
public class ActivityRecordPresenterTests
{
    private static readonly Guid CategoryId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    [Theory]
    [InlineData("Role details", CategoryNames.PeopleAndRoles, true)]
    [InlineData("Activity details", CategoryNames.PeopleAndRoles, false)]
    [InlineData("People and issues", CategoryNames.PeopleAndRoles, false)]
    [InlineData("Additional information", CategoryNames.PeopleAndRoles, false)]
    [InlineData("Activity details", "Needs assessment", true)]
    [InlineData("People and issues", "Needs assessment", false)]
    [InlineData("Role details", "Needs assessment", false)]
    [InlineData("Additional information", "Needs assessment", true)]
    [InlineData("People and issues", CategoryNames.EducationCampaignsAndOutreach, true)]
    [InlineData("Activity details", CategoryNames.HelpAdviceAndAccessToServices, true)]
    [InlineData("A section DHSC added", CategoryNames.PeopleAndRoles, true)]
    [InlineData("Activity details", null, true)]
    public void IsSectionApplicable_MatchesTheActivityPage(string sectionTitle, string? categoryName, bool expected)
    {
        ActivityRecordPresenter.IsSectionApplicable(sectionTitle, categoryName).Should().Be(expected);
    }

    [Theory]
    [InlineData(3, 2026, "March 2026")]
    [InlineData(12, 2025, "December 2025")]
    [InlineData(13, 2026, "13 2026")]
    [InlineData(null, 2026, null)]
    [InlineData(4, null, null)]
    public void FormatStartDate_MatchesTheActivityPage(int? month, int? year, string? expected)
    {
        ActivityRecordPresenter.FormatStartDate(month, year).Should().Be(expected);
    }

    [Theory]
    [InlineData("planned", false, "Planned")]
    [InlineData("finished", false, "Finished")]
    [InlineData("active", true, "Draft")]
    [InlineData(null, false, "Draft")]
    public void StatusLabel_MatchesTheActivityPage(string? status, bool isDraft, string expected)
    {
        ActivityRecordPresenter.StatusLabel(status, isDraft).Should().Be(expected);
    }

    [Fact]
    public void ReportLabel_NamesTheQuarterAndYear_FallingBackToWhatWasRecorded()
    {
        ActivityRecordPresenter.ReportLabel(Report("2026/27", 2)).Should().Be("Q2 2026/27");
        ActivityRecordPresenter.ReportLabel(Report("2026/27", null)).Should().Be("2026/27");
        ActivityRecordPresenter.ReportLabel(Report(null, 2)).Should().Be("Collection name");
    }

    [Fact]
    public void CreationRows_StandardMeasures_ListTheLiveLabelsThenOther_AndTheCustomText()
    {
        var activity = Activity(
            expectedResults: [new("kr_b", "Snapshotted B"), new("kr_a", "Snapshotted A"), new(null, " Footfall ")]);
        var taxonomy = Taxonomy(keyResults: [new("kr_a", TaskId, "Live A")]);

        var rows = ActivityRecordPresenter.CreationRows(activity, EmptySchema, taxonomy, transitive: false);

        rows.Where(r => r.Section == ActivityRecordPresenter.ExpectedResultsSection).Select(r => (r.Question, r.Answer)).Should().Equal(
            (ActivityRecordPresenter.ExpectedResultsQuestion, "Snapshotted B, Live A, Other"),
            (ActivityRecordPresenter.OtherMeasureQuestion, "Footfall"));
    }

    [Fact]
    public void CreationRows_FreeTextOnlyType_ListsNumberedMeasures()
    {
        var activity = Activity(expectedResults: [new(null, "Attendance"), new(null, "Referrals")]);

        var rows = ActivityRecordPresenter.CreationRows(activity, EmptySchema, Taxonomy(), transitive: false);

        rows.Where(r => r.Section == ActivityRecordPresenter.ExpectedResultsSection).Select(r => (r.Question, r.Answer)).Should().Equal(
            ("Measure 1", "Attendance"),
            ("Measure 2", "Referrals"));
    }

    [Fact]
    public void CreationRows_ACategoryWithOneType_HasNoTypeRows()
    {
        var rows = ActivityRecordPresenter.CreationRows(Activity(), EmptySchema, Taxonomy(taskCount: 1), transitive: false);

        rows.Should().NotContain(r => r.Section == "Activity type");
    }

    [Fact]
    public void CreationRows_KeepTheCustomTypeNameAlongsideTheType()
    {
        var activity = Activity(customTaskText: "Pop-up stall");

        var rows = ActivityRecordPresenter.CreationRows(activity, EmptySchema, Taxonomy(), transitive: false);

        rows.Where(r => r.Section == "Activity type").Select(r => (r.Question, r.Answer)).Should().Equal(
            ("What type of work does it involve?", "Other type of outreach"),
            ("What's the activity?", "Pop-up stall"));
    }

    [Fact]
    public void CreationRows_ListAnUnansweredVisibleQuestionBlank_AndRenderChoicesAndMoney()
    {
        var sectionId = Guid.NewGuid();
        var schema = new FormSchemaDto(
            [new FormSectionDto(sectionId, CategoryId, null, "Activity details", null, 0, true)],
            [
                Question(sectionId, "WHO", "Checkbox", 0, options: [new("council", "Council", 0), new("other", "Other", 1)]),
                Question(sectionId, "WHO_OTHER", "Text", 1, conditional: ("WHO", "other")),
                Question(sectionId, "MONEY", "Currency", 2),
                Question(sectionId, "UNANSWERED", "Textarea", 3),
            ]);
        var activity = Activity(answers: [new("WHO", "council"), new("WHO_OTHER", "Hidden text"), new("MONEY", "1500")]);

        var rows = ActivityRecordPresenter.CreationRows(activity, schema, Taxonomy(), transitive: false);

        rows.Where(r => r.Section == "Activity details").Select(r => (r.Question, r.Answer)).Should().Equal(
            (ActivityRecordPresenter.ActivityNameQuestion, "Outreach"),
            ("WHO label", "Council"),
            ("MONEY label", "£1,500"),
            ("UNANSWERED label", string.Empty));
    }

    [Fact]
    public void CreationRows_ActiveRecord_AddsItsStartDateAfterTheStatus()
    {
        var rows = ActivityRecordPresenter.CreationRows(Activity(status: "active", startMonth: 5, startYear: 2026), EmptySchema, Taxonomy(), transitive: false);

        rows.Where(r => r.Section == ActivityRecordPresenter.ActivityStatusSection).Select(r => (r.Question, r.Answer)).Should().Equal(
            ("What’s the status of this activity?", "Active"),
            ("When did the work start?", "May 2026"));
    }

    // BLUM-7673: the reason follows the status, listed blank for a record closed before the question existed — the same
    // rule as any other unanswered visible question.
    [Theory]
    [InlineData("No longer needed", "No longer needed")]
    [InlineData(null, "")]
    public void CreationRows_ClosedRecord_AddsItsClosureReasonAfterTheStatus(string? closureReason, string expected)
    {
        var rows = ActivityRecordPresenter.CreationRows(Activity(status: "closed", closureReason: closureReason), EmptySchema, Taxonomy(), transitive: false);

        rows.Where(r => r.Section == ActivityRecordPresenter.ActivityStatusSection).Select(r => (r.Question, r.Answer)).Should().Equal(
            ("What’s the status of this activity?", "Closed"),
            ("Why is this activity being closed?", expected));
    }

    [Fact]
    public void CreationRows_ClosedRole_AsksTheClosureQuestionInRoleWording()
    {
        var activity = Activity(status: "closed", closureReason: "Post deleted", categoryName: CategoryNames.PeopleAndRoles);

        var rows = ActivityRecordPresenter.CreationRows(activity, EmptySchema, Taxonomy(), transitive: false);

        rows.Should().Contain(r => r.Section == ActivityRecordPresenter.ActivityStatusSection && r.Question == "Why is this role being closed?" && r.Answer == "Post deleted");
    }

    [Theory]
    [InlineData("planned")]
    [InlineData("active")]
    [InlineData("finished")]
    public void CreationRows_OnlyAClosedRecord_HasAClosureRow(string status)
    {
        var rows = ActivityRecordPresenter.CreationRows(Activity(status: status, closureReason: "Stale reason"), EmptySchema, Taxonomy(), transitive: false);

        rows.Should().NotContain(r => r.Question.StartsWith("Why is this"));
    }

    private static readonly FormSchemaDto EmptySchema = new([], []);

    private static SubmittedReportRef Report(string? financialYear, int? quarter) =>
        new(Guid.NewGuid(), "Collection name", financialYear, quarter, null, DateTime.UtcNow, "REF");

    private static ActivityDto Activity(
        IReadOnlyList<ExpectedResultDto>? expectedResults = null,
        IReadOnlyList<ActivityAnswerDto>? answers = null,
        string? customTaskText = null,
        string status = "planned",
        int? startMonth = null,
        int? startYear = null,
        string? closureReason = null,
        string categoryName = "Education, campaigns and outreach") =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Outreach",
            "Outreach",
            CategoryId,
            categoryName,
            TaskId,
            "Other type of outreach",
            customTaskText,
            status,
            false,
            startMonth,
            startYear,
            closureReason,
            expectedResults ?? [],
            answers ?? [],
            DateTime.UtcNow,
            null);

    private static ActivityTaxonomyDto Taxonomy(int taskCount = 2, IReadOnlyList<TaxonomyKeyResultDto>? keyResults = null) =>
        new(
            [new TaxonomyCategoryDto(CategoryId, "Education, campaigns and outreach")],
            Enumerable.Range(0, taskCount).Select(i => new TaxonomyTaskDto(i == 0 ? TaskId : Guid.NewGuid(), $"Task {i}", CategoryId)).ToList(),
            keyResults ?? []);

    private static FormQuestionDto Question(
        Guid sectionId,
        string code,
        string questionType,
        int displayOrder,
        IReadOnlyList<FormQuestionOptionDto>? options = null,
        (string Code, string Value)? conditional = null) =>
        new(
            Guid.NewGuid(),
            sectionId,
            code,
            $"{code} label",
            null,
            null,
            null,
            questionType,
            false,
            true,
            displayOrder,
            conditional?.Code,
            conditional?.Value,
            options ?? []);
}
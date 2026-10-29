using Application.Submissions;
using Application.Submissions.Offline;
using Domain.Activities;
using Domain.Categories;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using FluentAssertions;
using Xunit;
using static Application.Tests.Unit.Submissions.Offline.OfflineFixture;

namespace Application.Tests.Unit.Submissions.Offline;

// "Record funding spent" and "Provide strategic feedback" as offline spreadsheets (BLUM-8310).
public class FundingAndFeedbackOfflineTests
{
    private static readonly UserId Actor = UserId.New();
    private static readonly Submission Report = Submission.Start(
        SubmissionId.New(), new CollectionId(ReportCollectionId), new OrganisationId(ReportOrganisationId), [], Actor, DateTime.UtcNow);

    private static Activity BuildActivity(ActivityStatus status, string title) =>
        Activity.NewComplete(ActivityId.New(), new OrganisationId(ReportOrganisationId), Actor, CategoryId.New(), ActivityTaskId.New(), status, title: title, utcNow: DateTime.UtcNow);

    private static (OfflineSheet Sheet, FundingSpendContext Context, IReadOnlyList<int> Remaining) Funding(
        int quarter,
        IReadOnlyList<SubmissionSpendEntry>? saved = null,
        params Activity[] activities)
    {
        var actual = activities.Where(FundingSpendContextResolver.IsActualSpendReportable).ToList();
        var forecast = activities.Where(FundingSpendContextResolver.IsForecastSpendReportable).ToList();
        var context = new FundingSpendContext(
            Report,
            null!,
            new OrganisationId(ReportOrganisationId),
            Actor,
            actual,
            actual.Select(activity => FundingSpendSubject.For(activity, null)).ToList(),
            forecast,
            forecast.Select(activity => FundingSpendSubject.For(activity, null)).ToList());
        var remaining = Application.Collections.ReportingPeriod.RemainingQuarters(quarter);
        var sheet = FundingSpentSheetBuilder.Build(
            ReportCollectionId,
            ReportOrganisationId,
            context,
            remaining,
            activities.ToDictionary(activity => activity.Id.Value, activity => activity.Title!),
            (new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)),
            remaining.ToDictionary(q => q, q => Application.Collections.ReportingPeriod.Derive("2026/27", q)),
            saved ?? []);
        return (sheet, context, remaining);
    }

    private static OfflineValidationResult ValidateFunding(OfflineSheet sheet, FundingSpendContext context, IReadOnlyList<int> remaining, byte[] file) =>
        OfflineUploadValidator.Validate(
            sheet, Upload(file), rows => OfflineSectionRules.FundingSpent(sheet, context, remaining, OfflineSectionRules.FundingAmounts(context, remaining, rows)), transitive: false);

    [Fact]
    public void FundingSheet_ShouldHaveAnOtherRow_AndPlannedActivitiesOnlyOnTheForecast()
    {
        var active = BuildActivity(ActivityStatus.Active, "Workshops");
        var planned = BuildActivity(ActivityStatus.Planned, "Next year's campaign");
        var saved = new[]
        {
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), Report.Id, active.Id, 1500m, Actor, DateTime.UtcNow),
            SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), Report.Id, null, 3, 20m, Actor, DateTime.UtcNow),
        };

        var (sheet, _, _) = Funding(quarter: 2, saved, active, planned);
        var records = Records(OfflineTemplateWriter.Write(sheet));

        sheet.Columns.Where(column => column.Kind == OfflineColumnKind.Question).Select(column => column.Heading).Should().Equal(
            "Q1. Amount spent this quarter (£)", "Q2. Expected spend in Quarter 3 (£)", "Q3. Expected spend in Quarter 4 (£)");
        sheet.Column("actual").Guidance.Should().Contain("1 April 2026 and 30 June 2026").And.Contain("Planned activities say N/A.");
        sheet.Column("forecast_q3").Guidance.Should().Contain("Quarter 3, 1 October 2026 to 31 December 2026");

        var activeRow = records.Single(record => record.Cells[0] == active.Id.Value.ToString());
        activeRow.CellAt(sheet.Column("actual").Index).Should().Be("1500");
        records.Single(record => record.Cells[0] == planned.Id.Value.ToString()).CellAt(sheet.Column("actual").Index).Should().Be("N/A");
        var other = records.Single(record => record.Cells[0] == "OTHER");
        other.CellAt(sheet.Column("forecast_q3").Index).Should().Be("20");
        records[^1].Cells[0].Should().Be("OTHER");
    }

    [Fact]
    public void FundingSheet_InQuarterFour_ShouldHaveNoForecast_AndLeavePlannedActivitiesOut()
    {
        var active = BuildActivity(ActivityStatus.Active, "Workshops");
        var planned = BuildActivity(ActivityStatus.Planned, "Next year's campaign");

        var (sheet, _, _) = Funding(quarter: 4, null, active, planned);

        sheet.Columns.Where(column => column.Kind == OfflineColumnKind.Question).Select(column => column.Key).Should().Equal("actual");
        sheet.Rows.Select(row => row.Reference).Should().Equal(active.Id.Value.ToString(), "OTHER");
    }

    [Fact]
    public void FundingUpload_ShouldReadMoneyAsTyped_AndMapEveryValidatorErrorToItsCell()
    {
        var active = BuildActivity(ActivityStatus.Active, "Workshops");
        var planned = BuildActivity(ActivityStatus.Planned, "Campaign");
        var (sheet, context, remaining) = Funding(quarter: 3, null, active, planned);
        var activeRef = active.Id.Value.ToString();
        var plannedRef = planned.Id.Value.ToString();

        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (activeRef, "actual", "£1,500"),
            (activeRef, "forecast_q4", "1500.004"),
            (plannedRef, "forecast_q4", "-5"),
            ("OTHER", "actual", "abc"),
            ("OTHER", "forecast_q4", "0"));

        var result = ValidateFunding(sheet, context, remaining, file);

        var q4 = sheet.Column("forecast_q4");
        result.Errors.Should().BeEquivalentTo(new[]
        {
            new OfflineCellError(4, q4.Index, q4.QuestionNumber, "Expected spend on Workshops for Quarter 4 must be a whole number and not include pence"),
            new OfflineCellError(5, q4.Index, q4.QuestionNumber, "Expected spend on Campaign for Quarter 4 must be £0 or more"),
            new OfflineCellError(6, sheet.Column("actual").Index, 1, "Enter a whole number of pounds, like 1500"),
        });
        result.Rows.Single(row => row.Row.Reference == activeRef).Answers[$"actual-{active.Id.Value}"].Should().Be("1500");
    }

    [Fact]
    public void FundingUpload_ShouldRequireEveryAmount_AndRefuseAnActualForAPlannedActivity()
    {
        var active = BuildActivity(ActivityStatus.Active, "Workshops");
        var planned = BuildActivity(ActivityStatus.Planned, "Campaign");
        var (sheet, context, remaining) = Funding(quarter: 3, null, active, planned);

        var file = Edit(OfflineTemplateWriter.Write(sheet), (planned.Id.Value.ToString(), "actual", "100"));

        var result = ValidateFunding(sheet, context, remaining, file);

        result.Errors.Should().Contain(error => error.Row == 5 && error.Message == OfflineUploadValidator.NotApplicableMessage);
        result.Errors.Should().Contain(error => error.Row == 4 && error.Message == "Enter the amount spent on Workshops");
        result.Errors.Should().Contain(error => error.Row == 6 && error.Message == "Enter the other spend this quarter");
    }

    [Fact]
    public void FundingUpload_ShouldRoundTripAFilledTemplate()
    {
        var active = BuildActivity(ActivityStatus.Active, "Workshops");
        var (sheet, context, remaining) = Funding(quarter: 3, null, active);
        var activeRef = active.Id.Value.ToString();
        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (activeRef, "actual", "100"),
            (activeRef, "forecast_q4", "50"),
            ("OTHER", "actual", "0"),
            ("OTHER", "forecast_q4", "10"));

        var result = ValidateFunding(sheet, context, remaining, file);
        var amounts = OfflineSectionRules.FundingAmounts(context, remaining, result.Rows);

        result.Errors.Should().BeEmpty();
        amounts.Actuals[active.Id.Value].Should().Be(100m);
        amounts.OtherActual.Should().Be(0m);
        amounts.Forecasts[(active.Id.Value, 4)].Should().Be(50m);
        amounts.OtherForecasts[4].Should().Be(10m);
        FundingSpendEntries.Actuals(context, amounts.Actuals, amounts.OtherActual, DateTime.UtcNow).Should().HaveCount(2);
    }

    private static IReadOnlyList<DcFormQuestion> FeedbackQuestions()
    {
        var module = FormModule.Create(FormModuleId.New(), SubmissionModules.StrategicFeedback, "Provide strategic feedback", 3, Actor, DateTime.UtcNow);
        var learnings = module.AddQuestion(DcFormQuestionId.New(), "SF_LEARNINGS", "Do you have any learnings to share?", QuestionType.Radio, 1, Actor, isRequired: true, utcNow: DateTime.UtcNow);
        learnings.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes", 0);
        learnings.AddOption(DcFormQuestionOptionId.New(), "no", "No", 1);
        learnings.AddOption(DcFormQuestionOptionId.New(), "maybe", "Maybe", 2, isActive: false);
        return
        [
            module.AddQuestion(
                DcFormQuestionId.New(), "SF_DHSC_SUPPORT", "Do you have any feedback for DHSC?", QuestionType.Textarea, 0, Actor,
                isRequired: true, errorMessage: "Enter your feedback for DHSC", maxLength: 20, maxLengthErrorMessage: "Feedback must be 20 characters or less",
                utcNow: DateTime.UtcNow),
            learnings,
        ];
    }

    [Fact]
    public void FeedbackSheet_ShouldBeASingleRowWithAColumnPerQuestion()
    {
        var questions = FeedbackQuestions();
        var saved = new[] { SubmissionFeedbackAnswer.Record(SubmissionFeedbackAnswerId.New(), Report.Id, "SF_LEARNINGS", "no", Actor, DateTime.UtcNow) };

        var sheet = StrategicFeedbackSheetBuilder.Build(ReportCollectionId, ReportOrganisationId, "Barnsley", questions, saved);
        var records = Records(OfflineTemplateWriter.Write(sheet));

        records.Should().HaveCount(4);
        records[0].Cells.Should().Equal("Organisation (do not change)", "Errors (leave blank)", "Q1. Do you have any feedback for DHSC?", "Q2. Do you have any learnings to share?");
        records[3].Cells.Should().Equal("Barnsley", string.Empty, string.Empty, "No");
        sheet.Column("SF_LEARNINGS").Guidance.Should().Contain("Enter ‘Yes’ or ‘No’.", "an inactive option is not offered");
        sheet.Column("SF_DHSC_SUPPORT").Guidance.Should().Be("Required. 20 characters or less.");
    }

    [Fact]
    public void FeedbackUpload_ShouldUseTheFeedbackValidator_AndRefuseExtraRows()
    {
        var questions = FeedbackQuestions();
        var sheet = StrategicFeedbackSheetBuilder.Build(ReportCollectionId, ReportOrganisationId, "Barnsley", questions, []);
        var records = Records(Edit(OfflineTemplateWriter.Write(sheet), ("Barnsley", "SF_DHSC_SUPPORT", "This answer is far too long for the limit"), ("Barnsley", "SF_LEARNINGS", "YES")))
            .Select(record => record.Cells)
            .ToList();
        records.Add(["Barnsley", string.Empty, "Second go", "No"]);
        var file = Application.Common.Csv.CsvWriter.Build(records[0], records.Skip(1), byteOrderMark: true);

        var result = OfflineUploadValidator.Validate(sheet, Upload(file), rows => OfflineSectionRules.StrategicFeedback(questions, rows), transitive: false);

        result.Errors.Should().BeEquivalentTo(new[]
        {
            new OfflineCellError(4, 2, 1, "Feedback must be 20 characters or less"),
            new OfflineCellError(5, null, null, "Only use one row for your answers. Delete this row"),
        });
        result.Rows.Single().Answers["SF_LEARNINGS"].Should().Be("yes");
    }

    [Fact]
    public void FeedbackUpload_WithNoAnswerRow_ShouldAppendOne()
    {
        var questions = FeedbackQuestions();
        var sheet = StrategicFeedbackSheetBuilder.Build(ReportCollectionId, ReportOrganisationId, "Barnsley", questions, []);
        var file = RemoveRow(OfflineTemplateWriter.Write(sheet), "Barnsley");

        var result = OfflineUploadValidator.Validate(sheet, Upload(file), rows => OfflineSectionRules.StrategicFeedback(questions, rows), transitive: false);

        result.Errors.Should().ContainSingle().Which.Should().Be(
            new OfflineCellError(4, null, null, "Your answers are missing from your file. Answer the questions in this row"));
    }
}
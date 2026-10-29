using System.Globalization;
using Api.Dtos;
using Api.Services.ReportSnapshot;
using Application.Common.Interfaces.Queries;
using Application.Common.ReportSnapshots;
using Application.Forms.Answers;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;

namespace Api.Services.ActivityRecord;

// One row of the activity-record CSV below its identity cells. Report is blank on the rows describing the record
// itself and names the quarterly report on the rows that say what was reported.
public readonly record struct ActivityRecordRow(string Report, string Section, string Question, string Answer);

// The rows of the "Download activity record" CSV (BLUM-7648). The creation half is a C# port of what the read-only
// activity page shows — client/src/views/Activities/View/index.tsx, with its copy and predicates from display.tsx and
// answers rendered as answers.ts resolveAnswerDisplay does — so the file restates the record in the words the LA
// answered it in. Keep the two in step: a question the page shows and the file omits is the defect this ticket was
// raised against. Pinned by ActivityRecordPresenterTests.
//
// The file always takes the completed-record layout (name row on Activity details, Additional information after
// Expected results), drafts included: a file has one shape, and the draft layout only reorders the same rows.
public static class ActivityRecordPresenter
{
    public const string RecordSection = "Activity record";
    public const string CategorySection = "Activity category";
    public const string ExpectedResultsSection = "Expected results";
    public const string ActivityStatusSection = "Activity status";
    public const string QuarterlyReportSection = "Quarterly report";
    public const string ActivityResultsSection = "Activity results";
    public const string ActivityDeliverySection = "Activity delivery";
    public const string FundingSpendSection = "Funding spend";

    // display.tsx CATEGORY_QUESTION / ACTIVITY_NAME_QUESTION / EXPECTED_RESULTS_QUESTION / OTHER_MEASURE_LABEL.
    public const string CategoryQuestion = "Which category best describes this activity?";
    public const string ActivityNameQuestion = "What is the name of this activity?";
    public const string ExpectedResultsQuestion = "How have you measured the progress of this activity, or how will you measure it?";
    public const string OtherMeasureQuestion = "What is the other measure you have used or will use?";

    public const string ActualSpendQuestion = "Actual spend this quarter (£)";

    // The seeded section titles the client's isActivitySectionApplicable keys on.
    private const string ActivityDetailsTitle = "Activity details";
    private const string PeopleAndIssuesTitle = "People and issues";
    private const string RoleDetailsTitle = "Role details";
    private const string AdditionalInformationTitle = "Additional information";

    private static readonly string[] DetailSectionTitles = [PeopleAndIssuesTitle, RoleDetailsTitle, ActivityDetailsTitle];

    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static readonly Dictionary<string, string> StatusLabels = new(StringComparer.Ordinal)
    {
        [ActivityStatuses.Planned] = "Planned",
        [ActivityStatuses.Active] = "Active",
        [ActivityStatuses.Closed] = "Closed",
        [ActivityStatuses.Finished] = "Finished",
    };

    // transitive is BLUM_9087: on, a question whose parent is hidden is left out too.
    public static IReadOnlyList<ActivityRecordRow> CreationRows(ActivityDto activity, FormSchemaDto schema, ActivityTaxonomyDto taxonomy, bool transitive)
    {
        var rows = new List<ActivityRecordRow>();
        var copy = CopyFor(activity.CategoryName);

        void Add(string section, string question, string? answer) =>
            rows.Add(new ActivityRecordRow(string.Empty, section, question, answer ?? string.Empty));

        Add(RecordSection, "Activity ID", activity.Id.ToString());
        Add(RecordSection, "Record status", activity.IsDraft ? "Draft" : "Complete");
        Add(RecordSection, "Created", FormatTimestamp(activity.CreatedAt));
        Add(RecordSection, "Last updated", activity.UpdatedAt is { } updatedAt ? FormatTimestamp(updatedAt) : null);

        // Category and type are locked once setup is complete, and are the only structural answers the page shows
        // before the dynamic sections.
        Add(CategorySection, CategoryQuestion, activity.CategoryName);

        // A category offering exactly one type never asked the type question (showTypeCard).
        if (taxonomy.Tasks.Count(task => task.CategoryId == activity.CategoryId) != 1)
        {
            Add(copy.TypeTitle, copy.TypeQuestion, activity.TaskName ?? activity.CustomTaskText);

            // The page shows the type OR the free-text "Other" name; the file keeps both when both were entered, so no
            // answer the LA typed is dropped from their record.
            if (activity.TaskName is not null && !string.IsNullOrWhiteSpace(activity.CustomTaskText))
            {
                Add(copy.TypeTitle, copy.OtherLabel, activity.CustomTaskText);
            }
        }

        var answers = activity.Answers
            .Where(answer => !string.IsNullOrEmpty(answer.Value))
            .GroupBy(answer => answer.QuestionCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        var visibility = AnswerVisibility.Of(
            schema.Questions
                .Where(question => question.IsActive)
                .Select(question => VisibilityNode.For(question.Code, question.ConditionalQuestionCode, question.ConditionalValue)),
            answers,
            transitive);

        // Every visible question of every applicable section is listed, answered or not (BLUM-7187) — the same
        // section and visibility predicates the wizard, editor, check-answers and view page share.
        var sections = schema.Sections
            .Where(section => section.IsActive)
            .Where(section => IsSectionApplicable(section.Title, activity.CategoryName))
            .Where(section => section.ActivityTaskId is null || section.ActivityTaskId == activity.TaskId)
            .OrderBy(section => section.DisplayOrder)
            .Select(section => (
                Section: section,
                Questions: schema.Questions
                    .Where(question => question.SectionId == section.Id && question.IsActive)
                    .Where(question => visibility.IsVisible(question.Code))
                    .OrderBy(question => question.DisplayOrder)
                    .ToList()))
            .Where(group => group.Questions.Count > 0 || ShowsNameField(group.Section.Title, activity.CategoryName))
            .ToList();

        void AddSection(FormSectionDto section, IReadOnlyList<FormQuestionDto> questions)
        {
            if (ShowsNameField(section.Title, activity.CategoryName))
            {
                Add(section.Title, ActivityNameQuestion, activity.Name?.Trim());
            }

            foreach (var question in questions)
            {
                answers.TryGetValue(question.Code, out var value);
                Add(
                    section.Title,
                    question.Label,
                    ReportAnswerPresenter.RenderAnswer(
                        question.QuestionType,
                        question.Options.OrderBy(option => option.DisplayOrder).Select(option => (option.Value, option.Label)).ToList(),
                        null,
                        value));
            }
        }

        foreach (var (section, questions) in sections.Where(group => group.Section.Title != AdditionalInformationTitle))
        {
            AddSection(section, questions);
        }

        foreach (var row in ExpectedResultRows(activity, taxonomy))
        {
            Add(ExpectedResultsSection, row.Question, row.Answer);
        }

        foreach (var (section, questions) in sections.Where(group => group.Section.Title == AdditionalInformationTitle))
        {
            AddSection(section, questions);
        }

        // A fixed status (Joint partnership) is shown read-only and asks no start date (BLUM-8090), so the row stays
        // but the date never follows it — even on a record created before then, which may still carry one.
        Add(ActivityStatusSection, copy.StatusQuestion, StatusLabel(activity.Status, activity.IsDraft));
        var startDate = FormatStartDate(activity.StartMonth, activity.StartYear);
        if (!copy.ForcedStatus && activity.Status == ActivityStatuses.Active && startDate is not null)
        {
            Add(ActivityStatusSection, copy.StartQuestion, startDate);
        }

        // Asked only for Closed (BLUM-7673). Listed even when a record closed before the question existed has no
        // answer, like every other visible question — blank here, "Not provided" on the page.
        if (!copy.ForcedStatus && activity.Status == ActivityStatuses.Closed)
        {
            Add(ActivityStatusSection, copy.ClosureQuestion, activity.ClosureReason);
        }

        return rows;
    }

    // What one finalised quarterly report said about this activity: its reference, the results and delivery answers
    // it submitted, and the spend entries it recorded. Each report restates only its own entries — never a total
    // across reports, because a later report's figures already carry earlier quarters.
    public static IReadOnlyList<ActivityRecordRow> ReportRows(
        SubmittedReportRef report,
        IReadOnlyList<(ResolvedQuestion Question, string Value)> answers,
        IReadOnlyList<SubmittedActivitySpendRow> spend)
    {
        var label = ReportLabel(report);
        var rows = new List<ActivityRecordRow>
        {
            new(label, QuarterlyReportSection, "Reference number", report.ReferenceNumber ?? string.Empty),
            new(label, QuarterlyReportSection, "Submitted", FormatTimestamp(report.SubmittedAt)),
        };

        foreach (var (section, sectionTitle) in new[]
                 {
                     (KeyResultQuestionResolver.ResultsSection, ActivityResultsSection),
                     (KeyResultQuestionResolver.DeliverySection, ActivityDeliverySection),
                 })
        {
            rows.AddRange(answers
                .Where(answer => answer.Question.Section == section)
                .Select(answer => new ActivityRecordRow(
                    label,
                    sectionTitle,
                    answer.Question.Label,
                    ReportAnswerPresenter.RenderAnswer(ReportQuestionMapper.ToDto(answer.Question), answer.Value))));
        }

        rows.AddRange(spend
            .Where(entry => !entry.IsForecast)
            .Select(entry => new ActivityRecordRow(label, FundingSpendSection, ActualSpendQuestion, ReportSnapshotMoney.Format(entry.Amount))));
        rows.AddRange(spend
            .Where(entry => entry.IsForecast)
            .OrderBy(entry => entry.Quarter)
            .Select(entry => new ActivityRecordRow(
                label,
                FundingSpendSection,
                $"Q{entry.Quarter?.ToString(CultureInfo.InvariantCulture)} forecast (£)",
                ReportSnapshotMoney.Format(entry.Amount))));

        return rows;
    }

    // Port of display.tsx isActivitySectionApplicable: People and roles gets Role details alone; every other category
    // gets Activity details, and Education/Help additionally get People and issues. Additional information applies to
    // everything but People and roles; any other (DHSC-authored) section applies everywhere.
    public static bool IsSectionApplicable(string sectionTitle, string? categoryName)
    {
        var isPeople = categoryName == CategoryNames.PeopleAndRoles;
        if (sectionTitle == AdditionalInformationTitle)
        {
            return !isPeople;
        }

        if (!DetailSectionTitles.Contains(sectionTitle))
        {
            return true;
        }

        if (isPeople)
        {
            return sectionTitle == RoleDetailsTitle;
        }

        if (categoryName is CategoryNames.EducationCampaignsAndOutreach or CategoryNames.HelpAdviceAndAccessToServices)
        {
            return sectionTitle is PeopleAndIssuesTitle or ActivityDetailsTitle;
        }

        return sectionTitle == ActivityDetailsTitle;
    }

    // Port of display.tsx formatStartDate: "3", 2026 → "March 2026"; null when either part is missing.
    public static string? FormatStartDate(int? month, int? year)
    {
        if (month is null || year is null)
        {
            return null;
        }

        var name = month is >= 1 and <= 12
            ? EnGb.DateTimeFormat.GetMonthName(month.Value)
            : month.Value.ToString(CultureInfo.InvariantCulture);
        return $"{name} {year.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    // Port of display.tsx statusLabel.
    public static string StatusLabel(string? status, bool isDraft) =>
        isDraft || status is null || !StatusLabels.TryGetValue(status, out var label) ? "Draft" : label;

    // "Q2 2026/27", falling back to whatever of the collection's setup was recorded.
    public static string ReportLabel(SubmittedReportRef report)
    {
        if (string.IsNullOrWhiteSpace(report.FinancialYear))
        {
            return report.CollectionName;
        }

        return report.Quarter is { } quarter
            ? $"Q{quarter.ToString(CultureInfo.InvariantCulture)} {report.FinancialYear}"
            : report.FinancialYear;
    }

    // Port of the view page's expected-results card (completed layout). A type with standard key results lists the
    // selected measures (live taxonomy label, falling back to the label snapshotted on the record) plus "Other", then
    // the custom texts; a free-text-only type lists its measures as Measure 1, Measure 2, ...
    private static IEnumerable<(string Question, string Answer)> ExpectedResultRows(ActivityDto activity, ActivityTaxonomyDto taxonomy)
    {
        if (activity.ExpectedResults.Count == 0)
        {
            yield break;
        }

        var standard = activity.ExpectedResults
            .Where(row => row.KeyResultCode is not null)
            .Select(row => taxonomy.KeyResults.FirstOrDefault(keyResult => keyResult.Code == row.KeyResultCode)?.Label ?? row.Text)
            .Where(label => !string.IsNullOrEmpty(label))
            .ToList();
        var custom = activity.ExpectedResults
            .Where(row => row.KeyResultCode is null)
            .Select(row => row.Text.Trim())
            .Where(text => text.Length > 0)
            .ToList();

        var hasStandardOptions = activity.TaskId is { } taskId && taxonomy.KeyResults.Any(keyResult => keyResult.TaskId == taskId);
        if (hasStandardOptions)
        {
            var selected = custom.Count > 0 ? standard.Append("Other") : standard;
            yield return (ExpectedResultsQuestion, string.Join(", ", selected));
            if (custom.Count > 0)
            {
                yield return (OtherMeasureQuestion, string.Join(", ", custom));
            }

            yield break;
        }

        for (var index = 0; index < custom.Count; index++)
        {
            yield return ($"Measure {(index + 1).ToString(CultureInfo.InvariantCulture)}", custom[index]);
        }
    }

    // Port of display.tsx sectionShowsNameField: roles are named by their type, and a Joint partnership has no name
    // (BLUM-8090).
    private static bool ShowsNameField(string sectionTitle, string? categoryName) =>
        sectionTitle == ActivityDetailsTitle && categoryName is not (CategoryNames.PeopleAndRoles or CategoryNames.JointPartnership);

    private static string FormatTimestamp(DateTime utc)
    {
        var uk = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), UkTimeZone);
        return $"{uk.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)";
    }

    // Port of display.tsx activityCopy (+ forcedActivityStatus): role wording for People and roles, and Joint
    // partnership's fixed status. Apostrophes are copied exactly — the activity status question uses a typographic one,
    // the others a straight one.
    private static ActivityCopy CopyFor(string? categoryName)
    {
        var copy = categoryName == CategoryNames.PeopleAndRoles
            ? new ActivityCopy(
                "Role type",
                "What's the role?",
                "What's the name of the other role?",
                "What's the status of this role?",
                "When did the staff member start work?",
                "Why is this role being closed?",
                ForcedStatus: false)
            : new ActivityCopy(
                "Activity type",
                "What type of work does it involve?",
                "What's the activity?",
                "What’s the status of this activity?",
                "When did the work start?",
                "Why is this activity being closed?",
                ForcedStatus: false);

        return categoryName == CategoryNames.JointPartnership ? copy with { ForcedStatus = true } : copy;
    }

    private sealed record ActivityCopy(
        string TypeTitle,
        string TypeQuestion,
        string OtherLabel,
        string StatusQuestion,
        string StartQuestion,
        string ClosureQuestion,
        bool ForcedStatus);
}
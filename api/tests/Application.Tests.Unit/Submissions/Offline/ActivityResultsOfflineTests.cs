using Application.Submissions.Offline;
using Domain.Activities;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Xunit;
using static Application.Tests.Unit.Submissions.Offline.OfflineFixture;

namespace Application.Tests.Unit.Submissions.Offline;

// "Enter activity results" as an offline spreadsheet (BLUM-8310): one row per activity, one column per question, and
// the conditional logic the web form shows and hides carried as helper text instead.
public class ActivityResultsOfflineTests
{
    private static readonly ActivitySpec ActiveRna = new(
        RnaTaskName, ActivityStatus.Active, "Regional assessment", ["rna_contributors", "rna_information"], ["Hold a workshop"]);

    private static readonly ActivitySpec FinishedRna = new(
        RnaTaskName, ActivityStatus.Finished, "Finished assessment", ["rna_contributors", "rna_published"]);

    private static readonly ActivitySpec ClosedRna = new(
        RnaTaskName, ActivityStatus.Closed, "Closed assessment", ["rna_contributors", "rna_published"]);

    [Fact]
    public void ShouldGiveEachStatusWordingItsOwnColumn_AndNumberOnlyTheQuestions()
    {
        var (sheet, _) = ActivityResults(ActiveRna, FinishedRna);

        sheet.Columns.Select(column => column.Key).Should().StartWith(["ref", "activity", "type", "status", "errors"]);
        sheet.Columns.Select(column => column.Key).Should().Contain(
        [
            "RNA_CONTRIBUTORS@active",
            "RNA_CONTRIBUTORS@closed_finished",
            "RNA_PUBLISHED@closed_finished",
            "RNA_PUBLISHED_URL@finished",
            "own_kr_1_name",
            "own_kr_1",
            "AKR_DELIVERY_PROGRESS",
        ]);

        var questions = sheet.Columns.Where(column => column.Kind == OfflineColumnKind.Question).ToList();
        questions.Select(column => column.QuestionNumber).Should().Equal(Enumerable.Range(1, questions.Count).Select(n => (int?)n));
        sheet.Column("RNA_CONTRIBUTORS@active").Heading.Should().Be("Q1. Who is contributing evidence?");
        sheet.Column("RNA_CONTRIBUTORS@closed_finished").Heading.Should().EndWith(". Who contributed evidence?");
        sheet.Column("own_kr_1_name").QuestionNumber.Should().BeNull();
    }

    [Fact]
    public void ShouldKeepEachStatusBlockTogether_WithAFinishedOnlyFollowUpNextToItsParent()
    {
        var (sheet, _) = ActivityResults(ActiveRna, FinishedRna);

        var published = sheet.Column("RNA_PUBLISHED@closed_finished");
        var url = sheet.Column("RNA_PUBLISHED_URL@finished");
        url.Index.Should().Be(published.Index + 1);
        sheet.Column("RNA_CONTRIBUTORS@closed_finished").Index.Should().BeGreaterThan(sheet.Column("RNA_INFORMATION_OTHER@active").Index);
    }

    [Fact]
    public void ShouldPutNotApplicableInCellsARowIsNotAsked()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna, FinishedRna);

        var records = Records(OfflineTemplateWriter.Write(sheet));
        var active = records.Single(record => record.Cells[0] == inputs[0].Activity.Id.Value.ToString());
        var finished = records.Single(record => record.Cells[0] == inputs[1].Activity.Id.Value.ToString());

        active.CellAt(sheet.Column("RNA_PUBLISHED@closed_finished").Index).Should().Be("N/A");
        active.CellAt(sheet.Column("RNA_CONTRIBUTORS@active").Index).Should().BeEmpty();
        active.CellAt(sheet.Column("own_kr_1_name").Index).Should().Be("Hold a workshop");
        finished.CellAt(sheet.Column("RNA_CONTRIBUTORS@active").Index).Should().Be("N/A");
        finished.CellAt(sheet.Column("own_kr_1_name").Index).Should().Be("N/A");
        finished.CellAt(sheet.Column("own_kr_1").Index).Should().Be("N/A");
    }

    [Fact]
    public void ShouldWriteTheIdentityAndTheColumnKeysOnRowThree()
    {
        var (sheet, _) = ActivityResults(ActiveRna);

        var keyRow = Records(OfflineTemplateWriter.Write(sheet))[2];

        keyRow.Cells[0].Should().StartWith($"offline-template|v1|activity_key_results|{ReportCollectionId:N}|{ReportOrganisationId:N}|");
        keyRow.Cells.Skip(1).Should().Equal(sheet.Columns.Skip(1).Select(column => column.Key));
    }

    [Fact]
    public void ShouldTellTheReaderWhereAnOtherAnswerLeads()
    {
        var (sheet, _) = ActivityResults(ActiveRna);

        var parent = sheet.Column("RNA_CONTRIBUTORS@active");
        var child = sheet.Column("RNA_CONTRIBUTORS_OTHER@active");

        parent.Guidance.Should().Contain($"If you choose ‘Other’, also answer {child.Name}. If not, leave {child.ShortName} blank.");
        parent.Guidance.Should().Contain("separated by a semicolon (;)");
        child.Guidance.Should().Contain($"Only answer if you chose ‘Other’ in {parent.Name} – an answer is then required. Otherwise leave it blank.");
    }

    [Fact]
    public void ShouldWordAYesNoFollowUpAsASkip_AndSayWhenItCanBeNotApplicable()
    {
        var (sheet, _) = ActivityResults(FinishedRna, ClosedRna);

        var published = sheet.Column("RNA_PUBLISHED@closed_finished");
        var url = sheet.Column("RNA_PUBLISHED_URL@finished");
        var next = sheet.Columns.First(column => column.Index > url.Index && column.Kind == OfflineColumnKind.Question);

        published.Guidance.Should().Contain($"If you answer ‘Yes’, answer {url.Name} next, unless it says N/A.");
        published.Guidance.Should().Contain($"If you answer ‘No’, leave {url.ShortName} blank and go to {next.Name}.");
        url.Guidance.Should().Contain($"Only answer if you answered ‘Yes’ to {published.Name}");
        url.Guidance.Should().StartWith($"For ‘{RnaTaskName}’ activities that are Finished.");
    }

    [Fact]
    public void ShouldSkipToTheNextQuestionTheSameActivitiesAreAsked_NotAnotherTypesColumn()
    {
        var (sheet, _) = ActivityResults(
            FinishedRna,
            new ActivitySpec(SteeringGroupTaskName, ActivityStatus.Active, "Steering group", ["lsg_members"]));

        var url = sheet.Column("RNA_PUBLISHED_URL@finished");
        var steeringGroup = sheet.Column("LSG_MEMBERS@active");
        var delivery = sheet.Column("AKR_DELIVERY_PROGRESS");

        steeringGroup.Index.Should().Be(url.Index + 1, "the steering group's columns follow the needs assessment's");
        sheet.Column("RNA_PUBLISHED@closed_finished").Guidance.Should().Contain($"leave {url.ShortName} blank and go to {delivery.Name}.");
    }

    [Fact]
    public void ShouldSummariseAParentWithManyFollowUpsAsARange()
    {
        var (sheet, _) = ActivityResults(new ActivitySpec(SteeringGroupTaskName, ActivityStatus.Active, "Steering group", ["lsg_members"]));

        var members = sheet.Column("LSG_MEMBERS@active");
        var followUps = sheet.Columns.Where(column => column.Key.StartsWith("LSG_ENGAGEMENT_") || column.Key.StartsWith("LSG_MEMBERS_OTHER")).ToList();
        var first = followUps.MinBy(column => column.Index)!;
        var last = followUps.MaxBy(column => column.Index)!;

        followUps.Should().HaveCountGreaterThan(3);
        members.Guidance.Should().Contain($"Some answers need a follow-up in {first.ShortName} to {last.ShortName} (columns {first.Letter} to {last.Letter}).");
        sheet.Column("LSG_ENGAGEMENT_POLICE@active").Guidance.Should().Contain($"Only answer if you chose ‘Police’ in {members.Name}");
    }

    [Fact]
    public void ShouldNotWriteAPrefilledFollowUpItsPrefilledParentHides()
    {
        // The activity record prefills both the contributors and the "who else" box; the web form hides the box
        // because "Other" is not among the prefilled contributors, so the file must not show it either.
        var spec = ActiveRna with
        {
            InitialValues = new Dictionary<string, string?>
            {
                ["RNA_CONTRIBUTORS"] = "police",
                ["RNA_CONTRIBUTORS_OTHER"] = "The coroner",
            },
        };
        var (sheet, inputs) = ActivityResults(spec);

        var row = Records(OfflineTemplateWriter.Write(sheet)).Single(record => record.Cells[0] == inputs[0].Activity.Id.Value.ToString());

        row.CellAt(sheet.Column("RNA_CONTRIBUTORS@active").Index).Should().Be("Police");
        row.CellAt(sheet.Column("RNA_CONTRIBUTORS_OTHER@active").Index).Should().BeEmpty();
    }

    [Fact]
    public void ShouldReadAFilledTemplateBack_AsTheValuesTheWebWouldStore()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna, FinishedRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var finished = inputs[1].Activity.Id.Value.ToString();

        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (active, "RNA_CONTRIBUTORS@active", "other; POLICE"),
            (active, "RNA_CONTRIBUTORS_OTHER@active", "The coroner"),
            (active, "RNA_INFORMATION@active", "Prevalence data"),
            (active, "own_kr_1", "- Ran two workshops\n- Wrote it up"),
            (active, "AKR_DELIVERY_PROGRESS", "Good progress"),
            (finished, "RNA_CONTRIBUTORS@closed_finished", "‘Licensing’"),
            (finished, "RNA_PUBLISHED@closed_finished", "yes"),
            (finished, "RNA_PUBLISHED_URL@finished", "https://example.org/assessment"),
            (finished, "AKR_DELIVERY_PROGRESS", "Done"));

        var result = ValidateActivityResults(sheet, file);

        result.Errors.Should().BeEmpty();
        var activeAnswers = result.Rows.Single(row => row.Row.Reference == active).Answers;
        activeAnswers["RNA_CONTRIBUTORS"].Should().Be("police,other", "values are stored comma-joined in option order");
        activeAnswers["RNA_CONTRIBUTORS_OTHER"].Should().Be("The coroner");
        activeAnswers["RNA_INFORMATION"].Should().Be("prevalence");
        activeAnswers[$"CUSTOM_{inputs[0].CustomResults[0].Id.Value:N}"].Should().Be("- Ran two workshops\n- Wrote it up", "the formula guard is taken off again");
        var finishedAnswers = result.Rows.Single(row => row.Row.Reference == finished).Answers;
        finishedAnswers["RNA_CONTRIBUTORS"].Should().Be("licensing");
        finishedAnswers["RNA_PUBLISHED"].Should().Be("yes");
    }

    [Fact]
    public void ShouldReportAFollowUpAnsweredWithoutItsTrigger()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (active, "RNA_CONTRIBUTORS@active", "Police"),
            (active, "RNA_CONTRIBUTORS_OTHER@active", "The coroner"),
            (active, "RNA_INFORMATION@active", "Prevalence data"),
            (active, "own_kr_1", "Progress"),
            (active, "AKR_DELIVERY_PROGRESS", "Progress"));

        var result = ValidateActivityResults(sheet, file);

        var child = sheet.Column("RNA_CONTRIBUTORS_OTHER@active");
        result.Errors.Should().ContainSingle().Which.Should().Be(new OfflineCellError(
            4, child.Index, child.QuestionNumber, "You answered Q2 but did not choose ‘Other’ in Q1. Choose ‘Other’ in Q1 or clear Q2"));
    }

    private static readonly ActivitySpec Chain = new(ChainTaskName, ActivityStatus.Active, "Chain", [ChainKeyResult]);

    // CHAIN_A → CHAIN_B → CHAIN_C. "No" to A hides B, which the file answered anyway, and with BLUM_9087 that hides C
    // too. One cause, so one error, on B: C's would blame B for a "Yes" the file does give it.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldReportAChainOnce_AtItsTopmostHiddenAnswer(bool transitive)
    {
        var (sheet, inputs) = ActivityResults(Chain);
        var row = inputs[0].Activity.Id.Value.ToString();
        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (row, "CHAIN_A@active", "No"),
            (row, "CHAIN_B@active", "Yes"),
            (row, "CHAIN_C@active", "Notes"),
            (row, "AKR_DELIVERY_PROGRESS", "Progress"));

        var result = ValidateActivityResults(sheet, file, transitive);

        result.Errors.Should().ContainSingle().Which.ColumnIndex.Should().Be(sheet.Column("CHAIN_B@active").Index);
    }

    // A cell that fails to read is the error to fix; nothing beneath it is reported as well.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldReportOnlyTheCellThatFailedToRead_NotTheFollowUpsBeneathIt(bool transitive)
    {
        var (sheet, inputs) = ActivityResults(Chain);
        var row = inputs[0].Activity.Id.Value.ToString();
        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (row, "CHAIN_A@active", "Maybe"),
            (row, "CHAIN_B@active", "Yes"),
            (row, "CHAIN_C@active", "Notes"),
            (row, "AKR_DELIVERY_PROGRESS", "Progress"));

        var result = ValidateActivityResults(sheet, file, transitive);

        result.Errors.Should().ContainSingle().Which.Should().Be(new OfflineCellError(
            4, sheet.Column("CHAIN_A@active").Index, sheet.Column("CHAIN_A@active").QuestionNumber, "Enter ‘Yes’ or ‘No’"));
    }

    [Fact]
    public void ShouldReportAnAnswerInACellTheRowIsNotAsked_AndRequiredAnswersMissing()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna, FinishedRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var file = Edit(OfflineTemplateWriter.Write(sheet), (active, "RNA_PUBLISHED@closed_finished", "Yes"));

        var result = ValidateActivityResults(sheet, file);

        var published = sheet.Column("RNA_PUBLISHED@closed_finished");
        result.Errors.Should().Contain(new OfflineCellError(4, published.Index, published.QuestionNumber, OfflineUploadValidator.NotApplicableMessage));
        result.Errors.Should().Contain(error =>
            error.Row == 4 && error.ColumnIndex == sheet.Column("RNA_CONTRIBUTORS@active").Index && error.Message == "Select who is contributing evidence");
        result.Errors.Should().OnlyContain(error => !error.Message.EndsWith('.'));
    }

    [Fact]
    public void ShouldExplainAnAnswerThatIsNotAnOption()
    {
        var (sheet, inputs) = ActivityResults(FinishedRna);
        var finished = inputs[0].Activity.Id.Value.ToString();
        var file = Edit(
            OfflineTemplateWriter.Write(sheet),
            (finished, "RNA_CONTRIBUTORS@closed_finished", "Police, Martians"),
            (finished, "RNA_PUBLISHED@closed_finished", "Maybe"));

        var result = ValidateActivityResults(sheet, file);

        result.Errors.Should().Contain(error => error.Message == "‘Police, Martians’ is not one of the answers listed in row 2. Separate answers with a semicolon (;)");
        result.Errors.Should().Contain(error => error.Message == "Enter ‘Yes’ or ‘No’");
    }

    [Fact]
    public void ShouldReportUnknownDuplicateAndMissingRows()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna, FinishedRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var finished = inputs[1].Activity.Id.Value.ToString();

        var records = Records(RemoveRow(OfflineTemplateWriter.Write(sheet), finished)).Select(record => record.Cells).ToList();
        var activeRow = records.Single(cells => cells[0] == active);
        records.Add(activeRow);
        records.Add(activeRow.Select((cell, index) => index == 0 ? Guid.NewGuid().ToString() : cell).ToList());
        var file = Application.Common.Csv.CsvWriter.Build(records[0], records.Skip(1), byteOrderMark: true);

        var result = ValidateActivityResults(sheet, file);

        result.Errors.Should().Contain(new OfflineCellError(5, 0, null, "This activity is also in row 4. Delete one of the rows"));
        result.Errors.Should().Contain(new OfflineCellError(
            6, 0, null, "The reference in column A does not match an activity in this report. Do not change column A"));
        result.Errors.Should().Contain(new OfflineCellError(7, null, null, "Finished assessment is missing from your file. Answer its questions in this row"));
        result.AppendedRows.Should().ContainSingle().Which.RowNumber.Should().Be(7);
    }

    [Fact]
    public void ShouldWriteErrorsIntoThePersonsOwnFile_AndAppendMissingRows()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna, FinishedRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var finished = inputs[1].Activity.Id.Value.ToString();
        var withOldErrors = Edit(
            OfflineTemplateWriter.Write(sheet),
            (active, "errors", "An error from last time"),
            (active, "RNA_CONTRIBUTORS@active", "Police"),
            (active, "RNA_CONTRIBUTORS_OTHER@active", "The coroner"));
        var file = RemoveRow(withOldErrors, finished);
        var upload = Upload(file);

        var result = OfflineUploadValidator.Validate(sheet, upload, rows => OfflineSectionRules.ActivityResults(rows, transitive: false), transitive: false);
        var annotated = Records(OfflineFileAnnotator.Annotate(sheet, upload, result));

        var original = Records(file);
        annotated.Should().HaveCount(original.Count + 1);
        var errorsIndex = sheet.ErrorsColumn.Index;
        var activeRow = annotated.Single(record => record.Cells[0] == active);
        activeRow.Cells[errorsIndex].Should().Contain("Q2 (column G): You answered Q2 but did not choose ‘Other’ in Q1");
        activeRow.Cells[errorsIndex].Should().NotContain("An error from last time");
        activeRow.Cells.Where((_, index) => index != errorsIndex).Should().Equal(
            original.Single(record => record.Cells[0] == active).Cells.Where((_, index) => index != errorsIndex));
        annotated[^1].Cells[0].Should().Be(finished);
        annotated[^1].Cells[errorsIndex].Should().Be("Finished assessment is missing from your file. Answer its questions in this row");
        annotated.Take(3).Select(record => record.Cells).Should().BeEquivalentTo(original.Take(3).Select(record => record.Cells), "the heading, guidance and key rows are left alone");
    }

    [Fact]
    public void ShouldFindTheKeyRowAfterASort_AndReadASemicolonFile()
    {
        var (sheet, inputs) = ActivityResults(ActiveRna);
        var active = inputs[0].Activity.Id.Value.ToString();
        var filled = Edit(
            OfflineTemplateWriter.Write(sheet),
            (active, "RNA_CONTRIBUTORS@active", "Police"),
            (active, "RNA_INFORMATION@active", "Prevalence data"),
            (active, "own_kr_1", "Progress"),
            (active, "AKR_DELIVERY_PROGRESS", "Progress"));

        // Excel's "sort, my data has headers" moves rows 2 and 3 down among the answers; some locales save with ";".
        var records = Records(filled).Select(record => record.Cells).ToList();
        var sorted = new List<IReadOnlyList<string>> { records[0], records[3], records[1], records[2] };
        var text = string.Join("\r\n", sorted.Select(cells => string.Join(';', cells.Select(cell => cell.Contains(';') || cell.Contains('\n') || cell.Contains('"') ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell))));

        var result = ValidateActivityResults(sheet, System.Text.Encoding.UTF8.GetBytes(text));

        result.Errors.Should().BeEmpty();
        result.Rows.Should().ContainSingle().Which.RowNumber.Should().Be(2);
    }

    [Fact]
    public void ShouldChangeTheFingerprintWhenAStatusChanges_ButNotWhenAnswersDo()
    {
        var spec = new ActivitySpec(RnaTaskName, ActivityStatus.Active, "Assessment", ["rna_contributors"], Id: ActivityId.New());

        var (downloaded, _) = ActivityResults(spec);
        var (answered, _) = ActivityResults(spec with { InitialValues = new Dictionary<string, string?> { ["RNA_CONTRIBUTORS"] = "police" } });
        var (finished, _) = ActivityResults(spec with { Status = ActivityStatus.Finished });

        answered.Fingerprint.Should().Be(downloaded.Fingerprint, "saving answers does not make a downloaded file out of date");
        finished.Fingerprint.Should().NotBe(downloaded.Fingerprint, "a status change moves the activity to other columns");
        OfflineIdentity.ComputeFingerprint(["a", "b"]).Should().Be(OfflineIdentity.ComputeFingerprint(["b", "a"]));
    }

    [Fact]
    public void ProtocolWordings_AreOnePerCodeAndStatus()
    {
        // The column key is {Code}@{scope}: two wordings of one code in one status would share a column.
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            type.Questions
                .GroupBy(question => (question.Code, question.StatusScope))
                .Should().OnlyContain(group => group.Count() == 1, "type \"{0}\" must word each question once per status", type.TaskName);
        }
    }
}
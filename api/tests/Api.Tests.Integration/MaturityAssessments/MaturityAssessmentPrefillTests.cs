using Api.Dtos;
using Application.Common.Interfaces.Queries;
using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using Domain.MaturityAssessments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// Pre-fill at start (BLUM-7856): the 2025 baseline, the organisation's prior submitted assessment and its latest
// SUBMITTED quarterly report, in ascending precedence; every source is recorded on the answer, and nothing is
// pre-marked complete.
public class MaturityAssessmentPrefillTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private readonly IntegrationTestWebFactory _factory = factory;

    // The quarterly source's read: the organisation's latest SUBMITTED answers per activity. Exercised directly
    // because the pre-fill service swallows a failing source (best effort) — a broken query would otherwise show up
    // only as "nothing was pre-filled".
    [Fact]
    public async Task TheLatestSubmittedAnswersQuery_ReturnsTheSubmittedReportsRows()
    {
        AuthenticateAsMainOrgLead();
        var quarterlyId = await CreateOpenCollectionAsync("Q1 for the answers query");
        var activityId = await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        await using var scope = _factory.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISubmissionQueries>();
        var rows = await queries.GetLatestSubmittedActivityAnswersForOrganisationAsync(MainOrg.Id, new DateOnly(2100, 3, 31));

        rows.Should().Contain(r => r.ActivityId == activityId && r.QuestionCode == "RNW_MEMBERSHIP" && r.Value == "yes");
        rows.Should().OnlyContain(r => r.SubmittedAt != default);

        // And the service built on it, called directly so a failing source surfaces here as an exception.
        var collectionId = await CreateMaturityCollectionAsync("Prefill probe");
        var collection = await Context.Collections.AsNoTracking().FirstAsync(c => c.Id == new CollectionId(collectionId));
        var prefill = scope.ServiceProvider.GetRequiredService<IMaturityPrefillService>();
        var prefilled = await prefill.BuildAsync(MainOrg, collection, MaturityQuestionResolver.ResolveAll(), CancellationToken.None);

        prefilled.Should().Contain(a => a.QuestionCode == "MA_STRATEGIC_NETWORK" && a.Value == "yes" && a.Source.Equals(AnswerSource.Quarterly));
    }

    [Fact]
    public async Task Start_PrefillsFromTheBaseline_AndLeavesEverySectionNotStarted()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, ("MA_DESIGNATED_LEAD", "yes"), ("MA_PRIORITIES", "Licensing policy review"));

        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.IsPrefilled.Should().BeTrue();
        taskList.PrefillSources.Should().Equal(AnswerSources.Baseline2025);
        taskList.Sections.Should().OnlyContain(s => s.Status == "not_started");

        var answers = await GetAnswersAsync(collectionId);
        answers.Should().HaveCount(2);
        answers.Should().OnlyContain(a => a.Source.Equals(AnswerSource.Baseline2025));

        var section = await GetSectionAsync(collectionId, "next_steps");
        section.Answers.Should().ContainSingle().Which.Should().Be(new MaturityAnswerDto("MA_PRIORITIES", "Licensing policy review", AnswerSources.Baseline2025));
    }

    // PR #87 review: the pre-fill rows went into the start transaction, so one the table refused rolled back the
    // whole start. The task list starts the assessment on every visit and every retry builds the same rows, so that
    // LA could never open its assessment. It must start blank instead, and say so on its audit row.
    [Fact]
    public async Task Start_StartsBlank_WhenThePrefilledAnswersCannotBeWritten()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, ("MA_DESIGNATED_LEAD", "yes"), ("MA_PRIORITIES", "Licensing policy review"));

        AuthenticateAsMainOrgLead();
        await RejectedInserts.RejectAllAsync(Context, "maturity_assessment_answers");
        MaturityTaskListDto taskList;
        try
        {
            taskList = await StartAssessmentAsync(collectionId);
        }
        finally
        {
            await RejectedInserts.RestoreAsync(Context);
        }

        taskList.IsPrefilled.Should().BeFalse();
        taskList.Sections.Should().OnlyContain(s => s.Status == "not_started");
        (await GetAnswersAsync(collectionId)).Should().BeEmpty();

        var assessment = await Context.MaturityAssessments.AsNoTracking().SingleAsync(a => a.CollectionId == new CollectionId(collectionId));
        var started = await Context.AuditLogs.AsNoTracking()
            .SingleAsync(x => x.TargetId == assessment.Id.Value && x.Action == Domain.AuditLogs.AuditAction.MaturityAssessmentStarted);
        using var after = System.Text.Json.JsonDocument.Parse(started.After!);
        after.RootElement.GetProperty("prefillFailed").GetBoolean().Should().BeTrue();
        after.RootElement.GetProperty("prefilledAnswers").EnumerateObject().Should().BeEmpty("the counts are the rows that landed, and none did");
    }

    [Fact]
    public async Task Start_PrefersThePriorSubmittedAssessment_OverTheBaseline()
    {
        var previousYear = TestFinancialYears.Previous(TestFinancialYears.Current);
        var priorCollectionId = await CreateMaturityCollectionAsync("Last year", financialYear: previousYear);
        await SeedSubmittedAssessmentAsync(
            priorCollectionId,
            MainOrg,
            TestFinancialYears.MaturityReference(previousYear, 1),
            answers: [("MA_DESIGNATED_LEAD", "no"), ("MA_NA_PUBLISHED", "retired_option"), ("MA_STAFF_SUPPORT", "yes")]);
        await SeedBaselineAnswersAsync(MainOrg, ("MA_DESIGNATED_LEAD", "yes"), ("MA_NA_PUBLISHED", "no"), ("MA_PRIORITIES", "From 2025"));

        var collectionId = await CreateMaturityCollectionAsync("This year");
        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.PrefillSources.Should().BeEquivalentTo([AnswerSources.Baseline2025, AnswerSources.PriorAssessment]);

        var answers = (await GetAnswersAsync(collectionId)).ToDictionary(a => a.QuestionCode);
        answers["MA_DESIGNATED_LEAD"].Value.Should().Be("no");
        answers["MA_DESIGNATED_LEAD"].Source.Should().Be(AnswerSource.PriorAssessment);
        answers["MA_STAFF_SUPPORT"].Source.Should().Be(AnswerSource.PriorAssessment);
        answers["MA_NA_PUBLISHED"].Value.Should().Be("no", "a prior value the catalogue no longer offers falls through to the baseline");
        answers["MA_NA_PUBLISHED"].Source.Should().Be(AnswerSource.Baseline2025);
        answers["MA_PRIORITIES"].Source.Should().Be(AnswerSource.Baseline2025);
    }

    [Fact]
    public async Task Start_PrefersTheSubmittedQuarterlyReport_OverThePriorAssessment()
    {
        var previousYear = TestFinancialYears.Previous(TestFinancialYears.Current);
        var priorCollectionId = await CreateMaturityCollectionAsync("Last year", financialYear: previousYear);
        await SeedSubmittedAssessmentAsync(
            priorCollectionId,
            MainOrg,
            TestFinancialYears.MaturityReference(previousYear, 1),
            answers: [("MA_STRATEGIC_NETWORK", "no")]);

        AuthenticateAsMainOrgLead();
        var quarterlyId = await CreateOpenCollectionAsync("Q1 for the maturity prefill");
        var activityId = await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        var collectionId = await CreateMaturityCollectionAsync("This year");
        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.PrefillSources.Should().Contain(AnswerSources.Quarterly);
        var answer = (await GetAnswersAsync(collectionId)).Single(a => a.QuestionCode == "MA_STRATEGIC_NETWORK");
        answer.Value.Should().Be("yes");
        answer.Source.Should().Be(AnswerSource.Quarterly);

        // AC5: editing the assessment never touches the quarterly report it was pre-filled from.
        (await SavePageAsync(collectionId, "la_capacity", "strategic_network", false, ("MA_STRATEGIC_NETWORK", "no"))).EnsureSuccessStatusCode();
        var quarterlyAnswers = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.QuestionCode == "RNW_MEMBERSHIP")
            .ToListAsync();
        quarterlyAnswers.Should().ContainSingle().Which.Value.Should().Be("yes");
        activityId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Start_IgnoresADraftQuarterlyReport()
    {
        AuthenticateAsMainOrgLead();
        var quarterlyId = await CreateOpenCollectionAsync("Q1 draft for the maturity prefill");
        await SaveRegionalNetworkReportAsync(quarterlyId, membership: "yes", submit: false);

        var collectionId = await CreateMaturityCollectionAsync("This year");
        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.IsPrefilled.Should().BeFalse();
        (await GetAnswersAsync(collectionId)).Should().BeEmpty();
    }
}
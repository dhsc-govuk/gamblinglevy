using System.Net;
using Api.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// The "Record funding spent" section (BLUM-6612): saving the Activity-spending and Forecast-spend screens, the
// section status roll-up, and the completion validation.
public class FundingSpendTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private sealed record ErrorEnvelope(string Error, string Message);

    private async Task<(Guid CollectionId, Guid ActivityId)> StartConfirmedReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity.Id);
    }

    [Fact]
    public async Task SaveActuals_AsDraft_SavesInProgressAndPersists()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 250m, (activityId, 1500m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<FundingSpendStateDto>();
        state.SectionStatus.Should().Be("in_progress");

        var entries = await Context.SubmissionSpendEntries.AsNoTracking().ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().Contain(e => e.ActivityId == new Domain.Activities.ActivityId(activityId) && e.Amount == 1500m);
        entries.Should().Contain(e => e.ActivityId == null && e.Amount == 250m);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).Status.Should().Be("in_progress");
    }

    [Fact]
    public async Task SaveActuals_WithZeroAmounts_IsAccepted()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activityId, 0m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = await Context.SubmissionSpendEntries.AsNoTracking().ToListAsync();
        entries.Should().HaveCount(2);
        entries.Should().OnlyContain(e => e.Amount == 0m);
    }

    [Fact]
    public async Task SaveActuals_WithNegativeAmount_Returns422EvenOnADraftSave()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: -5m, (activityId, 100m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "other-spend");

        (await Context.SubmissionSpendEntries.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SaveActuals_WithAmountOverMax_Returns422AndPersistsNothing()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activityId, 1_000_000_001m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"actual-{activityId}");

        (await Context.SubmissionSpendEntries.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SaveActuals_DoesNotWipeForecast()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();
        var funding = await GetFundingSpendAsync(collectionId);

        var forecastActivities = new[]
        {
            new FundingActivityForecastInputDto(
                activityId,
                funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 200m)).ToList()),
        };
        (await SaveFundingForecastAsync(collectionId, saveAsDraft: true, forecastActivities, [])).EnsureSuccessStatusCode();

        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 50m, (activityId, 300m)))
            .EnsureSuccessStatusCode();

        var entries = await Context.SubmissionSpendEntries.AsNoTracking().ToListAsync();
        entries.Count(e => e.Kind.IsForecast).Should().Be(funding.RemainingQuarters.Count);
        entries.Count(e => e.Kind.IsActual).Should().Be(2);
    }

    [Fact]
    public async Task Complete_WithMissingAmounts_Returns422()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartConfirmedReportAsync();

        var response = await CompleteFundingSpendAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode.StartsWith("actual-"));
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "other-spend");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode.StartsWith("forecast-"));

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).Status.Should().NotBe("completed");
    }

    [Fact]
    public async Task Complete_WithEverythingFilled_FlipsTheSectionToCompleted()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartConfirmedReportAsync();

        await CompleteFundingSpendSectionAsync(collectionId);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).Status.Should().Be("completed");
    }

    [Fact]
    public async Task SaveActuals_AfterCompleting_DropsTheSectionBackToInProgress()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();
        await CompleteFundingSpendSectionAsync(collectionId);

        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activityId, 1m)))
            .EnsureSuccessStatusCode();

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).Status.Should().Be("in_progress");
    }

    [Fact]
    public async Task GetFundingSpend_ForAQ1Collection_ReturnsTheForecastQuartersAfterQ1WithDates()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartConfirmedReportAsync();

        var funding = await GetFundingSpendAsync(collectionId);

        // Q1 is captured as actual spend, so the forecast covers only the quarters after it (Q2-Q4).
        funding.CurrentQuarter.Should().Be(1);
        funding.RemainingQuarters.Select(q => q.Quarter).Should().Equal(2, 3, 4);
        var firstForecastQuarter = funding.RemainingQuarters.Single(q => q.Quarter == 2);
        firstForecastQuarter.Start.Should().Be(new DateOnly(2026, 7, 1));
        firstForecastQuarter.End.Should().Be(new DateOnly(2026, 9, 30));
    }

    // BLUM-7216 review: an admin can revert a PUBLISHED collection to draft after LAs have started reporting, and
    // then clear its quarter / financial year. Requiring a submission does not shield the null-forgiving reads from
    // that — the submission already exists — so both LA report reads need the same IsDraft guard GetContextAsync
    // carries. Without it `collection.Quarter!.Value` throws and the LA gets an unhandled 500 on their own screens.
    [Fact]
    public async Task GetReportSections_WhenCollectionRevertedToDraftAndCleared_Return404NotAFault()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        // Raw SQL because the domain entity exposes no way to unset a published collection's setup fields.
        await Context.Database.ExecuteSqlRawAsync(
            """
            update collections
            set is_draft = true,
                quarter = null,
                financial_year = null
            where id = {0}
            """,
            collectionId);

        var funding = await Client.GetAsync($"quarterly-report/{collectionId}/funding-spend");
        var feedback = await Client.GetAsync($"quarterly-report/{collectionId}/strategic-feedback");

        funding.StatusCode.Should().Be(HttpStatusCode.NotFound);
        feedback.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFundingSpend_IncludesPlannedActivities()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var planned = await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        var active = await CreateRnaActivityAsync(status: "active", title: "Active work");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        // The funding report covers the LA's whole activity list, Planned activities included — unlike the
        // key-results report, which excludes them (see SaveActivityReportTests.Save_ForAPlannedActivity_Returns422).
        var ids = funding.Activities.Select(a => a.ActivityId).ToList();
        ids.Should().Contain(planned.Id);
        ids.Should().Contain(active.Id);
    }

    [Fact]
    public async Task GetFundingSpend_ForAQ4Collection_HasNoForecastQuartersAndCompletesWithActualsOnly()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(4, "Funding Q4");
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);
        funding.CurrentQuarter.Should().Be(4);
        funding.RemainingQuarters.Should().BeEmpty("the final quarter has no later quarters to forecast");

        // Only the actuals are required — there is nothing left to forecast, so no forecast save is needed.
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 200m, (activity.Id, 800m)))
            .EnsureSuccessStatusCode();

        var completed = await CompleteFundingSpendAsync(collectionId);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterComplete = await GetFundingSpendAsync(collectionId);
        afterComplete.Summary.TotalSpendThisQuarter.Should().Be(1_000m);
        // Full year with no earlier quarters and nothing to forecast = just this quarter's actual spend.
        afterComplete.Summary.ExpectedSpendForYear.Should().Be(1_000m);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FundingModuleCode).Status.Should().Be("completed");
    }

    [Fact]
    public async Task SaveActuals_BeforeConfirmingActivities_Returns422ActivitiesNotConfirmed()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activity.Id, 100m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
    }

    [Fact]
    public async Task SaveActuals_ForACollectionAssignedToAnotherOrg_Returns404()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activityId) = await StartConfirmedReportAsync();

        AuthenticateAsSecondOrgLead();
        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activityId, 100m));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
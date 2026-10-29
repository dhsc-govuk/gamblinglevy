using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// Pre-fill after start (BLUM-8927). The task list starts the assessment on the LA's first visit, so an LA that opens
// it BEFORE submitting a quarterly report used to keep a blank assessment for good. Submitting a report now pulls
// its data into every open assessment the organisation has started — on the pages the LA has not saved, and
// nowhere else.
public class MaturityAssessmentPrefillRefreshTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private const string StrategicNetwork = "MA_STRATEGIC_NETWORK";

    private readonly IntegrationTestWebFactory _factory = factory;

    [Fact]
    public async Task ShouldPrefillAnAssessmentStartedBeforeTheReportWasSubmitted()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        var started = await StartAssessmentAsync(collectionId);
        started.IsPrefilled.Should().BeFalse("nothing has been submitted yet");

        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted after the assessment was started");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        var answer = (await GetAnswersAsync(collectionId)).Should().ContainSingle().Subject;
        answer.QuestionCode.Should().Be(StrategicNetwork);
        answer.Value.Should().Be("yes");
        answer.Source.Should().Be(AnswerSource.Quarterly);
        answer.CreatedBy.Value.Should().Be(MainOrgLeadUserId);

        // The LA is told, and nothing is pre-marked: the section is theirs to review.
        var taskList = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        taskList.IsPrefilled.Should().BeTrue();
        taskList.PrefillSources.Should().Equal(AnswerSources.Quarterly);
        taskList.Sections.Should().OnlyContain(s => s.Status == "not_started");

        var assessment = await GetAssessmentAsync(collectionId);
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.MaturityAssessmentPrefillRefreshed)
            .ToListAsync();
        var row = audit.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.MaturityAssessment);
        row.TargetId.Should().Be(assessment.Id.Value);
        row.ActorUserId.Value.Should().Be(MainOrgLeadUserId);
        row.OrganisationId.Should().Be(MainOrg.Id);
    }

    [Fact]
    public async Task ShouldReplaceAnAnswerThatIsStillPrefilled()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, (StrategicNetwork, "no"), ("MA_DESIGNATED_LEAD", "yes"));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted over a baseline answer");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        var answers = (await GetAnswersAsync(collectionId)).ToDictionary(a => a.QuestionCode);
        answers.Should().HaveCount(2);
        answers[StrategicNetwork].Value.Should().Be("yes");
        answers[StrategicNetwork].Source.Should().Be(AnswerSource.Quarterly);
        answers["MA_DESIGNATED_LEAD"].Value.Should().Be("yes", "a question the report says nothing about keeps what it had");
        answers["MA_DESIGNATED_LEAD"].Source.Should().Be(AnswerSource.Baseline2025);
    }

    [Fact]
    public async Task ShouldKeepAPageTheLocalAuthoritySaved()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        (await SavePageAsync(collectionId, "la_capacity", "strategic_network", true, (StrategicNetwork, "no"))).EnsureSuccessStatusCode();

        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted after the page was saved");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        var answer = (await GetAnswersAsync(collectionId)).Should().ContainSingle().Subject;
        answer.Value.Should().Be("no");
        answer.Source.Should().Be(AnswerSource.User);
        (await Context.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == AuditAction.MaturityAssessmentPrefillRefreshed))
            .Should().BeFalse("nothing was written, so there is nothing to record");
    }

    [Fact]
    public async Task ShouldLeaveAClosedWindowAlone()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        await CloseCollectionAsync(collectionId);

        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted after the window closed");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        (await GetAnswersAsync(collectionId)).Should().BeEmpty();
    }

    // A form the admin adds to an open window after the LA has submitted (BLUM-8093) has no section row on that
    // assessment, so nothing marks it completed — only the submitted check keeps the refresh out of it.
    [Fact]
    public async Task ShouldLeaveASubmittedAssessmentAlone_EvenInASectionAddedAfterIt()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        var submitted = await SeedSubmittedAssessmentAsync(
            collectionId,
            MainOrg,
            TestFinancialYears.MaturityReference(TestFinancialYears.Current, 1),
            answers: [("MA_DESIGNATED_LEAD", "yes")]);
        await Context.MaturityAssessmentSections
            .Where(s => s.AssessmentId == submitted.Id && s.SectionCode == "la_capacity")
            .ExecuteDeleteAsync();

        AuthenticateAsMainOrgLead();
        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted after the assessment");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        var answer = (await GetAnswersAsync(collectionId)).Should().ContainSingle().Subject;
        answer.QuestionCode.Should().Be("MA_DESIGNATED_LEAD");
        answer.Source.Should().Be(AnswerSource.User);
    }

    // The refresh reads the answers, then writes. An LA who CLEARS a pre-filled value in between leaves no row for
    // the unique index to refuse the insert against, so the write itself has to notice the row it was replacing
    // has gone — or the value the LA just removed comes straight back.
    [Fact]
    public async Task ShouldRefuseTheWrite_WhenARowItWasToReplaceHasGone()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, (StrategicNetwork, "no"));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        var stale = (await GetAnswersAsync(collectionId)).Should().ContainSingle().Subject;

        (await SavePageAsync(collectionId, "la_capacity", "strategic_network", true, (StrategicNetwork, string.Empty))).EnsureSuccessStatusCode();
        (await GetAnswersAsync(collectionId)).Should().BeEmpty("the LA cleared the value");

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaturityAssessmentAnswerRepository>();
        var replacement = MaturityAssessmentAnswer.Record(
            MaturityAssessmentAnswerId.New(),
            stale.AssessmentId,
            StrategicNetwork,
            "yes",
            AnswerSource.Quarterly,
            new UserId(MainOrgLeadUserId),
            DateTime.UtcNow);

        var write = () => repository.ReplaceRowsAsync([stale.Id], [replacement]);

        await write.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await GetAnswersAsync(collectionId)).Should().BeEmpty();
    }

    // The pre-fill build is a dozen queries. A colleague who completes a section while it runs has checked and
    // signed off those answers, so the plan must be made on the sections as they are AFTER the build.
    [Fact]
    public async Task ShouldLeaveASectionAlone_WhenItWasCompletedWhileThePrefillWasBeingBuilt()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        var quarterlyId = await CreateOpenCollectionAsync("Q1 submitted while a colleague was working");
        await SubmitRegionalNetworkReportAsync(quarterlyId, membership: "yes");

        // Put the assessment back to "this report has not been pulled in yet", then run the refresh by hand with a
        // pre-fill service that lets the colleague in half-way.
        await Context.MaturityAssessmentAnswers.ExecuteDeleteAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var refresher = new MaturityPrefillRefresher(
            services.GetRequiredService<ICollectionQueries>(),
            services.GetRequiredService<IMaturityAssessmentQueries>(),
            services.GetRequiredService<IMaturityAssessmentAnswerRepository>(),
            new InterruptedPrefillService(services.GetRequiredService<IMaturityPrefillService>(), () => CompleteSectionDirectlyAsync(collectionId, "la_capacity")),
            services.GetRequiredService<IAuditLogRepository>(),
            services.GetRequiredService<IDateTimeProvider>(),
            services.GetRequiredService<ILogger<MaturityPrefillRefresher>>());

        await refresher.RefreshOpenAssessmentsAsync(MainOrg, new CollectionId(quarterlyId), new UserId(MainOrgLeadUserId));

        (await GetAnswersAsync(collectionId)).Should().BeEmpty();
    }

    private async Task CompleteSectionDirectlyAsync(Guid collectionId, string sectionCode)
    {
        var assessment = await Context.MaturityAssessments
            .Include(a => a.Sections)
            .FirstAsync(a => a.CollectionId == new CollectionId(collectionId) && a.OrganisationId == MainOrg.Id);
        assessment.SetSectionStatus(sectionCode, MaturityAssessmentSectionStatus.Completed, new UserId(MainOrgLeadUserId), DateTime.UtcNow);
        await SaveChangesAsync();
    }

    // The real pre-fill, with something happening between its reads and its caller's next line.
    private sealed class InterruptedPrefillService(IMaturityPrefillService inner, Func<Task> duringBuild) : IMaturityPrefillService
    {
        public async Task<IReadOnlyList<MaturityPrefilledAnswer>> BuildAsync(
            Organisation organisation,
            Collection collection,
            IReadOnlyList<MaturityResolvedQuestion> questions,
            CancellationToken cancellationToken)
        {
            var prefilled = await inner.BuildAsync(organisation, collection, questions, cancellationToken);
            await duringBuild();
            return prefilled;
        }
    }
}
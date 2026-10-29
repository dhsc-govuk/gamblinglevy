using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.MaturityAssessments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// The window and who may open it (BLUM-7855 / 7858): the context read, starting the assessment, and the 404 / 409 /
// 422 / 403 edges of the LA-facing surface.
public class MaturityAssessmentLifecycleTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    private readonly IntegrationTestWebFactory _factory = factory;

    // The task list auto-starts, so two tabs or two colleagues can both pass the "not started yet" read; the unique
    // index decides, and the loser must be answered 409 (TryAddAsync = false), not the 500 a bare insert threw.
    [Fact]
    public async Task Start_LoserOfARace_IsToldItAlreadyExists()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        var first = MaturityAssessment.Start(MaturityAssessmentId.New(), new CollectionId(collectionId), MainOrg.Id, MaturityProtocol.SectionCodes, AdminsData.MainAdmin.Id, DateTime.UtcNow);
        var second = MaturityAssessment.Start(MaturityAssessmentId.New(), new CollectionId(collectionId), MainOrg.Id, MaturityProtocol.SectionCodes, AdminsData.MainAdmin.Id, DateTime.UtcNow);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMaturityAssessmentRepository>();

        (await repository.TryAddAsync(first)).Should().BeTrue();
        (await repository.TryAddAsync(second)).Should().BeFalse();

        // And over HTTP the sequential second start reads the same way.
        AuthenticateAsMainOrgLead();
        (await StartAsync(collectionId)).StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        (await GetAssessmentAsync(collectionId)).Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Context_DescribesTheWindowBeforeAnythingIsStarted()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        var response = await GetContextAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var context = await response.ToResponseModel<MaturityAssessmentContextDto>();
        context.CollectionId.Should().Be(collectionId);
        context.FinancialYear.Should().Be(TestFinancialYears.Current);
        context.FinancialYearEndYear.Should().Be(MaturityAssessmentReference.EndYear(TestFinancialYears.Current));
        context.LocalAuthorityName.Should().Be(MainOrg.Name);
        context.IsOpen.Should().BeTrue();
        context.AssessmentId.Should().BeNull();
        context.Status.Should().Be("not_started");
        context.SubmittedAt.Should().BeNull();
        context.ReferenceNumber.Should().BeNull();
    }

    [Fact]
    public async Task Context_IsNotFound_ForAQuarterlyWindow()
    {
        var quarterlyId = await CreateOpenCollectionAsync();

        AuthenticateAsMainOrgLead();
        (await GetContextAsync(quarterlyId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_IsNotFound_ForADraftWindow()
    {
        var draftId = await CreateMaturityCollectionAsync("Draft maturity", saveAsDraft: true);

        AuthenticateAsMainOrgLead();
        (await GetContextAsync(draftId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Context_IsNotFound_ForAnUnassignedOrganisation_AndForAVcseOnAnAllLasWindow()
    {
        var explicitId = await CreateMaturityCollectionAsync("Main only", localAuthorityIds: [MainOrg.Id.Value]);
        var allLasId = await CreateMaturityCollectionAsync("All LAs", isSubmittedByAllLas: true);

        AuthenticateAsSecondOrgLead();
        (await GetContextAsync(explicitId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetContextAsync(allLasId)).StatusCode.Should().Be(HttpStatusCode.OK);

        AuthenticateAsVcseOrgLead();
        (await GetContextAsync(allLasId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_CreatesEverySectionNotStarted_AndTheContextFollows()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        var taskList = await StartAssessmentAsync(collectionId);

        taskList.Sections.Select(s => s.Code).Should().Equal(MaturityProtocol.SectionCodes);
        taskList.Sections.Should().OnlyContain(s => s.Status == "not_started" && s.Title.Length > 0);
        taskList.CanSubmit.Should().BeFalse();
        taskList.Submitted.Should().BeFalse();
        taskList.IsPrefilled.Should().BeFalse();

        var saved = await GetAssessmentAsync(collectionId);
        saved.Sections.Should().HaveCount(MaturityProtocol.SectionCodes.Count);
        saved.IsSubmitted.Should().BeFalse();

        var context = await (await GetContextAsync(collectionId)).ToResponseModel<MaturityAssessmentContextDto>();
        context.AssessmentId.Should().Be(saved.Id.Value);
        context.Status.Should().Be("in_progress");

        var again = await GetMaturityTaskListAsync(collectionId);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.ToResponseModel<MaturityTaskListDto>()).AssessmentId.Should().Be(saved.Id.Value);
    }

    [Fact]
    public async Task Start_Twice_IsAConflict()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        (await StartAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // BLUM-8747: starting is audited once, against the assessment and the LA. The 409 a second start gets is not a
    // start, so it must not add a second row.
    [Fact]
    public async Task Start_WritesOneStartedAuditRow_AndASecondStartWritesNone()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);
        (await StartAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var assessment = await GetAssessmentAsync(collectionId);
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.MaturityAssessmentStarted)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.MaturityAssessment);
        row.TargetId.Should().Be(assessment.Id.Value);
        row.ActorUserId.Value.Should().Be(MainOrgLeadUserId);
        row.OrganisationId.Should().Be(MainOrg.Id);
    }

    [Fact]
    public async Task Start_OutsideTheWindow_IsRefused_ButTheContextStaysReadable()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await CloseCollectionAsync(collectionId);

        AuthenticateAsMainOrgLead();
        var start = await StartAsync(collectionId);

        start.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await start.Content.ReadAsStringAsync()).Should().Contain("REPORT_NOT_OPEN");

        var context = await GetContextAsync(collectionId);
        context.StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.ToResponseModel<MaturityAssessmentContextDto>()).IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task TaskList_IsNotFound_BeforeStart_AndForAnotherOrganisation()
    {
        var collectionId = await CreateMaturityCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsMainOrgLead();
        (await GetMaturityTaskListAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await StartAssessmentAsync(collectionId);

        // SecondOrg is assigned but has not started — its own task list is 404, and it never sees MainOrg's.
        AuthenticateAsSecondOrgLead();
        (await GetMaturityTaskListAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetSectionResponseAsync(collectionId, "la_capacity")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EveryRoute_IsForbidden_ForTheDhscAdmin()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        AuthenticateAsAdmin();
        var responses = new[]
        {
            await GetContextAsync(collectionId),
            await StartAsync(collectionId),
            await GetMaturityTaskListAsync(collectionId),
            await GetSectionResponseAsync(collectionId, "la_capacity"),
            await SavePageAsync(collectionId, "la_capacity", "people_and_roles", true, ("MA_DESIGNATED_LEAD", "yes")),
            await CompleteSectionAsync(collectionId, "la_capacity"),
            await SubmitAssessmentAsync(collectionId),
            await ExportAssessmentAsync(collectionId),
            await GetSubmittedAsync(collectionId),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BothLaRoles_CanStartAnAssessment()
    {
        var collectionId = await CreateMaturityCollectionAsync();

        AuthenticateAs(Domain.ValueObjects.Role.OrganisationAdmin, Guid.NewGuid(), MainOrg.Id.Value);
        (await StartAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);

        AuthenticateAsMainOrgLead();
        (await GetMaturityTaskListAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
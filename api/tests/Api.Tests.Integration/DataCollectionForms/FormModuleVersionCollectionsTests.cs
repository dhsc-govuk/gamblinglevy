using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Features;
using Domain.DataCollectionForms;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataCollectionForms;

// BLUM-9086: which collections use each version of a form. GET .../modules/{id}/versions/collections lists every version
// of the module, newest first, with the non-deleted collections that record it. Behind BLUM_9087, like the other version
// endpoints. Each test publishes a module of its own, so the versions it reads are only the ones it made.
public class FormModuleVersionCollectionsTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "data-collections";
    private const string FormsRoute = "admin/data-collection-forms";

    private sealed record CollectionResult(Guid Id);

    private sealed record QuestionResult(Guid Id, Guid? SectionId, string Code, string Label, string QuestionType, bool IsRequired);

    private sealed record ModuleResult(Guid Id, List<QuestionResult> Questions);

    private sealed record PublishResult(int? LatestVersionNumber);

    public async Task InitializeAsync()
    {
        if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == AdminsData.MainAdmin.Id))
        {
            await Context.Users.AddAsync(AdminsData.MainAdmin);
            await SaveChangesAsync();
        }

        if (!await Context.Organisations.AsNoTracking().AnyAsync(o => o.Id == OrganisationsData.MainOrganisation.Id))
        {
            await Context.Organisations.AddAsync(OrganisationsData.MainOrganisation);
            await SaveChangesAsync();
        }

        Context.Collections.RemoveRange(Context.Collections);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    public async Task DisposeAsync()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsAdmin();
    }

    [Fact]
    public async Task ShouldListEachVersionNewestFirst_WithTheCollectionsThatRecordIt()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = await CreateAndPublishModuleAsync("vc_listed");
        var other = await CreateAndPublishModuleAsync("vc_other");
        // Both hold another form too, as a quarterly report does, so each must be listed by its version of this form.
        var open = await CreateAsync(Request("Open on version one", saveAsDraft: false, windowOpen: true, [other, module]));
        var scheduled = await CreateAsync(Request("Not open yet", saveAsDraft: false, windowOpen: false, [module, other]));
        var elsewhere = await CreateAsync(Request("Uses another form", saveAsDraft: false, windowOpen: false, [other]));

        // The collection not yet open moves to each new version, so version 2 is left with none.
        await PublishChangedVersionAsync(module);
        await PublishChangedVersionAsync(module);

        var versions = await ListAsync(module);

        versions.Select(v => v.VersionNumber).Should().Equal(3, 2, 1);
        versions[0].Collections.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = scheduled, Name = "Not open yet", Status = "scheduled" });
        versions[1].Collections.Should().BeEmpty();
        versions[2].Collections.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = open, Name = "Open on version one", Status = "open" });
        versions.SelectMany(v => v.Collections).Should().NotContain(c => c.Id == elsewhere);

        var recorded = await Context.FormModuleVersions.AsNoTracking()
            .Where(v => v.FormModuleId == FormModuleId.From(module))
            .ToDictionaryAsync(v => v.VersionNumber, v => v.Id.Value);
        versions.Should().AllSatisfy(v => v.VersionId.Should().Be(recorded[v.VersionNumber]));
    }

    [Fact]
    public async Task ShouldLeaveOutDeletedCollectionsAndDraftsWithNoVersion_AndListACollectionRevertedToDraft()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = await CreateAndPublishModuleAsync("vc_excluded");
        var deleted = await CreateAsync(Request("Deleted", saveAsDraft: false, windowOpen: true, [module]));
        var neverPublished = await CreateAsync(Request("Never published", saveAsDraft: true, windowOpen: true, [module]));
        var reverted = await CreateAsync(Request("Reverted to draft", saveAsDraft: false, windowOpen: true, [module]));
        (await Client.DeleteAsync($"{Route}/{deleted}")).IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsync($"{Route}/{reverted}/revert-to-draft", null)).IsSuccessStatusCode.Should().BeTrue();

        var versions = await ListAsync(module);

        var listed = versions.Should().ContainSingle().Subject.Collections;
        listed.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Id = reverted, Status = "draft" }, "a revert to draft keeps the versions it recorded");
        listed.Should().NotContain(c => c.Id == deleted || c.Id == neverPublished);
    }

    [Fact]
    public async Task ShouldListNothing_ForAnUnknownModule()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);

        var versions = await ListAsync(Guid.NewGuid());

        versions.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldHideTheEndpoint_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == "strategic_feedback");

        var response = await Client.GetAsync($"{FormsRoute}/modules/{module.Id.Value}/versions/collections");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldForbidLocalAuthorityUsers(string role)
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        var module = await Context.FormModules.AsNoTracking().SingleAsync(m => m.Code == "strategic_feedback");
        AuthenticateAs(role, Guid.NewGuid(), OrganisationsData.MainOrganisation.Id.Value);

        var response = await Client.GetAsync($"{FormsRoute}/modules/{module.Id.Value}/versions/collections");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<List<FormModuleVersionCollectionsDto>> ListAsync(Guid moduleId)
    {
        var response = await Client.GetAsync($"{FormsRoute}/modules/{moduleId}/versions/collections");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<List<FormModuleVersionCollectionsDto>>();
    }

    private async Task<Guid> CreateAndPublishModuleAsync(string code)
    {
        var create = await Client.PostAsJsonAsync($"{FormsRoute}/modules", new CreateFormModuleRequestDto(code, $"Module {code}", null));
        create.IsSuccessStatusCode.Should().BeTrue(await create.Content.ReadAsStringAsync());
        var moduleId = (await create.ToResponseModel<ModuleResult>()).Id;

        (await Client.PostAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions",
            new CreateFormQuestionRequestDto(null, "Q1", "Any other notes?", "Textarea", false, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();

        var publish = await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null);
        publish.StatusCode.Should().Be(HttpStatusCode.OK, await publish.Content.ReadAsStringAsync());
        (await publish.ToResponseModel<PublishResult>()).LatestVersionNumber.Should().Be(1);
        return moduleId;
    }

    // Reopens the module, rewords its question and publishes, so the content changes and a new version is saved.
    private async Task PublishChangedVersionAsync(Guid moduleId)
    {
        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/draft", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var moduleResponse = await Client.GetAsync($"{FormsRoute}/modules/{moduleId}");
        var question = (await moduleResponse.ToResponseModel<ModuleResult>()).Questions.First();
        (await Client.PutAsJsonAsync(
            $"{FormsRoute}/modules/{moduleId}/questions/{question.Id}",
            new UpdateFormQuestionRequestDto(question.SectionId, $"{question.Label} ({Guid.NewGuid():N})", question.QuestionType, question.IsRequired, null, null, null)))
            .IsSuccessStatusCode.Should().BeTrue();

        (await Client.PostAsync($"{FormsRoute}/modules/{moduleId}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.ToResponseModel<CollectionResult>()).Id;
    }

    private static CreateCollectionRequestDto Request(string name, bool saveAsDraft, bool windowOpen, IReadOnlyList<Guid> modules)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (open, deadline) = windowOpen ? (today.AddDays(-5), today.AddDays(25)) : (today.AddDays(5), today.AddDays(35));
        return new CreateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            OpenDate: open,
            Deadline: deadline,
            IsSubmittedByAllLas: true,
            SaveAsDraft: saveAsDraft,
            LocalAuthorityIds: null,
            FormModuleIds: modules,
            Reminders: null);
    }
}
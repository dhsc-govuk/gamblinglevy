using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Shared setup for activity-records integration tests. Seeds the two organisations used for org-scoping and
// cross-org assertions, exposes LA-user identities, and provides helpers built on the seeded activity taxonomy
// (six categories + their type options) so payloads stay valid regardless of the generated seed ids.
public abstract class ActivityTestBase(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    protected static readonly Organisation MainOrg = OrganisationsData.MainOrganisation; // North East, LA
    protected static readonly Organisation SecondOrg = OrganisationsData.SecondOrganisation; // North West, LA

    protected static readonly Guid MainOrgLeadUserId = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    protected static readonly Guid SecondOrgLeadUserId = Guid.Parse("a0000000-0000-0000-0000-000000000003");

    // Custom key results satisfy every category's completion minimum (one everywhere since the 13 August
    // protocol — BLUM-7620) without depending on the seeded standard definitions.
    protected static readonly IReadOnlyList<ExpectedResultInputDto> DefaultExpectedResults =
    [
        new(null, "Key result one"),
        new(null, "Key result two"),
    ];

    protected void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    protected void AuthenticateAsSecondOrgLead() => AuthenticateAs(Role.Lead, SecondOrgLeadUserId, SecondOrg.Id.Value);

    protected async Task<ActivityTaxonomyDto> GetTaxonomyAsync()
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<ActivityTaxonomyDto>();
    }

    // A valid (category, type) selection for the given seeded category.
    protected async Task<(Guid CategoryId, Guid TaskId)> GetCategoryAndTypeAsync(string categoryName = "Needs assessment")
    {
        var taxonomy = await GetTaxonomyAsync();
        var category = taxonomy.Categories.First(c => c.Name == categoryName);
        var task = taxonomy.Tasks.First(t => t.CategoryId == category.Id);
        return (category.Id, task.Id);
    }

    protected async Task<ActivityDto> CreatePlannedActivityAsync(string? title = "Planned activity")
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var request = new CreateActivityRequestDto(title, categoryId, taskId, null, "planned", null, null, false, DefaultExpectedResults);
        return await PostActivityAsync(request);
    }

    protected async Task<ActivityDto> CreateActiveActivityAsync(string? title = "Active activity")
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();
        var request = new CreateActivityRequestDto(title, categoryId, taskId, null, "active", 3, 2026, false, DefaultExpectedResults);
        return await PostActivityAsync(request);
    }

    protected async Task<ActivityDto> CreateDraftActivityAsync(string? title = "Draft activity")
    {
        var request = new CreateActivityRequestDto(title, null, null, null, null, null, null, true);
        return await PostActivityAsync(request);
    }

    protected async Task<ActivityDto> PostActivityAsync(CreateActivityRequestDto request)
    {
        var response = await Client.PostAsJsonAsync("activities", request);
        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<ActivityDto>();
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsMainOrgLead();
        await EnsureOrgAsync(MainOrg);
        await EnsureOrgAsync(SecondOrg);
        await ClearActivitiesAsync();

        // The category-scoped form schema is seeded on startup and its questions feed answer validation in the
        // create/update flow. Clear it so activity tests that do not exercise dynamic questions are unaffected;
        // answer-specific tests seed their own schema.
        await ClearFormSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearActivitiesAsync();
        await ClearFormSchemaAsync();
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    protected async Task ClearFormSchemaAsync()
    {
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.FormQuestionOptions.RemoveRange(Context.FormQuestionOptions);
        Context.FormQuestions.RemoveRange(Context.FormQuestions);
        Context.FormSections.RemoveRange(Context.FormSections);
        await SaveChangesAsync();
    }

    private async Task EnsureOrgAsync(Organisation organisation)
    {
        var exists = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == organisation.Id);
        if (!exists)
        {
            await Context.Organisations.AddAsync(organisation);
            await SaveChangesAsync();
        }
    }

    private async Task ClearActivitiesAsync()
    {
        Context.ActivityExpectedResults.RemoveRange(Context.ActivityExpectedResults);
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.Activities.RemoveRange(Context.Activities);
        await SaveChangesAsync();
    }
}
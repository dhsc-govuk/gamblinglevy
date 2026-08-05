using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Education and Help share the People and issues screen, but only on their "Other ..." activity type (BLUM-7092):
// protocol questions 1.EF.1.5/1.6 are mandatory there and absent for every other type in either category. The
// shared Activity details screen (protocol 1.BCDEF.1.1-1.4, BLUM-7120) and the common Additional information
// screen apply to both categories regardless of type.
public class ActivityPeopleAndIssuesTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string EducationCategoryName = "Education, campaigns and outreach";
    private const string HelpCategoryName = "Help, advice and access to services";
    private const string PeopleAndRolesCategoryName = "People and roles";
    private const string OtherEducationTaskName = "Other type of education, campaign or outreach";
    private const string OtherHelpTaskName = "Other type of help, advice or service";
    private const string WorkplaceCharterTaskName = "Creating a workplace charter";
    private const string ReferralsTaskName = "Improving referrals between services";

    private static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    private static readonly Guid MainOrgLeadUserId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    private static List<ExpectedResultInputDto> CustomResults() =>
        [new ExpectedResultInputDto(null, "Key result one"), new ExpectedResultInputDto(null, "Key result two")];

    private static List<ActivityAnswerInputDto> CompleteAnswers(string prefix) =>
    [
        new ActivityAnswerInputDto($"{prefix}_WHO_WITH", "licensing"),
        new ActivityAnswerInputDto($"{prefix}_SHARED", "no"),
        new ActivityAnswerInputDto($"{prefix}_WHO_FOR", "children,frontline_workers"),
        new ActivityAnswerInputDto($"{prefix}_RELATED_ISSUES", "mental_health"),
        new ActivityAnswerInputDto($"{prefix}_PLAN", "Deliver targeted local support"),
    ];

    private async Task<ActivityTaxonomyDto> GetTaxonomyAsync()
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<ActivityTaxonomyDto>();
    }

    private async Task<(Guid CategoryId, Guid TaskId)> GetCategoryTypeAsync(string categoryName, string? taskName = null)
    {
        var taxonomy = await GetTaxonomyAsync();
        var category = taxonomy.Categories.First(c => c.Name == categoryName);
        var tasks = taxonomy.Tasks.Where(t => t.CategoryId == category.Id);
        var task = taskName is null ? tasks.First() : tasks.First(t => t.Name == taskName);
        return (category.Id, task.Id);
    }

    [Theory]
    [InlineData(EducationCategoryName, "EDU", OtherEducationTaskName)]
    [InlineData(HelpCategoryName, "HELP", OtherHelpTaskName)]
    public async Task ShouldExposePeopleAndIssuesAndAdditionalInformation_ForEducationAndHelp(string categoryName, string prefix, string otherTaskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, otherTaskId) = await GetCategoryTypeAsync(categoryName, otherTaskName);

        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        var schema = await response.ToResponseModel<FormSchemaDto>();

        // The shared Activity details screen (1.BCDEF.1.1-1.4) applies to every type, ahead of People and issues
        // (BLUM-7120).
        var detailsSection = schema.Sections.Should().ContainSingle(s => s.Title == "Activity details").Subject;
        detailsSection.ActivityTaskId.Should().BeNull("1.BCDEF.1.1-1.4 apply to every {0} type", categoryName);
        detailsSection.DisplayOrder.Should().Be(0);

        var peopleSection = schema.Sections.Should().ContainSingle(s => s.Title == "People and issues").Subject;
        peopleSection.ActivityTaskId.Should().Be(otherTaskId, "1.EF.1.5/1.6 only apply to the '{0}' type", otherTaskName);
        peopleSection.DisplayOrder.Should().Be(1);

        var additionalSection = schema.Sections.Should().ContainSingle(s => s.Title == "Additional information").Subject;
        additionalSection.ActivityTaskId.Should().BeNull();
        additionalSection.DisplayOrder.Should().Be(2);

        schema.Sections.Should().NotContain(s => s.Title == "Role details");
        foreach (var code in new[] { $"{prefix}_WHO_WITH", $"{prefix}_WHO_WITH_OTHER", $"{prefix}_SHARED" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(detailsSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the Activity details screen (BLUM-7120)", code);
        }

        foreach (var code in new[] { $"{prefix}_WHO_FOR", $"{prefix}_WHO_FOR_OTHER", $"{prefix}_RELATED_ISSUES", $"{prefix}_RELATED_ISSUES_OTHER" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(peopleSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the People and issues screen", code);
        }

        var plan = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_PLAN").Subject;
        plan.SectionId.Should().Be(additionalSection.Id);
        plan.IsRequired.Should().BeTrue();
        plan.Label.Should().Be("What do you plan to do?");
        plan.Hint.Should().Be("Use this space to add any other information about your approach and the steps you'll take");
    }

    [Fact]
    public async Task ShouldHideAdditionalInformation_OnlyForPeopleAndRoles()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync(PeopleAndRolesCategoryName);

        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        var schema = await response.ToResponseModel<FormSchemaDto>();

        schema.Sections.Should().ContainSingle(s => s.Title == "Role details");
        schema.Sections.Should().NotContain(s => s.Title == "Additional information");
        schema.Questions.Should().NotContain(q => q.Code.EndsWith("_PLAN"));
    }

    [Theory]
    [InlineData(EducationCategoryName, "EDU", OtherEducationTaskName)]
    [InlineData(HelpCategoryName, "HELP", OtherHelpTaskName)]
    public async Task ShouldRequirePeopleAndIssues_ForTheOtherType(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var request = new CreateActivityRequestDto(
            "Community activity", categoryId, taskId, "Something else entirely", "planned", null, null, false, CustomResults());
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("ACTIVITY_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_WHO_WITH");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_SHARED");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_WHO_FOR");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_RELATED_ISSUES");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_PLAN");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // BLUM-7092 criterion 2: every type other than "Other ..." skips the People and issues screen, so its
    // questions are neither required nor stored — only the type-independent Activity details and Additional
    // information questions are (BLUM-7120).
    [Theory]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
    public async Task ShouldNotRequirePeopleAndIssues_ForNonOtherTypes(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var request = new CreateActivityRequestDto(
            "Community activity",
            categoryId,
            taskId,
            null,
            "planned",
            null,
            null,
            false,
            CustomResults(),
            [
                new ActivityAnswerInputDto($"{prefix}_WHO_WITH", "licensing"),
                new ActivityAnswerInputDto($"{prefix}_SHARED", "no"),
                new ActivityAnswerInputDto($"{prefix}_PLAN", "Deliver targeted local support"),
            ]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "1.EF.1.5/1.6 do not apply to '{0}', so the Activity details and plan answers should complete the activity: {1}",
            taskName,
            await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.IsComplete.Should().BeTrue();
        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_WHO_WITH");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_SHARED");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_PLAN");
        answers.Should().OnlyContain(a =>
            a.QuestionCode == $"{prefix}_WHO_WITH" || a.QuestionCode == $"{prefix}_SHARED" || a.QuestionCode == $"{prefix}_PLAN");
    }

    // The de-scoped questions are not rejected if a stale client posts them anyway: CreateActivityCommand
    // validates against the type's applicable questions but persists via CollectPersistable(schema.Questions),
    // deliberately keeping such answers rather than silently dropping them.
    [Theory]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
    public async Task ShouldStillStorePeopleAndIssuesAnswers_WhenPostedForANonOtherType(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var request = new CreateActivityRequestDto(
            "Community activity",
            categoryId,
            taskId,
            null,
            "planned",
            null,
            null,
            false,
            CustomResults(),
            [.. CompleteAnswers(prefix)]);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        answers.Should().Contain(a => a.QuestionCode == $"{prefix}_WHO_FOR", "de-scoped answers are kept, not pruned");
    }

    [Theory]
    [InlineData(EducationCategoryName, "EDU", OtherEducationTaskName)]
    [InlineData(HelpCategoryName, "HELP", OtherHelpTaskName)]
    public async Task ShouldCompleteEducationAndHelpActivities_WithPeopleAndIssuesAnswered(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var request = new CreateActivityRequestDto(
            "Community activity", categoryId, taskId, null, "planned", null, null, false, CustomResults(), CompleteAnswers(prefix));
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "Activity details, People and issues and Additional information answers should complete the activity: {0}",
            await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.IsComplete.Should().BeTrue();
        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_WHO_WITH");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_SHARED");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_WHO_FOR");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_RELATED_ISSUES");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_PLAN");
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        var exists = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == MainOrg.Id);
        if (!exists)
        {
            await Context.Organisations.AddAsync(MainOrg);
            await SaveChangesAsync();
        }

        await ClearActivitiesAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearActivitiesAsync();
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    private async Task ClearActivitiesAsync()
    {
        Context.ActivityExpectedResults.RemoveRange(Context.ActivityExpectedResults);
        Context.ActivityAnswers.RemoveRange(Context.ActivityAnswers);
        Context.Activities.RemoveRange(Context.Activities);
        await SaveChangesAsync();
    }
}
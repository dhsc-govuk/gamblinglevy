using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// Education and Help share the People and issues screen, and BLUM-7778 puts it back on EVERY type in either
// category — the section carries no activity_task_id, so protocol question 1.EF.1.5 is mandatory whatever the LA
// answered to "What type of work does it involve?". 1.EF.1.6 ("What related issues did it address, or will it
// address?") is the schema's one optional question (BLUM-7479). The shared Activity details screen (protocol
// 1.BCDEF.1.1-1.3, BLUM-7120; 1.4 was withdrawn by BLUM-8340) and the common Additional information screen apply
// to both categories too.
//
// The theories below therefore pair each category with both an "Other ..." type and an ordinary one: the ordinary
// rows are the ticket's acceptance criteria, and they are the rows that fail against the BLUM-7092 scoping this
// replaced.
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
    [InlineData(EducationCategoryName, "EDU")]
    [InlineData(HelpCategoryName, "HELP")]
    public async Task ShouldExposePeopleAndIssuesAndAdditionalInformation_ForEducationAndHelp(string categoryName, string prefix)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryTypeAsync(categoryName);

        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        var schema = await response.ToResponseModel<FormSchemaDto>();

        // The shared Activity details screen (1.BCDEF.1.1-1.3) applies to every type, ahead of People and issues
        // (BLUM-7120).
        var detailsSection = schema.Sections.Should().ContainSingle(s => s.Title == "Activity details").Subject;
        detailsSection.ActivityTaskId.Should().BeNull("1.BCDEF.1.1-1.3 apply to every {0} type", categoryName);
        detailsSection.DisplayOrder.Should().Be(0);

        var peopleSection = schema.Sections.Should().ContainSingle(s => s.Title == "People and issues").Subject;
        peopleSection.ActivityTaskId.Should().BeNull(
            "1.EF.1.5/1.6 apply to every {0} type, not just its \"Other ...\" one (BLUM-7778)", categoryName);
        peopleSection.DisplayOrder.Should().Be(1);

        var additionalSection = schema.Sections.Should().ContainSingle(s => s.Title == "Additional information").Subject;
        additionalSection.ActivityTaskId.Should().BeNull();
        additionalSection.DisplayOrder.Should().Be(2);

        schema.Sections.Should().NotContain(s => s.Title == "Role details");
        schema.Questions.Should().NotContain(q => q.Code == $"{prefix}_SHARED", "1.BCDEF.1.4 was withdrawn (BLUM-8340)");
        foreach (var code in new[] { $"{prefix}_WHO_WITH", $"{prefix}_WHO_WITH_OTHER" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(detailsSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the Activity details screen (BLUM-7120)", code);
        }

        // RELATED_ISSUES_OTHER stays required: it is conditional on RELATED_ISSUES = "other", and
        // ActivityAnswerValidator skips a question whose reveal is not showing — so ticking "Other" still demands
        // the free text, while leaving the parent blank demands nothing (BLUM-7479).
        foreach (var code in new[] { $"{prefix}_WHO_FOR", $"{prefix}_WHO_FOR_OTHER", $"{prefix}_RELATED_ISSUES_OTHER" })
        {
            var question = schema.Questions.Should().ContainSingle(q => q.Code == code).Subject;
            question.SectionId.Should().Be(peopleSection.Id);
            question.IsRequired.Should().BeTrue("{0} is mandatory on the People and issues screen", code);
        }

        var relatedIssues = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_RELATED_ISSUES").Subject;
        relatedIssues.SectionId.Should().Be(peopleSection.Id);
        relatedIssues.IsRequired.Should().BeFalse("1.EF.1.6 is optional (BLUM-7479)");
        relatedIssues.Label.Should().Be(
            "What related issues did it address, or will it address? (optional)",
            "the GDS optional suffix rides on the label, which every surface renders verbatim");

        var plan = schema.Questions.Should().ContainSingle(q => q.Code == $"{prefix}_PLAN").Subject;
        plan.SectionId.Should().Be(additionalSection.Id);
        plan.IsRequired.Should().BeTrue();
        plan.Label.Should().Be("Can you tell us about your approach to this activity?");
        plan.Hint.Should().Be("For example the steps you have taken or plan to take");
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

    // BLUM-7778, both acceptance criteria: the screen is required whichever answer the LA gave to "What type of
    // work does it involve?". The two non-"Other" rows are the regression — they 201'd before this change.
    [Theory]
    [InlineData(EducationCategoryName, "EDU", OtherEducationTaskName)]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", OtherHelpTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
    public async Task ShouldRequirePeopleAndIssues_ForEveryType(string categoryName, string prefix, string taskName)
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
        envelope.AnswerErrors.Should().NotContain(e => e.QuestionCode == $"{prefix}_SHARED", "1.BCDEF.1.4 was withdrawn (BLUM-8340)");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_WHO_FOR");
        envelope.AnswerErrors.Should().NotContain(
            e => e.QuestionCode == $"{prefix}_RELATED_ISSUES", "1.EF.1.6 is optional (BLUM-7479)");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == $"{prefix}_PLAN");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(EducationCategoryName, "EDU", OtherEducationTaskName)]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", OtherHelpTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
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
        answers.Should().NotContain(a => a.QuestionCode == $"{prefix}_SHARED");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_WHO_FOR");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_RELATED_ISSUES");
        answers.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_PLAN");
    }

    // BLUM-7479: QA hit a validation error on Continue with "What related issues did it address ...?" left blank.
    // The rest of the People and issues screen still applies — only 1.EF.1.6 became optional.
    [Theory]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
    public async Task ShouldCompleteEducationAndHelpActivities_WithoutRelatedIssues(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var answers = CompleteAnswers(prefix).Where(a => a.QuestionCode != $"{prefix}_RELATED_ISSUES").ToList();
        var request = new CreateActivityRequestDto(
            "Community activity", categoryId, taskId, null, "planned", null, null, false, CustomResults(), answers);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.IsSuccessStatusCode.Should().BeTrue(
            "1.EF.1.6 is optional, so the remaining answers should complete the activity: {0}",
            await response.Content.ReadAsStringAsync());
        var dto = await response.ToResponseModel<ActivityDto>();

        var saved = await Context.Activities.AsNoTracking().FirstAsync(x => x.Id == new ActivityId(dto.Id));
        saved.IsComplete.Should().BeTrue();
        var stored = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        stored.Should().ContainSingle(a => a.QuestionCode == $"{prefix}_WHO_FOR");
        stored.Should().NotContain(a => a.QuestionCode == $"{prefix}_RELATED_ISSUES", "a blank answer is never persisted");
    }

    // The optional parent does not make its conditional child optional: RELATED_ISSUES_OTHER is required whenever
    // the "other" reveal is showing, exactly as WHO_FOR_OTHER is. Without this, making the section optional later
    // would silently drop the free-text follow-up.
    [Theory]
    [InlineData(EducationCategoryName, "EDU", WorkplaceCharterTaskName)]
    [InlineData(HelpCategoryName, "HELP", ReferralsTaskName)]
    public async Task ShouldStillRequireTheOtherFreeText_WhenRelatedIssuesIsOther(string categoryName, string prefix, string taskName)
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryTypeAsync(categoryName, taskName);

        var answers = CompleteAnswers(prefix)
            .Select(a => a.QuestionCode == $"{prefix}_RELATED_ISSUES" ? new ActivityAnswerInputDto(a.QuestionCode, "other") : a)
            .ToList();
        var request = new CreateActivityRequestDto(
            "Community activity", categoryId, taskId, null, "planned", null, null, false, CustomResults(), answers);
        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("ACTIVITY_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().ContainSingle(e => e.QuestionCode == $"{prefix}_RELATED_ISSUES_OTHER");
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
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

        // ActivityTestBase deletes the seeded form schema in its own setup and teardown, and CI runs
        // xUnit.MaxParallelThreads=2 against one database — so this class can find the schema gone. That is worse
        // than a flake here: with no schema, the required-answer theories below get a 201 instead of the asserted
        // 422 and the completion theories pass without exercising anything, so BLUM-7778's acceptance criteria
        // could go green against the very scoping they replaced. Every seeder is guarded on its own tables being
        // empty, so this rebuilds exactly what was deleted and no-ops when the schema is intact. Same guard as
        // ActivityQuestionProtocolTests; fixing the race at its root is its own ticket.
        if (!await Context.FormSections.AsNoTracking().AnyAsync())
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedDataAsync();
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
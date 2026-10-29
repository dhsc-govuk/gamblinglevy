using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.Activities;
using Domain.Categories;
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

// The Joint partnership category (BLUM-7860, reshaped by BLUM-8090): an LA records what it has committed to a pooled
// pot, rather than work it delivers itself. Everything under test here is seeded — the category and its single
// activity type by SeedJointPartnershipTaxonomyAsync, the two questions by SeedFormDesigner — and both seeders write
// once into an empty table, so a reverted string or a dropped question would sit in every deployed database with
// nothing else to catch it. That makes these assertions the only guard on the seed, the same role
// ActivityQuestionProtocolTests plays for the other six categories.
//
// Deliberately NOT an ActivityTestBase, and for the same reason as that class: the base wipes the seeded form
// schema in its setup, and this class is entirely about what the seed contains. InitializeAsync re-seeds when it
// finds the schema gone so the class is independent of run order (CI runs two classes at a time against one
// database).
public class JointPartnershipActivityTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    private static readonly Guid MainOrgLeadUserId = Guid.Parse("e0000000-0000-0000-0000-000000000001");

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    // AC1: the category is offered alongside the original six, with one type under it — which is what lets the
    // wizard auto-select the type rather than ask a one-option radio group.
    [Fact]
    public async Task ShouldOfferJointPartnership_WithASingleActivityType()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();

        var category = taxonomy.Categories.Should().ContainSingle(c => c.Name == CategoryNames.JointPartnership).Subject;
        taxonomy.Tasks.Where(t => t.CategoryId == category.Id).Should().ContainSingle()
            .Which.Name.Should().Be("Joint partnership");

        // No key results: the measures screen is skipped, and in the quarterly report's "Enter activity results"
        // section the record answers only the delivery questions (pinned by QuestionResolutionTests).
        taxonomy.KeyResults.Should().NotContain(k => taxonomy.Tasks.Any(t => t.Id == k.TaskId && t.CategoryId == category.Id));
    }

    // BLUM-8090: one "Activity details" section with no lead-in, holding the two questions the Figma asks on two
    // pages (the wizard splits it — asksOneQuestionPerPage — so Check your answers keeps one card). The three
    // progress questions BLUM-7860 asked here moved into the quarterly report as the AKR_DELIVERY_* questions.
    [Fact]
    public async Task ShouldAskTheTwoJointPartnershipQuestions_InOneSection()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetCategoryAndTypeAsync();
        var schema = await GetSchemaAsync(categoryId);

        var section = schema.Sections.Should().ContainSingle().Subject;
        // "Activity details" is the H1 of both pages, and the title the client's section routing applies with no
        // special case (isActivitySectionApplicable). The name field it would bring is off for this category.
        section.Title.Should().Be("Activity details");
        section.Description.Should().BeNullOrEmpty();

        schema.Questions
            .OrderBy(q => q.DisplayOrder)
            .Select(q => (q.Code, q.Label, q.Hint, q.QuestionType, q.IsRequired))
            .Should().Equal(
                ("JP_COMMITTED", "How much have you committed to this joint partnership?", "The amount you have agreed", "Currency", true),
                ("JP_PARTNERS", "Who is in the joint partnership?", "Tell us what other local authorities are in the partnership", "Text", true));

        // BLUM-8955: the amount is the one Currency question with a minimum, and its messages name it "Amount
        // agreed" where every other question's quote the label. The client words its own copies from these.
        var committed = schema.Questions.Single(q => q.Code == "JP_COMMITTED");
        committed.ErrorMessage.Should().Be("Enter the amount you have agreed");
        committed.MinValue.Should().Be(1);
        committed.ErrorSubject.Should().Be("Amount agreed");
        committed.ErrorSubjectInline.Should().Be("the amount you have agreed");

        // A single-line input now, but still held to the free-text limit.
        var partners = schema.Questions.Single(q => q.Code == "JP_PARTNERS");
        partners.MaxLength.Should().Be(500);
        partners.MaxLengthErrorMessage.Should().Be("Joint partnership members must be 500 characters or less");
    }

    // AC4 + BLUM-8090: the record is Active on save, with no name and no start date — the wizard asks for neither,
    // and it has no status step at all. Without a name the record is labelled by its type everywhere.
    [Fact]
    public async Task ShouldSaveAnActiveRecord_WithNoNameOrStartDate()
    {
        AuthenticateAsMainOrgLead();
        var created = await PostJointPartnershipAsync();

        created.Status.Should().Be(ActivityStatuses.Active);
        created.IsDraft.Should().BeFalse();
        created.ExpectedResults.Should().BeEmpty();
        created.Title.Should().Be("Joint partnership");

        var saved = await Context.Activities.AsNoTracking().FirstAsync(a => a.Id == new ActivityId(created.Id));
        saved.Status!.ToString().Should().Be(ActivityStatuses.Active);
        saved.Title.Should().BeNull();
        saved.StartMonth.Should().BeNull();
        saved.StartYear.Should().BeNull();

        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == saved.Id).ToListAsync();
        answers.Select(a => (a.QuestionCode, a.Value)).Should().BeEquivalentTo(
            [("JP_COMMITTED", "25000"), ("JP_PARTNERS", "Bolton, Bury, Salford and Wigan")]);
    }

    // The start-date exemption is in both validators, and a change goes through the other one.
    [Fact]
    public async Task ShouldUpdateACompletedRecord_WithNoNameOrStartDate()
    {
        AuthenticateAsMainOrgLead();
        var created = await PostJointPartnershipAsync();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PutAsJsonAsync(
            $"activities/{created.Id}",
            new UpdateActivityRequestDto(
                null,
                categoryId,
                taskId,
                null,
                ActivityStatuses.Active,
                null,
                null,
                false,
                [],
                [new ActivityAnswerInputDto("JP_COMMITTED", "30000"), new ActivityAnswerInputDto("JP_PARTNERS", "Bolton and Bury")]));

        response.IsSuccessStatusCode.Should().BeTrue("updating the partnership failed: {0}", await response.Content.ReadAsStringAsync());
        var answers = await Context.ActivityAnswers.AsNoTracking().Where(a => a.ActivityId == new ActivityId(created.Id)).ToListAsync();
        answers.Should().ContainSingle(a => a.QuestionCode == "JP_COMMITTED" && a.Value == "30000");
    }

    // The exemption is scoped to the category: an Active record in any other category still needs its start date.
    [Fact]
    public async Task ShouldStillRequireAStartDate_ForAnActiveRecordInAnotherCategory()
    {
        AuthenticateAsMainOrgLead();
        var taxonomy = await GetTaxonomyAsync();
        var otherCategory = taxonomy.Categories.First(c => c.Name == "Needs assessment");
        var otherTask = taxonomy.Tasks.First(t => t.CategoryId == otherCategory.Id);

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Needs assessment", otherCategory.Id, otherTask.Id, null, ActivityStatuses.Active, null, null, false, [], []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("The start date must include a month");
    }

    [Theory]
    [InlineData("JP_COMMITTED")]
    [InlineData("JP_PARTNERS")]
    public async Task ShouldRefuseCompletion_WhenARequiredAnswerIsMissing(string code)
    {
        AuthenticateAsMainOrgLead();
        var request = await BuildRequestAsync(omit: code);

        var response = await Client.PostAsJsonAsync("activities", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().ContainSingle(e => e.QuestionCode == code);
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The Currency type's rules, mirrored byte-for-byte by validateCurrencyAnswer on the client. Unlike "Record
    // funding spent" — which posts an already-parsed decimal, so only the client can see a typed "£" — an activity
    // answer arrives as the raw string, which is why every one of these is enforced here.
    //
    // A comma or a space is read through only as a thousands separator, and the answer is stored as it was typed
    // (BLUM-8955): nothing is reformatted on the way in, so what the LA sees afterwards is what they entered.
    [Theory]
    [InlineData("25000")]
    [InlineData("1")]
    [InlineData("1,500")]
    [InlineData("1,000,000")]
    [InlineData("1000000000")]
    [InlineData("25 000")]
    [InlineData("25\u00a0000")]
    public async Task ShouldAcceptWholePounds_AndStoreThemAsTyped(string amount)
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync("activities", await BuildRequestAsync(committed: amount));

        response.IsSuccessStatusCode.Should().BeTrue("“{0}” is a whole number of pounds", amount);
        var stored = await Context.ActivityAnswers.AsNoTracking().SingleAsync(a => a.QuestionCode == "JP_COMMITTED");
        stored.Value.Should().Be(amount);
    }

    // BLUM-8955: 0 was accepted, and "0,10" and "5,10" were accepted and later shown as £10 and £510, because every
    // comma was stripped before the amount was looked at. A decimal is refused whichever mark it is written with.
    // The messages are the whole string: they are the field's copy, and the client's must match them exactly.
    [Theory]
    [InlineData("0", "Enter an amount of 1 or more for the amount you have agreed")]
    [InlineData("-100", "Enter an amount of 1 or more for the amount you have agreed")]
    [InlineData("25.50", "Amount agreed must be a whole number and not include pence")]
    [InlineData("0,10", "Amount agreed must be a whole number and not include pence")]
    [InlineData("5,10", "Amount agreed must be a whole number and not include pence")]
    [InlineData("£25000", "Amount agreed must not include £")]
    [InlineData("lots", "Enter a number for the amount you have agreed, for example 1500")]
    [InlineData("1000000001", "Amount agreed must be £1,000,000,000 or less")]
    public async Task ShouldRefuseAnInvalidAmount(string amount, string expectedMessage)
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync("activities", await BuildRequestAsync(committed: amount));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().ContainSingle(e => e.QuestionCode == "JP_COMMITTED")
            .Which.Message.Should().Be(expectedMessage);
        (await Context.Activities.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRefuseAMissingAmount_WithTheFieldsOwnMessage()
    {
        AuthenticateAsMainOrgLead();

        var response = await Client.PostAsJsonAsync("activities", await BuildRequestAsync(omit: "JP_COMMITTED"));

        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().ContainSingle().Which.Message.Should().Be("Enter the amount you have agreed");
    }

    // A draft is exempt from required answers, so a half-filled amount must still be refused on the way in —
    // otherwise the record reloads holding a value no completion path could ever accept.
    [Fact]
    public async Task ShouldRefuseAnInvalidAmount_EvenOnADraft()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var response = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(
                "Draft pool", categoryId, taskId, null, null, null, null, true, [], [new ActivityAnswerInputDto("JP_COMMITTED", "25.50")]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    private void AuthenticateAsMainOrgLead() => AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

    private async Task<ActivityTaxonomyDto> GetTaxonomyAsync()
    {
        var response = await Client.GetAsync("activities/taxonomy");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<ActivityTaxonomyDto>();
    }

    private async Task<(Guid CategoryId, Guid TaskId)> GetCategoryAndTypeAsync()
    {
        var taxonomy = await GetTaxonomyAsync();
        var category = taxonomy.Categories.First(c => c.Name == CategoryNames.JointPartnership);
        return (category.Id, taxonomy.Tasks.First(t => t.CategoryId == category.Id).Id);
    }

    private async Task<FormSchemaDto> GetSchemaAsync(Guid categoryId)
    {
        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FormSchemaDto>();
    }

    // A complete Joint partnership record as the wizard sends it (BLUM-8090): Active, no name, no start date, no
    // measures, and the two required answers. `omit` drops one of them; `committed` overrides the amount.
    private async Task<CreateActivityRequestDto> BuildRequestAsync(string? omit = null, string committed = "25000")
    {
        var (categoryId, taskId) = await GetCategoryAndTypeAsync();

        var answers = new List<ActivityAnswerInputDto>
        {
            new("JP_COMMITTED", committed),
            new("JP_PARTNERS", "Bolton, Bury, Salford and Wigan"),
        };

        return new CreateActivityRequestDto(
            null,
            categoryId,
            taskId,
            null,
            ActivityStatuses.Active,
            null,
            null,
            false,
            [],
            answers.Where(a => a.QuestionCode != omit).ToList());
    }

    private async Task<ActivityDto> PostJointPartnershipAsync()
    {
        var response = await Client.PostAsJsonAsync("activities", await BuildRequestAsync());
        response.IsSuccessStatusCode.Should().BeTrue("creating the activity failed: {0}", await response.Content.ReadAsStringAsync());
        return await response.ToResponseModel<ActivityDto>();
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        if (!await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == MainOrg.Id))
        {
            await Context.Organisations.AddAsync(MainOrg);
            await SaveChangesAsync();
        }

        // Every seeder is guarded on its own rows, so this rebuilds exactly what an ActivityTestBase class deleted
        // and no-ops when the schema is intact.
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
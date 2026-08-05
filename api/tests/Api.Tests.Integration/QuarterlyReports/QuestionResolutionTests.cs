using System.Net.Http.Json;
using Api.Dtos;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// GET quarterly-report/{collectionId}/activities/{activityId}/questions — protocol question resolution against the
// startup-seeded key-result protocol and the published activity_key_results module.
public class QuestionResolutionTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static readonly string[] DeliveryCodes =
    [
        "AKR_DELIVERY_PROGRESS",
        "AKR_DELIVERY_CHALLENGES",
        "AKR_DELIVERY_NEXT_QUARTER",
    ];

    // A collection with a started report. The caller must already be authenticated as a Main-org activity
    // manager: identity is AsyncLocal-scoped, so a helper cannot switch it for its caller — every test sets it
    // in its own body first (the repo-wide convention).
    private async Task<Guid> StartReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await CreateSubmissionAsync(collectionId);
        return collectionId;
    }

    [Fact]
    public async Task ActiveRnaActivity_GetsActivePhrasingForSelectedKeyResultsPlusDeliveryQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.ActivityStatus.Should().Be("active");
        dto.ReportStatus.Should().Be("not_started");

        // rna_contributors unlocks its Active variants; rna_published has no Active question at all.
        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal("RNA_CONTRIBUTORS", "RNA_CONTRIBUTORS_OTHER");
        resultsQuestions[0].Label.Should().Be("Who is contributing evidence?");
        resultsQuestions[0].QuestionType.Should().Be("Checkbox");
        resultsQuestions[1].ConditionalQuestionCode.Should().Be("RNA_CONTRIBUTORS");
        resultsQuestions[1].ConditionalValue.Should().Be("other");

        // Unselected key results (rna_information) contribute nothing.
        dto.Questions.Should().NotContain(q => q.Code == "RNA_INFORMATION");

        var delivery = dto.Questions.Where(q => q.Section == "delivery").ToList();
        delivery.Select(q => q.Code).Should().Equal(DeliveryCodes);
        delivery.Single(q => q.Code == "AKR_DELIVERY_PROGRESS").IsRequired.Should().BeTrue();
        delivery.Where(q => q.Code != "AKR_DELIVERY_PROGRESS").Should().OnlyContain(q => !q.IsRequired);
    }

    [Fact]
    public async Task FinishedRnaActivity_GetsClosedPhrasingAndFinishedOnlyConditionalChildren()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync(status: "finished");

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Select(q => q.Code).Should().Equal(
            "RNA_CONTRIBUTORS", "RNA_CONTRIBUTORS_OTHER", "RNA_PUBLISHED", "RNA_PUBLISHED_URL");
        resultsQuestions[0].Label.Should().Be("Who contributed evidence?");

        // Boolean protocol questions arrive as Radio with synthesised yes/no options.
        var published = resultsQuestions.Single(q => q.Code == "RNA_PUBLISHED");
        published.QuestionType.Should().Be("Radio");
        published.Options.Select(o => o.Value).Should().Equal("yes", "no");

        var publishedUrl = resultsQuestions.Single(q => q.Code == "RNA_PUBLISHED_URL");
        publishedUrl.ConditionalQuestionCode.Should().Be("RNA_PUBLISHED");
        publishedUrl.ConditionalValue.Should().Be("yes");
    }

    [Fact]
    public async Task ClosedRnaActivity_DoesNotGetTheFinishedOnlyChildren()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateRnaActivityAsync(status: "closed");

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var codes = dto.Questions.Where(q => q.Section == "results").Select(q => q.Code).ToList();
        codes.Should().Contain("RNA_PUBLISHED");
        codes.Should().NotContain("RNA_PUBLISHED_URL");
    }

    [Fact]
    public async Task CustomKeyResults_GetGenericRequiredProgressTextareas()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var activity = await CreateCompletedActivityAsync(expectedResults: DefaultExpectedResults);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var resultsQuestions = dto.Questions.Where(q => q.Section == "results").ToList();
        resultsQuestions.Should().HaveCount(2);
        resultsQuestions.Should().OnlyContain(q => q.Code.StartsWith("CUSTOM_") && q.QuestionType == "Textarea" && q.IsRequired);
        resultsQuestions[0].Label.Should().Be("Provide progress details for: Key result one");
        resultsQuestions[1].Label.Should().Be("Provide progress details for: Key result two");
    }

    [Fact]
    public async Task PeopleAndRolesActivity_GetsOnlyTheDeliveryQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (categoryId, taskId) = await GetTypeAsync(PeopleTaskName);

        // People-and-roles records have no key results (allowed: the >= 2 rule does not apply to that category)
        // but must answer the required seeded "Role details" questions to complete setup.
        var request = new CreateActivityRequestDto(
            "Public health officer role",
            categoryId,
            taskId,
            null,
            "active",
            4,
            2026,
            false,
            Array.Empty<ExpectedResultInputDto>(),
            new List<ActivityAnswerInputDto>
            {
                new("PR_FUNDING_TIME", "up_to_20"),
                new("PR_ROLE_DURATION", "6_12"),
                new("PR_ROLE_SHARED", "no"),
            });
        var response = await Client.PostAsJsonAsync("activities", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "creating the People-and-roles activity failed: {0}", await response.Content.ReadAsStringAsync());
        var activity = await response.ToResponseModel<ActivityDto>();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Questions.Should().HaveCount(3);
        dto.Questions.Should().OnlyContain(q => q.Section == "delivery");
        dto.Questions.Select(q => q.Code).Should().Equal(DeliveryCodes);
    }

    [Fact]
    public async Task WhoWithActivityAnswers_PrefillTheContributorQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");

        // The RNA type sits in the "Needs assessment" category, whose seeded form schema prefixes codes with NA_.
        var activity = await CreateCompletedActivityAsync(
            RnaTaskName,
            "active",
            results,
            answers:
            [
                new ActivityAnswerInputDto("NA_WHO_WITH", "licensing,other"),
                new ActivityAnswerInputDto("NA_WHO_WITH_OTHER", "Partner org"),
            ]);

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "licensing,other");
        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS_OTHER" && a.Value == "Partner org");
    }

    [Fact]
    public async Task SavedReportAnswers_TakePrecedenceOverThePrefill()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartReportAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");
        var activity = await CreateCompletedActivityAsync(
            RnaTaskName,
            "active",
            results,
            answers: [new ActivityAnswerInputDto("NA_WHO_WITH", "licensing")]);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("RNA_CONTRIBUTORS", "police")))
            .EnsureSuccessStatusCode();

        var dto = await GetActivityQuestionsAsync(collectionId, activity.Id);

        dto.Answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "police");
    }
}
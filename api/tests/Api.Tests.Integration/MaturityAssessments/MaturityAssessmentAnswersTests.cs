using System.Net;
using Api.Dtos;
using Application.Common.Features;
using Application.Common.Validation;
using Application.MaturityAssessments.Protocol;
using Domain.MaturityAssessments;
using Domain.Users;
using FluentAssertions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// Completing a section page by page (BLUM-7858): per-page merge, save-and-return drafts, the validation that runs
// on Continue and on the section's check-your-answers, and how a saved answer's source flips to the user.
public class MaturityAssessmentAnswersTests(IntegrationTestWebFactory factory) : MaturityAssessmentTestBase(factory)
{
    // Creates and starts a window as the caller's identity. The test body authenticates first: TestIdentityContext is
    // AsyncLocal, so an AuthenticateAs(...) inside an awaited helper would not survive the helper returning.
    private async Task<Guid> StartedAsync()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await StartAssessmentAsync(collectionId);
        return collectionId;
    }

    [Fact]
    public async Task SavingOnePage_LeavesTheOthersAlone_AndMarksTheSectionInProgress()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var first = await SavePageAsync(collectionId, "la_capacity", "people_and_roles", false, ("MA_DESIGNATED_LEAD", "yes"));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("in_progress");

        var second = await SavePageAsync(collectionId, "la_capacity", "business_plan", false, ("MA_BUSINESS_PLAN", "no"));
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var section = await GetSectionAsync(collectionId, "la_capacity");
        section.Status.Should().Be("in_progress");
        section.Answers.Should().BeEquivalentTo(
        [
            new MaturityAnswerDto("MA_DESIGNATED_LEAD", "yes", "user"),
            new MaturityAnswerDto("MA_BUSINESS_PLAN", "no", "user"),
        ]);
        section.Pages.Select(p => p.Code).Should().Contain(["people_and_roles", "non_funded_roles", "funded_roles", "business_plan", "strategic_network", "shared_resources"]);

        var taskList = await (await GetMaturityTaskListAsync(collectionId)).ToResponseModel<MaturityTaskListDto>();
        taskList.Sections.Single(s => s.Code == "la_capacity").Status.Should().Be("in_progress");
        taskList.Sections.Where(s => s.Code != "la_capacity").Should().OnlyContain(s => s.Status == "not_started");
    }

    [Fact]
    public async Task ABlankValue_ClearsThatAnswerOnly()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        (await SavePageAsync(collectionId, "la_capacity", "people_and_roles", false, ("MA_DESIGNATED_LEAD", "yes"))).EnsureSuccessStatusCode();
        (await SavePageAsync(collectionId, "la_capacity", "business_plan", false, ("MA_BUSINESS_PLAN", "no"))).EnsureSuccessStatusCode();

        (await SavePageAsync(collectionId, "la_capacity", "people_and_roles", true, ("MA_DESIGNATED_LEAD", ""))).EnsureSuccessStatusCode();

        var section = await GetSectionAsync(collectionId, "la_capacity");
        section.Answers.Select(a => a.QuestionCode).Should().Equal("MA_BUSINESS_PLAN");
    }

    [Fact]
    public async Task ADraftSave_SkipsRequired_ButContinueEnforcesIt()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        (await SavePageAsync(collectionId, "la_capacity", "strategic_network", saveAsDraft: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await SavePageAsync(collectionId, "la_capacity", "strategic_network", saveAsDraft: false);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("SUBMISSION_ANSWERS_INVALID");
        body.Should().Contain("MA_STRATEGIC_NETWORK");
        body.Should().Contain("Select an answer for");
    }

    [Fact]
    public async Task AHiddenConditionalQuestion_IsNeitherRequiredNorPersisted()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SavePageAsync(
            collectionId,
            "la_capacity",
            "strategic_network",
            false,
            ("MA_STRATEGIC_NETWORK", "no"),
            ("MA_STRATEGIC_NETWORK_DETAILS", "Hidden because the parent says no"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var section = await GetSectionAsync(collectionId, "la_capacity");
        section.Answers.Select(a => a.QuestionCode).Should().Equal("MA_STRATEGIC_NETWORK");
    }

    // BLUM-7924: the WTE is asked once per ticked non-funded role. One question revealed by any role rendered under
    // the first ticked box only; each role now reveals — and requires — its own, and loses it when unticked.
    [Fact]
    public async Task EachTickedNonFundedRole_RequiresItsOwnWte_AndAnUntickedRolesWteIsPruned()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        var officerWte = MaturityProtocol.NonFundedWteCode("public_health_officer");
        var analystWte = MaturityProtocol.NonFundedWteCode("public_health_analyst");

        var missing = await SavePageAsync(
            collectionId,
            "la_capacity",
            "non_funded_roles",
            false,
            ("MA_NONFUNDED_ROLES", "public_health_officer,public_health_analyst"),
            (officerWte, "up_to_0_2"));

        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await missing.Content.ReadAsStringAsync()).Should()
            .Contain("SUBMISSION_ANSWERS_INVALID")
            .And.Contain(analystWte)
            .And.Contain("Select the whole-time equivalent (WTE) for Public Health Analyst")
            .And.NotContain(officerWte);

        var both = await SavePageAsync(
            collectionId,
            "la_capacity",
            "non_funded_roles",
            false,
            ("MA_NONFUNDED_ROLES", "public_health_officer,public_health_analyst"),
            (officerWte, "up_to_0_2"),
            (analystWte, "over_1"));

        both.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSectionAsync(collectionId, "la_capacity")).Answers.Should().BeEquivalentTo(
        [
            new MaturityAnswerDto("MA_NONFUNDED_ROLES", "public_health_officer,public_health_analyst", "user"),
            new MaturityAnswerDto(officerWte, "up_to_0_2", "user"),
            new MaturityAnswerDto(analystWte, "over_1", "user"),
        ]);

        // The client sends every code on the page, a hidden reveal's included — so the analyst's band arrives again.
        var unticked = await SavePageAsync(
            collectionId,
            "la_capacity",
            "non_funded_roles",
            false,
            ("MA_NONFUNDED_ROLES", "public_health_officer"),
            (officerWte, "up_to_0_2"),
            (analystWte, "over_1"));

        unticked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAnswersAsync(collectionId)).Select(a => a.QuestionCode).Should().BeEquivalentTo(["MA_NONFUNDED_ROLES", officerWte]);
    }

    [Fact]
    public async Task AHiddenPage_IsSkippedByTheSectionCheck()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        // "No" hides Publishing dates, Research, Engagement and People with lived experience, leaving only the
        // regional HNA page to answer.
        (await SavePageAsync(collectionId, "needs_assessment", "publishing", false, ("MA_NA_PUBLISHED", "no"))).EnsureSuccessStatusCode();
        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", false, ("MA_NA_REGIONAL", "maybe"))).EnsureSuccessStatusCode();

        var complete = await CompleteSectionAsync(collectionId, "needs_assessment");

        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await complete.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("completed");
        (await GetAssessmentAsync(collectionId)).Sections.Single(s => s.SectionCode == "needs_assessment").Status.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task ARevealedPage_IsRequiredByTheSectionCheck()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        (await SavePageAsync(collectionId, "needs_assessment", "publishing", true, ("MA_NA_PUBLISHED", "yes"))).EnsureSuccessStatusCode();
        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", true, ("MA_NA_REGIONAL", "maybe"))).EnsureSuccessStatusCode();

        var complete = await CompleteSectionAsync(collectionId, "needs_assessment");

        complete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await complete.Content.ReadAsStringAsync();
        body.Should().Contain("MA_NA_URL").And.Contain("MA_NA_LAST_PUBLISHED").And.Contain("MA_NA_ENGAGEMENT__POLICE");
    }

    // MA_ADV_RESTRICTED → MA_ADV_ALL_PROPERTY → MA_ADV_EXEMPT. "No" to the first hides the second, but a value can sit
    // on the hidden second (Start persists hidden pre-fill, BLUM-8438), and "no" there is what reveals the third.
    // The page shows none of it (pruneHiddenAnswers). BLUM-9087 spike item 7: with the flag off the server still reads
    // that hidden answer and asks for the third; with it on, a question whose parent is hidden is hidden too.
    private async Task<HttpResponseMessage> CompleteAdvertisingWithAHiddenAnswerAsync()
    {
        var collectionId = await StartedAsync();
        (await SavePageAsync(collectionId, "advertising_restrictions", "council_owned_spaces", false, ("MA_ADV_RESTRICTED", "no"))).EnsureSuccessStatusCode();
        (await SavePageAsync(collectionId, "advertising_restrictions", "other_restrictions", false, ("MA_ADV_OTHER_COMMODITIES", "no"))).EnsureSuccessStatusCode();

        var assessment = await GetAssessmentAsync(collectionId);
        await Context.MaturityAssessmentAnswers.AddAsync(MaturityAssessmentAnswer.Record(
            MaturityAssessmentAnswerId.New(), assessment.Id, "MA_ADV_ALL_PROPERTY", "no", AnswerSource.Baseline2025, new UserId(MainOrgLeadUserId), DateTime.UtcNow));
        await SaveChangesAsync();

        return await CompleteSectionAsync(collectionId, "advertising_restrictions");
    }

    [Fact]
    public async Task ShouldAskForAQuestionUnderAHiddenParent_WhenTheFlagIsOff()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        AuthenticateAsMainOrgLead();

        var complete = await CompleteAdvertisingWithAHiddenAnswerAsync();

        complete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await complete.Content.ReadAsStringAsync()).Should().Contain("MA_ADV_EXEMPT");
    }

    [Fact]
    public async Task ShouldNotAskForAQuestionUnderAHiddenParent_WhenTheFlagIsOn()
    {
        await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: true);
        try
        {
            AuthenticateAsMainOrgLead();

            var complete = await CompleteAdvertisingWithAHiddenAnswerAsync();

            complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
            (await complete.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("completed");
        }
        finally
        {
            await factory.SetFeatureAsync(Feature.BLUM_9087, enabled: false);
        }
    }

    // BLUM-8438, sheet row 44: "In development" routes like "No" — past the steering group's members (rows 45-58)
    // and on to the action plan.
    [Theory]
    [InlineData("no")]
    [InlineData("in_development")]
    public async Task SteeringGroupNoOrInDevelopment_SkipsTheMembersPage(string answer)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        await CompleteSectionFullyAsync(collectionId, "strategic_approach", ("MA_SG_EXISTS", answer));

        var section = await GetSectionAsync(collectionId, "strategic_approach");
        section.Status.Should().Be("completed");
        section.Answers.Should().Contain(a => a.QuestionCode == "MA_SG_EXISTS" && a.Value == answer);
        section.Answers.Should().NotContain(a => a.QuestionCode.StartsWith("MA_SG_MEMBERS"));
    }

    [Fact]
    public async Task SteeringGroupYes_RequiresTheMembersPage()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        (await SavePageAsync(collectionId, "strategic_approach", "steering_group", false, ("MA_SG_EXISTS", "yes"))).EnsureSuccessStatusCode();

        var complete = await CompleteSectionAsync(collectionId, "strategic_approach");

        complete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await complete.Content.ReadAsStringAsync()).Should().Contain("MA_SG_MEMBERS__POLICE");
    }

    // BLUM-8438, sheet row 102: "Other" routes like "Yes" and "In progress" — on to the campaign's audience, aim and
    // resources link (rows 103-105). The audience and the aim are required there; the link is optional.
    [Theory]
    [InlineData("yes")]
    [InlineData("in_progress")]
    [InlineData("other")]
    public async Task CampaignYesInProgressOrOther_RequiresTheFollowUps(string answer)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SavePageAsync(
            collectionId,
            "local_campaigns",
            "raising_awareness",
            false,
            ("MA_CAMPAIGN", answer),
            ("MA_CAMPAIGN_OTHER", "Run jointly with a neighbouring authority"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should()
            .Contain("SUBMISSION_ANSWERS_INVALID")
            .And.Contain("MA_CAMPAIGN_AUDIENCE")
            .And.Contain("MA_CAMPAIGN_AIMS")
            .And.NotContain("MA_CAMPAIGN_URL");
    }

    [Fact]
    public async Task CampaignOther_KeepsItsFreeTextAndTheFollowUps()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SavePageAsync(
            collectionId,
            "local_campaigns",
            "raising_awareness",
            false,
            ("MA_CAMPAIGN", "other"),
            ("MA_CAMPAIGN_OTHER", "Run jointly with a neighbouring authority"),
            ("MA_CAMPAIGN_AUDIENCE", "Students and young adults"),
            ("MA_CAMPAIGN_AIMS", "reducing_stigma"),
            ("MA_CAMPAIGN_URL", "https://www.example.gov.uk/campaign"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await GetSectionAsync(collectionId, "local_campaigns")).Answers.Should().BeEquivalentTo(
        [
            new MaturityAnswerDto("MA_CAMPAIGN", "other", "user"),
            new MaturityAnswerDto("MA_CAMPAIGN_OTHER", "Run jointly with a neighbouring authority", "user"),
            new MaturityAnswerDto("MA_CAMPAIGN_AUDIENCE", "Students and young adults", "user"),
            new MaturityAnswerDto("MA_CAMPAIGN_AIMS", "reducing_stigma", "user"),
            new MaturityAnswerDto("MA_CAMPAIGN_URL", "https://www.example.gov.uk/campaign", "user"),
        ]);

        var complete = await CompleteSectionAsync(collectionId, "local_campaigns");
        (await complete.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("completed");
    }

    // The client blanks a hidden reveal before it saves (pruneHiddenAnswers). A caller that sends the follow-ups'
    // values anyway has them dropped rather than stored.
    [Theory]
    [InlineData("no")]
    [InlineData("dont_know")]
    public async Task CampaignNoOrDontKnow_SkipsTheFollowUps(string answer)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SavePageAsync(
            collectionId,
            "local_campaigns",
            "raising_awareness",
            false,
            ("MA_CAMPAIGN", answer),
            ("MA_CAMPAIGN_AUDIENCE", "Students and young adults"),
            ("MA_CAMPAIGN_AIMS", "reducing_stigma"),
            ("MA_CAMPAIGN_URL", "https://www.example.gov.uk/campaign"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await GetSectionAsync(collectionId, "local_campaigns")).Answers.Select(a => a.QuestionCode).Should().Equal("MA_CAMPAIGN");

        var complete = await CompleteSectionAsync(collectionId, "local_campaigns");
        (await complete.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("completed");
    }

    [Fact]
    public async Task AValueOutsideTheOptions_IsRefusedEvenAsADraft()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var response = await SavePageAsync(collectionId, "la_capacity", "people_and_roles", true, ("MA_DESIGNATED_LEAD", "maybe"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await GetSectionAsync(collectionId, "la_capacity")).Answers.Should().BeEmpty();
    }

    [Fact]
    public async Task LongText_IsAcceptedByADraft_AndOnContinue()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        var longText = new string('x', 5000);

        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", true, ("MA_NA_REGIONAL", "yes"), ("MA_NA_REGIONAL_IMPROVE", longText)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", false, ("MA_NA_REGIONAL", "yes"), ("MA_NA_REGIONAL_IMPROVE", longText)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAnswersAsync(collectionId)).Single(a => a.QuestionCode == "MA_NA_REGIONAL_IMPROVE").Value.Should().Be(longText);

        // No limit is sent, so the client renders no character count (BLUM-8411).
        var section = await GetSectionAsync(collectionId, "needs_assessment");
        section.Pages.SelectMany(p => p.Questions).Should().OnlyContain(q => q.MaxLength == null && q.MaxLengthErrorMessage == null);
    }

    // The one bound BLUM-8411 left: FieldLimits.AnswerText, which the client mirrors as MATURITY_ANSWER_MAX_LENGTH so
    // the LA sees it on the field instead of a generic failure. The draft exemption the 500 limit had does not apply.
    [Fact]
    public async Task TextPastTheAnswerCeiling_IsRefusedEvenAsADraft()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        var atCeiling = new string('x', FieldLimits.AnswerText);

        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", false, ("MA_NA_REGIONAL", "yes"), ("MA_NA_REGIONAL_IMPROVE", atCeiling)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var draft = await SavePageAsync(collectionId, "needs_assessment", "regional_hna", true, ("MA_NA_REGIONAL", "yes"), ("MA_NA_REGIONAL_IMPROVE", atCeiling + "x"));

        draft.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await draft.Content.ReadAsStringAsync()).Should().Contain("validation_failed").And.Contain("Value");
        (await GetAnswersAsync(collectionId)).Single(a => a.QuestionCode == "MA_NA_REGIONAL_IMPROVE").Value.Should().Be(atCeiling);
    }

    [Fact]
    public async Task ALongBaselineAnswer_DoesNotBlockTheSectionCheck()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, ("MA_NA_REGIONAL_IMPROVE", new string('b', 700)));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        (await SavePageAsync(collectionId, "needs_assessment", "publishing", true, ("MA_NA_PUBLISHED", "no"))).EnsureSuccessStatusCode();
        (await SavePageAsync(collectionId, "needs_assessment", "regional_hna", true, ("MA_NA_REGIONAL", "yes"))).EnsureSuccessStatusCode();

        var complete = await CompleteSectionAsync(collectionId, "needs_assessment");

        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        (await GetAnswersAsync(collectionId)).Single(a => a.QuestionCode == "MA_NA_REGIONAL_IMPROVE").Value.Should().HaveLength(700);
    }

    [Fact]
    public async Task TheRolesRepeater_IsValidatedRowByRow()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var invalid = await SavePageAsync(collectionId, "la_capacity", "funded_roles", false, ("MA_FUNDED_ROLES", "[{\"role\":\"other\",\"time\":\"21_40\"}]"));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("other role for role 1");

        var valid = await SavePageAsync(collectionId, "la_capacity", "funded_roles", false, ("MA_FUNDED_ROLES", "[{\"role\":\"other\",\"time\":\"21_40\",\"otherRole\":\"Data analyst\"}]"));
        valid.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = (await GetAnswersAsync(collectionId)).Single(a => a.QuestionCode == "MA_FUNDED_ROLES");
        MaturityRolesAnswer.TryParse(stored.Value, out var entries).Should().BeTrue();
        entries.Should().ContainSingle().Which.OtherRole.Should().Be("Data analyst");
    }

    // BLUM-9112: "None" answers the funded-roles question on its own — accepted on Continue with no time, stored with
    // its time and other-role name dropped, and refused beside a real role.
    [Fact]
    public async Task TheRolesRepeater_AcceptsNoneOnItsOwn()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var alongside = await SavePageAsync(collectionId, "la_capacity", "funded_roles", false, ("MA_FUNDED_ROLES", "[{\"role\":\"public_health_officer\",\"time\":\"81_100\"},{\"role\":\"none\"}]"));
        alongside.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await alongside.Content.ReadAsStringAsync()).Should().Contain("Select a valid role for role 2.");

        var none = await SavePageAsync(collectionId, "la_capacity", "funded_roles", false, ("MA_FUNDED_ROLES", "[{\"role\":\"none\",\"time\":\"21_40\",\"otherRole\":\"x\"}]"));
        none.StatusCode.Should().Be(HttpStatusCode.OK, await none.Content.ReadAsStringAsync());

        (await GetSectionAsync(collectionId, "la_capacity")).Answers.Should()
            .ContainSingle(a => a.QuestionCode == "MA_FUNDED_ROLES")
            .Which.Should().Be(new MaturityAnswerDto("MA_FUNDED_ROLES", "[{\"role\":\"none\",\"time\":\"\",\"otherRole\":null}]", "user"));
    }

    // BLUM-9084: "None" answers the non-funded roles question on its own. The page unticks every other box when it is
    // ticked, so only a request from elsewhere can send both, and the server refuses it rather than storing it.
    [Fact]
    public async Task TheNonFundedRoles_RefuseNoneBesideARole()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        var alongside = await SavePageAsync(collectionId, "la_capacity", "non_funded_roles", true, ("MA_NONFUNDED_ROLES", "none,public_health_officer"));
        alongside.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await alongside.Content.ReadAsStringAsync()).Should().Contain("Select valid options for “Which roles are not funded by the levy?”.");
        (await GetAnswersAsync(collectionId)).Should().NotContain(a => a.QuestionCode == "MA_NONFUNDED_ROLES");

        var none = await SavePageAsync(collectionId, "la_capacity", "non_funded_roles", false, ("MA_NONFUNDED_ROLES", "none"));
        none.StatusCode.Should().Be(HttpStatusCode.OK, await none.Content.ReadAsStringAsync());
        (await GetAnswersAsync(collectionId)).Should().ContainSingle(a => a.QuestionCode == "MA_NONFUNDED_ROLES").Which.Value.Should().Be("none");
    }

    [Fact]
    public async Task EditingAPrefilledAnswer_FlipsItsSourceToUser()
    {
        var collectionId = await CreateMaturityCollectionAsync();
        await SeedBaselineAnswersAsync(MainOrg, ("MA_DESIGNATED_LEAD", "no"), ("MA_BUSINESS_PLAN", "yes"));
        AuthenticateAsMainOrgLead();
        await StartAssessmentAsync(collectionId);

        (await GetSectionAsync(collectionId, "la_capacity")).Answers
            .Should().Contain(a => a.QuestionCode == "MA_DESIGNATED_LEAD" && a.Value == "no" && a.Source == AnswerSources.Baseline2025);

        (await SavePageAsync(collectionId, "la_capacity", "people_and_roles", false, ("MA_DESIGNATED_LEAD", "yes"))).EnsureSuccessStatusCode();

        var section = await GetSectionAsync(collectionId, "la_capacity");
        section.Answers.Should().Contain(a => a.QuestionCode == "MA_DESIGNATED_LEAD" && a.Value == "yes" && a.Source == AnswerSources.User);
        section.Answers.Should().Contain(a => a.QuestionCode == "MA_BUSINESS_PLAN" && a.Source == AnswerSources.Baseline2025, "untouched pages keep their source");
    }

    [Fact]
    public async Task ACompletedSection_GoesBackToInProgress_WhenEdited()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();
        await CompleteSectionFullyAsync(collectionId, "la_capacity");

        (await GetSectionAsync(collectionId, "la_capacity")).Status.Should().Be("completed");

        var save = await SavePageAsync(collectionId, "la_capacity", "people_and_roles", true, ("MA_DESIGNATED_LEAD", "no"));
        (await save.ToResponseModel<MaturitySectionStateDto>()).Status.Should().Be("in_progress");
    }

    [Fact]
    public async Task AnUnknownSectionOrPage_IsNotFound()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartedAsync();

        (await GetSectionResponseAsync(collectionId, "nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SavePageAsync(collectionId, "nope", "people_and_roles", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SavePageAsync(collectionId, "la_capacity", "nope", true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CompleteSectionAsync(collectionId, "nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
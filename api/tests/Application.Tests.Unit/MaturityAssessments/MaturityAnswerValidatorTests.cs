using Application.MaturityAssessments.Protocol;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// The maturity validator adds two things to the shared SubmissionAnswerValidator: page-level visibility (a hidden
// page's questions are neither required nor persisted) and the funded-roles repeater's JSON rules. Draft saves
// additionally ignore max-length, so an over-length 2025 baseline answer can be stored and refused only on
// Continue.
public class MaturityAnswerValidatorTests
{
    private static readonly IReadOnlyList<MaturityResolvedQuestion> NeedsAssessment = MaturityQuestionResolver.ResolveSection("needs_assessment");

    private static readonly IReadOnlyList<MaturityResolvedQuestion> LaCapacity = MaturityQuestionResolver.ResolveSection("la_capacity");

    private static Dictionary<string, string?> Answers(params (string Code, string? Value)[] answers) =>
        answers.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal);

    [Fact]
    public void Validate_DoesNotRequireQuestionsOnAHiddenPage()
    {
        var answers = Answers(("MA_NA_PUBLISHED", "no"));

        var errors = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: true, transitive: false);

        errors.Select(e => e.QuestionCode).Should().NotContain(["MA_NA_LAST_PUBLISHED", "MA_NA_RESEARCH", "MA_NA_ENGAGEMENT__POLICE", "MA_NA_LIVED_EXPERIENCE"]);
        errors.Select(e => e.QuestionCode).Should().Contain("MA_NA_REGIONAL");
    }

    [Fact]
    public void Validate_RequiresTheRevealedPagesOnceTheParentSaysYes()
    {
        var answers = Answers(("MA_NA_PUBLISHED", "yes"), ("MA_NA_REGIONAL", "yes"));

        var errors = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: true, transitive: false);

        errors.Select(e => e.QuestionCode).Should().Contain(["MA_NA_URL", "MA_NA_LAST_PUBLISHED", "MA_NA_RESEARCH", "MA_NA_ENGAGEMENT__POLICE", "MA_NA_LIVED_EXPERIENCE"]);
        errors.Select(e => e.QuestionCode).Should().NotContain("MA_NA_ENGAGEMENT_OTHER", "it is optional");
    }

    // BLUM-9115: the URL question's hint asks an LA whose assessment is not online to say "N/A", so that answer must
    // pass on Continue and be stored as typed.
    [Fact]
    public void Validate_AcceptsNotApplicableForThePublishedAssessmentUrl()
    {
        var answers = Answers(("MA_NA_PUBLISHED", "yes"), ("MA_NA_URL", "N/A"));

        var errors = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: true, transitive: false);
        var persistable = MaturityAnswerValidator.CollectPersistable(NeedsAssessment, answers, transitive: false);

        errors.Should().NotContain(e => e.QuestionCode == "MA_NA_URL");
        persistable.Should().Contain(p => p.Question.Code == "MA_NA_URL" && p.Value == "N/A");
    }

    [Fact]
    public void Validate_AcceptsLongFreeText_OnADraftAndOnComplete()
    {
        var answers = Answers(("MA_NA_REGIONAL_IMPROVE", new string('x', 5000)));

        var draft = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: false, transitive: false);
        var final = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: true, transitive: false);

        draft.Should().BeEmpty();
        final.Should().NotContain(e => e.QuestionCode == "MA_NA_REGIONAL_IMPROVE");
    }

    [Fact]
    public void Validate_RefusesAValueOutsideTheOptionList_EvenOnADraft()
    {
        var answers = Answers(("MA_NA_PUBLISHED", "definitely"));

        var errors = MaturityAnswerValidator.Validate(NeedsAssessment, answers, enforceRequired: false, transitive: false);

        errors.Should().ContainSingle(e => e.QuestionCode == "MA_NA_PUBLISHED");
    }

    [Fact]
    public void Validate_RefusesMalformedRolesJson()
    {
        var answers = Answers(("MA_FUNDED_ROLES", "not json"));

        var errors = MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: false, transitive: false);

        errors.Should().ContainSingle(e => e.QuestionCode == "MA_FUNDED_ROLES" && e.Message.Contains("expected format"));
    }

    [Fact]
    public void Validate_RequiresAnOtherRoleName_AndAValidTime()
    {
        var roles = MaturityRolesAnswer.Serialize(
        [
            new MaturityRolesAnswer.Entry("other", "21_40", null),
            new MaturityRolesAnswer.Entry("public_health_analyst", "half", null),
        ]);
        var answers = Answers(("MA_FUNDED_ROLES", roles));

        var errors = MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: true, transitive: false);

        errors.Should().Contain(e => e.Message.Contains("other role for role 1"));
        errors.Should().Contain(e => e.Message.Contains("time the funding is paying for role 2"));
    }

    // "Save and come back later" keeps a half-finished row (the client waives the same checks), but a value outside
    // the option list is still refused — the two sides must agree or every partial-row draft save 422s.
    [Fact]
    public void Validate_LetsAHalfFinishedRoleThroughOnADraft_ButStillRefusesAnUnknownTime()
    {
        var partial = MaturityRolesAnswer.Serialize([new MaturityRolesAnswer.Entry("other", string.Empty, null), new MaturityRolesAnswer.Entry(string.Empty, "21_40", null)]);
        MaturityAnswerValidator.Validate(LaCapacity, Answers(("MA_FUNDED_ROLES", partial)), enforceRequired: false, transitive: false)
            .Should().NotContain(e => e.QuestionCode == "MA_FUNDED_ROLES");

        var unknown = MaturityRolesAnswer.Serialize([new MaturityRolesAnswer.Entry("public_health_officer", "half", null)]);
        MaturityAnswerValidator.Validate(LaCapacity, Answers(("MA_FUNDED_ROLES", unknown)), enforceRequired: false, transitive: false)
            .Should().ContainSingle(e => e.Message.Contains("time the funding is paying for role 1"));
    }

    [Fact]
    public void Validate_AcceptsAWellFormedRepeater()
    {
        var roles = MaturityRolesAnswer.Serialize(
        [
            new MaturityRolesAnswer.Entry("public_health_officer", "81_100", null),
            new MaturityRolesAnswer.Entry("other", "up_to_20", "Data analyst"),
        ]);
        var answers = Answers(("MA_FUNDED_ROLES", roles));

        var errors = MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: false, transitive: false);

        errors.Should().NotContain(e => e.QuestionCode == "MA_FUNDED_ROLES");
    }

    // BLUM-9112: "None" answers the question on its own — no time is asked for it.
    [Fact]
    public void Validate_AcceptsNoneAsTheOnlyRole_WithoutATime()
    {
        var answers = Answers(("MA_FUNDED_ROLES", "[{\"role\":\"none\",\"time\":\"\",\"otherRole\":null}]"));

        MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: true, transitive: false)
            .Should().NotContain(e => e.QuestionCode == "MA_FUNDED_ROLES");
    }

    [Fact]
    public void Validate_RefusesNoneAlongsideAnotherRole()
    {
        var roles = MaturityRolesAnswer.Serialize(
        [
            new MaturityRolesAnswer.Entry("public_health_officer", "81_100", null),
            new MaturityRolesAnswer.Entry("none", string.Empty, null),
        ]);

        MaturityAnswerValidator.Validate(LaCapacity, Answers(("MA_FUNDED_ROLES", roles)), enforceRequired: false, transitive: false)
            .Should().ContainSingle(e => e.QuestionCode == "MA_FUNDED_ROLES" && e.Message == "Select a valid role for role 2.");
    }

    // Only the question's ExclusiveOptionValue stands alone. Without one, "none" is an ordinary value: refused when
    // the question does not offer it, and asked for a time like any role when it does.
    [Fact]
    public void Validate_TreatsNoneAsAnOrdinaryValue_WhenTheQuestionDeclaresNoExclusiveValue()
    {
        var funded = LaCapacity.Single(q => q.Code == "MA_FUNDED_ROLES");
        var answers = Answers(("MA_FUNDED_ROLES", "[{\"role\":\"none\",\"time\":\"\",\"otherRole\":null}]"));

        MaturityAnswerValidator.Validate([funded with { ExclusiveOptionValue = null, Options = MaturityProtocol.RoleOptions }], answers, enforceRequired: false, transitive: false)
            .Should().ContainSingle(e => e.Message == "Select a valid role for role 1.");
        MaturityAnswerValidator.Validate([funded with { ExclusiveOptionValue = null }], answers, enforceRequired: true, transitive: false)
            .Should().ContainSingle(e => e.Message.Contains("time the funding is paying for role 1"));
    }

    // BLUM-9084: the non-funded roles checkbox's "None" stands alone on the server too, not only on the page.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_RefusesNoneBesideAnotherOption_OnACheckbox(bool enforceRequired)
    {
        var answers = Answers(("MA_NONFUNDED_ROLES", "public_health_officer, none"));

        MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired, transitive: false)
            .Should().ContainSingle(e => e.QuestionCode == "MA_NONFUNDED_ROLES")
            .Which.Message.Should().Be("Select valid options for “Which roles are not funded by the levy?”.");
    }

    [Fact]
    public void Validate_AcceptsNoneOnItsOwn_OnACheckbox()
    {
        MaturityAnswerValidator.Validate(LaCapacity, Answers(("MA_NONFUNDED_ROLES", "none")), enforceRequired: true, transitive: false)
            .Should().NotContain(e => e.QuestionCode == "MA_NONFUNDED_ROLES");
        MaturityAnswerValidator.Validate(LaCapacity, Answers(("MA_NONFUNDED_ROLES", "none,none")), enforceRequired: false, transitive: false)
            .Should().NotContain(e => e.QuestionCode == "MA_NONFUNDED_ROLES", "ticking None twice is still None alone");
    }

    // Without an ExclusiveOptionValue, "none" is just one of the options a checkbox may hold alongside others.
    [Fact]
    public void Validate_TreatsNoneAsAnOrdinaryOption_WhenTheCheckboxDeclaresNoExclusiveValue()
    {
        var nonFunded = LaCapacity.Single(q => q.Code == "MA_NONFUNDED_ROLES");

        MaturityAnswerValidator.Validate([nonFunded with { ExclusiveOptionValue = null }], Answers(("MA_NONFUNDED_ROLES", "public_health_officer,none")), enforceRequired: false, transitive: false)
            .Should().BeEmpty();
    }

    [Fact]
    public void Validate_RequiresAtLeastOneRole_OnlyWhenEnforcingRequired()
    {
        var answers = Answers(("MA_FUNDED_ROLES", "[]"));

        MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: false, transitive: false)
            .Should().NotContain(e => e.QuestionCode == "MA_FUNDED_ROLES");
        MaturityAnswerValidator.Validate(LaCapacity, answers, enforceRequired: true, transitive: false)
            .Should().Contain(e => e.QuestionCode == "MA_FUNDED_ROLES");
    }

    [Fact]
    public void CollectPersistable_DropsHiddenPagesAndBlankValues()
    {
        var answers = Answers(("MA_NA_PUBLISHED", "no"), ("MA_NA_URL", "https://example.org"), ("MA_NA_REGIONAL", "maybe"), ("MA_NA_REGIONAL_IMPROVE", " "));

        var persistable = MaturityAnswerValidator.CollectPersistable(NeedsAssessment, answers, transitive: false);

        persistable.Select(p => p.Question.Code).Should().BeEquivalentTo(["MA_NA_PUBLISHED", "MA_NA_REGIONAL"]);
    }

    [Fact]
    public void CollectPersistable_CanonicalisesTheRolesJson()
    {
        var answers = Answers(("MA_FUNDED_ROLES", "[{\"role\":\" public_health_officer \",\"time\":\"81_100\"}]"));

        var persistable = MaturityAnswerValidator.CollectPersistable(LaCapacity, answers, transitive: false);

        var roles = persistable.Single(p => p.Question.Code == "MA_FUNDED_ROLES").Value;
        MaturityRolesAnswer.TryParse(roles, out var entries).Should().BeTrue();
        entries.Should().ContainSingle().Which.Should().Be(new MaturityRolesAnswer.Entry("public_health_officer", "81_100", null));
    }

    [Fact]
    public void CollectPersistable_StoresNoneWithNoTimeOrOtherRole()
    {
        var answers = Answers(("MA_FUNDED_ROLES", "[{\"role\":\"none\",\"time\":\"21_40\",\"otherRole\":\"Data analyst\"}]"));

        var persistable = MaturityAnswerValidator.CollectPersistable(LaCapacity, answers, transitive: false);

        var roles = persistable.Single(p => p.Question.Code == "MA_FUNDED_ROLES").Value;
        roles.Should().Be("[{\"role\":\"none\",\"time\":\"\",\"otherRole\":null}]");
    }

    [Fact]
    public void IsPageVisible_IsTrueWithNoRule_AndMatchesAnyListedValue()
    {
        MaturityAnswerValidator.IsPageVisible(MaturityAnswerValidator.Visibility([], Answers(), transitive: false), null).Should().BeTrue();

        var rule = new MaturityPageVisibility("MA_CAMPAIGN", ["yes", "in_progress"]);
        MaturityAnswerValidator.IsPageVisible(MaturityAnswerValidator.Visibility([], Answers(("MA_CAMPAIGN", "in_progress")), transitive: false), rule).Should().BeTrue();
        MaturityAnswerValidator.IsPageVisible(MaturityAnswerValidator.Visibility([], Answers(("MA_CAMPAIGN", "no")), transitive: false), rule).Should().BeFalse();
        MaturityAnswerValidator.IsPageVisible(MaturityAnswerValidator.Visibility([], Answers(), transitive: false), rule).Should().BeFalse();
    }
}
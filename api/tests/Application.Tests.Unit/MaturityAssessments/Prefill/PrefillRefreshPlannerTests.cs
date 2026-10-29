using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using Domain.MaturityAssessments;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments.Prefill;

// What a quarterly report submitted AFTER the assessment was started may change (BLUM-8927): only quarterly values,
// only on pages the LA has not saved, never inside a section they have completed, and never a removal on its own.
public class PrefillRefreshPlannerTests
{
    private static readonly MaturityAssessmentId AssessmentId = MaturityAssessmentId.New();

    // Section "capacity" has two pages; "strategy" has one.
    private static readonly IReadOnlyList<MaturityResolvedQuestion> Questions =
    [
        Question("MA_A", "capacity", "roles"),
        Question("MA_B", "capacity", "roles"),
        Question("MA_C", "capacity", "network"),
        Question("MA_D", "strategy", "plan"),
    ];

    private static readonly IReadOnlySet<string> NoneCompleted = new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void Plan_FillsABlankOnAPageTheLaHasNotSaved()
    {
        var plan = PrefillRefreshPlanner.Plan(Questions, NoneCompleted, [], [Quarterly("MA_C", "yes")]);

        plan.Remove.Should().BeEmpty();
        plan.Write.Should().ContainSingle().Which.Should().Be(new MaturityPrefilledAnswer("MA_C", "yes", AnswerSource.Quarterly));
    }

    [Theory]
    [InlineData(AnswerSources.Baseline2025, "no")]
    [InlineData(AnswerSources.PriorAssessment, "no")]
    [InlineData(AnswerSources.Quarterly, "no")]
    [InlineData(AnswerSources.Baseline2025, "yes")]
    public void Plan_ReplacesARowThatIsStillPrefilled(string storedSource, string storedValue)
    {
        var stored = Stored("MA_C", storedValue, AnswerSource.From(storedSource));

        var plan = PrefillRefreshPlanner.Plan(Questions, NoneCompleted, [stored], [Quarterly("MA_C", "yes")]);

        plan.Remove.Should().Equal(stored.Id);
        plan.Write.Should().ContainSingle().Which.Should().Be(new MaturityPrefilledAnswer("MA_C", "yes", AnswerSource.Quarterly));
    }

    [Fact]
    public void Plan_WritesNothing_WhenTheQuarterlyValueIsAlreadyStored()
    {
        var plan = PrefillRefreshPlanner.Plan(
            Questions,
            NoneCompleted,
            [Stored("MA_C", "yes", AnswerSource.Quarterly)],
            [Quarterly("MA_C", "yes")]);

        plan.IsEmpty.Should().BeTrue();
        plan.Remove.Should().BeEmpty();
    }

    // One user row means the LA saved the page: MA_A is theirs, and MA_B beside it is a blank they left or cleared.
    [Fact]
    public void Plan_LeavesASavedPageAlone_IncludingItsBlanks()
    {
        var plan = PrefillRefreshPlanner.Plan(
            Questions,
            NoneCompleted,
            [Stored("MA_A", "no", AnswerSource.User)],
            [Quarterly("MA_A", "yes"), Quarterly("MA_B", "yes"), Quarterly("MA_C", "yes")]);

        plan.Remove.Should().BeEmpty();
        plan.Write.Select(w => w.QuestionCode).Should().Equal("MA_C");
    }

    [Fact]
    public void Plan_LeavesACompletedSectionAlone()
    {
        var plan = PrefillRefreshPlanner.Plan(
            Questions,
            new HashSet<string>(StringComparer.Ordinal) { "capacity" },
            [Stored("MA_C", "no", AnswerSource.Baseline2025)],
            [Quarterly("MA_A", "yes"), Quarterly("MA_C", "yes"), Quarterly("MA_D", "yes")]);

        plan.Remove.Should().BeEmpty();
        plan.Write.Select(w => w.QuestionCode).Should().Equal("MA_D");
    }

    // A failed quarterly read comes back as "no quarterly values", so nothing may be taken away on that evidence —
    // and a lower-precedence value is not this refresh's to write.
    [Fact]
    public void Plan_NeverRemovesARowWithoutAQuarterlyReplacement()
    {
        var plan = PrefillRefreshPlanner.Plan(
            Questions,
            NoneCompleted,
            [Stored("MA_C", "yes", AnswerSource.Quarterly)],
            [
                new MaturityPrefilledAnswer("MA_C", "no", AnswerSource.Baseline2025),
                new MaturityPrefilledAnswer("MA_D", "no", AnswerSource.PriorAssessment),
                Quarterly("MA_A", " "),
            ]);

        plan.IsEmpty.Should().BeTrue();
        plan.Remove.Should().BeEmpty();
    }

    [Fact]
    public void Plan_IgnoresACodeOutsideTheIncludedQuestions()
    {
        var plan = PrefillRefreshPlanner.Plan(Questions, NoneCompleted, [], [Quarterly("MA_ZZ", "yes")]);

        plan.IsEmpty.Should().BeTrue();
    }

    private static MaturityPrefilledAnswer Quarterly(string code, string value) => new(code, value, AnswerSource.Quarterly);

    private static MaturityAssessmentAnswer Stored(string code, string value, AnswerSource source) =>
        MaturityAssessmentAnswer.Record(MaturityAssessmentAnswerId.New(), AssessmentId, code, value, source, UserId.New(), DateTime.UtcNow);

    private static MaturityResolvedQuestion Question(string code, string section, string page) =>
        new(
            MaturityQuestionResolver.IdFor(code),
            code,
            Label: code,
            Hint: null,
            QuestionType: "radio",
            IsRequired: false,
            DisplayOrder: 0,
            ConditionalQuestionCode: null,
            ConditionalValue: null,
            Options: [],
            PrefillSource: null,
            ErrorMessage: null,
            Section: section,
            MaxLength: null,
            MaxLengthErrorMessage: null,
            SectionTitle: section,
            Page: page,
            PageTitle: page,
            PageVisibleWhen: null,
            GroupLabel: null,
            SecondaryOptions: [],
            ExclusiveOptionValue: null);
}
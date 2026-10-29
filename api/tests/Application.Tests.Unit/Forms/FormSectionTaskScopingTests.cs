using Application.Common.Interfaces.Queries;
using Domain.Activities;
using Domain.Categories;
using Domain.Forms;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Forms;

// FormSection.ActivityTaskId narrows a section to one activity type; a null one applies to every type. Both
// CreateActivityCommand and UpdateActivityCommand split the schema on it — required answers are enforced only
// over QuestionsForTask, while everything else is still validated and persisted, which is what keeps an answer
// to a de-scoped question rather than silently dropping it.
//
// Nothing seeds a scoped section any more: BLUM-7778 removed the last one (People and issues, scoped to each
// category's "Other ..." type by BLUM-7092), and the Form Designer's create/update section commands cannot set
// an ActivityTaskId at all. So the whole mechanism is now unreachable from the integration suite, and these are
// the only tests that exercise it. They exist so a future edit that inverts AppliesToTask, or drops the
// de-scoped-questions branch, fails here instead of shipping silently — the Form Designer is expected to gain
// type scoping eventually.
public class FormSectionTaskScopingTests
{
    private static readonly UserId Actor = UserId.New();
    private static readonly CategoryId Category = CategoryId.New();
    private static readonly ActivityTaskId OtherType = ActivityTaskId.New();
    private static readonly ActivityTaskId OrdinaryType = ActivityTaskId.New();

    private static FormSection Section(ActivityTaskId? activityTaskId) =>
        FormSection.Create(
            FormSectionId.New(), "People and issues", 0, Actor, categoryId: Category, activityTaskId: activityTaskId, utcNow: DateTime.UtcNow);

    private static FormQuestion Question(FormSection section, string code) =>
        FormQuestion.Create(
            FormQuestionId.New(), section.Id, code, $"Label for {code}", QuestionType.Text, 0, Actor, isRequired: true, utcNow: DateTime.UtcNow);

    [Fact]
    public void UnscopedSection_AppliesToEveryType()
    {
        var section = Section(null);

        section.AppliesToTask(OrdinaryType).Should().BeTrue();
        section.AppliesToTask(OtherType).Should().BeTrue();

        // A draft that has not picked a type yet still renders its unscoped sections.
        section.AppliesToTask(null).Should().BeTrue();
    }

    [Fact]
    public void ScopedSection_AppliesOnlyToItsOwnType()
    {
        var section = Section(OtherType);

        section.AppliesToTask(OtherType).Should().BeTrue();
        section.AppliesToTask(OrdinaryType).Should().BeFalse();

        // A scoped section is not shown before a type is chosen — otherwise every scoped screen would appear on
        // a fresh draft and then vanish.
        section.AppliesToTask(null).Should().BeFalse();
    }

    [Fact]
    public void QuestionsForTask_ReturnsUnscopedQuestionsPlusTheOnesScopedToThatType()
    {
        var shared = Section(null);
        var scoped = Section(OtherType);
        var sharedQuestion = Question(shared, "EDU_WHO_WITH");
        var scopedQuestion = Question(scoped, "EDU_WHO_FOR");
        var schema = new FormSchemaData([shared, scoped], [sharedQuestion, scopedQuestion]);

        schema.QuestionsForTask(OtherType).Select(q => q.Code)
            .Should().BeEquivalentTo("EDU_WHO_WITH", "EDU_WHO_FOR");

        // The de-scoped question is absent, which is what stops it being enforced as required — the commands
        // still validate and persist it from schema.Questions, so a stale client's answer is kept, not dropped.
        schema.QuestionsForTask(OrdinaryType).Select(q => q.Code).Should().BeEquivalentTo("EDU_WHO_WITH");
        schema.Questions.Should().HaveCount(2);
    }
}
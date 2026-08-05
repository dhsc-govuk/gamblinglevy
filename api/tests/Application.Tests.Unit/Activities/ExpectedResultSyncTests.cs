using Application.Activities.Commands;
using Domain.Activities;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Activities;

// The diff/merge must preserve row ids wherever a row survives an edit — the quarterly report keys custom
// ("Other") answers on CUSTOM_{rowId:N}, so a recreated row orphans the saved answer, while a wrongly paired
// row would transplant a stale answer onto an unrelated question (BLUM-6805). Plan is pure: rows are only
// amended when the caller applies the returned amendments.
public class ExpectedResultSyncTests
{
    private static readonly UserId Actor = UserId.New();
    private static readonly ActivityId ActivityRef = ActivityId.New();

    private static ActivityExpectedResult Standard(string code, int order, string? label = null) =>
        ActivityExpectedResult.Record(
            ActivityExpectedResultId.New(), ActivityRef, label ?? $"Label for {code}", order, Actor, code);

    private static ActivityExpectedResult Custom(string text, int order) =>
        ActivityExpectedResult.Record(ActivityExpectedResultId.New(), ActivityRef, text, order, Actor);

    [Fact]
    public void Plan_WithIdenticalResend_ReturnsEmptyPlan()
    {
        // Arrange
        var standard = Standard("kr_a", 0);
        var custom = Custom("My custom result", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [standard, custom],
            [("kr_a", "Label for kr_a"), (null, "My custom result")],
            ActivityRef,
            Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
    }

    [Fact]
    public void Plan_WithRewordedCustom_KeepsRowIdWithoutMutatingIt()
    {
        // Arrange
        var custom = Custom("Old text", 0);

        // Act
        var plan = ExpectedResultSync.Plan([custom], [(null, "New text")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        var amendment = plan.ToUpdate.Should().ContainSingle().Subject;
        amendment.Row.Id.Should().Be(custom.Id);
        amendment.Text.Should().Be("New text");
        custom.Text.Should().Be("Old text", "Plan must not mutate its inputs");
    }

    [Fact]
    public void Plan_WithAddedCustom_AddsOnlyTheNewRow()
    {
        // Arrange
        var custom = Custom("Existing", 0);

        // Act
        var plan = ExpectedResultSync.Plan(
            [custom], [(null, "Existing"), (null, "Brand new")], ActivityRef, Actor);

        // Assert
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        var added = plan.ToAdd.Should().ContainSingle().Subject;
        added.Text.Should().Be("Brand new");
        added.DisplayOrder.Should().Be(1);
        added.Id.Should().NotBe(custom.Id);
    }

    [Fact]
    public void Plan_WithRemovedCustom_RemovesOnlyThatRow()
    {
        // Arrange
        var keep = Custom("Keep", 0);
        var remove = Custom("Remove", 1);

        // Act
        var plan = ExpectedResultSync.Plan([keep, remove], [(null, "Keep")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().ContainSingle().Which.Id.Should().Be(remove.Id);
    }

    [Fact]
    public void Plan_WithRemovedAndAddedCustoms_NeverTransplantsTheOldRow()
    {
        // Arrange — delete the first custom and append a new one: the new entry must NOT inherit the old
        // row's id, or the deleted result's saved report answer would resurface under the new question.
        var deleted = Custom("Attend 5 community events", 0);
        var kept = Custom("Keep this one", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [deleted, kept], [(null, "Keep this one"), (null, "Publish a newsletter")], ActivityRef, Actor);

        // Assert
        plan.ToRemove.Should().ContainSingle().Which.Id.Should().Be(deleted.Id);
        var added = plan.ToAdd.Should().ContainSingle().Subject;
        added.Text.Should().Be("Publish a newsletter");
        added.Id.Should().NotBe(deleted.Id);
    }

    [Fact]
    public void Plan_WithReorderedCustoms_KeepsIdsAndAmendsOrders()
    {
        // Arrange
        var first = Custom("First", 0);
        var second = Custom("Second", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [first, second], [(null, "Second"), (null, "First")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        plan.ToUpdate.Should().HaveCount(2);
        plan.ToUpdate.Should().Contain(a => a.Row.Id == first.Id && a.DisplayOrder == 1);
        plan.ToUpdate.Should().Contain(a => a.Row.Id == second.Id && a.DisplayOrder == 0);
    }

    [Fact]
    public void Plan_WithSwappedStandard_RemovesOldAndAddsNew()
    {
        // Arrange
        var standard = Standard("kr_a", 0);

        // Act
        var plan = ExpectedResultSync.Plan([standard], [("kr_b", "Label for kr_b")], ActivityRef, Actor);

        // Assert
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().ContainSingle().Which.Id.Should().Be(standard.Id);
        var added = plan.ToAdd.Should().ContainSingle().Subject;
        added.KeyResultCode.Should().Be("kr_b");
        added.DisplayOrder.Should().Be(0);
    }

    [Fact]
    public void Plan_WithChangedStandardLabel_KeepsRowAndResnapshotsText()
    {
        // Arrange
        var standard = Standard("kr_a", 0, "Old label");

        // Act
        var plan = ExpectedResultSync.Plan([standard], [("kr_a", "New label")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        var amendment = plan.ToUpdate.Should().ContainSingle().Subject;
        amendment.Row.Id.Should().Be(standard.Id);
        amendment.Text.Should().Be("New label");
    }

    [Fact]
    public void Plan_WithDuplicateStandardRows_KeepsOneAndRemovesTheSurplus()
    {
        // Arrange — concurrent edits can plant duplicate rows for one code (no unique index); the plan must
        // keep working and clean the surplus up rather than throwing on every subsequent edit.
        var original = Standard("kr_a", 0);
        var duplicate = Standard("kr_a", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [original, duplicate], [("kr_a", "Label for kr_a")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().ContainSingle().Which.Id.Should().Be(duplicate.Id);
    }

    [Fact]
    public void Plan_WithDuplicateCustomTexts_PairsOneToOneInDisplayOrder()
    {
        // Arrange
        var firstDupe = Custom("Same", 0);
        var secondDupe = Custom("Same", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [firstDupe, secondDupe], [(null, "Same"), (null, "Same"), (null, "Same")], ActivityRef, Actor);

        // Assert
        plan.ToUpdate.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        var added = plan.ToAdd.Should().ContainSingle().Subject;
        added.DisplayOrder.Should().Be(2);
    }

    [Fact]
    public void Plan_WithRepositionedRewording_TreatsItAsRemoveAndAdd()
    {
        // Arrange — "Kept" moves to position 1 while the row at position 1 is reworded and moved to
        // position 0. The rewording changed position, so it must NOT inherit the old row's id: pairing
        // across positions is what would transplant a stale answer onto the wrong question.
        var kept = Custom("Kept", 0);
        var reworded = Custom("Old name", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [kept, reworded], [(null, "New name"), (null, "Kept")], ActivityRef, Actor);

        // Assert
        plan.ToRemove.Should().ContainSingle().Which.Id.Should().Be(reworded.Id);
        var added = plan.ToAdd.Should().ContainSingle().Subject;
        added.Text.Should().Be("New name");
        added.DisplayOrder.Should().Be(0);
        var amendment = plan.ToUpdate.Should().ContainSingle().Subject;
        amendment.Row.Id.Should().Be(kept.Id);
        amendment.DisplayOrder.Should().Be(1);
    }

    [Fact]
    public void Plan_WithInPlaceRewording_KeepsTheRowId()
    {
        // Arrange — rewording the first of two customs without moving it keeps its id (and its answer).
        var reworded = Custom("Old name", 0);
        var kept = Custom("Kept", 1);

        // Act
        var plan = ExpectedResultSync.Plan(
            [reworded, kept], [(null, "New name"), (null, "Kept")], ActivityRef, Actor);

        // Assert
        plan.ToAdd.Should().BeEmpty();
        plan.ToRemove.Should().BeEmpty();
        var amendment = plan.ToUpdate.Should().ContainSingle().Subject;
        amendment.Row.Id.Should().Be(reworded.Id);
        amendment.Text.Should().Be("New name");
    }
}
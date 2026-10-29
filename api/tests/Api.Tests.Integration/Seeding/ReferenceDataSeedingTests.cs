using Application.Common.Seeding;
using Application.Common.Settings;
using Domain.Categories;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Seeding;

/// <summary>
/// The product-owned reference data — activity categories and types, the key-result protocol, the data-collection
/// form modules — is seeded by inserting whatever the catalogue lists and the database lacks, matched by key, on
/// every boot (BLUM-8749). It used to be seed-if-empty on whole tables, so a deployed database could only gain a row
/// through a migration that deleted the table first. These pin the three things that make the keyed version safe on a
/// live database: a converged database is left exactly as it is, a missing row comes back at its catalogue position
/// without disturbing the rows around it, and a withdrawn (inactive) row never comes back.
///
/// Each case simulates an environment seeded by an older catalogue by deleting a row and closing the gap it leaves in
/// the ordering, which is how that database would look had the row never been in the catalogue.
/// </summary>
public class ReferenceDataSeedingTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory)
{
    private const string NeedsAssessmentType = "Creating a local needs assessment";

    private readonly IntegrationTestWebFactory _factory = factory;

    [Fact]
    public async Task ShouldChangeNothing_WhenTheDatabaseIsAlreadySeeded()
    {
        var before = await SnapshotAsync();

        await SeedAsync();

        var after = await SnapshotAsync();
        after.Categories.Should().Equal(before.Categories);
        after.Types.Should().Equal(before.Types);
        after.KeyResults.Should().Equal(before.KeyResults);
        after.Questions.Should().Equal(before.Questions);
        after.Modules.Should().Equal(before.Modules);
        after.FormQuestions.Should().Equal(before.FormQuestions);
    }

    [Fact]
    public async Task ShouldRestoreAMissingActivityType_AtItsCataloguePosition_WithoutReplacingTheOthers()
    {
        var category = await CategoryAsync(CategoryNames.PeopleAndRoles);
        var catalogue = Catalogue(CategoryNames.PeopleAndRoles).TaskNames;
        const int removedIndex = 2;
        var removed = catalogue[removedIndex];
        await Context.Database.ExecuteSqlAsync($"DELETE FROM activity_tasks WHERE category_id = {category.Id.Value} AND task_name = {removed}");
        await Context.Database.ExecuteSqlAsync(
            $"UPDATE activity_tasks SET sequence_order = sequence_order - 1 WHERE category_id = {category.Id.Value} AND sequence_order > {removedIndex + 1}");
        var survivors = await TypesAsync(category.Id);

        await SeedAsync();

        var types = await TypesAsync(category.Id);
        types.Select(t => t.TaskName).Should().Equal(catalogue);
        types.Select(t => t.SequenceOrder).Should().Equal(Enumerable.Range(1, catalogue.Count));
        types.Where(t => t.TaskName != removed).Select(t => t.Id).Should().BeEquivalentTo(survivors.Select(t => t.Id));
        types.Should().ContainSingle(t => t.TaskName == removed).Which.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRestoreAMissingCategory_AndItsTypes_AtItsCataloguePosition()
    {
        const string removedName = "Needs assessment";
        var removed = await CategoryAsync(removedName);
        var position = removed.SequenceOrder;
        await Context.Database.ExecuteSqlAsync($"DELETE FROM activity_tasks WHERE category_id = {removed.Id.Value}");
        await Context.Database.ExecuteSqlAsync($"DELETE FROM categories WHERE id = {removed.Id.Value}");
        await Context.Database.ExecuteSqlAsync($"UPDATE categories SET sequence_order = sequence_order - 1 WHERE sequence_order > {position}");
        var survivors = await Context.Categories.AsNoTracking().ToListAsync();

        await SeedAsync();

        var categories = await Context.Categories.AsNoTracking().OrderBy(c => c.SequenceOrder).ToListAsync();
        categories.Select(c => c.Name).Should().Equal(ActivityTaxonomySeedData.Categories.Select(c => c.Name));
        categories.Select(c => c.SequenceOrder).Should().Equal(Enumerable.Range(1, ActivityTaxonomySeedData.Categories.Count));
        categories.Where(c => c.Name != removedName).Select(c => c.Id).Should().BeEquivalentTo(survivors.Select(c => c.Id));

        var restored = categories.Single(c => c.Name == removedName);
        (await TypesAsync(restored.Id)).Select(t => t.TaskName).Should().Equal(Catalogue(removedName).TaskNames);

        // The key-result blocks resolve their type by name, so the restored types get their key results too.
        var restoredType = await Context.ActivityTasks.AsNoTracking().SingleAsync(t => t.CategoryId == restored.Id && t.TaskName == NeedsAssessmentType);
        (await Context.KeyResultQuestions.AsNoTracking().CountAsync(q => q.ActivityTaskId == restoredType.Id))
            .Should().Be(KeyResultProtocolSeedData.Protocol.Single(p => p.TaskName == NeedsAssessmentType).Questions.Count);
    }

    [Fact]
    public async Task ShouldRestoreAMissingKeyResultQuestion_AtItsProtocolPosition_WithoutReplacingTheOthers()
    {
        var task = await ActiveTypeAsync(NeedsAssessmentType);
        var block = KeyResultProtocolSeedData.Protocol.Single(p => p.TaskName == NeedsAssessmentType);
        const int removedIndex = 2;
        var removed = block.Questions[removedIndex];
        await Context.Database.ExecuteSqlAsync(
            $"DELETE FROM key_result_questions WHERE activity_task_id = {task.Id.Value} AND code = {removed.Code} AND status_scope = {removed.StatusScope}");
        await Context.Database.ExecuteSqlAsync(
            $"UPDATE key_result_questions SET display_order = display_order - 1 WHERE activity_task_id = {task.Id.Value} AND display_order > {removedIndex}");
        var survivors = await QuestionsAsync(task.Id);

        await SeedAsync();

        var questions = await QuestionsAsync(task.Id);
        questions.Select(q => (q.Code, q.StatusScope.ToString())).Should().Equal(block.Questions.Select(q => (q.Code, q.StatusScope)));
        questions.Select(q => q.DisplayOrder).Should().Equal(Enumerable.Range(0, block.Questions.Count));
        questions.Where(q => !(q.Code == removed.Code && q.StatusScope.ToString() == removed.StatusScope)).Select(q => q.Id)
            .Should().BeEquivalentTo(survivors.Select(q => q.Id));
    }

    [Fact]
    public async Task ShouldNotReinsertAWithdrawnRow()
    {
        const string withdrawnType = "Creating a workplace charter";
        var task = await ActiveTypeAsync(NeedsAssessmentType);
        var question = KeyResultProtocolSeedData.Protocol.Single(p => p.TaskName == NeedsAssessmentType).Questions[0];
        await Context.Database.ExecuteSqlAsync($"UPDATE activity_tasks SET is_active = FALSE WHERE task_name = {withdrawnType}");
        await Context.Database.ExecuteSqlAsync(
            $"UPDATE key_result_questions SET is_active = FALSE WHERE activity_task_id = {task.Id.Value} AND code = {question.Code} AND status_scope = {question.StatusScope}");

        try
        {
            await SeedAsync();

            (await Context.ActivityTasks.AsNoTracking().Where(t => t.TaskName == withdrawnType).ToListAsync())
                .Should().ContainSingle().Which.IsActive.Should().BeFalse();
            (await QuestionsAsync(task.Id)).Where(q => q.Code == question.Code && q.StatusScope.ToString() == question.StatusScope)
                .Should().ContainSingle().Which.IsActive.Should().BeFalse();
        }
        finally
        {
            await Context.Database.ExecuteSqlAsync($"UPDATE activity_tasks SET is_active = TRUE WHERE task_name = {withdrawnType}");
            await Context.Database.ExecuteSqlAsync($"UPDATE key_result_questions SET is_active = TRUE WHERE activity_task_id = {task.Id.Value}");
        }
    }

    [Fact]
    public async Task ShouldReportTheLockAsAFailedStep_WhenAnotherInstanceHoldsItTooLong()
    {
        // Another instance seeding the same database, holding the lock on its own connection.
        await using var otherInstance = _factory.Services.CreateAsyncScope();
        var otherContext = otherInstance.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await otherContext.Database.OpenConnectionAsync();
        await otherContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({ApplicationDbContextInitialiser.SeedingLockKey})");

        var settings = _factory.Services.GetRequiredService<ApplicationSettings>();
        var configuredWait = settings.Seeding.LockWaitSeconds;
        settings.Seeding.LockWaitSeconds = 1;
        try
        {
            // A blocking pg_advisory_lock would have thrown a command timeout here, which boot does not start past.
            var seed = SeedAsync;
            var thrown = await seed.Should().ThrowAsync<SeedingIncompleteException>();
            thrown.Which.FailedSteps.Should().Equal(ApplicationDbContextInitialiser.SeedingLockStep);
            thrown.Which.InnerExceptions.Should().ContainSingle().Which.Should().BeOfType<TimeoutException>();
        }
        finally
        {
            settings.Seeding.LockWaitSeconds = configuredWait;
            await otherContext.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({ApplicationDbContextInitialiser.SeedingLockKey})");
            await otherContext.Database.CloseConnectionAsync();
        }

        // And the lock this run never took is free: the next run seeds normally.
        await SeedAsync();
    }

    private static ActivityTaxonomySeedData.CategorySeed Catalogue(string name) =>
        ActivityTaxonomySeedData.Categories.Single(c => c.Name == name);

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().SeedDataAsync();
    }

    private Task<Category> CategoryAsync(string name) => Context.Categories.AsNoTracking().SingleAsync(c => c.Name == name);

    // The type under its current category: a case that restores a category leaves its old types' key results behind
    // on the deleted type's id, so look the type up by name among the active rows.
    private async Task<ActivityTask> ActiveTypeAsync(string name)
    {
        var categoryIds = await Context.Categories.AsNoTracking().Select(c => c.Id).ToListAsync();
        var types = await Context.ActivityTasks.AsNoTracking().Where(t => t.TaskName == name && t.IsActive).ToListAsync();
        return types.Single(t => categoryIds.Contains(t.CategoryId));
    }

    private Task<List<ActivityTask>> TypesAsync(CategoryId categoryId) =>
        Context.ActivityTasks.AsNoTracking().Where(t => t.CategoryId == categoryId).OrderBy(t => t.SequenceOrder).ToListAsync();

    private Task<List<Domain.KeyResults.KeyResultQuestion>> QuestionsAsync(ActivityTaskId taskId) =>
        Context.KeyResultQuestions.AsNoTracking().Where(q => q.ActivityTaskId == taskId).OrderBy(q => q.DisplayOrder).ToListAsync();

    private async Task<Snapshot> SnapshotAsync() => new(
        (await Context.Categories.AsNoTracking().ToListAsync())
            .Select(c => (c.Id.Value, c.Name, c.SequenceOrder, c.IsActive, c.UpdatedAt)).OrderBy(r => r.Value).Cast<object>().ToList(),
        (await Context.ActivityTasks.AsNoTracking().ToListAsync())
            .Select(t => (t.Id.Value, t.CategoryId.Value, t.TaskName, t.SequenceOrder, t.IsActive, t.UpdatedAt)).OrderBy(r => r.Item1).Cast<object>().ToList(),
        (await Context.KeyResultDefinitions.AsNoTracking().ToListAsync())
            .Select(d => (d.Id.Value, d.ActivityTaskId.Value, d.Code, d.Label, d.DisplayOrder, d.IsActive)).OrderBy(r => r.Item1).Cast<object>().ToList(),
        (await Context.KeyResultQuestions.AsNoTracking().ToListAsync())
            .Select(q => (q.Id.Value, q.ActivityTaskId.Value, q.Code, q.StatusScope.ToString(), q.Label, q.DisplayOrder, q.IsActive)).OrderBy(r => r.Item1).Cast<object>().ToList(),
        (await Context.FormModules.AsNoTracking().ToListAsync())
            .Select(m => (m.Id.Value, m.Code, m.Name, m.DisplayOrder)).OrderBy(r => r.Value).Cast<object>().ToList(),
        (await Context.FormQuestions.AsNoTracking().ToListAsync())
            .Select(q => (q.Id.Value, q.Code, q.Label, q.IsActive)).OrderBy(r => r.Value).Cast<object>().ToList());

    private sealed record Snapshot(
        List<object> Categories,
        List<object> Types,
        List<object> KeyResults,
        List<object> Questions,
        List<object> Modules,
        List<object> FormQuestions);
}
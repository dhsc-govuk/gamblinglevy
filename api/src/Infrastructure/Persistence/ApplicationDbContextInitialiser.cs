using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Seeding;
using Application.Common.Settings;
using Domain.Categories;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.KeyResults;
using Domain.Users;
using Domain.ValueObjects;
using Infrastructure.Persistence.Seed;
using Infrastructure.Persistence.Seed.Features;
using Infrastructure.Persistence.Seed.FormVersions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence;

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    IWebHostEnvironment hostingEnvironment,
    MaturityBaselineSeeder maturityBaselineSeeder,
    FormModuleVersionBackfill formModuleVersionBackfill,
    FormModuleVersionSync formModuleVersionSync,
    IFeatureFlags featureFlags,
    FeatureConvergenceRunner featureConvergenceRunner)
{
    // The Question Protocol's single free-text ceiling (13 August, BLUM-7620): every long-answer question on the
    // activity record and the quarterly report caps at 500 characters, rendered as a GDS character count.
    private const int FreeTextMaxLength = 500;

    // Serialises seeding between instances that share a database (BLUM-8749). Two instances booting together used to
    // pass the same "is it there yet?" checks and both insert. A unique index turned that into a crash-looping boot,
    // and where there is none (activity_tasks, dc_form_questions) it would silently have made duplicates. Any
    // constant works as long as every instance uses the same one. Advisory locks belong to one database, so
    // integration test classes, each on their own database, never wait on one another. Public so a test can hold it.
    public const long SeedingLockKey = 8749;

    // What a run that could not start at all (no connection, or the lock not free in time) is reported as, in place
    // of the step names.
    public const string SeedingLockStep = "SeedingLock";

    // The questions SeedStrategicFeedbackModuleAsync creates, which BLUM_7637 makes optional.
    private static readonly string[] SeededStrategicFeedbackCodes = ["SF_OTHER_PREVENTION", "SF_CHALLENGES", "SF_DHSC_SUPPORT", "SF_LEARNINGS"];

    public async Task InitialiseAsync()
    {
        try
        {
            await dbContext.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    /// <summary>
    /// Brings the reference data up to what this build ships, on every boot and every test-data reset. Every step
    /// runs even when an earlier one failed, and the run then throws <see cref="SeedingIncompleteException"/>
    /// naming the failed steps (see <see cref="SeedStepRunner"/>). Boot logs that and starts anyway; a migration
    /// failure, by contrast, still stops it.
    /// </summary>
    public Task SeedDataAsync() => RunSeedingAsync(
        [
            new(nameof(SeedAdminUser), SeedAdminUser),
            new(nameof(SeedActivityTaxonomyAsync), SeedActivityTaxonomyAsync),
            new(nameof(SeedFormDesigner), SeedFormDesigner),
            new(nameof(SeedDataCollectionFormModulesAsync), SeedDataCollectionFormModulesAsync),
            new(nameof(SeedKeyResultProtocolAsync), SeedKeyResultProtocolAsync),
            new(nameof(SeedQuarterlyReportModuleAsync), SeedQuarterlyReportModuleAsync),

            // It reads organisations, which the bulk LA import creates at runtime, and converges on every boot.
            new(nameof(MaturityBaselineSeeder), () => maturityBaselineSeeder.SeedAsync()),

            // Last, so a flagged data change is applied over (or reverted to) the baseline the seeders above just
            // wrote. Inside SeedDataAsync rather than a separate boot step so the test-data reset, which re-runs this
            // method, converges too.
            .. FeatureFlagSteps(),

            // After the flag steps, so version 1 holds the strategic-feedback questions as BLUM_7637 converged them.
            // Not itself flag-dependent: it only fills gaps (BLUM-9103), so it is not part of ApplyFeatureFlagsAsync.
            new(nameof(FormModuleVersionBackfill), () => formModuleVersionBackfill.RunAsync()),
        ]);

    /// <summary>
    /// Everything whose data depends on a feature flag: the seeders that read IFeatureFlags themselves, then every
    /// IFeatureConvergence. A seeder that starts reading a flag moves here from SeedDataAsync (it must not depend on
    /// anything seeded after it), so the tests' SetFeatureAsync can re-apply a flag without re-running every seeder.
    /// </summary>
    public Task ApplyFeatureFlagsAsync() => RunSeedingAsync(FeatureFlagSteps());

    private SeedStep[] FeatureFlagSteps() =>
    [
        // Reads Feature.BLUM_7637 and BLUM_9087. Needs only the admin user and the strategic_feedback module, both seeded above.
        new(nameof(SeedStrategicFeedbackModuleAsync), SeedStrategicFeedbackModuleAsync),
        new(nameof(FeatureConvergenceRunner), () => featureConvergenceRunner.RunAsync()),
        // BLUM-9105 (reads BLUM_9087): saves a new version of any published form whose questions a seeder above, a
        // convergence or a migration changed in this release. Last, so it sees every change.
        new(nameof(FormModuleVersionSync), () => formModuleVersionSync.RunAsync()),
    ];

    // Runs the steps under the seeding lock. The only exception that may leave here is SeedingIncompleteException,
    // because that is the one boot logs and starts past (Api.Modules.DbModule). So everything around the steps is
    // turned into it or swallowed. A connection that won't open, or a lock that isn't free in time, is reported as the
    // SeedingLock step with nothing else run. Releasing the lock never throws.
    private async Task RunSeedingAsync(IReadOnlyList<SeedStep> steps)
    {
        try
        {
            await dbContext.Database.OpenConnectionAsync();
            await AcquireSeedingLockAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Seeding could not start, so no seeding step ran.");
            await ReleaseSeedingConnectionAsync(holdsLock: false);
            throw new SeedingIncompleteException([SeedingLockStep], [ex]);
        }

        try
        {
            await SeedStepRunner.RunAsync(steps, dbContext.ChangeTracker.Clear, logger);
        }
        finally
        {
            await ReleaseSeedingConnectionAsync(holdsLock: true);
        }
    }

    // Polls pg_try_advisory_lock, which answers at once, instead of blocking in pg_advisory_lock. A blocking call would
    // run into the 30-second command timeout whenever another instance seeds for longer than that. The wait is bounded
    // by Seeding:LockWaitSeconds instead. Running out of it only means this instance skips seeding while the other
    // instance does it.
    private async Task AcquireSeedingLockAsync()
    {
        for (var waited = 0; ; waited++)
        {
            if (await RunSeedingLockFunctionAsync("SELECT pg_try_advisory_lock(@key)"))
            {
                return;
            }

            if (waited >= settings.Seeding.LockWaitSeconds)
            {
                throw new TimeoutException($"Another instance has held the seeding lock for over {settings.Seeding.LockWaitSeconds} second(s).");
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    // This runs in a finally block, where an exception would replace the run's own outcome, so it never throws. A
    // failed unlock almost always means the connection has gone, and the session-level lock went with it. Otherwise
    // the lock is released when Npgsql's pool resets or retires that connection.
    private async Task ReleaseSeedingConnectionAsync(bool holdsLock)
    {
        if (holdsLock)
        {
            try
            {
                await RunSeedingLockFunctionAsync("SELECT pg_advisory_unlock(@key)");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Releasing the seeding lock failed.");
            }
        }

        try
        {
            await dbContext.Database.CloseConnectionAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Closing the seeding connection failed.");
        }
    }

    // On the connection RunSeedingAsync holds open for the whole run. The lock belongs to a session, and EF would
    // otherwise close the connection after each command and drop the lock with it.
    private async Task<bool> RunSeedingLockFunctionAsync(string sql)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter("key", SeedingLockKey));
        return await command.ExecuteScalarAsync() is true;
    }

    private async Task SeedAdminUser()
    {
        var email = settings.DefaultAdminUser.Email;
        var subId = new SubId(Guid.Parse(settings.DefaultAdminUser.SubId));

        var adminExists = await dbContext.Users
            .Where(u => u.SubId == subId)
            .AnyAsync();

        if (!adminExists)
        {
            var user = User.New(
                new UserId(Guid.NewGuid()),
                new Name("gambling-levy", "administrator"),
                email,
                subId,
                UserStatus.Active,
                UserRole.Admin,
                dateTimeProvider.GetUtcNow());
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
    }

    // Seeds the activity taxonomy from ActivityTaxonomySeedData: the activity categories and the "activity type"
    // options shown under each. The "Other ..." type lets the user enter a free-text name (Activity.CustomTaskText).
    //
    // Inserts whatever the catalogue lists and the database lacks, matching a category by name and a type by name
    // within its category, so adding a type is a catalogue edit with no migration (BLUM-8749). A new category also
    // needs its form sections; the catalogue explains why. It used to be
    // seed-if-empty on the whole categories table, which left no way to add a row except a migration deleting
    // categories, activity_tasks and everything that refers to them (BLUM-6933), or a seeder of its own (BLUM-7860's
    // Joint partnership, now folded in here). Nothing in the app writes these tables, so inserting a missing row
    // cannot fight anyone's edit. An inactive row counts as present, so a withdrawn type never comes back.
    //
    // It never changes or deletes an existing row's name; see the catalogue for how a rename ships. It must stay
    // ahead of SeedFormDesigner and SeedKeyResultProtocolAsync, which resolve categories and types by name.
    private async Task SeedActivityTaxonomyAsync()
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
        if (admin is null)
        {
            logger.LogWarning("Activity taxonomy seed: no user has the Admin role; skipping the categories and activity types.");
            return;
        }

        var actor = admin.Id;
        var utcNow = dateTimeProvider.GetUtcNow();

        // Tracked: a category that gains a row has its catalogued rows renumbered.
        var categories = await dbContext.Categories.ToListAsync();
        var tasks = await dbContext.ActivityTasks.ToListAsync();
        var catalogue = ActivityTaxonomySeedData.Categories;
        var insertedCategories = 0;
        var insertedTypes = 0;

        for (var categoryIndex = 0; categoryIndex < catalogue.Count; categoryIndex++)
        {
            var seed = catalogue[categoryIndex];
            var category = categories.FirstOrDefault(c => c.Name == seed.Name);
            if (category is null)
            {
                category = Category.Seed(CategoryId.New(), seed.Name, categoryIndex + 1, actor, utcNow);
                dbContext.Categories.Add(category);
                categories.Add(category);
                insertedCategories++;
            }

            var types = tasks.Where(t => t.CategoryId == category.Id).ToList();
            var typesBefore = types.Count;
            for (var typeIndex = 0; typeIndex < seed.TaskNames.Count; typeIndex++)
            {
                var name = seed.TaskNames[typeIndex];
                if (types.Any(t => t.TaskName == name))
                {
                    continue;
                }

                var type = ActivityTask.Seed(ActivityTaskId.New(), category.Id, name, typeIndex + 1, actor, utcNow);
                dbContext.ActivityTasks.Add(type);
                types.Add(type);
            }

            if (types.Count > typesBefore)
            {
                insertedTypes += types.Count - typesBefore;
                PutInCatalogueOrder(types, seed.TaskNames, t => t.TaskName, t => t.SequenceOrder, (t, order) => t.SetSequenceOrder(order, utcNow), firstPosition: 1);
            }
        }

        if (insertedCategories > 0)
        {
            PutInCatalogueOrder(categories, catalogue.Select(c => c.Name).ToList(), c => c.Name, c => c.SequenceOrder, (c, order) => c.SetSequenceOrder(order, utcNow), firstPosition: 1);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        if (insertedCategories + insertedTypes > 0)
        {
            logger.LogInformation(
                "Activity taxonomy seed: inserted {CategoryCount} category(ies) and {TypeCount} activity type(s).",
                insertedCategories,
                insertedTypes);
        }
    }

    // Moves every row the catalogue lists to the catalogue's position for it, counting from firstPosition. Called only
    // for a parent the seeder has just inserted into, so a row added mid-list doesn't tie with the row already at its
    // position, and a parent it did not touch gets no UPDATE at all. Rows the catalogue no longer lists (withdrawn
    // ones) keep their order. Only product-owned rows come through here: nothing else writes their order.
    private static void PutInCatalogueOrder<TRow, TKey>(
        IEnumerable<TRow> rows,
        IReadOnlyList<TKey> catalogue,
        Func<TRow, TKey> keyOf,
        Func<TRow, int> orderOf,
        Action<TRow, int> setOrder,
        int firstPosition)
        where TKey : notnull
    {
        var positions = catalogue.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index + firstPosition);
        foreach (var row in rows)
        {
            if (positions.TryGetValue(keyOf(row), out var position) && orderOf(row) != position)
            {
                setOrder(row, position);
            }
        }
    }

    // Seeds the category-scoped activity-record form schema. Detail questions render after the structural type;
    // Additional information renders after status and immediately before Check your answers. Codes are globally
    // unique and prefixed per category. The structural steps (category, type, status, name) and free-text
    // expected results are handled outside the schema.
    //
    // Deliberately seed-if-empty: the canonical schema is delivered ONCE, after which DHSC admins own it through
    // the Form Designer — this seeder must never overwrite their edits, so it does not touch a non-empty schema
    // (BLUM-7081). So an edit here only reaches a fresh database. Deployed databases need a migration as well.
    //
    // The service is live, so that migration must only UPDATE rows: guard each statement on the value it replaces,
    // keep every id, and write a Down that reverses it. To withdraw a question, set form_questions.is_active = false,
    // because every LA-facing read uses the active schema (BLUM-8340, BLUM-8156).
    //
    // Pre-live tickets (BLUM-7759, 7871, 8090) did it differently: they cleared form_question_options/form_questions/
    // form_sections so this guard would rebuild the schema. Do not do that again. It re-mints every question id and
    // destroys whatever DHSC admins have authored in the Form Designer.
    private async Task SeedFormDesigner()
    {
        try
        {
            if (await dbContext.FormSections.AnyAsync())
            {
                return;
            }

            var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
            if (admin is null)
            {
                logger.LogWarning("Form designer seed: no user has the Admin role; skipping the activity-record form schema.");
                return;
            }

            var actor = admin.Id;
            var categories = await dbContext.Categories.ToListAsync();

            Category? FindCategory(string name) => categories.FirstOrDefault(c => c.Name == name);

            // People and roles — bespoke "Role details" questions (Figma 138-6504). Skip (rather than crash
            // startup) if the category is missing — e.g. reference data drift on an already-seeded environment.
            //
            // Two questions, not three: "Is the role shared with other local authorities?" (PR_ROLE_SHARED, order
            // 2) was withdrawn by BLUM-7871. Joint partnership is now its own activity category (BLUM-7860), so a
            // role funded across authorities is recorded as a partnership record rather than as a flag on a
            // People and roles one, and the question had no reader anywhere — no export, no report prefill, no
            // key result. Its sibling on the Activity details screen, {ST,LIC,EDU,HELP}_SHARED, has since been
            // withdrawn too (BLUM-8340), so no shared-authority question is left anywhere in the schema.
            var peopleAndRoles = FindCategory("People and roles");
            if (peopleAndRoles is null)
            {
                logger.LogWarning("Form designer seed: category 'People and roles' not found; skipping its 'Role details' section.");
            }
            else
            {
                var roleSection = FormSection.Create(FormSectionId.New(), "Role details", 0, actor, categoryId: peopleAndRoles.Id, utcNow: dateTimeProvider.GetUtcNow());
                dbContext.FormSections.Add(roleSection);

                var fundingTime = NewQuestion(roleSection, "PR_FUNDING_TIME", "How much of the staff member's time is the funding paying for, or will it pay for?", QuestionType.Radio, 0, actor, isRequired: true);
                AddOptions(fundingTime, new[]
                {
                    ("0", "0%"),
                    ("up_to_20", "Up to 20%"),
                    ("21_40", "21 to 40%"),
                    ("41_60", "41 to 60%"),
                    ("61_80", "61 to 80%"),
                    ("81_100", "81 to 100%"),
                });

                var roleDuration = NewQuestion(roleSection, "PR_ROLE_DURATION", "How long will the role last?", QuestionType.Radio, 1, actor, isRequired: true);
                AddOptions(roleDuration, new[]
                {
                    ("lt_6", "Less than 6 months"),
                    ("6_12", "6 to 12 months"),
                    ("gt_12", "More than 12 months"),
                    ("no_end", "No fixed end date"),
                });

                dbContext.FormQuestions.AddRange(fundingTime, roleDuration);
            }

            // Every non-People category asks the Activity details questions (protocol 1.BCDEF.1.1-1.3) — Education
            // and Help included (BLUM-7120). Education and Help ADDITIONALLY get the People and issues screen
            // below, on every one of their types (BLUM-7778).
            //
            // 1.BCDEF.1.4, "Was this activity shared with other local authorities, or will it be shared?", is no
            // longer asked by any category. BLUM-7759 dropped it for Needs assessment, and BLUM-8340 withdrew it
            // from the other four. Deployed databases still hold {ST,LIC,EDU,HELP}_SHARED, deactivated rather than
            // deleted (migration BLUM_8340_RemoveActivitySharedQuestion), so their saved answers survive.
            var genericDetails = new[]
            {
                (Name: "Needs assessment", Prefix: "NA"),
                (Name: "Strategy and leadership", Prefix: "ST"),
                (Name: "Licensing, planning and policy", Prefix: "LIC"),
                (Name: "Education, campaigns and outreach", Prefix: "EDU"),
                (Name: "Help, advice and access to services", Prefix: "HELP"),
            };
            foreach (var (name, prefix) in genericDetails)
            {
                var category = FindCategory(name);
                if (category is null)
                {
                    logger.LogWarning("Form designer seed: category '{CategoryName}' not found; skipping its 'Activity details' section.", name);
                    continue;
                }

                SeedActivityDetailsSection(actor, category, prefix);
            }

            var peopleAndIssues = new[]
            {
                (Name: "Education, campaigns and outreach", Prefix: "EDU"),
                (Name: "Help, advice and access to services", Prefix: "HELP"),
            };
            foreach (var (name, prefix) in peopleAndIssues)
            {
                var category = FindCategory(name);
                if (category is null)
                {
                    logger.LogWarning("Form designer seed: category '{CategoryName}' not found; skipping its 'People and issues' section.", name);
                    continue;
                }

                SeedPeopleAndIssuesSection(actor, category, prefix);
            }

            // Every category except People and roles asks for a plan on its own final Additional information
            // screen, immediately before Check your answers in the client flow. Education and Help place it at
            // order 2 because their People and issues section occupies order 1.
            var additionalInformation = new[]
            {
                (Name: "Needs assessment", Prefix: "NA", DisplayOrder: 1),
                (Name: "Strategy and leadership", Prefix: "ST", DisplayOrder: 1),
                (Name: "Licensing, planning and policy", Prefix: "LIC", DisplayOrder: 1),
                (Name: "Education, campaigns and outreach", Prefix: "EDU", DisplayOrder: 2),
                (Name: "Help, advice and access to services", Prefix: "HELP", DisplayOrder: 2),
            };
            foreach (var (name, prefix, displayOrder) in additionalInformation)
            {
                var category = FindCategory(name);
                if (category is null)
                {
                    logger.LogWarning("Form designer seed: category '{CategoryName}' not found; skipping its 'Additional information' section.", name);
                    continue;
                }

                SeedAdditionalInformationSection(actor, category, prefix, displayOrder);
            }

            // Joint partnership (BLUM-7860) shares none of the three loops above: its questions are bespoke, it
            // asks no "who did you work with" or shared-authority question, and it has no Additional information
            // screen — so its whole flow is the one section below (BLUM-8090 removed the name field too).
            var jointPartnership = FindCategory(CategoryNames.JointPartnership);
            if (jointPartnership is null)
            {
                logger.LogWarning("Form designer seed: category '{CategoryName}' not found; skipping its section.", CategoryNames.JointPartnership);
            }
            else
            {
                SeedJointPartnershipSection(actor, jointPartnership);
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two instances can boot together against the one empty schema and both pass the guard;
            // form_questions.code is uniquely indexed, so the loser fails here. The seeding lock now keeps that from
            // happening between two builds that take it, but not against an instance of an older build that is
            // still running during a deploy. The failed inserts are still tracked as Added on this shared context
            // and would be re-flushed (and re-fail) by the next seeder's SaveChangesAsync, so drop them before
            // deciding anything else.
            dbContext.ChangeTracker.Clear();

            // Distinguish losing the race from a self-inflicted duplicate in the seed content itself. Postgres
            // only raises 23505 after the conflicting transaction has committed, so if another instance won, its
            // schema is visible by now — benign, continue. If the tables are still empty, the collision was our
            // own rows (e.g. a duplicated question code): the rollback left the schema permanently empty and the
            // guard will fail identically on every boot, so that must stay a failed step.
            if (await dbContext.FormSections.AsNoTracking().AnyAsync())
            {
                logger.LogWarning(ex, "Form designer seed: concurrent seed detected; another instance already applied the schema.");
            }
            else
            {
                logger.LogError(ex, "Form designer seed: unique violation but no schema was written — duplicate code inside the seed content?");
                throw;
            }
        }
    }

    // form_questions.code is uniquely indexed, so a concurrent seed surfaces as a 23505 unique violation.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Seeds the three built-in data-collection form modules (the reusable forms-builder modules DHSC admins assign
    // to collections). Inserts a module whose code the database lacks and touches nothing else (BLUM-8749): once a
    // module exists DHSC admins own its name, order and questions, and deleting one archives it, so an archived
    // module's row is still there and is never re-added.
    private async Task SeedDataCollectionFormModulesAsync()
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
        if (admin is null)
        {
            return;
        }

        var modules = new (string Code, string Name)[]
        {
            ("activity_key_results", "Enter activity results"),
            ("funding_spent", "Record funding spent"),
            ("strategic_feedback", "Provide strategic feedback"),
        };

        var existing = (await dbContext.FormModules.AsNoTracking().Select(m => m.Code).ToListAsync()).ToHashSet();
        var utcNow = dateTimeProvider.GetUtcNow();
        var inserted = 0;
        for (var index = 0; index < modules.Length; index++)
        {
            var (code, name) = modules[index];
            if (existing.Contains(code))
            {
                continue;
            }

            dbContext.FormModules.Add(FormModule.Create(FormModuleId.New(), code, name, index + 1, admin.Id, utcNow));
            inserted++;
        }

        if (inserted == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Data-collection form module seed: inserted {ModuleCount} module(s).", inserted);
    }

    // Seeds the Question Protocol's standard key results + quarterly-report question bank (BLUM-6611), transcribed
    // in KeyResultProtocolSeedData. Blocks resolve to ActivityTasks by name: a missing task is logged and skipped
    // (never crash boot — reference-data drift on an already-seeded environment must not crash-loop startup).
    //
    // Inserts each key result the database lacks by (type, code) and each question by (type, code, status scope),
    // so adding one is a catalogue edit with no migration (BLUM-8749). It used to be seed-if-empty on the whole
    // question table, which is why ten migrations up to BLUM-8159 emptied both tables to deliver a change. It never
    // changes or deletes an existing row, because saved answers and chosen outcomes point at these codes: wording
    // ships as a guarded UPDATE migration (BLUM-8156), and so does withdrawing a row (is_active = false). An
    // inactive row counts as present. Nothing else writes these system-owned rows, so a type that gains one has its
    // catalogued rows put back in protocol order.
    private async Task SeedKeyResultProtocolAsync()
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
        if (admin is null)
        {
            return;
        }

        var actor = admin.Id;
        var utcNow = dateTimeProvider.GetUtcNow();
        var tasks = await dbContext.ActivityTasks.AsNoTracking().ToListAsync();

        var definitionKeys = (await dbContext.KeyResultDefinitions.AsNoTracking()
                .Select(d => new { d.ActivityTaskId, d.Code })
                .ToListAsync())
            .Select(d => (TaskId: d.ActivityTaskId.Value, d.Code))
            .ToHashSet();
        var questionKeys = (await dbContext.KeyResultQuestions.AsNoTracking()
                .Select(q => new { q.ActivityTaskId, q.Code, q.StatusScope })
                .ToListAsync())
            .Select(q => (TaskId: q.ActivityTaskId.Value, q.Code, StatusScope: q.StatusScope.ToString()))
            .ToHashSet();

        var insertedDefinitions = 0;
        var insertedQuestions = 0;

        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var task = tasks.FirstOrDefault(t => t.TaskName == type.TaskName);
            if (task is null)
            {
                logger.LogWarning("Key-result protocol seed: activity type '{TaskName}' not found; skipping its block.", type.TaskName);
                continue;
            }

            var newDefinitions = 0;
            for (var order = 0; order < type.KeyResults.Count; order++)
            {
                var keyResult = type.KeyResults[order];
                if (definitionKeys.Contains((task.Id.Value, keyResult.Code)))
                {
                    continue;
                }

                dbContext.KeyResultDefinitions.Add(KeyResultDefinition.Seed(
                    KeyResultDefinitionId.New(), task.Id, keyResult.Code, keyResult.Label, order, actor, utcNow));
                newDefinitions++;
            }

            var newQuestions = 0;
            for (var order = 0; order < type.Questions.Count; order++)
            {
                var question = type.Questions[order];
                if (questionKeys.Contains((task.Id.Value, question.Code, question.StatusScope)))
                {
                    continue;
                }

                dbContext.KeyResultQuestions.Add(KeyResultQuestion.Seed(
                    KeyResultQuestionId.New(),
                    task.Id,
                    question.KeyResultCode,
                    question.Code,
                    QuestionStatusScope.From(question.StatusScope),
                    question.Label,
                    ProtocolFieldType.From(question.FieldType),
                    order,
                    actor,
                    hint: question.Hint,
                    minValue: question.Min,
                    maxValue: question.Max,
                    options: question.Options,
                    prefillSource: question.PrefillSource,
                    conditionalQuestionCode: question.ConditionalQuestionCode,
                    conditionalValue: question.ConditionalValue,
                    errorMessage: question.ErrorMessage,
                    utcNow: utcNow));
                newQuestions++;
            }

            // Renumber only when the type already held some of its catalogued rows; on a fresh type every row is new
            // and already in place. The rows loaded here are the ones already in the database, tracked.
            if (newDefinitions > 0 && newDefinitions < type.KeyResults.Count)
            {
                var definitions = await dbContext.KeyResultDefinitions.Where(d => d.ActivityTaskId == task.Id).ToListAsync();
                PutInCatalogueOrder(definitions, type.KeyResults.Select(k => k.Code).ToList(), d => d.Code, d => d.DisplayOrder, (d, order) => d.SetDisplayOrder(order), firstPosition: 0);
            }

            if (newQuestions > 0 && newQuestions < type.Questions.Count)
            {
                var questions = await dbContext.KeyResultQuestions.Where(q => q.ActivityTaskId == task.Id).ToListAsync();
                PutInCatalogueOrder(
                    questions,
                    type.Questions.Select(q => (q.Code, q.StatusScope)).ToList(),
                    q => (q.Code, q.StatusScope.ToString()),
                    q => q.DisplayOrder,
                    (q, order) => q.SetDisplayOrder(order),
                    firstPosition: 0);
            }

            insertedDefinitions += newDefinitions;
            insertedQuestions += newQuestions;
        }

        if (insertedDefinitions + insertedQuestions == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation(
            "Key-result protocol seed: inserted {DefinitionCount} key result(s) and {QuestionCount} question(s).",
            insertedDefinitions,
            insertedQuestions);
    }

    // Adds the three unconditional "Activity delivery" questions to the activity_key_results module and publishes
    // it so it can be assigned to collections. Deliberately NOT guarded by an aggregate-level AnyAsync: deployed
    // environments already hold the module (Draft, empty) from SeedDataCollectionFormModulesAsync's earlier run,
    // so this seeder converges at row level instead — it no-ops once the module is published.
    private async Task SeedQuarterlyReportModuleAsync()
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
        if (admin is null)
        {
            return;
        }

        // Tracked load with children — adding questions through the aggregate requires the change tracker to
        // see the owned collection (the owned-collection gotcha).
        var module = await dbContext.FormModules
            .Include(m => m.Questions)
            .FirstOrDefaultAsync(m => m.Code == "activity_key_results");
        if (module is null)
        {
            logger.LogWarning("Quarterly report module seed: module 'activity_key_results' not found; skipping.");
            return;
        }

        // "Ever published", not "is Published": a module reopened as a draft (BLUM-9102) keeps PublishedAt and must not be
        // rebuilt by this seeder.
        if (module.PublishedAt is not null)
        {
            return;
        }

        var actor = admin.Id;

        // The over-length message names the field the way the protocol's error column does ("Description of
        // progress"), which is not how the question asks it ("What progress have you made?") — hence its own
        // column rather than a template over Label.
        var deliveryQuestions = new (string Code, string Label, bool IsRequired, string Hint, string? ErrorMessage, string MaxLengthErrorMessage)[]
        {
            ("AKR_DELIVERY_PROGRESS", "What progress have you made?", true,
                "Describe the milestones reached this quarter, but do not include financial details",
                "Enter a description of your progress",
                "Description of progress must be 500 characters or less"),
            ("AKR_DELIVERY_CHALLENGES", "What challenges have you encountered? (optional)", false,
                "Describe any issues that have slowed down your progress, for example recruitment or partnership changes",
                null,
                "Description of challenges must be 500 characters or less"),
            ("AKR_DELIVERY_NEXT_QUARTER", "What are you planning to do next quarter? (optional)", false,
                "Describe your priorities, next steps and any changes you plan to make",
                null,
                "Description of plans must be 500 characters or less"),
        };

        var order = module.Questions.Count;
        foreach (var (code, label, isRequired, hint, errorMessage, maxLengthErrorMessage) in deliveryQuestions)
        {
            if (module.Questions.Any(q => q.Code == code))
            {
                continue;
            }

            module.AddQuestion(
                DcFormQuestionId.New(),
                code,
                label,
                QuestionType.Textarea,
                order++,
                actor,
                isRequired: isRequired,
                hint: hint,
                errorMessage: errorMessage,
                maxLength: FreeTextMaxLength,
                maxLengthErrorMessage: maxLengthErrorMessage,
                utcNow: dateTimeProvider.GetUtcNow());
        }

        module.Publish(actor, dateTimeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    // Adds the two pages ("Wider progress and challenges", "Working together") and their strategic-feedback questions to
    // the strategic_feedback module and publishes it so it is a usable, locked, assignable schema (BLUM-6613). Modeled
    // on SeedQuarterlyReportModuleAsync: NOT guarded by an aggregate-level AnyAsync — deployed environments already
    // hold the module from SeedDataCollectionFormModulesAsync, so this converges at row level and reconciles the
    // product-owned schema on published installations. Sections are owned, so the tracked load includes them too.
    //
    // Behind the BLUM-7637 flag (Feature.BLUM_7637, restoring BLUM-6427's intent), all four questions are optional —
    // an LA can leave both pages blank and still complete the section — and their labels carry " (optional)". BLUM-7637
    // first left page 2 ("Working together") required; BLUM-9092 brought it under the flag too, as BLUM-6427 asks.
    // This seeder reads the flag itself rather than through an IFeatureConvergence because it reconciles these rows
    // on every boot and would undo one. That reconcile is also
    // what makes the flag converge in both directions on the next restart or data reset with no migration: every
    // read of IsRequired and the label (the answer validator, the GET, the client) is live, never snapshotted.
    // Retiring the flag means hard-coding the optional values below.
    //
    // The reconcile runs only while BLUM_9087 is off (BLUM-9105). With it on, DHSC owns these questions once the module
    // has been published, so BLUM_7637 reaches only a fresh database. Switching BLUM_9087 off again hands the rows back
    // to this seeder, which resets DHSC's edits on the next boot.
    private async Task SeedStrategicFeedbackModuleAsync()
    {
        var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
        if (admin is null)
        {
            return;
        }

        var module = await dbContext.FormModules
            .Include(m => m.Sections)
            .Include(m => m.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(m => m.Code == "strategic_feedback");
        if (module is null)
        {
            logger.LogWarning("Strategic feedback module seed: module 'strategic_feedback' not found; skipping.");
            return;
        }

        // BLUM-9105: with form versioning on, a module that has been published belongs to DHSC, who edit it in the forms
        // builder, and our releases no longer change it. That covers a reopened draft (editing it here would overwrite
        // their unpublished changes) and a published module: its questions, options, protocol copy and sections, since
        // re-adding a page DHSC removed would change their form too. FormModuleVersionSync would save any such change as
        // a release version. A module never published (a fresh database) is still seeded and published below.
        if (featureFlags.IsEnabled(Feature.BLUM_9087) && module.PublishedAt is not null)
        {
            logger.LogInformation(
                "Strategic feedback module seed: DHSC owns the {ModuleState} module; leaving it alone.",
                module.Status.IsPublished ? "published" : "reopened draft");
            WarnWhereBlum7637NoLongerApplies(module);
            return;
        }

        var actor = admin.Id;

        var widerProgress = module.Sections.FirstOrDefault(s => s.SectionNumber == 1)
            ?? module.AddSection(DcFormSectionId.New(), 1, "Wider progress and challenges", actor, dateTimeProvider.GetUtcNow());
        var workingTogether = module.Sections.FirstOrDefault(s => s.SectionNumber == 2)
            ?? module.AddSection(DcFormSectionId.New(), 2, "Working together", actor, dateTimeProvider.GetUtcNow());

        var feedbackOptional = featureFlags.IsEnabled(Feature.BLUM_7637);
        var optionalLabelSuffix = feedbackOptional ? " (optional)" : string.Empty;

        var feedbackQuestions = new[]
        {
            new
            {
                Code = "SF_OTHER_PREVENTION",
                Label = "What other gambling harms prevention work have you focused on this quarter?" + optionalLabelSuffix,
                Hint = "Tell us about any work not included in the activities you’ve entered (levy funded or non funded)",
                Type = QuestionType.Textarea,
                SectionId = widerProgress.Id,
                IsRequired = !feedbackOptional,
                Options = Array.Empty<(string Value, string Label)>(),
                ErrorMessage = (string?)null,
                MaxLength = (int?)FreeTextMaxLength,
                MaxLengthErrorMessage = (string?)"Description of other gambling harms prevention work must be 500 characters or less",
            },
            new
            {
                Code = "SF_CHALLENGES",
                Label = "What challenges have you encountered?" + optionalLabelSuffix,
                Hint = "Describe any challenges you've faced, for example engagement, resources or competing priorities",
                Type = QuestionType.Textarea,
                SectionId = widerProgress.Id,
                IsRequired = !feedbackOptional,
                Options = Array.Empty<(string Value, string Label)>(),
                ErrorMessage = (string?)null,
                MaxLength = (int?)FreeTextMaxLength,
                MaxLengthErrorMessage = (string?)"Description of challenges must be 500 characters or less",
            },
            new
            {
                Code = "SF_DHSC_SUPPORT",
                Label = "Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?" + optionalLabelSuffix,
                Hint = "For example, things that could be improved or gaps you’ve identified",
                Type = QuestionType.Textarea,
                SectionId = workingTogether.Id,
                IsRequired = !feedbackOptional,
                Options = Array.Empty<(string Value, string Label)>(),
                ErrorMessage = (string?)null,
                MaxLength = (int?)FreeTextMaxLength,
                MaxLengthErrorMessage = (string?)"Feedback must be 500 characters or less",
            },

            // The 13 August protocol marks this one "Checkboxes", but it is a yes/no: a checkbox group cannot
            // express "one of two", and GDS reserves checkboxes for multi-select. Kept as radios and its blue
            // error message taken as written (BLUM-7620) — flagged back to DHSC.
            new
            {
                Code = "SF_LEARNINGS",
                Label = "Do you have any learnings you’d like to share with other local authorities?" + optionalLabelSuffix,
                Hint = "If yes, we’ll contact you to find out more",
                Type = QuestionType.Radio,
                SectionId = workingTogether.Id,
                IsRequired = !feedbackOptional,
                Options = new[] { (Value: "yes", Label: "Yes"), (Value: "no", Label: "No") },
                ErrorMessage = (string?)"Select yes if you have learnings to share",
                MaxLength = (int?)null,
                MaxLengthErrorMessage = (string?)null,
            },
        };

        var order = module.Questions.Count;
        foreach (var definition in feedbackQuestions)
        {
            var question = module.Questions.FirstOrDefault(q => q.Code == definition.Code);
            if (question is null)
            {
                if (module.PublishedAt is not null)
                {
                    logger.LogWarning(
                        "Strategic feedback module seed: published module is missing question '{QuestionCode}'; skipping it.",
                        definition.Code);
                    continue;
                }

                question = module.AddQuestion(
                    DcFormQuestionId.New(),
                    definition.Code,
                    definition.Label,
                    definition.Type,
                    order++,
                    actor,
                    sectionId: definition.SectionId,
                    isRequired: definition.IsRequired,
                    hint: definition.Hint,
                    errorMessage: definition.ErrorMessage,
                    maxLength: definition.MaxLength,
                    maxLengthErrorMessage: definition.MaxLengthErrorMessage,
                    utcNow: dateTimeProvider.GetUtcNow());
            }
            else
            {
                // While BLUM_9087 is off these questions are product-owned report copy. Reconcile already-published
                // local databases so existing installations receive the same field types, hints, labels and error copy
                // as fresh databases.
                question.UpdateDetails(definition.Label, definition.Type, isRequired: definition.IsRequired, hint: definition.Hint);
                question.SetSection(definition.SectionId);
            }

            // Seed-owned copy, kept off UpdateDetails so the module editor cannot null it; set on both the
            // freshly-added and the reconciled path.
            question.SetProtocolCopy(definition.ErrorMessage, definition.MaxLength, definition.MaxLengthErrorMessage);

            var currentOptions = question.Options
                .Where(option => option.IsActive)
                .OrderBy(option => option.DisplayOrder)
                .Select(option => (option.Value, option.Label))
                .ToList();

            if (!currentOptions.SequenceEqual(definition.Options))
            {
                question.ClearOptions();
                for (var optionIndex = 0; optionIndex < definition.Options.Length; optionIndex++)
                {
                    var option = definition.Options[optionIndex];
                    question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, optionIndex + 1);
                }
            }
        }

        // Only a module never published: a reopened draft (BLUM-9102) is published through the admin API, which
        // writes its version, not here.
        if (module.PublishedAt is null)
        {
            module.Publish(actor, dateTimeProvider.GetUtcNow());
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    // With BLUM_9087 on, BLUM_7637 no longer reaches a published strategic_feedback, so switching it would change nothing
    // and say nothing. This names every seeded question still in use whose required setting is not what BLUM_7637 asks
    // for. It also fires where DHSC chose that setting themselves; settling BLUM_7637 (BLUM-9087 handover) ends it.
    private void WarnWhereBlum7637NoLongerApplies(FormModule module)
    {
        var feedbackOptional = featureFlags.IsEnabled(Feature.BLUM_7637);
        var disagreeing = module.Questions
            .Where(q => q.IsActive && SeededStrategicFeedbackCodes.Contains(q.Code) && q.IsRequired == feedbackOptional)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.Code)
            .ToList();
        if (disagreeing.Count == 0)
        {
            return;
        }

        logger.LogWarning(
            "Strategic feedback module seed: BLUM_7637 is {Blum7637State}, which makes the strategic feedback questions {FlagMakesThem}, but {QuestionCodes} {AreThey}. BLUM_9087 is on, so DHSC owns these questions and BLUM_7637 no longer changes them: edit them in the forms builder, or settle BLUM_7637.",
            feedbackOptional ? "on" : "off",
            feedbackOptional ? "optional" : "required",
            string.Join(", ", disagreeing),
            feedbackOptional ? "are required" : "are optional");
    }

    private FormQuestion NewQuestion(
        FormSection section,
        string code,
        string label,
        QuestionType type,
        int displayOrder,
        UserId actor,
        bool isRequired = false,
        string? hint = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null,
        string? errorMessage = null,
        int? maxLength = null,
        string? maxLengthErrorMessage = null,
        int? minValue = null,
        string? errorSubject = null,
        string? errorSubjectInline = null) =>
        FormQuestion.Create(
            FormQuestionId.New(),
            section.Id,
            code,
            label,
            type,
            displayOrder,
            actor,
            isRequired: isRequired,
            hint: hint,
            conditionalQuestionCode: conditionalQuestionCode,
            conditionalValue: conditionalValue,
            errorMessage: errorMessage,
            maxLength: maxLength,
            maxLengthErrorMessage: maxLengthErrorMessage,
            minValue: minValue,
            errorSubject: errorSubject,
            errorSubjectInline: errorSubjectInline,
            utcNow: dateTimeProvider.GetUtcNow());

    // Every question in this section is mandatory. The activity's name is a structural field outside this schema
    // (BLUM-7041), and RELATED_ISSUES on the People and issues section is the schema's one optional question
    // (BLUM-7479). WHO_WITH_OTHER is only enforced while its "other" reveal is showing — the validators
    // skip questions that AnswerVisibility rules out.
    //
    // Two questions for every category. The shared-authority question (1.BCDEF.1.4, {prefix}_SHARED) used to be
    // the third on four of them; BLUM-8340 withdrew it, and deployed databases keep it deactivated.
    private void SeedActivityDetailsSection(UserId actor, Category category, string prefix, int displayOrder = 0)
    {
        var section = FormSection.Create(FormSectionId.New(), "Activity details", displayOrder, actor, categoryId: category.Id, utcNow: dateTimeProvider.GetUtcNow());
        dbContext.FormSections.Add(section);

        var whoWith = NewQuestion(section, $"{prefix}_WHO_WITH", "Who have you worked with, or who will you work with?", QuestionType.Checkbox, 0, actor, isRequired: true, hint: "Select all who have helped or will help plan and deliver this activity");
        AddOptions(whoWith, KeyResultProtocolSeedData.WhoWithOptions);

        var whoWithOther = NewQuestion(section, $"{prefix}_WHO_WITH_OTHER", "Who else have you worked with or will you work with?", QuestionType.Text, 1, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_WHO_WITH", conditionalValue: "other");

        dbContext.FormQuestions.AddRange(whoWith, whoWithOther);
    }

    // The Joint partnership record's only question section (BLUM-7860, reshaped by BLUM-8090).
    //
    // Titled "Activity details" because that is the H1 the Figma gives both of its pages, and because
    // isActivitySectionApplicable (views/Activities/display.tsx) falls through to its `sectionTitle === 'Activity
    // details'` arm for any category that is neither People-and-roles nor People-and-issues, which is what makes the
    // section apply with no new title in DETAIL_SECTION_TITLES. The structural activity-name field that title would
    // otherwise bring is switched off for this category by sectionShowsNameField — a partnership has no name.
    //
    // ONE section holding two questions, although the wizard asks them on two pages (asksOneQuestionPerPage): Check
    // your answers and the read-only view show a single "Activity details" card with one Change link, which two
    // sections could not give — each would be its own card, and a link could reach only one of them.
    //
    // No description: the pages are the question alone. BLUM-8090 also moved the three progress questions this
    // section used to ask (JP_PROGRESS / JP_CHALLENGES / JP_NEXT_QUARTER) into the quarterly report, where they are
    // the module's AKR_DELIVERY_* questions in partnership wording (JointPartnershipDeliveryCopy).
    private void SeedJointPartnershipSection(UserId actor, Category category)
    {
        var section = FormSection.Create(FormSectionId.New(), "Activity details", 0, actor, categoryId: category.Id, utcNow: dateTimeProvider.GetUtcNow());
        dbContext.FormSections.Add(section);

        // A commitment of nothing is not a commitment, so this is the one amount with a minimum, and its messages
        // name it the way the hint does rather than by the question (BLUM-8955). A deployed database gets the same
        // values from the BLUM_8955 migration.
        var committed = NewQuestion(
            section,
            "JP_COMMITTED",
            "How much have you committed to this joint partnership?",
            QuestionType.Currency,
            0,
            actor,
            isRequired: true,
            hint: "The amount you have agreed",
            errorMessage: "Enter the amount you have agreed",
            minValue: 1,
            errorSubject: "Amount agreed",
            errorSubjectInline: "the amount you have agreed");

        // A single-line text input, per the Figma, but still capped at the free-text limit.
        var partners = NewQuestion(
            section,
            "JP_PARTNERS",
            "Who is in the joint partnership?",
            QuestionType.Text,
            1,
            actor,
            isRequired: true,
            hint: "Tell us what other local authorities are in the partnership",
            errorMessage: "Enter who is in the joint partnership",
            maxLength: FreeTextMaxLength,
            maxLengthErrorMessage: $"Joint partnership members must be {FreeTextMaxLength} characters or less");

        dbContext.FormQuestions.AddRange(committed, partners);
    }

    private void SeedAdditionalInformationSection(UserId actor, Category category, string prefix, int displayOrder = 1)
    {
        var section = FormSection.Create(FormSectionId.New(), "Additional information", displayOrder, actor, categoryId: category.Id, utcNow: dateTimeProvider.GetUtcNow());
        dbContext.FormSections.Add(section);

        var plan = NewQuestion(
            section,
            $"{prefix}_PLAN",
            "Can you tell us about your approach to this activity?",
            QuestionType.Textarea,
            0,
            actor,
            isRequired: true,
            hint: "For example the steps you have taken or plan to take",
            errorMessage: "Enter a description of what you plan to do",
            maxLength: FreeTextMaxLength,
            maxLengthErrorMessage: "Description of plans must be 500 characters or less");
        dbContext.FormQuestions.Add(plan);
    }

    // Education and Help share the People and issues screen, and it applies to EVERY type in either category:
    // the section is category-scoped only, so FormSection.AppliesToTask returns true whatever the activity type
    // (BLUM-7778). Every question here is mandatory except RELATED_ISSUES (BLUM-7479); its "other" free-text
    // child stays required while that reveal is showing. It sits at order 1, after the shared Activity
    // details section (1.EF.1.5/1.6 follow 1.BCDEF.1.1-1.4 — BLUM-7120). Codes remain category-prefixed so
    // answers retain their category-specific schema identity while the visible wording and options stay aligned.
    //
    // This screen's routing has now changed hands three times — category-wide (17 July), scoped to each
    // category's "Other ..." type (BLUM-7092, 21 July), category-wide again here. Date any QA ticket describing
    // the Other-only behaviour against BLUM-7778 before "fixing" it back.
    private void SeedPeopleAndIssuesSection(UserId actor, Category category, string prefix)
    {
        var section = FormSection.Create(
            FormSectionId.New(), "People and issues", 1, actor, categoryId: category.Id, utcNow: dateTimeProvider.GetUtcNow());
        dbContext.FormSections.Add(section);

        var whoFor = NewQuestion(section, $"{prefix}_WHO_FOR", "Who was the activity for, or who will it be for?", QuestionType.Checkbox, 0, actor, isRequired: true, hint: "Select any that apply");
        AddOptions(whoFor, new[]
        {
            ("all_residents", "All residents in the local area"),
            ("children", "Children"),
            ("students_young_adults", "Students and young adults"),
            ("women_girls", "Women and girls"),
            ("men_boys", "Men and boys"),
            ("ethnic_minority", "Ethnic minority groups"),
            ("lgbtq", "LGBTQ+ people"),
            ("unemployed_low_income", "People who are unemployed or on low incomes"),
            ("in_work_workplace", "People in work or workplace groups"),
            ("criminal_justice", "People in the criminal justice system"),
            ("public_health_team", "Public health team"),
            ("frontline_workers", "Frontline workers"),
            ("other", "Other"),
        });

        var whoForOther = NewQuestion(section, $"{prefix}_WHO_FOR_OTHER", "Who else was or will the activity be for?", QuestionType.Text, 1, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_WHO_FOR", conditionalValue: "other");

        // BLUM-7479: the only optional question in the activity schema. QA raised that Continue was blocked with
        // this left blank; the protocol never made 1.EF.1.6 mandatory. The "(optional)" suffix is GDS convention for
        // the one optional field on a form of otherwise-required ones, and lives in the label because every surface
        // (wizard, Check your answers, the read-only view, the Form Designer list) renders question.Label verbatim.
        var relatedIssues = NewQuestion(section, $"{prefix}_RELATED_ISSUES", "What related issues did it address, or will it address? (optional)", QuestionType.Checkbox, 2, actor, isRequired: false, hint: "Select any that apply");

        // "Experience of gambling harms" leads the list in the 13 August protocol (BLUM-7620) — it is the issue
        // this service exists for, so it is offered first rather than buried among the comorbidities.
        AddOptions(relatedIssues, new[]
        {
            ("gambling_harms", "Experience of gambling harms"),
            ("mental_health", "Mental health problems"),
            ("money_debt", "Money or debt problems"),
            ("alcohol_drug", "Alcohol or drug dependency"),
            ("housing_homelessness", "Housing or homelessness"),
            ("learning_differences", "Learning differences or disabilities"),
            ("social_isolation", "Social isolation"),
            ("trauma_abuse", "Experience of trauma or abuse"),
            ("other", "Other"),
        });

        var relatedIssuesOther = NewQuestion(section, $"{prefix}_RELATED_ISSUES_OTHER", "What is the other related issue?", QuestionType.Text, 3, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_RELATED_ISSUES", conditionalValue: "other", errorMessage: "Enter the related issue");

        dbContext.FormQuestions.AddRange(whoFor, whoForOther, relatedIssues, relatedIssuesOther);
    }

    private void AddOptions(FormQuestion question, IReadOnlyList<(string Value, string Label)> options)
    {
        var order = 0;
        foreach (var (value, label) in options)
        {
            question.AddOption(FormQuestionOptionId.New(), value, label, order++, dateTimeProvider.GetUtcNow());
        }
    }
}
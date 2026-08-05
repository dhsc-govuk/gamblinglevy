using Application.Common.Settings;
using Domain.Categories;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.KeyResults;
using Domain.Users;
using Domain.ValueObjects;
using Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence;

public class ApplicationDbContextInitialiser(
    ILogger<ApplicationDbContextInitialiser> logger,
    ApplicationDbContext dbContext,
    ApplicationSettings settings,
    IWebHostEnvironment hostingEnvironment)
{
    // The two "Other ..." activity types that unlock the People and issues screen (BLUM-7092). Referenced by both
    // the taxonomy seed below and the form-schema seed's section lookup, so the names cannot drift apart — a
    // mismatch would silently leave the section unseeded.
    private const string OtherEducationTaskName = "Other type of education, campaign or outreach";

    private const string OtherHelpTaskName = "Other type of help, advice or service";

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

    public async Task SeedDataAsync()
    {
        await SeedAdminUser();
        await SeedActivityReferenceData();
        await SeedFormDesigner();
        await SeedDataCollectionFormModulesAsync();
        await SeedKeyResultProtocolAsync();
        await SeedQuarterlyReportModuleAsync();
        await SeedStrategicFeedbackModuleAsync();
    }

    private async Task SeedAdminUser()
    {
        try
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
                    UserRole.Admin);
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding default admin user.");
            throw;
        }
    }

    // Seeds the activity taxonomy: the six fixed activity categories and the "activity type" options shown under
    // each (BLUM redesign). The "Other ..." type lets the user enter a free-text name (Activity.CustomTaskText).
    private async Task SeedActivityReferenceData()
    {
        try
        {
            if (await dbContext.Categories.AnyAsync())
            {
                return;
            }

            var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
            if (admin is null)
            {
                return;
            }

            var actor = admin.Id;

            var peopleAndRoles = Category.Seed(CategoryId.New(), "People and roles", 1, actor);
            var needsAssessment = Category.Seed(CategoryId.New(), "Needs assessment", 2, actor);
            var strategy = Category.Seed(CategoryId.New(), "Strategy and leadership", 3, actor);
            var licensing = Category.Seed(CategoryId.New(), "Licensing, planning and policy", 4, actor);
            var education = Category.Seed(CategoryId.New(), "Education, campaigns and outreach", 5, actor);
            var help = Category.Seed(CategoryId.New(), "Help, advice and access to services", 6, actor);
            dbContext.Categories.AddRange(peopleAndRoles, needsAssessment, strategy, licensing, education, help);

            var peopleTypes = new[]
            {
                "Public Health Officer",
                "Public Health Analyst",
                "Public Health Practitioner",
                "Public Health Specialist",
                "Public Health Intelligence Specialist",
                "Consultant in Public Health",
                "Other role",
            };

            var needsTypes = new[]
            {
                "Contributing to a regional needs assessment",
                "Creating a local needs assessment",
            };

            // Type lists are aligned to the Question Protocol's per-category activity types (3 July update,
            // BLUM-6933) — the key-result protocol seed (SeedKeyResultProtocolAsync) resolves its question
            // blocks by these names. The companion BLUM_6933_ReseedCreateActivitiesProtocol migration wipes
            // already-seeded environments so this seeder rebuilds them on next boot.
            var strategyTypes = new[]
            {
                "Documenting a strategy and action plan",
                "Setting up and running a local steering group",
                "Joining and attending a regional network",
                "Other type of strategy or leadership",
            };

            var licensingTypes = new[]
            {
                "Reviewing and evolving current licensing policy",
                "Responding to gambling licence applications",
                "Managing planning applications for gambling venues",
                "Restricting gambling advertising on council-owned spaces",
                "Other type of licensing, planning or policy",
            };

            var educationTypes = new[]
            {
                "Training your workforce on preventing gambling harms",
                "Briefing elected members on gambling-related harms",
                "Running campaigns and workshops to raise awareness among local residents and at-risk groups",
                "Creating a workplace charter",
                "Involving people with lived experience in planning and peer support",
                OtherEducationTaskName,
            };

            var helpTypes = new[]
            {
                "Improving referrals between services",
                "Using screening tools to better assess gambling harm",
                "Working with organisations to support people affected by gambling harms",
                OtherHelpTaskName,
            };

            SeedTypes(actor, peopleAndRoles, peopleTypes);
            SeedTypes(actor, needsAssessment, needsTypes);
            SeedTypes(actor, strategy, strategyTypes);
            SeedTypes(actor, licensing, licensingTypes);
            SeedTypes(actor, education, educationTypes);
            SeedTypes(actor, help, helpTypes);

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding activity reference data.");
            throw;
        }
    }

    private void SeedTypes(UserId actor, Category category, IReadOnlyList<string> typeNames)
    {
        var order = 1;
        foreach (var name in typeNames)
        {
            dbContext.ActivityTasks.Add(ActivityTask.Seed(ActivityTaskId.New(), category.Id, name, order++, actor));
        }
    }

    // Seeds the category-scoped activity-record form schema. Detail questions render after the structural type;
    // Additional information renders after status and immediately before Check your answers. Codes are globally
    // unique and prefixed per category. The structural steps (category, type, status, name) and free-text
    // expected results are handled outside the schema.
    //
    // Deliberately seed-if-empty: the canonical schema is delivered ONCE, after which DHSC admins own it through
    // the Form Designer — this seeder must never overwrite their edits, so it does not touch a non-empty schema
    // (BLUM-7081). Shipping changed seed content therefore requires a one-shot DELETE-only migration that clears
    // form_question_options/form_questions/form_sections so this guard re-arms for a single rebuild on next boot.
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
            var peopleAndRoles = FindCategory("People and roles");
            if (peopleAndRoles is null)
            {
                logger.LogWarning("Form designer seed: category 'People and roles' not found; skipping its 'Role details' section.");
            }
            else
            {
                var roleSection = FormSection.Create(FormSectionId.New(), "Role details", 0, actor, categoryId: peopleAndRoles.Id);
                dbContext.FormSections.Add(roleSection);

                var fundingTime = NewQuestion(roleSection, "PR_FUNDING_TIME", "How much of the staff member's time will the funding pay for?", QuestionType.Radio, 0, actor, isRequired: true);
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

                var roleShared = NewQuestion(roleSection, "PR_ROLE_SHARED", "Is the role shared with other local authorities?", QuestionType.Radio, 2, actor, isRequired: true);
                AddOptions(roleShared, new[]
                {
                    ("yes", "Yes"),
                    ("no", "No, it's a local role"),
                });

                dbContext.FormQuestions.AddRange(fundingTime, roleDuration, roleShared);
            }

            // Every non-People category asks the shared Activity details questions (protocol 1.BCDEF.1.1-1.4) —
            // Education and Help included (BLUM-7120). Education and Help ADDITIONALLY get the People and issues
            // screen below, on their "Other ..." type only.
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
                (Name: "Education, campaigns and outreach", Prefix: "EDU", OtherTaskName: OtherEducationTaskName),
                (Name: "Help, advice and access to services", Prefix: "HELP", OtherTaskName: OtherHelpTaskName),
            };
            foreach (var (name, prefix, otherTaskName) in peopleAndIssues)
            {
                var category = FindCategory(name);
                if (category is null)
                {
                    logger.LogWarning("Form designer seed: category '{CategoryName}' not found; skipping its 'People and issues' section.", name);
                    continue;
                }

                await SeedPeopleAndIssuesSectionAsync(actor, category, prefix, otherTaskName);
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

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two instances can boot together against the one empty schema (e.g. right after the reseed
            // migration) and both pass the guard; form_questions.code is uniquely indexed, so the loser fails
            // here. The failed inserts are still tracked as Added on this shared context and would be re-flushed
            // (and re-fail) by the next seeder's SaveChangesAsync, so drop them before deciding anything else.
            dbContext.ChangeTracker.Clear();

            // Distinguish losing the race from a self-inflicted duplicate in the seed content itself. Postgres
            // only raises 23505 after the conflicting transaction has committed, so if another instance won, its
            // schema is visible by now — benign, continue. If the tables are still empty, the collision was our
            // own rows (e.g. a duplicated question code): the rollback left the schema permanently empty and the
            // guard will fail identically on every boot, so that must stay a loud startup failure.
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
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the activity-record form designer schema.");
            throw;
        }
    }

    // form_questions.code is uniquely indexed, so a concurrent seed surfaces as a 23505 unique violation.
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Seeds the three built-in data-collection form modules (the reusable forms-builder modules DHSC admins assign
    // to collections). Idempotent: skips if any module already exists so it never crash-loops boot on a re-seed.
    private async Task SeedDataCollectionFormModulesAsync()
    {
        try
        {
            if (await dbContext.FormModules.AnyAsync())
            {
                return;
            }

            var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
            if (admin is null)
            {
                return;
            }

            var actor = admin.Id;

            dbContext.FormModules.AddRange(
                FormModule.Create(FormModuleId.New(), "activity_key_results", "Enter activity key results", 1, actor),
                FormModule.Create(FormModuleId.New(), "funding_spent", "Record funding spent", 2, actor),
                FormModule.Create(FormModuleId.New(), "strategic_feedback", "Provide strategic feedback", 3, actor));

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding data-collection form modules.");
            throw;
        }
    }

    // Seeds the Question Protocol's standard key results + quarterly-report question bank (BLUM-6611), transcribed
    // in KeyResultProtocolSeedData. Blocks resolve to ActivityTasks by name: a missing task is logged and skipped
    // (never crash boot — reference-data drift on an already-seeded environment must not crash-loop startup).
    private async Task SeedKeyResultProtocolAsync()
    {
        try
        {
            if (await dbContext.KeyResultQuestions.AnyAsync())
            {
                return;
            }

            var admin = await dbContext.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin);
            if (admin is null)
            {
                return;
            }

            var actor = admin.Id;
            var tasks = await dbContext.ActivityTasks.ToListAsync();

            foreach (var type in KeyResultProtocolSeedData.Protocol)
            {
                var task = tasks.FirstOrDefault(t => t.TaskName == type.TaskName);
                if (task is null)
                {
                    logger.LogWarning("Key-result protocol seed: activity type '{TaskName}' not found; skipping its block.", type.TaskName);
                    continue;
                }

                var order = 0;
                foreach (var keyResult in type.KeyResults)
                {
                    dbContext.KeyResultDefinitions.Add(KeyResultDefinition.Seed(
                        KeyResultDefinitionId.New(), task.Id, keyResult.Code, keyResult.Label, order++, actor));
                }

                order = 0;
                foreach (var question in type.Questions)
                {
                    dbContext.KeyResultQuestions.Add(KeyResultQuestion.Seed(
                        KeyResultQuestionId.New(),
                        task.Id,
                        question.KeyResultCode,
                        question.Code,
                        QuestionStatusScope.From(question.StatusScope),
                        question.Label,
                        ProtocolFieldType.From(question.FieldType),
                        order++,
                        actor,
                        hint: question.Hint,
                        minValue: question.Min,
                        maxValue: question.Max,
                        options: question.Options,
                        prefillSource: question.PrefillSource,
                        conditionalQuestionCode: question.ConditionalQuestionCode,
                        conditionalValue: question.ConditionalValue,
                        errorMessage: question.ErrorMessage));
                }
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the key-result question protocol.");
            throw;
        }
    }

    // Adds the three unconditional "Activity delivery" questions to the activity_key_results module and publishes
    // it so it can be assigned to collections. Deliberately NOT guarded by an aggregate-level AnyAsync: deployed
    // environments already hold the module (Draft, empty) from SeedDataCollectionFormModulesAsync's earlier run,
    // so this seeder converges at row level instead — it no-ops once the module is published.
    private async Task SeedQuarterlyReportModuleAsync()
    {
        try
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

            if (module.Status.IsPublished)
            {
                return;
            }

            var actor = admin.Id;
            var deliveryQuestions = new (string Code, string Label, bool IsRequired, string Hint)[]
            {
                ("AKR_DELIVERY_PROGRESS", "What progress have you made?", true,
                    "Describe the milestones reached this quarter, but do not include financial details"),
                ("AKR_DELIVERY_CHALLENGES", "What challenges have you encountered? (optional)", false,
                    "Describe any issues that have slowed down your progress, for example recruitment or partnership changes"),
                ("AKR_DELIVERY_NEXT_QUARTER", "What are you planning to do next quarter? (optional)", false,
                    "Describe your priorities, next steps and any changes you plan to make"),
            };

            var order = module.Questions.Count;
            foreach (var (code, label, isRequired, hint) in deliveryQuestions)
            {
                if (module.Questions.Any(q => q.Code == code))
                {
                    continue;
                }

                module.AddQuestion(DcFormQuestionId.New(), code, label, QuestionType.Textarea, order++, actor, isRequired: isRequired, hint: hint);
            }

            module.Publish(actor);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the quarterly report module.");
            throw;
        }
    }

    // Adds the two pages ("Wider progress and challenges", "Working together") and their strategic-feedback questions to
    // the strategic_feedback module and publishes it so it is a usable, locked, assignable schema (BLUM-6613). Modeled
    // on SeedQuarterlyReportModuleAsync: NOT guarded by an aggregate-level AnyAsync — deployed environments already
    // hold the module from SeedDataCollectionFormModulesAsync, so this converges at row level and reconciles the
    // product-owned schema on published installations. Sections are owned, so the tracked load includes them too.
    private async Task SeedStrategicFeedbackModuleAsync()
    {
        try
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

            var actor = admin.Id;

            var widerProgress = module.Sections.FirstOrDefault(s => s.SectionNumber == 1)
                ?? module.AddSection(DcFormSectionId.New(), 1, "Wider progress and challenges", actor);
            var workingTogether = module.Sections.FirstOrDefault(s => s.SectionNumber == 2)
                ?? module.AddSection(DcFormSectionId.New(), 2, "Working together", actor);

            var feedbackQuestions = new[]
            {
                new
                {
                    Code = "SF_OTHER_PREVENTION",
                    Label = "What other gambling harms prevention work have you focused on this quarter?",
                    Hint = "Use this space to tell us about any work not already included in the activities you’ve entered",
                    Type = QuestionType.Textarea,
                    SectionId = widerProgress.Id,
                    Options = Array.Empty<(string Value, string Label)>(),
                },
                new
                {
                    Code = "SF_CHALLENGES",
                    Label = "What challenges have you encountered?",
                    Hint = "Describe any challenges you've faced, for example engagement, resources or competing priorities",
                    Type = QuestionType.Textarea,
                    SectionId = widerProgress.Id,
                    Options = Array.Empty<(string Value, string Label)>(),
                },
                new
                {
                    Code = "SF_DHSC_SUPPORT",
                    Label = "Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?",
                    Hint = "For example, things that could be improved or gaps you’ve identified",
                    Type = QuestionType.Textarea,
                    SectionId = workingTogether.Id,
                    Options = Array.Empty<(string Value, string Label)>(),
                },
                new
                {
                    Code = "SF_LEARNINGS",
                    Label = "Do you have any learnings you’d like to share with other local authorities?",
                    Hint = "If yes, we’ll contact you to find out more",
                    Type = QuestionType.Radio,
                    SectionId = workingTogether.Id,
                    Options = new[] { (Value: "yes", Label: "Yes"), (Value: "no", Label: "No") },
                },
            };

            var order = module.Questions.Count;
            foreach (var definition in feedbackQuestions)
            {
                var question = module.Questions.FirstOrDefault(q => q.Code == definition.Code);
                if (question is null)
                {
                    if (module.Status.IsPublished)
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
                        isRequired: true,
                        hint: definition.Hint);
                }
                else
                {
                    // These questions are product-owned report copy. Reconcile already-published local databases so
                    // existing installations receive the same field types, hints and labels as fresh databases.
                    question.UpdateDetails(definition.Label, definition.Type, isRequired: true, hint: definition.Hint);
                    question.SetSection(definition.SectionId);
                }

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

            if (!module.Status.IsPublished)
            {
                module.Publish(actor);
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the strategic feedback module.");
            throw;
        }
    }

    private static FormQuestion NewQuestion(
        FormSection section,
        string code,
        string label,
        QuestionType type,
        int displayOrder,
        UserId actor,
        bool isRequired = false,
        string? hint = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null) =>
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
            conditionalValue: conditionalValue);

    // Every question here is mandatory except the activity's name, which is a structural field outside this
    // schema (BLUM-7041). WHO_WITH_OTHER is only enforced while its "other" reveal is showing — the validators
    // skip questions that IsVisible rules out. All five non-People categories ask the same questions, including
    // the shared-authority one (protocol 1.BCDEF.1.4) — see BLUM-7081 and BLUM-7120.
    private void SeedActivityDetailsSection(UserId actor, Category category, string prefix, int displayOrder = 0)
    {
        var section = FormSection.Create(FormSectionId.New(), "Activity details", displayOrder, actor, categoryId: category.Id);
        dbContext.FormSections.Add(section);

        var whoWith = NewQuestion(section, $"{prefix}_WHO_WITH", "Who will you work with?", QuestionType.Checkbox, 0, actor, isRequired: true, hint: "Select any that apply");
        AddOptions(whoWith, KeyResultProtocolSeedData.WhoWithOptions);

        var whoWithOther = NewQuestion(section, $"{prefix}_WHO_WITH_OTHER", "Who else will you work with?", QuestionType.Text, 1, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_WHO_WITH", conditionalValue: "other");

        var shared = NewQuestion(section, $"{prefix}_SHARED", "Is this activity shared with other local authorities?", QuestionType.Radio, 3, actor, isRequired: true);
        AddOptions(shared, new[] { ("yes", "Yes"), ("no", "No, it's a local activity") });

        dbContext.FormQuestions.AddRange(whoWith, whoWithOther, shared);
    }

    private void SeedAdditionalInformationSection(UserId actor, Category category, string prefix, int displayOrder = 1)
    {
        var section = FormSection.Create(FormSectionId.New(), "Additional information", displayOrder, actor, categoryId: category.Id);
        dbContext.FormSections.Add(section);

        var plan = NewQuestion(
            section,
            $"{prefix}_PLAN",
            "What do you plan to do?",
            QuestionType.Textarea,
            0,
            actor,
            isRequired: true,
            hint: "Use this space to add any other information about your approach and the steps you'll take");
        dbContext.FormQuestions.Add(plan);
    }

    // Education and Help share the People and issues screen, but only for their "Other ..." activity type — the
    // protocol shows 1.EF.1.5/1.6 for that type alone, so the section is scoped to it and every other type in
    // either category skips the screen entirely (BLUM-7092). It sits at order 1, after the shared Activity
    // details section (1.EF.1.5/1.6 follow 1.BCDEF.1.1-1.4 — BLUM-7120). Codes remain category-prefixed so
    // answers retain their category-specific schema identity while the visible wording and options stay aligned.
    private async Task SeedPeopleAndIssuesSectionAsync(UserId actor, Category category, string prefix, string otherTaskName)
    {
        var otherTask = await dbContext.ActivityTasks.FirstOrDefaultAsync(
            t => t.CategoryId == category.Id && t.TaskName == otherTaskName);
        if (otherTask is null)
        {
            logger.LogWarning(
                "Form designer seed: activity type '{TaskName}' not found; skipping the 'People and issues' section for '{CategoryName}'.",
                otherTaskName,
                category.Name);
            return;
        }

        var section = FormSection.Create(
            FormSectionId.New(), "People and issues", 1, actor, categoryId: category.Id, activityTaskId: otherTask.Id);
        dbContext.FormSections.Add(section);

        var whoFor = NewQuestion(section, $"{prefix}_WHO_FOR", "Who is the activity for?", QuestionType.Checkbox, 0, actor, isRequired: true, hint: "Select any that apply");
        AddOptions(whoFor, new[]
        {
            ("all_residents", "All residents in the local area"),
            ("children", "Children"),
            ("students_young_adults", "Students and young adults"),
            ("women_girls", "Women and girls"),
            ("ethnic_minority", "Ethnic minority groups"),
            ("lgbtq", "LGBTQ+ people"),
            ("unemployed_low_income", "People who are unemployed or on low incomes"),
            ("in_work_workplace", "People in work or workplace groups"),
            ("criminal_justice", "People in the criminal justice system"),
            ("public_health_team", "Public health team"),
            ("frontline_workers", "Frontline workers"),
            ("other", "Other"),
        });

        var whoForOther = NewQuestion(section, $"{prefix}_WHO_FOR_OTHER", "Who else is the activity for?", QuestionType.Text, 1, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_WHO_FOR", conditionalValue: "other");

        var relatedIssues = NewQuestion(section, $"{prefix}_RELATED_ISSUES", "What related issues will it address?", QuestionType.Checkbox, 2, actor, isRequired: true, hint: "Select any that apply");
        AddOptions(relatedIssues, new[]
        {
            ("mental_health", "Mental health problems"),
            ("money_debt", "Money or debt problems"),
            ("alcohol_drug", "Alcohol or drug dependency"),
            ("housing_homelessness", "Housing or homelessness"),
            ("learning_differences", "Learning differences or disabilities"),
            ("social_isolation", "Social isolation"),
            ("trauma_abuse", "Experience of trauma or abuse"),
            ("gambling_harms", "Experience of gambling harms"),
            ("other", "Other"),
        });

        var relatedIssuesOther = NewQuestion(section, $"{prefix}_RELATED_ISSUES_OTHER", "What other issue will it address?", QuestionType.Text, 3, actor, isRequired: true, conditionalQuestionCode: $"{prefix}_RELATED_ISSUES", conditionalValue: "other");

        dbContext.FormQuestions.AddRange(whoFor, whoForOther, relatedIssues, relatedIssuesOther);
    }

    private static void AddOptions(FormQuestion question, IReadOnlyList<(string Value, string Label)> options)
    {
        var order = 0;
        foreach (var (value, label) in options)
        {
            question.AddOption(FormQuestionOptionId.New(), value, label, order++);
        }
    }
}
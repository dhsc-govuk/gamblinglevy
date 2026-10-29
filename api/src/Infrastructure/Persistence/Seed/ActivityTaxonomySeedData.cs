using Domain.Categories;

namespace Infrastructure.Persistence.Seed;

// The activity taxonomy: the fixed activity categories and the "activity type" options shown under each. The
// position in each list is the row's SequenceOrder (1-based), and the names are the keys the seeder matches on —
// SeedKeyResultProtocolAsync resolves its question blocks by type name and SeedFormDesigner its sections by category
// name, so a name here is not just copy.
//
// The seeder inserts any category or type this lists that the database lacks, so a new type is an edit here and
// nothing else (BLUM-8749). A new category is not: its activity-record form sections come from SeedFormDesigner,
// which only seeds an empty schema. On a live database those sections ship as a guarded INSERT migration, written
// alongside the matching SeedFormDesigner edit so a fresh database gets them too. Without that the category has no
// questions. The seeder never updates or deletes: renaming or withdrawing a row is a guarded UPDATE migration
// (BLUM-8156 renamed "Using screening tools…", BLUM-8340 withdrew questions with is_active = false), with this list
// edited to match so a fresh database agrees. A renamed entry left here under its old name would be inserted again.
public static class ActivityTaxonomySeedData
{
    // The two catch-all "Other ..." activity types Education and Help each end their type list with. They used to
    // scope the People and issues screen as well (BLUM-7092); BLUM-7778 removed that, so these are now only the
    // taxonomy's own names — the client still keys its free-text "name the other type" reveal off the "Other"
    // prefix, so don't rename them casually.
    public const string OtherEducationTaskName = "Other type of education, campaign or outreach";

    public const string OtherHelpTaskName = "Other type of help, advice or service";

    // The Joint partnership category's single activity type (BLUM-7860). It shows on Check your answers and the
    // read-only view as the "Activity type" row, and is the funding section's last-resort row label. One type, so
    // the wizard auto-selects it and shows the explanatory copy in place of a one-option radio group. The name must
    // not begin with "Other" — the client keys its free-text "name the other type" reveal off that prefix.
    public const string JointPartnershipTaskName = "Joint partnership";

    public sealed record CategorySeed(string Name, IReadOnlyList<string> TaskNames);

    // Type lists are aligned to the Question Protocol's per-category activity types (3 July update, BLUM-6933).
    public static IReadOnlyList<CategorySeed> Categories { get; } =
    [
        new(
            CategoryNames.PeopleAndRoles,
            [
                "Public Health Officer",
                "Public Health Analyst",
                "Public Health Practitioner",
                "Public Health Specialist",
                "Public Health Intelligence Specialist",
                "Consultant in Public Health",
                "Other role",
            ]),
        new(
            "Needs assessment",
            [
                "Contributing to a regional needs assessment",
                "Creating a local needs assessment",
            ]),
        new(
            "Strategy and leadership",
            [
                "Documenting a strategy and action plan",
                "Setting up and running a local steering group",
                "Joining and attending a regional network",
                "Other type of strategy or leadership",
            ]),
        new(
            "Licensing, planning and policy",
            [
                "Reviewing and evolving current licensing policy",
                "Responding to gambling licence applications",
                "Managing planning applications for gambling venues",
                "Restricting gambling advertising on council-owned spaces",
                "Other type of licensing, planning or policy",
            ]),
        new(
            CategoryNames.EducationCampaignsAndOutreach,
            [
                "Training your workforce on preventing gambling harms",
                "Briefing elected members on gambling-related harms",
                "Running campaigns and workshops to raise awareness among local residents and at-risk groups",
                "Creating a workplace charter",
                "Involving people with lived experience in planning and peer support",
                OtherEducationTaskName,
            ]),
        new(
            CategoryNames.HelpAdviceAndAccessToServices,
            [
                "Improving referrals between services",
                "Using screening tools to better assess gambling harms",
                "Working with organisations to support people affected by gambling harms",
                OtherHelpTaskName,
            ]),
        new(
            CategoryNames.JointPartnership,
            [JointPartnershipTaskName]),
    ];
}
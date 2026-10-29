namespace Domain.Categories;

// The seeded category names that behaviour keys on. Category ids are minted per environment (Category.Seed takes
// CategoryId.New()), so anything that has to recognise one specific category matches on its name instead — the
// precedent is ExpectedResultInputs.PeopleCategoryName, which now reads from here.
//
// Renaming a value is a coordinated change, not a rename: the seeder writes these strings, SeedFormDesigner
// resolves its sections by them, and the client mirrors both in client/src/views/Activities/display.tsx.
public static class CategoryNames
{
    // People-and-roles records are named by their role type, so they collect neither an activity name nor key
    // results — no protocol questions exist for any of their types.
    public const string PeopleAndRoles = "People and roles";

    // Joint partnership (BLUM-7860): levy money pooled with other organisations into a shared pot, as opposed to
    // an activity an LA funds directly. The record asks only what was committed and who the partners are — no
    // name, no start date (it is always Active) and no measures (BLUM-8090) — and reports funding like any other
    // activity. In "Enter activity results" it answers only the delivery questions, in its own wording
    // (JointPartnershipDeliveryCopy).
    public const string JointPartnership = "Joint partnership";

    // The two categories that additionally ask the "People and issues" screen (BLUM-7120). Named here because the
    // activity-record export decides which form sections a record was asked by category name, the way the client's
    // isActivitySectionApplicable does (BLUM-7648).
    public const string EducationCampaignsAndOutreach = "Education, campaigns and outreach";

    public const string HelpAdviceAndAccessToServices = "Help, advice and access to services";
}
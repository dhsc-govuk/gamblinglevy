using Domain.Collections;

namespace Application.MaturityAssessments.Protocol;

public sealed record MaturityCollectionForm(Guid Id, string SectionCode, string Title);

// The maturity-assessment sections as the "forms" a DHSC admin picks on the Add data collection Forms step
// (BLUM-7943). A quarterly report's forms are form_modules rows; the maturity catalogue is static C#, so each
// section is given a fixed id here instead and rides in collection_forms.form_module_id, which has no foreign key to
// form_modules.
//
// These literals are persisted: collection_forms stores them and migration 20260916120000_BLUM_7943 writes them for
// every maturity collection that existed before. Never change or reuse one — a changed id orphans every collection
// that holds it. MaturityCollectionFormsTests pins the values and checks the migration carries each of them.
public static class MaturityCollectionForms
{
    private static readonly IReadOnlyDictionary<string, Guid> IdsBySectionCode = new Dictionary<string, Guid>(StringComparer.Ordinal)
    {
        ["la_capacity"] = Guid.Parse("aa721c9a-7034-4444-b706-f61167e7022e"),
        ["needs_assessment"] = Guid.Parse("a963f8c5-a06b-4a71-8f4e-33635f4f557d"),
        ["strategic_approach"] = Guid.Parse("14e6c7ed-3fa0-42fa-80b0-9a5f19c36ea9"),
        ["awareness_training_signposting"] = Guid.Parse("882b90b0-0cc8-4417-b56b-111928e2bbe5"),
        ["planning_licensing"] = Guid.Parse("6f9b7386-fc4f-446c-a77d-b1a9e84904ea"),
        ["local_campaigns"] = Guid.Parse("88cd0c58-026f-49e0-a6a8-b978b04562ab"),
        ["advertising_restrictions"] = Guid.Parse("079504a1-7b31-4505-bf70-ca3a6ced0bbe"),
        ["working_with_vcse"] = Guid.Parse("6ab2174b-9321-42fe-9cd7-4d2b47f7ac92"),
        ["other_preventative_services"] = Guid.Parse("37ac8124-0133-4728-85cb-c364462eda0b"),
        ["next_steps"] = Guid.Parse("ecbf4e5b-78fa-4158-a73f-3bf166c497d2"),
    };

    // Catalogue order and titles. A section added to MaturityProtocol without an id here fails type initialisation
    // (and the pinning test) rather than silently dropping off the Forms step.
    public static IReadOnlyList<MaturityCollectionForm> All { get; } = MaturityProtocol.Sections
        .Select(section => new MaturityCollectionForm(IdsBySectionCode[section.Code], section.Code, section.Title))
        .ToList();

    public static IReadOnlyList<Guid> Ids { get; } = All.Select(form => form.Id).ToList();

    public static MaturityCollectionForm? Find(Guid id) => All.FirstOrDefault(form => form.Id == id);

    public static bool IsMaturityFormId(Guid id) => Find(id) is not null;

    // The sections an LA's assessment is made of: the collection's CURRENT forms, in catalogue order (BLUM-8093). Read
    // live on every request rather than frozen at Start, so a form the admin unticks leaves the task list, the submit
    // gate, the snapshot and the DHSC views together. Ids that are not maturity sections are ignored.
    public static IReadOnlyList<string> SectionCodesFor(Collection collection)
    {
        var selected = collection.Forms.Select(form => form.FormModuleId.Value).ToHashSet();
        return All.Where(form => selected.Contains(form.Id)).Select(form => form.SectionCode).ToList();
    }

    public static bool Includes(Collection collection, string sectionCode) =>
        SectionCodesFor(collection).Contains(sectionCode, StringComparer.Ordinal);
}
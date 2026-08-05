using Domain.Common;

namespace Domain.DataCollectionForms;

// A grouping of forms-builder questions within a module. Sections are ordered (SectionNumber) and rendered as
// individual steps when the module is completed. Sections are owned by their FormModule.
public class DcFormSection : IEntity<DcFormSectionId>
{
    public DcFormSectionId Id { get; private set; }
    public FormModuleId FormModuleId { get; private set; }
    public int SectionNumber { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public string? HelpText { get; private set; }
    public bool IsActive { get; private set; }

    private DcFormSection(
        DcFormSectionId id,
        FormModuleId formModuleId,
        int sectionNumber,
        string title,
        string? description,
        string? helpText,
        bool isActive)
    {
        Id = id;
        FormModuleId = formModuleId;
        SectionNumber = sectionNumber;
        Title = title;
        Description = description;
        HelpText = helpText;
        IsActive = isActive;
    }

    internal static DcFormSection Create(
        DcFormSectionId id,
        FormModuleId formModuleId,
        int sectionNumber,
        string title,
        string? description = null,
        string? helpText = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(formModuleId, nameof(formModuleId));
        Guard.NotNullOrEmptyOrWhiteSpace(title, nameof(title));

        return new DcFormSection(
            id,
            formModuleId,
            sectionNumber,
            title.Trim(),
            Normalise(description),
            Normalise(helpText),
            isActive: true);
    }

    public void UpdateDetails(string title, string? description, string? helpText)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(title, nameof(title));

        Title = title.Trim();
        Description = Normalise(description);
        HelpText = Normalise(helpText);
    }

    public void UpdateSectionNumber(int sectionNumber)
    {
        SectionNumber = sectionNumber;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
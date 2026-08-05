using Domain.Common;
using Domain.Users;

namespace Domain.DataCollectionForms;

// A reusable forms-builder module: a named, ordered set of sections and questions authored by DHSC admins and
// assigned to data collections. A module is authored as a Draft; once Published its structure (sections /
// questions) is locked and it can only be Archived. Sections and questions are owned satellites.
public class FormModule : IEntity<FormModuleId>
{
    private readonly List<DcFormSection> _sections = new();
    private readonly List<DcFormQuestion> _questions = new();

    public FormModuleId Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public FormStatus Status { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public UserId? PublishedBy { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<DcFormSection> Sections => _sections.AsReadOnly();
    public IReadOnlyCollection<DcFormQuestion> Questions => _questions.AsReadOnly();

    private FormModule(
        FormModuleId id,
        string code,
        string name,
        string? description,
        int displayOrder,
        bool isActive,
        FormStatus status,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        Status = status;
        PublishedAt = null;
        PublishedBy = null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = null;
        UpdatedAt = null;
    }

    public static FormModule Create(
        FormModuleId id,
        string code,
        string name,
        int displayOrder,
        UserId createdBy,
        string? description = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNullOrEmptyOrWhiteSpace(code, nameof(code));
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new FormModule(
            id,
            code.Trim(),
            name.Trim(),
            Normalise(description),
            displayOrder,
            isActive: true,
            FormStatus.Draft,
            createdBy,
            DateTime.UtcNow);
    }

    public void UpdateDetails(string name, string? description, UserId actor)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(actor, nameof(actor));

        Name = name.Trim();
        Description = Normalise(description);
        Touch(actor);
    }

    public DcFormSection AddSection(
        DcFormSectionId id,
        int sectionNumber,
        string title,
        UserId actor,
        string? description = null,
        string? helpText = null)
    {
        EnsureNotPublished();
        Guard.NotNull(actor, nameof(actor));

        var section = DcFormSection.Create(id, Id, sectionNumber, title, description, helpText);
        _sections.Add(section);
        Touch(actor);
        return section;
    }

    public void RemoveSection(DcFormSectionId sectionId)
    {
        EnsureNotPublished();

        var section = _sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is not null)
        {
            _sections.Remove(section);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public DcFormQuestion AddQuestion(
        DcFormQuestionId id,
        string code,
        string label,
        Domain.Forms.QuestionType questionType,
        int displayOrder,
        UserId actor,
        DcFormSectionId? sectionId = null,
        bool isRequired = false,
        string? hint = null,
        string? helpText = null)
    {
        EnsureNotPublished();
        Guard.NotNull(actor, nameof(actor));

        var question = DcFormQuestion.Create(
            id,
            Id,
            code,
            label,
            questionType,
            displayOrder,
            sectionId,
            isRequired,
            hint,
            helpText);

        _questions.Add(question);
        Touch(actor);
        return question;
    }

    public void RemoveQuestion(DcFormQuestionId questionId)
    {
        EnsureNotPublished();

        var question = _questions.FirstOrDefault(q => q.Id == questionId);
        if (question is not null)
        {
            _questions.Remove(question);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Publish(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));

        if (Status.IsPublished)
        {
            throw new FormModuleAlreadyPublishedException(Id);
        }

        if (!_questions.Any())
        {
            throw new FormModuleEmptyOnPublishException(Id);
        }

        Status = FormStatus.Published;
        PublishedAt = DateTime.UtcNow;
        PublishedBy = actor;
        Touch(actor);
    }

    public void Archive()
    {
        Status = FormStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private void EnsureNotPublished()
    {
        if (Status.IsPublished)
        {
            throw new FormModulePublishedException(Id);
        }
    }

    private void Touch(UserId actor)
    {
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
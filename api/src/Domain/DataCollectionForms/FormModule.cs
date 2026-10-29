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
        DateTime utcNow,
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
            utcNow);
    }

    public void UpdateDetails(string name, string? description, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(actor, nameof(actor));

        Name = name.Trim();
        Description = Normalise(description);
        Touch(actor, utcNow);
    }

    public DcFormSection AddSection(
        DcFormSectionId id,
        int sectionNumber,
        string title,
        UserId actor,
        DateTime utcNow,
        string? description = null,
        string? helpText = null)
    {
        EnsureNotPublished();
        Guard.NotNull(actor, nameof(actor));

        var section = DcFormSection.Create(id, Id, sectionNumber, title, description, helpText);
        _sections.Add(section);
        Touch(actor, utcNow);
        return section;
    }

    public void RemoveSection(DcFormSectionId sectionId, DateTime utcNow)
    {
        EnsureNotPublished();

        var section = _sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is not null)
        {
            _sections.Remove(section);
            UpdatedAt = utcNow;
        }
    }

    public DcFormQuestion AddQuestion(
        DcFormQuestionId id,
        string code,
        string label,
        Domain.Forms.QuestionType questionType,
        int displayOrder,
        UserId actor,
        DateTime utcNow,
        DcFormSectionId? sectionId = null,
        bool isRequired = false,
        string? hint = null,
        string? helpText = null,
        string? errorMessage = null,
        int? maxLength = null,
        string? maxLengthErrorMessage = null)
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
            helpText,
            errorMessage,
            maxLength,
            maxLengthErrorMessage);

        _questions.Add(question);
        Touch(actor, utcNow);
        return question;
    }

    public void RemoveQuestion(DcFormQuestionId questionId, DateTime utcNow)
    {
        EnsureNotPublished();

        var question = _questions.FirstOrDefault(q => q.Id == questionId);
        if (question is not null)
        {
            _questions.Remove(question);
            UpdatedAt = utcNow;
        }
    }

    // BLUM-9102: the delete of a question a saved version published. The row stays, inactive, so readers of the live
    // form can still label the answers older reports gave, and it leaves its section so the section can be deleted.
    // Returns false when it was already withdrawn, so the caller can skip a second audit row.
    public bool WithdrawQuestion(DcFormQuestionId questionId, DateTime utcNow)
    {
        EnsureNotPublished();

        var question = _questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null || (!question.IsActive && question.SectionId is null))
        {
            return false;
        }

        question.Deactivate();
        question.SetSection(null);
        UpdatedAt = utcNow;
        return true;
    }

    public void Publish(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        if (Status.IsPublished)
        {
            throw new FormModuleAlreadyPublishedException(Id);
        }

        // An inactive question is one no LA sees (a withdrawn one keeps its row), so it does not make a form non-empty.
        if (!_questions.Any(q => q.IsActive))
        {
            throw new FormModuleEmptyOnPublishException(Id);
        }

        Status = FormStatus.Published;
        PublishedAt = utcNow;
        PublishedBy = actor;
        Touch(actor, utcNow);
    }

    // BLUM-9102: reopens a published module for editing. Its last publish is kept as a FormModuleVersion, so the draft
    // can change freely and the next Publish decides whether it is a new version. PublishedAt/PublishedBy keep the
    // last publish: they are what tells a reopened draft from one that was never published.
    public void StartDraft(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        if (!Status.IsPublished)
        {
            throw new FormModuleNotPublishedException(Id);
        }

        Status = FormStatus.Draft;
        Touch(actor, utcNow);
    }

    public void Archive(DateTime utcNow)
    {
        Status = FormStatus.Archived;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    private void EnsureNotPublished()
    {
        if (Status.IsPublished)
        {
            throw new FormModulePublishedException(Id);
        }
    }

    private void Touch(UserId actor, DateTime utcNow)
    {
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
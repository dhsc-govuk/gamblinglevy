using Application.Submissions.Protocol;

namespace Application.MaturityAssessments.Protocol;

// One maturity-assessment question in the wire shape the quarterly report already uses. Deriving from
// ResolvedQuestion is what lets SubmissionAnswerValidator (visibility / required / options / max length) and
// ReportAnswerPresenter take the maturity list unchanged; the extra members carry what the annual assessment adds:
// the owning section and page (with titles), the page-level visibility rule, the matrix group heading, the
// "Funded roles" repeater's second option list, and the checkbox "None" exclusive option.
public record MaturityResolvedQuestion(
    Guid Id,
    string Code,
    string Label,
    string? Hint,
    string QuestionType,
    bool IsRequired,
    int DisplayOrder,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<ResolvedQuestionOption> Options,
    string? PrefillSource,
    string? ErrorMessage,
    string Section,
    int? MaxLength,
    string? MaxLengthErrorMessage,
    string SectionTitle,
    string Page,
    string PageTitle,
    MaturityPageVisibility? PageVisibleWhen,
    string? GroupLabel,
    IReadOnlyList<ResolvedQuestionOption> SecondaryOptions,
    string? ExclusiveOptionValue)
    : ResolvedQuestion(
        Id,
        Code,
        Label,
        Hint,
        QuestionType,
        IsRequired,
        DisplayOrder,
        ConditionalQuestionCode,
        ConditionalValue,
        Options,
        MinValue: null,
        MaxValue: null,
        IsPercentage: false,
        PrefillSource,
        ErrorMessage,
        Section,
        MaxLength,
        MaxLengthErrorMessage)
{
    public bool IsRoles => QuestionType == MaturityFieldTypes.Roles;
}
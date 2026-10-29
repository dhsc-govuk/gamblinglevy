using Api.Dtos;
using Application.Submissions.Protocol;

namespace Api.Services.ReportSnapshot;

// The one mapping from a resolved quarterly-report question to the renderer-shaped DTO. Shared by the report screens
// (QuarterlyReportControllerService) and the activity-record export (BLUM-7648), so the percentage "%" suffix — which
// the presenter appends to an exported answer — is decided in one place.
public static class ReportQuestionMapper
{
    public static ReportQuestionDto ToDto(ResolvedQuestion question) => new(
        question.Id,
        question.Code,
        question.Label,
        question.Hint,
        question.QuestionType,
        question.IsRequired,
        question.DisplayOrder,
        question.ConditionalQuestionCode,
        question.ConditionalValue,
        question.Options.Select(o => new ReportQuestionOptionDto(o.Value, o.Label)).ToList(),
        question.MinValue,
        question.MaxValue,
        question.IsPercentage ? "%" : null,
        question.Section,
        question.ErrorMessage,
        question.MaxLength,
        question.MaxLengthErrorMessage)
    {
        WithdrawnOptions = question.WithdrawnOptions.Select(o => new ReportQuestionOptionDto(o.Value, o.Label)).ToList(),
    };
}
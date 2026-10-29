using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;

namespace Application.MaturityAssessments.Prefill;

public sealed record MaturityPrefilledAnswer(string QuestionCode, string Value, AnswerSource Source);

// Computes the answers a freshly started maturity assessment opens with (BLUM-7856). Precedence per question: the
// organisation's latest SUBMITTED quarterly reports (green rows of the mapping file) → its most recent submitted
// maturity assessment for an earlier financial year → the imported 2025 survey baseline → nothing. Called by
// StartMaturityAssessmentCommand, which persists the rows, and again by MaturityPrefillRefresher whenever the
// organisation submits a quarterly report (BLUM-8927), which writes only the quarterly values and only where the
// LA has not saved the page. Never an overlay on a read: the LA owns every value once it is stored (the
// ActivityResultCarryForward "write, don't overlay" reasoning).
public interface IMaturityPrefillService
{
    Task<IReadOnlyList<MaturityPrefilledAnswer>> BuildAsync(
        Organisation organisation,
        Collection collection,
        IReadOnlyList<MaturityResolvedQuestion> questions,
        CancellationToken cancellationToken);
}
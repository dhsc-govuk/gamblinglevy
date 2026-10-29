using Domain.MaturityAssessments;

namespace Application.Common.Interfaces.Repositories;

public interface IMaturityAssessmentAnswerRepository
{
    Task<IReadOnlyList<MaturityAssessmentAnswer>> GetForAssessmentAsync(MaturityAssessmentId assessmentId, CancellationToken cancellationToken = default);

    // Replaces the answers for only the given question codes (delete-then-insert), leaving every other answer intact
    // — each page submits its own codes, so saving one page never wipes another's. `answers` holds the non-empty
    // values to insert; a submitted code with no matching answer is simply cleared.
    Task ReplaceForQuestionsAsync(MaturityAssessmentId assessmentId, IReadOnlyList<string> questionCodes, IReadOnlyList<MaturityAssessmentAnswer> answers, CancellationToken cancellationToken = default);

    // The Start command's pre-fill write.
    Task InsertAsync(IReadOnlyList<MaturityAssessmentAnswer> answers, CancellationToken cancellationToken = default);

    // The pre-fill refresh's write (BLUM-8927): removes the given rows BY ID and inserts their replacements in one
    // transaction. By id rather than by question code on purpose — if the LA saved the page since the rows were
    // read, those ids are gone, and the call throws and rolls back, leaving the LA's answers as they are. Two
    // checks, because the save has two outcomes: a value the LA kept is a new row that the unique (assessment,
    // question code) index refuses the insert against; a value the LA CLEARED leaves no row, so the delete removing
    // fewer rows than it was given is the only sign of it.
    Task ReplaceRowsAsync(IReadOnlyList<MaturityAssessmentAnswerId> staleIds, IReadOnlyList<MaturityAssessmentAnswer> answers, CancellationToken cancellationToken = default);
}
using Application.Common.Permissions;
using Application.MaturityAssessments.Snapshots;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IMaturityAssessmentSnapshotService
{
    // Renders the assessment's sections, pages and VISIBLE questions from the live saved answers, applying the
    // conditional / page-visibility filter and answer rendering. Called at submit time to freeze the assessment.
    // None — never a throw — when the assessment or collection is not eligible: the controller builds the snapshot
    // BEFORE the submit command re-checks eligibility (BLUM-7216).
    Task<Option<MaturityAssessmentSnapshotDocument>> BuildAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    string Serialize(MaturityAssessmentSnapshotDocument snapshot);

    MaturityAssessmentSnapshotDocument? Deserialize(string json);
}
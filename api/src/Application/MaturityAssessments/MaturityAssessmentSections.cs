using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Domain.Collections;
using Domain.MaturityAssessments;

namespace Application.MaturityAssessments;

// The sections one LA's assessment is made of (BLUM-8093). While it is open it follows the collection's CURRENT
// forms, so an admin's edit reaches work under way. Once submitted it is frozen: the collection's forms can still
// change while the window is open, and neither a form added afterwards (a "Not started" row on a read-only
// assessment) nor one removed afterwards (a completed section vanishing) may reshape what was submitted.
public static class MaturityAssessmentSections
{
    public static IReadOnlyList<string> For(MaturityAssessment assessment, Collection collection) =>
        For(collection, assessment.SubmittedAt, assessment.SnapshotJson, assessment.Sections.Select(s => s.SectionCode));

    // A submitted assessment's sections are its snapshot's — exactly what was rendered and frozen at submit. Only if
    // that is missing or unreadable does it fall back to the assessment's own section rows, in catalogue order.
    public static IReadOnlyList<string> For(
        Collection collection,
        DateTime? submittedAt,
        string? snapshotJson,
        IEnumerable<string> sectionRowCodes)
    {
        if (submittedAt is null)
        {
            return MaturityCollectionForms.SectionCodesFor(collection);
        }

        var snapshot = MaturityAssessmentSnapshotJson.TryRead(snapshotJson);
        if (snapshot is { Sections.Count: > 0 })
        {
            return snapshot.Sections.Select(s => s.Code).ToList();
        }

        var rows = sectionRowCodes.ToHashSet(StringComparer.Ordinal);
        return MaturityProtocol.SectionCodes.Where(rows.Contains).ToList();
    }
}
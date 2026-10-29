using Application.Common.Interfaces.Queries;
using Domain.Collections;
using Domain.Organisations;

namespace Application.Submissions;

// The organisations assigned to a collection that have NOT submitted (BLUM-7571, shared with BLUM-8155). Extracted
// out of CollectionReminderNotifier because BLUM-8155's LA "window closed" notification needs the exact same
// recipe: assigned, not suspended, no finalised submission.
public class OutstandingSubmissionResolver(
    ISubmissionQueries submissionQueries,
    IMaturityAssessmentQueries maturityAssessmentQueries)
{
    public async Task<IReadOnlyList<Organisation>> ResolveAsync(
        Collection collection,
        IReadOnlyList<Organisation> allOrganisations,
        CancellationToken cancellationToken)
    {
        // The single place that knows "submitted by all LAs" means every Local Authority organisation, VCSEs
        // excluded, with no collection_local_authorities rows at all.
        var assigned = SubmissionAssignment.ResolveAssignedOrganisations(collection, allOrganisations);

        // A suspended organisation's users are blocked from the API by OrganisationSuspendedMiddleware, so they
        // physically cannot submit; "please submit" would be unactionable. Filtered HERE rather than inside
        // SubmissionAssignment, because the DHSC status screens and the consolidated export legitimately still
        // report on a suspended organisation. Invited organisations are kept — an invited org with active users
        // can submit, and one without simply has no contacts to mail.
        //
        // OnboardingStatus is a ValueObject with no operator==, so compare by value via Equals.
        assigned = assigned
            .Where(organisation => !organisation.OnboardingStatus.Equals(OnboardingStatus.Suspended))
            .ToList();

        // SubmittedAt is the only "has submitted" test in the codebase — an existing row with a null SubmittedAt
        // is a draft, and a draft still needs reminding. A maturity-assessment window reads its own aggregate
        // (BLUM-7858): before that flow existed no row could ever exist, so every assigned LA was reminded at
        // every trigger point forever.
        var submitted = SubmissionEligibility.IsMaturityAssessment(collection)
            ? (await maturityAssessmentQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken))
                .Where(row => row.SubmittedAt is not null)
                .Select(row => row.OrganisationId)
                .ToHashSet()
            : (await submissionQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken))
                .Where(row => row.SubmittedAt is not null)
                .Select(row => row.OrganisationId)
                .ToHashSet();

        return assigned.Where(organisation => !submitted.Contains(organisation.Id.Value)).ToList();
    }
}
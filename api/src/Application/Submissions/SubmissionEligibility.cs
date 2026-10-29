using Domain.Collections;
using Domain.Organisations;

namespace Application.Submissions;

// Shared eligibility rules for the LA-facing quarterly-report surface: a report can only be created or edited
// against a quarterly collection that is currently Open (derived status) and has the caller's organisation in its
// assigned local authorities.
public static class SubmissionEligibility
{
    // Type is null while the collection is an unanswered draft (BLUM-6625) — a draft is never quarterly-eligible.
    public static bool IsQuarterly(Collection collection) => collection.Type?.IsQuarterly ?? false;

    // The annual maturity assessment (BLUM-7855). Same draft rule as IsQuarterly: a draft has no type and is never
    // eligible. The two are mutually exclusive, and every surface that branches on collection type should test one
    // of these rather than negating the other — a legacy row can have no type at all.
    public static bool IsMaturityAssessment(Collection collection) => collection.Type?.IsMaturityAssessment ?? false;

    // CollectionStatus is a ValueObject (no operator==) — compare via Equals.
    public static bool IsOpen(Collection collection, DateTime utcNow) => collection.GetStatus(utcNow).Equals(CollectionStatus.Open);

    // A collection is assigned to an organisation either by an explicit assignment row, or — only for
    // Local-Authority organisations — by the "submitted by all LAs" flag (saved with no assignment rows).
    // This is the authoritative, type-aware assignment gate: CollectionRepository.GetAssignedToOrganisationAsync
    // is only a coarse prefilter, and every caller re-checks here. OrgType is a ValueObject with no ==
    // operator, so compare via Equals.
    public static bool IsAssignedTo(Collection collection, Organisation organisation) =>
        (collection.IsSubmittedByAllLas == true && organisation.Type.Equals(OrgType.LocalAuthority))
        || collection.LocalAuthorities.Any(la => la.LocalAuthorityId == organisation.Id);
}
using Domain.Collections;
using Domain.Organisations;

namespace Application.Submissions;

public static class SubmissionAssignment
{
    // The organisations a collection is assigned to. "Submitted by all LAs" is saved with no explicit assignment rows
    // and means every Local-Authority organisation (VCSEs are never implicitly assigned); otherwise the explicit
    // assignment rows are resolved to their organisations. Mirrors SubmissionEligibility.IsAssignedTo. OrgType is a
    // ValueObject with no == operator, so compare via Equals. Shared by the DHSC submission-status list and export.
    public static IReadOnlyList<Organisation> ResolveAssignedOrganisations(
        Collection collection,
        IReadOnlyList<Organisation> allOrganisations)
    {
        if (collection.IsSubmittedByAllLas == true)
        {
            return allOrganisations
                .Where(o => o.Type.Equals(OrgType.LocalAuthority))
                .ToList();
        }

        var assignedIds = collection.LocalAuthorities
            .Select(la => la.LocalAuthorityId.Value)
            .ToHashSet();

        return allOrganisations
            .Where(o => assignedIds.Contains(o.Id.Value))
            .ToList();
    }

    // The organisations the DHSC submission-visibility screens should report on: every currently-assigned org PLUS any
    // org that has a submission row for the window even if it is no longer (or was never) assigned. Assignment can
    // change after a report is started/submitted (SetLocalAuthorities removes assignment rows but not the submission),
    // so resolving by assignment alone silently drops real submission data. Deduped by org id; an org id with a
    // submission but no matching organisation row is skipped. Callers order by name (this helper does not).
    public static IReadOnlyList<Organisation> ResolveReportableOrganisations(
        Collection collection,
        IReadOnlyList<Organisation> allOrganisations,
        IEnumerable<Guid> organisationIdsWithSubmissions)
    {
        var assigned = ResolveAssignedOrganisations(collection, allOrganisations);
        var seen = assigned.Select(o => o.Id.Value).ToHashSet();

        var result = new List<Organisation>(assigned);
        foreach (var organisationId in organisationIdsWithSubmissions)
        {
            if (!seen.Add(organisationId))
            {
                continue;
            }

            var organisation = allOrganisations.FirstOrDefault(o => o.Id.Value == organisationId);
            if (organisation is not null)
            {
                result.Add(organisation);
            }
        }

        return result;
    }
}
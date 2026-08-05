using Api.Dtos;
using Application.Common.Permissions;
using LanguageExt;

namespace Api.Services.Abstract;

// LA-facing submission history + detail reads (BLUM-6097 / BLUM-6098). Read-only and org-scoped — the org is taken
// from the caller's Permission, never the route, so an LA only ever sees its own submissions. The hosting controller
// is [Authorize(Roles = Role.AnyOrganisationMember)]. Mirrors IDhscSubmissionsControllerService, re-scoped from
// "all orgs for one collection" to "all collections for the caller's one org".
public interface ILaSubmissionsControllerService
{
    // Every quarterly collection the caller's organisation is assigned to (or has a submission for), with its derived
    // collection + submission status. Drives both the "Open data collections" landing (Open rows) and the
    // "Previously submitted reports" list (rows with a SubmittedAt). Empty when the caller has no organisation.
    Task<IReadOnlyList<MyCollectionSummaryDto>> GetMyCollectionsAsync(Permission permission, CancellationToken cancellationToken);

    // The caller's own submission detail for one collection. None when the collection does not exist, is not a
    // quarterly report, or the caller's organisation is neither assigned to it nor has a submission for it (prevents
    // probing arbitrary collection ids). HasSubmission == false is the graceful "not submitted yet" empty state.
    Task<Option<MySubmissionDetailDto>> GetMySubmissionDetailAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);
}
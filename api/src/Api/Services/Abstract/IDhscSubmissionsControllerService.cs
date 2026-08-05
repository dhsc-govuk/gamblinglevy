using Api.Dtos;
using LanguageExt;

namespace Api.Services.Abstract;

// DHSC submission-visibility reads (BLUM-6095 / BLUM-6445). Read-only and DHSC-scoped — the hosting controller is
// [Authorize(Roles = Role.Admin)]. Both return Option.None for an unknown collection (or, for the detail, an
// organisation not assigned to the collection) so the controller can fold to 404.
public interface IDhscSubmissionsControllerService
{
    // The per-organisation submission status list for a reporting window: one row per assigned organisation with its
    // derived status (Not started / In progress / Completed / Overdue). None when the collection does not exist.
    Task<Option<CollectionSubmissionSummaryDto>> GetOrgStatusesAsync(Guid collectionId, CancellationToken cancellationToken);

    // A single organisation's submission detail for a reporting window. None when the collection does not exist or the
    // organisation is not assigned to it (prevents probing arbitrary org ids). HasSubmission == false is the graceful
    // "no submission made for this window" empty state.
    Task<Option<OrgSubmissionDetailDto>> GetOrgDetailAsync(Guid collectionId, Guid organisationId, CancellationToken cancellationToken);
}
using Api.Dtos;
using Domain.Collections;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IDataCollectionsControllerService
{
    // Maps a domain Collection to its full DTO, enriching with Local Authority and form-module names and the
    // derived lifecycle status.
    Task<CollectionDto> ToDtoAsync(Collection collection, CancellationToken cancellationToken);

    // All non-deleted collections — feeds the collections list screen, which groups by derived status client-side.
    Task<IReadOnlyList<CollectionSummaryDto>> ListAsync(CancellationToken cancellationToken);

    Task<Option<CollectionDto>> GetAsync(Guid collectionId, CancellationToken cancellationToken);

    // Soft-deleted collections — feeds the "Deleted collections" page.
    Task<IReadOnlyList<CollectionSummaryDto>> DeletedAsync(CancellationToken cancellationToken);

    // The active form modules as options for the collection form-assignment picker.
    Task<IReadOnlyList<CollectionFormOptionDto>> GetFormOptionsAsync(CancellationToken cancellationToken);

    // The Local Authorities that can be assigned to a collection.
    Task<IReadOnlyList<ReportingLaDto>> GetReportingLasAsync(CancellationToken cancellationToken);
}
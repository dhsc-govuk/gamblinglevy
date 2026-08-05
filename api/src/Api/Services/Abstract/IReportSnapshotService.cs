using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IReportSnapshotService
{
    // Renders the report's section content (activities, funding, strategic feedback) from the LIVE read side, applying
    // the conditional-visibility filter and answer rendering. Called at submit time to freeze the report, and as the
    // export fallback for legacy submissions finalised before snapshots existed. None when there is nothing to render.
    Task<Option<ReportSnapshotDocument>> BuildAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // Shared JSON (de)serialisation so the value persisted at submit time round-trips exactly at export time.
    string Serialize(ReportSnapshotDocument snapshot);

    ReportSnapshotDocument? Deserialize(string json);
}
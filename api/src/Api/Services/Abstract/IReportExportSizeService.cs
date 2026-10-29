using LanguageExt;

namespace Api.Services.Abstract;

// The byte length a detail screen captions its download with ("ZIP, 389 KB"), measured by building the file through the
// call its download endpoint makes (BLUM-9018). Null when there is no file, or when building it failed.
public interface IReportExportSizeService
{
    Task<long?> MeasureAsync(ReportExportSizeKey key, Func<Task<Option<ReportExportFile>>> build);
}

// One finalised report as one screen sees it. The surface is part of the key because the LA and DHSC calls can build
// different files for the same report (only the LA's may fall back to a live render), and SubmittedAt because the file
// only exists — and is only measured — once the report is finalised.
public record ReportExportSizeKey(string Surface, Guid CollectionId, Guid OrganisationId, DateTime SubmittedAt);
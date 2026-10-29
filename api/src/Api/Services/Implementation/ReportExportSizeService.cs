using Api.Services.Abstract;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Caching.Memory;

namespace Api.Services.Implementation;

// The byte length a detail screen captions its download with ("ZIP, 389 KB", the GDS attachment pattern), so a user
// on a slow or metered connection can decide before committing (BLUM-9018).
//
// A ZIP's length is only known by building it, so the caller passes the exact call its download endpoint makes: the
// caption then matches the file whatever the organisation is called, whichever flags are on and however the CSV
// writers have changed since the snapshot was frozen. A size persisted at submit time could not promise that. Never
// measure on a list: building a ZIP for every organisation in a window on every load is what made BLUM-7216 take the
// size away.
//
// The answer is cached per report for a few minutes. The detail reads sit on the Read rate-limit budget, not the
// Export one the download is held to, so without the cache a caller reloading a detail screen could make the server
// build ZIPs far faster than the export route allows. A frozen report's file does not change, so the cache costs
// nothing in accuracy beyond an organisation rename showing up a few minutes late; a flag change restarts the app and
// starts with an empty cache. The relative expiry is judged against the system clock, which is what IMemoryCache
// needs (see FixedWindowCounter for the absolute-expiry trap).
//
// A failure is logged as a warning and reads as "size unknown", so the screen still loads and the caption falls back to
// the format alone — BLUM-7216's other objection, that measuring gave the read a way to fail. The failure is cached as
// well, so a broken export is not rebuilt and logged on every view; it surfaces as an error when someone downloads it.
public class ReportExportSizeService(IMemoryCache cache, ILogger<ReportExportSizeService> logger) : IReportExportSizeService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    public async Task<long?> MeasureAsync(ReportExportSizeKey key, Func<Task<Option<ReportExportFile>>> build)
    {
        if (cache.TryGetValue(key, out Measurement? cached) && cached is not null)
        {
            return cached.Bytes;
        }

        long? bytes;
        try
        {
            var file = await build();
            bytes = file.IsNone ? null : file.ValueUnsafe().Content.LongLength;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to measure the report export for collection {CollectionId}", key.CollectionId);
            bytes = null;
        }

        cache.Set(key, new Measurement(bytes), CacheFor);
        return bytes;
    }

    // A wrapper so a cached "no size" is told apart from a cache miss.
    private sealed record Measurement(long? Bytes);
}
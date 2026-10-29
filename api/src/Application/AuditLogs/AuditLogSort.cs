namespace Application.AuditLogs;

/// <summary>
/// The orderings the audit-log list endpoints accept, surfaced as the "Sort by" control on the
/// Audit log page (BLUM-7775).
///
/// A read concern rather than a domain value object: nothing persists it, and the audit trail itself
/// is ordered by when things happened regardless of how anyone chooses to page through it. Exposed as
/// IsSupported so the controllers can reject an unrecognised value through QueryGuard.AllowedValue
/// instead of silently falling back to Newest — a bookmarked ?sort=bogus that quietly answers with the
/// opposite order is worse than a 400.
/// </summary>
public static class AuditLogSort
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";

    public static bool IsSupported(string? value) =>
        value is Newest or Oldest;
}
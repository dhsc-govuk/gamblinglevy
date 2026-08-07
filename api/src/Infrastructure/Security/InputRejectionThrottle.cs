using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Security;

/// <summary>The outcome of asking whether a security rejection may be written to audit_logs.</summary>
/// <param name="Admitted">False when the caller should degrade to telemetry and logs only.</param>
/// <param name="SuppressedSincePrevious">
/// How many rejections were suppressed since the last admitted one, carried onto the admitted row so a
/// burst stays visible in the audit trail as a number.
/// </param>
public readonly record struct InputRejectionThrottleDecision(bool Admitted, int SuppressedSincePrevious);

/// <summary>
/// Bounds how many audit_logs rows a rejected request may cause (BLUM-7240 AC8).
///
/// The counting itself now lives in <see cref="AuditFloodThrottle"/>, which BLUM-7239 needed for the same
/// job against a different kind of event. This type stays because it owns the two things that are specific
/// to input validation and have no business in a shared primitive: the scope name that keeps this trail's
/// budget separate from every other one, and the one-hour window that
/// <see cref="InputValidationSettings.MaxAuditRowsPerActorPerHour"/> is named after.
///
/// The lessons that used to be listed here — bucket on the route template, never charge a suppressed hit,
/// plain ints under a gate, and never hand IMemoryCache an absolute expiry derived from the injected clock —
/// moved with the mechanics and are documented on <see cref="FixedWindowCounter"/>.
///
/// The constructor signature is unchanged on purpose. InputRejectionThrottleTests exercises this class
/// directly and passes against the extraction without a single edit, which is what makes that suite the
/// regression proof rather than a suite that was rewritten until it agreed.
/// </summary>
public sealed class InputRejectionThrottle
{
    /// <summary>
    /// Namespaces this trail's counters, including the global one. Sharing a global key with another kind of
    /// security event would let a flood of that event blank this trail.
    /// </summary>
    public const string Scope = "input_rejection_audit";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly AuditFloodThrottle throttle;

    public InputRejectionThrottle(IMemoryCache cache, IDateTimeProvider dateTimeProvider)
    {
        this.throttle = new AuditFloodThrottle(cache, new FixedWindowCounter(dateTimeProvider));
    }

    public InputRejectionThrottleDecision Reserve(string actorKey, InputValidationSettings limits)
    {
        var decision = this.throttle.Reserve(
            Scope,
            actorKey,
            new AuditThrottleLimits(
                limits.MaxAuditRowsPerActorPerHour,
                limits.MaxAuditRowsPerHour,
                Window));

        return new InputRejectionThrottleDecision(decision.Admitted, decision.SuppressedSincePrevious);
    }
}
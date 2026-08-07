namespace Application.Mfa;

/// <summary>
/// How many seconds a refused MFA attempt should be told to wait (BLUM-7239).
///
/// Pure and static, in the same folder and for the same reason as <see cref="MfaCodeFormat"/>: it is
/// arithmetic with two edges that matter, and it should be testable without standing up an HttpContext.
/// </summary>
public static class MfaRetryAfter
{
    /// <summary>
    /// Seconds until <paramref name="until"/>, clamped into [1, <paramref name="ceilingSeconds"/>].
    ///
    /// Never zero, because a Retry-After of 0 invites a conforming client straight back into a tight retry
    /// loop against the limit it just hit. Never longer than the control that produced it, because
    /// overstating it would keep a legitimate user out past the point the service would have let them back
    /// in. Rounded up, because rounding down advertises the window as over while it is still closed.
    /// </summary>
    public static int Seconds(DateTime? until, DateTime now, int ceilingSeconds)
    {
        var ceiling = ceilingSeconds < 1 ? 1 : ceilingSeconds;

        if (until is null)
        {
            return 1;
        }

        var remaining = (until.Value - now).TotalSeconds;

        if (remaining <= 1)
        {
            return 1;
        }

        return Math.Min((int)Math.Ceiling(remaining), ceiling);
    }
}
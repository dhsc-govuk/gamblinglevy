namespace Application.Mfa;

/// <summary>
/// The shapes a TOTP code and a recovery code can legitimately take.
///
/// Deliberately not expressed as a FluentValidation rule. A validator runs before the handler, so
/// rejecting a malformed code there would return 400 without ever reaching the branch that calls
/// MfaRateLimitService.RecordAttempt, UserMfa.RecordFailedAttempt and the mfa.verify.failed audit append.
/// The five-per-minute limiter and the lockout counter would then be blind to a burst of malformed
/// traffic, and an existing security trail would quietly disappear from a ticket whose whole point is to
/// add one. It would also put a second error envelope on a surface where the client only knows the MFA_*
/// codes.
///
/// So the validators bound the length — that is the denial-of-service concern and it does belong at the
/// boundary — and the exact shape is checked here, inside the handlers, on the same failure branch as a
/// wrong code (BLUM-7240 / CWE-20).
/// </summary>
public static class MfaCodeFormat
{
    // TotpService constructs Otp.NET's Totp with totpSize: 6, so the accepted space is exactly the ten
    // to the sixth six-digit strings.
    private const int TotpDigits = 6;

    // RecoveryCodeService generates twelve characters and formats them XXXX-XXXX-XXXX.
    private const int RecoveryCodeCharacters = 12;

    public static bool IsTotpShape(string? code)
    {
        if (code is null || code.Length != TotpDigits)
        {
            return false;
        }

        // Written as an explicit digit range rather than char.IsDigit, which is true of every Unicode
        // decimal digit — Arabic-Indic numerals would pass a check that claims to be an allow-list and
        // then fail verification anyway.
        foreach (var character in code)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Accepts exactly what RecoveryCodeService.VerifyRecoveryCode can normalise into a candidate: it
    /// strips hyphens and upper-cases, so hyphen placement is not part of the shape.
    ///
    /// The alphabet is checked as ASCII alphanumerics rather than as the generator's reduced set (which
    /// omits I, O, 0 and 1 to avoid transcription errors). The security value is entirely in the length
    /// bound; a stricter alphabet would only turn an O-for-0 slip into "wrong format" instead of
    /// "invalid code", for no gain.
    /// </summary>
    public static bool IsRecoveryCodeShape(string? code)
    {
        if (code is null)
        {
            return false;
        }

        var characters = 0;

        foreach (var character in code)
        {
            if (character == '-')
            {
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }

            characters++;

            if (characters > RecoveryCodeCharacters)
            {
                return false;
            }
        }

        return characters == RecoveryCodeCharacters;
    }
}
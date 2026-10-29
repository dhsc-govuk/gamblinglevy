using LanguageExt;

namespace Application.Common.Interfaces;

// Tells a user their temporary password after an admin enables or resets password login for their account
// (BLUM-XXXX). Unlike every other Notify-only email type here, a failed send cannot be logged and swallowed:
// the temporary password this email carries is the ONLY way the user finds out how to sign in, and by the time
// SendTemporaryPasswordAsync runs the caller has already replaced their Entra credential and revoked every
// session. Swallowing a failure here means reporting success on a request that just locked the user out.
// EnableUserPasswordLoginCommand/ResetUserPasswordCommand (sent by both the DHSC and the LA routes) therefore
// check IsConfigured BEFORE touching Entra at all, and surface a send failure that happens anyway as an error
// to the caller rather than a logged warning.
public interface ITemporaryPasswordEmailService
{
    // True when a send would actually be attempted (API key, template id and client URL all present). Checked
    // before EnablePasswordLogin/ResetPassword ever runs, so a missing template — the state every environment
    // ships in until DHSC creates it — refuses the whole request instead of changing the user's credential and
    // then discovering there is no way to tell them what it is.
    bool IsConfigured { get; }

    Task<Either<Exception, Unit>> SendTemporaryPasswordAsync(
        string email,
        TemporaryPasswordNotification notification,
        CancellationToken cancellationToken);
}

public record TemporaryPasswordNotification(string RecipientName, string TemporaryPassword);
using LanguageExt;

namespace Application.Common.Interfaces;

/// <summary>
/// Tells a user that their account was signed in to on a device that was not already signed in
/// (BLUM-7241 / CWE-613). The pen test's point was that an attacker holding valid credentials could run a
/// session alongside the real user's indefinitely and unseen; this is the out-of-band channel that makes it
/// seen, so it must reach the user even when the attacker controls every browser session.
///
/// Best-effort in exactly the sense ISubmissionConfirmationEmailService is: it is sent on the verify path,
/// after the session already exists, so a failed send is logged and never fails the sign-in.
/// </summary>
public interface INewSignInEmailService
{
    Task<Either<Exception, Unit>> SendNewSignInAsync(
        string email,
        NewSignInNotification notification,
        CancellationToken cancellationToken);
}

/// <summary>
/// Everything the email needs, already resolved by the caller. DeviceLabel and IpAddress originate in
/// request headers and are therefore attacker-controlled — the service HTML-encodes them.
/// </summary>
public record NewSignInNotification(
    string UserName,
    string DeviceLabel,
    string IpAddress,
    DateTime SignedInAt);
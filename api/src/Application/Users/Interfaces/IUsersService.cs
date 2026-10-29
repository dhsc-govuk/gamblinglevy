using Domain.ValueObjects;
using LanguageExt;

namespace Application.Users.Interfaces;

public interface IUsersService
{
    Task<Option<string>> FindById(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Looks the account up by its mail address. None means the directory answered and holds no such
    /// account; a Left means the directory refused the lookup, which says nothing about whether the account
    /// exists — a caller must not go on to <see cref="Add"/> on the strength of it.
    /// </summary>
    Task<Either<Exception, Option<string>>> FindByEmail(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the CIAM account, always enabled. Local <see cref="Domain.ValueObjects.UserStatus"/> — not
    /// the Entra account — is what decides whether a user may use the service (BLUM-6386); a user created
    /// with activation deferred still has to be able to reach the sign-in flow, or they are refused by
    /// Microsoft's own page instead of ours and told nothing about why.
    /// </summary>
    Task<Either<Exception, string>> Add(
        Name userName,
        string email,
        CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> Update(
        string id,
        Name userName,
        string email,
        bool passwordLoginEnabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Converts the account to Entra's emailAddress-shaped local identity (always tenant-scoped, unlike the
    /// federated/mail shape Add uses, which is subject to cross-tenant home-directory resolution for a domain
    /// independently verified elsewhere — see BLUM-XXXX) and sets a fresh PasswordProfile with
    /// ForceChangePasswordNextSignIn = true. Idempotent — safe to call on an already-converted account.
    ///
    /// Graph refuses to add a non-federated (emailAddress) identity via PATCH to an account whose existing
    /// identities are all federated/social or absent entirely — which describes every account Add() creates,
    /// so this recreates the Entra account outright on essentially every FIRST call for a given user, not
    /// just the rare case of an account with no identity at all. Only an account that already has an
    /// emailAddress identity from a previous EnablePasswordLogin call skips the recreate and goes straight to
    /// a PATCH. The return value is the new Entra object id when a recreate happened, or None when the
    /// existing account was patched — the caller must persist a Some result as the new User.SubId (see
    /// User.UpdateSubId), since the old id stops resolving once this runs.
    /// </summary>
    Task<Either<Exception, Option<string>>> EnablePasswordLogin(
        string id,
        Name userName,
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-verifies/re-merges the emailAddress identity shape (same as <see cref="EnablePasswordLogin"/>) and
    /// rotates the password. Kept as a distinct method from EnablePasswordLogin for interface/DI clarity even
    /// though the two now share an implementation — callers must only reach this after confirming
    /// <see cref="Domain.Users.User.PasswordLoginEnabled"/> is already true.
    /// </summary>
    Task<Either<Exception, Unit>> ResetPassword(
        string id,
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deliberately unused since BLUM-6386, and not what deactivation should call. Disabling the CIAM
    /// account moves the refusal onto Microsoft's own sign-in page, whose copy Entra External ID does not
    /// let us customise — the user is told sign-in failed and nothing more, which is the bug. Deactivation
    /// is enforced by UserDeactivatedMiddleware instead. Kept for a future control that genuinely needs the
    /// credential itself killed rather than the account refused.
    /// </summary>
    Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Called on reactivation as a repair rather than a mechanism: nothing disables accounts any more, but
    /// users deactivated before BLUM-6386 still carry AccountEnabled = false in the tenant, and this is
    /// what clears it.
    /// </summary>
    Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken);
}
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Audit;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;

namespace Application.Users;

/// <summary>
/// Turns one account off — the sequence behind every way a user is deactivated.
///
/// There are three: DeactivateUserCommand, which DHSC (BLUM-6014) and an LA admin (BLUM-6021) both send for one
/// user; the LA edit form switching a user to deactivated; and DHSC deactivating an organisation, which
/// deactivates each of its active users in the same operation (BLUM-7667). The cascade is why it is shared: the
/// ticket asks it to do exactly what an individual deactivation does, and a security sequence written more than
/// once is one that drifts.
///
/// Who may be deactivated is not decided here. Already-deactivated, self and last-admin
/// (<see cref="LastActiveAdminGuard"/>) are rules about a request for one user, so those callers run them — and
/// the cascade must not, because an organisation's last admin is exactly who it is there to deactivate.
/// </summary>
public class UserDeactivator(
    IUserRepository userRepository,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
{
    /// <param name="organisationId">Recorded on the audit row, which is what scopes it to an LA's own audit log.</param>
    /// <param name="cause">
    /// Set when the user was deactivated as a consequence of something else rather than by name. It is written
    /// into the audit row's After snapshot so the row says which event it belongs to; an individual
    /// deactivation leaves it null and its row keeps the plain snapshot.
    /// </param>
    /// <remarks>
    /// <para>
    /// A failure at any step must leave the user still active, which is the one state a retry can see — the
    /// cascade's retry only picks up users who are still active, so anything left half-done on a user it has
    /// already deactivated is never revisited.
    /// </para>
    /// <list type="number">
    /// <item>Revoke MFA sessions, stage the status change and append its audit row, all in one transaction
    /// (BLUM-8748). Either the user is deactivated with no live sessions and a record of who did it, or nothing
    /// changed. Revocation still comes first within it, so a reader never sees the status ahead of it.</item>
    /// <item>Invalidate the claims cache, only after that commit. Any earlier, and a request in between could
    /// re-read the user as still active and cache that for the full TTL. When this joined a caller's
    /// transaction (the organisation cascade) the commit is the caller's, so the caller invalidates afterwards
    /// through <see cref="InvalidateClaims"/>.</item>
    /// </list>
    /// <para>
    /// Takes no CancellationToken, deliberately. By the time this runs the caller has decided the user is going,
    /// and an admin closing the tab must not be able to cancel the audit write after the status has been staged
    /// — the same reasoning as MfaFailureRecorder.
    /// </para>
    /// <para>
    /// Deliberately does not disable the CIAM account (BLUM-6386). Doing so moved the refusal onto the
    /// Microsoft-hosted sign-in page, where the copy is Entra's and cannot be customised — the user was simply
    /// told sign-in failed, which reads as a broken service. Letting them authenticate and refusing them here
    /// means UserDeactivatedMiddleware answers with USER_DEACTIVATED and the SPA can say what actually
    /// happened. The block itself does not depend on Entra: sessions are revoked and the claims cache is
    /// invalidated below, and the middleware refuses every subsequent request.
    /// </para>
    /// </remarks>
    public async Task<User> DeactivateAsync(
        User user,
        UserId actorId,
        OrganisationId? organisationId = null,
        UserDeactivationCause? cause = null)
    {
        var ownsTransaction = !transactionRunner.InTransaction;

        await transactionRunner.RunAsync(
            async _ =>
            {
                await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);

                var before = UserSnapshot.Serialize(user);
                user.Deactivate(dateTimeProvider.GetUtcNow());
                var after = cause is null ? UserSnapshot.Serialize(user) : UserSnapshot.Serialize(user, cause);

                await userRepository.UpdateRangeAsync([user], CancellationToken.None, saveChanges: false);
                await auditLogRepository.AppendAsync(
                    AuditLog.Create(
                        actorId,
                        AuditTargetType.User,
                        user.Id.Value,
                        AuditAction.UserDeactivated,
                        before,
                        after,
                        organisationId),
                    CancellationToken.None);
            },
            CancellationToken.None);

        if (ownsTransaction)
        {
            userClaimsCache.Invalidate(user.SubId);
        }

        return user;
    }

    /// <summary>
    /// Drops the cached claims of users deactivated inside a caller's transaction. Call it after that transaction
    /// commits, never before — see the ordering on <see cref="DeactivateAsync"/>.
    /// </summary>
    public void InvalidateClaims(IEnumerable<User> users)
    {
        foreach (var user in users)
        {
            userClaimsCache.Invalidate(user.SubId);
        }
    }
}

/// <summary>The event a user's deactivation belongs to, when it was not a request about that user alone.</summary>
public sealed record UserDeactivationCause(string Action, OrganisationId OrganisationId)
{
    /// <summary>DHSC deactivated the user's organisation (BLUM-7667). The API still calls that suspending it.</summary>
    public static UserDeactivationCause OrganisationDeactivated(OrganisationId organisationId) =>
        new(AuditAction.OrganisationSuspended, organisationId);
}
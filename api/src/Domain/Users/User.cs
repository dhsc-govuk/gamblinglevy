using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Users;

public class User : IEntity<UserId>
{
    public UserId Id { get; private set; }
    public Name Name { get; private set; }
    public string Email { get; private set; }
    public SubId SubId { get; private set; }
    public UserStatus Status { get; private set; }
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Whether this account has been converted to Entra's emailAddress-shaped local identity with an actual
    /// usable password, instead of the default federated/mail (passwordless) shape UsersService.Add creates
    /// every account with. See UsersService.EnablePasswordLogin — a deliberate, admin-triggered escape hatch
    /// for the (small) set of users whose email domain collides with a separate real Entra tenant.
    /// </summary>
    public bool PasswordLoginEnabled { get; private set; }

    /// <summary>
    /// When this user's invitation email was held back rather than sent, because they were created deactivated
    /// (BLUM-7945); null when nothing is held. An invitation to a service the user cannot yet sign in to only
    /// lands them on the "account deactivated" page, so it is sent the first time they are reactivated instead.
    /// A held invitation also means the user has never signed in: they were never told the account exists, and
    /// a deactivated account is refused on every request.
    /// </summary>
    public DateTime? InvitationHeldAt { get; private set; }

    private User(
        UserId id,
        Name name,
        string email,
        SubId subId,
        UserStatus status,
        UserRole role,
        DateTime createdAt,
        DateTime? updatedAt,
        bool passwordLoginEnabled)
    {
        Id = id;
        Name = name;
        Email = email;
        SubId = subId;
        Status = status;
        Role = role;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        PasswordLoginEnabled = passwordLoginEnabled;
    }

    public static User New(UserId id, Name name, string email, SubId subId, UserStatus status, UserRole role, DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(name, nameof(name));
        Guard.NotNullOrEmpty(email, nameof(email));
        Guard.NotNull(subId, nameof(subId));
        Guard.NotNull(status, nameof(status));
        Guard.NotNull(role, nameof(role));

        return new User(id, name, NormaliseEmail(email), subId, status, role, utcNow, null, false);
    }

    public void UpdateDetails(Name name, string email, DateTime utcNow, UserRole? role = null)
    {
        Guard.NotNull(name, nameof(name));
        Guard.NotNullOrEmpty(email, nameof(email));

        Name = name;
        Email = NormaliseEmail(email);
        Role = role ?? Role;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        Status = UserStatus.Deactivated;
        UpdatedAt = utcNow;
    }

    public void Reactivate(DateTime utcNow)
    {
        Status = UserStatus.Active;
        UpdatedAt = utcNow;
    }

    public void HoldInvitation(DateTime utcNow)
    {
        InvitationHeldAt = utcNow;
    }

    /// <summary>Clears a held invitation, returning whether there was one — that is, whether it is now owed.</summary>
    public bool ReleaseHeldInvitation(DateTime utcNow)
    {
        if (InvitationHeldAt is null)
        {
            return false;
        }

        InvitationHeldAt = null;
        UpdatedAt = utcNow;
        return true;
    }

    // Set when Infrastructure.Graph.UsersService.EnablePasswordLogin has to delete and recreate the Entra
    // account outright — Graph refuses to add a non-federated (password) identity to an account whose
    // existing identities are all federated/tenant-default, and recreating is the only fix. The old Entra
    // object id stops resolving the moment that happens, so this has to be persisted in the same transaction
    // as everything else EnablePasswordLogin changes, not treated as a separate follow-up.
    public void UpdateSubId(SubId subId, DateTime utcNow)
    {
        Guard.NotNull(subId, nameof(subId));
        SubId = subId;
        UpdatedAt = utcNow;
    }

    // No guard against calling this twice, unlike Deactivate/Reactivate's paired exceptions — re-enabling an
    // already-enabled user (e.g. via ResetUserPasswordCommand's flow) must not error.
    public void EnablePasswordLogin(DateTime utcNow)
    {
        PasswordLoginEnabled = true;
        UpdatedAt = utcNow;
    }

    // Entra's local "mail" identity (Infrastructure/Graph/UsersService.Add) and every application-side
    // lookup (UserRepository.FindByEmailAsync/FindDuplicateAsync) already treat email as case-insensitive.
    // Normalising here — the one place both New and UpdateDetails funnel through — means every write path
    // (create, update, the DB seeder, CSV bulk-import) stores the same casing without each caller having
    // to remember to do it.
    private static string NormaliseEmail(string email) => email.Trim().ToLowerInvariant();
}
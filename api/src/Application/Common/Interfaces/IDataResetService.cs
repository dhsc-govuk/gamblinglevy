namespace Application.Common.Interfaces;

/// <summary>
/// Truncates every application table except <c>users</c> — where only non-admin rows are removed, so DHSC
/// admin accounts survive — and re-runs the seed protocol, leaving the database in exactly the state
/// <c>ApplicationDbContextInitialiser.SeedDataAsync</c> produces on a fresh boot (plus whichever admins
/// were already there). Implemented in Infrastructure because the truncate has to walk the EF model
/// directly.
/// </summary>
public interface IDataResetService
{
    Task ResetToSeedDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every row from <c>user_mfas</c> and <c>mfa_sessions</c> — including DHSC admins, unlike
    /// <see cref="ResetToSeedDataAsync"/>'s carve-out. That carve-out exists because a test-data reset has
    /// nothing to do with an admin's own MFA enrolment; this operation is the opposite case — it exists
    /// specifically because a Data Protection key change (see ApplicationSettings.Mfa.UseBlobKeyPersistence)
    /// makes every already-encrypted secret undecryptable at once, and an admin's secret is exactly as broken
    /// as anyone else's. Forces every account back through MFA setup on its next sign-in. Returns the rows
    /// deleted from each table, for the audit row.
    /// </summary>
    Task<MfaDataCleared> ClearMfaDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Headline counts across <c>users</c>, <c>user_mfas</c> and <c>mfa_sessions</c> — an at-a-glance view of
    /// how many accounts still need MFA re-enrolment after a run of <see cref="ClearMfaDataAsync"/>, without
    /// querying the database directly.
    /// </summary>
    Task<UserStatistics> GetUserStatisticsAsync(CancellationToken cancellationToken);
}

public record MfaDataCleared(int UserMfasDeleted, int SessionsDeleted);

/// <summary>
/// <see cref="UsersLoggedInAtLeastOnce"/> is a count of distinct users with at least one <c>mfa_sessions</c>
/// row ever created, not a "currently signed in" figure — a session past its expiry or revoked still proves a
/// completed sign-in happened. <see cref="UsersPendingMfaEnrolment"/> is <see cref="TotalUsers"/> minus users
/// with an enabled <c>user_mfas</c> row, so it rises the moment <see cref="IDataResetService.ClearMfaDataAsync"/>
/// runs and falls back to zero as each account re-enrols. <see cref="UsersPendingMfaEnrolmentEmails"/> is the
/// actual list behind that count — who to follow up with — deliberately not exposed for any other category
/// here, since this is the one an operator actually needs to act on.
/// </summary>
public record UserStatistics(
    int TotalUsers,
    int ActiveUsers,
    int DeactivatedUsers,
    int UsersLoggedInAtLeastOnce,
    int UsersPendingMfaEnrolment,
    IReadOnlyList<string> UsersPendingMfaEnrolmentEmails);
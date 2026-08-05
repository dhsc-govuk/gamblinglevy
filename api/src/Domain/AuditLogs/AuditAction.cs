namespace Domain.AuditLogs;

public static class AuditAction
{
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
    public const string UserDeleted = "user.deleted";
    public const string UserMfaReset = "user.mfa_reset";

    public const string OrganisationCreated = "organisation.created";
    public const string OrganisationUpdated = "organisation.updated";
    public const string OrganisationDeleted = "organisation.deleted";
    public const string OrganisationBulkImported = "organisation.bulk_imported";
    public const string OrganisationAllocationAmended = "organisation.allocation.amended";
    public const string OrganisationAllocationBulkAmended = "organisation.allocation.bulk_amended";
    public const string OrganisationSuspended = "organisation.suspended";
    public const string OrganisationReinstated = "organisation.reinstated";

    public const string ActivityCreated = "activity.created";
    public const string ActivityUpdated = "activity.updated";
    public const string ActivityCompleted = "activity.completed";
    public const string ActivityDeleted = "activity.deleted";
    public const string ActivityRestored = "activity.restored";

    public const string CollectionCreated = "collection.created";
    public const string CollectionUpdated = "collection.updated";
    public const string CollectionPublished = "collection.published";
    public const string CollectionRevertedToDraft = "collection.reverted_to_draft";
    public const string CollectionClosed = "collection.closed";
    public const string CollectionDeleted = "collection.deleted";
    public const string CollectionRestored = "collection.restored";
    public const string CollectionReminderSent = "collection.reminder_sent";

    public const string SubmissionExported = "submission.exported";

    public const string MfaEnrolled = "mfa.enrolled";
    public const string MfaDisabled = "mfa.disabled";
    public const string MfaVerifySucceeded = "mfa.verify.succeeded";
    public const string MfaVerifyFailed = "mfa.verify.failed";
    public const string MfaRecoveryCodeUsed = "mfa.recovery_code.used";
    public const string MfaSessionCreated = "mfa.session.created";
    public const string MfaSessionRevoked = "mfa.session.revoked";
    public const string MfaSessionExpired = "mfa.session.expired";
    public const string MfaSessionIdleTimeout = "mfa.session.idle_timeout";

    /// <summary>Revoked to keep the user within MfaSettings.MaxConcurrentSessions, not by anyone's request.</summary>
    public const string MfaSessionEvicted = "mfa.session.evicted";

    /// <summary>The "new sign-in on your account" email was accepted by SendGrid for this session.</summary>
    public const string MfaSessionNotified = "mfa.session.notified";

    /// <summary>
    /// Input a conforming client cannot produce — an undeclared field, a control character, a bidirectional
    /// override — was rejected at the boundary (BLUM-7240 / CWE-20).
    ///
    /// Ordinary validation failures are deliberately NOT recorded here. Every user typo is a validation
    /// failure, each audit append is its own committed transaction, and this table has no retention sweep,
    /// so auditing all of them would both swamp the admin view and hand any authenticated caller a way to
    /// choose how many rows the service writes. Those go to the validation_failed telemetry event and the
    /// security log instead.
    /// </summary>
    public const string SecurityInputRejected = "security.input_rejected";
}

public static class AuditTargetType
{
    public const string User = "User";
    public const string Organisation = "Organisation";
    public const string Activity = "Activity";
    public const string Collection = "Collection";
    public const string MfaSession = "MfaSession";

    /// <summary>The subject is the HTTP request itself rather than a domain entity.</summary>
    public const string Request = "Request";
}
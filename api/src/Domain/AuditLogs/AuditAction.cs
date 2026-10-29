namespace Domain.AuditLogs;

public static class AuditAction
{
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
    public const string UserDeleted = "user.deleted";
    public const string UserMfaReset = "user.mfa_reset";
    public const string UserPasswordLoginEnabled = "user.password_login_enabled";
    public const string UserPasswordReset = "user.password_reset";

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

    // BLUM-9103: a collection whose window has not opened moved onto a newer version of its forms, because DHSC published
    // (source "publish"), a release changed a published form (source "release"), or a boot found it behind a version
    // saved earlier (source "catch_up"). One row per collection moved.
    public const string CollectionFormVersionsFollowed = "collection.form_versions_followed";

    public const string SubmissionExported = "submission.exported";

    // BLUM-8747: the quarterly report journey. Target is the submission. The four *Saved actions are a draft step
    // ("Save and continue"), so their After carries ids and counts only, never an LA's answer text.
    public const string SubmissionCreated = "submission.created";
    public const string SubmissionActivitiesConfirmed = "submission.activities_confirmed";
    public const string SubmissionActivityReportSaved = "submission.activity_report.saved";
    public const string SubmissionActivityReportCompleted = "submission.activity_report.completed";
    public const string SubmissionFundingForecastSaved = "submission.funding_forecast.saved";
    public const string SubmissionFundingActualsSaved = "submission.funding_actuals.saved";
    public const string SubmissionFundingSpentCompleted = "submission.funding_spent.completed";
    public const string SubmissionStrategicFeedbackSaved = "submission.strategic_feedback.saved";
    public const string SubmissionStrategicFeedbackCompleted = "submission.strategic_feedback.completed";

    /// <summary>An LA submitted its quarterly report — the service's core act. After carries the reference number.</summary>
    public const string SubmissionSubmitted = "submission.submitted";

    public const string SubmissionOfflineReportUploaded = "submission.offline_report.uploaded";

    /// <summary>DHSC re-froze an already-submitted report's export snapshot (BLUM-7130).</summary>
    public const string SubmissionSnapshotRegenerated = "submission.snapshot_regenerated";

    /// <summary>An LA began its maturity assessment; pre-fill first ran here (BLUM-7856). Target is the assessment.</summary>
    public const string MaturityAssessmentStarted = "maturity_assessment.started";

    /// <summary>A draft step of the maturity assessment. After carries question codes only, never answer text.</summary>
    public const string MaturityAssessmentAnswersSaved = "maturity_assessment.answers_saved";

    public const string MaturityAssessmentSectionCompleted = "maturity_assessment.section_completed";

    /// <summary>
    /// A quarterly report submitted after the assessment was started was pulled into it (BLUM-8927). Target is the
    /// assessment; the actor is the user who submitted the report. After carries counts only, never answer text.
    /// </summary>
    public const string MaturityAssessmentPrefillRefreshed = "maturity_assessment.prefill_refreshed";

    public const string LocationCreated = "location.created";
    public const string LocationUpdated = "location.updated";

    // BLUM-8747: the activity form designer (Forms aggregate). Hidden in the client but still mapped, so audited.
    public const string FormSectionCreated = "form.section.created";
    public const string FormSectionUpdated = "form.section.updated";
    public const string FormSectionDeleted = "form.section.deleted";
    public const string FormSectionsReordered = "form.sections.reordered";
    public const string FormQuestionCreated = "form.question.created";
    public const string FormQuestionUpdated = "form.question.updated";
    public const string FormQuestionDeleted = "form.question.deleted";
    public const string FormQuestionsReordered = "form.questions.reordered";

    // BLUM-8747: the data-collection-form builder (DataCollectionForms aggregate).
    public const string DataCollectionFormModuleCreated = "data_collection_form.module.created";
    public const string DataCollectionFormModuleUpdated = "data_collection_form.module.updated";
    public const string DataCollectionFormModuleDeleted = "data_collection_form.module.deleted";
    public const string DataCollectionFormModulePublished = "data_collection_form.module.published";

    // BLUM-9102: a publish that saved a new fixed version, and a published module reopened as a draft.
    public const string DataCollectionFormModuleVersionPublished = "data_collection_form.module.version_published";
    public const string DataCollectionFormModuleDraftStarted = "data_collection_form.module.draft_started";
    public const string DataCollectionFormModuleVersionRestored = "data_collection_form.module.version_restored";

    // BLUM-9103: the boot step that gave unversioned forms a version 1 and published collections their versions.
    public const string FormModuleVersionsBackfilled = "data_collection_form.versions_backfilled";

    // BLUM-9103: the boot step that, while BLUM_9087 is off, moved collections' automatically recorded versions onto
    // each form's latest version, because every report reads the live form then.
    public const string CollectionFormVersionsFollowedLive = "data_collection_form.collection_versions_followed_live";
    public const string DataCollectionFormSectionCreated = "data_collection_form.section.created";
    public const string DataCollectionFormSectionUpdated = "data_collection_form.section.updated";
    public const string DataCollectionFormSectionDeleted = "data_collection_form.section.deleted";
    public const string DataCollectionFormSectionsReordered = "data_collection_form.sections.reordered";
    public const string DataCollectionFormQuestionCreated = "data_collection_form.question.created";
    public const string DataCollectionFormQuestionUpdated = "data_collection_form.question.updated";
    public const string DataCollectionFormQuestionDeleted = "data_collection_form.question.deleted";
    public const string DataCollectionFormQuestionsReordered = "data_collection_form.questions.reordered";

    /// <summary>
    /// <c>POST /internal/test-data/reset</c>. The reset truncates <c>audit_logs</c> itself, so this row is written
    /// after it and is the first row of the fresh log. The actor is <c>UserId.Empty()</c>: the endpoint is gated by a
    /// shared secret, not a signed-in user. Written for a failed reset too, since it may have truncated before failing:
    /// After is <c>{ succeeded, error }</c>, the error being the exception type only.
    /// </summary>
    public const string SystemTestDataReset = "system.test_data_reset";

    /// <summary><c>POST /internal/mfa-data/clear</c>. Actor is <c>UserId.Empty()</c>, as above.</summary>
    public const string SystemMfaDataCleared = "system.mfa_data_cleared";

    /// <summary>
    /// <c>POST /internal/user-emails/normalise</c> run for real (a dry run writes no row). Actor is <c>UserId.Empty()</c>.
    /// Also written for a run that stopped part-way after correcting someone: After's <c>completed</c> is then false and
    /// <c>interruptedAtUserId</c> names the user whose Entra identity may have changed without the local row following.
    /// </summary>
    public const string SystemUserEmailsNormalised = "system.user_emails_normalised";

    /// <summary>
    /// The audit retention sweep deleted rows older than AuditSettings.RetentionDays (BLUM-8750). Written only when
    /// something was deleted, after the deletes. Actor is <c>UserId.Empty()</c>: nobody asked for it.
    /// After is <c>{ cutoff, retentionDays, deleted }</c>.
    /// </summary>
    public const string SystemAuditLogsPurged = "system.audit_logs_purged";

    /// <summary>An LA finalised its annual maturity assessment (BLUM-7858). Target is the assessment.</summary>
    public const string MaturityAssessmentSubmitted = "maturity_assessment.submitted";

    /// <summary>DHSC downloaded the consolidated maturity-assessment export for a window (BLUM-7859). Target is the collection.</summary>
    public const string MaturityAssessmentExported = "maturity_assessment.exported";

    /// <summary>
    /// The boot seeder loaded prior-year survey answers for one or more organisations (BLUM-7857 AC4). One row
    /// per boot that actually imported something; a boot that found nothing new writes no row. The After JSON
    /// lists every survey row as matched, unmatched or skipped with its reason.
    /// </summary>
    public const string MaturityBaselineImported = "maturity_assessment.baseline_imported";

    /// <summary>A user generated a TOTP secret to enrol. Enrolment itself is <see cref="MfaEnrolled"/>; this row never carries the secret.</summary>
    public const string MfaSetupStarted = "mfa.setup_started";

    public const string MfaEnrolled = "mfa.enrolled";
    public const string MfaDisabled = "mfa.disabled";
    public const string MfaVerifySucceeded = "mfa.verify.succeeded";
    public const string MfaVerifyFailed = "mfa.verify.failed";
    public const string MfaRecoveryCodeUsed = "mfa.recovery_code.used";

    /// <summary>
    /// A recovery code was rejected (BLUM-7239 AC3).
    ///
    /// The TOTP path has recorded its failures since BLUM-7240; this one never has, so a brute force against
    /// the recovery codes — the more valuable target, since each one is a single-use bypass of the
    /// authenticator entirely — left no trace at all. Without it the lockout this ticket adds is not
    /// verifiable on that path.
    /// </summary>
    public const string MfaRecoveryCodeFailed = "mfa.recovery_code.failed";

    /// <summary>
    /// Too many incorrect codes locked the account, and its live sessions were revoked (BLUM-7239 AC3).
    ///
    /// Written once per lockout rather than once per failure — the failures either side of it already have
    /// their own rows, and a row per attempt during a lockout would let an attacker choose how many the
    /// service writes.
    /// </summary>
    public const string MfaLockedOut = "mfa.locked_out";
    public const string MfaSessionCreated = "mfa.session.created";
    public const string MfaSessionRevoked = "mfa.session.revoked";
    public const string MfaSessionExpired = "mfa.session.expired";
    public const string MfaSessionIdleTimeout = "mfa.session.idle_timeout";

    /// <summary>Revoked to keep the user within MfaSettings.MaxConcurrentSessions, not by anyone's request.</summary>
    public const string MfaSessionEvicted = "mfa.session.evicted";

    /// <summary>The "new sign-in on your account" email was accepted by SendGrid for this session.</summary>
    public const string MfaSessionNotified = "mfa.session.notified";

    /// <summary>
    /// A live session was refused because the request's IP and/or User-Agent no longer matched the ones it
    /// was bound to at sign-in, so the user was sent back to enter a code (BLUM-8321).
    ///
    /// Recorded because that refusal used to leave no trace at all: DAC users were being returned to the
    /// code-entry screen mid-task and nothing server-side could say which half of the binding had moved.
    /// Before holds what the session was bound to, After what the request presented. Throttled per user, so
    /// a client that changes its claimed address on every request cannot choose how many rows get written.
    /// </summary>
    public const string MfaSessionDeviceMismatch = "mfa.session.device_mismatch";

    /// <summary>
    /// A live session carried a request from a different IP address than the one it was last seen on, and was
    /// let through because the browser still matched and MfaSettings.BindSessionToIpAddress is off
    /// (BLUM-7848). Written once per move, not per request: Before is the address it moved from, After the
    /// one it moved to. Same shape and per-user cap as MfaSessionDeviceMismatch, on its own budget, so moves
    /// cannot crowd the refusals out.
    /// </summary>
    public const string MfaSessionIpChanged = "mfa.session.ip_changed";

    /// <summary>
    /// Input a conforming client cannot produce — an undeclared field, a control character, a bidirectional
    /// override — was rejected at the boundary (BLUM-7240 / CWE-20).
    ///
    /// Ordinary validation failures are deliberately NOT recorded here. Every user typo is a validation
    /// failure, each audit append is its own committed transaction, and rows are kept for the whole retention
    /// period (for ever by default), so auditing all of them would both swamp the admin view and hand any authenticated caller a way to
    /// choose how many rows the service writes. Those go to the validation_failed telemetry event and the
    /// security log instead.
    /// </summary>
    public const string SecurityInputRejected = "security.input_rejected";

    /// <summary>
    /// A caller exceeded a request-rate budget (BLUM-7239 AC6).
    ///
    /// One row stands for a burst, not for a rejection. A flood is the entire scenario here, so auditing
    /// every refused request would hand the caller the same power over this table that
    /// <see cref="SecurityInputRejected"/> refuses them — and a flood is far cheaper to send than a hostile
    /// payload. Two things bound it: only the first rejection in a partition's window is even a candidate,
    /// and AuditFloodThrottle then caps what survives that. The row carries the size of the burst it stands
    /// for, so nothing is lost except the row count.
    /// </summary>
    public const string SecurityRateLimited = "security.rate_limited";
}

public static class AuditTargetType
{
    public const string User = "User";
    public const string Organisation = "Organisation";
    public const string Activity = "Activity";
    public const string Collection = "Collection";
    public const string MfaSession = "MfaSession";
    public const string MaturityAssessment = "MaturityAssessment";

    /// <summary>An LA's quarterly report (the <c>submissions</c> row for one collection and organisation).</summary>
    public const string Submission = "Submission";

    /// <summary>A section or question of the activity form designer. TargetId is the section or question id.</summary>
    public const string Form = "Form";

    /// <summary>A module, section or question of the data-collection-form builder. TargetId is that row's id.</summary>
    public const string DataCollectionForm = "DataCollectionForm";

    public const string Location = "Location";

    /// <summary>An ops action on the whole service rather than one entity. TargetId is <c>Guid.Empty</c>.</summary>
    public const string System = "System";

    /// <summary>The subject is the HTTP request itself rather than a domain entity.</summary>
    public const string Request = "Request";
}
import axios from 'axios';

export interface AuditLogListItem {
  id: string;
  occurredAt: string;
  action: string;
  actorUserId: string;
  actorDisplayName: string | null;
  targetType: string;
  targetId: string;
  targetDisplayName: string | null;
  organisationId: string | null;
  organisationName: string | null;
  onsCode: string | null;
}

export interface AuditLogDetail extends AuditLogListItem {
  before: string | null;
  after: string | null;
}

export interface PagedAuditLogs {
  items: AuditLogListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export type AuditLogsResponse = PagedAuditLogs | AuditLogListItem[];

export type AuditLogSort = 'newest' | 'oldest';

export interface AuditLogFilterParams {
  action?: string;
  actorUserId?: string;
  targetUserId?: string;
  organisationId?: string;
  from?: string;
  to?: string;
  sort?: AuditLogSort;
  page?: number;
  pageSize?: number;
}

export const getAuditLogs = (filter: AuditLogFilterParams) => axios.get<PagedAuditLogs>('admin/audit-logs', { params: filter });

export const getAuditLog = (id: string) => axios.get<AuditLogDetail>(`admin/audit-logs/${id}`);

export const getOrganisationAdminAuditLogs = (filter: AuditLogFilterParams) => axios.get<AuditLogsResponse>('organisation-admin/audit-logs', { params: filter });

export const getOrganisationAdminAuditLog = (id: string) => axios.get<AuditLogDetail>(`organisation-admin/audit-logs/${id}`);

// Every action api/src/Domain/AuditLogs/AuditAction.cs can write, in the order that file declares them.
// The list is both the "Filter by action" dropdown and the lookup behind every Action cell and the detail
// page's heading, so a key missing here is not a missing filter option — it renders raw, as
// "activity.created", wherever the log is shown.
export const AUDIT_ACTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All actions' },
  { value: 'user.created', label: 'User created' },
  { value: 'user.updated', label: 'User updated' },
  { value: 'user.deactivated', label: 'User deactivated' },
  { value: 'user.reactivated', label: 'User reactivated' },
  { value: 'user.deleted', label: 'User deleted' },
  { value: 'user.mfa_reset', label: 'User MFA reset' },
  { value: 'user.password_login_enabled', label: 'User password sign-in enabled' },
  { value: 'user.password_reset', label: 'User password reset' },
  { value: 'organisation.created', label: 'Organisation created' },
  { value: 'organisation.updated', label: 'Organisation updated' },
  { value: 'organisation.deleted', label: 'Organisation deleted' },
  { value: 'organisation.bulk_imported', label: 'Organisations bulk imported' },
  { value: 'organisation.allocation.amended', label: 'Organisation allocation amended' },
  { value: 'organisation.allocation.bulk_amended', label: 'Organisation allocations bulk amended' },
  { value: 'organisation.suspended', label: 'Organisation suspended' },
  { value: 'organisation.reinstated', label: 'Organisation reinstated' },
  { value: 'activity.created', label: 'Activity created' },
  { value: 'activity.updated', label: 'Activity updated' },
  { value: 'activity.completed', label: 'Activity completed' },
  { value: 'activity.deleted', label: 'Activity deleted' },
  { value: 'activity.restored', label: 'Activity restored' },
  { value: 'collection.created', label: 'Data collection created' },
  { value: 'collection.updated', label: 'Data collection updated' },
  { value: 'collection.published', label: 'Data collection published' },
  { value: 'collection.reverted_to_draft', label: 'Data collection reverted to draft' },
  { value: 'collection.closed', label: 'Data collection closed' },
  { value: 'collection.deleted', label: 'Data collection deleted' },
  { value: 'collection.restored', label: 'Data collection restored' },
  { value: 'collection.reminder_sent', label: 'Data collection reminder sent' },
  { value: 'submission.exported', label: 'Submissions exported' },
  { value: 'submission.created', label: 'Quarterly report started' },
  { value: 'submission.activities_confirmed', label: 'Quarterly report activities confirmed' },
  { value: 'submission.activity_report.saved', label: 'Quarterly report activity saved' },
  { value: 'submission.activity_report.completed', label: 'Quarterly report activities completed' },
  { value: 'submission.funding_forecast.saved', label: 'Quarterly report funding forecast saved' },
  { value: 'submission.funding_actuals.saved', label: 'Quarterly report funding spent saved' },
  { value: 'submission.funding_spent.completed', label: 'Quarterly report funding spent completed' },
  { value: 'submission.strategic_feedback.saved', label: 'Quarterly report strategic feedback saved' },
  { value: 'submission.strategic_feedback.completed', label: 'Quarterly report strategic feedback completed' },
  { value: 'submission.submitted', label: 'Quarterly report submitted' },
  { value: 'submission.offline_report.uploaded', label: 'Quarterly report offline file uploaded' },
  { value: 'submission.snapshot_regenerated', label: 'Quarterly report export copy regenerated' },
  { value: 'maturity_assessment.started', label: 'Maturity assessment started' },
  { value: 'maturity_assessment.answers_saved', label: 'Maturity assessment answers saved' },
  { value: 'maturity_assessment.section_completed', label: 'Maturity assessment section completed' },
  { value: 'maturity_assessment.prefill_refreshed', label: 'Maturity assessment pre-fill refreshed' },
  { value: 'maturity_assessment.submitted', label: 'Maturity assessment submitted' },
  { value: 'maturity_assessment.exported', label: 'Maturity assessments exported' },
  { value: 'maturity_assessment.baseline_imported', label: 'Maturity assessment 2025 survey imported' },
  { value: 'location.created', label: 'Location created' },
  { value: 'location.updated', label: 'Location updated' },
  { value: 'form.section.created', label: 'Activity form section created' },
  { value: 'form.section.updated', label: 'Activity form section updated' },
  { value: 'form.section.deleted', label: 'Activity form section deleted' },
  { value: 'form.sections.reordered', label: 'Activity form sections reordered' },
  { value: 'form.question.created', label: 'Activity form question created' },
  { value: 'form.question.updated', label: 'Activity form question updated' },
  { value: 'form.question.deleted', label: 'Activity form question deleted' },
  { value: 'form.questions.reordered', label: 'Activity form questions reordered' },
  { value: 'data_collection_form.module.created', label: 'Data collection form created' },
  { value: 'data_collection_form.module.updated', label: 'Data collection form updated' },
  { value: 'data_collection_form.module.deleted', label: 'Data collection form deleted' },
  { value: 'data_collection_form.module.published', label: 'Data collection form published' },
  { value: 'data_collection_form.section.created', label: 'Data collection form section created' },
  { value: 'data_collection_form.section.updated', label: 'Data collection form section updated' },
  { value: 'data_collection_form.section.deleted', label: 'Data collection form section deleted' },
  { value: 'data_collection_form.sections.reordered', label: 'Data collection form sections reordered' },
  { value: 'data_collection_form.question.created', label: 'Data collection form question created' },
  { value: 'data_collection_form.question.updated', label: 'Data collection form question updated' },
  { value: 'data_collection_form.question.deleted', label: 'Data collection form question deleted' },
  { value: 'data_collection_form.questions.reordered', label: 'Data collection form questions reordered' },
  { value: 'system.test_data_reset', label: 'Test data reset' },
  { value: 'system.mfa_data_cleared', label: 'All MFA enrolments cleared' },
  { value: 'system.user_emails_normalised', label: 'User email addresses normalised' },
  { value: 'mfa.setup_started', label: 'MFA setup started' },
  { value: 'mfa.enrolled', label: 'MFA enrolled' },
  { value: 'mfa.disabled', label: 'MFA disabled' },
  { value: 'mfa.verify.succeeded', label: 'MFA verify succeeded' },
  { value: 'mfa.verify.failed', label: 'MFA verify failed' },
  { value: 'mfa.recovery_code.used', label: 'MFA recovery code used' },
  { value: 'mfa.recovery_code.failed', label: 'MFA recovery code rejected' },
  { value: 'mfa.locked_out', label: 'Account locked after too many incorrect codes' },
  { value: 'mfa.session.created', label: 'MFA session created' },
  { value: 'mfa.session.revoked', label: 'MFA session revoked' },
  { value: 'mfa.session.expired', label: 'MFA session expired' },
  { value: 'mfa.session.idle_timeout', label: 'MFA session timed out' },
  { value: 'mfa.session.evicted', label: 'MFA session ended by concurrent session limit' },
  { value: 'mfa.session.notified', label: 'New sign-in notification sent' },
  { value: 'mfa.session.device_mismatch', label: 'MFA session refused: IP address or browser changed' },
  { value: 'mfa.session.ip_changed', label: 'MFA session allowed: IP address changed' },
  { value: 'security.input_rejected', label: 'Malicious input rejected' },
  { value: 'security.rate_limited', label: 'Too many requests blocked' },
];

export const AUDIT_LOG_SORT_OPTIONS: { value: AuditLogSort; label: string }[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'oldest', label: 'Oldest' },
];

export const auditActionLabel = (action: string): string => AUDIT_ACTIONS.find((a) => a.value === action)?.label ?? action;

// api/src/Domain/AuditLogs/AuditTargetType — the kind of record an entry was about. The backend sends the
// C# constant verbatim, so without this map the detail page prints "MfaSession" at a citizen-facing
// government service. The raw-value fallback matches auditActionLabel: a target type added on the server
// before this list catches up should still show something rather than nothing.
const AUDIT_TARGET_TYPE_LABELS: Record<string, string> = {
  User: 'User',
  Organisation: 'Organisation',
  Activity: 'Activity',
  Collection: 'Data collection',
  MaturityAssessment: 'Maturity assessment',
  MfaSession: 'MFA session',
  Submission: 'Quarterly report',
  Form: 'Activity form',
  DataCollectionForm: 'Data collection form',
  Location: 'Location',
  System: 'System',
  Request: 'Request',
};

export const auditTargetTypeLabel = (targetType: string): string => AUDIT_TARGET_TYPE_LABELS[targetType] ?? targetType;

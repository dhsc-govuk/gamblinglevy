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

export interface AuditLogFilterParams {
  action?: string;
  actorUserId?: string;
  targetUserId?: string;
  organisationId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export const getAuditLogs = (filter: AuditLogFilterParams) =>
  axios.get<PagedAuditLogs>('admin/audit-logs', { params: filter });

export const getAuditLog = (id: string) =>
  axios.get<AuditLogDetail>(`admin/audit-logs/${id}`);

export const getOrganisationAdminAuditLogs = (filter: AuditLogFilterParams) =>
  axios.get<AuditLogsResponse>('organisation-admin/audit-logs', { params: filter });

export const getOrganisationAdminAuditLog = (id: string) =>
  axios.get<AuditLogDetail>(`organisation-admin/audit-logs/${id}`);

export const AUDIT_ACTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All events' },
  { value: 'user.created', label: 'User created' },
  { value: 'user.updated', label: 'User updated' },
  { value: 'user.deactivated', label: 'User deactivated' },
  { value: 'user.reactivated', label: 'User reactivated' },
  { value: 'user.deleted', label: 'User deleted' },
  { value: 'organisation.created', label: 'Organisation created' },
  { value: 'organisation.updated', label: 'Organisation updated' },
  { value: 'organisation.deleted', label: 'Organisation deleted' },
  { value: 'organisation.bulk_imported', label: 'Organisations bulk imported' },
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
  { value: 'security.input_rejected', label: 'Malicious input rejected' },
  { value: 'security.rate_limited', label: 'Too many requests blocked' },
];

export const auditActionLabel = (action: string): string =>
  AUDIT_ACTIONS.find((a) => a.value === action)?.label ?? action;

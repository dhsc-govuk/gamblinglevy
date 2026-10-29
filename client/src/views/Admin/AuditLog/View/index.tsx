import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { ManagementLayout } from '../../../../layouts';
import { H1, SummaryList } from '../../../../components/GovUKComponents';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { auditLogKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { auditActionLabel, auditTargetTypeLabel, getAuditLog } from '../queries';
import './styles.css';

const prettyJson = (value: string | null): string => {
  if (!value) return '—';
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
};

const formatDate = (iso: string): string => {
  try {
    return new Date(iso).toUTCString();
  } catch {
    return iso;
  }
};

const AuditLogView = (): React.ReactElement => {
  const { auditLogId = '' } = useParams<{ auditLogId: string }>();
  const { data, isLoading } = useQuery({ queryKey: auditLogKey(auditLogId), queryFn: () => getAuditLog(auditLogId), enabled: !!auditLogId });

  const entry = data?.data;
  usePageTitle(pageTitleWithName('Audit log action', entry && auditActionLabel(entry.action)));

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.auditLogs.list }}>
      <div className="audit-log-view-page">
        {isLoading || !entry ? (
          <PageLoading />
        ) : (
          <>
            <div className="audit-log-view-page__heading">
              {entry.organisationName && (
                <span className="govuk-caption-l" data-testid="admin-audit-log-detail-caption">
                  {entry.organisationName}
                </span>
              )}
              <H1 size="l" data-testid="admin-audit-log-detail-heading">
                {auditActionLabel(entry.action)}
              </H1>
            </div>

            <SummaryList
              testId="admin-audit-log-detail"
              items={[
                { label: 'When', value: formatDate(entry.occurredAt) },
                { label: 'User', value: entry.actorDisplayName ?? entry.actorUserId },
                // The kind of record the event was about — "Activity", "Data collection" — not its id and
                // not its name. The id is in the After payload below, and targetDisplayName is only ever
                // resolved for User targets, so using it here would print a person's name under "Action".
                { label: 'Action', value: auditTargetTypeLabel(entry.targetType) },
                { label: 'Organisation', value: entry.organisationName ?? '—' },
              ]}
            />

            <h2 className="govuk-heading-m">Before</h2>
            <pre
              data-testid="admin-audit-log-detail-before"
              style={{
                background: '#f3f2f1',
                padding: '15px',
                overflow: 'auto',
                fontSize: '14px',
                fontFamily: 'monospace',
              }}>
              {prettyJson(entry.before)}
            </pre>

            <h2 className="govuk-heading-m">After</h2>
            <pre
              data-testid="admin-audit-log-detail-after"
              style={{
                background: '#f3f2f1',
                padding: '15px',
                overflow: 'auto',
                fontSize: '14px',
                fontFamily: 'monospace',
              }}>
              {prettyJson(entry.after)}
            </pre>
          </>
        )}
      </div>
    </ManagementLayout>
  );
};

export default AuditLogView;

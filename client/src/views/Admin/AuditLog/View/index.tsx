import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { H1 } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { auditLogKey } from '../../../../helpers/queriesParams';
import { auditActionLabel, getAuditLog } from '../queries';

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
  usePageTitle('Audit log event');
  const { auditLogId = '' } = useParams<{ auditLogId: string }>();
  const { data, isLoading } = useQuery(
    auditLogKey(auditLogId),
    () => getAuditLog(auditLogId),
    { enabled: !!auditLogId },
  );

  const entry = data?.data;

  return (
    <GeneralLayout
      breadcrumbs={[
        { label: 'Home', link: '/admin/home' },
        { label: 'Audit log', link: '/admin/audit-logs' },
      ]}
      backLink={{ href: '/admin/audit-logs' }}
    >
      <div style={{ paddingTop: '20px' }}>
        {isLoading || !entry ? (
          <PageLoading />
        ) : (
          <>
            <H1>{auditActionLabel(entry.action)}</H1>

            <dl className="govuk-summary-list">
              <div className="govuk-summary-list__row">
                <dt className="govuk-summary-list__key">When</dt>
                <dd className="govuk-summary-list__value">{formatDate(entry.occurredAt)}</dd>
              </div>
              <div className="govuk-summary-list__row">
                <dt className="govuk-summary-list__key">Actor</dt>
                <dd className="govuk-summary-list__value">{entry.actorDisplayName ?? entry.actorUserId}</dd>
              </div>
              <div className="govuk-summary-list__row">
                <dt className="govuk-summary-list__key">Target</dt>
                <dd className="govuk-summary-list__value">
                  {entry.targetDisplayName ?? `${entry.targetType} (${entry.targetId})`}
                </dd>
              </div>
              <div className="govuk-summary-list__row">
                <dt className="govuk-summary-list__key">Organisation</dt>
                <dd className="govuk-summary-list__value">{entry.organisationName ?? '—'}</dd>
              </div>
            </dl>

            <h2 className="govuk-heading-m">Before</h2>
            <pre style={{
              background: '#f3f2f1',
              padding: '15px',
              overflow: 'auto',
              fontSize: '14px',
              fontFamily: 'monospace',
            }}>
              {prettyJson(entry.before)}
            </pre>

            <h2 className="govuk-heading-m">After</h2>
            <pre style={{
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
    </GeneralLayout>
  );
};

export default AuditLogView;

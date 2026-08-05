import React from 'react';
import { useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKSummaryCard, SummaryList, GovUKInsetText, H1, PageLoading } from '../../../../components/GovUKComponents';
import { HISTORICAL_ROLE_LABELS, homeLinkForRole, EnumUserRole } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { getAuditLogs, getAuditLog } from '../../../Admin/AuditLog/queries';
import { deletedUserLogsKey, deletedUserDetailKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import './styles.css';

const formatDateTime = (iso: string): string => {
  const d = new Date(iso);
  const date = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${date}, ${time}`;
};

const ActorAndDate = ({ actor, iso }: { actor: string | null; iso: string }): React.ReactElement => (
  <span className="admin-deleted-user-view__history-value">
    <span>{actor ?? '—'}</span>
    <span>{formatDateTime(iso)}</span>
  </span>
);

interface UserSnapshot {
  id?: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  role?: string;
  status?: string;
}

const DeletedUserView = (): React.ReactElement => {
  usePageTitle('Deleted user');
  const { userId = '' } = useParams<{ userId: string }>();

  const { data: logsData, isLoading: logsLoading } = useQuery(deletedUserLogsKey(userId), () => getAuditLogs({ targetUserId: userId, pageSize: 100 }), { enabled: !!userId });

  const logs = logsData?.data?.items ?? [];
  const createdEntry = logs.find((l) => l.action === 'user.created');
  const updatedEntries = logs.filter((l) => l.action === 'user.updated');
  const lastUpdatedEntry = updatedEntries.length > 0 ? updatedEntries.reduce((a, b) => (new Date(a.occurredAt) > new Date(b.occurredAt) ? a : b)) : createdEntry;
  const deletedEntry = logs.find((l) => l.action === 'user.deleted');

  const { data: detailData, isLoading: detailLoading } = useQuery(deletedUserDetailKey(deletedEntry?.id ?? ''), () => getAuditLog(deletedEntry!.id), {
    enabled: !!deletedEntry?.id,
  });

  const snapshot: UserSnapshot = (() => {
    try {
      return JSON.parse(detailData?.data?.before ?? '{}');
    } catch {
      return {};
    }
  })();

  const fullName = snapshot.firstName && snapshot.lastName ? `${snapshot.firstName} ${snapshot.lastName}` : deletedEntry?.targetDisplayName ?? 'Deleted user';
  const isLoading = logsLoading || (!!deletedEntry?.id && (detailLoading || !detailData));

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) },
        { label: 'Management', link: ROUTES.admin.management },
        { label: 'Users', link: ROUTES.admin.users.list },
        { label: 'Deleted users', link: ROUTES.admin.users.deleted },
      ]}>
      {isLoading ? (
        <PageLoading />
      ) : (
        <div className="admin-deleted-user-view">
          <H1 className="admin-deleted-user-view__heading">{fullName}</H1>

          <GovUKInsetText className="admin-deleted-user-view__notice" data-testid="admin-deleted-user-notice">
            You cannot edit this account because it has been deleted.
          </GovUKInsetText>

          <GovUKSummaryCard title="User details" testId="admin-deleted-user-details">
            <SummaryList
              equalColumns
              testId="admin-deleted-user-details-list"
              items={[
                { label: 'First name', value: snapshot.firstName ?? '—' },
                { label: 'Last name', value: snapshot.lastName ?? '—' },
                {
                  label: 'Access level',
                  value: HISTORICAL_ROLE_LABELS[snapshot.role ?? ''] ?? snapshot.role ?? '—',
                },
                {
                  label: 'Email address',
                  value: snapshot.email ? (
                    <a href={`mailto:${snapshot.email}`} className="govuk-link" data-testid="admin-deleted-user-email-link">
                      {snapshot.email}
                    </a>
                  ) : (
                    '—'
                  ),
                },
              ]}
            />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Sign in security" testId="admin-deleted-user-security">
            <SummaryList equalColumns testId="admin-deleted-user-security-list" items={[{ label: 'User access', value: 'Deleted' }]} />
          </GovUKSummaryCard>

          <GovUKSummaryCard title={<span className="admin-deleted-user-view__record-title">Record information</span>} testId="admin-deleted-user-record">
            <SummaryList
              equalColumns
              testId="admin-deleted-user-record-list"
              items={[
                {
                  label: 'Created',
                  value: createdEntry ? <ActorAndDate actor={createdEntry.actorDisplayName} iso={createdEntry.occurredAt} /> : '—',
                },
                {
                  label: 'Last updated',
                  value: lastUpdatedEntry ? <ActorAndDate actor={lastUpdatedEntry.actorDisplayName} iso={lastUpdatedEntry.occurredAt} /> : '—',
                },
                {
                  label: 'Deleted',
                  value: deletedEntry ? <ActorAndDate actor={deletedEntry.actorDisplayName} iso={deletedEntry.occurredAt} /> : '—',
                },
              ]}
            />
          </GovUKSummaryCard>
        </div>
      )}
    </ManagementLayout>
  );
};

export default DeletedUserView;

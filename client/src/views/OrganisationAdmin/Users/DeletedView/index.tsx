import React from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKInsetText, GovUKSummaryCard, H1, NotFound, PageLoading, SummaryList } from '../../../../components/GovUKComponents';
import { HISTORICAL_ROLE_LABELS, useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { deletedUserDetailKey, deletedUserLogsKey } from '../../../../helpers/queriesParams';
import { UserLoadFailed, SERVICE_PROBLEM_HEADING } from '../../../../components/Global/OrganisationUsers';
import { getOrganisationAdminAuditLog, getOrganisationAdminAuditLogs, type AuditLogListItem, type AuditLogsResponse } from '../../../Admin/AuditLog/queries';

interface UserSnapshot {
  firstName?: string;
  lastName?: string;
  email?: string;
  role?: string;
}

const formatDateTime = (iso: string): string => {
  const d = new Date(iso);
  const date = d.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const time = d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${date}, ${time}`;
};

const ActorAndDate = ({ actor, iso }: { actor: string | null; iso: string }): React.ReactElement => (
  <>
    {actor ?? '—'}
    <br />
    {formatDateTime(iso)}
  </>
);

const parseSnapshot = (before: string | null | undefined): UserSnapshot => {
  if (!before) return {};
  try {
    return JSON.parse(before) as UserSnapshot;
  } catch {
    return {};
  }
};

const getAuditLogItems = (data: AuditLogsResponse | undefined): AuditLogListItem[] => (Array.isArray(data) ? data : data?.items ?? []);

const DeletedUserView = (): React.ReactElement => {
  const { userId = '' } = useParams<{ userId: string }>();
  const navigate = useNavigate();
  const { organisationId, isAuthLoading } = useAuthProvider();

  const {
    data: logsData,
    isLoading: logsLoading,
    isSuccess: logsLoaded,
    isError: logsFailed,
  } = useQuery({
    queryKey: deletedUserLogsKey(userId),
    queryFn: () => getOrganisationAdminAuditLogs({ targetUserId: userId, pageSize: 100 }),
    enabled: !!userId && !!organisationId,
  });

  const logs = getAuditLogItems(logsData?.data);
  const createdEntry = logs.find((l) => l.action === 'user.created');
  const updatedEntries = logs.filter((l) => l.action === 'user.updated');
  const lastUpdatedEntry = updatedEntries.length > 0 ? updatedEntries.reduce((a, b) => (new Date(a.occurredAt) > new Date(b.occurredAt) ? a : b)) : createdEntry;
  const deletedEntry = logs.find((l) => l.action === 'user.deleted');

  const {
    data: detailData,
    isLoading: detailLoading,
    isError: detailFailed,
  } = useQuery({ queryKey: deletedUserDetailKey(deletedEntry?.id ?? ''), queryFn: () => getOrganisationAdminAuditLog(deletedEntry!.id), enabled: !!deletedEntry?.id });

  const snapshot = parseSnapshot(detailData?.data?.before);
  const fullName = snapshot.firstName && snapshot.lastName ? `${snapshot.firstName} ${snapshot.lastName}` : deletedEntry?.targetDisplayName ?? 'Deleted user';

  // This view reads audit logs rather than the user endpoint, so it cannot use useOrganisationUser — but the two
  // rules that hook enforces apply here by hand. First: a profile that resolved without an organisation id leaves
  // the query permanently `enabled: false`, so no error ever arrives and "still loading" would spin forever.
  const missingOrganisation = !isAuthLoading && !organisationId;

  // `!detailFailed` guards the `!detailData` term: on a failed snapshot fetch it never clears, which would spin
  // forever — the very symptom this ticket exists to remove, one query further along.
  const isLoading = isAuthLoading || logsLoading || (!!deletedEntry?.id && !detailFailed && (detailLoading || !detailData));

  // Only the logs query failing costs us the page. If just the snapshot fetch failed we still hold the created,
  // updated and deleted history, and the name — render those and let the snapshot-only fields fall back to the
  // em-dashes `parseSnapshot({})` already produces, rather than throwing away data we successfully loaded.
  const loadFailed = logsFailed || missingOrganisation;

  // A successful lookup with no `user.deleted` row is NOT a permissions verdict: the same empty result is produced by
  // another LA's id, by one of your own colleagues who simply has not been deleted, and by a deletion whose audit row
  // carries no organisation id. Only the first is a refusal, so claiming refusal would be untrue for the other two —
  // "no such deleted record" is the one statement that holds for all three, and it leaks nothing about the first.
  // Read it off `isSuccess`, never off "not loading": a failed request also yields an empty list.
  const noDeletedRecord = logsLoaded && !deletedEntry;

  // Second rule from the hook: the title must describe the page that is actually rendered. `noDeletedRecord` needs
  // `logsLoaded`, which is false whenever the logs query failed — so the fault state needs its own arm here.
  usePageTitle(noDeletedRecord ? 'Page not found' : loadFailed ? SERVICE_PROBLEM_HEADING : 'Deleted user');

  // NotFound renders its own <main>, so it replaces the layout rather than sitting inside it — otherwise the page
  // would carry two main landmarks.
  if (noDeletedRecord) return <NotFound goBack={navigate} testId="organisation-admin-deleted-user-not-found" />;

  return (
    <OrganisationManagementLayout
      breadcrumbs={[
        { label: 'Home', link: '/organisation-admin/home' },
        { label: 'Manage account', link: '/organisation-admin/users' },
        { label: 'Users', link: '/organisation-admin/users' },
        { label: 'Deleted users', link: '/organisation-admin/users/deleted' },
      ]}>
      {loadFailed ? (
        <UserLoadFailed testId="organisation-admin-deleted-user-load-error" />
      ) : isLoading ? (
        <PageLoading />
      ) : (
        <>
          <H1>{fullName}</H1>

          <GovUKInsetText data-testid="organisation-admin-deleted-user-notice">You cannot edit this account because it has been deleted.</GovUKInsetText>

          <GovUKSummaryCard title="User details" testId="organisation-admin-deleted-user-details">
            <SummaryList
              testId="organisation-admin-deleted-user-details-list"
              items={[
                { label: 'First name', value: snapshot.firstName ?? '—' },
                { label: 'Last name', value: snapshot.lastName ?? '—' },
                { label: 'Access level', value: HISTORICAL_ROLE_LABELS[snapshot.role ?? ''] ?? snapshot.role ?? '—' },
                {
                  label: 'Email address',
                  value: snapshot.email ? (
                    <a href={`mailto:${snapshot.email}`} className="govuk-link" data-testid="organisation-admin-deleted-user-email-link">
                      {snapshot.email}
                    </a>
                  ) : (
                    '—'
                  ),
                },
              ]}
            />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Sign in security" testId="organisation-admin-deleted-user-security">
            <SummaryList testId="organisation-admin-deleted-user-security-list" items={[{ label: 'User access', value: 'Deleted' }]} />
          </GovUKSummaryCard>

          <GovUKSummaryCard title="Record information" testId="organisation-admin-deleted-user-record">
            <SummaryList
              testId="organisation-admin-deleted-user-record-list"
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
        </>
      )}
    </OrganisationManagementLayout>
  );
};

export default DeletedUserView;

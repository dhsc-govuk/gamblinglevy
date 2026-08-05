import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Link as RouterLink } from 'react-router-dom';
import { useQuery, useQueries } from 'react-query';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKTable, GovUKInsetText, NameLink, H1, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import { HISTORICAL_ROLE_LABELS, useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { getOrganisationAdminAuditLogs, getOrganisationAdminAuditLog, type AuditLogListItem, type AuditLogsResponse } from '../../../Admin/AuditLog/queries';

interface DeletedUser {
  id: string;
  targetId: string;
  name: string;
  roleName: string;
  occurredAt: string;
}

const sortOptions: FilterOption[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'name-asc', label: 'Name A–Z' },
  { value: 'name-desc', label: 'Name Z–A' },
];

const columns: Column<DeletedUser>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (u) => <NameLink name={u.name} href={`/organisation-admin/users/deleted/${u.targetId}`} />,
  },
  {
    key: 'roleName',
    header: 'Access level',
    render: (u) => u.roleName || '—',
  },
];

interface DeletedUserSnapshot {
  firstName?: string;
  lastName?: string;
  role?: string;
}

const getAuditLogItems = (data: AuditLogsResponse | undefined): AuditLogListItem[] => (Array.isArray(data) ? data : data?.items ?? []);

const parseSnapshot = (before: string | null | undefined): DeletedUserSnapshot => {
  if (!before) return {};
  try {
    return JSON.parse(before) as DeletedUserSnapshot;
  } catch {
    return {};
  }
};

const getSnapshotName = (snapshot: DeletedUserSnapshot): string => {
  const name = [snapshot.firstName, snapshot.lastName].filter(Boolean).join(' ').trim();
  return name || '—';
};

const DeletedUsersList = (): React.ReactElement => {
  usePageTitle('Deleted users');
  const { organisationId } = useAuthProvider();

  const { data: listData, isLoading: listLoading } = useQuery(
    ['org-deleted-users-list', organisationId],
    () => getOrganisationAdminAuditLogs({ action: 'user.deleted', pageSize: 200 }),
    { enabled: !!organisationId }
  );

  const items = getAuditLogItems(listData?.data);

  const detailQueries = useQueries(
    items.map((item) => ({
      queryKey: ['org-deleted-user-detail', item.id],
      queryFn: () => getOrganisationAdminAuditLog(item.id),
      enabled: items.length > 0,
    }))
  );

  const deletedUsers: DeletedUser[] = items.map((item, i) => {
    const detail = detailQueries[i]?.data?.data;
    const snapshot = parseSnapshot(detail?.before);
    const roleName = HISTORICAL_ROLE_LABELS[snapshot.role ?? ''] ?? snapshot.role ?? '';

    return {
      id: item.id,
      targetId: item.targetId,
      name: item.targetDisplayName ?? getSnapshotName(snapshot),
      roleName,
      occurredAt: item.occurredAt,
    };
  });

  const isLoading = listLoading || detailQueries.some((q) => q.isLoading);

  return (
    <OrganisationManagementLayout
      breadcrumbs={[
        { label: 'Home', link: '/organisation-admin/home' },
        { label: 'Manage account', link: '/organisation-admin/users' },
        { label: 'Users', link: '/organisation-admin/users' },
      ]}>
      <H1>Deleted users</H1>

      {isLoading ? (
        <PageLoading />
      ) : deletedUsers.length === 0 ? (
        <>
          <GovUKInsetText data-testid="organisation-admin-deleted-users-empty">There are no deleted users. Any accounts you delete will appear here.</GovUKInsetText>
          <RouterLink to="/organisation-admin/users" className="govuk-link" data-testid="organisation-admin-deleted-users-return">
            Return to users
          </RouterLink>
        </>
      ) : (
        <>
          <GovUKTable<DeletedUser>
            testId="organisation-admin-deleted-users-table"
            data={deletedUsers}
            columns={columns}
            keyExtractor={(u) => u.id}
            searchPlaceholder="Search by name"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            getRowHref={(u) => `/organisation-admin/users/deleted/${u.targetId}`}
            emptyMessage="No deleted users found."
          />
          <RouterLink to="/organisation-admin/users" className="govuk-link" data-testid="organisation-admin-deleted-users-return">
            Return to users
          </RouterLink>
        </>
      )}
    </OrganisationManagementLayout>
  );
};

export default DeletedUsersList;

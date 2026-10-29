import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useQueries, useQuery } from '@tanstack/react-query';
import { composeTestId } from '../../../../helpers/testIds';
import { ManagementLayout } from '../../../../layouts';
import { GovUKInsetText, GovUKTable, NameLink, H1, PageLoading, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import { homeLinkForRole, EnumUserRole, HISTORICAL_ROLE_LABELS } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { getAuditLog, getAuditLogs, AuditLogListItem } from '../../../Admin/AuditLog/queries';
import { defaultStaleTime, deletedUsersListKey, deletedUserDetailKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { accessLevelFilterOptions } from '../accessLevels';
import './styles.css';

const sortOptions: FilterOption[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'name-asc', label: 'Name A–Z' },
  { value: 'name-desc', label: 'Name Z–A' },
];

// The name field must be called `name`: GovUKTable's Name A–Z / Z–A sort reads `name` (or firstName + lastName),
// so under `displayName` every row compared equal and the sort silently did nothing (BLUM-9004).
interface DeletedUserRow extends AuditLogListItem {
  name: string;
  // The raw snapshot role ('' when the snapshot has none), which the access-level filter matches exactly.
  role: string;
  accessLevel: string;
}

interface UserSnapshot {
  firstName?: string;
  lastName?: string;
  role?: string;
}

const parseSnapshot = (before: string | null | undefined): UserSnapshot => {
  if (!before) return {};
  try {
    return JSON.parse(before) as UserSnapshot;
  } catch {
    return {};
  }
};

const columns: Column<DeletedUserRow>[] = [
  {
    // Not renamed with the field: the key only names the cell testid (…-cell-displayName), which QA automation keys on.
    key: 'displayName',
    header: 'Name',
    className: 'admin-deleted-users-list__column',
    render: (entry) => (
      <NameLink name={entry.name} href={ROUTES.admin.users.deletedDetail(entry.targetId)} testId={composeTestId('admin-deleted-users-table', 'row', entry.id, 'name-link')} />
    ),
  },
  {
    key: 'organisationName',
    header: 'Organisation',
    className: 'admin-deleted-users-list__column',
    render: (entry) => entry.organisationName ?? '—',
  },
  {
    key: 'accessLevel',
    header: 'Access level',
    className: 'admin-deleted-users-list__column',
    render: (entry) => entry.accessLevel,
  },
];

const DeletedUsersList = (): React.ReactElement => {
  usePageTitle('Deleted users');
  const { data, isLoading: listLoading } = useQuery({ queryKey: deletedUsersListKey, queryFn: () => getAuditLogs({ action: 'user.deleted', pageSize: 200 }) });

  const entries = data?.data?.items ?? [];

  // The audit-log list response doesn't carry the deletion event's `before` snapshot
  // (firstName/lastName/role live there). Fan out a parallel detail-fetch per row so
  // the Name and Access level columns populate. Cached per-event via React Query, so
  // pagination/refilter won't re-hit the network.
  const detailQueries = useQueries({
    queries: entries.map((entry) => ({
      queryKey: deletedUserDetailKey(entry.id),
      queryFn: () => getAuditLog(entry.id),
      enabled: !!entry.id,
      staleTime: defaultStaleTime,
    })),
  });

  const rows: DeletedUserRow[] = entries.map((entry, i) => {
    const snapshot = parseSnapshot(detailQueries[i]?.data?.data?.before);
    const fullName = snapshot.firstName && snapshot.lastName ? `${snapshot.firstName} ${snapshot.lastName}` : entry.targetDisplayName ?? '—';
    const accessLevel = snapshot.role ? HISTORICAL_ROLE_LABELS[snapshot.role] ?? snapshot.role : '—';
    return { ...entry, name: fullName, role: snapshot.role ?? '', accessLevel };
  });
  const isLoading = listLoading || detailQueries.some((query) => query.isLoading);

  // The active Users page's three levels, plus any role retired in BLUM-7038 that a deletion snapshot still
  // carries: audit history is never rewritten, so without these a row labelled "LA contributor" could be
  // seen under "All access levels" but never isolated.
  const retiredRoleOptions: FilterOption[] = [...new Set(rows.map((row) => row.role))]
    .filter((role) => role && !accessLevelFilterOptions.some((option) => option.value === role))
    .map((role) => ({ value: role, label: HISTORICAL_ROLE_LABELS[role] ?? role }))
    .sort((a, b) => a.label.localeCompare(b.label));
  const filterOptions = [...accessLevelFilterOptions, ...retiredRoleOptions];

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) },
        { label: 'Management', link: ROUTES.admin.management },
        { label: 'Users', link: ROUTES.admin.users.list },
      ]}>
      <div className="admin-deleted-users-list">
        <H1 className="admin-deleted-users-list__heading">Deleted users</H1>

        {isLoading ? (
          <PageLoading />
        ) : rows.length === 0 ? (
          <GovUKInsetText className="admin-deleted-users-list__empty" data-testid="admin-deleted-users-empty">
            There are no deleted users. Any accounts you delete will appear here.
          </GovUKInsetText>
        ) : (
          <div className="admin-deleted-users-list__table">
            <GovUKTable<DeletedUserRow>
              label="Deleted users"
              testId="admin-deleted-users-table"
              data={rows}
              columns={columns}
              keyExtractor={(e) => e.id}
              searchPlaceholder="Search by name"
              searchField="name"
              sortOptions={sortOptions}
              sortLabel="Sort by"
              filterOptions={filterOptions}
              filterLabel="Filter by access level"
              filterPlaceholder="All access levels"
              filterField="role"
              getRowHref={(e) => ROUTES.admin.users.deletedDetail(e.targetId)}
              emptyMessage="No deleted users found."
              noResultsMessage="No deleted users match your search or filter."
            />
          </div>
        )}

        {!isLoading && (
          <RouterLink to={ROUTES.admin.users.list} className="govuk-link admin-deleted-users-list__return" data-testid="admin-deleted-users-return">
            Return to users
          </RouterLink>
        )}
      </div>
    </ManagementLayout>
  );
};

export default DeletedUsersList;

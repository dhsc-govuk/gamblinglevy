import React, { useMemo } from 'react';
import { composeTestId } from '../../../../helpers/testIds';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKTable, GovUKNotificationBanner, NameLink, GovUKTag, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import { ROLE_LABELS, useAuthProvider } from '../../../../components/AuthProvider';
import { canManageUsers } from '../../navigation';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../../helpers/queriesParams';
import { getOrganisationUserList, OrganisationUserDto } from '../../../../components/Global/OrganisationUsers/api';
import { AccessLevelGuidance, laAccessLevelFilterOptions } from '../../../../components/Global/OrganisationUsers';
import './styles.css';

type EnrichedOrgUser = OrganisationUserDto & { isActive: boolean; searchText: string };

interface BannerState {
  banner?: { title: string; body: string };
}

const sortOptions: FilterOption[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'name-asc', label: 'Name A–Z' },
  { value: 'name-desc', label: 'Name Z–A' },
  { value: 'status-asc', label: 'By status' },
];

const columns: Column<EnrichedOrgUser>[] = [
  {
    key: 'firstName',
    header: 'Name',
    render: (u) => (
      <NameLink
        name={`${u.firstName} ${u.lastName}`}
        href={`/organisation-admin/users/${u.id}`}
        testId={composeTestId('organisation-admin-users-table', 'row', u.id, 'name-link')}
      />
    ),
  },
  {
    key: 'role',
    header: 'Access level',
    render: (u) => ROLE_LABELS[u.role] ?? u.role,
  },
  {
    key: 'status',
    header: 'Status',
    render: (u) => (u.status === 'active' ? <>Active</> : <GovUKTag colour="yellow">Inactive</GovUKTag>),
  },
];

const UsersList = (): React.ReactElement => {
  usePageTitle('Users');
  const { organisationId, userRole } = useAuthProvider();
  const canManage = canManageUsers(userRole);
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;

  const { data, isLoading } = useQuery({
    queryKey: organisationUsersListKey(organisationId ?? ''),
    queryFn: () => getOrganisationUserList(organisationId!),
    enabled: !!organisationId,
  });

  // Memoised so the array identity is stable: GovUKTable's search/filter/sort useMemo keys on `data`, and
  // an inline .map() hands it a fresh array on every render.
  const users: EnrichedOrgUser[] = useMemo(
    () =>
      (data?.data ?? []).map((u) => ({
        ...u,
        // Boolean, not 0/1: GovUKTable's comparator has string, boolean and Date branches and no number
        // branch, so a numeric isActive fell through to `return 0` and the "By status" sort silently did
        // nothing while the live region still announced it. The boolean branch sorts active-first for asc.
        isActive: u.status === 'active',
        // Name and email only. This list previously set no searchField at all, so GovUKTable fell back to
        // scanning every string property: email matched only by accident, alongside the id GUID, role,
        // status and mfaStatus. Narrowing it is the point — the label never promised those.
        searchText: `${u.firstName} ${u.lastName} ${u.email}`,
      })),
    [data]
  );

  if (!organisationId) {
    return <PageLoading />;
  }

  return (
    <OrganisationManagementLayout
      breadcrumbs={[
        { label: 'Home', link: '/organisation-admin/home' },
        { label: 'Management', link: '/organisation-admin/users' },
      ]}>
      {banner && (
        <GovUKNotificationBanner type="success" title={banner.title} testId="organisation-admin-users-list-banner">
          <p className="govuk-body">{banner.body}</p>
        </GovUKNotificationBanner>
      )}

      {/* An LA lead sees the list read-only: no add, no deleted-users link, and no management copy. */}
      <div className="govuk-button-group" style={{ justifyContent: 'space-between', marginBottom: '20px' }}>
        <H1 style={{ marginBottom: 0 }}>Users</H1>
        {canManage && (
          <RouterLink to="/organisation-admin/users/create" className="govuk-button" data-testid="organisation-admin-users-list-add-button">
            Add user
          </RouterLink>
        )}
      </div>

      {canManage && <Paragraph>You can check your answers and make changes before the user is added.</Paragraph>}

      <AccessLevelGuidance view="la" canAddUsers={canManage} />

      {isLoading ? (
        <PageLoading />
      ) : (
        <div className="organisation-admin-users-list">
          <GovUKTable<EnrichedOrgUser>
            label="Users"
            testId="organisation-admin-users-table"
            data={users}
            columns={columns}
            keyExtractor={(u) => u.id}
            searchPlaceholder="Search by name or email"
            searchField="searchText"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            filterOptions={laAccessLevelFilterOptions}
            filterLabel="Filter by access level"
            filterPlaceholder="All access levels"
            filterField="role"
            getRowHref={(u) => `/organisation-admin/users/${u.id}`}
            emptyMessage="No users yet."
            noResultsMessage="No users match your search or filters."
          />
        </div>
      )}

      {canManage && (
        <RouterLink to="/organisation-admin/users/deleted" className="govuk-link" data-testid="organisation-admin-users-list-view-deleted">
          View deleted users
        </RouterLink>
      )}
    </OrganisationManagementLayout>
  );
};

export default UsersList;

import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
import { OrganisationManagementLayout } from '../../../../layouts';
import { GovUKTable, GovUKNotificationBanner, NameLink, GovUKTag, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import { ROLE_LABELS, useAuthProvider } from '../../../../components/AuthProvider';
import { canManageUsers } from '../../navigation';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../../helpers/queriesParams';
import { getOrganisationUserList, OrganisationUserDto } from '../../../../components/Global/OrganisationUsers/api';
import { AccessLevelGuidance } from '../../../../components/Global/OrganisationUsers';

type EnrichedOrgUser = OrganisationUserDto & { isActive: number };

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
    render: (u) => <NameLink name={`${u.firstName} ${u.lastName}`} href={`/organisation-admin/users/${u.id}`} />,
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

  const { data, isLoading } = useQuery(organisationUsersListKey(organisationId ?? ''), () => getOrganisationUserList(organisationId!), { enabled: !!organisationId });

  const users: EnrichedOrgUser[] = (data?.data ?? []).map((u) => ({
    ...u,
    isActive: u.status === 'active' ? 1 : 0,
  }));

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
        <GovUKTable<EnrichedOrgUser>
          testId="organisation-admin-users-table"
          data={users}
          columns={columns}
          keyExtractor={(u) => u.id}
          searchPlaceholder="Search by name"
          sortOptions={sortOptions}
          sortLabel="Sort by"
          getRowHref={(u) => `/organisation-admin/users/${u.id}`}
          emptyMessage="No users yet."
        />
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

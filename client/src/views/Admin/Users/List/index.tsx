import React from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKTable, GovUKNotificationBanner, GovUKInsetText, NameLink, GovUKTag, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getUserList, AdminUserDto } from '../queries';
import { ROLE_LABELS, homeLinkForRole, EnumUserRole } from '../../../../components/AuthProvider';
import { AccessLevelGuidance } from '../../../../components/Global/OrganisationUsers';
import './styles.css';

type EnrichedUser = AdminUserDto & { isActive: number };

interface BannerState {
  banner?: { title: string; body: string };
}

const sortOptions: FilterOption[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'name-asc', label: 'Name A–Z' },
  { value: 'name-desc', label: 'Name Z–A' },
  { value: 'status-asc', label: 'By status' },
];

const columns: Column<EnrichedUser>[] = [
  {
    key: 'firstName',
    header: 'Name',
    className: 'admin-users-list__column admin-users-list__column--name',
    render: (u) => <NameLink name={`${u.firstName} ${u.lastName}`} href={ROUTES.admin.users.detail(u.id)} />,
  },
  {
    key: 'organisationName',
    header: 'Organisation',
    className: 'admin-users-list__column admin-users-list__column--organisation',
    render: (u) => u.organisationName ?? '—',
  },
  {
    key: 'role',
    header: 'Access level',
    className: 'admin-users-list__column admin-users-list__column--access-level',
    render: (u) => ROLE_LABELS[u.role] ?? u.role,
  },
  {
    key: 'status',
    header: 'Status',
    className: 'admin-users-list__column admin-users-list__column--status',
    align: 'right',
    render: (u) => (u.status === 'active' ? <>Active</> : <GovUKTag colour="yellow">Inactive</GovUKTag>),
  },
];

const UsersList = (): React.ReactElement => {
  usePageTitle('Users');
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;
  const { data, isLoading } = useQuery(adminUsersListKey, getUserList);

  const users: EnrichedUser[] = (data?.data ?? []).map((u) => ({
    ...u,
    isActive: u.status === 'active' ? 1 : 0,
  }));

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: homeLinkForRole(EnumUserRole.ADMIN) },
        { label: 'Management', link: ROUTES.admin.management },
      ]}>
      <div className="admin-users-list">
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-users-list-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}

        <H1 className="admin-users-list__heading">Users</H1>

        <div className="admin-users-list__introduction">
          <Paragraph>
            You must{' '}
            <RouterLink to={ROUTES.admin.organisations.create} className="govuk-link">
              set up an organisation
            </RouterLink>{' '}
            before you can add users to it.
          </Paragraph>
          <Paragraph>You can check your answers and make changes before the user is added.</Paragraph>
        </div>

        <AccessLevelGuidance view="dhsc" className="admin-users-list__access-levels" />

        <div className="admin-users-list__add-user">
          <RouterLink to={ROUTES.admin.users.create} className="govuk-button" data-testid="admin-users-list-add-button">
            Add user
          </RouterLink>
        </div>

        {!isLoading &&
          (users.length === 0 ? (
            <GovUKInsetText className="admin-users-list__empty-message">
              You have not added any users. When you add a user, their details will be listed on this page.
            </GovUKInsetText>
          ) : (
            <div className="admin-users-list__table">
              <GovUKTable<EnrichedUser>
                testId="admin-users-table"
                data={users}
                columns={columns}
                keyExtractor={(u) => u.id}
                searchPlaceholder="Search by name"
                sortOptions={sortOptions}
                sortLabel="Sort by"
                getRowHref={(u) => ROUTES.admin.users.detail(u.id)}
              />
            </div>
          ))}

        <RouterLink to={ROUTES.admin.users.deleted} className="govuk-link admin-users-list__deleted-users" data-testid="admin-users-list-view-deleted">
          View deleted users
        </RouterLink>
      </div>
    </ManagementLayout>
  );
};

export default UsersList;

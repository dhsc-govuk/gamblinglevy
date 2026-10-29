import React, { useMemo } from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { composeTestId } from '../../../../helpers/testIds';
import { ManagementLayout } from '../../../../layouts';
import { GovUKTable, GovUKNotificationBanner, GovUKInsetText, NameLink, GovUKTag, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { adminUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getUserList, AdminUserDto } from '../queries';
import { accessLevelFilterOptions } from '../accessLevels';
import { ROLE_LABELS, homeLinkForRole, EnumUserRole } from '../../../../components/AuthProvider';
import { AccessLevelGuidance } from '../../../../components/Global/OrganisationUsers';
import './styles.css';

type EnrichedUser = AdminUserDto & { isActive: boolean; searchText: string };

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
    render: (u) => <NameLink name={`${u.firstName} ${u.lastName}`} href={ROUTES.admin.users.detail(u.id)} testId={composeTestId('admin-users-table', 'row', u.id, 'name-link')} />,
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
  const { data, isLoading } = useQuery({ queryKey: adminUsersListKey, queryFn: getUserList });

  // Memoised so the array identity is stable: GovUKTable's search/filter/sort useMemo keys on `data`, and
  // the inline .map() this replaces handed it a fresh array on every render.
  const users: EnrichedUser[] = useMemo(
    () =>
      (data?.data ?? []).map((u) => ({
        ...u,
        // Boolean, not 0/1: GovUKTable's comparator has string, boolean and Date branches and no number
        // branch, so a numeric isActive fell through to `return 0` and the "By status" sort silently did
        // nothing while the live region still announced it. The boolean branch sorts active-first for asc.
        isActive: u.status === 'active',
        // Name and email only. GovUKTable matches a single field, so this is how the repo searches two
        // (same idiom as Admin/Organisations/List's name + ONS code). Email is deliberately in: it is often
        // the only identifier on a support ticket, and this table has no Email column to read it from.
        // Everything else is deliberately out — without a searchField GovUKTable scans every string property,
        // so "lead" behaved as a second access-level filter contradicting the dropdown beside it, and a
        // single hex character matched almost every row via the id GUID.
        // The label and this field must move together: searchPlaceholder is what tells the user what is
        // searched, and nothing fails if they disagree.
        searchText: `${u.firstName} ${u.lastName} ${u.email}`,
      })),
    [data]
  );

  // Derived from the rows rather than from getLocalAuthorities(): the filter matches on organisationName, so
  // the organisations endpoint would only add a request, a failure state and options that match no rows.
  // Non-empty names only — an empty string would collide with the clear placeholder and match everything.
  // Super Admins have no organisation and are correctly unreachable here; "Access level: Super Admin" is
  // that view.
  const organisationFilterOptions: FilterOption[] = useMemo(() => {
    const names = [...new Set(users.map((u) => u.organisationName).filter((name): name is string => !!name))];
    return names.sort((a, b) => a.localeCompare(b)).map((name) => ({ value: name, label: name }));
  }, [users]);

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
            <RouterLink to={ROUTES.admin.organisations.create} className="govuk-link" data-testid="admin-users-list-create-organisation-link">
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

        {/* Roster-empty is answered here, outside the table, rather than through GovUKTable's emptyMessage as
            the two organisation user lists do. Deliberate, and do not "unify" the three: this page renders its
            own Add user button above, whereas UsersListView passes addButtonLabel/addButtonHref to GovUKTable —
            so short-circuiting the table there hides the Create user button on exactly the empty roster that
            needs it. That copy is also a richer empty state than a single table cell can carry. */}
        {!isLoading &&
          (users.length === 0 ? (
            <GovUKInsetText className="admin-users-list__empty-message">
              You have not added any users. When you add a user, their details will be listed on this page.
            </GovUKInsetText>
          ) : (
            <div className="admin-users-list__table">
              <GovUKTable<EnrichedUser>
                label="Users"
                testId="admin-users-table"
                data={users}
                columns={columns}
                keyExtractor={(u) => u.id}
                searchPlaceholder="Search by name or email"
                searchField="searchText"
                sortOptions={sortOptions}
                sortLabel="Sort by"
                filterOptions={organisationFilterOptions}
                filterLabel="Filter by organisation"
                filterPlaceholder="All organisations"
                filterField="organisationName"
                secondaryFilterOptions={accessLevelFilterOptions}
                secondaryFilterLabel="Filter by access level"
                secondaryFilterPlaceholder="All access levels"
                secondaryFilterField="role"
                noResultsMessage="No users match your search or filters."
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

import React, { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { composeTestId } from '../../../helpers/testIds';
import { GeneralLayout } from '../../../layouts';
import { GovUKTable, NameLink, GovUKTag, type Column, type FilterOption } from '../../GovUKComponents';
import { ROLE_LABELS } from '../../AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../helpers/queriesParams';
import { getOrganisationUserList, OrganisationUserDto } from './api';
import { laAccessLevelFilterOptions } from './accessLevels';
import './UsersListView.css';

const statusFilterOptions: FilterOption[] = [
  { value: 'active', label: 'Active' },
  { value: 'deactivated', label: 'Deactivated' },
];

type UserRow = OrganisationUserDto & { searchText: string };

export interface Breadcrumb {
  label: string;
  link: string;
}

export interface UsersListViewProps {
  organisationId: string;
  pageTitle: string;
  tableTitle: string;
  breadcrumbs: Breadcrumb[];
  createHref: string;
  rowHrefFn: (userId: string) => string;
  testIdPrefix: string;
  emptyMessage?: string;
}

const UsersListView = ({
  organisationId,
  pageTitle,
  tableTitle,
  breadcrumbs,
  createHref,
  rowHrefFn,
  testIdPrefix,
  emptyMessage = 'No LA users yet.',
}: UsersListViewProps): React.ReactElement => {
  usePageTitle(pageTitle);

  const { data, isLoading } = useQuery({ queryKey: organisationUsersListKey(organisationId), queryFn: () => getOrganisationUserList(organisationId), enabled: !!organisationId });

  // Memoised so the array identity is stable: GovUKTable's search/filter/sort useMemo keys on `data`.
  const users: UserRow[] = useMemo(
    () =>
      (data?.data ?? []).map((u) => ({
        ...u,
        // The label has always read "Search by name or email", but with no searchField GovUKTable fell back
        // to scanning every string property, so it also matched the id GUID, role, status and mfaStatus.
        // This makes the label honest rather than changing what it promises. GovUKTable matches one field,
        // so a derived string is how the repo searches two (same idiom as Admin/Organisations/List).
        searchText: `${u.firstName} ${u.lastName} ${u.email}`,
      })),
    [data]
  );

  const columns: Column<UserRow>[] = [
    {
      key: 'firstName',
      header: 'Name',
      render: (u) => <NameLink name={`${u.firstName} ${u.lastName}`} href={rowHrefFn(u.id)} testId={composeTestId(testIdPrefix, 'table', 'row', u.id, 'name-link')} />,
    },
    { key: 'email', header: 'Email' },
    {
      key: 'role',
      header: 'Role',
      render: (u) => ROLE_LABELS[u.role] ?? u.role,
    },
    {
      key: 'status',
      header: 'Status',
      render: (u) => <GovUKTag colour={u.status === 'active' ? 'green' : 'grey'}>{u.status === 'active' ? 'Active' : 'Deactivated'}</GovUKTag>,
    },
  ];

  return (
    <GeneralLayout breadcrumbs={breadcrumbs}>
      <div className="la-users-list-page">
        {isLoading ? (
          <p className="govuk-body">Loading…</p>
        ) : (
          <GovUKTable<UserRow>
            testId={`${testIdPrefix}-table`}
            title={tableTitle}
            data={users}
            columns={columns}
            keyExtractor={(u) => u.id}
            searchPlaceholder="Search by name or email"
            searchField="searchText"
            filterOptions={statusFilterOptions}
            filterLabel="Filter by status"
            filterPlaceholder="All statuses"
            filterField="status"
            secondaryFilterOptions={laAccessLevelFilterOptions}
            secondaryFilterLabel="Filter by access level"
            secondaryFilterPlaceholder="All access levels"
            secondaryFilterField="role"
            addButtonLabel="Create user"
            addButtonHref={createHref}
            getRowHref={(u) => rowHrefFn(u.id)}
            emptyMessage={emptyMessage}
            noResultsMessage="No users match your search or filters."
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default UsersListView;

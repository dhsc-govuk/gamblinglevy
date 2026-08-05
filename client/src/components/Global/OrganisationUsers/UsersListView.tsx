import React from 'react';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import {
  GovUKTable,
  NameLink,
  GovUKTag,
  type Column,
  type FilterOption,
} from '../../GovUKComponents';
import { ROLE_LABELS } from '../../AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../helpers/queriesParams';
import { getOrganisationUserList, OrganisationUserDto } from './api';

const statusFilterOptions: FilterOption[] = [
  { value: 'active', label: 'Active' },
  { value: 'deactivated', label: 'Deactivated' },
];

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

  const { data, isLoading } = useQuery(
    organisationUsersListKey(organisationId),
    () => getOrganisationUserList(organisationId),
    { enabled: !!organisationId },
  );

  const users = data?.data ?? [];

  const columns: Column<OrganisationUserDto>[] = [
    {
      key: 'firstName',
      header: 'Name',
      render: (u) => <NameLink name={`${u.firstName} ${u.lastName}`} href={rowHrefFn(u.id)} />,
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
      render: (u) => (
        <GovUKTag colour={u.status === 'active' ? 'green' : 'grey'}>
          {u.status === 'active' ? 'Active' : 'Deactivated'}
        </GovUKTag>
      ),
    },
  ];

  return (
    <GeneralLayout breadcrumbs={breadcrumbs}>
      <div style={{ paddingTop: '20px' }}>
        {isLoading ? (
          <p className="govuk-body">Loading…</p>
        ) : (
          <GovUKTable<OrganisationUserDto>
            testId={`${testIdPrefix}-table`}
            title={tableTitle}
            data={users}
            columns={columns}
            keyExtractor={(u) => u.id}
            searchPlaceholder="Search by name or email"
            filterOptions={statusFilterOptions}
            filterLabel="Filter by status"
            filterField="status"
            addButtonLabel="Create user"
            addButtonHref={createHref}
            getRowHref={(u) => rowHrefFn(u.id)}
            emptyMessage={emptyMessage}
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default UsersListView;

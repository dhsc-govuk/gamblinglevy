import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { composeTestId } from '../../../../helpers/testIds';
import { ManagementLayout } from '../../../../layouts';
import { GovUKTable, H1, NameLink, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { deletedOrganisationsListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getAuditLogs, AuditLogListItem } from '../../AuditLog/queries';
import './styles.css';

const sortOptions: FilterOption[] = [
  { value: 'name-asc', label: 'Name A-Z' },
  { value: 'name-desc', label: 'Name Z-A' },
];

type DeletedOrganisationRow = AuditLogListItem & {
  name: string;
  onsCode: string;
  searchText: string;
};

const columns: Column<DeletedOrganisationRow>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (entry) => (
      <NameLink name={entry.name} href={ROUTES.admin.auditLogs.detail(entry.id)} testId={composeTestId('admin-deleted-organisations-table', 'row', entry.id, 'name-link')} />
    ),
    headerStyle: { width: '320px' },
    cellStyle: { width: '320px' },
  },
  {
    key: 'onsCode',
    header: 'ONS code',
    render: (entry) => entry.onsCode,
    headerStyle: { width: '343px' },
    cellStyle: { width: '343px' },
  },
];

const readOnsCode = (entry: AuditLogListItem): string => {
  const snapshot = (entry as AuditLogListItem & { before?: string | null; after?: string | null }).before ?? (entry as AuditLogListItem & { after?: string | null }).after;
  if (!snapshot) return '—';

  try {
    const parsed = JSON.parse(snapshot) as { onsCode?: unknown };
    return typeof parsed.onsCode === 'string' && parsed.onsCode.trim() ? parsed.onsCode : '—';
  } catch {
    return '—';
  }
};

const DeletedOrganisationsList = (): React.ReactElement => {
  usePageTitle('Deleted organisations');
  const { data } = useQuery({ queryKey: deletedOrganisationsListKey, queryFn: () => getAuditLogs({ action: 'organisation.deleted', pageSize: 200 }) });

  const rows: DeletedOrganisationRow[] = (data?.data?.items ?? []).map((entry) => ({
    ...entry,
    name: entry.targetDisplayName ?? entry.organisationName ?? 'Deleted organisation',
    onsCode: entry.onsCode ?? readOnsCode(entry),
    searchText: `${entry.targetDisplayName ?? entry.organisationName ?? 'Deleted organisation'} ${entry.onsCode ?? readOnsCode(entry)}`.trim(),
  }));

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Management', link: ROUTES.admin.organisations.list },
        { label: 'Organisations', link: ROUTES.admin.organisations.list },
      ]}>
      <div className="deleted-organisations-page">
        <H1>Deleted organisations</H1>

        <GovUKTable<DeletedOrganisationRow>
          label="Deleted organisations"
          testId="admin-deleted-organisations-table"
          data={rows}
          columns={columns}
          keyExtractor={(entry) => entry.id}
          searchPlaceholder="Search by name or ONS code"
          searchField="searchText"
          sortOptions={sortOptions}
          sortLabel="Sort by"
          emptyMessage="No deleted organisations."
        />

        <RouterLink to={ROUTES.admin.organisations.list} className="govuk-link" data-testid="admin-deleted-organisations-return">
          Return to organisations
        </RouterLink>
      </div>
    </ManagementLayout>
  );
};

export default DeletedOrganisationsList;

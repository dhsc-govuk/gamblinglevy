import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useQuery } from 'react-query';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../../layouts';
import { GovUKTable, GovUKInsetText, GovUKTag, H1, H2, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import CollectionStatusTag from '../../../../components/Global/Collections/CollectionStatusTag';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey } from '../../../../helpers/queriesParams';
import { getCollections, type CollectionSummaryDto, type CollectionType } from '../../Collections/queries';
import { formatGovUkDate, REPORT_COLLECTION_STATUS_LABEL, typeLabel } from '../display';
import { reportsListBreadcrumbs } from '../navigation';
import './styles.css';

const sortOptions: FilterOption[] = [
  { value: 'deadline-asc', label: 'Deadline, earliest first' },
  { value: 'deadline-desc', label: 'Deadline, latest first' },
  { value: 'name-asc', label: 'Name (A to Z)' },
  { value: 'name-desc', label: 'Name (Z to A)' },
];

const statusFilterOptions: FilterOption[] = [
  { value: 'draft', label: REPORT_COLLECTION_STATUS_LABEL.draft },
  { value: 'scheduled', label: 'Scheduled' },
  { value: 'open', label: 'Open' },
  { value: 'closed', label: 'Closed' },
];

const typeFilterOptions: FilterOption[] = [
  { value: 'quarterly_report', label: 'Quarterly report' },
  { value: 'maturity_assessment', label: 'Maturity assessment' },
];

const ReportCollectionStatus = ({ collection }: { collection: CollectionSummaryDto }): React.ReactElement => {
  if (collection.status === 'draft') {
    return (
      <GovUKTag colour="grey" data-testid={`admin-reports-list-row-${collection.id}-status`}>
        {REPORT_COLLECTION_STATUS_LABEL.draft}
      </GovUKTag>
    );
  }

  return <CollectionStatusTag status={collection.status} testId={`admin-reports-list-row-${collection.id}-status`} />;
};

const columns: Column<CollectionSummaryDto>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (collection) => (
      <RouterLink to={`/admin/reports/${collection.id}`} className="govuk-link" data-testid={`admin-reports-list-row-${collection.id}-name`}>
        {collection.name}
      </RouterLink>
    ),
  },
  {
    // The list is currently filtered to quarterly reports, so this column reads the same on every row today. It is
    // kept intentionally for forward-compatibility (when more reportable collection types are surfaced here) — do
    // not remove it as "redundant".
    key: 'type',
    header: 'Type',
    render: (collection) => typeLabel(collection.type),
  },
  {
    key: 'deadline',
    header: 'Deadline',
    render: (collection) => formatGovUkDate(collection.deadline),
  },
  {
    key: 'status',
    header: 'Status',
    align: 'right',
    render: (collection) => <ReportCollectionStatus collection={collection} />,
  },
];

const isReportableCollection = (collection: CollectionSummaryDto): collection is CollectionSummaryDto & { type: CollectionType } => collection.type !== null;

const ReportsList = (): React.ReactElement => {
  usePageTitle('Reports');

  const { data, isLoading } = useQuery(collectionsListKey, getCollections);
  const rows = (data?.data ?? []).filter(isReportableCollection);

  return (
    <GeneralLayout breadcrumbs={reportsListBreadcrumbs()}>
      <div className="reports-list-page" data-testid="admin-reports-list-page">
        <H1 className="reports-list-page__title">Reports</H1>
        <Paragraph>You can view and download reports for data collections.</Paragraph>
        <Paragraph>To edit your data collections, select &lsquo;Manage data collections&rsquo;.</Paragraph>

        <div className="reports-list-page__header">
          <H2 className="reports-list-page__subheading">Your data collections</H2>
          <RouterLink to="/admin/collections" className="govuk-button govuk-button--secondary" data-testid="admin-reports-list-manage-button">
            Manage data collections
          </RouterLink>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : rows.length === 0 ? (
          <GovUKInsetText data-testid="admin-reports-list-empty">There are no data collections to report on yet. Once one has been published, it will appear here.</GovUKInsetText>
        ) : (
          <GovUKTable<CollectionSummaryDto>
            testId="admin-reports-list"
            data={rows}
            columns={columns}
            keyExtractor={(collection) => collection.id}
            searchable
            searchPlaceholder="Search by name"
            searchField="name"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            filterOptions={typeFilterOptions}
            filterLabel="Filter by type"
            filterPlaceholder="Select type"
            filterField="type"
            secondaryFilterOptions={statusFilterOptions}
            secondaryFilterLabel="Filter by status"
            secondaryFilterPlaceholder="Select status"
            secondaryFilterField="status"
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default ReportsList;

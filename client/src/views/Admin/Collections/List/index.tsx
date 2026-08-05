import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useQuery } from 'react-query';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { ManagementLayout } from '../../../../layouts';
import {
  GovUKTable,
  GovUKInsetText,
  GovUKNotificationBanner,
  H1,
  H2,
  Paragraph,
  UnorderedList,
  ListItem,
  type Column,
  type FilterOption,
} from '../../../../components/GovUKComponents';
import CollectionStatusTag from '../../../../components/Global/Collections/CollectionStatusTag';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsListKey } from '../../../../helpers/queriesParams';
import { getCollections, type CollectionSummaryDto } from '../queries';
import { formatGovUkDate } from '../display';
import { collectionBreadcrumbs } from '../navigation';
import './styles.css';

interface BannerState {
  banner?: { title: string; body: string };
}

const PAGE_TITLE = 'Data collections';

const sortOptions: FilterOption[] = [
  { value: 'name-asc', label: 'Name A-Z' },
  { value: 'name-desc', label: 'Name Z-A' },
];

const filterOptions: FilterOption[] = [
  { value: 'draft', label: 'Draft' },
  { value: 'scheduled', label: 'Scheduled' },
  { value: 'open', label: 'Open' },
  { value: 'closed', label: 'Closed' },
];

const columns: Column<CollectionSummaryDto>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (collection) => (
      <RouterLink
        to={collection.status === 'draft' ? `/admin/collections/${collection.id}/edit` : `/admin/collections/${collection.id}`}
        className="govuk-link"
        data-testid={`admin-collections-list-row-${collection.id}-name`}>
        {collection.name}
      </RouterLink>
    ),
  },
  {
    key: 'deadline',
    header: 'Deadline',
    render: (collection) => formatGovUkDate(collection.deadline),
  },
  {
    key: 'status',
    header: 'Status',
    render: (collection) => <CollectionStatusTag status={collection.status} testId={`admin-collections-list-row-${collection.id}-status`} />,
  },
];

const CollectionsList = (): React.ReactElement => {
  usePageTitle(PAGE_TITLE);
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;
  const breadcrumbs = collectionBreadcrumbs();

  const { data, isLoading } = useQuery(collectionsListKey, getCollections);
  const rows = data?.data ?? [];

  return (
    <ManagementLayout breadcrumbs={breadcrumbs}>
      <div className={`collections-list-page ${banner ? 'collections-list-page--has-banner' : ''}`}>
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-collections-list-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}

        <H1 className="collections-list-page__title">{PAGE_TITLE}</H1>

        <div className="collections-list-page__intro">
          <Paragraph>Use this section to collect data for a quarterly report or a maturity assessment.</Paragraph>
          <Paragraph>You can:</Paragraph>
          <UnorderedList>
            <ListItem>add a new data collection</ListItem>
            <ListItem>edit scheduled data collections</ListItem>
            <ListItem>send reminders to all reporting organisations, to ask them to submit their data</ListItem>
            <ListItem>close a data collection early</ListItem>
          </UnorderedList>
        </div>

        <div className="collections-list-page__header">
          <H2 className="collections-list-page__subheading">Your data collections</H2>
          <RouterLink to="/admin/collections/create" className="govuk-button" data-testid="admin-collections-list-add-button">
            Add data collection
          </RouterLink>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : rows.length === 0 ? (
          <GovUKInsetText data-testid="admin-collections-list-empty">You have not added any data collections. Once you have set one up, it will appear here.</GovUKInsetText>
        ) : (
          <GovUKTable<CollectionSummaryDto>
            testId="admin-collections-list"
            data={rows}
            columns={columns}
            keyExtractor={(collection) => collection.id}
            searchable
            searchPlaceholder="Search by name"
            searchField="name"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            filterOptions={filterOptions}
            filterLabel="Filter by status"
            filterPlaceholder="Select status..."
            filterField="status"
          />
        )}

        <p className="govuk-body collections-list-page__deleted-link">
          <RouterLink to="/admin/collections/deleted" className="govuk-link" data-testid="admin-collections-list-view-deleted">
            View deleted data collections
          </RouterLink>
        </p>
      </div>
    </ManagementLayout>
  );
};

export default CollectionsList;

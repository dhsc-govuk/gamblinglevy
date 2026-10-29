import React from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { composeTestId } from '../../../../helpers/testIds';
import { ManagementLayout } from '../../../../layouts';
import { GovUKNotificationBanner, GovUKTable, NameLink, GovUKTag, PageLoading, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getLocalAuthorities, LocalAuthorityDto, REGIONS } from '../queries';
import { displayOrganisationStatus } from '../display';
import './styles.css';

type OrganisationRow = LocalAuthorityDto & {
  searchText: string;
  // GovUKTable's generic 'status' sort branch compares a boolean isActive field, which only suits a two-state
  // status (like the Users lists' 'active'|'deactivated'). Organisations have four states — active, invited,
  // suspended, deactivated — so collapsing to a boolean would bucket three of them together with no defined
  // order. Sorting the displayed label instead, under a field name that isn't 'status', routes it through the
  // generic string-sort branch and orders rows the same way the column itself reads: Active, Deactivated, Invited.
  statusLabel: string;
};

interface BannerState {
  banner?: { title: string; body: string };
}

const sortOptions: FilterOption[] = [
  { value: 'name-asc', label: 'Name A-Z' },
  { value: 'name-desc', label: 'Name Z-A' },
  { value: 'onsCode-asc', label: 'ONS code' },
  { value: 'region-asc', label: 'Region' },
  { value: 'statusLabel-asc', label: 'Status' },
];

const regionFilterOptions: FilterOption[] = REGIONS.map((region) => ({ value: region, label: region }));

const columns: Column<OrganisationRow>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (o) => <NameLink name={o.name} href={ROUTES.admin.organisations.detail(o.id)} testId={composeTestId('admin-local-authorities-table', 'row', o.id, 'name-link')} />,
  },
  { key: 'onsCode', header: 'ONS code', render: (o) => o.onsCode ?? '—' },
  { key: 'region', header: 'Region', render: (o) => o.region },
  {
    key: 'status',
    header: 'Status',
    align: 'right',
    className: 'organisations-list-page__status-cell',
    headerStyle: { paddingRight: 0, width: 124 },
    cellStyle: { paddingRight: 0, width: 124 },
    render: (o) => (
      <span className="organisations-list-page__status">
        {o.onboardingStatus === 'active' ? displayOrganisationStatus(o.onboardingStatus) : <GovUKTag colour="yellow">{displayOrganisationStatus(o.onboardingStatus)}</GovUKTag>}
      </span>
    ),
  },
];

const LocalAuthoritiesList = (): React.ReactElement => {
  usePageTitle('Organisations');
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;
  const { data, isLoading } = useQuery({ queryKey: localAuthoritiesListKey(), queryFn: () => getLocalAuthorities() });
  const items: OrganisationRow[] = (data?.data ?? []).map((organisation) => ({
    ...organisation,
    searchText: `${organisation.name} ${organisation.onsCode ?? ''}`.trim(),
    statusLabel: displayOrganisationStatus(organisation.onboardingStatus),
  }));

  return (
    <ManagementLayout
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Management', link: ROUTES.admin.organisations.list },
      ]}>
      <div className={['organisations-list-page', banner ? 'organisations-list-page--has-banner' : ''].filter(Boolean).join(' ')}>
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-local-authorities-list-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}
        <H1>Organisations</H1>

        <div className="organisations-list-page__intro">
          <Paragraph>You must set up an organisation before you can assign users to it.</Paragraph>
          <Paragraph>You&rsquo;ll need to enter contact details for the people who will receive notifications for the organisation.</Paragraph>
          <Paragraph>You can check the details and make changes before the organisation is added.</Paragraph>
        </div>

        <div className="govuk-button-group organisations-list-page__actions">
          <RouterLink to={ROUTES.admin.organisations.bulkImport} className="govuk-button govuk-button--secondary" data-testid="admin-local-authorities-bulk-import-link">
            Bulk import from CSV
          </RouterLink>
          <RouterLink to={ROUTES.admin.organisations.create} className="govuk-button" data-testid="admin-local-authorities-add-button">
            Add organisation
          </RouterLink>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : (
          <GovUKTable<OrganisationRow>
            label="Organisations"
            testId="admin-local-authorities-table"
            data={items}
            columns={columns}
            keyExtractor={(o) => o.id}
            searchPlaceholder="Search by name or ONS code"
            searchField="searchText"
            sortOptions={sortOptions}
            sortLabel="Sort by"
            filterOptions={regionFilterOptions}
            filterLabel="Filter by region"
            filterPlaceholder="Select region"
            filterField="region"
            getRowHref={(o) => ROUTES.admin.organisations.detail(o.id)}
            emptyMessage="No organisations yet."
          />
        )}

        <RouterLink to={ROUTES.admin.organisations.deleted} className="govuk-link" data-testid="admin-local-authorities-view-deleted">
          View deleted organisations
        </RouterLink>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesList;

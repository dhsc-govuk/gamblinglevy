import React from 'react';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { useQuery } from 'react-query';
import { ManagementLayout } from '../../../../layouts';
import { GovUKNotificationBanner, GovUKTable, NameLink, GovUKTag, PageLoading, H1, Paragraph, type Column, type FilterOption } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getLocalAuthorities, LocalAuthorityDto } from '../queries';
import './styles.css';

type OrganisationRow = LocalAuthorityDto & {
  searchText: string;
};

interface BannerState {
  banner?: { title: string; body: string };
}

const sortOptions: FilterOption[] = [
  { value: 'name-asc', label: 'Name A-Z' },
  { value: 'name-desc', label: 'Name Z-A' },
];

const columns: Column<OrganisationRow>[] = [
  {
    key: 'name',
    header: 'Name',
    render: (o) => <NameLink name={o.name} href={ROUTES.admin.organisations.detail(o.id)} />,
  },
  { key: 'onsCode', header: 'ONS code', render: (o) => o.onsCode ?? '—' },
  {
    key: 'status',
    header: 'Status',
    align: 'right',
    className: 'organisations-list-page__status-cell',
    headerStyle: { paddingRight: 0, width: 124 },
    cellStyle: { paddingRight: 0, width: 124 },
    render: (o) => <span className="organisations-list-page__status">{o.onboardingStatus === 'active' ? <>Active</> : <GovUKTag colour="yellow">Inactive</GovUKTag>}</span>,
  },
];

const LocalAuthoritiesList = (): React.ReactElement => {
  usePageTitle('Organisations');
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;
  const { data, isLoading } = useQuery(localAuthoritiesListKey(), () => getLocalAuthorities());
  const items: OrganisationRow[] = (data?.data ?? []).map((organisation) => ({
    ...organisation,
    searchText: `${organisation.name} ${organisation.onsCode ?? ''}`.trim(),
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
            testId="admin-local-authorities-table"
            data={items}
            columns={columns}
            keyExtractor={(o) => o.id}
            searchPlaceholder="Search by name or ONS code"
            searchField="searchText"
            sortOptions={sortOptions}
            sortLabel="Sort by"
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

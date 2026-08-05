import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useQuery } from 'react-query';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { ManagementLayout } from '../../../../layouts';
import { GovUKInsetText, GovUKNotificationBanner, H1, Paragraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionsDeletedKey } from '../../../../helpers/queriesParams';
import { getDeletedCollections, type CollectionSummaryDto } from '../queries';
import './styles.css';

interface BannerState {
  banner?: { title: string; body: string; link?: { label: string; href: string } };
}

const CollectionsDeleted = (): React.ReactElement => {
  usePageTitle('Deleted data collections');
  const location = useLocation();
  const banner = ((location.state as BannerState | null) ?? {}).banner;
  const hasRestoredBanner = banner?.title === 'Data collection restored';

  const { data, isLoading } = useQuery(collectionsDeletedKey, getDeletedCollections);
  const rows = data?.data ?? [];

  return (
    <ManagementLayout backLink={{ href: '/admin/collections' }}>
      <div className="collections-deleted-page">
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="admin-collections-deleted-banner">
            <p className="govuk-body">
              {banner.body}
              {banner.link && (
                <RouterLink to={banner.link.href} className="govuk-link">
                  {banner.link.label}
                </RouterLink>
              )}
            </p>
          </GovUKNotificationBanner>
        )}

        <H1 className="collections-deleted-page__title">Deleted data collections</H1>
        <Paragraph>
          {hasRestoredBanner
            ? 'These are the data collections you deleted from your main list. You can restore any data collection to continue working on it.'
            : 'These are the data collections you deleted. If you want to work on one, you can restore it.'}
        </Paragraph>

        {isLoading ? (
          <PageLoading />
        ) : rows.length === 0 ? (
          <GovUKInsetText data-testid="admin-collections-deleted-empty">You have no deleted data collections.</GovUKInsetText>
        ) : (
          <div className="collections-deleted-page__list" data-testid="admin-collections-deleted-list">
            {rows.map((collection: CollectionSummaryDto) => (
              <div key={collection.id} className="collections-deleted-page__row" data-testid={`admin-collections-deleted-row-${collection.id}`}>
                <span className="govuk-body govuk-!-font-weight-bold govuk-!-margin-bottom-0 collections-deleted-page__name">{collection.name}</span>
                <RouterLink
                  to={`/admin/collections/${collection.id}/restore`}
                  state={{ name: collection.name }}
                  className="govuk-link"
                  data-testid={`admin-collections-deleted-row-${collection.id}-restore`}>
                  Restore<span className="govuk-visually-hidden"> {collection.name}</span>
                </RouterLink>
              </div>
            ))}
          </div>
        )}
      </div>
    </ManagementLayout>
  );
};

export default CollectionsDeleted;

import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink, useLocation } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { GovUKNotificationBanner, H1, Paragraph, SummaryList } from '../../../components/GovUKComponents';
import usePageTitle from '../../../hooks/usePageTitle';
import { activitiesDeletedKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { useActivityReportContext } from '../reportContext';
import { getDeletedActivities, type ActivitySummaryDto } from '../queries';

const ActivitiesDeleted = (): React.ReactElement => {
  usePageTitle('Deleted activities');
  const location = useLocation();
  const bannerState = (location.state as { banner?: string; name?: string } | null) ?? {};
  // Reachable from Section 1 of a quarterly return via "View deleted activities" — restoring an activity from here
  // must not eject the user from the return flow (BLUM-6967).
  const report = useActivityReportContext();

  const { data, isLoading } = useQuery({ queryKey: activitiesDeletedKey, queryFn: () => getDeletedActivities() });
  const activities = data?.data ?? [];

  return (
    // The design (Figma 420-30209) shows only a Back link in the chrome — no breadcrumbs — with the intro copy in a
    // two-thirds column and the list as summary-list rows (name + Restore action). It also omits the beta phase
    // banner, but that is now global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
    <GeneralLayout backLink={{ href: report.listPath, testId: 'activities-deleted-back' }}>
      <div data-testid="activities-deleted">
        {bannerState.banner === 'restored' && (
          <GovUKNotificationBanner type="success" title="Activity restored" testId="activities-deleted-banner">
            <p className="govuk-body">
              ‘{bannerState.name ?? 'The activity'}’ has been restored. To view it,{' '}
              <RouterLink to={report.listPath} className="govuk-link" data-testid="activities-deleted-banner-return">
                return to your list of activities
              </RouterLink>
              .
            </p>
          </GovUKNotificationBanner>
        )}

        <div className="govuk-grid-row">
          <div className="govuk-grid-column-two-thirds">
            <H1>Deleted activities</H1>
            <Paragraph>These are the activities you deleted from your main list. You can restore any activity to continue working on it.</Paragraph>
          </div>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : activities.length === 0 ? (
          <Paragraph>You have no deleted activities.</Paragraph>
        ) : (
          <SummaryList
            testId="activities-deleted-list"
            items={activities.map((activity: ActivitySummaryDto) => ({
              label: activity.title,
              actionTo: report.activityLink(ROUTES.activities.restore(activity.id)),
              actionState: { name: activity.title },
              actionLabel: 'Restore',
              testId: `activities-deleted-row-${activity.id}`,
            }))}
          />
        )}

        <p className="govuk-body">
          <RouterLink to={report.listPath} className="govuk-link" data-testid="activities-deleted-return">
            Return to your activities
          </RouterLink>
        </p>
      </div>
    </GeneralLayout>
  );
};

export default ActivitiesDeleted;

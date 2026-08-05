import React from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Link as RouterLink, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { GovUKInsetText, GovUKNotificationBanner, GovUKTaskList, type TaskListItem } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { composeTestId } from '../../../../helpers/testIds';
import { reportActivitiesKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getReportActivities } from '../../queries';
import { progressTaskStatus, SELECT_BANNER_COPY } from '../../display';
import { useReportContext } from '../../context';
import './styles.css';

interface BannerState {
  banner?: 'activityCompleted' | 'progressSaved';
  name?: string;
}

// "Select an activity" (BLUM-6611): the reportable activities (active / closed / finished) with their
// per-activity report progress.
const Select = (): React.ReactElement => {
  usePageTitle('Select an activity');
  const navigate = useNavigate();
  const location = useLocation();
  const bannerState = (location.state as BannerState | null) ?? {};
  const banner = bannerState.banner ? SELECT_BANNER_COPY[bannerState.banner] : undefined;

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();

  const { data, isLoading: activitiesLoading } = useQuery(reportActivitiesKey(collectionId), () => getReportActivities(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const activities = data?.data ?? [];

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // The report has not been created yet — back to the intro page for this collection.
  if (!contextLoading && (!context || !context.submissionId)) {
    return <Navigate to={ROUTES.reports.quarterly(collectionId)} replace />;
  }

  const isLoading = contextLoading || activitiesLoading;

  const items: TaskListItem[] = activities.map((activity) => ({
    id: activity.activityId,
    title: activity.title,
    href: ROUTES.reports.keyResultsActivity(collectionId, activity.activityId),
    onClick: () => navigate(ROUTES.reports.keyResultsActivity(collectionId, activity.activityId)),
    status: progressTaskStatus(activity.reportStatus),
    testId: composeTestId('reports-key-results-list', 'item', activity.activityId),
  }));

  return (
    <GeneralLayout backLink={{ href: ROUTES.reports.tasks(collectionId), testId: 'reports-key-results-select-back' }}>
      <div className="reports-key-results-select">
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="reports-key-results-select-banner">
            <p className="govuk-body">{banner.body(bannerState.name ?? 'activity')}</p>
          </GovUKNotificationBanner>
        )}

        <div className="reports-key-results-select__heading">
          <span className="govuk-caption-l">Enter activity key results</span>
          <h1 className="govuk-heading-l">Select an activity</h1>
        </div>

        <div className="reports-key-results-select__activities-heading">
          <h2 className="govuk-heading-m">Your activities</h2>
        </div>

        {isLoading ? (
          <PageLoading />
        ) : activities.length === 0 ? (
          <GovUKInsetText data-testid="reports-key-results-empty">
            You have not created any activities yet. You need to create at least one activity before you can start your quarterly report.
          </GovUKInsetText>
        ) : (
          <GovUKTaskList testId="reports-key-results-list" idPrefix="reports-key-results" items={items} />
        )}

        <p className="govuk-body reports-key-results-select__return">
          <RouterLink to={ROUTES.reports.tasks(collectionId)} className="govuk-link" data-testid="reports-key-results-select-return">
            Return to manage your report
          </RouterLink>
        </p>
      </div>
    </GeneralLayout>
  );
};

export default Select;

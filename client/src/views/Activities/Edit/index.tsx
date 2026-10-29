import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useParams, useSearchParams, Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKNotificationBanner, H1 } from '../../../components/GovUKComponents';
import ActivityWizard from '../../../components/Global/Activities/ActivityWizard';
import ActivityEditor from '../../../components/Global/Activities/ActivityEditor';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../hooks/usePageTitle';
import { activityKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { useActivityReportContext } from '../reportContext';
import { getActivity } from '../queries';
import { splitExpectedResults } from '../display';
import type { ActivityFormState } from '../reducer';

const ActivitiesEdit = (): React.ReactElement => {
  const { activityId = '' } = useParams<{ activityId: string }>();
  const [searchParams] = useSearchParams();
  const { userRole } = useAuthProvider();
  // Set when the activity was opened from Section 1 of a quarterly return (BLUM-6967). The wizard and the editor
  // read the same param themselves, so only the two branches rendered here need it.
  const report = useActivityReportContext();

  const { data, isLoading } = useQuery({ queryKey: activityKey(activityId), queryFn: () => getActivity(activityId), enabled: !!activityId });
  const activity = data?.data;
  const readOnly = !!activity && !activity.isDraft && (activity.status === 'closed' || activity.status === 'finished');

  // Editable journeys set their title from the active in-page view. Keep a route-level title only while
  // loading or when this route renders the read-only interruption instead of a wizard/editor.
  usePageTitle(!activity ? 'Change activity' : readOnly ? pageTitleWithName('Activity', activity.title) : undefined);

  if (isLoading || !activity) {
    return (
      <GeneralLayout breadcrumbs={report.breadcrumbs(userRole)}>
        <div style={{ paddingTop: '20px' }}>
          <PageLoading />
        </div>
      </GeneralLayout>
    );
  }

  // Closed/Finished completed records are read-only.
  if (readOnly) {
    return (
      <GeneralLayout breadcrumbs={report.breadcrumbs(userRole)} backLink={{ href: report.activityLink(ROUTES.activities.detail(activityId)) }}>
        <div style={{ paddingTop: '20px' }}>
          <H1>{activity.title}</H1>
          <GovUKNotificationBanner type="important" title="Important" testId="activities-edit-closed-banner">
            This activity is {activity.status} and can no longer be edited.
          </GovUKNotificationBanner>
          <RouterLink to={report.activityLink(ROUTES.activities.detail(activityId))} className="govuk-link" data-testid="activities-edit-closed-return">
            Return to the activity
          </RouterLink>
        </div>
      </GeneralLayout>
    );
  }

  // Completed Planned/Active records use the section-by-section "change" editor with Save/Discard.
  if (!activity.isDraft) {
    return <ActivityEditor activity={activity} initialEdit={searchParams.get('edit') ?? undefined} />;
  }

  // Drafts resume the linear "Continue setup" wizard.
  const answers = activity.answers.reduce<Record<string, string>>((acc, answer) => {
    if (answer.value != null) {
      acc[answer.questionCode] = answer.value;
    }
    return acc;
  }, {});

  const initialState: Partial<ActivityFormState> = {
    title: activity.name ?? '',
    categoryId: activity.categoryId ?? '',
    taskId: activity.taskId ?? '',
    customTaskText: activity.customTaskText ?? '',
    answers,
    ...splitExpectedResults(activity.expectedResults),
    status: activity.status ?? '',
    startMonth: activity.startMonth != null ? String(activity.startMonth) : '',
    startYear: activity.startYear != null ? String(activity.startYear) : '',
    closureReason: activity.closureReason ?? '',
  };

  return <ActivityWizard mode="edit" activityId={activityId} initialState={initialState} allowSaveDraft />;
};

export default ActivitiesEdit;

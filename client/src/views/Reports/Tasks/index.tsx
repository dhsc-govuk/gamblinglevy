import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKButton, GovUKNotificationBanner, GovUKTaskList, H1, Paragraph, type TaskListItem } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { getReportTaskList, type ReportModuleCode } from '../queries';
import { CANNOT_START_YET_STATUS, progressTaskStatus, TASKS_BANNER_COPY } from '../display';
import { useReportContext } from '../context';
import { canEditReport } from '../navigation';
import './styles.css';

interface BannerState {
  banner?: 'activitiesConfirmed' | 'fundingCompleted' | 'fundingProgressSaved' | 'feedbackCompleted' | 'feedbackProgressSaved';
}

// The landing route for each report section that is wired up. Sections not listed here are not linked yet
// (they render with their status but no link).
const ROUTE_BY_MODULE: Partial<Record<ReportModuleCode, (collectionId: string) => string>> = {
  activity_key_results: ROUTES.reports.keyResults,
  funding_spent: ROUTES.reports.fundingSpent,
  strategic_feedback: ROUTES.reports.strategicFeedback,
};

// "Manage your quarterly report" task list (BLUM-6611). Sections unlock once the activities list is confirmed.
const Tasks = (): React.ReactElement => {
  usePageTitle('Manage your quarterly report');
  const navigate = useNavigate();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);
  const location = useLocation();
  const bannerState = (location.state as BannerState | null) ?? {};
  const banner = bannerState.banner ? TASKS_BANNER_COPY[bannerState.banner] : undefined;

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();

  const { data: taskListData, isLoading: taskListLoading } = useQuery(reportTaskListKey(collectionId), () => getReportTaskList(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const taskList = taskListData?.data;

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // The report has not been created yet — back to the intro page for this collection.
  if (!contextLoading && (!context || !context.submissionId)) {
    return <Navigate to={ROUTES.reports.quarterly(collectionId)} replace />;
  }

  // The report has been submitted — it is read-only, so route to the confirmation.
  if (taskList?.submitted) {
    return <Navigate to={ROUTES.reports.submittedConfirmation(collectionId)} replace />;
  }

  const isLoading = contextLoading || taskListLoading || !taskList;

  const confirmItems: TaskListItem[] = taskList
    ? [
        {
          id: 'confirm-activities',
          title: 'Manage your list of activities',
          href: ROUTES.reports.confirmActivities(collectionId),
          onClick: () => navigate(ROUTES.reports.confirmActivities(collectionId)),
          status: taskList.activitiesConfirmed ? progressTaskStatus('completed') : { text: 'Not started', tag: { text: 'Not started', colour: 'blue' as const } },
          testId: 'reports-tasks-confirm-activities',
        },
      ]
    : [];

  const sectionItems: TaskListItem[] = (taskList?.sections ?? []).map((section) => {
    const route = ROUTE_BY_MODULE[section.moduleCode]?.(collectionId);
    const linked = section.isAvailable && !!route;
    return {
      id: section.moduleCode,
      title: section.moduleName,
      href: linked ? route : undefined,
      onClick: linked ? () => navigate(route!) : undefined,
      status: !section.isAvailable
        ? CANNOT_START_YET_STATUS
        : section.reviewRequired
        ? { text: 'In progress', tag: { text: 'In progress', colour: 'yellow' as const } }
        : progressTaskStatus(section.status),
      testId: composeTestId('reports-tasks-sections', 'item', slugifyTestId(section.moduleCode)),
    };
  });

  return (
    <GeneralLayout backLink={{ href: ROUTES.reports.quarterly(collectionId), testId: 'reports-tasks-back' }}>
      <div className="reports-tasks" data-testid="reports-tasks">
        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="reports-tasks-banner">
            <p className="govuk-body">{banner.body}</p>
          </GovUKNotificationBanner>
        )}

        {taskList?.activitiesReviewRequired && (
          <GovUKNotificationBanner type="important" title="Important" testId="reports-tasks-review-banner">
            <h3 className="govuk-notification-banner__heading">Review your report sections</h3>
            <p className="govuk-body">Your list of activities has changed. You must review and reconfirm each section before you can submit your report.</p>
          </GovUKNotificationBanner>
        )}

        <H1>Manage your quarterly report</H1>
        <Paragraph className="reports-tasks__intro">You must complete all sections before you can submit this report.</Paragraph>

        {isLoading ? (
          <PageLoading />
        ) : (
          <>
            <section
              className={`reports-tasks__section reports-tasks__section--check${taskList.activitiesConfirmed ? ' reports-tasks__section--check-completed' : ''}`}>
              <h2 className="govuk-heading-m">Check before you start</h2>
              <GovUKTaskList testId="reports-tasks-before-you-start" idPrefix="reports-tasks-before" items={confirmItems} />
            </section>

            <section
              className={`reports-tasks__section reports-tasks__section--prepare${taskList.activitiesConfirmed ? ' reports-tasks__section--prepare-available' : ''}`}>
              <h2 className="govuk-heading-m">Prepare your report</h2>
              <GovUKTaskList testId="reports-tasks-sections" idPrefix="reports-tasks-sections" items={sectionItems} />
            </section>

            <div className="reports-tasks__actions">
              <GovUKButton disabled={!taskList?.canSubmit || !canEdit} onClick={() => navigate(ROUTES.reports.submit(collectionId))} data-testid="reports-tasks-continue">
                Continue
              </GovUKButton>
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Tasks;

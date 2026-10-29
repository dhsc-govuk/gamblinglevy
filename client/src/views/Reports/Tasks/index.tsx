import React, { useEffect, useRef, useState } from 'react';
import type { AxiosResponse } from 'axios';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import {
  ErrorSummary,
  GovUKButton,
  GovUKInsetText,
  GovUKNotificationBanner,
  GovUKTaskList,
  H1,
  Paragraph,
  SummaryList,
  type TaskListItem,
} from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import useFeature from '../../../hooks/useFeature';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { getReportTaskList, type QuarterlyReportContextDto, type ReportModuleCode } from '../queries';
import { startReport } from '../mutations';
import { mapReportError } from '../messages';
import { CANNOT_START_YET_STATUS, formatGovUkDate, progressTaskStatus, reportingPeriodValue, TASKS_BANNER_COPY } from '../display';
import { useReportContext } from '../context';
import { canEditReport, reportBreadcrumbs } from '../navigation';
import OfflinePanel, { OFFLINE_FILE_INPUT_ID } from './OfflinePanel';
import useOfflineUpload from './useOfflineUpload';
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
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);
  const [startError, setStartError] = useState<string | null>(null);
  const [startAttempts, setStartAttempts] = useState(0);
  const startedCollection = useRef<string | null>(null);
  const location = useLocation();
  const bannerState = (location.state as BannerState | null) ?? {};
  const banner = bannerState.banner ? TASKS_BANNER_COPY[bannerState.banner] : undefined;

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();

  // "Complete your report offline" (BLUM-8310, behind BLUM_8310). Its error summary and success banner render at
  // the top of the page with the page's other messages.
  const offlineEnabled = useFeature('BLUM_8310');
  const offline = useOfflineUpload(collectionId);
  usePageTitle(offline.fileError || startError ? 'Error: Manage your quarterly report' : 'Manage your quarterly report');

  const startMutation = useMutation({
    mutationFn: startReport,
    onSuccess: (response) => {
      const cachedContext = queryClient.getQueryData<AxiosResponse<QuarterlyReportContextDto>>(reportContextKey(collectionId));
      if (cachedContext) {
        queryClient.setQueryData(reportContextKey(collectionId), {
          ...cachedContext,
          data: { ...cachedContext.data, submissionId: response.data.submissionId },
        });
      }
      queryClient.setQueryData(reportTaskListKey(collectionId), response);
    },
    onError: (error) => {
      setStartError(mapReportError(error).message);
      setStartAttempts((attempts) => attempts + 1);
    },
  });

  useEffect(() => {
    if (!contextLoading && context && !context.submissionId && canEdit && startedCollection.current !== collectionId) {
      startedCollection.current = collectionId;
      startMutation.mutate(collectionId);
    }
  }, [canEdit, collectionId, context, contextLoading, startMutation]);

  const { data: taskListData, isLoading: taskListLoading } = useQuery({
    queryKey: reportTaskListKey(collectionId),
    queryFn: () => getReportTaskList(collectionId),
    enabled: !!collectionId && !!context?.submissionId,
  });
  const taskList = taskListData?.data;

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // The report has been submitted — it is read-only, so route to the confirmation.
  if (taskList?.submitted) {
    return <Navigate to={ROUTES.reports.submittedConfirmation(collectionId)} replace />;
  }

  const isLoading = contextLoading || startMutation.isPending || taskListLoading || !taskList;
  const showOffline = offlineEnabled && canEdit && !offline.unavailable && !!taskList?.activitiesConfirmed;
  const uploadedSection = taskList?.sections.find((section) => section.moduleCode === offline.uploadedModule);

  const retryStart = () => {
    setStartError(null);
    startMutation.mutate(collectionId);
  };

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
    <GeneralLayout breadcrumbs={reportBreadcrumbs(userRole, collectionId, context?.quarter)}>
      <div className="reports-tasks" data-testid="reports-tasks">
        <ErrorSummary heading="There is a problem" errors={startError ? [{ targetName: 'reports-tasks-start', text: startError }] : []} focusTrigger={startAttempts} />

        <ErrorSummary
          heading="There is a problem"
          errors={showOffline && offline.fileError ? [{ targetName: OFFLINE_FILE_INPUT_ID, text: offline.fileError }] : []}
          focusTrigger={offline.problemAttempts}
          testId="reports-tasks-offline-error-summary"
        />

        {showOffline && uploadedSection && (
          <GovUKNotificationBanner type="success" title="Spreadsheet uploaded" focusTrigger={offline.successCount} testId="reports-tasks-offline-banner">
            <p className="govuk-body">{`Your answers to ‘${uploadedSection.moduleName}’ have been saved and the section is completed. You still need to submit your report.`}</p>
          </GovUKNotificationBanner>
        )}

        {/* A banner carried in from another page is out of date once an upload has replaced a section. */}
        {banner && offline.successCount === 0 && (
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
        {context && (
          <GovUKInsetText className="reports-tasks__summary" data-testid="reports-tasks-details">
            <SummaryList
              halfWidthColumns
              testId="reports-tasks-summary"
              items={[
                { label: 'Local authority', value: context.localAuthorityName },
                { label: 'Reporting period', value: reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd) },
                { label: 'Deadline', value: formatGovUkDate(context.submissionDeadline) },
              ]}
            />
          </GovUKInsetText>
        )}
        <div className="reports-tasks__intro">
          <Paragraph>
            Use this report to share results, insights and funding spent this quarter. Your information helps build a national evidence base of what works to prevent gambling
            harms.
          </Paragraph>
          <Paragraph>You can save your answers and return to this report at any time before the deadline.</Paragraph>
        </div>

        {startError ? (
          <GovUKButton onClick={retryStart} isLoading={startMutation.isPending} name="reports-tasks-start" data-testid="reports-tasks-start">
            Try again
          </GovUKButton>
        ) : isLoading ? (
          <PageLoading />
        ) : (
          <>
            <section className={`reports-tasks__section reports-tasks__section--check${taskList.activitiesConfirmed ? ' reports-tasks__section--check-completed' : ''}`}>
              <h2 className="govuk-heading-m">Check before you start</h2>
              <GovUKTaskList testId="reports-tasks-before-you-start" idPrefix="reports-tasks-before" items={confirmItems} />
            </section>

            <section className={`reports-tasks__section reports-tasks__section--prepare${taskList.activitiesConfirmed ? ' reports-tasks__section--prepare-available' : ''}`}>
              <h2 className="govuk-heading-m">Prepare your report</h2>
              <GovUKTaskList testId="reports-tasks-sections" idPrefix="reports-tasks-sections" items={sectionItems} />
            </section>

            {showOffline && <OfflinePanel collectionId={collectionId} sections={taskList.sections} upload={offline} />}

            <section className="reports-tasks__submit">
              <h2 className="govuk-heading-m">Submit your report</h2>
              <Paragraph>You must complete all sections before you can submit this report.</Paragraph>
              <GovUKButton disabled={!taskList.canSubmit || !canEdit} onClick={() => navigate(ROUTES.reports.submit(collectionId))} data-testid="reports-tasks-submit">
                Submit
              </GovUKButton>
            </section>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Tasks;

import React, { useEffect, useRef, useState } from 'react';
import type { AxiosResponse } from 'axios';
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
  PageLoading,
  Paragraph,
  SummaryList,
  type TaskListItem,
} from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { maturityContextKey, maturityTaskListKey } from '../../../helpers/queriesParams';
import { httpStatusOf } from '../../../helpers/httpErrors';
import { ROUTES } from '../../../helpers/routes';
import { formatGovUkDate, progressTaskStatus } from '../../Reports/display';
import { getMaturityTaskList, type MaturityAssessmentContextDto } from '../queries';
import { startMaturityAssessment } from '../mutations';
import { mapMaturityError } from '../messages';
import { maturityPeriodValue, MATURITY_TASKS_BANNER, prefillBannerBody } from '../display';
import { useMaturityContext } from '../context';
import { canEditMaturityAssessment, maturityBreadcrumbs } from '../navigation';
import './styles.css';

interface BannerState {
  banner?: keyof typeof MATURITY_TASKS_BANNER;
  sectionTitle?: string;
}

// "Manage your maturity assessment" (BLUM-7855 / 7858, Figma 2744-182185): starts the assessment on first visit
// (which pre-fills it — BLUM-7856), then lists the ten sections as a task list with the submit gate underneath.
const Tasks = (): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditMaturityAssessment(userRole);
  const [startError, setStartError] = useState<string | null>(null);
  const [startAttempts, setStartAttempts] = useState(0);
  const startedCollection = useRef<string | null>(null);
  usePageTitle(`${startError ? 'Error: ' : ''}Manage your maturity assessment`);
  const location = useLocation();
  const bannerState = (location.state as BannerState | null) ?? {};
  const banner = bannerState.banner ? MATURITY_TASKS_BANNER[bannerState.banner] : undefined;

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useMaturityContext();

  const startMutation = useMutation({
    mutationFn: startMaturityAssessment,
    onSuccess: (response) => {
      const cachedContext = queryClient.getQueryData<AxiosResponse<MaturityAssessmentContextDto>>(maturityContextKey(collectionId));
      if (cachedContext) {
        queryClient.setQueryData(maturityContextKey(collectionId), {
          ...cachedContext,
          data: { ...cachedContext.data, assessmentId: response.data.assessmentId, status: 'in_progress' },
        });
      }
      queryClient.setQueryData(maturityTaskListKey(collectionId), response);
    },
    onError: (error) => {
      // 409: a colleague (or another tab) started it between the context read and this POST. The context is merely
      // stale — refetch it and the task list loads; showing "Try again" would only 409 again forever.
      if (httpStatusOf(error) === 409) {
        queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
        return;
      }
      setStartError(mapMaturityError(error).message);
      setStartAttempts((attempts) => attempts + 1);
    },
  });

  // Start once per collection, only while the window is open — a closed window cannot be started, and asking would
  // only surface REPORT_NOT_OPEN as an error on a page that has nothing else to show.
  useEffect(() => {
    if (!contextLoading && context && !context.assessmentId && context.isOpen && canEdit && startedCollection.current !== collectionId) {
      startedCollection.current = collectionId;
      startMutation.mutate(collectionId);
    }
  }, [canEdit, collectionId, context, contextLoading, startMutation]);

  const {
    data: taskListData,
    isLoading: taskListLoading,
    isError: taskListError,
  } = useQuery({
    queryKey: maturityTaskListKey(collectionId),
    queryFn: () => getMaturityTaskList(collectionId),
    enabled: !!collectionId && !!context?.assessmentId,
  });
  const taskList = taskListData?.data;

  // The context (3-minute stale time) still says unsubmitted when a colleague submitted a moment ago; refresh it,
  // or the confirmation page — which reads submittedAt from the context — would bounce straight back here.
  // Keyed on what the task list says rather than on a fetch landing: the start mutation above writes the same
  // cache entry with setQueryData, and useQuery has had no onSuccess since react-query v5. It cannot loop —
  // once asked, neither input changes again until the context itself reports the submission.
  const taskListSubmitted = !!taskList?.submitted;
  const contextSubmitted = !!context?.submittedAt;
  useEffect(() => {
    if (taskListSubmitted && !contextSubmitted) {
      queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
    }
  }, [taskListSubmitted, contextSubmitted, collectionId, queryClient]);

  // Unknown collection, another organisation's, or not a maturity-assessment window.
  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // Submitted — read-only, so route to the confirmation, carrying the reference so it renders without waiting on the
  // context refresh above.
  if (taskList?.submitted || context?.submittedAt) {
    return <Navigate to={ROUTES.reports.maturitySubmittedConfirmation(collectionId)} state={{ referenceNumber: taskList?.referenceNumber ?? context?.referenceNumber }} replace />;
  }

  const windowClosed = !!context && !context.isOpen;
  const started = !!context?.assessmentId;
  const isLoading = contextLoading || startMutation.isPending || (started && (taskListLoading || !taskList));

  const retryStart = () => {
    setStartError(null);
    queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
    startMutation.mutate(collectionId);
  };

  const sectionItems: TaskListItem[] = (taskList?.sections ?? []).map((section) => {
    const route = ROUTES.reports.maturitySection(collectionId, section.code);
    return {
      id: section.code,
      title: section.title,
      href: route,
      onClick: () => navigate(route),
      status: progressTaskStatus(section.status),
      testId: composeTestId('la-maturity-tasks-sections', 'item', slugifyTestId(section.code)),
    };
  });

  return (
    <GeneralLayout breadcrumbs={maturityBreadcrumbs(userRole, collectionId, context?.financialYearEndYear)}>
      <div className="maturity-tasks" data-testid="la-maturity-tasks">
        <ErrorSummary
          heading="There is a problem"
          errors={startError ? [{ targetName: 'la-maturity-tasks-start', text: startError }] : []}
          testId="la-maturity-tasks-errors"
          focusTrigger={startAttempts}
        />

        {banner && (
          <GovUKNotificationBanner type="success" title={banner.title} testId="la-maturity-tasks-banner">
            <p className="govuk-body">{banner.body(bannerState.sectionTitle ?? 'This section')}</p>
          </GovUKNotificationBanner>
        )}

        {taskList?.isPrefilled && (
          <GovUKNotificationBanner type="important" title="Important" testId="la-maturity-tasks-prefilled-banner">
            <h3 className="govuk-notification-banner__heading">Your maturity assessment is pre-filled</h3>
            <p className="govuk-body">{prefillBannerBody(taskList.prefillSources)}</p>
          </GovUKNotificationBanner>
        )}

        <H1>Manage your maturity assessment</H1>
        {context && (
          <GovUKInsetText className="maturity-tasks__summary" data-testid="la-maturity-tasks-details">
            <SummaryList
              halfWidthColumns
              testId="la-maturity-tasks-summary"
              items={[
                { label: 'Local authority', value: context.localAuthorityName },
                { label: 'Reporting period', value: maturityPeriodValue(context.reportingPeriodStart, context.reportingPeriodEnd) },
                { label: 'Deadline', value: formatGovUkDate(context.submissionDeadline) },
              ]}
            />
          </GovUKInsetText>
        )}
        <div className="maturity-tasks__intro">
          <Paragraph>
            Use this assessment to tell us how your local authority is developing its capacity, strategy and regulatory approaches to prevent gambling harms. Your answers help DHSC
            understand progress across England.
          </Paragraph>
          <Paragraph>You can save your answers and return to this assessment at any time before the deadline.</Paragraph>
        </div>

        {startError ? (
          <GovUKButton onClick={retryStart} isLoading={startMutation.isPending} id="la-maturity-tasks-start" name="la-maturity-tasks-start" data-testid="la-maturity-tasks-start">
            Try again
          </GovUKButton>
        ) : windowClosed && !started && !contextLoading ? (
          <GovUKInsetText data-testid="la-maturity-tasks-closed">This maturity assessment has closed. You can no longer complete it.</GovUKInsetText>
        ) : taskListError ? (
          <GovUKInsetText data-testid="la-maturity-tasks-load-failed">We could not load your maturity assessment. Refresh the page to try again.</GovUKInsetText>
        ) : isLoading || !taskList ? (
          <PageLoading />
        ) : (
          <>
            {windowClosed && <GovUKInsetText data-testid="la-maturity-tasks-closed">This maturity assessment has closed. You can no longer make changes.</GovUKInsetText>}

            <section className="maturity-tasks__section">
              <h2 className="govuk-heading-m">Prepare your assessment</h2>
              <GovUKTaskList testId="la-maturity-tasks-sections" idPrefix="la-maturity-tasks-sections" items={sectionItems} />
            </section>

            <section className="maturity-tasks__submit">
              <h2 className="govuk-heading-m">Submit your assessment</h2>
              <Paragraph>You must complete all sections before you can submit this assessment.</Paragraph>
              <GovUKButton
                disabled={!taskList.canSubmit || !canEdit || windowClosed}
                onClick={() => navigate(ROUTES.reports.maturitySubmit(collectionId))}
                data-testid="la-maturity-tasks-submit">
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

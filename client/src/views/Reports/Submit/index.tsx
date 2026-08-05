import React, { useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, GovUKWarningText, H1, H2, Paragraph, SummaryList } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';
import { myReportCollectionsKey, reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { formatGovUkDate } from '../../Admin/Collections/display';
import { getReportTaskList } from '../queries';
import { submitReport } from '../mutations';
import { mapReportError } from '../messages';
import { reportingPeriodValue } from '../display';
import { useReportContext } from '../context';
import { canEditReport } from '../navigation';
import { trackEvent } from '../../../helpers/analytics';
import './styles.css';

// "Submit your quarterly report" confirmation (the irreversible final step). Reachable only once every section is
// complete (canSubmit); submitting locks the report and routes to the confirmation page with the reference number.
const Submit = (): React.ReactElement => {
  usePageTitle('Submit your quarterly report');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const { data: taskListData, isLoading: taskListLoading } = useQuery(reportTaskListKey(collectionId), () => getReportTaskList(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const taskList = taskListData?.data;

  const submitMutation = useMutation(submitReport, {
    onSuccess: (response) => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      // The landing lists open collections that are not yet submitted — this one has just left that list.
      queryClient.invalidateQueries(myReportCollectionsKey);
      navigate(ROUTES.reports.submittedConfirmation(collectionId), { state: { referenceNumber: response.data.referenceNumber } });
    },
    onError: (error) => {
      setSubmitError(mapReportError(error).message);
      setSubmitAttempts((attempts) => attempts + 1);
    },
  });

  useErrorSummaryFocus(submitAttempts, !!submitError);

  // Unknown collection, another organisation's, or a link that predates the collection-scoped flow.
  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  // The report has not been created yet — back to the intro page for this collection.
  if (!contextLoading && (!context || !context.submissionId)) {
    return <Navigate to={ROUTES.reports.quarterly(collectionId)} replace />;
  }

  // Already submitted — the report is read-only, so route to the confirmation.
  if (context?.submittedAt) {
    return <Navigate to={ROUTES.reports.submittedConfirmation(collectionId)} replace />;
  }

  const isLoading = contextLoading || taskListLoading || !taskList || !context;

  // Sections still incomplete — there is nothing to confirm yet, so go back to the task list.
  if (!isLoading && taskList && !taskList.canSubmit) {
    return <Navigate to={tasksPath} replace />;
  }

  return (
    <GeneralLayout backLink={{ href: tasksPath, testId: 'reports-submit-back' }}>
      <div className="reports-submit" data-testid="reports-submit-page">
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            {submitError && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'reports-submit-confirm', text: submitError }]} />}

            <H1 className="reports-submit__title">Submit your quarterly report</H1>

            <GovUKInsetText className="reports-submit__summary">
              <SummaryList
                halfWidthColumns
                testId="reports-submit-summary"
                items={[
                  { label: 'Local authority', value: context.localAuthorityName },
                  {
                    label: 'Reporting period',
                    value: reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd),
                  },
                  { label: 'Deadline', value: formatGovUkDate(context.submissionDeadline) },
                ]}
              />
            </GovUKInsetText>

            <H2 className="reports-submit__confirmation-heading">Are you sure you want to submit?</H2>
            <GovUKWarningText className="reports-submit__warning" data-testid="reports-submit-warning">
              Once you submit this report, you cannot change it.
            </GovUKWarningText>
            <Paragraph className="reports-submit__body">
              Your information will be sent to the Department of Health and Social Care (DHSC). They&rsquo;ll contact you if they need more information.
            </Paragraph>

            <div className="govuk-button-group reports-submit__actions">
              {canEdit && (
                <GovUKButton
                  onClick={() => {
                    trackEvent('report_submit_attempted', { collectionId });
                    submitMutation.mutate(collectionId);
                  }}
                  isLoading={submitMutation.isLoading}
                  name="reports-submit-confirm"
                  data-testid="reports-submit-confirm">
                  Submit
                </GovUKButton>
              )}
              <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => navigate(tasksPath)} data-testid="reports-submit-return">
                Return to your report
              </GovUKButton>
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Submit;

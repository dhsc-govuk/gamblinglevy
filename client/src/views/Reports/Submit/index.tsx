import React, { useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { ConfirmActionPanel, ErrorSummary, GovUKButton, H1, Paragraph } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { maturitySectionsRootKey, maturityTaskListsRootKey, myReportCollectionsKey, reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { getReportTaskList } from '../queries';
import { submitReport } from '../mutations';
import { mapReportError } from '../messages';
import { useReportContext } from '../context';
import { canEditReport } from '../navigation';
import { trackEvent } from '../../../helpers/analytics';
import './styles.css';

// The submit confirmation (the irreversible final step). Reachable only once every section is complete
// (canSubmit); submitting locks the report and routes to the confirmation page with the reference number.
//
// Built as the Figma review's single blue panel (section 16): the question IS the page, so it carries the H1 and
// there is no separate page heading above it. The panel is `ConfirmActionPanel`, the service's existing blue
// "Are you sure?" wrapper (govuk-frontend's own panel is green-only), used here exactly as the Deactivate and
// Discard-edits pages use it — H1, Paragraph, then a govuk-button-group holding a `govuk-button--inverse`
// confirm and a `govuk-link--inverse` cancel. Nothing on this page is hand-coloured.
const Submit = (): React.ReactElement => {
  usePageTitle('Are you sure you want to submit?');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const { data: taskListData, isLoading: taskListLoading } = useQuery({
    queryKey: reportTaskListKey(collectionId),
    queryFn: () => getReportTaskList(collectionId),
    enabled: !!collectionId && !!context?.submissionId,
  });
  const taskList = taskListData?.data;

  const submitMutation = useMutation({
    mutationFn: submitReport,
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
      // The landing lists open collections that are not yet submitted — this one has just left that list.
      queryClient.invalidateQueries({ queryKey: myReportCollectionsKey });
      // The submit has just pulled this report into the organisation's open maturity assessments (BLUM-8927).
      // Removed, not invalidated: a maturity page fills its inputs once from whatever is cached, and an
      // invalidated entry is still handed to it while the refetch is in flight.
      queryClient.removeQueries({ queryKey: maturityTaskListsRootKey });
      queryClient.removeQueries({ queryKey: maturitySectionsRootKey });
      navigate(ROUTES.reports.submittedConfirmation(collectionId), { state: { referenceNumber: response.data.referenceNumber } });
    },
    onError: (error) => {
      setSubmitError(mapReportError(error).message);
      setSubmitAttempts((attempts) => attempts + 1);
    },
  });

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
            <ErrorSummary heading="There is a problem" errors={submitError ? [{ targetName: 'reports-submit-confirm', text: submitError }] : []} focusTrigger={submitAttempts} />

            <ConfirmActionPanel testId="reports-submit-panel">
              <H1>Are you sure you want to submit?</H1>
              <Paragraph>Once you submit this report, it will be sent to the Department of Health and Social Care (DHSC). You will not be able to change it.</Paragraph>

              <div className="govuk-button-group">
                {canEdit && (
                  <GovUKButton
                    className="govuk-button govuk-button--inverse"
                    onClick={() => {
                      trackEvent('report_submit_attempted', { collectionId });
                      submitMutation.mutate(collectionId);
                    }}
                    isLoading={submitMutation.isPending}
                    // `id`, not just `name`: the error summary links to #reports-submit-confirm and
                    // GovUKErrorSummary resolves it with getElementById after preventDefault, so a button
                    // carrying only `name` makes the error link do nothing at all when clicked.
                    id="reports-submit-confirm"
                    name="reports-submit-confirm"
                    data-testid="reports-submit-confirm">
                    Yes, submit
                  </GovUKButton>
                )}
                {/*
                  A link, not a secondary button, per the design — and it is genuinely a link: it navigates back to
                  the task list without changing anything. Rendered through react-router so it keeps SPA navigation
                  and still opens in a new tab on middle-click, which an onClick-only button would not.
                */}
                <Link className="govuk-link govuk-link--inverse" to={tasksPath} data-testid="reports-submit-return">
                  No, return to report
                </Link>
              </div>
            </ConfirmActionPanel>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Submit;

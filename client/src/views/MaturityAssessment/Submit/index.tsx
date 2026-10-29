import React, { useState } from 'react';
import { Link, Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { ConfirmActionPanel, ErrorSummary, GovUKButton, H1, PageLoading, Paragraph } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { maturityContextKey, maturityTaskListKey, myReportCollectionsKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { trackEvent } from '../../../helpers/analytics';
import { getMaturityTaskList } from '../queries';
import { submitMaturityAssessment } from '../mutations';
import { mapMaturityError } from '../messages';
import { useMaturityContext } from '../context';
import { canEditMaturityAssessment } from '../navigation';
import './styles.css';

// "Are you sure you want to submit?" (BLUM-7858 AC3, Figma 2744-182345): the irreversible final step, reachable
// only once every section is complete. Same ConfirmActionPanel treatment as the quarterly report's Submit page.
const Submit = (): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditMaturityAssessment(userRole);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);
  usePageTitle(`${submitError ? 'Error: ' : ''}Are you sure you want to submit?`);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useMaturityContext();
  const tasksPath = ROUTES.reports.maturity(collectionId);

  const { data: taskListData, isLoading: taskListLoading } = useQuery({
    queryKey: maturityTaskListKey(collectionId),
    queryFn: () => getMaturityTaskList(collectionId),
    enabled: !!collectionId && !!context?.assessmentId,
  });
  const taskList = taskListData?.data;

  const submitMutation = useMutation({
    mutationFn: submitMaturityAssessment,
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: maturityTaskListKey(collectionId) });
      // The Reports landing lists open windows not yet submitted — this one has just left that list.
      queryClient.invalidateQueries({ queryKey: myReportCollectionsKey });
      navigate(ROUTES.reports.maturitySubmittedConfirmation(collectionId), { state: { referenceNumber: response.data.referenceNumber } });
    },
    onError: (error) => {
      setSubmitError(mapMaturityError(error).message);
      setSubmitAttempts((attempts) => attempts + 1);
    },
  });

  if (contextError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  if (!contextLoading && (!context || !context.assessmentId)) {
    return <Navigate to={tasksPath} replace />;
  }

  if (context?.submittedAt) {
    return <Navigate to={ROUTES.reports.maturitySubmittedConfirmation(collectionId)} replace />;
  }

  const isLoading = contextLoading || taskListLoading || !taskList || !context;

  if (!isLoading && taskList && !taskList.canSubmit) {
    return <Navigate to={tasksPath} replace />;
  }

  return (
    <GeneralLayout backLink={{ href: tasksPath, testId: 'la-maturity-submit-back' }}>
      <div className="maturity-submit" data-testid="la-maturity-submit">
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary
              heading="There is a problem"
              errors={submitError ? [{ targetName: 'la-maturity-submit-confirm', text: submitError }] : []}
              testId="la-maturity-submit-errors"
              focusTrigger={submitAttempts}
            />

            <ConfirmActionPanel testId="la-maturity-submit-panel">
              <H1>Are you sure you want to submit?</H1>
              <Paragraph>
                Once you submit this maturity assessment, it will be sent to the Department of Health and Social Care (DHSC). You will not be able to change it.
              </Paragraph>

              <div className="govuk-button-group">
                {canEdit && (
                  <GovUKButton
                    className="govuk-button govuk-button--inverse"
                    onClick={() => {
                      trackEvent('maturity_submit_attempted', { collectionId });
                      submitMutation.mutate(collectionId);
                    }}
                    isLoading={submitMutation.isPending}
                    id="la-maturity-submit-confirm"
                    name="la-maturity-submit-confirm"
                    data-testid="la-maturity-submit-confirm">
                    Yes, submit
                  </GovUKButton>
                )}
                <Link className="govuk-link govuk-link--inverse" to={tasksPath} data-testid="la-maturity-submit-return">
                  No, return to maturity assessment
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

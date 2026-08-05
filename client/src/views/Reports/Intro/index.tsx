import React, { useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, H1, Paragraph, SummaryList } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';
import { reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { formatGovUkDate, reportingPeriodValue } from '../display';
import { startReport } from '../mutations';
import { mapReportError } from '../messages';
import { useReportContext } from '../context';
import { canEditReport, reportBreadcrumbs } from '../navigation';
import './styles.css';

// "Submit your quarterly report" intro (BLUM-6611). Shows the reporting window of the collection opened from the
// Reports landing and creates the report (submission) on first entry.
const Intro = (): React.ReactElement => {
  usePageTitle('Submit your quarterly report');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading, isError } = useReportContext();

  const startMutation = useMutation(startReport, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      navigate(ROUTES.reports.tasks(collectionId));
    },
    onError: (error) => {
      setSubmitError(mapReportError(error).message);
      setSubmitAttempts((attempts) => attempts + 1);
    },
  });

  useErrorSummaryFocus(submitAttempts, !!submitError);

  // The collection does not exist, is not this organisation's, or the link predates the collection-scoped flow —
  // back to the landing, which lists the collections that are actually open and owns the "nothing due" empty state.
  if (isError) {
    return <Navigate to={ROUTES.reports.list} replace />;
  }

  return (
    <GeneralLayout breadcrumbs={reportBreadcrumbs(userRole, collectionId, context?.quarter)}>
      <div className="reports-intro" data-testid="reports-intro">
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            {submitError && <ErrorSummary heading="There is a problem" errors={[{ targetName: 'reports-intro-start', text: submitError }]} />}

            <H1>Submit your quarterly report</H1>

            <GovUKInsetText className="reports-intro__summary">
              <SummaryList
                testId="reports-intro-summary"
                items={[
                  { label: 'Local authority', value: context.localAuthorityName },
                  { label: 'Reporting period', value: reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd) },
                  { label: 'Deadline', value: formatGovUkDate(context.submissionDeadline) },
                ]}
              />
            </GovUKInsetText>

            <div className="reports-intro__body">
              <Paragraph>
                Use this report to share key results, insights and funding spent this quarter. Your information helps build a national evidence base of what works to prevent
                gambling harms.
              </Paragraph>
              <Paragraph>You can save your progress and return to this report at any time before the deadline.</Paragraph>
            </div>

            <div className="reports-intro__actions">
              {context.submissionId ? (
                <GovUKButton onClick={() => navigate(ROUTES.reports.tasks(collectionId))} name="reports-intro-start" data-testid="reports-intro-start">
                  Create report
                </GovUKButton>
              ) : (
                canEdit && (
                  <GovUKButton onClick={() => startMutation.mutate(collectionId)} isLoading={startMutation.isLoading} name="reports-intro-start" data-testid="reports-intro-start">
                    Create report
                  </GovUKButton>
                )
              )}
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Intro;

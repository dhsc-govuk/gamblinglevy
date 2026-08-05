import React, { useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, GovUKSummaryCard, H3, SummaryList } from '../../../../components/GovUKComponents';
import ReportFundingPageHeader from '../../../../components/Global/Reports/ReportFundingPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import { formatGBP } from '../../../../helpers/currency';
import { formatGovUkDate } from '../../../Admin/Collections/display';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { reportContextKey, reportFundingSpendKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getFundingSpend } from '../../queries';
import { completeFundingSpend } from '../../mutations';
import { mapReportError } from '../../messages';
import { reportingPeriodLabel } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import { summaryRows } from '../display';
import './styles.css';

// "Check your spending totals" (BLUM-6612, screen 3): the saved actual + forecast spend. Continue
// completes the section (the backend validates every required amount); a 422 surfaces the errors with
// links back to the screen that owns each one.
const Check = (): React.ReactElement => {
  usePageTitle('Check your spending totals');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const spendingPath = ROUTES.reports.fundingSpent(collectionId);
  const forecastPath = ROUTES.reports.fundingSpentForecast(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const fundingQuery = useQuery(reportFundingSpendKey(collectionId), () => getFundingSpend(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const funding = fundingQuery.data?.data;

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const completeMutation = useMutation(completeFundingSpend, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      queryClient.invalidateQueries(reportFundingSpendKey(collectionId));
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

  const isLoading = contextLoading || fundingQuery.isLoading || !funding;

  // A 422 amount error links back to the screen that owns it.
  const pageForError = (targetName: string): string => (targetName.startsWith('forecast-') || targetName.startsWith('other-forecast') ? forecastPath : spendingPath);

  const handleComplete = () => {
    completeMutation.mutate(collectionId, {
      onSuccess: () => navigate(tasksPath, { state: { banner: 'fundingCompleted' } }),
      onError: (error) => {
        const mapped = mapReportError(error);
        setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
        setSubmitAttempts((attempts) => attempts + 1);
      },
    });
  };

  const activitySpendingRows = funding
    ? [
        ...funding.activities.map((activity) => ({ label: activity.title, value: formatGBP(activity.actualThisQuarter) })),
        { label: 'Planned spend', value: formatGBP(funding.otherActualThisQuarter) },
      ]
    : [];

  const forecastRows = funding
    ? [
        ...funding.activities.flatMap((activity) =>
          funding.remainingQuarters.map((quarter) => ({
            label: `${activity.title}, Quarter ${quarter.quarter} (${formatGovUkDate(quarter.start)} to ${formatGovUkDate(quarter.end)})`,
            value: formatGBP(activity.forecast[quarter.quarter] ?? null),
          }))
        ),
        ...funding.remainingQuarters.map((quarter) => ({
          label: `Other planned spend, Quarter ${quarter.quarter} (${formatGovUkDate(quarter.start)} to ${formatGovUkDate(quarter.end)})`,
          value: formatGBP(funding.otherForecast[quarter.quarter] ?? null),
        })),
      ]
    : [];

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout
      backLink={{ href: funding && funding.remainingQuarters.length === 0 ? spendingPath : forecastPath, testId: 'reports-funding-spent-check-back' }}>
      <div className="reports-funding-check">
        {isLoading || !context || !funding ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} onHandleErrorClick={(targetName) => navigate(pageForError(targetName))} />}

            <div className="reports-funding-check__header">
              <ReportFundingPageHeader heading="Check your spending totals" reportingPeriod={reportingPeriodLabel(context)} />
            </div>

            <div className="reports-funding-check__content">
              <GovUKSummaryCard
                title="Activity spending"
                testId="reports-funding-spent-check-activity-spending"
                actions={[{ label: 'Change', onClick: () => navigate(spendingPath), visuallyHiddenText: 'activity spending' }]}>
                <SummaryList items={activitySpendingRows} halfWidthColumns testId="reports-funding-spent-check-activity-spending-list" />
              </GovUKSummaryCard>

              {forecastRows.length > 0 && (
                <GovUKSummaryCard
                  title="Forecast spend"
                  testId="reports-funding-spent-check-forecast-spend"
                  actions={[{ label: 'Change', onClick: () => navigate(forecastPath), visuallyHiddenText: 'forecast spend' }]}>
                  <SummaryList items={forecastRows} halfWidthColumns testId="reports-funding-spent-check-forecast-spend-list" />
                </GovUKSummaryCard>
              )}

              <GovUKInsetText className="reports-funding-check__summary" data-testid="reports-funding-spent-check-summary">
                <H3>Summary for all activities</H3>
                <SummaryList items={summaryRows(funding.summary, true)} halfWidthColumns testId="reports-funding-spent-check-summary-list" />
              </GovUKInsetText>
            </div>

            <div className="govuk-button-group reports-funding-check__actions">
              {canEdit && (
                <GovUKButton onClick={handleComplete} isLoading={completeMutation.isLoading} data-testid="reports-funding-spent-check-continue">
                  Continue
                </GovUKButton>
              )}
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={() => navigate(tasksPath, { state: { banner: 'fundingProgressSaved' } })}
                  data-testid="reports-funding-spent-check-save">
                  Save and come back later
                </GovUKButton>
              )}
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Check;

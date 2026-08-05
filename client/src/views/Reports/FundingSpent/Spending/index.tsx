import React, { useEffect, useRef, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInputWithSuffix, GovUKInsetText, H2, H3, Paragraph, SummaryList } from '../../../../components/GovUKComponents';
import ReportFundingPageHeader from '../../../../components/Global/Reports/ReportFundingPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import { formatGBP } from '../../../../helpers/currency';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { reportContextKey, reportFundingSpendKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getFundingSpend } from '../../queries';
import { saveFundingActuals } from '../../mutations';
import { mapReportError } from '../../messages';
import { reportingPeriodLabel } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import { amountToInput, hasForecastQuarters, parseAmount, SPENDING_INTRO, summaryRows, validateMoney } from '../display';
import { trackEvent } from '../../../../helpers/analytics';
import './styles.css';

// "Activity spending" (BLUM-6612, screen 1): the levy actually spent this quarter per reportable activity,
// plus a single non-activity "Other spend this quarter". Continue saves a draft and moves to Forecast;
// "Save and come back later" returns to the task list with the section In progress.
const Spending = (): React.ReactElement => {
  usePageTitle('Activity spending');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [actuals, setActuals] = useState<Record<string, string>>({});
  const [otherSpend, setOtherSpend] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const forecastPath = ROUTES.reports.fundingSpentForecast(collectionId);
  const checkPath = ROUTES.reports.fundingSpentCheck(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const fundingQuery = useQuery(reportFundingSpendKey(collectionId), () => getFundingSpend(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const funding = fundingQuery.data?.data;

  const hydratedRef = useRef(false);
  useEffect(() => {
    if (funding && !hydratedRef.current) {
      hydratedRef.current = true;
      setActuals(
        funding.activities.reduce<Record<string, string>>((acc, activity) => {
          acc[activity.activityId] = amountToInput(activity.actualThisQuarter);
          return acc;
        }, {})
      );
      setOtherSpend(amountToInput(funding.otherActualThisQuarter));
    }
  }, [funding]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const saveMutation = useMutation(saveFundingActuals, {
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

  const buildErrors = (required: boolean): Record<string, string> => {
    const next: Record<string, string> = {};
    (funding?.activities ?? []).forEach((activity) => {
      const error = validateMoney(actuals[activity.activityId] ?? '', { required, label: 'the amount spent' });
      if (error) {
        next[`actual-${activity.activityId}`] = error;
      }
    });
    const otherError = validateMoney(otherSpend, { required, label: 'the other spend this quarter' });
    if (otherError) {
      next['other-spend'] = otherError;
    }
    return next;
  };

  const applyServerError = (error: unknown) => {
    const mapped = mapReportError(error);
    setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
    setSubmitAttempts((attempts) => attempts + 1);
  };

  const save = (onSaved: () => void) => {
    saveMutation.mutate(
      {
        collectionId,
        saveAsDraft: true,
        otherAmount: parseAmount(otherSpend),
        activities: (funding?.activities ?? []).map((activity) => ({
          activityId: activity.activityId,
          amount: parseAmount(actuals[activity.activityId] ?? ''),
        })),
      },
      { onSuccess: onSaved, onError: applyServerError }
    );
  };

  // A Q4 report has no quarters to forecast, so skip the "Forecast spend" screen and go straight to Check.
  const handleContinue = () => {
    const nextPath = hasForecastQuarters(funding) ? forecastPath : checkPath;
    if (!canEdit) {
      navigate(nextPath);
      return;
    }
    const validationErrors = buildErrors(true);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => navigate(nextPath));
  };

  const handleSaveDraft = () => {
    const validationErrors = buildErrors(false);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => {
      trackEvent('report_draft_saved', { collectionId, section: 'funding_spend_actuals' });
      navigate(tasksPath, { state: { banner: 'fundingProgressSaved' } });
    });
  };

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout backLink={{ href: tasksPath, testId: 'reports-funding-spent-spending-back' }}>
      <div className="reports-funding-spending">
        {isLoading || !context || !funding ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

            <div className="reports-funding-spending__header">
              <ReportFundingPageHeader heading="Activity spending" reportingPeriod={reportingPeriodLabel(context)} />

              <div className="reports-funding-spending__intro">
                {SPENDING_INTRO.map((line) => (
                  <Paragraph key={line}>{line}</Paragraph>
                ))}
              </div>
            </div>

            <div className="reports-funding-spending__activities">
              {funding.activities.map((activity) => (
                <section className="reports-funding-spending__activity" key={activity.activityId}>
                  <div className="reports-funding-spending__activity-heading">
                    <span className="govuk-caption-m">Activity</span>
                    <H2>{activity.title}</H2>
                  </div>
                  <GovUKInsetText data-testid={`reports-funding-spent-spending-total-spent-${activity.activityId}`}>
                    Total spent so far: {formatGBP(activity.totalSpentSoFar)}
                  </GovUKInsetText>
                  <GovUKInputWithSuffix
                    id={`actual-${activity.activityId}`}
                    name={`actual-${activity.activityId}`}
                    label="Levy funding spent this quarter"
                    prefix="£"
                    width="10"
                    value={actuals[activity.activityId] ?? ''}
                    error={errors[`actual-${activity.activityId}`]}
                    disabled={!canEdit}
                    testId={`reports-funding-spent-spending-actual-${activity.activityId}`}
                    onChange={(event) => setActuals((previous) => ({ ...previous, [activity.activityId]: event.target.value }))}
                  />
                </section>
              ))}
            </div>

            <div className="reports-funding-spending__other">
              <GovUKInputWithSuffix
                id="other-spend"
                name="other-spend"
                label="Other spend this quarter"
                prefix="£"
                width="10"
                value={otherSpend}
                error={errors['other-spend']}
                disabled={!canEdit}
                testId="reports-funding-spent-spending-other"
                onChange={(event) => setOtherSpend(event.target.value)}
              />
            </div>

            <GovUKInsetText className="reports-funding-spending__summary" data-testid="reports-funding-spent-spending-summary">
              <H3>Summary for all activities</H3>
              <SummaryList items={summaryRows(funding.summary, false)} halfWidthColumns testId="reports-funding-spent-spending-summary-list" />
            </GovUKInsetText>

            <div className="govuk-button-group reports-funding-spending__actions">
              <GovUKButton onClick={handleContinue} isLoading={saveMutation.isLoading} data-testid="reports-funding-spent-spending-continue">
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleSaveDraft}
                  isLoading={saveMutation.isLoading}
                  data-testid="reports-funding-spent-spending-save">
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

export default Spending;

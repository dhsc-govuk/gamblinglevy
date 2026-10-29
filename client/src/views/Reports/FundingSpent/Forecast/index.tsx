import React, { useEffect, useRef, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKGroupedInputs, Paragraph } from '../../../../components/GovUKComponents';
import ReportFundingPageHeader from '../../../../components/Global/Reports/ReportFundingPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { reportContextKey, reportFundingSpendKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getFundingSpend } from '../../queries';
import { saveFundingForecast } from '../../mutations';
import { mapReportError } from '../../messages';
import { reportingPeriodValue } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import { parseAmount } from '../../../../helpers/currency';
import { amountToInput, FORECAST_INTRO, forecastQuarterLabel, moneySubjectForForecast, moneySubjectForOtherForecast, validateMoney } from '../display';
import { trackEvent } from '../../../../helpers/analytics';
import './styles.css';

const activityQuarterCode = (activityId: string, quarter: number): string => `forecast-${activityId}-q${quarter}`;
const otherQuarterCode = (quarter: number): string => `other-forecast-q${quarter}`;

// "Forecast spend" (BLUM-6612, screen 2): the expected levy spend per reportable activity for each
// remaining quarter of the financial year, plus a non-activity "Other planned spend" per quarter.
const Forecast = (): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  // Flat map keyed by input code (forecast-{activityId}-q{n} / other-forecast-q{n}).
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Forecast spend`);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const spendingPath = ROUTES.reports.fundingSpent(collectionId);
  const checkPath = ROUTES.reports.fundingSpentCheck(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const fundingQuery = useQuery({
    queryKey: reportFundingSpendKey(collectionId),
    queryFn: () => getFundingSpend(collectionId),
    enabled: !!collectionId && !!context?.submissionId,
  });
  const funding = fundingQuery.data?.data;

  const hydratedRef = useRef(false);
  useEffect(() => {
    if (funding && !hydratedRef.current) {
      hydratedRef.current = true;
      const next: Record<string, string> = {};
      funding.activities.forEach((activity) => {
        funding.remainingQuarters.forEach((quarter) => {
          next[activityQuarterCode(activity.activityId, quarter.quarter)] = amountToInput(activity.forecast[quarter.quarter] ?? null);
        });
      });
      funding.remainingQuarters.forEach((quarter) => {
        next[otherQuarterCode(quarter.quarter)] = amountToInput(funding.otherForecast[quarter.quarter] ?? null);
      });
      setValues(next);
    }
  }, [funding]);

  const saveMutation = useMutation({
    mutationFn: saveFundingForecast,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportFundingSpendKey(collectionId) });
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

  // A Q4 report has no quarters to forecast — there is nothing to show here, so skip straight to Check.
  if (funding && funding.remainingQuarters.length === 0) {
    return <Navigate to={checkPath} replace />;
  }

  const isLoading = contextLoading || fundingQuery.isLoading || !funding;

  const buildErrors = (required: boolean): Record<string, string> => {
    const next: Record<string, string> = {};
    (funding?.activities ?? []).forEach((activity) => {
      (funding?.remainingQuarters ?? []).forEach((quarter) => {
        const code = activityQuarterCode(activity.activityId, quarter.quarter);
        const error = validateMoney(values[code] ?? '', { required, subject: moneySubjectForForecast(activity.title, quarter.quarter) });
        if (error) {
          next[code] = error;
        }
      });
    });
    (funding?.remainingQuarters ?? []).forEach((quarter) => {
      const code = otherQuarterCode(quarter.quarter);
      const error = validateMoney(values[code] ?? '', { required, subject: moneySubjectForOtherForecast(quarter.quarter) });
      if (error) {
        next[code] = error;
      }
    });
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
        activities: (funding?.activities ?? []).map((activity) => ({
          activityId: activity.activityId,
          quarters: (funding?.remainingQuarters ?? []).map((quarter) => ({
            quarter: quarter.quarter,
            amount: parseAmount(values[activityQuarterCode(activity.activityId, quarter.quarter)] ?? ''),
          })),
        })),
        other: (funding?.remainingQuarters ?? []).map((quarter) => ({
          quarter: quarter.quarter,
          amount: parseAmount(values[otherQuarterCode(quarter.quarter)] ?? ''),
        })),
      },
      { onSuccess: onSaved, onError: applyServerError }
    );
  };

  const handleContinue = () => {
    if (!canEdit) {
      navigate(checkPath);
      return;
    }
    const validationErrors = buildErrors(true);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => navigate(checkPath));
  };

  const handleSaveDraft = () => {
    const validationErrors = buildErrors(false);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => {
      trackEvent('report_draft_saved', { collectionId, section: 'funding_spend_forecast' });
      navigate(tasksPath, { state: { banner: 'fundingProgressSaved' } });
    });
  };

  const handleChange = (code: string, value: string) => setValues((previous) => ({ ...previous, [code]: value }));

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout backLink={{ href: spendingPath, testId: 'reports-funding-spent-forecast-back' }}>
      <div className="reports-funding-forecast">
        {isLoading || !context || !funding ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

            <div className="reports-funding-forecast__header">
              <ReportFundingPageHeader heading="Forecast spend" reportingPeriod={reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd)} />

              <div className="reports-funding-forecast__intro">
                {FORECAST_INTRO.map((line) => (
                  <Paragraph key={line}>{line}</Paragraph>
                ))}
              </div>
            </div>

            <div className="reports-funding-forecast__activities">
              {funding.activities.map((activity) => (
                <section className="reports-funding-forecast__activity" key={activity.activityId}>
                  <span className="govuk-caption-m">Activity</span>
                  <GovUKGroupedInputs
                    stacked
                    legend={activity.title}
                    testId={`reports-funding-spent-forecast-${activity.activityId}`}
                    items={funding.remainingQuarters.map((quarter) => {
                      const code = activityQuarterCode(activity.activityId, quarter.quarter);
                      return {
                        code,
                        label: forecastQuarterLabel(quarter),
                        value: values[code] ?? '',
                        prefix: '£',
                        error: errors[code],
                        testId: code,
                      };
                    })}
                    onChange={handleChange}
                  />
                </section>
              ))}
            </div>

            <div className="reports-funding-forecast__other">
              <GovUKGroupedInputs
                stacked
                legend="Other planned spend"
                hint="Use this for any levy spending not linked to an activity"
                testId="reports-funding-spent-forecast-other"
                items={funding.remainingQuarters.map((quarter) => {
                  const code = otherQuarterCode(quarter.quarter);
                  return {
                    code,
                    label: forecastQuarterLabel(quarter),
                    value: values[code] ?? '',
                    prefix: '£',
                    error: errors[code],
                    testId: code,
                  };
                })}
                onChange={handleChange}
              />
            </div>

            <div className="govuk-button-group reports-funding-forecast__actions">
              <GovUKButton onClick={handleContinue} isLoading={saveMutation.isPending} data-testid="reports-funding-spent-forecast-continue">
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleSaveDraft}
                  isLoading={saveMutation.isPending}
                  data-testid="reports-funding-spent-forecast-save">
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

export default Forecast;

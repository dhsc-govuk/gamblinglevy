import React, { useEffect, useRef, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInputWithSuffix, GovUKInsetText, H2, H3, Paragraph, SummaryList } from '../../../../components/GovUKComponents';
import ReportFundingPageHeader from '../../../../components/Global/Reports/ReportFundingPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import { formatGBP } from '../../../../helpers/currency';
import usePageTitle from '../../../../hooks/usePageTitle';
import { laFundingDashboardKey, reportContextKey, reportFundingSpendKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getFundingSpend } from '../../queries';
import { saveFundingActuals } from '../../mutations';
import { mapReportError } from '../../messages';
import { reportingPeriodValue } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import { parseAmount } from '../../../../helpers/currency';
import {
  actualForActivity,
  actualSpendActivities,
  amountToInput,
  hasForecastQuarters,
  liveTotalSpendThisQuarter,
  TOTAL_UNAVAILABLE_MESSAGE,
  moneySubjectForActivity,
  OTHER_SPEND_SUBJECT,
  SPENDING_INTRO,
  summaryRows,
  validateMoney,
} from '../display';
import { trackEvent } from '../../../../helpers/analytics';
import './styles.css';

// "Activity spending" (BLUM-6612, screen 1): the levy actually spent this quarter per reportable activity,
// plus a single non-activity "Other spend this quarter". Continue saves a draft and moves to Forecast;
// "Save and come back later" returns to the task list with the section In progress.
const Spending = (): React.ReactElement => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [actuals, setActuals] = useState<Record<string, string>>({});
  const [otherSpend, setOtherSpend] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Activity spending`);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const forecastPath = ROUTES.reports.fundingSpentForecast(collectionId);
  const checkPath = ROUTES.reports.fundingSpentCheck(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const fundingQuery = useQuery({
    queryKey: reportFundingSpendKey(collectionId),
    queryFn: () => getFundingSpend(collectionId),
    enabled: !!collectionId && !!context?.submissionId,
  });
  const funding = fundingQuery.data?.data;
  // Planned activities are in the payload for "Forecast spend" only (BLUM-7672), so this screen reads the rest.
  const activities = funding ? actualSpendActivities(funding) : [];

  // Hydrate the inputs from the server once, so a background refetch (the save mutation below, or
  // invalidateReportCaches after an activity is added/edited in the return flow) can't clobber what the user is
  // part-way through typing. Same one-shot ref as Forecast, StrategicFeedbackQuestionsPage and FormDesigner/Edit
  // — but unlike those three, the summary box below reads it during *render*. Two properties make that sound,
  // and both must survive any edit here:
  //   - the ref write and setActuals are adjacent statements, and setActuals always gets a fresh object from
  //     reduce, so React can never bail out of the re-render and can never render between the two. There is no
  //     state in which this reads true while `actuals` is still empty.
  //   - it only ever goes false → true, and false is the safe reading: it shows the saved server summary, which
  //     is what should be on screen before hydration anyway. A discarded concurrent render costs nothing.
  const hydratedRef = useRef(false);
  useEffect(() => {
    if (funding && !hydratedRef.current) {
      hydratedRef.current = true;
      setActuals(
        actualSpendActivities(funding).reduce<Record<string, string>>((acc, activity) => {
          acc[activity.activityId] = amountToInput(activity.actualThisQuarter);
          return acc;
        }, {})
      );
      setOtherSpend(amountToInput(funding.otherActualThisQuarter));
    }
  }, [funding]);

  const saveMutation = useMutation({
    mutationFn: saveFundingActuals,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportFundingSpendKey(collectionId) });
      // This screen is the only one that writes Actual spend rows, and the funding page's "Spent to date" tile is
      // summed from them — it would otherwise keep showing the pre-save figure on the first paint after navigating
      // back (BLUM-7701). Same reason as the DHSC amend screen invalidating this key for the allocation figure.
      queryClient.invalidateQueries({ queryKey: laFundingDashboardKey });
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
    activities.forEach((activity) => {
      const error = validateMoney(actualForActivity(actuals, activity), { required, subject: moneySubjectForActivity(activity.title) });
      if (error) {
        next[`actual-${activity.activityId}`] = error;
      }
    });
    const otherError = validateMoney(otherSpend, { required, subject: OTHER_SPEND_SUBJECT });
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
        activities: activities.map((activity) => ({
          activityId: activity.activityId,
          amount: parseAmount(actualForActivity(actuals, activity)),
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

  // A non-null `issue` means at least one entry cannot stand for an amount, which is why the row shows an em
  // dash. The dash alone reads as nil in a money panel and says nothing about what is wrong, so the status
  // line below spells it out — and carries the live region, rather than the number, which would otherwise
  // announce on every keystroke. Null before hydration: the inputs are not populated yet, so there is nothing
  // to total and the saved summary is shown instead.
  const liveTotal = funding && hydratedRef.current ? liveTotalSpendThisQuarter(activities, actuals, otherSpend) : null;
  const otherTotalSpentSoFar = funding ? funding.otherTotalActualToDate - (funding.otherActualThisQuarter ?? 0) : 0;

  return (
    <GeneralLayout backLink={{ href: tasksPath, testId: 'reports-funding-spent-spending-back' }}>
      <div className="reports-funding-spending">
        {isLoading || !context || !funding ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={submitAttempts} />

            <div className="reports-funding-spending__header">
              <ReportFundingPageHeader
                heading="Activity spending"
                reportingPeriod={reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd)}
              />

              <div className="reports-funding-spending__intro">
                {SPENDING_INTRO.map((line) => (
                  <Paragraph key={line}>{line}</Paragraph>
                ))}
              </div>
            </div>

            <div className="reports-funding-spending__activities">
              {activities.map((activity) => (
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
                    visuallyHiddenText={`for ${activity.title}`}
                    prefix="£"
                    width="10"
                    value={actualForActivity(actuals, activity)}
                    error={errors[`actual-${activity.activityId}`]}
                    disabled={!canEdit}
                    testId={`reports-funding-spent-spending-actual-${activity.activityId}`}
                    onChange={(event) => setActuals((previous) => ({ ...previous, [activity.activityId]: event.target.value }))}
                  />
                </section>
              ))}
            </div>

            <section className="reports-funding-spending__other">
              <div className="reports-funding-spending__other-heading">
                <H2>Other spend</H2>
                <Paragraph>Use this for any levy spending not linked to an activity</Paragraph>
              </div>
              <GovUKInsetText data-testid="reports-funding-spent-spending-other-total-spent">Total spent so far: {formatGBP(otherTotalSpentSoFar)}</GovUKInsetText>
              <GovUKInputWithSuffix
                id="other-spend"
                name="other-spend"
                label="Levy funding spent this quarter"
                visuallyHiddenText="for other spend"
                prefix="£"
                width="10"
                value={otherSpend}
                error={errors['other-spend']}
                disabled={!canEdit}
                testId="reports-funding-spent-spending-other"
                onChange={(event) => setOtherSpend(event.target.value)}
              />
            </section>

            <GovUKInsetText className="reports-funding-spending__summary" data-testid="reports-funding-spent-spending-summary">
              <H3>Summary for all activities</H3>
              {/*
                "Total spend this quarter" tracks the inputs above rather than the last saved figure (BLUM-7465) —
                the server only recomputes it on save. The other rows keep their server values: both are
                prior-quarter or organisation data this screen cannot change. Before hydration the inputs are still
                empty, so the saved summary is shown as-is rather than flashing £0.
              */}
              <SummaryList
                items={summaryRows(funding.summary, false, true, liveTotal !== null ? liveTotal.total : undefined)}
                halfWidthColumns
                testId="reports-funding-spent-spending-summary-list"
              />
              {/*
                Rendered whenever the panel is, so the region exists before its content changes — a screen reader
                can miss text inserted along with its own live-region container. (The whole panel still unmounts
                while the query is loading, which is the one case that cannot be avoided from here.)
              */}
              <div role="status" aria-live="polite" aria-atomic="true" data-testid="reports-funding-spent-spending-summary-total-status">
                {liveTotal?.issue && <p className="govuk-hint govuk-!-margin-top-2 govuk-!-margin-bottom-0">{TOTAL_UNAVAILABLE_MESSAGE[liveTotal.issue]}</p>}
              </div>
            </GovUKInsetText>

            <div className="govuk-button-group reports-funding-spending__actions">
              <GovUKButton onClick={handleContinue} isLoading={saveMutation.isPending} data-testid="reports-funding-spent-spending-continue">
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleSaveDraft}
                  isLoading={saveMutation.isPending}
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

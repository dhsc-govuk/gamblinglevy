import React, { useMemo, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKSummaryCard, SummaryList } from '../../../../components/GovUKComponents';
import ReportActivityPageHeader from '../../../../components/Global/Reports/ReportActivityPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { reportActivitiesKey, reportActivityFormKey, reportContextKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getReportActivities, getReportActivityForm } from '../../queries';
import { completeActivityReport } from '../../mutations';
import { mapReportError } from '../../messages';
import { answersToRecord, reportingPeriodLabel, toFormQuestion, visibleQuestionRows } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import './styles.css';

// "Check your answers" (BLUM-6611): the saved results/delivery answers for one activity. Continue
// completes the activity's report via the complete endpoint (no body — the backend validates the saved
// answers); a 422 surfaces the per-question errors with links back to the owning page.
const CheckAnswers = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const { activityId = '' } = useParams<{ activityId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);
  // Which completion button is mid-request — "Continue" and "Report on another activity" share one
  // mutation instance, so this keeps the spinner on the button the user actually pressed.
  const [pendingAction, setPendingAction] = useState<'complete' | 'another' | null>(null);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();

  const formQuery = useQuery(reportActivityFormKey(collectionId, activityId), () => getReportActivityForm(collectionId, activityId), {
    enabled: !!collectionId && !!context?.submissionId && !!activityId,
  });
  const form = formQuery.data?.data;

  // The reportable activities with their per-activity progress, so "Report on another activity" can jump
  // straight to the next one still needing input. Already cached from the Select page the user came
  // through, so this resolves without a visible fetch.
  const activitiesQuery = useQuery(reportActivitiesKey(collectionId), () => getReportActivities(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const activities = activitiesQuery.data?.data ?? [];

  const allQuestions = useMemo(() => (form?.questions ?? []).map(toFormQuestion), [form]);
  // This page renders the saved server state — answers were persisted by the results/delivery pages.
  const answers = useMemo(() => answersToRecord(form?.answers ?? []), [form]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const completeMutation = useMutation(completeActivityReport, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      queryClient.invalidateQueries(reportActivitiesKey(collectionId));
      // cancelRefetch: a fetch already in flight (started before this mutation) would otherwise be reused
      // by the invalidation and resolve with pre-complete data (BLUM-6805).
      queryClient.invalidateQueries(reportActivityFormKey(collectionId, activityId), undefined, { cancelRefetch: true });
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

  // The whole page waits for any in-flight refetch, and a failed refetch renders the retry state
  // instead of the cached answers: the user is confirming what they see before completing, so a stale
  // render on a finalise step is misleading even though the backend validates the saved answers itself.
  // It also blocks on the activities list's first load so "Report on another activity" can resolve the
  // next activity on the very first click — otherwise a cold-cache deep-link straight onto this page
  // would complete the activity but bounce to the list (empty `activities`) instead of the next one
  // (BLUM-7020). `isLoading` (not `isFetching`) so a post-complete background refetch doesn't re-block.
  const loadFailed = formQuery.isError;
  const isLoading = contextLoading || formQuery.isLoading || formQuery.isFetching || activitiesQuery.isLoading || !form;

  const selectPath = ROUTES.reports.keyResults(collectionId);
  const resultsPath = ROUTES.reports.keyResultsActivity(collectionId, activityId);
  const deliveryPath = ROUTES.reports.keyResultsDelivery(collectionId, activityId);

  const resultsRows = visibleQuestionRows(
    allQuestions.filter((question) => question.sectionId === 'results'),
    answers
  );
  const deliveryRows = visibleQuestionRows(
    allQuestions.filter((question) => question.sectionId === 'delivery'),
    answers
  );

  // A 422 question error links back to the page that owns the question.
  const pageForError = (targetName: string): string | null => {
    const question = allQuestions.find((item) => item.code === targetName);
    if (!question) {
      return null;
    }
    return question.sectionId === 'delivery' ? deliveryPath : resultsPath;
  };

  // The next activity still needing input, searching forward (with wrap-around) from the current one so
  // the user is taken straight there instead of back to the list. The cached list predates this
  // completion, so the current activity is excluded by id rather than trusting its now-stale status.
  // 'completed' is the only status skipped — a partially-filled 'in_progress' activity still needs input.
  const nextIncompleteActivityId = (): string | null => {
    const total = activities.length;
    if (total === 0) {
      return null;
    }
    const currentIndex = Math.max(
      activities.findIndex((activity) => activity.activityId === activityId),
      0
    );
    for (let offset = 1; offset <= total; offset += 1) {
      const candidate = activities[(currentIndex + offset) % total];
      if (candidate.activityId !== activityId && candidate.reportStatus !== 'completed') {
        return candidate.activityId;
      }
    }
    return null;
  };

  const backToList = () => navigate(selectPath, { state: { banner: 'activityCompleted', name: form?.activityTitle } });

  // Both "Continue" and "Report on another activity" finalise the activity through the same complete
  // endpoint (a 422 surfaces the per-question errors) — they differ only in where success navigates.
  const completeAndThen = (action: 'complete' | 'another', onCompleted: () => void) => {
    setPendingAction(action);
    completeMutation.mutate(
      {
        collectionId,
        activityId,
      },
      {
        onSuccess: onCompleted,
        onError: (error) => {
          const mapped = mapReportError(error);
          setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
          setSubmitAttempts((attempts) => attempts + 1);
        },
        onSettled: () => setPendingAction(null),
      }
    );
  };

  const handleComplete = () => completeAndThen('complete', backToList);

  // BLUM-7020: complete the current activity, then go directly to the next one still to be filled in.
  // Falls back to the list (with the completion banner) when nothing else is left to report on.
  const handleReportAnother = () =>
    completeAndThen('another', () => {
      const nextId = nextIncompleteActivityId();
      if (!nextId) {
        backToList();
        return;
      }
      navigate(ROUTES.reports.keyResultsActivity(collectionId, nextId));
    });

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout backLink={{ href: deliveryPath, testId: 'reports-key-results-check-back' }}>
      <div className="reports-key-results-check">
        {loadFailed ? (
          <>
            {/* role="alert" announces the failure when the summary replaces the polite "Loading" status. */}
            <ErrorSummary
              role="alert"
              heading="There is a problem"
              errors={[{ targetName: 'form-load', text: 'Could not load your saved answers. Try again.' }]}
              onHandleErrorClick={() => formQuery.refetch()}
              data-testid="reports-key-results-check-load-error"
            />
            <div className="govuk-button-group govuk-!-margin-top-6">
              <GovUKButton onClick={() => formQuery.refetch()} isLoading={formQuery.isFetching} data-testid="reports-key-results-check-retry">
                Try again
              </GovUKButton>
            </div>
          </>
        ) : isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && (
              <ErrorSummary
                heading="There is a problem"
                errors={errorList}
                onHandleErrorClick={(targetName) => {
                  const target = pageForError(targetName);
                  if (target) {
                    navigate(target);
                  }
                }}
              />
            )}

            <div className="reports-key-results-check__header">
              <ReportActivityPageHeader heading="Check your answers" reportingPeriod={reportingPeriodLabel(context)} activityTitle={form.activityTitle} />
            </div>

            {resultsRows.length > 0 && (
              <GovUKSummaryCard
                title="Activity results"
                testId="reports-key-results-check-results"
                actions={[{ label: 'Change', onClick: () => navigate(resultsPath), visuallyHiddenText: 'activity results' }]}>
                <SummaryList items={resultsRows} halfWidthColumns testId="reports-key-results-check-results-list" />
              </GovUKSummaryCard>
            )}

            <GovUKSummaryCard
              title="Activity delivery"
              testId="reports-key-results-check-delivery"
              actions={[{ label: 'Change', onClick: () => navigate(deliveryPath), visuallyHiddenText: 'activity delivery' }]}>
              <SummaryList items={deliveryRows} halfWidthColumns testId="reports-key-results-check-delivery-list" />
            </GovUKSummaryCard>

            <div className="govuk-button-group reports-key-results-check__actions">
              {canEdit && (
                <GovUKButton
                  onClick={handleComplete}
                  isLoading={completeMutation.isLoading && pendingAction === 'complete'}
                  disabled={completeMutation.isLoading && pendingAction !== 'complete'}
                  data-testid="reports-key-results-check-continue">
                  Continue
                </GovUKButton>
              )}
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  disabled={completeMutation.isLoading}
                  onClick={() =>
                    navigate(selectPath, {
                      state: { banner: 'progressSaved', name: form?.activityTitle },
                    })
                  }
                  data-testid="reports-key-results-check-save">
                  Save and come back later
                </GovUKButton>
              )}
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleReportAnother}
                  isLoading={completeMutation.isLoading && pendingAction === 'another'}
                  disabled={completeMutation.isLoading && pendingAction !== 'another'}
                  data-testid="reports-key-results-check-another">
                  Report on another activity
                </GovUKButton>
              )}
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

// Param-only navigation (activity A -> B via back/forward) does not remount a route element, so
// key by collection + activity to reset errors/submit attempts for the new activity.
const Check = (): React.ReactElement => {
  const { collectionId = '', activityId = '' } = useParams<{ collectionId: string; activityId: string }>();
  return <CheckAnswers key={`${collectionId}:${activityId}`} />;
};

export default Check;

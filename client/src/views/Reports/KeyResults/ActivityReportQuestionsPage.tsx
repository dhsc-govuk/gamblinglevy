import React, { useEffect, useMemo, useRef, useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKDynamicQuestionRenderer } from '../../../components/GovUKComponents';
import ReportActivityPageHeader from '../../../components/Global/Reports/ReportActivityPageHeader';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';
import { reportActivitiesKey, reportActivityFormKey, reportContextKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { questionErrorTargetId, validateAnswers } from '../../Activities/answers';
import { getReportActivityForm, type ReportSection } from '../queries';
import { saveActivityReport } from '../mutations';
import { mapReportError } from '../messages';
import { answersToRecord, reportingPeriodLabel, toFormQuestion } from '../display';
import { useReportContext } from '../context';
import { canEditReport } from '../navigation';
import { trackEvent } from '../../../helpers/analytics';
import './ActivityReportQuestionsPage.css';

const SECTION_COPY: Record<ReportSection, { heading: string; note?: string }> = {
  results: { heading: 'Activity results', note: 'Only enter results for the quarter you are reporting on.' },
  delivery: { heading: 'Activity delivery' },
};

interface Props {
  section: ReportSection;
}

// One page of the "Enter activity key results" flow (BLUM-6611): the 'results' or 'delivery' questions
// for one activity. Continue validates this page only, saves this page's answers as a draft (the backend
// replaces only these question codes, so saving one page never wipes the other's) and moves on;
// "Save and come back later" skips required checks (type/range errors still block) and returns to the
// activity picker. Completion happens on the Check page via the complete endpoint.
const ActivityReportQuestions = ({ section }: Props): React.ReactElement => {
  const copy = SECTION_COPY[section];
  usePageTitle(copy.heading);
  const { activityId = '' } = useParams<{ activityId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);
  const [hydrated, setHydrated] = useState(false);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();

  const formQuery = useQuery(reportActivityFormKey(collectionId, activityId), () => getReportActivityForm(collectionId, activityId), {
    enabled: !!collectionId && !!context?.submissionId && !!activityId,
  });
  const form = formQuery.data?.data;

  // dataUpdatedAt as of the first render: hydration waits until it moves on (a fetch resolved after
  // mount), which unlike a Date.now() comparison is immune to wall-clock steps between mount and fetch.
  const initialDataUpdatedAtRef = useRef(formQuery.dataUpdatedAt);

  // Hydrate only from data fetched after this mount: reopening this page right after a save can hand us
  // the stale pre-save cache (the invalidation refetch may still be in flight), and a form seeded from it
  // would show — and on the next save silently revert — this page's just-saved answers (BLUM-6805).
  // dataUpdatedAt drives the deps — structural sharing keeps `form` referentially stable when a
  // refetch returns identical data. A failed refetch renders the retry state below instead of
  // falling back to the stale cache. The not-stale branch never fires under the default staleTime of 0;
  // it exists so a future staleTime on this query cannot leave the page hanging when a fresh-at-mount
  // cache means no refetch is coming.
  useEffect(() => {
    if (!form || hydrated) {
      return;
    }
    if (formQuery.dataUpdatedAt !== initialDataUpdatedAtRef.current || (!formQuery.isFetching && !formQuery.isStale)) {
      setHydrated(true);
      setAnswers(answersToRecord(form.answers));
    }
  }, [form, hydrated, formQuery.dataUpdatedAt, formQuery.isFetching, formQuery.isStale]);

  const allQuestions = useMemo(() => (form?.questions ?? []).map(toFormQuestion), [form]);
  const pageQuestions = useMemo(() => allQuestions.filter((question) => question.sectionId === section), [allQuestions, section]);
  const resultsQuestions = useMemo(() => allQuestions.filter((question) => question.sectionId === 'results'), [allQuestions]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const saveMutation = useMutation(saveActivityReport, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      queryClient.invalidateQueries(reportActivitiesKey(collectionId));
      // cancelRefetch: a fetch already in flight (started before this save) would otherwise be reused
      // by the invalidation and resolve with pre-save data that passes the next page's hydration gate.
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

  const selectPath = ROUTES.reports.keyResults(collectionId);
  const resultsPath = ROUTES.reports.keyResultsActivity(collectionId, activityId);
  const deliveryPath = ROUTES.reports.keyResultsDelivery(collectionId, activityId);
  const checkPath = ROUTES.reports.keyResultsCheck(collectionId, activityId);
  const nextPath = section === 'results' ? deliveryPath : checkPath;
  // For the delivery page on people-and-roles activities, resultsQuestions will be empty and the results
  // page immediately redirects back to delivery — so go up to the activity picker instead.
  const backHref = section === 'results' || (!!form && resultsQuestions.length === 0) ? selectPath : resultsPath;

  // People-and-roles activities have no 'results' questions — skip straight to the delivery page.
  // Decided only after hydration: a stale cached form could otherwise skip past results questions a
  // recent activity edit added.
  if (form && hydrated && section === 'results' && pageQuestions.length === 0) {
    return <Navigate to={deliveryPath} replace />;
  }

  // A refetch that fails before hydration renders the retry state instead of the form — a form seeded
  // from the stale cache would show and then re-save this page's pre-save values.
  const loadFailed = formQuery.isError && !hydrated;
  const isLoading = contextLoading || formQuery.isLoading || !form || !hydrated;

  const applyServerError = (error: unknown) => {
    const mapped = mapReportError(error);
    setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
    setSubmitAttempts((attempts) => attempts + 1);
  };

  const save = (onSaved: () => void) => {
    saveMutation.mutate(
      {
        collectionId,
        activityId,
        saveAsDraft: true,
        // This page's codes only, cleared fields included (a blank value deletes that saved answer) —
        // the backend merges per code, so the other page's answers are never touched.
        answers: pageQuestions.map((question) => ({ questionCode: question.code, value: answers[question.code] ?? '' })),
      },
      { onSuccess: onSaved, onError: applyServerError }
    );
  };

  const handleContinue = () => {
    // View-only roles page through without saving (mutations are forbidden server-side).
    if (!canEdit) {
      navigate(nextPath);
      return;
    }
    const validationErrors = validateAnswers(pageQuestions, answers);
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => navigate(nextPath));
  };

  const handleSaveDraft = () => {
    const validationErrors = validateAnswers(pageQuestions, answers, { enforceRequired: false });
    setErrors(validationErrors);
    setSubmitAttempts((attempts) => attempts + 1);
    if (Object.keys(validationErrors).length > 0) {
      return;
    }
    save(() => {
      trackEvent('report_draft_saved', { collectionId, section });
      navigate(selectPath, { state: { banner: 'progressSaved', name: form?.activityTitle } });
    });
  };

  const handleAnswerChange = (questionCode: string, value: string) => {
    setAnswers((previous) => ({ ...previous, [questionCode]: value }));
  };

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => {
      const question = pageQuestions.find((item) => item.code === key);
      return { targetName: question ? questionErrorTargetId(question) : key.toLowerCase(), text: message };
    });

  return (
    <GeneralLayout backLink={{ href: backHref, testId: `reports-key-results-${section}-back` }}>
      <div className={`reports-key-results-questions reports-key-results-questions--${section}`}>
        {loadFailed ? (
          <>
            {/* role="alert" announces the failure when the summary replaces the polite "Loading" status. */}
            <ErrorSummary
              role="alert"
              heading="There is a problem"
              errors={[{ targetName: 'form-load', text: 'Could not load your saved answers. Try again.' }]}
              onHandleErrorClick={() => formQuery.refetch()}
              data-testid={`reports-key-results-${section}-load-error`}
            />
            <div className="govuk-button-group govuk-!-margin-top-6">
              <GovUKButton onClick={() => formQuery.refetch()} isLoading={formQuery.isFetching} data-testid={`reports-key-results-${section}-retry`}>
                Try again
              </GovUKButton>
            </div>
          </>
        ) : isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

            <div className="reports-key-results-questions__header">
              <ReportActivityPageHeader heading={copy.heading} reportingPeriod={reportingPeriodLabel(context)} activityTitle={form.activityTitle} note={copy.note} />
            </div>

            <div className="reports-key-results-questions__form">
              <GovUKDynamicQuestionRenderer
                questions={pageQuestions}
                answers={answers}
                errors={errors}
                onAnswerChange={handleAnswerChange}
                questionSize={section === 'delivery' ? 'm' : undefined}
                showQuestionCode={false}
              />
            </div>

            <div className="govuk-button-group govuk-!-margin-top-6 reports-key-results-questions__actions">
              <GovUKButton onClick={handleContinue} isLoading={saveMutation.isLoading} data-testid={`reports-key-results-${section}-continue`}>
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleSaveDraft}
                  isLoading={saveMutation.isLoading}
                  data-testid={`reports-key-results-${section}-save`}>
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

// Param-only navigation (activity A -> B via back/forward) does not remount a route element, so
// key by collection + activity to reset answers/errors/hydration for the new activity.
const ActivityReportQuestionsPage = ({ section }: Props): React.ReactElement => {
  const { collectionId = '', activityId = '' } = useParams<{ collectionId: string; activityId: string }>();
  return <ActivityReportQuestions key={`${collectionId}:${activityId}`} section={section} />;
};

export default ActivityReportQuestionsPage;

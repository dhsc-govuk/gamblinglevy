import React, { useEffect, useMemo, useRef, useState } from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKDynamicQuestionRenderer } from '../../../components/GovUKComponents';
import ReportStrategicFeedbackPageHeader from '../../../components/Global/Reports/ReportStrategicFeedbackPageHeader';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../hooks/useErrorSummaryFocus';
import { reportContextKey, reportStrategicFeedbackKey, reportTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { questionErrorTargetId, validateAnswers } from '../../Activities/answers';
import { getStrategicFeedback } from '../queries';
import { saveStrategicFeedback } from '../mutations';
import { mapReportError } from '../messages';
import { answersToRecord, reportingPeriodLabel, toFormQuestion } from '../display';
import { useReportContext } from '../context';
import { canEditReport } from '../navigation';
import { trackEvent } from '../../../helpers/analytics';
import './styles.css';

export type StrategicFeedbackPage = 'wider-progress' | 'working-together';

const PAGE_COPY: Record<StrategicFeedbackPage, { heading: string }> = {
  'wider-progress': { heading: 'Wider progress and challenges' },
  'working-together': { heading: 'Working together' },
};

interface Props {
  page: StrategicFeedbackPage;
}

// One page of the "Provide strategic feedback" flow (BLUM-6613): the two free-text questions for one page.
// Continue validates this page only, saves the full answer set (both pages) as a draft and moves on; "Save and
// come back later" skips required checks and returns to the task list. No conditional logic.
const StrategicFeedbackQuestionsPage = ({ page }: Props): React.ReactElement => {
  const copy = PAGE_COPY[page];
  usePageTitle(copy.heading);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const page1Path = ROUTES.reports.strategicFeedback(collectionId);
  const page2Path = ROUTES.reports.strategicFeedbackWorkingTogether(collectionId);
  const checkPath = ROUTES.reports.strategicFeedbackCheck(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);

  const feedbackQuery = useQuery(reportStrategicFeedbackKey(collectionId), () => getStrategicFeedback(collectionId), {
    enabled: !!collectionId && !!context?.submissionId,
  });
  const feedback = feedbackQuery.data?.data;

  // Hydrate once per mount — answers cover BOTH pages, so this page's edits merge over the other page's saved
  // values when the full set is collected on save.
  const hydratedRef = useRef(false);
  useEffect(() => {
    if (feedback && !hydratedRef.current) {
      hydratedRef.current = true;
      setAnswers(answersToRecord(feedback.answers));
    }
  }, [feedback]);

  const allQuestions = useMemo(() => (feedback?.pages ?? []).flatMap((p) => p.questions).map(toFormQuestion), [feedback]);
  const pageQuestions = useMemo(() => allQuestions.filter((question) => question.sectionId === page), [allQuestions, page]);

  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  const saveMutation = useMutation(saveStrategicFeedback, {
    onSuccess: () => {
      queryClient.invalidateQueries(reportContextKey(collectionId));
      queryClient.invalidateQueries(reportTaskListKey(collectionId));
      queryClient.invalidateQueries(reportStrategicFeedbackKey(collectionId));
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

  const nextPath = page === 'wider-progress' ? page2Path : checkPath;
  const backHref = page === 'wider-progress' ? tasksPath : page1Path;

  const isLoading = contextLoading || feedbackQuery.isLoading || !feedback;

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
        // Send this page's questions (including any cleared to empty). The backend replaces only these answers, so
        // saving one page never wipes the other page's saved answers.
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
      trackEvent('report_draft_saved', { collectionId, section: `strategic_feedback_${page}` });
      navigate(tasksPath, { state: { banner: 'feedbackProgressSaved' } });
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
    <GeneralLayout backLink={{ href: backHref, testId: `reports-strategic-feedback-${page}-back` }}>
      <div className={`reports-strategic-feedback-questions reports-strategic-feedback-questions--${page}`}>
        {isLoading || !context ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

            <div className="reports-strategic-feedback-questions__header">
              <ReportStrategicFeedbackPageHeader heading={copy.heading} reportingPeriod={reportingPeriodLabel(context)} />
            </div>

            <div className="reports-strategic-feedback-questions__form">
              <GovUKDynamicQuestionRenderer
                questions={pageQuestions}
                answers={answers}
                errors={errors}
                onAnswerChange={handleAnswerChange}
                controlSize={page === 'working-together' ? 'small' : undefined}
                questionSize="m"
                showQuestionCode={false}
              />
            </div>

            <div className="govuk-button-group reports-strategic-feedback-questions__actions">
              <GovUKButton onClick={handleContinue} isLoading={saveMutation.isLoading} data-testid={`reports-strategic-feedback-${page}-continue`}>
                Continue
              </GovUKButton>
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={handleSaveDraft}
                  isLoading={saveMutation.isLoading}
                  data-testid={`reports-strategic-feedback-${page}-save`}>
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

export default StrategicFeedbackQuestionsPage;

import React, { useMemo, useState } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { Navigate, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, GovUKSummaryCard, SummaryList } from '../../../../components/GovUKComponents';
import ReportStrategicFeedbackPageHeader from '../../../../components/Global/Reports/ReportStrategicFeedbackPageHeader';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { reportContextKey, reportStrategicFeedbackKey, reportTaskListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getStrategicFeedback } from '../../queries';
import { completeStrategicFeedback } from '../../mutations';
import { mapReportError } from '../../messages';
import { answersToRecord, reportingPeriodValue, toFormQuestion, visibleQuestionRows } from '../../display';
import { useReportContext } from '../../context';
import { canEditReport } from '../../navigation';
import './styles.css';

// "Check your answers" (BLUM-6613): the saved feedback answers grouped by page. Continue completes the section (the
// backend validates every required answer); a 422 surfaces the errors with links back to the page that owns each one.
const Check = (): React.ReactElement => {
  usePageTitle('Check your answers');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditReport(userRole);

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { collectionId, context, isLoading: contextLoading, isError: contextError } = useReportContext();
  const page1Path = ROUTES.reports.strategicFeedback(collectionId);
  const page2Path = ROUTES.reports.strategicFeedbackWorkingTogether(collectionId);
  const tasksPath = ROUTES.reports.tasks(collectionId);
  const pathForPage = (key: string): string => (key === 'working-together' ? page2Path : page1Path);

  const feedbackQuery = useQuery({
    queryKey: reportStrategicFeedbackKey(collectionId),
    queryFn: () => getStrategicFeedback(collectionId),
    enabled: !!collectionId && !!context?.submissionId,
  });
  const feedback = feedbackQuery.data?.data;

  // This page renders the saved server state — answers were persisted by the question pages.
  const allQuestions = useMemo(() => (feedback?.pages ?? []).flatMap((p) => p.questions).map(toFormQuestion), [feedback]);
  const answers = useMemo(() => answersToRecord(feedback?.answers ?? []), [feedback]);

  const completeMutation = useMutation({
    mutationFn: completeStrategicFeedback,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: reportContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportTaskListKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: reportStrategicFeedbackKey(collectionId) });
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

  const isLoading = contextLoading || feedbackQuery.isLoading || !feedback;

  // A 422 question error links back to the page that owns the question.
  const pageForError = (targetName: string): string | null => {
    const question = allQuestions.find((item) => item.code === targetName);
    return question ? pathForPage(question.sectionId) : null;
  };

  const handleComplete = () => {
    completeMutation.mutate(collectionId, {
      onSuccess: () => navigate(tasksPath, { state: { banner: 'feedbackCompleted' } }),
      onError: (error) => {
        const mapped = mapReportError(error);
        setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
        setSubmitAttempts((attempts) => attempts + 1);
      },
    });
  };

  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout backLink={{ href: page2Path, testId: 'reports-strategic-feedback-check-back' }}>
      <div className="reports-strategic-feedback-check">
        {isLoading || !context || !feedback ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary
              heading="There is a problem"
              errors={errorList}
              focusTrigger={submitAttempts}
              onHandleErrorClick={(targetName) => {
                const target = pageForError(targetName);
                if (target) {
                  navigate(target);
                }
              }}
            />

            <div className="reports-strategic-feedback-check__header">
              <ReportStrategicFeedbackPageHeader
                heading="Check your answers"
                reportingPeriod={reportingPeriodValue(context.quarter, context.reportingPeriodStart, context.reportingPeriodEnd)}
              />
            </div>

            {feedback.pages.map((feedbackPage) => (
              <GovUKSummaryCard
                key={feedbackPage.key}
                title={feedbackPage.title}
                testId={`reports-strategic-feedback-check-${feedbackPage.key}`}
                actions={[{ label: 'Change', onClick: () => navigate(pathForPage(feedbackPage.key)), visuallyHiddenText: feedbackPage.title.toLowerCase() }]}>
                <SummaryList
                  items={visibleQuestionRows(
                    allQuestions.filter((question) => question.sectionId === feedbackPage.key),
                    answers
                  )}
                  halfWidthColumns
                  testId={`reports-strategic-feedback-check-${feedbackPage.key}-list`}
                />
              </GovUKSummaryCard>
            ))}

            <div className="govuk-button-group reports-strategic-feedback-check__actions">
              {canEdit && (
                <GovUKButton onClick={handleComplete} isLoading={completeMutation.isPending} data-testid="reports-strategic-feedback-check-continue">
                  Continue
                </GovUKButton>
              )}
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={() => navigate(tasksPath, { state: { banner: 'feedbackProgressSaved' } })}
                  data-testid="reports-strategic-feedback-check-save">
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

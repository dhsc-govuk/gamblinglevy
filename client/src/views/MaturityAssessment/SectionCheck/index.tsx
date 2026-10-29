import React, { useMemo, useState } from 'react';
import type { AxiosResponse } from 'axios';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { ErrorSummary, GovUKButton, GovUKInsetText, GovUKSummaryCard, H1, PageLoading, SummaryList } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId, slugifyTestId } from '../../../helpers/testIds';
import { maturityContextKey, maturitySectionKey, maturityTaskListKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { getMaturitySection, type MaturitySectionDto } from '../queries';
import { completeMaturitySection } from '../mutations';
import { mapMaturityError } from '../messages';
import { checkPageRows, maturityAnswersToRecord, maturityPeriodValue } from '../display';
import { pageOwning, visiblePages } from '../pages';
import { useMaturityContextFor } from '../context';
import { canEditMaturityAssessment } from '../navigation';
import './styles.css';

const CONTINUE_ID = 'la-maturity-check-continue';

// A section's "Check your answers" (BLUM-7858, Figma 2744-182300): one summary card per VISIBLE page with a Change
// link, Continue (the backend validates every required answer and marks the section completed) and "Save and come
// back later". A 422 lists the errors with links to the page that owns each question.
const SectionCheck = (): React.ReactElement => {
  const { collectionId = '', sectionCode = '' } = useParams<{ collectionId: string; sectionCode: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const canEdit = canEditMaturityAssessment(userRole);

  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  const { context, isLoading: contextLoading, isError: contextError } = useMaturityContextFor(collectionId);
  const tasksPath = ROUTES.reports.maturity(collectionId);

  const sectionQuery = useQuery({
    queryKey: maturitySectionKey(collectionId, sectionCode),
    queryFn: () => getMaturitySection(collectionId, sectionCode),
    enabled: !!collectionId && !!sectionCode && !!context?.assessmentId,
  });
  const section = sectionQuery.data?.data;
  const answers = useMemo(() => maturityAnswersToRecord(section?.answers ?? []), [section]);
  const pages = useMemo(() => (section ? visiblePages(section.pages, answers) : []), [section, answers]);

  const hasErrors = Object.keys(errors).length > 0;
  usePageTitle(`${hasErrors ? 'Error: ' : ''}Check your answers`);

  const completeMutation = useMutation({
    mutationFn: completeMaturitySection,
    onSuccess: (response) => {
      // Seed the cached status before invalidating, so SectionEntry (which forwards on the cached status) sends the
      // next visit to check-your-answers rather than back to the first page while the refetch is in flight.
      queryClient.setQueryData<AxiosResponse<MaturitySectionDto> | undefined>(maturitySectionKey(collectionId, sectionCode), (cached) =>
        cached ? { ...cached, data: { ...cached.data, status: response.data.status } } : cached
      );
      queryClient.invalidateQueries({ queryKey: maturityContextKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: maturityTaskListKey(collectionId) });
      queryClient.invalidateQueries({ queryKey: maturitySectionKey(collectionId, sectionCode) });
    },
  });

  if (contextError || sectionQuery.isError) {
    return <Navigate to={tasksPath} replace />;
  }

  if (!contextLoading && (!context || !context.assessmentId)) {
    return <Navigate to={tasksPath} replace />;
  }

  if (context?.submittedAt) {
    return <Navigate to={ROUTES.reports.maturitySubmittedConfirmation(collectionId)} replace />;
  }

  const isLoading = contextLoading || sectionQuery.isLoading || !section || !context;
  const pagePath = (pageCode: string) => ROUTES.reports.maturitySectionPage(collectionId, sectionCode, pageCode);
  const lastPage = pages[pages.length - 1];
  const backHref = lastPage ? pagePath(lastPage.code) : tasksPath;

  const handleComplete = () => {
    completeMutation.mutate(
      { collectionId, sectionCode },
      {
        onSuccess: () => navigate(tasksPath, { state: { banner: 'sectionCompleted', sectionTitle: section?.title } }),
        onError: (error) => {
          const mapped = mapMaturityError(error);
          setErrors(Object.keys(mapped.fieldErrors).length > 0 ? mapped.fieldErrors : { submit: mapped.message });
          setSubmitAttempts((attempts) => attempts + 1);
        },
      }
    );
  };

  // A question error links to the page that owns it (below); a page-level refusal links to the Continue button, which
  // carries the id — an href with no element behind it is a link that does nothing for keyboard and screen-reader users.
  const errorList = Object.entries(errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key === 'submit' ? CONTINUE_ID : key, text: message }));

  return (
    <GeneralLayout backLink={{ href: backHref, testId: 'la-maturity-check-back' }}>
      <div className="maturity-check" data-testid="la-maturity-check">
        {isLoading || !section ? (
          <PageLoading />
        ) : (
          <>
            <ErrorSummary
              heading="There is a problem"
              errors={errorList}
              focusTrigger={submitAttempts}
              testId="la-maturity-check-errors"
              onHandleErrorClick={(targetName) => {
                const owner = pageOwning(section.pages, targetName);
                if (owner) {
                  navigate(pagePath(owner));
                  return;
                }
                // Not a question: the summary has already prevented the default jump, so land on the target ourselves.
                document.getElementById(targetName)?.focus();
              }}
            />

            <div className="maturity-check__header">
              <span className="govuk-caption-l">{section.title}</span>
              <H1>Check your answers</H1>
              {context && (
                <GovUKInsetText className="maturity-check__details" data-testid="la-maturity-check-details">
                  <SummaryList
                    halfWidthColumns
                    testId="la-maturity-check-summary"
                    items={[{ label: 'Reporting period', value: maturityPeriodValue(context.reportingPeriodStart, context.reportingPeriodEnd) }]}
                  />
                </GovUKInsetText>
              )}
            </div>

            {pages.map((page) => {
              const cardTestId = composeTestId('la-maturity-check', slugifyTestId(page.code));
              const listTestId = composeTestId(cardTestId, 'list');
              return (
                <GovUKSummaryCard
                  key={page.code}
                  title={page.title}
                  testId={cardTestId}
                  actions={
                    canEdit
                      ? [
                          {
                            label: 'Change',
                            onClick: () => navigate(pagePath(page.code)),
                            visuallyHiddenText: page.title.toLowerCase(),
                            testId: composeTestId(cardTestId, 'change'),
                          },
                        ]
                      : []
                  }>
                  <SummaryList items={checkPageRows(page, answers, listTestId)} halfWidthColumns plainSubAnswers testId={listTestId} />
                </GovUKSummaryCard>
              );
            })}

            <div className="govuk-button-group maturity-check__actions">
              {canEdit && (
                <GovUKButton onClick={handleComplete} isLoading={completeMutation.isPending} id={CONTINUE_ID} name={CONTINUE_ID} data-testid="la-maturity-check-continue">
                  Continue
                </GovUKButton>
              )}
              {canEdit && (
                <GovUKButton
                  className="govuk-button govuk-button--secondary"
                  onClick={() => navigate(tasksPath, { state: { banner: 'progressSaved', sectionTitle: section.title } })}
                  data-testid="la-maturity-check-save">
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

export default SectionCheck;

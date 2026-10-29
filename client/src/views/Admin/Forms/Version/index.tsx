import React, { useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import {
  DeleteConfirmDialog,
  ErrorSummary,
  GovUKButton,
  GovUKInsetText,
  GovUKSummaryCard,
  H1,
  H2,
  NotFound,
  PageLoading,
  Paragraph,
  SummaryList,
} from '../../../../components/GovUKComponents';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle, { pageTitleWithName } from '../../../../hooks/usePageTitle';
import { defaultStaleTime, formModuleKey, formModuleVersionKey, formModuleVersionsKey, formModulesKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { composeTestId, slugifyTestId } from '../../../../helpers/testIds';
import { formatDateTime } from '../../../../helpers/dateDisplay';
import { isNotFoundError } from '../../../../helpers/httpErrors';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';
import { getFormModule, getFormModuleVersion, type DcQuestionType, type FormVersionQuestionDto } from '../queries';
import { restoreFormModuleVersion } from '../mutations';
import { formsErrorCode, mapFormsError } from '../messages';
import { QUESTION_TYPE_LABELS } from '../../../../components/Global/Forms/QuestionForm';
import { shouldRetryQuery } from '../../../../helpers/queryRetry';

const RESTORE_ID = 'admin-forms-version-restore';

const typeLabel = (type: string): string => (type in QUESTION_TYPE_LABELS ? QUESTION_TYPE_LABELS[type as DcQuestionType] : type);

// A 404 is a verdict (no such version, or the flag is off), so it is not retried; everything else keeps the shared rule.
const retryUnlessNotFound = (failureCount: number, error: unknown): boolean => !isNotFoundError(error) && shouldRetryQuery(failureCount, error);

// One question as the version saved it. Keyed on the question code, which never changes.
const QuestionCard = ({ question }: { question: FormVersionQuestionDto }): React.ReactElement => {
  const testId = composeTestId('admin-forms-version-question', slugifyTestId(question.code) || question.id);
  const options = question.options.slice().sort((a, b) => a.displayOrder - b.displayOrder);
  return (
    <GovUKSummaryCard title={question.label} headingLevel={3} testId={testId}>
      <SummaryList
        testId={composeTestId(testId, 'details')}
        items={[
          { label: 'Question code', value: question.code },
          { label: 'Answer type', value: typeLabel(question.questionType) },
          { label: 'Required', value: question.isRequired ? 'Yes' : 'No' },
          ...(question.hint ? [{ label: 'Hint', value: question.hint }] : []),
          ...(question.helpText ? [{ label: 'Help text', value: question.helpText }] : []),
          ...(options.length > 0
            ? [
                {
                  label: 'Options',
                  value: (
                    <ul className="govuk-list govuk-list--bullet">
                      {options.map((option) => (
                        <li key={option.value}>
                          {option.label}
                          {option.isActive ? '' : ' (inactive)'}
                        </li>
                      ))}
                    </ul>
                  ),
                },
              ]
            : []),
          ...(question.isActive ? [] : [{ label: 'Status', value: 'Not shown (inactive)' }]),
        ]}
      />
    </GovUKSummaryCard>
  );
};

// BLUM-9102: a saved version of a form, read-only, with "Restore this version". Restoring replaces the form's draft
// with this version's questions; publishing it afterwards saves the next version, so this one never changes.
const FormVersion = (): React.ReactElement => {
  const { moduleId = '', versionNumber: versionParam = '' } = useParams<{ moduleId: string; versionNumber: string }>();
  const versionNumber = Number(versionParam);
  const validNumber = Number.isInteger(versionNumber) && versionNumber > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [confirming, setConfirming] = useState(false);
  const [restoreError, setRestoreError] = useState<string | null>(null);
  const [restoreAttempts, setRestoreAttempts] = useState(0);

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate(ROUTES.admin.home);
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery({
    queryKey: formModuleKey(moduleId),
    queryFn: () => getFormModule(moduleId),
    staleTime: defaultStaleTime,
    enabled: Boolean(moduleId),
    retry: retryUnlessNotFound,
  });
  // A version never changes, so it can be cached for as long as the reference data is.
  const versionQuery = useQuery({
    queryKey: formModuleVersionKey(moduleId, versionNumber),
    queryFn: () => getFormModuleVersion(moduleId, versionNumber),
    staleTime: defaultStaleTime,
    enabled: Boolean(moduleId) && validNumber,
    retry: retryUnlessNotFound,
  });
  const module = moduleQuery.data?.data;
  const version = versionQuery.data?.data;

  const restoreM = useMutation({
    mutationFn: restoreFormModuleVersion,
    onSuccess: (response) => {
      // Seed the form page with the restored draft, so it does not first show the cached published form under a
      // banner saying it is now a draft.
      queryClient.setQueryData(formModuleKey(moduleId), response);
      queryClient.invalidateQueries({ queryKey: formModulesKey });
      queryClient.invalidateQueries({ queryKey: formModuleVersionsKey(moduleId) });
      navigate(ROUTES.admin.forms.module(moduleId), {
        state: { banner: `Version ${versionNumber} has been restored as a draft. Publish the form to save it as a new version.` },
      });
    },
    onError: (error) => {
      setConfirming(false);
      setRestoreError(mapFormsError(error));
    },
  });

  const versionLabel = validNumber ? `Version ${versionNumber}` : 'Version not found';
  usePageTitle(`${restoreError ? 'Error: ' : ''}${pageTitleWithName(versionLabel, module?.name)}`);

  // A bodiless 404 is the API's answer while BLUM_9087 is off (switched off since this tab loaded). A 404 that names
  // the version is a version this form does not have, and gets its own page below.
  if (isNotFoundError(versionQuery.error) && formsErrorCode(versionQuery.error) !== 'VERSION_NOT_FOUND') {
    return <NotFound goBack={navigate} />;
  }

  const sections = (version?.sections ?? []).slice().sort((a, b) => a.sectionNumber - b.sectionNumber);
  const questions = (version?.questions ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);
  const unsectioned = questions.filter((q) => !q.sectionId || !sections.some((s) => s.id === q.sectionId));
  const canRestore = module !== undefined && module.status !== 'archived';

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs([
        { label: module?.name ?? 'Form', link: ROUTES.admin.forms.module(moduleId) },
        { label: 'Previous versions', link: ROUTES.admin.forms.versions(moduleId) },
      ])}
      backLink={{ href: ROUTES.admin.forms.versions(moduleId) }}>
      <div className="govuk-!-padding-top-4" data-testid="admin-forms-version">
        {moduleQuery.isLoading || versionQuery.isLoading ? (
          <PageLoading />
        ) : !version || !module ? (
          <>
            <H1>Version not found</H1>
            <Paragraph data-testid="admin-forms-version-not-found">This version of the form could not be found.</Paragraph>
            <RouterLink to={ROUTES.admin.forms.versions(moduleId)} className="govuk-link" data-testid="admin-forms-version-back-to-list">
              Return to previous versions
            </RouterLink>
          </>
        ) : (
          <>
            <ErrorSummary errors={restoreError ? [{ targetName: RESTORE_ID, text: restoreError }] : []} focusTrigger={restoreAttempts} testId="admin-forms-version-error" />
            {/* The name the form had in this version, which may differ from its name now. */}
            <span className="govuk-caption-l">{version.name}</span>
            <H1>Version {version.versionNumber}</H1>

            <SummaryList
              testId="admin-forms-version-summary"
              items={[
                { label: 'Published', value: formatDateTime(version.publishedAt) },
                { label: 'Published by', value: version.publishedByName ?? 'Unknown user' },
              ]}
            />

            {canRestore ? (
              <>
                <GovUKInsetText>
                  Restoring this version replaces the form’s current questions with the ones below. Changes that have not been published will be lost. Publish the form afterwards
                  to save it as a new version.
                </GovUKInsetText>
                <div className="govuk-button-group">
                  <GovUKButton
                    id={RESTORE_ID}
                    className="govuk-button govuk-button--secondary"
                    onClick={() => {
                      setRestoreError(null);
                      setConfirming(true);
                    }}
                    disabled={restoreM.isPending}
                    data-testid={RESTORE_ID}>
                    Restore this version
                  </GovUKButton>
                </div>
              </>
            ) : (
              <GovUKInsetText data-testid="admin-forms-version-archived">This form is archived, so this version cannot be restored.</GovUKInsetText>
            )}

            {sections.map((section) => {
              const sectionQuestions = questions.filter((q) => q.sectionId === section.id);
              return (
                <section key={section.id} data-testid={composeTestId('admin-forms-version-section', section.sectionNumber)}>
                  <H2>
                    {section.title}
                    {section.isActive ? '' : ' (inactive)'}
                  </H2>
                  {section.description && <Paragraph>{section.description}</Paragraph>}
                  {section.helpText && <Paragraph>{section.helpText}</Paragraph>}
                  {sectionQuestions.length === 0 ? (
                    <Paragraph>No questions in this section.</Paragraph>
                  ) : (
                    sectionQuestions.map((question) => <QuestionCard key={question.id} question={question} />)
                  )}
                </section>
              );
            })}

            {unsectioned.length > 0 && (
              <section data-testid="admin-forms-version-unsectioned">
                <H2>{sections.length > 0 ? 'Questions not in a section' : 'Questions'}</H2>
                {unsectioned.map((question) => (
                  <QuestionCard key={question.id} question={question} />
                ))}
              </section>
            )}

            <DeleteConfirmDialog
              open={confirming}
              title={`Restore version ${version.versionNumber}?`}
              message="This replaces the form’s current questions with the questions from this version. Changes that have not been published will be lost."
              confirmLabel={`Restore version ${version.versionNumber}`}
              isLoading={restoreM.isPending}
              onConfirm={() => {
                setRestoreAttempts((attempts) => attempts + 1);
                setRestoreError(null);
                restoreM.mutate({ moduleId, versionNumber: version.versionNumber });
              }}
              onCancel={() => setConfirming(false)}
              testId="admin-forms-version-restore-dialog"
            />
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormVersion;

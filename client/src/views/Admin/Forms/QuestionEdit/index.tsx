import React, { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { H1, PageLoading, Paragraph } from '../../../../components/GovUKComponents';
import QuestionForm from '../../../../components/Global/Forms/QuestionForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { defaultStaleTime, formModuleKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { getFormModule } from '../queries';
import { updateQuestion, type CreateQuestionInput } from '../mutations';
import { mapFormsError, questionErrorTarget } from '../messages';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';

// Edit an existing question on a draft form module. The shared QuestionForm hydrates from the
// supplied question and owns its editor state, validation and submit/cancel buttons; this page
// loads the module, finds the question, and wires the update mutation (the question code is
// immutable, so the update payload omits it), invalidating the module cache on success.
const FormQuestionEdit = (): React.ReactElement => {
  const { moduleId = '', questionId = '' } = useParams<{ moduleId: string; questionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const [serverError, setServerError] = useState<{ targetName: string; text: string } | null>(null);
  usePageTitle(serverError ? 'Error: Edit question' : 'Edit question');

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate(ROUTES.admin.home);
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery({ queryKey: formModuleKey(moduleId), queryFn: () => getFormModule(moduleId), staleTime: defaultStaleTime, enabled: Boolean(moduleId) });
  const module = moduleQuery.data?.data;
  const sections = module?.sections ?? [];
  const question = module?.questions.find((q) => q.id === questionId);

  // QuestionForm's payload builder always emits `code` because the same form creates questions too.
  // UpdateFormQuestionRequestDto does not declare it, and the API now rejects unmapped members
  // (BLUM-7240), so it has to be dropped here rather than sent and ignored.
  const updateMutation = useMutation({
    mutationFn: ({ code: _code, ...input }: CreateQuestionInput) => updateQuestion(moduleId, questionId, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: formModuleKey(moduleId) });
      navigate(ROUTES.admin.forms.module(moduleId));
    },
    onError: (error) => setServerError({ targetName: questionErrorTarget(error), text: mapFormsError(error) }),
  });

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs([
        { label: module?.name ?? 'Form', link: ROUTES.admin.forms.module(moduleId) },
        { label: 'Edit question', link: ROUTES.admin.forms.editQuestion(moduleId, questionId) },
      ])}
      backLink={{ href: ROUTES.admin.forms.module(moduleId) }}>
      <div className="govuk-!-padding-top-4" data-testid="admin-forms-question-edit">
        {moduleQuery.isLoading ? (
          <PageLoading />
        ) : !module || !question ? (
          <Paragraph data-testid="admin-forms-question-edit-not-found">This question could not be found.</Paragraph>
        ) : (
          <>
            <H1>Edit question</H1>

            {module.status !== 'draft' ? (
              <div className="govuk-inset-text" data-testid="admin-forms-question-edit-locked">
                This form is not a draft, so its questions cannot be changed. To change a published form, start a new draft from the form page.
              </div>
            ) : (
              <QuestionForm
                moduleId={moduleId}
                sections={sections}
                question={question}
                onSubmit={(input) => {
                  setServerError(null);
                  updateMutation.mutate(input);
                }}
                onCancel={() => navigate(ROUTES.admin.forms.module(moduleId))}
                isSubmitting={updateMutation.isPending}
                serverError={serverError}
              />
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionEdit;

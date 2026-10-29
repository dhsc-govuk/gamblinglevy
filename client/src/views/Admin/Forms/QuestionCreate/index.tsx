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
import { createQuestion, type CreateQuestionInput } from '../mutations';
import { mapFormsError, questionErrorTarget } from '../messages';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';

// Add a new question to a draft form module. The shared QuestionForm owns the editor state,
// validation and submit/cancel buttons; this page loads the module (for its sections) and wires
// the create mutation, invalidating the module cache and returning to the module editor on success.
const FormQuestionCreate = (): React.ReactElement => {
  const { moduleId = '' } = useParams<{ moduleId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const [serverError, setServerError] = useState<{ targetName: string; text: string } | null>(null);
  usePageTitle(serverError ? 'Error: Add question' : 'Add question');

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate(ROUTES.admin.home);
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery({ queryKey: formModuleKey(moduleId), queryFn: () => getFormModule(moduleId), staleTime: defaultStaleTime, enabled: Boolean(moduleId) });
  const module = moduleQuery.data?.data;
  const sections = module?.sections ?? [];

  const createMutation = useMutation({
    mutationFn: (input: CreateQuestionInput) => createQuestion(moduleId, input),
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
        { label: 'Add question', link: ROUTES.admin.forms.createQuestion(moduleId) },
      ])}
      backLink={{ href: ROUTES.admin.forms.module(moduleId) }}>
      <div className="govuk-!-padding-top-4" data-testid="admin-forms-question-create">
        {moduleQuery.isLoading ? (
          <PageLoading />
        ) : !module ? (
          <Paragraph data-testid="admin-forms-question-create-not-found">This form could not be found.</Paragraph>
        ) : (
          <>
            <H1>Add question</H1>

            {module.status !== 'draft' ? (
              <div className="govuk-inset-text" data-testid="admin-forms-question-create-locked">
                This form is not a draft, so you cannot add questions to it. To change a published form, start a new draft from the form page.
              </div>
            ) : (
              <QuestionForm
                moduleId={moduleId}
                sections={sections}
                onSubmit={(input) => {
                  setServerError(null);
                  createMutation.mutate(input);
                }}
                onCancel={() => navigate(ROUTES.admin.forms.module(moduleId))}
                isSubmitting={createMutation.isPending}
                serverError={serverError}
              />
            )}
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionCreate;

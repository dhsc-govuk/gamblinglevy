import React, { useEffect } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { H1 } from '../../../../components/GovUKComponents';
import QuestionForm from '../../../../components/Global/Forms/QuestionForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { formModuleKey } from '../../../../helpers/queriesParams';
import { getFormModule } from '../queries';
import { createQuestion, type CreateQuestionInput } from '../mutations';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';

// Add a new question to a draft form module. The shared QuestionForm owns the editor state,
// validation and submit/cancel buttons; this page loads the module (for its sections) and wires
// the create mutation, invalidating the module cache and returning to the module editor on success.
const FormQuestionCreate = (): React.ReactElement => {
  usePageTitle('Add question');
  const { moduleId = '' } = useParams<{ moduleId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  useEffect(() => {
    if (userRole && !canManageForms(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const moduleQuery = useQuery(formModuleKey(moduleId), () => getFormModule(moduleId), { enabled: Boolean(moduleId) });
  const module = moduleQuery.data?.data;
  const sections = module?.sections ?? [];

  const createMutation = useMutation((input: CreateQuestionInput) => createQuestion(moduleId, input), {
    onSuccess: () => {
      queryClient.invalidateQueries(formModuleKey(moduleId));
      navigate(`/admin/forms/${moduleId}`);
    },
  });

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs([
        { label: module?.name ?? 'Form', link: `/admin/forms/${moduleId}` },
        { label: 'Add question', link: `/admin/forms/${moduleId}/questions/create` },
      ])}
      backLink={{ href: `/admin/forms/${moduleId}` }}>
      <div style={{ paddingTop: '20px' }} data-testid="admin-forms-question-create">
        {moduleQuery.isLoading || !module ? (
          <PageLoading />
        ) : (
          <>
            <H1>Add question</H1>

            <QuestionForm
              moduleId={moduleId}
              sections={sections}
              onSubmit={(input) => createMutation.mutate(input)}
              onCancel={() => navigate(`/admin/forms/${moduleId}`)}
              isSubmitting={createMutation.isLoading}
            />
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionCreate;

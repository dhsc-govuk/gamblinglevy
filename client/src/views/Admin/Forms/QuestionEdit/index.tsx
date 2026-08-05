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
import { updateQuestion, type CreateQuestionInput } from '../mutations';
import { canManageForms, formsBuilderBreadcrumbs } from '../navigation';

// Edit an existing question on a draft form module. The shared QuestionForm hydrates from the
// supplied question and owns its editor state, validation and submit/cancel buttons; this page
// loads the module, finds the question, and wires the update mutation (the question code is
// immutable, so the update payload omits it), invalidating the module cache on success.
const FormQuestionEdit = (): React.ReactElement => {
  usePageTitle('Edit question');
  const { moduleId = '', questionId = '' } = useParams<{ moduleId: string; questionId: string }>();
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
  const question = module?.questions.find((q) => q.id === questionId);

  // QuestionForm's payload builder always emits `code` because the same form creates questions too.
  // UpdateFormQuestionRequestDto does not declare it, and the API now rejects unmapped members
  // (BLUM-7240), so it has to be dropped here rather than sent and ignored.
  const updateMutation = useMutation(({ code: _code, ...input }: CreateQuestionInput) => updateQuestion(moduleId, questionId, input), {
    onSuccess: () => {
      queryClient.invalidateQueries(formModuleKey(moduleId));
      navigate(`/admin/forms/${moduleId}`);
    },
  });

  return (
    <GeneralLayout
      breadcrumbs={formsBuilderBreadcrumbs([
        { label: module?.name ?? 'Form', link: `/admin/forms/${moduleId}` },
        { label: 'Edit question', link: `/admin/forms/${moduleId}/questions/${questionId}/edit` },
      ])}
      backLink={{ href: `/admin/forms/${moduleId}` }}>
      <div style={{ paddingTop: '20px' }} data-testid="admin-forms-question-edit">
        {moduleQuery.isLoading || !module || !question ? (
          <PageLoading />
        ) : (
          <>
            <H1>Edit question</H1>

            <QuestionForm
              moduleId={moduleId}
              sections={sections}
              question={question}
              onSubmit={(input) => updateMutation.mutate(input)}
              onCancel={() => navigate(`/admin/forms/${moduleId}`)}
              isSubmitting={updateMutation.isLoading}
            />
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionEdit;

import React, { useEffect, useReducer, useRef } from 'react';
import { PageLoading } from '../../../../components/GovUKComponents';
import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1 } from '../../../../components/GovUKComponents';
import QuestionForm from '../../../../components/Global/FormDesigner/QuestionForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import { activityFormSchemaKey, adminActivityFormSchemaKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { isChoiceType, type QuestionType } from '../../../../types/formSchema';
import { canManageFormDesigner, formDesignerBreadcrumbs } from '../navigation';
import { questionFormReducer, createInitialQuestionFormState, validateQuestion } from '../reducer';
import { getFormSchema } from '../queries';
import { updateQuestion } from '../mutations';
import { mapFormDesignerError } from '../messages';

const FormQuestionsEdit = (): React.ReactElement => {
  usePageTitle('Edit question');
  const { questionId = '' } = useParams<{ questionId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();

  const [state, dispatch] = useReducer(questionFormReducer, createInitialQuestionFormState(''));
  const hydratedRef = useRef(false);

  useEffect(() => {
    if (userRole && !canManageFormDesigner(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const schemaQuery = useQuery(adminActivityFormSchemaKey, getFormSchema, { staleTime: defaultStaleTime });
  const sections = (schemaQuery.data?.data.sections ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);
  const questions = schemaQuery.data?.data.questions ?? [];
  const question = questions.find((q) => q.id === questionId);

  useEffect(() => {
    if (question && !hydratedRef.current) {
      dispatch({
        type: 'HYDRATE',
        state: {
          code: question.code,
          label: question.label,
          questionType: question.questionType as QuestionType,
          sectionId: question.sectionId,
          hint: question.hint ?? '',
          helpText: question.helpText ?? '',
          helpTextSummary: question.helpTextSummary ?? '',
          isRequired: question.isRequired,
          isActive: question.isActive,
          conditionalEnabled: Boolean(question.conditionalQuestionCode),
          conditionalQuestionCode: question.conditionalQuestionCode ?? '',
          conditionalValue: question.conditionalValue ?? '',
          options: question.options
            .slice()
            .sort((a, b) => a.displayOrder - b.displayOrder)
            .map((option) => ({ value: option.value, label: option.label })),
        },
      });
      hydratedRef.current = true;
    }
  }, [question]);

  const availableParents = questions.filter(
    (q) => q.sectionId === state.sectionId && q.isActive && isChoiceType(q.questionType) && q.code !== state.code,
  );

  useErrorSummaryFocus(state.submitAttempts, Object.keys(state.errors).length > 0);

  const updateMutation = useMutation((input: Parameters<typeof updateQuestion>[1]) => updateQuestion(questionId, input), {
    onSuccess: () => {
      queryClient.invalidateQueries(adminActivityFormSchemaKey);
      queryClient.invalidateQueries(activityFormSchemaKey);
      navigate('/admin/activity-record-questions');
    },
    onError: (error) => {
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors: { submit: mapFormDesignerError(error) } });
    },
  });

  const handleSubmit = () => {
    const errors = validateQuestion(state);
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length > 0) {
      return;
    }
    updateMutation.mutate({
      sectionId: state.sectionId,
      label: state.label.trim(),
      questionType: state.questionType,
      isRequired: state.isRequired,
      isActive: state.isActive,
      hint: state.hint.trim() || null,
      helpText: state.helpText.trim() || null,
      helpTextSummary: state.helpTextSummary.trim() || null,
      conditionalQuestionCode: state.conditionalEnabled ? state.conditionalQuestionCode : null,
      conditionalValue: state.conditionalEnabled ? state.conditionalValue : null,
      options: isChoiceType(state.questionType)
        ? state.options.map((option, index) => ({ value: option.value.trim(), label: option.label.trim(), displayOrder: index }))
        : [],
    });
  };

  const errorList = Object.entries(state.errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <GeneralLayout
      breadcrumbs={formDesignerBreadcrumbs([{ label: 'Edit question', link: `/admin/activity-record-questions/${questionId}/edit` }])}
      backLink={{ href: '/admin/activity-record-questions' }}
    >
      <div style={{ paddingTop: '20px' }}>
        {schemaQuery.isLoading || !question ? (
          <PageLoading />
        ) : (
          <>
            {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}
            <H1>Edit question</H1>

            <QuestionForm state={state} dispatch={dispatch} sections={sections} availableParents={availableParents} isEdit />

            <div className="govuk-button-group">
              <GovUKButton onClick={handleSubmit} disabled={updateMutation.isLoading} data-testid="admin-activity-questions-edit-submit">
                Save changes
              </GovUKButton>
              <button
                type="button"
                className="govuk-link"
                style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer' }}
                onClick={() => navigate('/admin/activity-record-questions')}
                data-testid="admin-activity-questions-edit-cancel"
              >
                Cancel
              </button>
            </div>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionsEdit;

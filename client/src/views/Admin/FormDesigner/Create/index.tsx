import React, { useEffect, useReducer } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { GeneralLayout } from '../../../../layouts';
import { ErrorSummary, GovUKButton, H1, NotFound } from '../../../../components/GovUKComponents';
import QuestionForm from '../../../../components/Global/FormDesigner/QuestionForm';
import { useAuthProvider } from '../../../../components/AuthProvider';
import usePageTitle from '../../../../hooks/usePageTitle';
import { activityFormSchemaKey, adminActivityFormSchemaKey, defaultStaleTime } from '../../../../helpers/queriesParams';
import { isNotFoundError } from '../../../../helpers/httpErrors';
import { retryUnlessNotFound } from '../../../../helpers/queryRetry';
import { isChoiceType } from '../../../../types/formSchema';
import { canManageFormDesigner, formDesignerBreadcrumbs } from '../navigation';
import { questionFormReducer, createInitialQuestionFormState, validateQuestion } from '../reducer';
import { getFormSchema } from '../queries';
import { createQuestion } from '../mutations';
import { mapFormDesignerError, formDesignerErrorCode } from '../messages';

const FormQuestionsCreate = (): React.ReactElement => {
  usePageTitle('Add a question');
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { userRole } = useAuthProvider();
  const [searchParams] = useSearchParams();

  const [state, dispatch] = useReducer(questionFormReducer, createInitialQuestionFormState(searchParams.get('sectionId') ?? ''));

  useEffect(() => {
    if (userRole && !canManageFormDesigner(userRole)) {
      navigate('/admin/home');
    }
  }, [userRole, navigate]);

  const schemaQuery = useQuery({ queryKey: adminActivityFormSchemaKey, queryFn: getFormSchema, staleTime: defaultStaleTime, retry: retryUnlessNotFound });
  const sections = (schemaQuery.data?.data.sections ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder);
  const availableParents = (schemaQuery.data?.data.questions ?? []).filter(
    (question) => question.sectionId === state.sectionId && question.isActive && isChoiceType(question.questionType)
  );

  const createMutation = useMutation({
    mutationFn: createQuestion,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: adminActivityFormSchemaKey });
      queryClient.invalidateQueries({ queryKey: activityFormSchemaKey });
      navigate('/admin/activity-record-questions');
    },
    onError: (error) => {
      const errors: Record<string, string> =
        formDesignerErrorCode(error) === 'DUPLICATE_CODE' ? { 'form-question-code': 'A question with this code already exists' } : { submit: mapFormDesignerError(error) };
      dispatch({ type: 'BUMP_ATTEMPTS' });
      dispatch({ type: 'SET_ERRORS', errors });
    },
  });

  const handleSubmit = () => {
    const errors = validateQuestion(state);
    dispatch({ type: 'BUMP_ATTEMPTS' });
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length > 0) {
      return;
    }
    createMutation.mutate({
      sectionId: state.sectionId,
      code: state.code.trim(),
      label: state.label.trim(),
      questionType: state.questionType,
      isRequired: state.isRequired,
      hint: state.hint.trim() || null,
      helpText: state.helpText.trim() || null,
      helpTextSummary: state.helpTextSummary.trim() || null,
      conditionalQuestionCode: state.conditionalEnabled ? state.conditionalQuestionCode : null,
      conditionalValue: state.conditionalEnabled ? state.conditionalValue : null,
      options: isChoiceType(state.questionType) ? state.options.map((option, index) => ({ value: option.value.trim(), label: option.label.trim(), displayOrder: index })) : [],
    });
  };

  const errorList = Object.entries(state.errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  // The API answers a bodiless 404 only when BLUM_9395 has been switched off since this tab loaded.
  if (isNotFoundError(schemaQuery.error)) {
    return <NotFound goBack={navigate} />;
  }

  return (
    <GeneralLayout
      breadcrumbs={formDesignerBreadcrumbs([{ label: 'Add question', link: '/admin/activity-record-questions/create' }])}
      backLink={{ href: '/admin/activity-record-questions' }}>
      <div style={{ paddingTop: '20px' }}>
        <ErrorSummary heading="There is a problem" errors={errorList} focusTrigger={state.submitAttempts} />
        <H1>Add a question</H1>

        <QuestionForm state={state} dispatch={dispatch} sections={sections} availableParents={availableParents} isEdit={false} />

        <div className="govuk-button-group">
          <GovUKButton onClick={handleSubmit} disabled={createMutation.isPending} data-testid="admin-activity-questions-create-submit">
            Save question
          </GovUKButton>
          <button type="button" className="govuk-link" onClick={() => navigate('/admin/activity-record-questions')} data-testid="admin-activity-questions-create-cancel">
            Cancel
          </button>
        </div>
      </div>
    </GeneralLayout>
  );
};

export default FormQuestionsCreate;

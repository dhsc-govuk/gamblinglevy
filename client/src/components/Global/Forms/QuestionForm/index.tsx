import React, { useEffect, useReducer, useState } from 'react';
import { ErrorSummary, GovUKFieldset, GovUKButton } from '../../../GovUKComponents';
import OptionsManager from '../OptionsManager';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import {
  DC_QUESTION_TYPES,
  isDcChoiceType,
  questionFormReducer,
  createInitialQuestionFormState,
  validateQuestion,
  buildQuestionInput,
  type DcQuestionType,
  type DcFormSectionDto,
  type DcFormQuestionDto,
  type CreateQuestionInput,
} from '../../../../views/Admin/Forms';

// Polymorphic editor for a data-collection question. Mirrors the activity Form Designer's QuestionForm
// but drops the conditional-rule builder (data-collection questions have no conditional logic). Owns the
// question reducer internally, hydrating from `question` on edit; calls onSubmit with the built payload.
interface Props {
  moduleId: string;
  sections: DcFormSectionDto[];
  question?: DcFormQuestionDto;
  onSubmit: (input: CreateQuestionInput) => void;
  onCancel: () => void;
  isSubmitting?: boolean;
}

const TYPE_LABELS: Record<DcQuestionType, string> = {
  Text: 'Text (single line)',
  Textarea: 'Text (multiple lines)',
  Number: 'Number',
  Radio: 'Radio buttons (choose one)',
  Checkbox: 'Checkboxes (choose several)',
  Select: 'Dropdown (choose one)',
};

const QuestionForm = ({ sections, question, onSubmit, onCancel, isSubmitting = false }: Props): React.ReactElement => {
  const isEdit = Boolean(question);
  const [state, dispatch] = useReducer(questionFormReducer, createInitialQuestionFormState());
  const [submitAttempts, setSubmitAttempts] = useState(0);

  // Hydrate the editor when editing an existing question (or reset for a fresh add).
  useEffect(() => {
    if (question) {
      dispatch({
        type: 'INIT',
        state: {
          code: question.code,
          label: question.label,
          questionType: question.questionType,
          sectionId: question.sectionId ?? '',
          hint: question.hint ?? '',
          helpText: question.helpText ?? '',
          isRequired: question.isRequired,
          options: question.options
            .slice()
            .sort((a, b) => a.displayOrder - b.displayOrder)
            .map((option) => ({ value: option.value, label: option.label })),
        },
      });
    } else {
      // Adding a new question: default to the first section (if any) rather than "No section".
      dispatch({ type: 'INIT', state: { sectionId: sections[0]?.id ?? '' } });
    }
  }, [question, sections]);

  useErrorSummaryFocus(submitAttempts, Object.keys(state.errors).length > 0);

  const sectionOptions = [
    { value: '', label: 'No section' },
    ...sections
      .slice()
      .sort((a, b) => a.sectionNumber - b.sectionNumber)
      .map((section) => ({ value: section.id, label: section.title })),
  ];

  const typeOptions = DC_QUESTION_TYPES.map((type) => ({ value: type, label: TYPE_LABELS[type] }));

  const handleSubmit = () => {
    const errors = validateQuestion(state, isEdit);
    setSubmitAttempts((attempts) => attempts + 1);
    dispatch({ type: 'SET_ERRORS', errors });
    if (Object.keys(errors).length > 0) {
      return;
    }
    onSubmit(buildQuestionInput(state));
  };

  const errorList = Object.entries(state.errors)
    .filter(([, message]) => Boolean(message))
    .map(([key, message]) => ({ targetName: key, text: message }));

  return (
    <div data-testid="admin-forms-question-form">
      {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Question details</legend>

        <GovUKFieldset.Select
          id="admin-forms-question-section"
          name="admin-forms-question-section"
          label="Section"
          value={state.sectionId}
          options={sectionOptions}
          error={state.errors['admin-forms-question-section']}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'sectionId', value: e.target.value })}
          testId="admin-forms-question-section"
        />

        <GovUKFieldset.Input
          id="admin-forms-question-code"
          name="admin-forms-question-code"
          label="Question code"
          hint="A unique identifier used to store answers, e.g. DELIVERY_METHOD. Capital letters, numbers and underscores only. Cannot be changed later."
          value={state.code}
          error={state.errors['admin-forms-question-code']}
          disabled={isEdit}
          required={!isEdit}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'code', value: e.target.value.toUpperCase() })}
          testId="admin-forms-question-code"
        />

        <GovUKFieldset.Input
          id="admin-forms-question-label"
          name="admin-forms-question-label"
          label="Question label"
          hint="The question shown to the person filling in the form."
          value={state.label}
          error={state.errors['admin-forms-question-label']}
          required
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'label', value: e.target.value })}
          testId="admin-forms-question-label"
        />

        <GovUKFieldset.Select
          id="admin-forms-question-type"
          name="admin-forms-question-type"
          label="Question type"
          value={state.questionType}
          options={typeOptions}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'questionType', value: e.target.value as DcQuestionType })}
          testId="admin-forms-question-type"
        />
      </fieldset>

      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Help and hints (optional)</legend>

        <GovUKFieldset.Input
          id="admin-forms-question-hint"
          name="admin-forms-question-hint"
          label="Hint text"
          hint="Short guidance shown under the question."
          value={state.hint}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'hint', value: e.target.value })}
          testId="admin-forms-question-hint"
        />

        <GovUKFieldset.Textarea
          id="admin-forms-question-help-text"
          name="admin-forms-question-help-text"
          label="Help text"
          hint="Longer guidance shown with the question."
          rows={4}
          value={state.helpText}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'helpText', value: e.target.value })}
          testId="admin-forms-question-help-text"
        />
      </fieldset>

      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Settings</legend>

        <GovUKFieldset.Checkbox
          id="admin-forms-question-required"
          name="admin-forms-question-required"
          label="This question must be answered"
          checked={state.isRequired}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'isRequired', value: e.target.checked })}
          testId="admin-forms-question-required"
        />
      </fieldset>

      {isDcChoiceType(state.questionType) && <OptionsManager state={state} dispatch={dispatch} />}

      <div className="govuk-button-group">
        <GovUKButton onClick={handleSubmit} disabled={isSubmitting} data-testid="admin-forms-question-form-submit">
          {isEdit ? 'Save changes' : 'Save question'}
        </GovUKButton>
        <button
          type="button"
          className="govuk-link"
          style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer' }}
          onClick={onCancel}
          data-testid="admin-forms-question-form-cancel">
          Cancel
        </button>
      </div>
    </div>
  );
};

export default QuestionForm;

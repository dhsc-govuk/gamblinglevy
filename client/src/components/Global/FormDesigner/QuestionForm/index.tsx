import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import OptionsManager from '../OptionsManager';
import ConditionalRuleBuilder from '../ConditionalRuleBuilder';
import { QUESTION_TYPES, isChoiceType, type FormQuestionDto, type FormSectionDto, type QuestionType } from '../../../../types/formSchema';
import type { QuestionFormState, QuestionFormAction } from '../../../../views/Admin/FormDesigner/reducer';

interface Props {
  state: QuestionFormState;
  dispatch: React.Dispatch<QuestionFormAction>;
  sections: FormSectionDto[];
  availableParents: FormQuestionDto[];
  isEdit: boolean;
}

const TYPE_LABELS: Record<QuestionType, string> = {
  Text: 'Text (single line)',
  Textarea: 'Text (multiple lines)',
  Number: 'Number',
  Radio: 'Radio buttons (choose one)',
  Checkbox: 'Checkboxes (choose several)',
  Select: 'Dropdown (choose one)',
};

const QuestionForm = ({ state, dispatch, sections, availableParents, isEdit }: Props): React.ReactElement => {
  const sectionOptions = [
    { value: '', label: 'Select a section' },
    ...sections.map((section) => ({ value: section.id, label: section.title })),
  ];

  const typeOptions = QUESTION_TYPES.map((type) => ({ value: type, label: TYPE_LABELS[type] }));

  return (
    <>
      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Question details</legend>

        <GovUKFieldset.Select
          id="form-question-section"
          name="form-question-section"
          label="Section"
          value={state.sectionId}
          options={sectionOptions}
          error={state.errors['form-question-section']}
          required
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'sectionId', value: e.target.value })}
        />

        <GovUKFieldset.Input
          id="form-question-code"
          name="form-question-code"
          label="Question code"
          hint="A unique identifier used to store answers, e.g. DELIVERY_METHOD. Capital letters, numbers and underscores only. Cannot be changed later."
          value={state.code}
          error={state.errors['form-question-code']}
          disabled={isEdit}
          required
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'code', value: e.target.value.toUpperCase() })}
        />

        <GovUKFieldset.Input
          id="form-question-label"
          name="form-question-label"
          label="Question label"
          hint="The question shown to the person filling in the form."
          value={state.label}
          error={state.errors['form-question-label']}
          required
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'label', value: e.target.value })}
        />

        <GovUKFieldset.Select
          id="form-question-type"
          name="form-question-type"
          label="Question type"
          value={state.questionType}
          options={typeOptions}
          onChange={(e) => dispatch({ type: 'SET_TYPE', value: e.target.value as QuestionType })}
        />
      </fieldset>

      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Help and hints (optional)</legend>

        <GovUKFieldset.Input
          id="form-question-hint"
          name="form-question-hint"
          label="Hint text"
          hint="Short guidance shown under the question."
          value={state.hint}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'hint', value: e.target.value })}
        />

        <GovUKFieldset.Input
          id="form-question-help-summary"
          name="form-question-help-summary"
          label="Expandable help — summary"
          hint="The clickable summary of an expandable help section."
          value={state.helpTextSummary}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'helpTextSummary', value: e.target.value })}
        />

        <GovUKFieldset.Textarea
          id="form-question-help-text"
          name="form-question-help-text"
          label="Expandable help — content"
          rows={4}
          value={state.helpText}
          onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'helpText', value: e.target.value })}
        />
      </fieldset>

      <fieldset className="govuk-fieldset govuk-!-margin-bottom-6">
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Settings</legend>

        <GovUKFieldset.Checkbox
          id="form-question-required"
          name="form-question-required"
          label="This question must be answered"
          checked={state.isRequired}
          onChange={(e) => dispatch({ type: 'SET_FLAG', field: 'isRequired', value: e.target.checked })}
        />

        {isEdit && (
          <GovUKFieldset.Checkbox
            id="form-question-active"
            name="form-question-active"
            label="Show this question on the form"
            hint="Clear this to hide the question without deleting it or its saved answers."
            checked={state.isActive}
            onChange={(e) => dispatch({ type: 'SET_FLAG', field: 'isActive', value: e.target.checked })}
          />
        )}
      </fieldset>

      <ConditionalRuleBuilder state={state} dispatch={dispatch} availableParents={availableParents} />

      {isChoiceType(state.questionType) && <OptionsManager state={state} dispatch={dispatch} />}
    </>
  );
};

export default QuestionForm;

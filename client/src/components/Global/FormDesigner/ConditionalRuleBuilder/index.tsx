import React from 'react';
import { GovUKFieldset, GovUKInsetText } from '../../../GovUKComponents';
import type { FormQuestionDto } from '../../../../types/formSchema';
import type { QuestionFormState, QuestionFormAction } from '../../../../views/Admin/FormDesigner/reducer';

interface Props {
  state: QuestionFormState;
  dispatch: React.Dispatch<QuestionFormAction>;
  availableParents: FormQuestionDto[];
}

// Builds a simple "show this question when another answer matches" rule, written to the flat
// conditionalQuestionCode / conditionalValue fields the renderer and backend both read. Parents are limited to
// choice-type questions in the same section (so a revealed question never sits on an unreached step).
const ConditionalRuleBuilder = ({ state, dispatch, availableParents }: Props): React.ReactElement => {
  const parent = availableParents.find((question) => question.code === state.conditionalQuestionCode);

  const parentOptions = [
    { value: '', label: 'Select a question' },
    ...availableParents.map((question) => ({ value: question.code, label: `${question.label} (${question.code})` })),
  ];

  const valueOptions = [{ value: '', label: 'Select an answer' }, ...(parent?.options ?? []).map((option) => ({ value: option.value, label: option.label }))];

  return (
    <fieldset className="govuk-form-group">
      <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Conditional display</legend>

      <GovUKFieldset.Checkbox
        id="form-question-conditional-enabled"
        name="form-question-conditional-enabled"
        label="Only show this question based on another answer"
        checked={state.conditionalEnabled}
        disabled={availableParents.length === 0}
        hint={availableParents.length === 0 ? 'Add a radio, checkbox or select question to this section first.' : undefined}
        onChange={(e) => dispatch({ type: 'TOGGLE_CONDITIONAL', enabled: e.target.checked })}
      />

      {state.conditionalEnabled && (
        <>
          <GovUKFieldset.Select
            id="form-question-conditional-parent"
            name="form-question-conditional-parent"
            label="Question this depends on"
            value={state.conditionalQuestionCode}
            options={parentOptions}
            error={state.errors['form-question-conditional-parent']}
            onChange={(e) => {
              dispatch({ type: 'SET_FIELD', field: 'conditionalQuestionCode', value: e.target.value });
              dispatch({ type: 'SET_FIELD', field: 'conditionalValue', value: '' });
            }}
          />
          <GovUKFieldset.Select
            id="form-question-conditional-value"
            name="form-question-conditional-value"
            label="Show this question when the answer is"
            value={state.conditionalValue}
            options={valueOptions}
            error={state.errors['form-question-conditional-value']}
            disabled={!state.conditionalQuestionCode}
            onChange={(e) => dispatch({ type: 'SET_FIELD', field: 'conditionalValue', value: e.target.value })}
          />
          {parent && state.conditionalValue && (
            <GovUKInsetText>
              Shows only when “{parent.label}” is “{parent.options.find((option) => option.value === state.conditionalValue)?.label ?? state.conditionalValue}”.
            </GovUKInsetText>
          )}
        </>
      )}
    </fieldset>
  );
};

export default ConditionalRuleBuilder;

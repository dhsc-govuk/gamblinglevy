import React from 'react';
import { GovUKFieldset, GovUKButton } from '../../../GovUKComponents';
import ReorderControls from '../ReorderControls';
import type { QuestionFormState, QuestionFormAction } from '../../../../views/Admin/FormDesigner/reducer';

interface Props {
  state: QuestionFormState;
  dispatch: React.Dispatch<QuestionFormAction>;
}

// Add / edit / reorder / remove the options for a choice-type question (radio, checkbox, select).
const OptionsManager = ({ state, dispatch }: Props): React.ReactElement => (
  <fieldset className={`govuk-form-group ${state.errors['form-question-options'] ? 'govuk-form-group--error' : ''}`}>
    <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Answer options</legend>

    {state.errors['form-question-options'] && (
      <p className="govuk-error-message" data-testid="form-question-options-error">
        <span className="govuk-visually-hidden">Error:</span> {state.errors['form-question-options']}
      </p>
    )}

    {state.options.map((option, index) => (
      <div key={index} className="govuk-!-margin-bottom-4 govuk-!-padding-3" style={{ border: '1px solid #b1b4b6' }} data-testid={`form-question-option-${index}`}>
        <div className="govuk-!-margin-bottom-2">
          <ReorderControls
            label={`option ${index + 1}`}
            isFirst={index === 0}
            isLast={index === state.options.length - 1}
            onMoveUp={() => dispatch({ type: 'MOVE_OPTION', index, direction: 'up' })}
            onMoveDown={() => dispatch({ type: 'MOVE_OPTION', index, direction: 'down' })}
            testId={`form-question-option-${index}`}
          />
          {state.options.length > 1 && (
            <button type="button" className="govuk-link" onClick={() => dispatch({ type: 'REMOVE_OPTION', index })} data-testid={`form-question-option-${index}-remove`}>
              Remove<span className="govuk-visually-hidden"> option {index + 1}</span>
            </button>
          )}
        </div>
        <GovUKFieldset.Input
          id={`form-question-option-${index}-value`}
          name={`form-question-option-${index}-value`}
          label="Value (stored)"
          value={option.value}
          onChange={(e) => dispatch({ type: 'UPDATE_OPTION', index, field: 'value', value: e.target.value })}
        />
        <GovUKFieldset.Input
          id={`form-question-option-${index}-label`}
          name={`form-question-option-${index}-label`}
          label="Label (shown to the user)"
          value={option.label}
          onChange={(e) => dispatch({ type: 'UPDATE_OPTION', index, field: 'label', value: e.target.value })}
        />
      </div>
    ))}

    <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => dispatch({ type: 'ADD_OPTION' })} data-testid="form-question-options-add">
      Add option
    </GovUKButton>
  </fieldset>
);

export default OptionsManager;

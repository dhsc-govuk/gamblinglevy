import React from 'react';
import { GovUKFieldset, GovUKButton } from '../../../GovUKComponents';
import ReorderControls from '../../FormDesigner/ReorderControls';
import type { QuestionFormState, QuestionFormAction } from '../../../../views/Admin/Forms';

interface Props {
  state: QuestionFormState;
  dispatch: React.Dispatch<QuestionFormAction>;
}

// Add / edit / reorder / remove the answer options for a choice-type question (radio, checkbox, select).
// Driven by the data-collection question reducer; reuses the Form Designer's keyboard-accessible
// ReorderControls (no drag-drop, per WCAG 2.2 AA).
const OptionsManager = ({ state, dispatch }: Props): React.ReactElement => (
  <fieldset className={`govuk-form-group ${state.errors['admin-forms-question-options'] ? 'govuk-form-group--error' : ''}`} data-testid="admin-forms-options">
    <legend className="govuk-fieldset__legend govuk-fieldset__legend--m">Answer options</legend>

    {state.errors['admin-forms-question-options'] && (
      <p className="govuk-error-message" data-testid="admin-forms-options-error">
        <span className="govuk-visually-hidden">Error:</span> {state.errors['admin-forms-question-options']}
      </p>
    )}

    {state.options.map((option, index) => (
      <div key={index} className="govuk-!-margin-bottom-4 govuk-!-padding-3" style={{ border: '1px solid #b1b4b6' }} data-testid={`admin-forms-options-option-${index}`}>
        <div className="govuk-!-margin-bottom-2">
          <ReorderControls
            label={`option ${index + 1}`}
            isFirst={index === 0}
            isLast={index === state.options.length - 1}
            onMoveUp={() => dispatch({ type: 'REORDER_OPTIONS', index, direction: 'up' })}
            onMoveDown={() => dispatch({ type: 'REORDER_OPTIONS', index, direction: 'down' })}
            testId={`admin-forms-options-option-${index}`}
          />
          {state.options.length > 1 && (
            <button
              type="button"
              className="govuk-link"
              style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer' }}
              onClick={() => dispatch({ type: 'REMOVE_OPTION', index })}
              data-testid={`admin-forms-options-option-${index}-remove`}>
              Remove<span className="govuk-visually-hidden"> option {index + 1}</span>
            </button>
          )}
        </div>
        <GovUKFieldset.Input
          id={`admin-forms-options-option-${index}-value`}
          name={`admin-forms-options-option-${index}-value`}
          label="Value (stored)"
          value={option.value}
          onChange={(e) => dispatch({ type: 'UPDATE_OPTION', index, field: 'value', value: e.target.value })}
          testId={`admin-forms-options-option-${index}-value`}
        />
        <GovUKFieldset.Input
          id={`admin-forms-options-option-${index}-label`}
          name={`admin-forms-options-option-${index}-label`}
          label="Label (shown to the user)"
          value={option.label}
          onChange={(e) => dispatch({ type: 'UPDATE_OPTION', index, field: 'label', value: e.target.value })}
          testId={`admin-forms-options-option-${index}-label`}
        />
      </div>
    ))}

    <GovUKButton className="govuk-button govuk-button--secondary" onClick={() => dispatch({ type: 'ADD_OPTION' })} data-testid="admin-forms-options-add">
      Add option
    </GovUKButton>
  </fieldset>
);

export default OptionsManager;

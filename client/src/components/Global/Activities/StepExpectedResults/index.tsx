import React from 'react';
import { GovUKCheckbox, GovUKFieldset } from '../../../GovUKComponents';
import { keyResultsQuestionLabel } from '../../../../views/Activities/display';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import type { TaxonomyKeyResultDto } from '../../../../views/Activities/queries';
import './styles.css';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  // The full taxonomy key-result list; filtered to the current type here.
  keyResults: TaxonomyKeyResultDto[];
  isNeedsAssessment?: boolean;
}

// Sentinel value for the "Other" checkbox — double-underscored so it can never collide with a protocol
// key-result code. slugifyTestId strips it to "other" for the option's data-testid.
const OTHER_VALUE = '__other';

// "Key results" step — standardised per-type checkbox options from the Question Protocol, with an "Other"
// option revealing the free-text repeater for custom key results. Types with no standard options fall back
// to the free-text-only repeater. Minimums (1, or 2 for needs assessment / free-text-only types) are enforced
// at validation — the Question Protocol (3 July) carries no on-screen hint for them.
const StepExpectedResults = ({ state, dispatch, keyResults, isNeedsAssessment = false }: Props): React.ReactElement => {
  const options = keyResults.filter((keyResult) => keyResult.taskId === state.taskId);
  const results = options.length === 0 ? [...state.expectedResults, '', ''].slice(0, Math.max(2, state.expectedResults.length)) : [state.expectedResults[0] ?? ''];

  const setResults = (values: string[]) => dispatch({ type: 'SET_EXPECTED_RESULTS', values });
  const update = (index: number, value: string) => setResults(results.map((result, i) => (i === index ? value : result)));

  const repeater = (
    <>
      {results.map((result, index) => (
        <div className="activity-key-result-row" key={index}>
          <GovUKFieldset.Input
            id={`activity-expected-result-${index}`}
            name={`expectedResult-${index}`}
            label={options.length === 0 ? `Key result ${index + 1}` : 'What other result will you use?'}
            labelSize="s"
            value={result}
            error={state.errors[`activity-expected-result-${index}`]}
            onChange={(event) => update(index, event.target.value)}
            testId={`activities-create-expected-result-${index}`}
          />
        </div>
      ))}
    </>
  );

  // Types with no standard protocol options (e.g. "Other type of ..." types) keep the free-text-only step.
  if (options.length === 0) {
    return (
      <>
        <h2 className="govuk-heading-m">{keyResultsQuestionLabel(false)}</h2>
        <div className="govuk-hint">Enter 2</div>

        {state.errors['activity-expected-results'] && (
          <p className="govuk-error-message" data-testid="activities-create-expected-results-error">
            <span className="govuk-visually-hidden">Error:</span> {state.errors['activity-expected-results']}
          </p>
        )}

        {repeater}
      </>
    );
  }

  const checkedValues = [...state.keyResultCodes, ...(state.otherExpectedResultSelected ? [OTHER_VALUE] : [])];

  const handleChange = (values: (string | number)[]) => {
    const selected = values.map(String);
    const nextOther = selected.includes(OTHER_VALUE);
    dispatch({ type: 'SET_KEY_RESULT_CODES', values: selected.filter((value) => value !== OTHER_VALUE) });
    if (nextOther !== state.otherExpectedResultSelected) {
      dispatch({ type: 'SET_OTHER_EXPECTED_RESULT_SELECTED', value: nextOther });
      if (!nextOther) {
        setResults(['']);
      }
    }
  };

  return (
    <GovUKCheckbox
      id="activity-expected-results"
      name="activity-expected-results"
      legend={keyResultsQuestionLabel(true)}
      legendSize="m"
      hint={isNeedsAssessment ? 'Select at least 2' : undefined}
      size="small"
      options={[...options.map((keyResult) => ({ value: keyResult.code, label: keyResult.label })), { value: OTHER_VALUE, label: 'Other', conditionalContent: repeater }]}
      value={checkedValues}
      error={state.errors['activity-expected-results']}
      onChange={handleChange}
      testId="activities-create-expected-results"
    />
  );
};

export default StepExpectedResults;

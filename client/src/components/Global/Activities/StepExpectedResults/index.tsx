import React from 'react';
import { GovUKCheckbox, GovUKFieldset } from '../../../GovUKComponents';
import { EXPECTED_RESULTS_ERROR_KEY, EXPECTED_RESULTS_QUESTION, OTHER_MEASURE_LABEL, expectedResultsHint, measureRowLabel } from '../../../../views/Activities/display';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import type { TaxonomyKeyResultDto } from '../../../../views/Activities/queries';
import './styles.css';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  // The full taxonomy key-result list; filtered to the current type here.
  keyResults: TaxonomyKeyResultDto[];
}

// Sentinel value for the "Other" checkbox — double-underscored so it can never collide with a protocol
// key-result code. slugifyTestId strips it to "other" for the option's data-testid.
const OTHER_VALUE = '__other';

// "Expected results" step — standardised per-type checkbox options from the Question Protocol, with an "Other"
// option revealing the free-text repeater for custom measures. Types with no standard options fall back to the
// free-text-only repeater. Every screen needs at least one measure (13 August protocol, BLUM-7620); the hint says
// so, and only its verb changes between the two screens.
const StepExpectedResults = ({ state, dispatch, keyResults }: Props): React.ReactElement => {
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
            label={options.length === 0 ? measureRowLabel(index) : OTHER_MEASURE_LABEL}
            labelSize="s"
            value={result}
            error={state.errors[`activity-expected-result-${index}`]}
            // The free-text screen's hint and error describe the repeater, not any one row, so the first input
            // names them — otherwise a screen reader never hears either, and they are also where the error
            // summary lands.
            describedBy={
              options.length === 0 && index === 0
                ? [`${EXPECTED_RESULTS_ERROR_KEY}-hint`, state.errors[EXPECTED_RESULTS_ERROR_KEY] ? `${EXPECTED_RESULTS_ERROR_KEY}-error` : ''].filter(Boolean).join(' ')
                : undefined
            }
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
        <h2 className="govuk-heading-m">{EXPECTED_RESULTS_QUESTION}</h2>
        {/* The hint and the screen-level error describe the repeater as a whole rather than any one input, so
            they carry ids for the error summary to reach (expectedResultsErrorTargetId points at the first input,
            which is the nearest focusable thing to them). */}
        <div className="govuk-hint" id={`${EXPECTED_RESULTS_ERROR_KEY}-hint`}>
          {expectedResultsHint(false)}
        </div>

        {state.errors[EXPECTED_RESULTS_ERROR_KEY] && (
          <p className="govuk-error-message" id={`${EXPECTED_RESULTS_ERROR_KEY}-error`} data-testid="activities-create-expected-results-error">
            <span className="govuk-visually-hidden">Error:</span> {state.errors[EXPECTED_RESULTS_ERROR_KEY]}
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
      legend={EXPECTED_RESULTS_QUESTION}
      legendSize="m"
      hint={expectedResultsHint(true)}
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

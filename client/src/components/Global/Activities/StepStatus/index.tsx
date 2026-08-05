import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import { activityCopy } from '../../../../views/Activities/display';
import './styles.css';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  isPeople: boolean;
}

// "Activity status" step — the user-chosen lifecycle status. Choosing "Active" reveals a start month/year
// (Figma 138-6733 / 138-7454).
const StepStatus = ({ state, dispatch, isPeople }: Props): React.ReactElement => {
  const copy = activityCopy(isPeople);
  const startDateError = state.errors['activity-start-month'] ?? state.errors['activity-start-year'] ?? state.errors['activity-start-date'];
  const updateStartMonth = (value: string) => {
    if (/^\d{0,2}$/.test(value)) {
      dispatch({ type: 'SET_START_MONTH', value });
    }
  };
  const updateStartYear = (value: string) => {
    if (/^\d{0,4}$/.test(value)) {
      dispatch({ type: 'SET_START_YEAR', value });
    }
  };

  const startDate = (
    <div className="govuk-form-group activity-status-step__start-date" id="activity-start-date" tabIndex={-1}>
      <h2 className="govuk-heading-m activity-status-step__date-heading">{copy.startQuestion}</h2>
      <div className="govuk-hint">For example, 3 2026</div>
      {startDateError && (
        <p className="govuk-error-message">
          <span className="govuk-visually-hidden">Error:</span> {startDateError}
        </p>
      )}
      <div className="govuk-date-input">
        <div className="govuk-date-input__item">
          <GovUKFieldset.Input
            id="activity-start-month"
            name="startMonth"
            label="Month"
            labelSize="s"
            width="2"
            value={state.startMonth}
            inputMode="numeric"
            pattern="[0-9]*"
            onChange={(event) => updateStartMonth(event.target.value)}
            testId="activities-create-start-month"
          />
        </div>
        <div className="govuk-date-input__item">
          <GovUKFieldset.Input
            id="activity-start-year"
            name="startYear"
            label="Year"
            labelSize="s"
            width="4"
            value={state.startYear}
            inputMode="numeric"
            pattern="[0-9]*"
            onChange={(event) => updateStartYear(event.target.value)}
            testId="activities-create-start-year"
          />
        </div>
      </div>
    </div>
  );

  return (
    <div className="activity-status-step">
      <GovUKFieldset.RadioGroup
        name="activity-status"
        legend={copy.statusQuestion}
        legendSize="m"
        size="small"
        options={[
          { value: 'planned', text: 'Planned', hint: copy.statusHints.planned },
          { value: 'active', text: 'Active', hint: copy.statusHints.active, conditionalContent: startDate },
          { value: 'closed', text: 'Closed', hint: copy.statusHints.closed },
          { value: 'finished', text: 'Finished', hint: copy.statusHints.finished },
        ]}
        value={state.status}
        error={state.errors['activity-status']}
        onChange={(value) => dispatch({ type: 'SET_STATUS', value })}
        testId="activities-create-status"
      />
    </div>
  );
};

export default StepStatus;

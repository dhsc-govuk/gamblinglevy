import React from 'react';
import { GovUKDateField, GovUKFieldset } from '../../../GovUKComponents';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import { activityCopy, startDateErrorParts } from '../../../../views/Activities/display';
import './styles.css';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  isPeople: boolean;
  categoryName?: string | null;
}

// "Activity status" step — the user-chosen lifecycle status. Choosing "Active" reveals a start month/year
// (Figma 138-6733 / 138-7454), and "Closed" asks why (Figma 3201:339943, BLUM-7673). A category whose status is fixed
// (forcedActivityStatus) never reaches this step.
const StepStatus = ({ state, dispatch, isPeople, categoryName }: Props): React.ReactElement => {
  const copy = activityCopy(isPeople, categoryName);
  const startDateError = state.errors['activity-start-month'] ?? state.errors['activity-start-year'] ?? state.errors['activity-start-date'];

  // Month and Year are two inputs answering one question: the GDS date input, with a month-and-year shape. The
  // wrapper keeps the testid the step has always had on the question, and is the stylesheet's hook.
  const startDate = (
    <div className="activity-status-step__start-date" data-testid="activities-create-start-date">
      <GovUKDateField
        id="activity-start"
        testId="activities-create-start"
        legend={copy.startQuestion}
        legendSize="m"
        hint="For example, 3 2026"
        fields={['month', 'year']}
        value={{ month: state.startMonth, year: state.startYear }}
        error={startDateError}
        errorParts={startDateErrorParts(state.errors)}
        onChange={(part, value) => {
          if (part === 'month') dispatch({ type: 'SET_START_MONTH', value });
          if (part === 'year') dispatch({ type: 'SET_START_YEAR', value });
        }}
      />
    </div>
  );

  // A single question, so a plain labelled input rather than a fieldset. The wrapper exists for the stylesheet: the
  // wizard's 630px form-group column must give way to the reveal's own width, as the start-date group does.
  const closureReason = (
    <div className="activity-status-step__closure-reason">
      <GovUKFieldset.Input
        id="activity-closure-reason"
        name="closureReason"
        label={copy.closureQuestion}
        labelSize="m"
        hint={copy.closureHint}
        width="20"
        value={state.closureReason}
        error={state.errors['activity-closure-reason']}
        onChange={(event) => dispatch({ type: 'SET_CLOSURE_REASON', value: event.target.value })}
        testId="activities-create-closure-reason"
      />
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
          { value: 'closed', text: 'Closed', hint: copy.statusHints.closed, conditionalContent: closureReason },
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

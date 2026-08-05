import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import type { TaxonomyTaskDto } from '../../../../views/Activities/queries';
import { activityCopy, activityTypeDisplayName, isOtherType, TYPE_OPTION_HINTS } from '../../../../views/Activities/display';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  tasks: TaxonomyTaskDto[];
  isPeople: boolean;
  categoryName?: string;
}

// "Activity type" step — choose the type of work (a second-level taxonomy option). The "Other ..." option
// reveals a free-text name stored as the custom task text (Figma 138-6469 / 138-7733).
const StepType = ({ state, dispatch, tasks, isPeople, categoryName }: Props): React.ReactElement => {
  const copy = activityCopy(isPeople, categoryName);

  const otherInput = (
    <GovUKFieldset.Input
      id="activity-other-task"
      name="customTaskText"
      label={copy.otherLabel}
      labelSize="m"
      value={state.customTaskText}
      error={state.errors['activity-other-task']}
      onChange={(event) => dispatch({ type: 'SET_CUSTOM_TASK', value: event.target.value })}
      testId="activities-create-other-task"
    />
  );

  return (
    <GovUKFieldset.RadioGroup
      name="activity-type"
      legend={copy.typeQuestion}
      legendSize="m"
      size="small"
      hint={copy.typeHint}
      options={tasks.map((task) => ({
        value: task.id,
        text: activityTypeDisplayName(task.name),
        hint: TYPE_OPTION_HINTS[task.name],
        conditionalContent: isOtherType(task.name) ? otherInput : undefined,
      }))}
      value={state.taskId}
      error={state.errors['activity-type']}
      onChange={(value) => {
        const task = tasks.find((item) => item.id === value);
        dispatch({ type: 'SET_TASK', value, isOther: !!task && isOtherType(task.name) });
      }}
      testId="activities-create-type"
    />
  );
};

export default StepType;

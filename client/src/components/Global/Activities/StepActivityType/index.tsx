import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import StepType from '../StepType';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import type { TaxonomyCategoryDto, TaxonomyTaskDto } from '../../../../views/Activities/queries';
import { CATEGORY_OPTION_HINTS, CATEGORY_QUESTION, isPeopleCategory } from '../../../../views/Activities/display';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  categories: TaxonomyCategoryDto[];
  tasks: TaxonomyTaskDto[];
}

// "Activity type" step — the category question (Figma 138-6395) with that category's types revealed inline
// underneath it, rendered by the unchanged StepType (Figma 138-6469 / 138-7733). BLUM-7649 merged what were two
// consecutive pages, so a user can read a category's options before committing to it.
//
// Only the selected category's type group is mounted: GovUKFieldset.RadioGroup keeps every conditionalContent in
// the DOM and merely CSS-hides the unselected panels, so rendering all six would put six copies of the
// `activity-type` group and its `activity-other-task` input on the page — duplicate ids and name attributes, and
// error-summary links resolving to a hidden element. Gating here also gives AC4 (nothing expanded by default).
// A category offering exactly one type is not a question — the host auto-selects it (see ActivityWizard), so it
// has no reveal at all. Joint partnership is the only such category today; the explanatory copy BLUM-7860 put in
// its reveal was removed from the design (BLUM-8090), leaving only the option hint on its radio.
const revealFor = (category: TaxonomyCategoryDto, categoryTasks: TaxonomyTaskDto[], state: ActivityFormState, dispatch: React.Dispatch<ActivityFormAction>): React.ReactNode => {
  if (categoryTasks.length === 1) {
    return undefined;
  }

  return <StepType state={state} dispatch={dispatch} tasks={categoryTasks} isPeople={isPeopleCategory(category.name)} categoryName={category.name} />;
};

const StepActivityType = ({ state, dispatch, categories, tasks }: Props): React.ReactElement => (
  <GovUKFieldset.RadioGroup
    name="activity-category"
    legend={CATEGORY_QUESTION}
    legendSize="m"
    size="small"
    options={categories.map((category) => {
      const categoryTasks = tasks.filter((task) => task.categoryId === category.id);
      const reveal = category.id === state.categoryId ? revealFor(category, categoryTasks, state, dispatch) : undefined;

      return {
        value: category.id,
        text: category.name,
        hint: CATEGORY_OPTION_HINTS[category.name],
        // The wrapper exists only to scope the reveal's layout rule to this group: the wizard's stylesheet is
        // shared with the schema-driven sections, whose own conditional reveals can hold several form groups.
        conditionalContent: reveal ? <div className="activity-wizard__type-reveal">{reveal}</div> : undefined,
      };
    })}
    value={state.categoryId}
    error={state.errors['activity-category']}
    onChange={(value) => dispatch({ type: 'SET_CATEGORY', value })}
    testId="activities-create-category"
  />
);

export default StepActivityType;

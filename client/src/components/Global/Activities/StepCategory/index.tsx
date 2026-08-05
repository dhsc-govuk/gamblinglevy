import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { ActivityFormState, ActivityFormAction } from '../../../../views/Activities/reducer';
import type { TaxonomyCategoryDto } from '../../../../views/Activities/queries';
import { CATEGORY_QUESTION } from '../../../../views/Activities/display';

interface Props {
  state: ActivityFormState;
  dispatch: React.Dispatch<ActivityFormAction>;
  categories: TaxonomyCategoryDto[];
}

// "Activity category" step — choose one of the fixed activity categories (Figma 138-6395).
const StepCategory = ({ state, dispatch, categories }: Props): React.ReactElement => (
  <GovUKFieldset.RadioGroup
    name="activity-category"
    legend={CATEGORY_QUESTION}
    legendSize="m"
    size="small"
    options={categories.map((category) => ({ value: category.id, text: category.name }))}
    value={state.categoryId}
    error={state.errors['activity-category']}
    onChange={(value) => dispatch({ type: 'SET_CATEGORY', value })}
    testId="activities-create-category"
  />
);

export default StepCategory;

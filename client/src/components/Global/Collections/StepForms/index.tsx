import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';
import type { CollectionFormOptionDto } from '../../../../views/Admin/Collections/queries';
import { formatGovUkDate } from '../../../../views/Admin/Collections/display';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
  formOptions: CollectionFormOptionDto[];
}

// "Forms" step of the add-collection wizard — the admin chooses which form modules make up the data
// collection. Each option surfaces when the module was last changed so the admin can confirm they are
// including the current version.
const StepForms = ({ state, dispatch, formOptions }: Props): React.ReactElement => (
  <GovUKFieldset.CheckboxGroup
    name="admin-collections-create-forms"
    legend="Which forms do you want to include in this data collection?"
    legendSize="m"
    options={formOptions.map((option) => ({
      value: option.moduleId,
      text: option.name,
      hint: option.lastChangedAt ? `Last changed on ${formatGovUkDate(option.lastChangedAt)}` : undefined,
    }))}
    value={state.formModuleIds}
    error={state.errors['forms']}
    size="small"
    onChange={(value) => dispatch({ type: 'TOGGLE_FORM', id: value })}
    testId="admin-collections-create-forms"
  />
);

export default StepForms;

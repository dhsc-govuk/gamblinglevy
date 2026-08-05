import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';
import { financialYearOptions, quarterOptions } from '../../../../views/Admin/Collections/display';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
}

// "Add data collection" — details step (Figma): name, optional purpose and the collection type.
const StepDetails = ({ state, dispatch }: Props): React.ReactElement => {
  const financialYears = [{ value: '', label: 'Select a financial year...' }, ...financialYearOptions()];
  const quarters = [{ value: '', label: 'Select a quarter...' }, ...quarterOptions()];
  const renderFinancialYearSelect = (id: string) => (
    <GovUKFieldset.Select
      id={id}
      name="financialYear"
      label="Financial year"
      value={state.financialYear}
      options={financialYears}
      error={state.errors['financialYear']}
      onChange={(event) => dispatch({ type: 'SET_FIELD', field: 'financialYear', value: event.target.value })}
      testId={id}
    />
  );

  return (
    <>
      <GovUKFieldset.Input
        id="admin-collections-create-name"
        name="name"
        label="What is the name of this data collection?"
        hint="The name must be unique, but organisations will not see it"
        value={state.name}
        error={state.errors['name']}
        onChange={(event) => dispatch({ type: 'SET_FIELD', field: 'name', value: event.target.value })}
        testId="admin-collections-create-name"
      />

      <GovUKFieldset.Textarea
        id="admin-collections-create-purpose"
        name="purpose"
        label="What is the purpose of this data collection? (optional)"
        hint="Organisations will not see this"
        value={state.purpose}
        error={state.errors['purpose']}
        onChange={(event) => dispatch({ type: 'SET_FIELD', field: 'purpose', value: event.target.value })}
        testId="admin-collections-create-purpose"
      />

      <GovUKFieldset.RadioGroup
        name="admin-collections-create-type"
        legend="What type of data collection is it?"
        legendSize="m"
        value={state.type}
        error={state.errors['type']}
        size="small"
        options={[
          {
            value: 'quarterly_report',
            text: 'Quarterly report',
            conditionalContent:
              state.type === 'quarterly_report' ? (
                <div className="collection-wizard__type-fields">
                  {renderFinancialYearSelect('admin-collections-create-quarterly-financial-year')}
                  <GovUKFieldset.Select
                    id="admin-collections-create-quarter"
                    name="quarter"
                    label="Quarter"
                    value={state.quarter}
                    options={quarters}
                    error={state.errors['quarter']}
                    onChange={(event) => dispatch({ type: 'SET_FIELD', field: 'quarter', value: event.target.value })}
                    testId="admin-collections-create-quarter"
                  />
                </div>
              ) : undefined,
          },
          {
            value: 'maturity_assessment',
            text: 'Maturity assessment',
            conditionalContent:
              state.type === 'maturity_assessment' ? (
                <div className="collection-wizard__type-fields">{renderFinancialYearSelect('admin-collections-create-maturity-financial-year')}</div>
              ) : undefined,
          },
        ]}
        onChange={(value) => dispatch({ type: 'SET_TYPE', value: value as CollectionFormState['type'] })}
        testId="admin-collections-create-type"
      />
    </>
  );
};

export default StepDetails;

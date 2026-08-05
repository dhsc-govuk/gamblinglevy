import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';
import CollectionLaSelector from '../CollectionLaSelector';
import type { ReportingLaDto } from '../../../../views/Admin/Collections/queries';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
  reportingLas: ReportingLaDto[];
  isLoading?: boolean;
}

// "Reporting local authorities" step of the add-collection wizard (Figma): Yes/No plus LA selector for No.
const StepReportingLas = ({ state, dispatch, reportingLas, isLoading = false }: Props): React.ReactElement => {
  return (
    <GovUKFieldset.RadioGroup
      name="admin-collections-create-all-las"
      legend="Are you requesting data from all organisations?"
      legendSize="s"
      options={[
        { value: 'yes', text: 'Yes' },
        {
          value: 'no',
          text: 'No',
          conditionalContent:
            state.isSubmittedByAllLas === 'no' ? (
              <div className="collection-wizard__reporting-las-fields">
                {isLoading ? (
                  <p className="govuk-body">Loading organisations...</p>
                ) : (
                  <CollectionLaSelector
                    las={reportingLas}
                    selectedIds={state.selectedLaIds}
                    onToggle={(id) => dispatch({ type: 'TOGGLE_LA', id })}
                    onToggleAll={(checked, ids) => {
                      if (checked) {
                        dispatch({ type: 'ADD_LAS', ids });
                      } else {
                        dispatch({ type: 'REMOVE_LAS', ids });
                      }
                    }}
                    error={state.errors['selected-las']}
                    testId="admin-collections-create-reporting-las"
                  />
                )}
              </div>
            ) : undefined,
        },
      ]}
      value={state.isSubmittedByAllLas}
      error={state.errors['all-las']}
      size="small"
      onChange={(value) => dispatch({ type: 'SET_ALL_LAS', value: value as 'yes' | 'no' })}
      testId="admin-collections-create-all-las"
    />
  );
};

export default StepReportingLas;

import React from 'react';
import { GovUKDateField, GovUKFieldset, type DatePart } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction } from '../../../../views/Admin/Collections/reducer';
import { reportingPeriod } from '../../../../views/Admin/Collections/display';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
}

// The reducer field each part of the two dates writes to.
const OPENS_FIELDS = { day: 'opensDay', month: 'opensMonth', year: 'opensYear' } as const;
const DEADLINE_FIELDS = { day: 'deadlineDay', month: 'deadlineMonth', year: 'deadlineYear' } as const;

// "Add data collection" — window step (Figma): whether the data collection window matches the reporting period.
const StepWindow = ({ state, dispatch }: Props): React.ReactElement => {
  const period = reportingPeriod(state.financialYear, state.quarter);

  const dateError = (key: 'open-date' | 'deadline-date') =>
    state.errors[key] ??
    state.errors[`${key}-day`] ??
    state.errors[`${key}-month`] ??
    state.errors[`${key}-year`] ??
    state.errors[`${key}-invalid`] ??
    state.errors[`${key}-future`] ??
    (key === 'deadline-date' ? state.errors['deadline-date-order'] : undefined);

  // Which sub-fields to outline red: a single-part error highlights just that part; anything that
  // isn't tied to one specific part (missing every part, an unreal date, the future/ordering checks)
  // highlights all three, same as a plain GDS "enter a date" group error would.
  const highlightedParts = (key: 'open-date' | 'deadline-date'): DatePart[] => {
    if (state.errors[`${key}-day`]) {
      return ['day'];
    }
    if (state.errors[`${key}-month`]) {
      return ['month'];
    }
    if (state.errors[`${key}-year`]) {
      return ['year'];
    }
    return ['day', 'month', 'year'];
  };

  return (
    <GovUKFieldset.RadioGroup
      name="admin-collections-create-window-same"
      legend="Is the data collection window the same as the reporting period?"
      legendSize="m"
      hint={
        <>
          <p className="govuk-body collection-wizard__window-hint-paragraph">
            The data collection window is the period when organisations can submit information. It can be the same as the reporting period, or it can open and close on different
            dates.
          </p>
          <p className="govuk-body collection-wizard__window-hint-paragraph">
            The reporting period for this data collection is from {period.start} to {period.end}.
          </p>
        </>
      }
      value={state.windowSameAsReportingPeriod}
      error={state.errors['window-same']}
      size="small"
      options={[
        { value: 'yes', text: 'Yes' },
        {
          value: 'no',
          text: 'No',
          conditionalContent:
            state.windowSameAsReportingPeriod === 'no' ? (
              <div className="collection-wizard__window-fields">
                <GovUKDateField
                  id="admin-collections-create-open-date"
                  legend="When will the data collection open?"
                  hint="This is the date the data collection will open and organisations can start submitting information. For example, 15 3 2026"
                  value={{ day: state.opensDay, month: state.opensMonth, year: state.opensYear }}
                  error={dateError('open-date')}
                  errorParts={highlightedParts('open-date')}
                  onChange={(part, value) => dispatch({ type: 'SET_FIELD', field: OPENS_FIELDS[part], value })}
                />
                <GovUKDateField
                  id="admin-collections-create-deadline-date"
                  legend="What is the deadline for submissions?"
                  hint="This is the last date organisations can submit information. After this date, submissions will close. For example, 31 3 2026"
                  value={{ day: state.deadlineDay, month: state.deadlineMonth, year: state.deadlineYear }}
                  error={dateError('deadline-date')}
                  errorParts={highlightedParts('deadline-date')}
                  onChange={(part, value) => dispatch({ type: 'SET_FIELD', field: DEADLINE_FIELDS[part], value })}
                />
              </div>
            ) : undefined,
        },
      ]}
      onChange={(value) => dispatch({ type: 'SET_WINDOW_SAME', value: value as CollectionFormState['windowSameAsReportingPeriod'] })}
      testId="admin-collections-create-window-same"
    />
  );
};

export default StepWindow;

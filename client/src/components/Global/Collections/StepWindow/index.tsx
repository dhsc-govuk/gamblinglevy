import React from 'react';
import { GovUKFieldset } from '../../../GovUKComponents';
import type { CollectionFormState, CollectionFormAction, DatePartValidationIssue } from '../../../../views/Admin/Collections/reducer';
import { reportingPeriod } from '../../../../views/Admin/Collections/display';

interface Props {
  state: CollectionFormState;
  dispatch: React.Dispatch<CollectionFormAction>;
}

const DATE_PART_LIMITS = {
  day: { maxLength: 2, min: 1, max: 31 },
  month: { maxLength: 2, min: 1, max: 12 },
  year: { maxLength: 4, min: 1970 },
} as const;

// "Add data collection" — window step (Figma): whether the data collection window matches the reporting period.
const StepWindow = ({ state, dispatch }: Props): React.ReactElement => {
  const period = reportingPeriod(state.financialYear, state.quarter);

  const dateError = (key: 'open-date' | 'deadline-date') => state.errors[key] ?? state.errors[`${key}-day`] ?? state.errors[`${key}-month`] ?? state.errors[`${key}-year`];
  const dateErrorPart = (key: 'open-date' | 'deadline-date'): DatePartValidationIssue | null => {
    if (state.errors[`${key}-day`]) {
      return 'day';
    }
    if (state.errors[`${key}-month`]) {
      return 'month';
    }
    if (state.errors[`${key}-year`]) {
      return 'year';
    }
    return state.errors[key] ? 'missing' : null;
  };

  const setDatePart = (field: keyof CollectionFormState, value: string, maxLength: number) => {
    dispatch({ type: 'SET_FIELD', field, value: value.replace(/\D/g, '').slice(0, maxLength) });
  };

  const renderDateInput = ({
    id,
    legend,
    hint,
    error,
    errorPart,
    dayField,
    monthField,
    yearField,
  }: {
    id: string;
    legend: string;
    hint: string;
    error?: string;
    errorPart?: DatePartValidationIssue | null;
    dayField: 'opensDay' | 'deadlineDay';
    monthField: 'opensMonth' | 'deadlineMonth';
    yearField: 'opensYear' | 'deadlineYear';
  }) => (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`}>
      <fieldset className="govuk-fieldset" role="group" aria-describedby={[`${id}-hint`, error ? `${id}-error` : ''].filter(Boolean).join(' ')}>
        <legend className="govuk-fieldset__legend govuk-fieldset__legend--s">{legend}</legend>
        <div className="govuk-hint" id={`${id}-hint`}>
          {hint}
        </div>
        {error && (
          <p className="govuk-error-message" id={`${id}-error`}>
            <span className="govuk-visually-hidden">Error:</span> {error}
          </p>
        )}
        <div className="govuk-date-input" id={id}>
          {[
            { label: 'Day', errorPart: 'day', field: dayField, width: '2', ...DATE_PART_LIMITS.day },
            { label: 'Month', errorPart: 'month', field: monthField, width: '2', ...DATE_PART_LIMITS.month },
            { label: 'Year', errorPart: 'year', field: yearField, width: '4', ...DATE_PART_LIMITS.year },
          ].map((item) => (
            <div className="govuk-date-input__item" key={item.field}>
              <div className="govuk-form-group">
                <label className="govuk-label govuk-date-input__label" htmlFor={`${id}-${item.label.toLowerCase()}`}>
                  {item.label}
                </label>
                <input
                  className={`govuk-input govuk-date-input__input govuk-input--width-${item.width} ${errorPart === 'missing' || errorPart === item.errorPart ? 'govuk-input--error' : ''}`}
                  id={`${id}-${item.label.toLowerCase()}`}
                  name={item.field}
                  type="text"
                  inputMode="numeric"
                  pattern="[0-9]*"
                  maxLength={item.maxLength}
                  min={item.min}
                  max={'max' in item ? item.max : undefined}
                  value={state[item.field as keyof CollectionFormState] as string}
                  onChange={(event) => setDatePart(item.field, event.target.value, item.maxLength)}
                  data-testid={`${id}-${item.label.toLowerCase()}`}
                />
              </div>
            </div>
          ))}
        </div>
      </fieldset>
    </div>
  );

  return (
    <GovUKFieldset.RadioGroup
      name="admin-collections-create-window-same"
      legend="Is the data collection window the same as the reporting period?"
      legendSize="m"
      hint={
        <>
          <p className="govuk-body collection-wizard__window-hint-paragraph">
            The data collection window is the period when organisations can submit information. It can be the same as the reporting period, or it can open and close on different dates.
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
                {renderDateInput({
                  id: 'admin-collections-create-open-date',
                  legend: 'When will the data collection open?',
                  hint: 'This is the date the data collection will open and organisations can start submitting information. For example, 15 3 2026',
                  error: dateError('open-date'),
                  errorPart: dateErrorPart('open-date'),
                  dayField: 'opensDay',
                  monthField: 'opensMonth',
                  yearField: 'opensYear',
                })}
                {renderDateInput({
                  id: 'admin-collections-create-deadline-date',
                  legend: 'What is the deadline for submissions?',
                  hint: 'This is the last date organisations can submit information. After this date, submissions will close. For example, 31 3 2026',
                  error: dateError('deadline-date'),
                  errorPart: dateErrorPart('deadline-date'),
                  dayField: 'deadlineDay',
                  monthField: 'deadlineMonth',
                  yearField: 'deadlineYear',
                })}
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

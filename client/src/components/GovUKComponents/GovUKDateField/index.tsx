import React from 'react';
import { DATE_PARTS, sanitiseDatePart, type DatePart } from '../../../helpers/dateParts';

export type { DatePart } from '../../../helpers/dateParts';
export type DateParts = Partial<Record<DatePart, string>>;

interface GovUKDateFieldProps {
  /** Root id. The inputs are `${id}-day`, `${id}-month` and `${id}-year`; an error-summary link targets the first. */
  id: string;
  legend: React.ReactNode;
  legendSize?: 's' | 'm' | 'l' | 'xl';
  hint?: React.ReactNode;
  /** The parts to show, always in day, month, year order. Defaults to all three. */
  fields?: readonly DatePart[];
  value: DateParts;
  /** Called with the part that changed and its new value, already reduced to digits within the part's length. */
  onChange: (part: DatePart, value: string) => void;
  error?: string;
  /** The parts to outline red. With an error and no parts given, every part is outlined: a whole-date error. */
  errorParts?: readonly DatePart[];
  disabled?: boolean;
  testId?: string;
}

const PART_LABELS: Record<DatePart, string> = { day: 'Day', month: 'Month', year: 'Year' };

/**
 * The GOV.UK date input (https://design-system.service.gov.uk/components/date-input/): one fieldset of separate
 * day, month and year boxes. Each part is controlled on its own, so a caller keeps every part in its own state and
 * can say which part an error is about — a single "D/M/YYYY" string can do neither (BLUM-8758).
 *
 * testids: the date row is `testId` (default `id`), each input `{testId}-{part}`, the error `{testId}-error`.
 */
function GovUKDateField({
  id,
  legend,
  legendSize = 's',
  hint,
  fields = DATE_PARTS,
  value,
  onChange,
  error,
  errorParts,
  disabled = false,
  testId,
}: GovUKDateFieldProps): React.ReactElement {
  const rootTestId = testId ?? id;
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined;
  const parts = DATE_PARTS.filter((part) => fields.includes(part));
  const isPartInError = (part: DatePart): boolean => Boolean(error) && (!errorParts || errorParts.includes(part));

  return (
    <div className={`govuk-form-group${error ? ' govuk-form-group--error' : ''}`}>
      <fieldset className="govuk-fieldset" role="group" aria-describedby={describedBy}>
        <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`}>{legend}</legend>
        {hint && (
          <div className="govuk-hint" id={hintId}>
            {hint}
          </div>
        )}
        {error && (
          <p className="govuk-error-message" id={errorId} data-testid={`${rootTestId}-error`}>
            <span className="govuk-visually-hidden">Error:</span> {error}
          </p>
        )}
        <div className="govuk-date-input" id={id} data-testid={rootTestId}>
          {parts.map((part) => (
            <div className="govuk-date-input__item" key={part}>
              <div className="govuk-form-group">
                <label className="govuk-label govuk-date-input__label" htmlFor={`${id}-${part}`}>
                  {PART_LABELS[part]}
                </label>
                {/* No maxLength: the browser applies it to a paste before onChange sees it, so " 2026" would arrive as
                    " 202". sanitiseDatePart strips the non-digits first and then caps the length, as GOV.UK's own date
                    input leaves the length to validation. */}
                <input
                  className={`govuk-input govuk-date-input__input govuk-input--width-${part === 'year' ? '4' : '2'}${isPartInError(part) ? ' govuk-input--error' : ''}`}
                  id={`${id}-${part}`}
                  name={`${id}-${part}`}
                  type="text"
                  inputMode="numeric"
                  value={value[part] ?? ''}
                  disabled={disabled}
                  onChange={(event) => onChange(part, sanitiseDatePart(part, event.target.value))}
                  data-testid={`${rootTestId}-${part}`}
                />
              </div>
            </div>
          ))}
        </div>
      </fieldset>
    </div>
  );
}

export default GovUKDateField;

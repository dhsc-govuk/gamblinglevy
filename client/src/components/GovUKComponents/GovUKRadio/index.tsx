import React from 'react';
import { slugifyTestId } from '../../../helpers/testIds';

interface RadioOption {
  value: string | number;
  label: string;
  hint?: string;
  conditional?: React.ReactNode;
}

interface GovUKRadioProps {
  id: string;
  name: string;
  legend: string;
  legendSize?: 's' | 'm' | 'l';
  questionCode?: string;
  options: RadioOption[];
  value?: string | number;
  error?: string;
  hint?: string;
  disabled?: boolean;
  required?: boolean;
  size?: 'small';
  onChange?: (value: string | number) => void;
  testId?: string;
}

function GovUKRadio({
  id,
  name,
  legend,
  legendSize = 's',
  questionCode,
  options,
  value,
  error,
  hint,
  disabled = false,
  required = false,
  size,
  onChange,
  testId,
}: GovUKRadioProps): React.ReactElement {
  const rootTestId = testId ?? id;
  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} data-testid={rootTestId}>
      {questionCode && <span className="question-code">{questionCode}</span>}
      <fieldset className="govuk-fieldset" aria-describedby={[hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined}>
        <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`}>
          {legend}
          {required && <span className="govuk-visually-hidden"> (required)</span>}
        </legend>

        {hint && (
          <div className="govuk-hint" id={`${id}-hint`}>
            {hint}
          </div>
        )}

        {error && (
          <p className="govuk-error-message" id={`${id}-error`} data-testid={`${rootTestId}-error`}>
            <span className="govuk-visually-hidden">Error:</span> {error}
          </p>
        )}

        <div className={`govuk-radios${size === 'small' ? ' govuk-radios--small' : ''}`} data-module="govuk-radios">
          {options.map((option) => {
            const optionSlug = slugifyTestId(String(option.value)) || String(option.value);
            return (
              <React.Fragment key={String(option.value)}>
                <div className="govuk-radios__item">
                  <input
                    className="govuk-radios__input"
                    id={`${id}-${option.value}`}
                    name={name}
                    type="radio"
                    value={String(option.value)}
                    checked={value === option.value}
                    disabled={disabled}
                    data-testid={`${rootTestId}-${optionSlug}`}
                    onChange={() => onChange?.(option.value)}
                    // Only advertised while the panel below is mounted — this component unmounts it when the
                    // option is deselected, and aria-controls pointing at a missing id fails axe.
                    // aria-expanded is intentionally omitted: ARIA does not support it on role="radio" (unlike
                    // checkbox), so axe flags it as a disallowed attribute.
                    aria-controls={option.conditional && value === option.value ? `${id}-${option.value}-conditional` : undefined}
                  />
                  <label className="govuk-label govuk-radios__label" htmlFor={`${id}-${option.value}`}>
                    {option.label}
                  </label>
                  {option.hint && <div className="govuk-hint govuk-radios__hint">{option.hint}</div>}
                </div>
                {option.conditional && value === option.value && (
                  <div className="govuk-radios__conditional" id={`${id}-${option.value}-conditional`}>
                    {option.conditional}
                  </div>
                )}
              </React.Fragment>
            );
          })}
        </div>
      </fieldset>
    </div>
  );
}

export default GovUKRadio;
export type { RadioOption };

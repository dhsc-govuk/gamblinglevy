import React from 'react';
import { slugifyTestId } from '../../../helpers/testIds';

/* =======================
   Interfaces
======================= */

interface GovUKFieldsetProps {
  legend: string;
  legendSize?: 's' | 'm' | 'l' | 'xl';
  children: React.ReactNode;
  testId?: string;
}

interface GovUKFieldsetInputProps {
  id: string;
  name: string;
  label: string;
  value?: string;
  type?: 'text' | 'email' | 'password' | 'number' | 'tel' | 'url';
  hint?: React.ReactNode;
  error?: string;
  width?: '2' | '3' | '4' | '5' | '10' | '20' | 'two-thirds' | 'full';
  disabled?: boolean;
  required?: boolean;
  readOnly?: boolean;
  autoComplete?: string;
  inputMode?: React.HTMLAttributes<HTMLInputElement>['inputMode'];
  pattern?: string;
  spellCheck?: boolean;
  labelSize?: 's' | 'm' | 'l' | 'xl';
  questionCode?: string;
  onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onBlur?: (e: React.FocusEvent<HTMLInputElement>) => void;
  onKeyUp?: (e: React.KeyboardEvent<HTMLInputElement>) => void;
  testId?: string;
}

interface GovUKFieldsetCheckboxProps {
  id: string;
  name: string;
  label: string;
  labelSize?: 's' | 'm' | 'l' | 'xl';
  checked?: boolean;
  hint?: string;
  disabled?: boolean;
  onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  testId?: string;
}

interface GovUKFieldsetSelectProps {
  id: string;
  name: string;
  label: string;
  labelSize?: 's' | 'm' | 'l' | 'xl';
  value?: string;
  options: { value: string; label: string }[];
  hint?: string;
  error?: string;
  width?: 'one-quarter' | 'one-third' | 'one-half' | 'two-thirds' | 'full';
  disabled?: boolean;
  required?: boolean;
  questionCode?: string;
  onChange?: (e: React.ChangeEvent<HTMLSelectElement>) => void;
  testId?: string;
  conditionalId?: string;
}

interface RadioOption {
  value: string;
  text: string;
  hint?: string;
  conditionalContent?: React.ReactNode;
}

interface GovUKFieldsetRadioGroupProps {
  name: string;
  legend: string;
  legendSize?: 's' | 'm' | 'l';
  labelSize?: 's' | 'm' | 'l' | 'xl';
  options: RadioOption[];
  value?: string;
  error?: string;
  hint?: React.ReactNode;
  disabled?: boolean;
  required?: boolean;
  questionCode?: string;
  size?: 'small';
  onChange?: (value: string) => void;
  testId?: string;
}

interface CheckboxOption {
  value: string;
  text: string;
  hint?: string;
}

interface GovUKFieldsetCheckboxGroupProps {
  name: string;
  legend: string;
  legendSize?: 's' | 'm' | 'l';
  labelSize?: 's' | 'm' | 'l' | 'xl';
  options: CheckboxOption[];
  value?: string[];
  error?: string;
  hint?: string;
  disabled?: boolean;
  required?: boolean;
  questionCode?: string;
  size?: 'small';
  onChange?: (value: string, checked: boolean, allValues: string[]) => void;
  testId?: string;
}

interface GovUKFieldsetTextareaProps {
  id: string;
  name: string;
  label: string;
  labelSize?: 's' | 'm' | 'l' | 'xl';
  value?: string;
  rows?: number;
  hint?: string;
  error?: string;
  disabled?: boolean;
  required?: boolean;
  questionCode?: string;
  onChange?: (e: React.ChangeEvent<HTMLTextAreaElement>) => void;
  onBlur?: (e: React.FocusEvent<HTMLTextAreaElement>) => void;
  testId?: string;
}

/* =======================
   Components
======================= */

function GovUKFieldsetInput({
  id,
  name,
  label,
  value = '',
  type = 'text',
  hint,
  error,
  width,
  disabled = false,
  required = false,
  readOnly = false,
  autoComplete,
  inputMode,
  pattern,
  spellCheck,
  questionCode,
  labelSize = 's',
  onChange,
  onBlur,
  onKeyUp,
  testId,
}: GovUKFieldsetInputProps): React.ReactElement {
  const inputTestId = testId ?? id;
  const getWidthClass = () => {
    if (!width) return '';
    if (width === 'two-thirds') return 'govuk-!-width-two-thirds';
    if (width === 'full') return '';
    return `govuk-input--width-${width}`;
  };

  const inputClassName = ['govuk-input', error ? 'govuk-input--error' : '', getWidthClass()].filter(Boolean).join(' ');

  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} style={{ position: 'relative' }}>
      {questionCode && (
        <span
          style={{
            position: 'absolute',
            right: 0,
            top: 0,
            color: '#505a5f',
            fontSize: '16px',
            fontWeight: 'normal',
            whiteSpace: 'nowrap',
          }}>
          {questionCode}
        </span>
      )}
      <label className={`govuk-label govuk-label--${labelSize}`} htmlFor={id} style={{ paddingRight: questionCode ? '100px' : undefined }}>
        {label}
      </label>

      {hint && (
        <div className="govuk-hint" id={`${id}-hint`}>
          {hint}
        </div>
      )}

      {error && (
        <p className="govuk-error-message" id={`${id}-error`} data-testid={`${inputTestId}-error`}>
          <span className="govuk-visually-hidden">Error:</span> {error}
        </p>
      )}

      <input
        className={inputClassName}
        id={id}
        name={name}
        data-testid={inputTestId}
        type={type}
        value={value}
        disabled={disabled}
        required={required}
        readOnly={readOnly}
        autoComplete={autoComplete}
        inputMode={inputMode}
        pattern={pattern}
        spellCheck={spellCheck}
        aria-describedby={[hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined}
        onChange={onChange}
        onBlur={onBlur}
        onKeyUp={onKeyUp}
      />
    </div>
  );
}

function GovUKFieldsetCheckbox({ id, name, label, checked = false, hint, disabled = false, onChange, testId }: GovUKFieldsetCheckboxProps): React.ReactElement {
  const inputTestId = testId ?? id;
  return (
    <div className="govuk-form-group">
      <div className="govuk-checkboxes" data-module="govuk-checkboxes">
        <div className="govuk-checkboxes__item">
          <input className="govuk-checkboxes__input" id={id} name={name} data-testid={inputTestId} type="checkbox" checked={checked} disabled={disabled} onChange={onChange} />
          <label className="govuk-label govuk-checkboxes__label" htmlFor={id}>
            {label}
          </label>
          {hint && (
            <div className="govuk-hint govuk-checkboxes__hint" id={`${id}-hint`}>
              {hint}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function GovUKFieldsetSelect({
  id,
  name,
  label,
  labelSize = 's',
  value = '',
  options,
  hint,
  error,
  width,
  disabled = false,
  required = false,
  questionCode,
  onChange,
  testId,
  conditionalId,
}: GovUKFieldsetSelectProps): React.ReactElement {
  const selectTestId = testId ?? id;
  const getWidthClass = () => {
    if (!width || width === 'full') return '';
    return `govuk-!-width-${width}`;
  };

  const selectClassName = ['govuk-select', error ? 'govuk-select--error' : '', getWidthClass()].filter(Boolean).join(' ');
  const isFullWidth = !width || width === 'full';

  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} style={{ position: 'relative' }}>
      {questionCode && (
        <span
          style={{
            position: 'absolute',
            right: 0,
            top: 0,
            color: '#505a5f',
            fontSize: '16px',
            fontWeight: 'normal',
            whiteSpace: 'nowrap',
          }}>
          {questionCode}
        </span>
      )}
      <label className={`govuk-label govuk-label--${labelSize}`} htmlFor={id} style={{ paddingRight: questionCode ? '100px' : undefined }}>
        {label}
      </label>

      {hint && (
        <div className="govuk-hint" id={`${id}-hint`}>
          {hint}
        </div>
      )}

      {error && (
        <p className="govuk-error-message" id={`${id}-error`} data-testid={`${selectTestId}-error`}>
          <span className="govuk-visually-hidden">Error:</span> {error}
        </p>
      )}

      <select
        className={selectClassName}
        id={id}
        name={name}
        data-testid={selectTestId}
        value={value}
        disabled={disabled}
        required={required}
        aria-describedby={[hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined}
        aria-controls={conditionalId || undefined}
        onChange={onChange}
        style={isFullWidth ? { width: '100%' } : undefined}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

function GovUKFieldsetRadioGroup({
  name,
  legend,
  legendSize = 's',
  options,
  value,
  error,
  hint,
  disabled = false,
  required = false,
  questionCode,
  size,
  onChange,
  testId,
}: GovUKFieldsetRadioGroupProps): React.ReactElement {
  const fieldsetId = `${name}-fieldset`;
  const rootTestId = testId ?? name;
  const radiosClassName = ['govuk-radios', size === 'small' ? 'govuk-radios--small' : ''].filter(Boolean).join(' ');

  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} style={{ position: 'relative' }}>
      {questionCode && (
        <span
          style={{
            position: 'absolute',
            right: 0,
            top: 0,
            color: '#505a5f',
            fontSize: '16px',
            fontWeight: 'normal',
            whiteSpace: 'nowrap',
          }}>
          {questionCode}
        </span>
      )}
      <fieldset className="govuk-fieldset" aria-describedby={[hint ? `${fieldsetId}-hint` : '', error ? `${fieldsetId}-error` : ''].filter(Boolean).join(' ') || undefined}>
        <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`} style={{ paddingRight: questionCode ? '100px' : undefined }}>
          {legend}
          {required && <span className="govuk-visually-hidden"> (required)</span>}
        </legend>

        {hint && (
          <div className="govuk-hint" id={`${fieldsetId}-hint`}>
            {hint}
          </div>
        )}

        {error && (
          <p className="govuk-error-message" id={`${fieldsetId}-error`} data-testid={testId ? `${testId}-error` : undefined}>
            <span className="govuk-visually-hidden">Error:</span> {error}
          </p>
        )}

        <div className={radiosClassName} data-module="govuk-radios" data-testid={rootTestId}>
          {options.map((option: RadioOption) => {
            const optionSlug = slugifyTestId(option.value) || option.value;
            // Per the govuk-frontend radios component, an option hint must be announced with its input:
            // the hint carries an id and the input references it via aria-describedby.
            const optionHintId = option.hint ? `${name}-${option.value}-item-hint` : undefined;
            return (
              <React.Fragment key={option.value}>
                <div className="govuk-radios__item">
                  <input
                    className="govuk-radios__input"
                    id={`${name}-${option.value}`}
                    name={name}
                    type="radio"
                    value={option.value}
                    checked={value === option.value}
                    disabled={disabled}
                    aria-describedby={optionHintId}
                    data-testid={`${rootTestId}-${optionSlug}`}
                    // aria-expanded is intentionally omitted: ARIA does not support it on role="radio" (unlike
                    // checkbox), so axe flags it as a disallowed attribute. aria-controls carries the association.
                    aria-controls={option.conditionalContent ? `conditional-${name}-${option.value}` : undefined}
                    onChange={() => onChange?.(option.value)}
                  />
                  <label className="govuk-label govuk-radios__label" htmlFor={`${name}-${option.value}`}>
                    {option.text}
                  </label>
                  {option.hint && (
                    <div className="govuk-hint govuk-radios__hint" id={optionHintId}>
                      {option.hint}
                    </div>
                  )}
                </div>
                {option.conditionalContent && (
                  <div className={`govuk-radios__conditional ${value !== option.value ? 'govuk-radios__conditional--hidden' : ''}`} id={`conditional-${name}-${option.value}`}>
                    {option.conditionalContent}
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

function GovUKFieldsetCheckboxGroup({
  name,
  legend,
  legendSize = 's',
  options,
  value = [],
  error,
  hint,
  disabled = false,
  questionCode,
  size,
  onChange,
  testId,
}: GovUKFieldsetCheckboxGroupProps): React.ReactElement {
  const rootTestId = testId ?? name;
  const handleChange = (optionValue: string, checked: boolean) => {
    const updated = checked ? [...value, optionValue] : value.filter((v: string) => v !== optionValue);
    onChange?.(optionValue, checked, updated);
  };
  const checkboxesClassName = ['govuk-checkboxes', size === 'small' ? 'govuk-checkboxes--small' : ''].filter(Boolean).join(' ');

  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`}>
      <fieldset className="govuk-fieldset">
        <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`}>
          {legend}
          {questionCode && <sup style={{ color: '#505a5f', fontSize: '14px', fontWeight: 'normal', marginLeft: '2px' }}>{questionCode}</sup>}
        </legend>

        {hint && <div className="govuk-hint">{hint}</div>}

        {error && (
          <p className="govuk-error-message">
            <span className="govuk-visually-hidden">Error:</span> {error}
          </p>
        )}

        <div className={checkboxesClassName} data-module="govuk-checkboxes" data-testid={rootTestId}>
          {options.map((option: CheckboxOption) => {
            const optionSlug = slugifyTestId(option.value) || option.value;
            const optionId = `${name}-${option.value}`;
            const hintId = option.hint ? `${optionId}-hint` : undefined;
            return (
              <div className="govuk-checkboxes__item" key={option.value}>
                <input
                  className="govuk-checkboxes__input"
                  id={optionId}
                  name={name}
                  data-testid={`${rootTestId}-${optionSlug}`}
                  type="checkbox"
                  checked={value.includes(option.value)}
                  disabled={disabled}
                  aria-describedby={hintId}
                  onChange={(e) => handleChange(option.value, e.target.checked)}
                />
                <label className="govuk-label govuk-checkboxes__label" htmlFor={optionId}>
                  {option.text}
                </label>
                {option.hint && (
                  <div className="govuk-hint govuk-checkboxes__hint" id={hintId}>
                    {option.hint}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </fieldset>
    </div>
  );
}

function GovUKFieldsetTextarea({
  id,
  name,
  label,
  labelSize = 's',
  value = '',
  rows = 4,
  hint,
  error,
  disabled = false,
  required = false,
  questionCode,
  onChange,
  onBlur,
  testId,
}: GovUKFieldsetTextareaProps): React.ReactElement {
  const textareaTestId = testId ?? id;
  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} style={{ position: 'relative' }}>
      {questionCode && (
        <span
          style={{
            position: 'absolute',
            right: 0,
            top: 0,
            color: '#505a5f',
            fontSize: '16px',
            fontWeight: 'normal',
            whiteSpace: 'nowrap',
          }}>
          {questionCode}
        </span>
      )}
      <label className={`govuk-label govuk-label--${labelSize}`} htmlFor={id} style={{ paddingRight: questionCode ? '100px' : undefined }}>
        {label}
        {required && <span className="govuk-visually-hidden"> (required)</span>}
      </label>

      {hint && (
        <div className="govuk-hint" id={`${id}-hint`}>
          {hint}
        </div>
      )}

      {error && (
        <p className="govuk-error-message" id={`${id}-error`} data-testid={`${textareaTestId}-error`}>
          <span className="govuk-visually-hidden">Error:</span> {error}
        </p>
      )}

      <textarea
        className={`govuk-textarea ${error ? 'govuk-textarea--error' : ''}`}
        id={id}
        name={name}
        data-testid={textareaTestId}
        rows={rows}
        value={value}
        disabled={disabled}
        required={required}
        onChange={onChange}
        onBlur={onBlur}
      />
    </div>
  );
}

function GovUKFieldset({ legend, legendSize = 'l', children, testId }: GovUKFieldsetProps): React.ReactElement {
  return (
    <fieldset className="govuk-fieldset" data-testid={testId}>
      <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`}>
        <h1 className="govuk-fieldset__heading">{legend}</h1>
      </legend>
      {children}
    </fieldset>
  );
}

/* =======================
   Attach Sub-components
======================= */

GovUKFieldset.Input = GovUKFieldsetInput;
GovUKFieldset.Checkbox = GovUKFieldsetCheckbox;
GovUKFieldset.Select = GovUKFieldsetSelect;
GovUKFieldset.RadioGroup = GovUKFieldsetRadioGroup;
GovUKFieldset.CheckboxGroup = GovUKFieldsetCheckboxGroup;
GovUKFieldset.Textarea = GovUKFieldsetTextarea;

export default GovUKFieldset;
export type { RadioOption, CheckboxOption };

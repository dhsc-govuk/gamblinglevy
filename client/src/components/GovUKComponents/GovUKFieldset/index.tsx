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
  // Extra element ids to append to aria-describedby, for a hint or error that describes the group this input
  // belongs to rather than the input itself (e.g. the expected-results repeater's screen-level hint).
  describedBy?: string;
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
  // A single box the user must tick to go on ("I have saved my codes"). The error-summary link targets `id`.
  error?: string;
  size?: 'small';
  disabled?: boolean;
  onChange?: (e: React.ChangeEvent<HTMLInputElement>) => void;
  testId?: string;
}

interface GovUKFieldsetSelectProps {
  id: string;
  name: string;
  label: string;
  // 'none' renders the stock `govuk-label` with no size modifier — the regular weight GovUKTable already
  // uses for its own sort and filter labels, and what the GDS Select component ships with by default.
  labelSize?: 's' | 'm' | 'l' | 'xl' | 'none';
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
  // Appended to the legend for assistive tech only, so a legend repeated down the page (one per item in
  // a repeating block) still names a unique group. Same idea as GovUKInputWithSuffix's prop of this name.
  visuallyHiddenText?: string;
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

interface CheckboxSelectAll {
  label: React.ReactNode;
  checked: boolean;
  // Some but not all of the options are ticked: shown as a dash and announced as "mixed".
  indeterminate?: boolean;
  onChange: (checked: boolean) => void;
}

interface GovUKFieldsetCheckboxGroupProps {
  name: string;
  legend: string;
  legendSize?: 's' | 'm' | 'l';
  labelSize?: 's' | 'm' | 'l' | 'xl';
  options: CheckboxOption[];
  value?: string[];
  error?: string;
  hint?: React.ReactNode;
  disabled?: boolean;
  required?: boolean;
  questionCode?: string;
  size?: 'small';
  onChange?: (value: string, checked: boolean, allValues: string[]) => void;
  // A leading "select all" box, id `${name}-all`, testid `${testId}-all`.
  selectAll?: CheckboxSelectAll;
  // Shown in place of the boxes when there are no options (a search that matched nothing); testid `${testId}-empty`.
  emptyMessage?: React.ReactNode;
  // Rendered inside the fieldset between the hint and error and the boxes, for a control that narrows the options.
  children?: React.ReactNode;
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
  // Turns the field into a GDS character count: a live "characters remaining / too many" message under the box.
  // The field is deliberately NOT capped with the native maxlength attribute — GDS requires the user to be able
  // to type past the limit and see how far over they are (BLUM-7620).
  maxLength?: number;
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
  describedBy,
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
        aria-describedby={[hint && `${id}-hint`, error && `${id}-error`, describedBy].filter(Boolean).join(' ') || undefined}
        onChange={onChange}
        onBlur={onBlur}
        onKeyUp={onKeyUp}
      />
    </div>
  );
}

function GovUKFieldsetCheckbox({ id, name, label, checked = false, hint, error, size, disabled = false, onChange, testId }: GovUKFieldsetCheckboxProps): React.ReactElement {
  const inputTestId = testId ?? id;
  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`}>
      {error && (
        <p className="govuk-error-message" id={`${id}-error`} data-testid={`${inputTestId}-error`}>
          <span className="govuk-visually-hidden">Error:</span> {error}
        </p>
      )}
      <div className={`govuk-checkboxes ${size === 'small' ? 'govuk-checkboxes--small' : ''}`} data-module="govuk-checkboxes">
        <div className="govuk-checkboxes__item">
          <input
            className="govuk-checkboxes__input"
            id={id}
            name={name}
            data-testid={inputTestId}
            type="checkbox"
            checked={checked}
            disabled={disabled}
            aria-describedby={[hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined}
            onChange={onChange}
          />
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
      <label className={labelSize === 'none' ? 'govuk-label' : `govuk-label govuk-label--${labelSize}`} htmlFor={id} style={{ paddingRight: questionCode ? '100px' : undefined }}>
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
  visuallyHiddenText,
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
          {visuallyHiddenText && <span className="govuk-visually-hidden"> {visuallyHiddenText}</span>}
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
  selectAll,
  emptyMessage,
  children,
  testId,
}: GovUKFieldsetCheckboxGroupProps): React.ReactElement {
  const rootTestId = testId ?? name;
  // Not `${name}-hint`: option ids are `${name}-${value}`, and an option could be valued "hint".
  const hintId = `${name}-fieldset-hint`;
  const errorId = `${name}-fieldset-error`;
  const handleChange = (optionValue: string, checked: boolean) => {
    const updated = checked ? [...value, optionValue] : value.filter((v: string) => v !== optionValue);
    onChange?.(optionValue, checked, updated);
  };
  const checkboxesClassName = ['govuk-checkboxes', size === 'small' ? 'govuk-checkboxes--small' : ''].filter(Boolean).join(' ');

  // The root testid is on the form group, so it covers the empty state as well as the boxes.
  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`} data-testid={rootTestId}>
      <fieldset className="govuk-fieldset" aria-describedby={[hint && hintId, error && errorId].filter(Boolean).join(' ') || undefined}>
        <legend className={`govuk-fieldset__legend govuk-fieldset__legend--${legendSize}`}>
          {legend}
          {questionCode && <sup style={{ color: '#505a5f', fontSize: '14px', fontWeight: 'normal', marginLeft: '2px' }}>{questionCode}</sup>}
        </legend>

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

        {children}

        {options.length === 0 && emptyMessage !== undefined ? (
          <p className="govuk-hint" data-testid={`${rootTestId}-empty`}>
            {emptyMessage}
          </p>
        ) : (
          <div className={checkboxesClassName} data-module="govuk-checkboxes">
            {selectAll && (
              <div className="govuk-checkboxes__item">
                <input
                  // A callback ref rather than an effect: indeterminate is a property with no attribute, and this
                  // re-applies it on every render, including when the box comes back after a search matched nothing.
                  ref={(input) => {
                    if (input) input.indeterminate = Boolean(selectAll.indeterminate);
                  }}
                  className="govuk-checkboxes__input"
                  id={`${name}-all`}
                  name={`${name}-all`}
                  data-testid={`${rootTestId}-all`}
                  type="checkbox"
                  checked={selectAll.checked}
                  disabled={disabled}
                  onChange={(e) => selectAll.onChange(e.target.checked)}
                />
                <label className="govuk-label govuk-checkboxes__label" htmlFor={`${name}-all`}>
                  {selectAll.label}
                </label>
              </div>
            )}
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
        )}
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
  maxLength,
  onChange,
  onBlur,
  testId,
}: GovUKFieldsetTextareaProps): React.ReactElement {
  const textareaTestId = testId ?? id;

  // govuk-frontend's own character count is a JS module and this app ships the CSS only, so the count is
  // computed here. Wording follows the GDS component: "remaining" until the limit is passed, then "too many".
  // Counts the raw value, as govuk-frontend does — so trailing whitespace can show "1 character too many" while
  // Continue still succeeds, because the validators compare the trimmed length.
  const remaining = maxLength === undefined ? null : maxLength - value.length;
  const overLimit = remaining !== null && remaining < 0;
  const countMessage =
    remaining === null
      ? null
      : remaining >= 0
      ? `You have ${remaining} character${remaining === 1 ? '' : 's'} remaining`
      : `You have ${-remaining} character${remaining === -1 ? '' : 's'} too many`;

  // The visible counter is aria-hidden and this is the copy of it screen readers get, deliberately lagging a
  // second behind the last keystroke. govuk-frontend does the same (updateCountMessage polls on focus and only
  // speaks once typing has paused): announcing on every keystroke talks over the user's own character echo.
  //
  // The first value is written in the same commit that mounts the region, never by the timer — a polite region
  // that appears empty and gains text a moment later is announced unprompted on page load. govuk-frontend avoids
  // that the same way, by calling updateScreenReaderCountMessage() synchronously from init().
  const [announcedCount, setAnnouncedCount] = React.useState<string | null>(countMessage);
  const hasSeededAnnouncement = React.useRef(countMessage !== null);
  React.useEffect(() => {
    if (countMessage === null) {
      return undefined;
    }

    // Also covers a maxLength that only arrives after mount (a reseed while the page is open), where the region
    // mounts empty: seed it on the next commit rather than a second later, so the gap is one frame.
    if (!hasSeededAnnouncement.current) {
      hasSeededAnnouncement.current = true;
      setAnnouncedCount(countMessage);
      return undefined;
    }

    const timer = setTimeout(() => setAnnouncedCount(countMessage), 1000);
    return () => clearTimeout(timer);
  }, [countMessage]);

  // Only the static description is named here. The counter itself is announced through the live region above, so
  // adding it would make a screen reader read the count again every time the field takes focus.
  const describedBy = [hint ? `${id}-hint` : null, error ? `${id}-error` : null, countMessage ? `${id}-info` : null].filter(Boolean).join(' ');

  return (
    <div
      className={['govuk-form-group', error ? 'govuk-form-group--error' : '', countMessage ? 'govuk-character-count' : ''].filter(Boolean).join(' ')}
      style={{ position: 'relative' }}>
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
        className={`govuk-textarea ${error || overLimit ? 'govuk-textarea--error' : ''}`.trimEnd()}
        id={id}
        name={name}
        data-testid={textareaTestId}
        rows={rows}
        value={value}
        disabled={disabled}
        required={required}
        aria-describedby={describedBy || undefined}
        onChange={onChange}
        onBlur={onBlur}
      />

      {countMessage !== null && (
        <>
          {/* The three nodes govuk-frontend splits the counter into: a static description that is the only thing
              aria-describedby names, the visible count (hidden from assistive tech), and the debounced live
              region that actually speaks it. */}
          <div id={`${id}-info`} className="govuk-hint govuk-character-count__message govuk-visually-hidden">
            You can enter up to {maxLength} characters
          </div>
          {/* __message carries the tabular numerals and zeroed margins; __status is the marker govuk-frontend
              adds on top of it. Without __message the digits reflow on every keystroke. */}
          <div
            className={`govuk-character-count__message govuk-character-count__status ${overLimit ? 'govuk-error-message' : 'govuk-hint'}`}
            data-testid={`${textareaTestId}-info`}
            aria-hidden="true">
            {countMessage}
          </div>
          <div className="govuk-character-count__sr-status govuk-visually-hidden" aria-live="polite">
            {announcedCount}
          </div>
        </>
      )}
    </div>
  );
}

interface GovUKFieldsetFileUploadProps {
  id: string;
  name: string;
  label: string;
  // 'none' renders the stock regular-weight `govuk-label`, as GovUKFieldset.Select's does.
  labelSize?: 's' | 'm' | 'l' | 'xl' | 'none';
  hint?: React.ReactNode;
  error?: string;
  // The file types the picker offers, e.g. ".csv,text/csv". The server still checks what arrives.
  accept?: string;
  disabled?: boolean;
  onChange?: (file: File | null) => void;
  testId?: string;
}

// The GDS file upload: a native <input type="file"> in the standard form group, so an error summary link to #id
// moves focus onto the control itself. It is uncontrolled (a browser will not let a page set a file input's
// value); a caller that needs to clear it remounts it with a new key.
function GovUKFieldsetFileUpload({ id, name, label, labelSize = 's', hint, error, accept, disabled = false, onChange, testId }: GovUKFieldsetFileUploadProps): React.ReactElement {
  const inputTestId = testId ?? id;
  const describedBy = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined;

  return (
    <div className={`govuk-form-group ${error ? 'govuk-form-group--error' : ''}`}>
      <label className={labelSize === 'none' ? 'govuk-label' : `govuk-label govuk-label--${labelSize}`} htmlFor={id}>
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
        className={`govuk-file-upload ${error ? 'govuk-file-upload--error' : ''}`}
        id={id}
        name={name}
        type="file"
        accept={accept}
        disabled={disabled}
        aria-describedby={describedBy}
        data-testid={inputTestId}
        onChange={(event) => onChange?.(event.target.files?.[0] ?? null)}
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
GovUKFieldset.FileUpload = GovUKFieldsetFileUpload;

export default GovUKFieldset;
export type { RadioOption, CheckboxOption };

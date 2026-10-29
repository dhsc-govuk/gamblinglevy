import React from 'react';
import SearchIcon from '../../Icons/SearchIcon';
import { composeTestId } from '../../../helpers/testIds';
import './styles.css';

export interface GovUKSearchInputProps {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  /** The search button. Results already follow every keystroke, so callers use it to announce them. */
  onSearch?: () => void;
  name?: string;
  /** The button's accessible name. Defaults to "Search". */
  buttonLabel?: string;
  /** On the root element, for the caller's grid column or spacing. */
  className?: string;
  /** The input's testid; the button's is `{testId}-button`. No testids are emitted without it. */
  testId?: string;
}

/**
 * A labelled search box with the blue search-icon button beside it: the filter search used by GovUKTable and by
 * the pages that build their own filter row. Extracted from three hand-written copies (BLUM-8758).
 *
 * It is not a GovUKFieldset.Input: a filter box has no error or hint, and sits in a filter row rather than a
 * form group.
 */
const GovUKSearchInput = ({ id, label, value, onChange, onSearch, name, buttonLabel = 'Search', className, testId }: GovUKSearchInputProps): React.ReactElement => (
  <div className={className}>
    <label htmlFor={id} className="govuk-label">
      {label}
    </label>
    <div className="app-search-input__control">
      <input
        id={id}
        name={name}
        type="search"
        className="govuk-input app-search-input__input"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        data-testid={testId}
      />
      <button type="button" className="app-search-input__button" aria-label={buttonLabel} onClick={onSearch} data-testid={composeTestId(testId, 'button')}>
        <SearchIcon />
      </button>
    </div>
  </div>
);

export default GovUKSearchInput;

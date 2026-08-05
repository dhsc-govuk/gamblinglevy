import React, { useEffect, useMemo, useRef, useState } from 'react';
import { composeTestId } from '../../../../helpers/testIds';
import SearchIcon from '../../../Icons/SearchIcon';
import './styles.css';

interface LaOption {
  id: string;
  name: string;
}

interface CollectionLaSelectorProps {
  las: LaOption[];
  selectedIds: string[];
  onToggle: (id: string) => void;
  onToggleAll: (checked: boolean, filteredIds: string[]) => void;
  error?: string;
  testId?: string;
}

// Searchable, multi-select list of reporting local authorities used by the add-collection wizard's
// "Reporting LAs" step. Search filters the list client-side; the "All" checkbox selects/clears every
// LA currently matching the search.
const CollectionLaSelector = ({ las, selectedIds, onToggle, onToggleAll, error, testId }: CollectionLaSelectorProps): React.ReactElement => {
  const [search, setSearch] = useState('');
  const allCheckboxRef = useRef<HTMLInputElement>(null);

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) {
      return las;
    }
    return las.filter((la) => la.name.toLowerCase().includes(query));
  }, [las, search]);

  const allFilteredSelected = filtered.length > 0 && filtered.every((la) => selectedIds.includes(la.id));
  const someFilteredSelected = filtered.some((la) => selectedIds.includes(la.id));
  const partiallyFilteredSelected = someFilteredSelected && !allFilteredSelected;
  const errorId = composeTestId(testId, 'error') ?? 'collection-la-selector-error';
  const describedBy = error ? errorId : undefined;

  useEffect(() => {
    if (allCheckboxRef.current) {
      allCheckboxRef.current.indeterminate = partiallyFilteredSelected;
    }
  }, [partiallyFilteredSelected]);

  return (
    <div className={`govuk-form-group collection-la-selector ${error ? 'govuk-form-group--error' : ''}`} data-testid={testId}>
      <h2 className="govuk-heading-s collection-la-selector__heading">Which organisations must submit information?</h2>
      <div className="govuk-hint collection-la-selector__hint">Select the organisations to include in this data collection</div>

      {error && (
        <p className="govuk-error-message" id={errorId} data-testid={composeTestId(testId, 'error')}>
          <span className="govuk-visually-hidden">Error:</span> {error}
        </p>
      )}

      <div className="govuk-form-group collection-la-selector__search-group">
        <label className="govuk-label" htmlFor={`${testId ?? 'collection-la-selector'}-search`}>
          Search organisations by name
        </label>
        <div className="collection-la-selector__search">
          <input
            className="govuk-input collection-la-selector__search-input"
            id={`${testId ?? 'collection-la-selector'}-search`}
            name="la-search"
            type="search"
            value={search}
            data-testid={composeTestId(testId, 'search')}
            aria-describedby={describedBy}
            onChange={(event) => setSearch(event.target.value)}
          />
          <button type="button" className="collection-la-selector__search-button" aria-label="Search organisations by name" data-testid={composeTestId(testId, 'search-button')}>
            <SearchIcon />
          </button>
        </div>
      </div>

      {filtered.length === 0 ? (
        <p className="govuk-hint" data-testid={composeTestId(testId, 'empty')}>
          There are no results matching your search.
        </p>
      ) : (
        <div className="govuk-checkboxes govuk-checkboxes--small collection-la-selector__checkboxes" data-module="govuk-checkboxes">
          <div className="govuk-checkboxes__item collection-la-selector__all">
            <input
              ref={allCheckboxRef}
              className={`govuk-checkboxes__input ${partiallyFilteredSelected ? 'collection-la-selector__all-input--indeterminate' : ''}`}
              id={`${testId ?? 'collection-la-selector'}-all`}
              name="la-select-all"
              type="checkbox"
              checked={allFilteredSelected}
              data-testid={composeTestId(testId, 'all')}
              aria-describedby={describedBy}
              onChange={(event) =>
                onToggleAll(
                  partiallyFilteredSelected ? false : event.target.checked,
                  filtered.map((la) => la.id)
                )
              }
            />
            <label className="govuk-label govuk-checkboxes__label" htmlFor={`${testId ?? 'collection-la-selector'}-all`}>
              {partiallyFilteredSelected ? <span className="govuk-visually-hidden">All</span> : 'All'}
            </label>
          </div>

          {filtered.map((la) => {
            const inputId = `${testId ?? 'collection-la-selector'}-${la.id}`;
            return (
              <div className="govuk-checkboxes__item collection-la-selector__option" key={la.id}>
                <input
                  className="govuk-checkboxes__input"
                  id={inputId}
                  name="la"
                  type="checkbox"
                  value={la.id}
                  checked={selectedIds.includes(la.id)}
                  data-testid={composeTestId(testId, la.id)}
                  onChange={() => onToggle(la.id)}
                />
                <label className="govuk-label govuk-checkboxes__label" htmlFor={inputId}>
                  {la.name}
                </label>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

export default CollectionLaSelector;

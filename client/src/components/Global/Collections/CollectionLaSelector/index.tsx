import React, { useMemo, useState } from 'react';
import { GovUKFieldset, GovUKSearchInput } from '../../../GovUKComponents';
import { composeTestId } from '../../../../helpers/testIds';
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
  // The checkbox group's name, which its input ids derive from: `${name}-all` is the error summary's target.
  const name = testId ?? 'collection-la-selector';

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

  return (
    <div className="collection-la-selector">
      <GovUKFieldset.CheckboxGroup
        name={name}
        legend="Which organisations must submit information?"
        legendSize="s"
        hint="Select which organisations to include in this data collection"
        size="small"
        options={filtered.map((la) => ({ value: la.id, text: la.name }))}
        value={selectedIds}
        error={error}
        onChange={(id) => onToggle(id)}
        selectAll={{
          label: partiallyFilteredSelected ? <span className="govuk-visually-hidden">All</span> : 'All',
          checked: allFilteredSelected,
          indeterminate: partiallyFilteredSelected,
          // From part-selected, a click clears rather than completing the selection.
          onChange: (checked) =>
            onToggleAll(
              partiallyFilteredSelected ? false : checked,
              filtered.map((la) => la.id)
            ),
        }}
        emptyMessage="There are no results matching your search."
        testId={testId}>
        <GovUKSearchInput
          className="collection-la-selector__search-group"
          id={`${name}-search`}
          name="la-search"
          label="Search organisations by name"
          buttonLabel="Search organisations by name"
          value={search}
          onChange={setSearch}
          testId={composeTestId(testId, 'search')}
        />
      </GovUKFieldset.CheckboxGroup>
    </div>
  );
};

export default CollectionLaSelector;

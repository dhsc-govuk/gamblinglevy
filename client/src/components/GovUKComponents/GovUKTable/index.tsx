import React, { useEffect, useState, useMemo, useId } from 'react';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { Button, Table, Tag } from 'govuk-react';
import { H1 } from '../GovUKTypography';
import GovUKSearchInput from '../GovUKSearchInput';
import GovUKTableScroll from '../GovUKTableScroll';
import { composeTestId } from '../../../helpers/testIds';
import './styles.css';

export interface Column<T> {
  key: keyof T | string;
  header: React.ReactNode;
  render?: (item: T) => React.ReactNode;
  sortable?: boolean;
  align?: 'left' | 'right' | 'center';
  className?: string;
  headerClassName?: string;
  cellClassName?: string;
  headerStyle?: React.CSSProperties;
  cellStyle?: React.CSSProperties;
}

export interface FilterOption<T = unknown> {
  value: string;
  label: string;
  // Decides which rows the option keeps, in place of the exact match on filterField. This is how one select filters on
  // more than one field (the LA report history's "Type: …" and "Status: …" options, BLUM-9088).
  matches?: (item: T) => boolean;
}

interface GovUKTableBaseProps<T> {
  data: T[];
  columns: Column<T>[];
  searchPlaceholder?: string;
  searchable?: boolean;
  sortOptions?: FilterOption[];
  sortLabel?: string;
  onSort?: (value: string) => void;
  searchField?: keyof T | string;
  filterOptions?: FilterOption<T>[];
  filterLabel?: string;
  filterPlaceholder?: string;
  onFilter?: (value: string) => void;
  filterField?: keyof T | string;
  // Optional second exact-match filter (e.g. filter by type AND by status on the same list). All four
  // secondary* props are additive — when omitted the table behaves exactly as before (single filter).
  secondaryFilterOptions?: FilterOption<T>[];
  secondaryFilterLabel?: string;
  secondaryFilterPlaceholder?: string;
  onSecondaryFilter?: (value: string) => void;
  secondaryFilterField?: keyof T | string;
  addButtonLabel?: string;
  addButtonHref?: string;
  onRowClick?: (item: T) => void;
  getRowHref?: (item: T) => string;
  keyExtractor: (item: T) => string;
  /** Shown when the list has no rows at all. */
  emptyMessage?: string;
  /** Shown when rows exist but the search/filters matched none. Passing the wrong one of these two fails silently. */
  noResultsMessage?: string;
  hideHeader?: boolean;
  testId?: string;
}

// A table must have a name for the region its scroll box becomes when the table is too wide for its column (see
// GovUKTableScroll), so it takes `label`, `title`, or both — never neither.
type GovUKTableName =
  | {
      /** What the table lists ("Organisations"), matching the heading it sits under. Defaults to `title`. */
      label: string;
      /** Rendered as the page's <h1> above the table. */
      title?: string;
    }
  | { label?: string; title: string };

export type GovUKTableProps<T> = GovUKTableBaseProps<T> & GovUKTableName;

// The filter row's default column tracks from 641px, one per control: the widths the row had when it borrowed
// govuk-grid-row and govuk-grid-column-*. Those columns are a share of the row plus its 30px of negative gutters, less
// 30px of padding — two-thirds of (W + 30px) - 30px is two-thirds of W - 10px — so with the row's 30px column-gap these
// tracks reproduce the GOV.UK grid exactly, without the gutters, padding, floats and clearfix every page that laid the
// row out itself had to undo (BLUM-9350). Such a page overrides grid-template-columns, or display, and never sees them.
const FILTER_TRACKS = {
  twoThirds: 'calc(200% / 3 - 10px)',
  third: 'calc(100% / 3 - 20px)',
  quarter: 'calc(25% - 22.5px)',
};

// The row an exact-match filter keeps: the selected option's own `matches` when it has one, otherwise the row whose
// `field` equals the value. With neither, the row is kept, because a caller that filters in the parent via onFilter
// passes no field.
function matchesFilter<T>(item: T, options: FilterOption<T>[] | undefined, value: string, field: keyof T | string | undefined): boolean {
  const option = options?.find((candidate) => candidate.value === value);
  if (option?.matches) {
    return option.matches(item);
  }
  return field ? (item as Record<string, unknown>)[field as string] === value : true;
}

function GovUKTableComponent<T>({
  data,
  columns,
  label,
  title,
  searchPlaceholder = 'Search by name',
  searchable = true,
  sortOptions,
  sortLabel = 'Sort list',
  onSort,
  searchField,
  filterOptions,
  filterLabel = 'Filter by status',
  filterPlaceholder = 'Select status...',
  onFilter,
  filterField,
  secondaryFilterOptions,
  secondaryFilterLabel = 'Filter by type',
  secondaryFilterPlaceholder = 'Select type...',
  onSecondaryFilter,
  secondaryFilterField,
  addButtonLabel,
  addButtonHref,
  onRowClick,
  getRowHref,
  keyExtractor,
  emptyMessage = 'No results found',
  noResultsMessage = 'No results found',
  hideHeader = false,
  testId,
}: GovUKTableProps<T>): React.ReactElement {
  const navigate = useNavigate();
  // Control ids are derived from a per-instance useId so the search/sort/filter <label htmlFor> bindings stay
  // unique and valid when more than one GovUKTable (or a second filter) renders on the same page (WCAG 4.1.1).
  const fieldId = useId();
  const [searchTerm, setSearchTerm] = useState('');
  const [sortValue, setSortValue] = useState(sortOptions?.[0]?.value || '');
  const [filterValue, setFilterValue] = useState('');
  const [secondaryFilterValue, setSecondaryFilterValue] = useState('');
  const [announcementRequest, setAnnouncementRequest] = useState(0);
  const [resultsAnnouncement, setResultsAnnouncement] = useState('');
  // GovUKTableName guarantees one of the two; the empty fallback only satisfies the type checker.
  const regionLabel = label ?? title ?? '';

  const filteredAndSortedData = useMemo(() => {
    let result = data;

    // Apply search filter.
    // Trimmed: a term pasted from a support ticket or mail client routinely carries leading/trailing
    // whitespace, and a plain `includes` would then match nothing at all — silently, since the user can see
    // the address is right. Whitespace is never meaningful in a search term. Guarding on the trimmed value
    // also stops a whitespace-only entry announcing itself as a search that matched everything.
    const lowerSearch = searchTerm.trim().toLowerCase();
    if (lowerSearch) {
      result = result.filter((item) => {
        const record = item as Record<string, unknown>;
        if (searchField) {
          const value = record[searchField as string];
          return typeof value === 'string' && value.toLowerCase().includes(lowerSearch);
        }

        return Object.values(record).some((value) => {
          if (typeof value === 'string') {
            return value.toLowerCase().includes(lowerSearch);
          }
          return false;
        });
      });
    }

    // Apply the filter, then the optional second one
    if (filterValue) {
      result = result.filter((item) => matchesFilter(item, filterOptions, filterValue, filterField));
    }
    if (secondaryFilterValue) {
      result = result.filter((item) => matchesFilter(item, secondaryFilterOptions, secondaryFilterValue, secondaryFilterField));
    }

    // Apply sorting
    if (sortValue) {
      const [field, direction] = sortValue.split('-');
      result = [...result].sort((a, b) => {
        const recordA = a as Record<string, unknown>;
        const recordB = b as Record<string, unknown>;

        let valueA: unknown;
        let valueB: unknown;

        // Handle common sort fields
        if (field === 'name') {
          // Try common name patterns
          valueA = recordA.name ?? `${recordA.firstName ?? ''} ${recordA.lastName ?? ''}`.trim();
          valueB = recordB.name ?? `${recordB.firstName ?? ''} ${recordB.lastName ?? ''}`.trim();
        } else if (field === 'status') {
          valueA = recordA.isActive;
          valueB = recordB.isActive;
        } else if (field === 'date') {
          valueA = recordA.startDate ?? recordA.createdAt ?? recordA.date;
          valueB = recordB.startDate ?? recordB.createdAt ?? recordB.date;
        } else {
          valueA = recordA[field];
          valueB = recordB[field];
        }

        // Compare values
        if (typeof valueA === 'string' && typeof valueB === 'string') {
          const comparison = valueA.toLowerCase().localeCompare(valueB.toLowerCase());
          return direction === 'desc' ? -comparison : comparison;
        }

        if (typeof valueA === 'boolean' && typeof valueB === 'boolean') {
          // Active items first for 'asc', inactive first for 'desc'
          if (valueA === valueB) return 0;
          if (direction === 'asc') return valueA ? -1 : 1;
          return valueA ? 1 : -1;
        }

        if (
          valueA instanceof Date ||
          valueB instanceof Date ||
          (typeof valueA === 'string' && typeof valueB === 'string' && !isNaN(Date.parse(valueA)) && !isNaN(Date.parse(valueB)))
        ) {
          const dateA = new Date(valueA as string).getTime();
          const dateB = new Date(valueB as string).getTime();
          return direction === 'desc' ? dateB - dateA : dateA - dateB;
        }

        return 0;
      });
    }

    return result;
  }, [data, searchTerm, searchField, sortValue, filterValue, filterField, filterOptions, secondaryFilterValue, secondaryFilterField, secondaryFilterOptions]);

  const selectedLabel = (options: FilterOption<T>[] | undefined, value: string): string | undefined => options?.find((option) => option.value === value)?.label;
  // A caller's emptyMessage describes an empty list ("No users yet."), which is untrue — and misleading in
  // the live region — when the list is full and a search simply matched nothing. Only this component knows
  // which case applies, because the search and filter values are its own state.
  // Excludes sortValue on purpose: sorting never removes rows, so it must not flip the message. Uses the
  // same searchTerm.trim() predicate as the filter above, so the message and the row set cannot disagree.
  // Deliberately guards on filterValue alone rather than `filterValue && filterField`: a caller filtering
  // in the parent via onFilter has no filterField, and is still narrowed.
  const isNarrowed = !!(searchTerm.trim() || filterValue || secondaryFilterValue);
  const noRowsMessage = isNarrowed ? noResultsMessage : emptyMessage;
  const resultCount = filteredAndSortedData.length;
  const emptyResultsAnnouncement = /[.!?]$/.test(noRowsMessage.trim()) ? noRowsMessage : `${noRowsMessage}.`;
  const pendingResultsAnnouncement = [
    resultCount === 0 ? emptyResultsAnnouncement : `${resultCount} ${resultCount === 1 ? 'result' : 'results'} found.`,
    searchTerm.trim() ? `Search term: ${searchTerm.trim()}.` : '',
    filterValue ? `${filterLabel}: ${selectedLabel(filterOptions, filterValue) ?? filterValue}.` : '',
    secondaryFilterValue ? `${secondaryFilterLabel}: ${selectedLabel(secondaryFilterOptions, secondaryFilterValue) ?? secondaryFilterValue}.` : '',
    sortValue ? `${sortLabel}: ${selectedLabel(sortOptions, sortValue) ?? sortValue}.` : '',
  ]
    .filter(Boolean)
    .join(' ');

  // Keep the live region in the DOM before any interaction, then update it shortly after the
  // user changes a search/filter. Clearing first also lets the Search button repeat the current
  // result status when the table has already updated while the user was typing.
  useEffect(() => {
    if (announcementRequest === 0) {
      return;
    }

    setResultsAnnouncement('');
    const timer = window.setTimeout(() => setResultsAnnouncement(pendingResultsAnnouncement), 250);
    return () => window.clearTimeout(timer);
  }, [announcementRequest, pendingResultsAnnouncement]);

  const requestResultsAnnouncement = () => setAnnouncementRequest((request) => request + 1);

  const handleSortChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = e.target.value;
    setSortValue(value);
    requestResultsAnnouncement();
    onSort?.(value);
  };

  const handleFilterChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = e.target.value;
    setFilterValue(value);
    requestResultsAnnouncement();
    onFilter?.(value);
  };

  const handleSecondaryFilterChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const value = e.target.value;
    setSecondaryFilterValue(value);
    requestResultsAnnouncement();
    onSecondaryFilter?.(value);
  };

  const handleRowClick = (item: T) => {
    if (getRowHref) {
      navigate(getRowHref(item));
    } else if (onRowClick) {
      onRowClick(item);
    }
  };

  const renderCellValue = (item: T, column: Column<T>): React.ReactNode => {
    if (column.render) {
      return column.render(item);
    }
    const value = (item as Record<string, unknown>)[column.key as string];
    return value as React.ReactNode;
  };

  const columnStyle = (column: Column<T>, style?: React.CSSProperties): React.CSSProperties | undefined => {
    const alignStyle = column.align ? { textAlign: column.align } : undefined;
    return alignStyle || style ? { ...alignStyle, ...style } : undefined;
  };

  return (
    <div data-testid={testId}>
      {(title || addButtonLabel) && (
        <div className="app-table-page-header">
          {title && <H1 data-testid={composeTestId(testId, 'title')}>{title}</H1>}
          {addButtonLabel && addButtonHref && (
            <div className="app-table-page-header__actions">
              <Button onClick={() => navigate(addButtonHref)} data-testid={composeTestId(testId, 'add-button')}>
                {addButtonLabel}
              </Button>
            </div>
          )}
        </div>
      )}

      {(searchable || sortOptions || filterOptions || secondaryFilterOptions) &&
        (() => {
          const hasSort = sortOptions && sortOptions.length > 0;
          const hasFilter = filterOptions && filterOptions.length > 0;
          const hasSecondaryFilter = secondaryFilterOptions && secondaryFilterOptions.length > 0;
          const hasBothDropdowns = hasSort && hasFilter;
          // Three controls + search fit a one-third grid; once a second filter is added, drop to quarters so
          // search + sort + two filters sit on one row.
          const dropdownTrack = hasSecondaryFilter ? FILTER_TRACKS.quarter : FILTER_TRACKS.third;
          const searchTrack = hasSecondaryFilter ? FILTER_TRACKS.quarter : hasBothDropdowns ? FILTER_TRACKS.third : FILTER_TRACKS.twoThirds;
          const tracks = [searchable && searchTrack, hasSort && dropdownTrack, hasFilter && dropdownTrack, hasSecondaryFilter && dropdownTrack].filter(Boolean).join(' ');

          return (
            <div className="govuk-table-filter" style={{ '--app-table-filter-columns': tracks } as React.CSSProperties}>
              {searchable && (
                <GovUKSearchInput
                  className="govuk-table-filter__search"
                  id={`${fieldId}-search`}
                  label={searchPlaceholder}
                  value={searchTerm}
                  onChange={(value) => {
                    setSearchTerm(value);
                    requestResultsAnnouncement();
                  }}
                  onSearch={requestResultsAnnouncement}
                  testId={composeTestId(testId, 'search')}
                />
              )}
              {hasSort && (
                <div className="govuk-table-filter__sort">
                  <label htmlFor={`${fieldId}-sort`} className="govuk-label">
                    {sortLabel}
                  </label>
                  <select id={`${fieldId}-sort`} data-testid={composeTestId(testId, 'sort')} className="govuk-select" value={sortValue} onChange={handleSortChange}>
                    {sortOptions.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              {hasFilter && (
                <div className="govuk-table-filter__filter">
                  <label htmlFor={`${fieldId}-filter`} className="govuk-label">
                    {filterLabel}
                  </label>
                  <select id={`${fieldId}-filter`} data-testid={composeTestId(testId, 'filter')} className="govuk-select" value={filterValue} onChange={handleFilterChange}>
                    <option value="">{filterPlaceholder}</option>
                    {filterOptions.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}
              {hasSecondaryFilter && (
                <div className="govuk-table-filter__filter">
                  <label htmlFor={`${fieldId}-secondary-filter`} className="govuk-label">
                    {secondaryFilterLabel}
                  </label>
                  <select
                    id={`${fieldId}-secondary-filter`}
                    data-testid={composeTestId(testId, 'secondary-filter')}
                    className="govuk-select"
                    value={secondaryFilterValue}
                    onChange={handleSecondaryFilterChange}>
                    <option value="">{secondaryFilterPlaceholder}</option>
                    {secondaryFilterOptions.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
              )}
            </div>
          );
        })()}

      <div className="govuk-visually-hidden" role="status" aria-live="polite" aria-atomic="true" data-testid={composeTestId(testId, 'results-status')}>
        {resultsAnnouncement}
      </div>

      {/* The table scrolls inside this box when it cannot fit its column, so the page itself never scrolls
          sideways (WCAG 1.4.10, BLUM-9350). */}
      <GovUKTableScroll label={regionLabel} testId={composeTestId(testId, 'scroll')}>
        {/* The header row goes through govuk-react's `head` prop, not in with the body rows: `Table` renders
            `children` into its `<tbody>`, so passing the header row as a child put the `<th>`s inside the
            body with no `<thead>` at all. That plus the missing `scope="col"` is why screen readers did not
            announce a column header alongside each cell (WCAG 1.3.1). Neither element carries any styling in
            govuk-react or in this repo, so the rendered result is visually identical. */}
        <Table
          head={
            hideHeader ? undefined : (
              <Table.Row>
                {columns.map((column) => (
                  <Table.CellHeader
                    key={String(column.key)}
                    scope="col"
                    className={[column.className, column.headerClassName].filter(Boolean).join(' ') || undefined}
                    style={columnStyle(column, column.headerStyle)}>
                    {column.header}
                  </Table.CellHeader>
                ))}
              </Table.Row>
            )
          }>
          {filteredAndSortedData.length === 0 ? (
            <Table.Row data-testid={composeTestId(testId, 'empty')}>
              <Table.Cell colSpan={columns.length}>{noRowsMessage}</Table.Cell>
            </Table.Row>
          ) : (
            filteredAndSortedData.map((item) => {
              const rowKey = keyExtractor(item);
              const rowTestId = composeTestId(testId, 'row', rowKey);
              return (
                <Table.Row key={rowKey} onClick={() => handleRowClick(item)} className={getRowHref || onRowClick ? 'govuk-table-row--clickable' : ''} data-testid={rowTestId}>
                  {columns.map((column) => (
                    <Table.Cell
                      key={String(column.key)}
                      className={[column.className, column.cellClassName].filter(Boolean).join(' ') || undefined}
                      style={columnStyle(column, column.cellStyle)}
                      data-testid={composeTestId(rowTestId, 'cell', String(column.key))}>
                      {renderCellValue(item, column)}
                    </Table.Cell>
                  ))}
                </Table.Row>
              );
            })
          )}
        </Table>
      </GovUKTableScroll>
    </div>
  );
}

// Helper components for common cell renderers
export const NameLink = ({ name, href, onClick, testId }: { name: string; href?: string; onClick?: () => void; testId?: string }): React.ReactElement => (
  <RouterLink
    to={href ?? '/'}
    className="govuk-link"
    data-testid={testId}
    onClick={(e: React.MouseEvent) => {
      if (onClick) {
        e.preventDefault();
        onClick();
      }
    }}>
    {name}
  </RouterLink>
);

export const StatusCell = ({ isActive, testId }: { isActive: boolean; testId?: string }): React.ReactElement => (
  <Tag tint={isActive ? 'GREEN' : 'YELLOW'} data-testid={testId}>
    {isActive ? 'Active' : 'Inactive'}
  </Tag>
);

export default GovUKTableComponent;

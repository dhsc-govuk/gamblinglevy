import React, { useEffect, useState, useMemo, useId } from 'react';
import { useNavigate, Link as RouterLink } from 'react-router-dom';
import { Button, Table, Tag } from 'govuk-react';
import { H1 } from '../GovUKTypography';
import { PageHeaderContainer, PageHeaderActions } from '../../../styles/govuk-global';
import SearchIcon from '../../Logos/SearchIcon';
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

export interface FilterOption {
  value: string;
  label: string;
}

export interface GovUKTableProps<T> {
  data: T[];
  columns: Column<T>[];
  title?: string;
  searchPlaceholder?: string;
  searchable?: boolean;
  sortOptions?: FilterOption[];
  sortLabel?: string;
  onSort?: (value: string) => void;
  searchField?: keyof T | string;
  filterOptions?: FilterOption[];
  filterLabel?: string;
  filterPlaceholder?: string;
  onFilter?: (value: string) => void;
  filterField?: keyof T | string;
  // Optional second exact-match filter (e.g. filter by type AND by status on the same list). All four
  // secondary* props are additive — when omitted the table behaves exactly as before (single filter).
  secondaryFilterOptions?: FilterOption[];
  secondaryFilterLabel?: string;
  secondaryFilterPlaceholder?: string;
  onSecondaryFilter?: (value: string) => void;
  secondaryFilterField?: keyof T | string;
  addButtonLabel?: string;
  addButtonHref?: string;
  onRowClick?: (item: T) => void;
  getRowHref?: (item: T) => string;
  keyExtractor: (item: T) => string;
  emptyMessage?: string;
  hideHeader?: boolean;
  testId?: string;
}

function GovUKTableComponent<T>({
  data,
  columns,
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

  const filteredAndSortedData = useMemo(() => {
    let result = data;

    // Apply search filter
    if (searchTerm) {
      const lowerSearch = searchTerm.toLowerCase();
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

    // Apply status/field filter
    if (filterValue && filterField) {
      result = result.filter((item) => {
        const record = item as Record<string, unknown>;
        const fieldValue = record[filterField as string];
        return fieldValue === filterValue;
      });
    }

    // Apply the optional second exact-match filter
    if (secondaryFilterValue && secondaryFilterField) {
      result = result.filter((item) => {
        const record = item as Record<string, unknown>;
        const fieldValue = record[secondaryFilterField as string];
        return fieldValue === secondaryFilterValue;
      });
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
  }, [data, searchTerm, searchField, sortValue, filterValue, filterField, secondaryFilterValue, secondaryFilterField]);

  const selectedLabel = (options: FilterOption[] | undefined, value: string): string | undefined => options?.find((option) => option.value === value)?.label;
  const resultCount = filteredAndSortedData.length;
  const emptyResultsAnnouncement = /[.!?]$/.test(emptyMessage.trim()) ? emptyMessage : `${emptyMessage}.`;
  const pendingResultsAnnouncement = [
    resultCount === 0 ? emptyResultsAnnouncement : `${resultCount} ${resultCount === 1 ? 'result' : 'results'} found.`,
    searchTerm ? `Search term: ${searchTerm}.` : '',
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
        <PageHeaderContainer>
          {title && <H1 data-testid={composeTestId(testId, 'title')}>{title}</H1>}
          {addButtonLabel && addButtonHref && (
            <PageHeaderActions>
              <Button onClick={() => navigate(addButtonHref)} data-testid={composeTestId(testId, 'add-button')}>
                {addButtonLabel}
              </Button>
            </PageHeaderActions>
          )}
        </PageHeaderContainer>
      )}

      {(searchable || sortOptions || filterOptions || secondaryFilterOptions) &&
        (() => {
          const hasSort = sortOptions && sortOptions.length > 0;
          const hasFilter = filterOptions && filterOptions.length > 0;
          const hasSecondaryFilter = secondaryFilterOptions && secondaryFilterOptions.length > 0;
          const hasBothDropdowns = hasSort && hasFilter;
          // Three controls + search fit a one-third grid; once a second filter is added, drop to quarters so
          // search + sort + two filters sit on one row.
          const dropdownColumnClass = hasSecondaryFilter ? 'govuk-grid-column-one-quarter' : 'govuk-grid-column-one-third';
          const searchColumnClass = hasSecondaryFilter ? 'govuk-grid-column-one-quarter' : hasBothDropdowns ? 'govuk-grid-column-one-third' : 'govuk-grid-column-two-thirds';

          return (
            <div className="govuk-grid-row govuk-table-filter">
              {searchable && (
                <div className={searchColumnClass}>
                  <label htmlFor={`${fieldId}-search`} className="govuk-label">
                    {searchPlaceholder}
                  </label>
                  <div className="govuk-table-filter__search">
                    <input
                      id={`${fieldId}-search`}
                      data-testid={composeTestId(testId, 'search')}
                      type="search"
                      value={searchTerm}
                      onChange={(e) => {
                        setSearchTerm(e.target.value);
                        requestResultsAnnouncement();
                      }}
                      className="govuk-input govuk-table-filter__input"
                    />
                    <button
                      type="button"
                      className="govuk-table-filter__search-button"
                      aria-label="Search"
                      data-testid={composeTestId(testId, 'search-button')}
                      onClick={requestResultsAnnouncement}>
                      <SearchIcon />
                    </button>
                  </div>
                </div>
              )}
              {hasSort && (
                <div className={`${dropdownColumnClass} govuk-table-filter__sort`}>
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
                <div className={`${dropdownColumnClass} govuk-table-filter__filter`}>
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
                <div className={`${dropdownColumnClass} govuk-table-filter__filter`}>
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

      <div
        className="govuk-visually-hidden"
        role="status"
        aria-live="polite"
        aria-atomic="true"
        data-testid={composeTestId(testId, 'results-status')}>
        {resultsAnnouncement}
      </div>

      <Table>
        {!hideHeader && (
          <Table.Row>
            {columns.map((column) => (
              <Table.CellHeader
                key={String(column.key)}
                className={[column.className, column.headerClassName].filter(Boolean).join(' ') || undefined}
                style={columnStyle(column, column.headerStyle)}>
                {column.header}
              </Table.CellHeader>
            ))}
          </Table.Row>
        )}
        {filteredAndSortedData.length === 0 ? (
          <Table.Row data-testid={composeTestId(testId, 'empty')}>
            <Table.Cell colSpan={columns.length}>{emptyMessage}</Table.Cell>
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

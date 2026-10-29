import { describe, expect, it } from 'vitest';
import type { ReportHistoryRow } from '../display';
import { REPORT_HISTORY_FILTERS } from './filter';

const row = (collectionId: string, type: ReportHistoryRow['type'], status: ReportHistoryRow['status']): ReportHistoryRow => ({
  collectionId,
  displayName: collectionId,
  type,
  status,
  reportingPeriodStart: '',
  submittedAt: '',
});

const ROWS: ReportHistoryRow[] = [
  row('quarterly-submitted', 'quarterly_report', 'completed'),
  row('quarterly-overdue', 'quarterly_report', 'overdue'),
  row('maturity-submitted', 'maturity_assessment', 'completed'),
  row('maturity-overdue', 'maturity_assessment', 'overdue'),
];

const ids = (rows: ReportHistoryRow[]): string[] => rows.map((r) => r.collectionId);

describe('REPORT_HISTORY_FILTERS', () => {
  it('keeps every value distinct', () => {
    const values = REPORT_HISTORY_FILTERS.map((option) => option.value);
    expect(new Set(values).size).toBe(values.length);
  });

  it('offers the ticket options in order', () => {
    expect(REPORT_HISTORY_FILTERS.map((option) => option.label)).toEqual(['Type: Quarterly report', 'Type: Maturity assessment', 'Status: Submitted', 'Status: Overdue']);
  });
});

// GovUKTable applies the selected option's `matches` to every row, so that is what each option is tested through.
const apply = (value: string): ReportHistoryRow[] => ROWS.filter((r) => REPORT_HISTORY_FILTERS.find((option) => option.value === value)?.matches?.(r));

describe('REPORT_HISTORY_FILTERS matches', () => {
  it.each([
    ['quarterly_report', ['quarterly-submitted', 'quarterly-overdue']],
    ['maturity_assessment', ['maturity-submitted', 'maturity-overdue']],
    ['completed', ['quarterly-submitted', 'maturity-submitted']],
    ['overdue', ['quarterly-overdue', 'maturity-overdue']],
  ])('keeps only the rows matching %s', (value, expected) => {
    expect(ids(apply(value))).toEqual(expected);
  });
});

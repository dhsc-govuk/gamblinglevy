import { afterEach, describe, expect, it, vi } from 'vitest';
import { currentFinancialYear, financialYearEndYear, financialYearLabel, financialYearOptions, nextFinancialYear } from './financialYear';

// Dates are built with the local-time constructor (month is zero-based) because the helpers read local time, so
// these cases hold in any time zone the suite runs in.

const MALFORMED = ['', '2026', '26/27', '2026/2027', '2026-27', ' 2026/27', '2026/27 ', '  26/27', 'abcd/ef', '2026/28', '2026/26', '2099/99'];

afterEach(() => {
  vi.useRealTimers();
});

describe('currentFinancialYear', () => {
  it.each([
    [new Date(2026, 0, 1), '2025/26'],
    [new Date(2026, 2, 31, 23, 59, 59), '2025/26'],
    [new Date(2026, 3, 1), '2026/27'],
    [new Date(2026, 11, 31), '2026/27'],
    [new Date(2000, 0, 1), '1999/00'],
    [new Date(2099, 5, 1), '2099/00'],
    [new Date(2009, 3, 1), '2009/10'],
  ])('puts %s in %s, with the year starting on 1 April', (date, expected) => {
    expect(currentFinancialYear(date)).toBe(expected);
  });

  it('defaults to today', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2027, 2, 31));

    expect(currentFinancialYear()).toBe('2026/27');
  });
});

describe('nextFinancialYear', () => {
  it.each([
    ['2026/27', '2027/28'],
    ['2098/99', '2099/00'],
    ['2099/00', '2100/01'],
  ])('follows %s with %s', (financialYear, expected) => {
    expect(nextFinancialYear(financialYear)).toBe(expected);
  });

  it.each(MALFORMED)('is empty for the malformed year %j', (financialYear) => {
    expect(nextFinancialYear(financialYear)).toBe('');
  });
});

describe('financialYearEndYear', () => {
  it.each([
    ['2026/27', 2027],
    ['2099/00', 2100],
  ])('ends %s in %s', (financialYear, expected) => {
    expect(financialYearEndYear(financialYear)).toBe(expected);
  });

  it.each(MALFORMED)('is null for the malformed year %j', (financialYear) => {
    expect(financialYearEndYear(financialYear)).toBeNull();
  });
});

describe('financialYearOptions', () => {
  it('lists the year before the current one through four years after it', () => {
    expect(financialYearOptions(new Date(2026, 9, 14))).toEqual(['2025/26', '2026/27', '2027/28', '2028/29', '2029/30', '2030/31'].map((year) => ({ value: year, label: year })));
  });

  it('follows the financial year, not the calendar year, between January and March', () => {
    expect(financialYearOptions(new Date(2027, 1, 1)).map((option) => option.value)).toEqual(['2025/26', '2026/27', '2027/28', '2028/29', '2029/30', '2030/31']);
  });
});

describe('financialYearLabel', () => {
  it('shows a year as stored', () => {
    expect(financialYearLabel('2026/27')).toBe('2026/27');
  });

  it.each([null, ''])('renders %j as an em dash', (financialYear) => {
    expect(financialYearLabel(financialYear)).toBe('—');
  });
});

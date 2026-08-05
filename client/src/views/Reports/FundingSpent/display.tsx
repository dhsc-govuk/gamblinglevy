import { formatGBP } from '../../../helpers/currency';
import { formatGovUkDate } from '../../Admin/Collections/display';
import type { FundingSpendDto, FundingSpendQuarterDto, FundingSpendSummaryDto } from '../queries';

// Presentation/copy helpers for the "Record funding spent" section (BLUM-6612). Keeps the page
// components thin, mirroring views/Reports/display.tsx.

// Whether the "Forecast spend" step applies: true when there are quarters after the reporting quarter
// left to forecast. False for a Q4 report (nothing left to forecast) — the Forecast screen is then
// skipped and the user goes straight from Activity spending to Check.
export const hasForecastQuarters = (funding?: FundingSpendDto): boolean => (funding?.remainingQuarters.length ?? 0) > 0;

export const SPENDING_INTRO = [
  'Enter how much gambling levy funding you spent on each activity this quarter.',
  'If you did not spend any levy funding on an activity, enter £0.',
  'If an activity was also funded by other sources, only record the amount funded by the levy here.',
];

export const FORECAST_INTRO = [
  'Enter how much gambling levy funding you expect to spend on each activity.',
  'If you do not plan to spend any levy funding on an activity, enter £0.',
];

// "Summary for all activities" rows. The "Expected spend for the year" row is only shown on the Check
// screen. "Total budget for the year" / "Budget remaining" are intentionally omitted — there is no
// backend source for an LA annual budget yet (follow-up).
export const summaryRows = (summary: FundingSpendSummaryDto, includeExpectedForYear: boolean): { label: string; value: string }[] => [
  { label: 'Total grant allocation', value: formatGBP(summary.totalGrantAllocation) },
  { label: 'Spend to date', value: formatGBP(summary.spendToDate) },
  { label: 'Total spend this quarter', value: formatGBP(summary.totalSpendThisQuarter) },
  ...(includeExpectedForYear ? [{ label: 'Expected spend for the year', value: formatGBP(summary.expectedSpendForYear) }] : []),
];

// "Expected spend for Quarter 2, (1 July 2026 to 30 September 2026)".
export const forecastQuarterLabel = (quarter: FundingSpendQuarterDto): string =>
  `Expected spend for Quarter ${quarter.quarter}, (${formatGovUkDate(quarter.start)} to ${formatGovUkDate(quarter.end)})`;

// Money entered into the £-prefixed inputs: digits with optional 2dp (commas / £ / spaces stripped). £0 is
// allowed (the design tells users to enter £0). Required is only enforced on Continue / complete.
const MONEY_PATTERN = /^\d+(\.\d{1,2})?$/;

// Matches the backend FundingSpendValidator cap: keeps amounts within the numeric(18,2) column.
const MAX_AMOUNT = 1_000_000_000;

const normalizeAmount = (value: string): string => (value ?? '').replace(/[£,\s]/g, '');

export const validateMoney = (value: string, { required, label }: { required: boolean; label: string }): string | null => {
  const normalized = normalizeAmount(value);
  if (!normalized) {
    return required ? `Enter ${label}` : null;
  }
  if (!MONEY_PATTERN.test(normalized)) {
    return `Enter ${label} as a number, like 1500`;
  }
  if (Number(normalized) > MAX_AMOUNT) {
    return 'Enter an amount of £1,000,000,000 or less';
  }
  return null;
};

// The numeric value to send to the API: null when empty or invalid (validation blocks invalid before save).
export const parseAmount = (value: string): number | null => {
  const normalized = normalizeAmount(value);
  if (!normalized || !MONEY_PATTERN.test(normalized)) {
    return null;
  }
  return Number(normalized);
};

// Renders a saved numeric amount back into an input string ('' when not yet answered).
export const amountToInput = (amount: number | null): string => (amount == null ? '' : String(amount));

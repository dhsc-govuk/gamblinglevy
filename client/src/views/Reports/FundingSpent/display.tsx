import type { ReactElement } from 'react';
import { formatGBP, isPenceAmount, MAX_MONEY_AMOUNT, MONEY_PATTERN, normalizeAmount } from '../../../helpers/currency';
import { formatGovUkDate } from '../../Admin/Collections/display';
import type { FundingSpendActivityDto, FundingSpendDto, FundingSpendQuarterDto, FundingSpendSummaryDto } from '../queries';

// Presentation/copy helpers for the "Record funding spent" section (BLUM-6612). Keeps the page
// components thin, mirroring views/Reports/display.tsx.

// Whether the "Forecast spend" step applies: true when there are quarters after the reporting quarter
// left to forecast. False for a Q4 report (nothing left to forecast) — the Forecast screen is then
// skipped and the user goes straight from Activity spending to Check.
export const hasForecastQuarters = (funding?: FundingSpendDto): boolean => (funding?.remainingQuarters.length ?? 0) > 0;

// The activities the "Activity spending" screen asks for an actual. funding.activities also carries Planned
// activities, which appear on "Forecast spend" only (BLUM-7672) — anything reading or writing actual spend must
// go through this, or it renders an input the server discards on save.
export const actualSpendActivities = (funding: FundingSpendDto): FundingSpendActivityDto[] => funding.activities.filter((activity) => !activity.forecastOnly);

export const SPENDING_INTRO = [
  'Enter how much gambling levy funding you spent on each activity this quarter.',
  'If you did not spend any levy funding on an activity, enter £0.',
  'If an activity was also funded by other sources, only record the amount funded by the levy here.',
];

export const FORECAST_INTRO = [
  'Enter how much gambling levy funding you expect to spend on each activity.',
  'If you do not plan to spend any levy funding on an activity, enter £0.',
];

// Why the live "Total spend this quarter" cannot be shown. Both reasons are things validateMoney also
// rejects, so each message is worded to lead into the error the user meets on Continue.
export type LiveTotalIssue = 'not-a-number' | 'over-cap';

export interface LiveTotal {
  total: number | null;
  issue: LiveTotalIssue | null;
}

// Deliberately not phrased as an imperative ("Enter every amount as…") — that reads as an inline error, and
// GDS keeps error messages for after the user submits. This only explains the em dash in the meantime.
export const TOTAL_UNAVAILABLE_MESSAGE: Record<LiveTotalIssue, string> = {
  'not-a-number': 'The totals will appear when every amount is a number, like 1500.',
  'over-cap': 'The totals will appear when every amount is £1,000,000,000 or less.',
};

interface FundingSummaryRow {
  label: string;
  value: ReactElement | string;
}

const unavailableAmount = (): ReactElement => (
  <>
    {formatGBP(null)}
    <span className="govuk-visually-hidden"> Not available</span>
  </>
);

// "Summary for all activities" rows, in the order the Figma review's section 14 lists them.
//
// "Allocations remaining for the year" is the API's own remainingBudget (totalGrantAllocation - totalSpendToDate),
// NOT a client derivation. Deriving it as expectedSpendForYear - spendToDate - totalSpendThisQuarter looks
// right and is not: the API defines expectedSpendForYear = totalSpendToDate + totalForecast and
// totalSpendToDate = spendToDate + totalSpendThisQuarter, so that expression cancels down to totalForecast
// exactly. It could never go negative, ignored the grant allocation entirely, and read £0 on every Q4 report
// (no quarters left to forecast). On the Activity spending screen the row still has to move with the live
// current-quarter total, so the saved figure is re-based onto it there.
//
// There is no "Total budget for the year" row: the API holds no LA annual budget, so it only ever rendered "—",
// and BLUM-8157 removed it.
//
// The Check screen passes the server's summary straight through. The Activity spending screen recomputes the
// current quarter's total from the inputs and passes it as liveSpendThisQuarter, or null when it cannot be
// known (see liveTotalSpendThisQuarter) — which formatGBP renders as "—". Omitting the argument entirely
// means "use the saved figure"; passing null means "it is currently unknowable".
export const summaryRows = (
  summary: FundingSpendSummaryDto,
  includeExpectedForYear: boolean,
  includeAllocationsRemaining = false,
  liveSpendThisQuarter?: number | null
): FundingSummaryRow[] => {
  const spendThisQuarter = liveSpendThisQuarter === undefined ? summary.totalSpendThisQuarter : liveSpendThisQuarter;

  // Re-based onto the live total when there is one: the server's remainingBudget counts the SAVED actual for
  // this quarter, so swap that out for what is on screen. Null while the live total cannot be known.
  const allocationsRemaining = spendThisQuarter === null ? null : summary.remainingBudget + (summary.totalSpendThisQuarter - spendThisQuarter);

  return [
    { label: 'Total grant allocation', value: formatGBP(summary.totalGrantAllocation) },
    { label: 'Spend to date', value: formatGBP(summary.spendToDate) },
    {
      label: 'Total spend this quarter',
      // The em dash announces as nothing on some screen readers and as nil on others, and the status line that
      // explains it is a sibling of the list with no programmatic tie to this cell — so the cell says so itself
      // for anyone who reads the row later, after the live announcement has passed.
      value: spendThisQuarter === null ? unavailableAmount() : formatGBP(spendThisQuarter),
    },
    ...(includeExpectedForYear ? [{ label: 'Expected spend for the year', value: formatGBP(summary.expectedSpendForYear) }] : []),
    ...(includeAllocationsRemaining
      ? [
          {
            label: 'Allocations remaining for the year',
            value: allocationsRemaining === null ? unavailableAmount() : formatGBP(allocationsRemaining),
          },
        ]
      : []),
  ];
};

// "Expected spend for Quarter 2, (1 July 2026 to 30 September 2026)".
export const forecastQuarterLabel = (quarter: FundingSpendQuarterDto): string =>
  `Expected spend for Quarter ${quarter.quarter}, (${formatGovUkDate(quarter.start)} to ${formatGovUkDate(quarter.end)})`;

// Money entered into the £-prefixed inputs: whole pounds only (thousands separators read through). £0 is allowed (the
// design tells users to enter £0). Required is only enforced on Continue / complete. The 13 August Question
// Protocol dropped the "amount of 1 or more" rule and banned both pence and a typed "£" (BLUM-7620) — the £ rule
// is client-only here, because the API receives an already-parsed number and can never see the symbol.
//
// The rules themselves now live in helpers/currency (BLUM-7860): the activity record's Currency questions enforce
// the same ones, and only the message wording differs. This screen keeps its own, which name the activity and
// quarter — an error summary listing several amounts has to say which one to fix.

// The protocol words each amount's messages around two subjects: `required` reads after "Enter " and mid-sentence
// ("the amount spent on Outreach"), `sentence` starts one ("Amount spent on Outreach"). Mirrors the pair
// FundingSpendValidator passes to its own ValidateAmount.
export interface MoneySubject {
  required: string;
  sentence: string;
}

export const moneySubjectForActivity = (activityName: string): MoneySubject => ({
  required: `the amount spent on ${activityName}`,
  sentence: `Amount spent on ${activityName}`,
});

export const OTHER_SPEND_SUBJECT: MoneySubject = { required: 'the other spend this quarter', sentence: 'Other spend this quarter' };

export const moneySubjectForForecast = (activityName: string, quarter: number): MoneySubject => ({
  required: `expected spend on ${activityName} for Quarter ${quarter}`,
  sentence: `Expected spend on ${activityName} for Quarter ${quarter}`,
});

export const moneySubjectForOtherForecast = (quarter: number): MoneySubject => ({
  required: `the amount of other planned spend for Quarter ${quarter}`,
  sentence: `Other planned spend for Quarter ${quarter}`,
});

export const validateMoney = (value: string, { required, subject }: { required: boolean; subject: MoneySubject }): string | null => {
  const raw = value ?? '';
  const normalized = normalizeAmount(raw);
  if (!normalized) {
    return required ? `Enter ${subject.required}` : null;
  }
  if (raw.includes('£')) {
    return `${subject.sentence} must not include £`;
  }
  if (!MONEY_PATTERN.test(normalized)) {
    // A well-formed amount with pence gets the protocol's specific message; anything else is not a number.
    return isPenceAmount(normalized) ? `${subject.sentence} must be a whole number and not include pence` : `Enter a number for ${subject.required}, for example 0 or 1500`;
  }
  if (Number(normalized) > MAX_MONEY_AMOUNT) {
    // Named like the rest so an error summary listing several activities says which one to fix.
    return `${subject.sentence} must be £1,000,000,000 or less`;
  }
  return null;
};

// Renders a saved numeric amount back into an input string ('' when not yet answered).
export const amountToInput = (amount: number | null): string => (amount == null ? '' : String(amount));

// The raw input string for one activity. The only place that knows how the Activity spending screen keys its
// `actuals` map — validation, the save payload and the live total below all read through it, and they parse
// the result differently, so the lookup is the piece that has to stay in step.
export const actualForActivity = (actuals: Record<string, string>, activity: FundingSpendActivityDto): string => actuals[activity.activityId] ?? '';

// Amounts are whole pounds now (BLUM-7620), so there is no longer a mid-entry state that is a prefix of a valid
// amount but not itself valid — this is MONEY_PATTERN. Kept as its own constant because the two answer different
// questions: what may be saved, and what may be counted into the live total while the user is still typing.
const PARTIAL_MONEY_PATTERN = /^\d+$/;

// What one input contributes to the live total below, or null when the entry cannot stand for a savable
// amount at all. parseAmount is the wrong tool here: it answers "what would be saved", so it returns null
// mid-keystroke and the caller's `?? 0` would drop the field to £0 while the user is still typing it.
// Blank is 0 rather than null on purpose — an unanswered field is "nothing spent yet", and nulling the
// total until every field is filled would defeat the live total for the whole session.
const amountForTotal = (value: string): number | LiveTotalIssue => {
  const normalized = normalizeAmount(value);
  if (!normalized) {
    return 0;
  }
  if (!PARTIAL_MONEY_PATTERN.test(normalized)) {
    return 'not-a-number';
  }
  // isFinite is still load-bearing: a long enough digit paste reaches Infinity before the cap comparison,
  // and "too big" is what that is.
  const parsed = Number(normalized);
  return Number.isFinite(parsed) && parsed <= MAX_MONEY_AMOUNT ? parsed : 'over-cap';
};

// The live "Total spend this quarter" for the Activity spending screen (BLUM-7465). The server's
// summary.totalSpendThisQuarter is only recomputed on save, so the box read £0 until the user had
// continued and come back. Mirrors GetFundingSpendAsync's formula — the per-activity actuals plus the
// single "other" line — but returns null if any entry is unusable, so the row renders formatGBP's "—"
// instead of confidently showing a total that silently excludes a field, or one the validator will
// reject on Continue. Nothing here decides what gets saved; that stays with parseAmount.
export const liveTotalSpendThisQuarter = (activities: FundingSpendActivityDto[], actuals: Record<string, string>, otherSpend: string): LiveTotal => {
  const amounts = [...activities.map((activity) => actualForActivity(actuals, activity)), otherSpend].map(amountForTotal);
  // First in reading order, so the message names the problem the user meets first going down the page.
  const issue = amounts.find((amount): amount is LiveTotalIssue => typeof amount === 'string') ?? null;
  const usable = amounts.filter((amount): amount is number => typeof amount === 'number');
  return issue ? { total: null, issue } : { total: usable.reduce((total, amount) => total + amount, 0), issue: null };
};

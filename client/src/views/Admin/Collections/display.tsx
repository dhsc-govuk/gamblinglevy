import type { TagColour } from '../../../components/GovUKComponents';
import type { CollectionReminderDto, CollectionStatus, CollectionType } from './queries';

// Shared data-collection presentation/copy helpers — single source of truth for status labels,
// tag colours and date/record formatting used across the list, view and form screens.

export const STATUS_LABEL: Record<CollectionStatus, string> = {
  draft: 'Draft',
  scheduled: 'Scheduled',
  open: 'Open',
  closed: 'Closed',
};

// `closed` deliberately has no tag colour — the list/view render plain "Closed" text for it.
export const STATUS_TAG_COLOUR: Record<Exclude<CollectionStatus, 'closed'>, TagColour> = {
  draft: 'grey',
  scheduled: 'blue',
  open: 'green',
};

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

// Formats a DateOnly string ("2026-12-31") as "31 December 2026". Returns "—" when null/empty.
export const formatGovUkDate = (iso: string | null): string => {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  return `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]} ${date.getUTCFullYear()}`;
};

// Formats an ISO timestamp for record info ("31 December 2026 at 2:05pm"). Returns "—" when null/empty.
export const formatDateTime = (iso: string | null): string => {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return iso;
  }
  let hours = date.getHours();
  const minutes = date.getMinutes().toString().padStart(2, '0');
  const period = hours >= 12 ? 'pm' : 'am';
  hours = hours % 12 || 12;
  return `${date.getDate()} ${MONTHS[date.getMonth()]} ${date.getFullYear()} at ${hours}:${minutes}${period}`;
};

export const typeLabel = (type: CollectionType | null): string => {
  if (type == null) {
    return '—';
  }
  return type === 'maturity_assessment' ? 'Maturity assessment' : 'Quarterly report';
};

// Yes/No answers are nullable on drafts — null means "not answered yet" and renders as an em-dash,
// never as "No".
export const yesNoLabel = (value: boolean | null): string => {
  if (value == null) {
    return '—';
  }
  return value ? 'Yes' : 'No';
};

// UK financial-year options for the create form, centred on the current calendar year (~6 years).
// Value/label format is "2026/27" (start year / two-digit next year), matching the list view.
export const financialYearOptions = (): { value: string; label: string }[] => {
  const currentYear = new Date().getFullYear();
  const startYear = currentYear - 1;
  return Array.from({ length: 6 }, (_unused, index) => {
    const year = startYear + index;
    const next = ((year + 1) % 100).toString().padStart(2, '0');
    const label = `${year}/${next}`;
    return { value: label, label };
  });
};

// UK fiscal quarters for the quarterly-report type.
// NOTE this mapping is the confirmed-with-design assumption.
export const quarterOptions = (): { value: string; label: string }[] => [
  { value: '1', label: 'Quarter 1 (1 April to 30 June)' },
  { value: '2', label: 'Quarter 2 (1 July to 30 September)' },
  { value: '3', label: 'Quarter 3 (1 October to 31 December)' },
  { value: '4', label: 'Quarter 4 (1 January to 31 March)' },
];

// Full quarter label for read-only screens — derived from quarterOptions() so the select, the
// check-answers step and the view page can never disagree on the date range. Accepts the DTO's
// number and the form state's string; both null and the unanswered empty string render as an em-dash.
export const quarterLabel = (quarter: number | string | null): string => {
  if (quarter == null || quarter === '') {
    return '—';
  }
  return quarterOptions().find((option) => option.value === String(quarter))?.label ?? `Quarter ${quarter}`;
};

export const financialYearLabel = (financialYear: string | null): string => financialYear || '—';

// Reporting-period bounds for a financial year (and optional quarter), used by the wizard's window step
// hint. Financial year value is "2026/27" (UK fiscal year starting 1 April 2026). Quarters follow the
// quarterOptions mapping: Q1 Apr–Jun and Q2 Jul–Sep and Q3 Oct–Dec fall in the start year; Q4 Jan–Mar
// falls in the following calendar year. With no quarter the whole fiscal year (1 Apr–31 Mar) is used.
// Returns formatted GOV.UK date strings, or em-dashes when the financial year is not yet chosen.
export const reportingPeriod = (financialYear: string, quarter: string): { start: string; end: string } => {
  const startYear = Number(financialYear.split('/')[0]);
  if (!Number.isInteger(startYear)) {
    return { start: '—', end: '—' };
  }
  const nextYear = startYear + 1;
  const iso = (year: number, month: number, day: number): string => `${year}-${month.toString().padStart(2, '0')}-${day.toString().padStart(2, '0')}`;

  const bounds: Record<string, { start: string; end: string }> = {
    '1': { start: iso(startYear, 4, 1), end: iso(startYear, 6, 30) },
    '2': { start: iso(startYear, 7, 1), end: iso(startYear, 9, 30) },
    '3': { start: iso(startYear, 10, 1), end: iso(startYear, 12, 31) },
    '4': { start: iso(nextYear, 1, 1), end: iso(nextYear, 3, 31) },
  };
  const period = bounds[quarter] ?? { start: iso(startYear, 4, 1), end: iso(nextYear, 3, 31) };
  return { start: formatGovUkDate(period.start), end: formatGovUkDate(period.end) };
};

export const reminderLabel = (reminder: CollectionReminderDto): string =>
  reminder.offsetType === 'after_open' ? `${reminder.days} days after the collection opens` : `${reminder.days} days before the submission deadline`;

// Draft, scheduled and open collections can still be edited; closed ones are read-only.
export const canEditStatus = (status: CollectionStatus): boolean => status === 'draft' || status === 'scheduled' || status === 'open';

export const isDraftStatus = (status: CollectionStatus): boolean => status === 'draft';

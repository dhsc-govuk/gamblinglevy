import React from 'react';
import type { TagColour } from '../../../components/GovUKComponents';
import type { CollectionStatus } from '../Collections/queries';
import type { SubmissionStatus } from './queries';
import { STATUS_LABEL as COLLECTION_STATUS_LABEL, formatGovUkDate } from '../Collections/display';

// Submission-status presentation for the DHSC submission-visibility screens — the single source of truth for the
// status labels and tag colours. Date/type formatting is shared with the data-collections screens.
export { formatGovUkDate, formatDateTime, typeLabel } from '../Collections/display';

export const REPORT_COLLECTION_STATUS_LABEL: Record<CollectionStatus, string> = {
  ...COLLECTION_STATUS_LABEL,
  draft: 'Draft',
};

export const SUBMISSION_STATUS_LABEL: Record<SubmissionStatus, string> = {
  not_started: 'Not started',
  in_progress: 'In progress',
  completed: 'Completed',
  overdue: 'Overdue',
};

// GOV.UK tag colours. Completed = blue (distinct from a collection's green "Open"), in progress = yellow,
// not started = grey, overdue = red.
export const SUBMISSION_STATUS_TAG_COLOUR: Record<SubmissionStatus, TagColour> = {
  not_started: 'grey',
  in_progress: 'yellow',
  completed: 'blue',
  overdue: 'red',
};

// The period a report covers, as a summary-list value:
//
//   Quarter 2
//   (1 July 2026 to 30 September 2026)
//
// This is the REPORTING PERIOD (derived server-side from financial year + quarter), never the collection window —
// the two differ whenever a collection was published with "window same as reporting period" = No. Shared by the
// DHSC submission detail and the LA report intro so the two surfaces cannot drift (BLUM-7079).
//
// Not the only reporting-period formatter: views/Reports/display.tsx keeps `reportName`, the string form used for
// a report's display name. That stays separate because it is a string, not a node — GovUKTable sorts and searches
// over raw string fields. Change the wording in both together. (A third, `reportingPeriodLabel`, rendered the
// single-line "Quarter 1 (1 April 2026 to 30 June 2026)" for the bold inline header line; it went with that line.)
export const reportingPeriodValue = (quarter: number | null, start: string | null, end: string | null): React.ReactNode => {
  // Both ends required, deliberately: a half-known period would render "1 July 2026 to —", which reads as a real
  // range with a broken end rather than as missing data. The server derives start and end together from financial
  // year + quarter, so this is belt-and-braces.
  const hasDates = !!start && !!end;

  // Nothing answered at all. Only here do we fall back to an em dash — a draft collection can have an answered
  // quarter with no derived period yet (UpdateCollectionCommand only derives one once the financial year is set),
  // and showing "—" for that would hide the half of the answer we do have.
  if (quarter == null && !hasDates) {
    return '—';
  }

  const dates = `${formatGovUkDate(start)} to ${formatGovUkDate(end)}`;
  if (quarter == null) {
    return dates;
  }
  if (!hasDates) {
    return `Quarter ${quarter}`;
  }

  return (
    <>
      Quarter {quarter}
      <br />({dates})
    </>
  );
};

// Maps a quarterly-report API error to a page-level message plus per-question field errors (keyed by
// question code). Mirrors views/Activities/messages.ts and the backend SubmissionErrorFactory envelopes.

// The two Reports surfaces share every rule about a failed read except which verdicts exist — see below.
import { reportFailureMessage, reportLoadVerdict, reportPageTitle } from '../Admin/Reports/messages';
import { httpStatusOf, retryAfterOf } from '../../helpers/httpErrors';
import type { OfflineUploadInvalidDto } from './queries';

export interface ReportError {
  message: string;
  fieldErrors: Record<string, string>;
}

interface ReportErrorEnvelope {
  error?: string;
  message?: string;
  answerErrors?: { questionCode: string; message: string }[];
}

export const mapReportError = (error: unknown): ReportError => {
  const response = (error as { response?: { status?: number; data?: unknown } } | undefined)?.response;
  const status = response?.status;
  const data = response?.data as ReportErrorEnvelope | string | undefined;
  const envelope = typeof data === 'object' ? data : undefined;
  const code = envelope?.error;

  if (code === 'SUBMISSION_ANSWERS_INVALID') {
    const fieldErrors = (envelope?.answerErrors ?? []).reduce<Record<string, string>>((acc, item) => {
      acc[item.questionCode] = item.message;
      return acc;
    }, {});
    return { message: envelope?.message ?? 'Check your answers and try again.', fieldErrors };
  }
  if (code === 'REPORT_NOT_OPEN') {
    return { message: envelope?.message ?? 'This report is no longer open. You can no longer make changes.', fieldErrors: {} };
  }
  if (code === 'ACTIVITIES_NOT_CONFIRMED') {
    return { message: envelope?.message ?? 'Confirm your list of activities before entering activity results.', fieldErrors: {} };
  }
  if (code === 'REPORT_INCOMPLETE') {
    return { message: envelope?.message ?? 'Complete every section before you submit this report.', fieldErrors: {} };
  }
  if (status === 409) {
    return { message: 'A report has already been created for this collection. Refresh the page and try again.', fieldErrors: {} };
  }
  if (status === 403) {
    return { message: 'You do not have permission to do this.', fieldErrors: {} };
  }
  if (status === 422 && envelope?.message) {
    return { message: envelope.message, fieldErrors: {} };
  }
  if (typeof data === 'string' && data.length > 0) {
    return { message: data, fieldErrors: {} };
  }
  return { message: 'Something went wrong. Try again.', fieldErrors: {} };
};

// The report ZIP download's failure copy lives in helpers/downloadErrors.downloadErrorMessage (BLUM-6098,
// BLUM-7239), imported directly by the screens that render it — the Submitted confirmation and SubmissionDetail,
// which share it with the DHSC screens.

// --- Why a read of the caller's own submission failed (BLUM-6101) ---

// Two outcomes, not the three the DHSC Reports area splits into (views/Admin/Reports/messages.ts reportErrorKind).
// That area is not organisation-scoped — DHSC sees every organisation — so it can honestly tell "no such report"
// apart from "you may not see it". This one cannot: an LA-scoped read answers 404 for another organisation's
// collection precisely so it is indistinguishable from one that does not exist (BLUM-7113), and re-splitting the
// two here would put the difference back on screen and turn the page into a probe for other LAs' collection ids.
// So 403 and 404 fold together, exactly as helpers/httpErrors.isAccessDeniedError defines the fold, and everything
// else is a fault.
export type SubmissionDetailErrorKind = 'not-found' | 'failed';

export const SUBMISSION_DETAIL_NOT_FOUND_HEADING = 'Report not found';

// Deliberately identical for "does not exist" and "belongs to another organisation", and actionable for the case
// that actually happens to real users: a mistyped or stale link.
const SUBMISSION_DETAIL_NOT_FOUND_MESSAGE = 'This report was not found. Check the web address, or return to your reports.';

// The shared precedence with the fold applied on top, rather than a second copy of the ordering. That ordering
// (record in hand beats every failure, because react-query keeps cached data when a background refetch fails;
// a missing route param leaves the query disabled so no error ever arrives, and calling that "loading" is a
// spinner that never clears) is the one views/Admin/Reports/messages.ts says is "shared so the two screens cannot
// disagree about it" — restating it here is exactly how they would come to disagree.
//
// Only the last step differs, and collapsing 'forbidden' into 'not-found' is the whole difference between the two
// surfaces: this one cannot distinguish the two without leaking (see above).
export const submissionDetailVerdict = (hasRecord: boolean, hasCollectionId: boolean, isError: boolean, error: unknown): SubmissionDetailErrorKind | undefined => {
  const kind = reportLoadVerdict(hasRecord, hasCollectionId, isError, error);
  return kind === 'forbidden' ? 'not-found' : kind;
};

// The title and the failure copy are the DHSC helpers with this screen's headings bound in, not reimplementations.
// Only the *verdict* differs between the two Reports surfaces (the fold above); what a given verdict is called and
// what it says are the same rules, and stating them twice is how the two screens would drift. Binding the heading
// and the message here also keeps each paired with the other, so a page can never show one screen's <h1> under the
// other's title.
//
// reportPageTitle: the title must describe the page actually rendered, not the one requested, or the tab, the
// history entry and the screen-reader page-change announcement all claim a report is on screen while a failure page
// is (WCAG 2.2 SC 2.4.2). reportFailureMessage: reads the API's own text for a 4xx and never for a 5xx, whose body
// is an empty or unhandled-exception string that tells the user nothing and may be an internal detail.
export const submissionDetailPageTitle = (errorKind: SubmissionDetailErrorKind | undefined, pageTitle: string): string =>
  reportPageTitle(errorKind, SUBMISSION_DETAIL_NOT_FOUND_HEADING, pageTitle);

export const submissionDetailFailureMessage = (errorKind: SubmissionDetailErrorKind, error: unknown): string =>
  reportFailureMessage(errorKind, SUBMISSION_DETAIL_NOT_FOUND_MESSAGE, error);

// --- "Complete your report offline" upload (BLUM-8310) ---

// The client-side checks share the server's copy, so a person sees the same words whichever side catches it.
export const OFFLINE_FILE_MESSAGES = {
  missing: 'Select a CSV file',
  notCsv: 'The selected file must be a CSV',
  spreadsheet: 'The selected file must be a CSV. Save your spreadsheet as ‘CSV UTF-8 (Comma delimited)’ and upload it again',
  tooLarge: 'The selected file must be smaller than 5MB',
} as const;

// The feature gate's refusal (the flag was switched off after the page loaded) is the only bodiless 404 these
// routes send; every other 404 carries a message. A download's error body is a Blob, which the axios interceptor
// turns back into JSON unless it is empty — so an empty Blob is the gate too.
export const isOfflineFeatureOff = (error: unknown): boolean => {
  if (httpStatusOf(error) !== 404) {
    return false;
  }

  const data = (error as { response?: { data?: unknown } } | undefined)?.response?.data;
  return data === undefined || data === null || data === '' || (data instanceof Blob && data.size === 0);
};

export const OFFLINE_MAX_FILE_BYTES = 5 * 1024 * 1024;

// What a failed upload means for the page: the panel is unavailable (the flag was switched off since the page
// loaded, so the route now answers a bare 404), the file as a whole was refused (one message, shown on the file
// input), or the file was read and its answers have problems (the errors table and the annotated file).
export type OfflineUploadProblem = { kind: 'unavailable' } | { kind: 'file'; message: string } | { kind: 'rows'; invalid: OfflineUploadInvalidDto };

export const mapOfflineUploadError = (error: unknown): OfflineUploadProblem => {
  const status = httpStatusOf(error);
  const data = (error as { response?: { data?: unknown } } | undefined)?.response?.data;
  const envelope = typeof data === 'object' && data !== null ? (data as { error?: string; message?: string }) : undefined;

  if (isOfflineFeatureOff(error)) {
    return { kind: 'unavailable' };
  }
  if (status === 422 && envelope?.error === 'OFFLINE_UPLOAD_INVALID') {
    return { kind: 'rows', invalid: envelope as OfflineUploadInvalidDto };
  }
  if (status === 400 && envelope?.error?.startsWith('OFFLINE_') && envelope.message) {
    return { kind: 'file', message: envelope.message };
  }
  if (status === 413) {
    return { kind: 'file', message: OFFLINE_FILE_MESSAGES.tooLarge };
  }
  if (status === 429) {
    // The rate limit is per IP address rather than per person — see helpers/downloadErrors.ts for why this is
    // worded impersonally.
    const seconds = retryAfterOf(error);
    const minutes = seconds === undefined ? undefined : Math.ceil(seconds / 60);
    return {
      kind: 'file',
      message:
        minutes === undefined
          ? 'Too many files have been uploaded in a short time. Try again in a few minutes'
          : `Too many files have been uploaded. Try again in ${minutes <= 1 ? '1 minute' : `${minutes} minutes`}`,
    };
  }
  // The report's own write gate. Worded for this form: the shared mapReportError relays the server's text, which
  // names the collection by its id, and its 409 copy is about starting a report.
  if (status === 409) {
    return { kind: 'file', message: 'This report has already been submitted, so it can no longer be changed' };
  }
  if (envelope?.error === 'REPORT_NOT_OPEN') {
    return { kind: 'file', message: 'This report is closed, so you can no longer upload a spreadsheet' };
  }
  if (envelope?.error === 'ACTIVITIES_NOT_CONFIRMED') {
    return { kind: 'file', message: 'Confirm your list of activities before you upload a spreadsheet' };
  }
  return { kind: 'file', message: 'Sorry, there is a problem with the service. Try again later' };
};

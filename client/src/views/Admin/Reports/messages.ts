// Deep-imported rather than through the OrganisationUsers barrel: this module is a handful of strings and has no
// business pulling that whole feature into its module graph (the useOrganisationUser precedent imports the same
// constant the same way).
import { SERVICE_PROBLEM_HEADING } from '../../../components/Global/OrganisationUsers/UserLoadFailed';
import { httpStatusOf, isForbiddenError, isNotFoundError } from '../../../helpers/httpErrors';

// Why a failed DHSC submission-visibility read failed. The three cases need three different pages, and lumping them
// together is the BLUM-7216 defect: a 500 was reported to admins as "Submission not found", so a service outage read
// as missing data and nobody reported the real fault.
//   not-found — the reporting window, or the organisation within it, does not exist. This area is NOT
//               organisation-scoped (DHSC sees every organisation), so a 404 really does mean "no such record"
//               rather than the scoped-out 404 that isAccessDeniedError exists to hide.
//   forbidden — the role gate refused. Reachable past the route guard via an MFA or suspension envelope.
//   failed    — anything else: 5xx, a dropped connection, an aborted request. Not a statement about the record.
export type ReportErrorKind = 'not-found' | 'forbidden' | 'failed';

export const reportErrorKind = (error: unknown): ReportErrorKind => {
  if (isNotFoundError(error)) return 'not-found';
  if (isForbiddenError(error)) return 'forbidden';
  return 'failed';
};

// Only a 404 is retry-proof, so it falls straight through to the not-found page. A 403 keeps its retries on purpose:
// the axios interceptor deliberately lets an MFA_REQUIRED 403 through inside its 5-second post-verify guard, and the
// retry backoff is what carries the last attempt past that window so the redirect can fire. Shared by both Reports
// screens rather than copied into each — the rule that a 403 must keep retrying is not obvious enough to survive
// being restated (the useOrganisationUser precedent, written after five copies had already drifted).
export const reportQueryRetry = (failureCount: number, error: unknown): boolean => !isNotFoundError(error) && failureCount < 3;

// Each screen's own not-found wording. The detail page is about one organisation's submission, so it must not tell
// the admin "this report was not found" under a "Submission not found" heading.
export const REPORT_NOT_FOUND_MESSAGE = 'This report was not found. It may have been removed.';
export const SUBMISSION_NOT_FOUND_MESSAGE = 'This submission was not found. The organisation may no longer be part of this report.';
const SERVICE_PROBLEM_MESSAGE = 'Try again in a few minutes.';

// The verdict for a screen, in precedence order: a record in hand beats everything; route params that never arrived
// leave the query idle, so no error is ever raised and calling that "loading" is a spinner that never clears; only
// then does the request's own outcome decide. Shared so the two screens cannot disagree about that ordering.
export const reportLoadVerdict = (hasRecord: boolean, hasRouteParams: boolean, isError: boolean, error: unknown): ReportErrorKind | undefined => {
  if (hasRecord) return undefined;
  if (!hasRouteParams) return 'not-found';
  return isError ? reportErrorKind(error) : undefined;
};

// The page title must describe the page that is actually rendered, not the one that was requested — otherwise the
// tab, the history entry and the screen-reader page-change announcement all claim the admin is looking at a
// submission while a 403 or a not-found page is on screen (WCAG 2.2 SC 2.4.2). Mirrors organisationUserPageTitle,
// and takes the not-found heading so the title and the <h1> cannot drift apart.
export const reportPageTitle = (errorKind: ReportErrorKind | undefined, notFoundHeading: string, pageTitle: string): string => {
  if (errorKind === 'forbidden') return 'Not authorised';
  if (errorKind === 'not-found') return notFoundHeading;
  if (errorKind === 'failed') return SERVICE_PROBLEM_HEADING;
  return pageTitle;
};

// Reads the API's own user-facing text out of an error body. The controllers return it either as a bare string
// (ObjectResult over exception.Message) or as the { error, message } envelope — and only the string shape used to be
// handled, so every envelope silently fell through to the generic copy.
const bodyMessage = (data: unknown): string | undefined => {
  if (typeof data === 'string') {
    return data.length > 0 ? data : undefined;
  }
  if (data !== null && typeof data === 'object') {
    const { message, error } = data as { message?: unknown; error?: unknown };
    if (typeof message === 'string' && message.length > 0) return message;
    if (typeof error === 'string' && error.length > 0) return error;
  }
  return undefined;
};

// Maps a DHSC submission-visibility API error to a single user-facing message. A 403 has no entry here: it renders
// NotAuthorised, which carries its own copy. Not exported — reportFailureMessage below is the single entry point,
// so a caller cannot reach for this one and lose the not-found pairing.
const mapReportError = (error: unknown): string => {
  const status = httpStatusOf(error);

  if (status === 404) {
    return REPORT_NOT_FOUND_MESSAGE;
  }

  // Only a 4xx body is safe to show. Those carry text the API wrote for this user; a 5xx body is either empty or an
  // unhandled exception message, which is an internal detail and tells the admin nothing they can act on.
  const isClientError = status !== undefined && status >= 400 && status < 500;
  return (isClientError ? bodyMessage((error as { response?: { data?: unknown } } | undefined)?.response?.data) : undefined) ?? SERVICE_PROBLEM_MESSAGE;
};

// Paired with reportPageTitle so the heading and the body of a failure page are decided together. The not-found
// message comes from the caller rather than from the error, because the missing-route-params verdict has no error
// object at all — reading it from mapReportError would put "Try again in a few minutes." under a not-found heading.
export const reportFailureMessage = (errorKind: ReportErrorKind, notFoundMessage: string, error: unknown): string =>
  errorKind === 'not-found' ? notFoundMessage : mapReportError(error);

// Shown when a file download (the submissions spreadsheet or an organisation's report ZIP) fails to prepare.
export const DOWNLOAD_ERROR_MESSAGE = 'Sorry, there is a problem preparing your download. Try again.';

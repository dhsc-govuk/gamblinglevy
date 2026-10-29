import { httpStatusOf } from '../../helpers/httpErrors';
import { mapReportError, type ReportError } from '../Reports/messages';

// The maturity endpoints answer with the quarterly report's envelopes (SUBMISSION_ANSWERS_INVALID, REPORT_NOT_OPEN,
// REPORT_INCOMPLETE — see MaturityAssessmentErrorFactory), so mapReportError does the work; only the 409 wording
// is the assessment's own.
export const mapMaturityError = (error: unknown): ReportError => {
  if (httpStatusOf(error) === 409) {
    return { message: 'This maturity assessment has already been started or submitted. Refresh the page and try again.', fieldErrors: {} };
  }
  return mapReportError(error);
};

import { KEY_RESULTS_MIN_MESSAGE } from './display';

// Maps an activity API error to a single user-facing message for the error summary / notification banner.
export const mapActivityError = (error: unknown): string => {
  const response = (error as { response?: { status?: number; data?: unknown } } | undefined)?.response;
  const status = response?.status;
  const data = response?.data as { error?: string; message?: string } | string | undefined;
  const code = typeof data === 'object' ? data?.error : undefined;

  if (code === 'KEY_RESULT_NOT_ELIGIBLE') {
    return 'One or more selected Key Results are not available for your organisation and task.';
  }
  if (code === 'ACTIVITY_INCOMPLETE') {
    return 'Complete all required fields before activating this activity.';
  }
  if (status === 409) {
    return 'This activity was changed or removed by someone else. Refresh the page and try again.';
  }
  if (status === 403) {
    return 'You do not have permission to do this.';
  }
  if (status === 422 && typeof data === 'object' && data?.message) {
    return data.message;
  }
  if (typeof data === 'string' && data.length > 0) {
    return data;
  }
  return 'Something went wrong. Try again.';
};

// Maps an activity API error to form error-summary entries. Key-result problems (the min-2 message or the
// plain-422 invalid-code messages) are surfaced against the expected-results field; everything else stays a
// general 'submit' error.
export const mapActivityErrorFields = (error: unknown): Record<string, string> => {
  const message = mapActivityError(error);
  const aboutKeyResults = message === KEY_RESULTS_MIN_MESSAGE || /key result/i.test(message);
  return aboutKeyResults ? { 'activity-expected-results': message } : { submit: message };
};

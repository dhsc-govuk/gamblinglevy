import { ACTIVITY_NAME_REQUIRED_MESSAGE, KEY_RESULTS_ENTER_MESSAGE, KEY_RESULTS_SELECT_MESSAGE } from './display';

// The two API refusals that are about the expected-results selection. NOT_ELIGIBLE is our copy for the
// KEY_RESULT_NOT_ELIGIBLE envelope (which no handler currently emits — the branch is kept as a defensive case);
// INVALID mirrors ExpectedResultInputs.InvalidCodeError verbatim, as the two KEY_RESULTS_* messages do
// SelectionError and EntryError.
const EXPECTED_RESULTS_NOT_ELIGIBLE_MESSAGE = 'One or more selected outcomes are not available for your organisation and task.';

const EXPECTED_RESULTS_INVALID_MESSAGE = 'One or more selected outcomes are not valid for this activity type.';

// Maps an activity API error to a single user-facing message for the error summary / notification banner.
export const mapActivityError = (error: unknown): string => {
  const response = (error as { response?: { status?: number; data?: unknown } } | undefined)?.response;
  const status = response?.status;
  const data = response?.data as { error?: string; message?: string } | string | undefined;
  const code = typeof data === 'object' ? data?.error : undefined;

  if (code === 'KEY_RESULT_NOT_ELIGIBLE') {
    return EXPECTED_RESULTS_NOT_ELIGIBLE_MESSAGE;
  }
  if (code === 'ACTIVITY_INCOMPLETE') {
    return 'Complete all required fields before activating this activity.';
  }
  if (code === 'ACTIVITY_NAME_REQUIRED') {
    return ACTIVITY_NAME_REQUIRED_MESSAGE;
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

// Maps an activity API error to form error-summary entries. Expected-results problems (either minimum-selections
// message or either invalid-code refusal) are surfaced against the expected-results field, and the missing-name
// refusal against the name field; everything else stays a general 'submit' error. Matched against the known
// messages rather than by searching the text for a phrase — the wording is user-facing copy and has already been
// reworded twice (BLUM-7563, BLUM-7620), which silently reroutes these errors to the generic summary.
const EXPECTED_RESULTS_MESSAGES = [KEY_RESULTS_SELECT_MESSAGE, KEY_RESULTS_ENTER_MESSAGE, EXPECTED_RESULTS_NOT_ELIGIBLE_MESSAGE, EXPECTED_RESULTS_INVALID_MESSAGE];

export const mapActivityErrorFields = (error: unknown): Record<string, string> => {
  const message = mapActivityError(error);
  if (EXPECTED_RESULTS_MESSAGES.includes(message)) {
    return { 'activity-expected-results': message };
  }
  return message === ACTIVITY_NAME_REQUIRED_MESSAGE ? { 'activity-name': message } : { submit: message };
};

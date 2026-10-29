import { httpStatusOf, retryAfterOf } from '../../helpers/httpErrors';

// The two 429s the MFA endpoints answer (BLUM-7239), and the copy for each.
//
// They are genuinely different answers and the wording has to say so. MFA_RATE_LIMITED is the per-minute
// damper: nothing has been revoked and the user is fine again shortly. MFA_LOCKED_OUT is the lockout, which
// also revoked every session the user held, so half an hour is the honest number — telling that user to
// "wait a few minutes", as this used to, sends them back to a screen that cannot succeed yet.
//
// In practice the locked-out message rarely renders: the axios interceptor recognises MFA_LOCKED_OUT as a
// session-ended reason and navigates to the sign-in page, which shows the fuller banner. It is here for the
// case where the interceptor has already handled a sign-out for this tab and rejects instead of navigating.
// Says what happened and what to do, with the real number rather than "later". The same lockout is described
// on the sign-in page (views/Public/SignIn), which is where a locked-out user normally lands, so the two have
// to agree — and 30 minutes is MfaSettings.LockoutMinutes. "tell your administrator" matches the house verb
// used by every other banner on that page.
const LOCKED_OUT = 'You have entered too many incorrect codes. For your security you cannot sign in for 30 minutes. If you did not try to sign in, tell your administrator.';

const tooManyAttempts = (error: unknown): string => {
  const seconds = retryAfterOf(error);

  if (seconds === undefined) {
    // Matches the house fallback in views/Admin/Reports/messages.ts rather than inventing a vaguer one.
    return 'You have tried too many times. Try again in a few minutes.';
  }

  // Minutes, not seconds. A countdown the page cannot update is stale the moment it renders, and "wait 43
  // seconds" reads as a precision this number does not have — the window is fixed, so the remainder depends
  // on when the first attempt in it landed.
  const minutes = Math.ceil(seconds / 60);

  return minutes <= 1 ? 'You have tried too many times. Try again in 1 minute.' : `You have tried too many times. Try again in ${minutes} minutes.`;
};

/** The message for a 429 from any MFA endpoint. Callers have already checked the status. */
export const mfaTooManyRequestsMessage = (error: unknown): string => {
  const code = (error as { response?: { data?: { error?: string } } } | undefined)?.response?.data?.error;

  return code === 'MFA_LOCKED_OUT' ? LOCKED_OUT : tooManyAttempts(error);
};

// The 6-digit authenticator code, as every MFA page that takes one handles it (Verify, Setup, RecoveryCodes). One copy,
// so the three pages cannot drift apart in what they accept or how they word a refusal.

const MFA_CODE_LENGTH = 6;

/** What the code input holds: digits only, at most six. */
export const sanitiseMfaCode = (raw: string): string => raw.replace(/\D/g, '').slice(0, MFA_CODE_LENGTH);

/** Why a code cannot be sent yet, or null when it can. */
export const mfaCodeError = (code: string): string | null => {
  if (code.length === 0) {
    return 'Enter the 6-digit code shown in your authenticator app';
  }

  return code.length === MFA_CODE_LENGTH ? null : 'The code must be 6 digits. Check the code and try again';
};

/**
 * The message for a code the server refused. A wrong or expired code (401) and too many attempts (429) read the same on
 * every page. A 400 means something different on each endpoint, so a page that can get one names it; anything else
 * is the page's own fallback.
 */
export const mfaCodeRefusalMessage = (error: unknown, fallback: string, badRequest?: string): string => {
  const status = httpStatusOf(error);

  if (status === 401) {
    return 'That code no longer works. You need to enter the 6-digit code currently shown in your app';
  }
  if (status === 429) {
    return mfaTooManyRequestsMessage(error);
  }

  return status === 400 && badRequest ? badRequest : fallback;
};

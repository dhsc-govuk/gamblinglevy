// The decision half of the axios error interceptor in components/App: given a failed response and what the
// app currently knows, what should happen? Kept pure — no MSAL, no window, no React, no storage — because
// there is no frontend test runner in this repo, and this is the code that decides whether a user whose
// session was just destroyed is offered a screen that can mint them a new one. Effects (navigation, ending the
// CIAM sign-in, writing storage) stay in the interceptor.

import type { SessionEndedReason } from './sessionEnd';

export type AuthFailureAction =
  // Let the caller see the error. Never suspend where we are not also navigating: a request left hanging on a
  // promise that never settles means react-query never reaches an error state either, so the page shows a
  // spinner forever instead of saying anything. `announceReason` carries the sign-out banner for the case
  // where we are rejecting *because* we are already on the page that would have shown it.
  | { kind: 'reject'; announceReason?: SessionEndedReason }
  // Already on the page we would have sent them to. Hang the request so nothing renders a failure behind it.
  | { kind: 'suspend' }
  // The server destroyed the session. End the CIAM sign-in, so the user re-authenticates rather than just
  // re-entering a code — which would mint a fresh session and undo whatever ended this one.
  | { kind: 'end-session'; reason: SessionEndedReason }
  // Navigate, then suspend. `rememberPath` stashes where they were so verification can return them,
  // `forgetPath` drops anything already stashed, and `announceReason` asks the sign-in page for the
  // "you have been signed out" banner.
  | { kind: 'redirect'; to: string; rememberPath: boolean; forgetPath?: boolean; announceReason?: SessionEndedReason };

export type AuthFailureContext = {
  status?: number;
  /** `error` from the response envelope, once any Blob body has been decoded. */
  errorCode?: string;
  currentPath: string;
  /** `import.meta.env.BASE_URL`, with its trailing slash. */
  basePath: string;
  /** This tab has already begun ending the CIAM sign-in. */
  sessionEndHandled: boolean;
  /** A code was accepted within the last few seconds, so a 403 here is a race, not a verdict. */
  isRecentlyVerified: boolean;
  canAttemptSessionEnd: boolean;
  /**
   * Why the session ended, but only when the marker in sessionEnd names the account MSAL still has cached.
   * Null when there is no marker, no cached account, or a different user has since signed in.
   */
  forcedSignOutReason: SessionEndedReason | null;
};

// Mirrors the paths the app is served under. `replace` rather than a join because BASE_URL always carries its
// trailing slash and the suffixes never lead with one; it is there for the case where that stops being true.
const joinBase = (basePath: string, suffix: string): string => `${basePath}${suffix}`.replace('//', '/');

// The envelopes that mean the session is gone server-side, mapped to the banner the sign-in page shows.
// Exported separately from the decision below because the caller has to record the signed-out account on
// *every* one of these, including the ones the decision then rejects.
//
// Three come from MfaRequiredMiddleware as 401s. The fourth is the odd one: MFA_LOCKED_OUT arrives as a 429
// from MfaErrorFactory, because too many incorrect codes is a rate-limit answer (BLUM-7239 AC3) — but the
// lockout also revokes every session the user holds, so the browser's job is the same as for the other
// three. Handling it here rather than as its own branch in the decision is what gives it the whole existing
// mechanism: the signed-out-account marker, the attempt budget, and the guard that stops a second tab
// walking to /mfa/verify and minting a replacement session.
//
// MFA_RATE_LIMITED is deliberately absent. That one is the short per-minute damper: nothing was revoked and
// the user is fine in a moment, so it belongs to the view that made the request, not to this.
export const sessionEndedReasonFromError = (status: number | undefined, errorCode: string | undefined): SessionEndedReason | null => {
  if (status === 429) {
    return errorCode === 'MFA_LOCKED_OUT' ? 'locked' : null;
  }

  if (status !== 401) {
    return null;
  }

  switch (errorCode) {
    case 'SESSION_IDLE_TIMEOUT':
      return 'idle';
    case 'SESSION_REVOKED':
      return 'revoked';
    case 'SESSION_EXPIRED':
      return 'expired';
    default:
      return null;
  }
};

export const decideAuthFailureAction = (context: AuthFailureContext): AuthFailureAction => {
  const { status, errorCode, currentPath, basePath, sessionEndHandled, isRecentlyVerified, canAttemptSessionEnd, forcedSignOutReason } = context;

  const signInPath = joinBase(basePath, 'sign-in');

  // Checked ahead of the 403 branches, and deliberately not subject to the isRecentlyVerified grace window
  // below: a SESSION_* 401 is the server telling us the session is gone, never a race with verification.
  const endedReason = sessionEndedReasonFromError(status, errorCode);
  if (endedReason !== null) {
    if (!canAttemptSessionEnd) {
      // Out of attempts: signing out is not working, so let the error through rather than hanging every
      // request on a promise that never settles and leaving the user staring at a spinner. Announce on the
      // sign-in page for the same reason the forced-sign-out arm below does: a bare reject there leaves the
      // page showing its generic "we cannot sign you in … tell your administrator" banner, which blames a
      // service fault for a session that ended exactly as designed.
      return currentPath === signInPath ? { kind: 'reject', announceReason: endedReason } : { kind: 'reject' };
    }

    // A page fires several requests at once, so without this every one of them would race to call
    // logoutRedirect and MSAL would throw on the interaction already in progress.
    return sessionEndHandled ? { kind: 'suspend' } : { kind: 'end-session', reason: endedReason };
  }

  if (status !== 403) {
    return { kind: 'reject' };
  }

  // Suspension outranks an MFA prompt, matching the middleware order in Program.cs, and is deliberately not
  // gated on sessionEndHandled: a suspended organisation is a different answer, not a session problem.
  if (errorCode === 'ORGANISATION_SUSPENDED') {
    const suspendedPath = joinBase(basePath, 'organisation-suspended');
    return currentPath === suspendedPath ? { kind: 'suspend' } : { kind: 'redirect', to: suspendedPath, rememberPath: false };
  }

  if (errorCode !== 'MFA_REQUIRED' && errorCode !== 'MFA_SETUP_REQUIRED') {
    return { kind: 'reject' };
  }

  // sessionEndHandled gates the whole block, which is what makes the server's cookie clear on a SESSION_* 401
  // safe (BLUM-7241). Signing out of CIAM is asynchronous, so requests already in flight come back while
  // logoutRedirect is still navigating; with the cookie gone those come back 403 MFA_REQUIRED, and unguarded
  // they would race the sign-out and win. Gating the block rather than just the navigation also stops
  // mfa_redirect_path being written back after the sign-out deliberately cleared it.
  const mfaPath = joinBase(basePath, 'mfa/');
  if (sessionEndHandled || currentPath.startsWith(mfaPath) || isRecentlyVerified) {
    return { kind: 'reject' };
  }

  // The same race, but across a page load or a tab — the two cases sessionEndHandled cannot see, because it is
  // module state that a reload resets and that no other tab shares. Either the CIAM sign-out did not take
  // (its failure is undetectable: clearCache swallows its own errors), or a second tab holding its own copy of
  // MSAL's sessionStorage never saw the 401 at all. In both, this account's session was ended and no
  // re-authentication has happened since, so the answer is the sign-in page, not a code entry screen.
  //
  // MFA_REQUIRED only. MFA_SETUP_REQUIRED means the enrolment itself is gone — an admin reset revokes every
  // session *and* removes the authenticator — so /mfa/setup is where that user legitimately has to go, and
  // blocking it would strand them with no self-service way back. It gains nothing either: with no enrolment,
  // VerifyMfaCommand refuses to mint whatever the user types.
  if (errorCode === 'MFA_REQUIRED' && forcedSignOutReason !== null) {
    // Already parked. Reject rather than navigating to the page we are on, which would reload it into the
    // same 403 and reload again — but still announce, because the sign-in page consumes the reason on mount
    // and a reload therefore arrives with it already spent. Without this the page falls back to its generic
    // "we cannot sign you in … tell your administrator" banner: a service-fault message, pointing at the
    // wrong person, for a session that ended exactly as designed.
    // forgetPath for the reason endSignedOutSession drops it in the tab that saw the 401: after a forced
    // sign-out the user re-authenticates from scratch, and on a shared machine that may not be the same
    // person, so nobody is returned to the previous session's URL.
    return currentPath === signInPath
      ? { kind: 'reject', announceReason: forcedSignOutReason }
      : { kind: 'redirect', to: signInPath, rememberPath: false, forgetPath: true, announceReason: forcedSignOutReason };
  }

  const rootPath = basePath.replace(/\/$/, '') || '/';

  return {
    kind: 'redirect',
    to: joinBase(basePath, errorCode === 'MFA_SETUP_REQUIRED' ? 'mfa/setup' : 'mfa/verify'),
    rememberPath: currentPath !== rootPath && currentPath !== signInPath,
  };
};

// Why a signed-in session ended on the server (BLUM-7238 / BLUM-7241, CWE-613). Two separate concerns live
// here, with deliberately different storage scopes:
//
//   session_ended_reason / session_end_attempts — sessionStorage, per tab. Written by the axios interceptor in
//     components/App just before it ends the CIAM sign-in, read by views/Public/SignIn on the far side of the
//     redirect. sessionStorage rather than a module variable because the CIAM round trip reloads the app, and
//     per-tab because they describe what happened to *this* tab.
//
//   session_ended_account — localStorage, shared by every tab, and the one thing that has to outlive the tab
//     that learned about it. MSAL's cache is sessionStorage, so each tab holds its own copy of the account and
//     its own still-valid token, while mfa_session is a cookie and therefore shared. Ending the CIAM sign-in in
//     the tab that saw the 401 touches only that tab's storage; every other tab then gets 403 MFA_REQUIRED,
//     which without this marker walks it straight to /mfa/verify, where a correct code mints a replacement
//     session and undoes the revocation.
//
// The marker is written once (recordSignedOutAccount) and comes down three ways, which is where the bugs
// were: clearSignedOutAccountFor on a successful request by *that* account, clearSignedOutAccount when the
// user deliberately signs out, and releaseSignedOutAccount — a time-boxed stand-down, not a delete — when the
// user clicks through to sign in again. Read it through signedOutAccountFor, never readSignedOutAccount.
//
// Neither sessionStorage key may ever contain the substring "msal" or the CIAM client id: clearCache() deletes
// every sessionStorage key whose name *contains* either (BrowserCacheManager.clear uses indexOf, not a prefix
// test), and components/App calls it when logoutRedirect fails.

export type SessionEndedReason = 'idle' | 'expired' | 'revoked';

const REASON_KEY = 'session_ended_reason';
const ATTEMPTS_KEY = 'session_end_attempts';
const ACCOUNT_KEY = 'session_ended_account';

// The server does now clear the mfa_session cookie on a SESSION_* 401 (BLUM-7241), so the loop this was
// written for — sign out, land back with the same dead cookie, be told the same thing, repeat — can no
// longer happen by that route. Kept as a backstop: if logoutRedirect fails and we fall back to the sign-in
// page with MSAL still authenticated, two attempts covers a transient MSAL failure without spinning.
const maxAttempts = 2;

// Which account was signed out, so the 403 branch can tell "the sign-out did not take" from "somebody has
// since signed in properly". Matching on the account id is what makes the guard a conjunction rather than a
// bare flag — a leftover marker is inert for a different user and inert when no account is cached at all.
// `releasedAt` is stamped when the user asks to sign in again; see releaseSignedOutAccount.
type SignedOutAccount = { accountId: string; reason: SessionEndedReason; at: number; releasedAt?: number };

// Past both the 8 h absolute session (ApplicationSettings.Mfa.SessionExpiryHours) and MSAL's SPA refresh-token
// lifetime, so by then nothing a stale tab still holds could mint a session anyway. Deliberately not shorter:
// the marker's whole job is to outlast an idle tab, and a window that expires underneath one reopens the hole
// it exists to close. Bounded in both directions so a backwards clock change cannot strand anyone either.
const maxMarkerAgeMs = 24 * 60 * 60 * 1000;

// How long clicking "Continue to Microsoft sign in" holds the guard down. Clicking Continue is the app's only
// reliable "the user is deliberately re-authenticating" signal — a gesture in our own code, on the only button
// on the page — but it fires before we can know whether it worked, and an *abandoned* redirect never tells us:
// loginRedirect rejects when it fails to start, not when the user presses Back at the CIAM screen, closes the
// account picker, or is bounced by the IdP. So the release is bounded rather than permanent. Long enough for a
// CIAM round trip that may include an email, a password and the IdP's own MFA; short enough that an abandoned
// one re-arms the guard by itself instead of disabling it for this account indefinitely.
//
// The obvious alternative — release only once something proves the user came back authenticated — has no
// signal to hang on. handleRedirectPromise() returns null whenever msal.interaction.status is missing from the
// tab's sessionStorage, so it would strand people permanently; auth_time is not guaranteed on Entra External
// ID tokens; iat bumps on a silent refresh; and "wait for a non-exempt 2xx" is circular, because reaching one
// means passing the very gate this guard stands in front of. Closing the hole properly is a server-side check
// (sessions_revoked_at on User, verified against the bearer token's auth_time), which is its own ticket.
const releaseWindowMs = 5 * 60 * 1000;

const isReason = (value: string | null): value is SessionEndedReason => value === 'idle' || value === 'expired' || value === 'revoked';

export const readSessionEndedReason = (): SessionEndedReason | null => {
  const reason = sessionStorage.getItem(REASON_KEY);
  return isReason(reason) ? reason : null;
};

export const clearSessionEndedReason = (): void => {
  sessionStorage.removeItem(REASON_KEY);
};

// Split out from recordSessionEnded because the 403 branch needs the banner without spending an attempt: the
// budget below bounds how many times a tab may try to *sign out*, and a tab that is only being told about a
// sign-out another tab already performed has not tried anything.
export const setSessionEndedReason = (reason: SessionEndedReason): void => {
  sessionStorage.setItem(REASON_KEY, reason);
};

export const recordSessionEnded = (reason: SessionEndedReason): void => {
  setSessionEndedReason(reason);
  sessionStorage.setItem(ATTEMPTS_KEY, String(readSessionEndAttempts() + 1));
};

const readSessionEndAttempts = (): number => {
  const attempts = Number.parseInt(sessionStorage.getItem(ATTEMPTS_KEY) ?? '0', 10);
  return Number.isNaN(attempts) ? 0 : attempts;
};

export const canAttemptSessionEnd = (): boolean => readSessionEndAttempts() < maxAttempts;

// Called once a request succeeds again: whatever went wrong is over, so the next genuine timeout in this tab
// gets a full set of attempts rather than inheriting the last one's.
export const clearSessionEndAttempts = (): void => {
  sessionStorage.removeItem(ATTEMPTS_KEY);
};

const writeSignedOutAccount = (marker: SignedOutAccount): void => {
  try {
    localStorage.setItem(ACCOUNT_KEY, JSON.stringify(marker));
  } catch {
    // Storage blocked or full. Degrade to the behaviour we had before this guard existed rather than taking
    // the caller down with it — a caller mid-way through ending a session must still finish.
  }
};

export const recordSignedOutAccount = (accountId: string | undefined, reason: SessionEndedReason): void => {
  // No cached account means there is nothing to suppress later: AuthProvider never sets subId, so the profile
  // query stays disabled and no 403 arrives. Writing a bare marker would only risk locking out whoever signs
  // in next on a shared machine.
  if (!accountId) {
    return;
  }

  // Writing the whole object is what re-arms the guard after a recovery attempt that did not finish: any
  // releasedAt left behind by an earlier "Continue" click goes with it, because a session has just ended again.
  writeSignedOutAccount({ accountId, reason, at: Date.now() });
};

// Anything unreadable, unrecognised or out of the window reads as "no marker". A corrupt key must fail open to
// the old behaviour; failing closed would be a locked-out user with no self-service way back in.
export const readSignedOutAccount = (): SignedOutAccount | null => {
  let raw: string | null = null;

  try {
    raw = localStorage.getItem(ACCOUNT_KEY);
  } catch {
    return null;
  }

  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<SignedOutAccount> | null;
    const accountId = parsed?.accountId;
    const reason = parsed?.reason ?? null;
    const at = parsed?.at;
    const releasedAt = parsed?.releasedAt;

    if (typeof accountId !== 'string' || accountId.length === 0 || !isReason(reason) || typeof at !== 'number' || !Number.isFinite(at)) {
      return null;
    }

    const age = Date.now() - at;

    if (age > maxMarkerAgeMs || age < -maxMarkerAgeMs) {
      return null;
    }

    // An unusable releasedAt drops the release, not the marker — the guard stays up, which is the recoverable
    // direction: the next "Continue" click stamps a fresh one. Dropping the marker instead is the irreversible
    // one, and it is exactly what a corrupted release field should not be able to do.
    const marker: SignedOutAccount = { accountId, reason, at };

    if (typeof releasedAt === 'number' && Number.isFinite(releasedAt)) {
      marker.releasedAt = releasedAt;
    }

    return marker;
  } catch {
    return null;
  }
};

// The guard read: the marker, but only while it is actually in force. Everything that decides whether to
// suppress a 403 goes through here rather than readSignedOutAccount, which returns the raw record.
export const signedOutAccountFor = (accountId: string | undefined): SignedOutAccount | null => {
  if (!accountId) {
    return null;
  }

  const marker = readSignedOutAccount();

  if (marker === null || marker.accountId !== accountId) {
    return null;
  }

  if (marker.releasedAt !== undefined) {
    // Bounded in both directions like `at` above. Past the window the guard re-asserts, which is what makes an
    // abandoned redirect self-correcting; a clock that jumped far enough to put the stamp in the future also
    // reads as expired, because that direction only costs the user another click, not their way back in.
    const sinceRelease = Date.now() - marker.releasedAt;

    if (sinceRelease <= releaseWindowMs && sinceRelease >= -releaseWindowMs) {
      return null;
    }
  }

  return marker;
};

// Unconditional. Only signOut may use this: the user is deliberately leaving, so whatever is on this machine
// is theirs to drop. Every other caller wants clearSignedOutAccountFor.
export const clearSignedOutAccount = (): void => {
  try {
    localStorage.removeItem(ACCOUNT_KEY);
  } catch {
    // Same reasoning as the write: never let storage take down the caller.
  }
};

// Clear only this account's marker. Two functions rather than one optional parameter on purpose: with
// clearSignedOutAccount(accountId?) a stray undefined — an interceptor firing before MSAL has rehydrated, say
// — silently becomes the unconditional clear, and that read/write asymmetry is what let one account's
// successful request erase another account's marker. MSAL's cache is per tab while this key is shared, so two
// tabs on one origin really can hold two different accounts against one marker.
//
// Reads the raw marker, not signedOutAccountFor: a released marker still belongs to this account and a
// success is precisely the proof that the recovery it was released for worked.
export const clearSignedOutAccountFor = (accountId: string | undefined): void => {
  if (!accountId) {
    return;
  }

  const marker = readSignedOutAccount();

  if (marker === null || marker.accountId !== accountId) {
    return;
  }

  clearSignedOutAccount();
};

// The user asked to sign in again. Hold the guard down for releaseWindowMs rather than deleting the marker, so
// an abandoned CIAM redirect costs a few minutes of exposure instead of switching the guard off for good.
export const releaseSignedOutAccount = (): void => {
  const marker = readSignedOutAccount();

  if (marker === null) {
    return;
  }

  writeSignedOutAccount({ ...marker, releasedAt: Date.now() });
};

// loginRedirect rejected, so the user never reached CIAM and nothing was re-authenticated. Put the guard back
// now rather than waiting out a window we already know released nobody.
export const reassertSignedOutAccount = (): void => {
  const marker = readSignedOutAccount();

  if (marker === null || marker.releasedAt === undefined) {
    return;
  }

  writeSignedOutAccount({ accountId: marker.accountId, reason: marker.reason, at: marker.at });
};

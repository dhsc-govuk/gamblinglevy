import * as Sentry from '@sentry/react';
import { trackEvent } from './analytics';

// What to do when a lazily loaded part of the app cannot be fetched (BLUM-8759). The usual cause is a deploy:
// index.html is no-store but the hashed files under /assets are replaced, so a tab opened before the deploy asks
// for a chunk URL that no longer exists and both servers answer 404. Reloading fetches the new index.html and
// with it the new hashes, which is the whole fix — but only once, and only when a reload can help.
//
// The marker is what makes it "once". It is written just before the reload and cleared by the next chunk that
// loads, so a chunk that is still missing after the reload reaches the error page instead of reloading forever.
// It also stops counting after RELOAD_GUARD_MS — see below.
// sessionStorage because the question is about this tab's document, and the key must never contain "msal" or
// the CIAM client id: clearCache() deletes every sessionStorage key whose name contains either (see
// helpers/sessionEnd.ts). Losing the marker that way would cost one extra reload, not a loop.
const RELOAD_KEY = 'chunk_reload_at';

// How long a recorded reload counts as "already tried". A loop is a reload followed seconds later by the same
// failure, so the guard only has to outlast one page load on a slow connection. Without a limit the marker
// would outlive the failure it was written for: a reload that lands on a page with no lazy views (the sign-in
// page, the MFA code screen) leaves nothing to clear it, and the next deploy that tab sits through would then
// get the error page instead of its one reload. Bounded in both directions so a clock that moves backwards
// cannot make a marker count for ever either.
const RELOAD_GUARD_MS = 5 * 60 * 1000;

// How long to wait for a requested reload to replace this document before treating it as not happening. A
// reload can be refused — a beforeunload prompt answered "Stay" is the obvious way — and the spinner would
// otherwise stay up for good. Generous, because on a slow connection the old document legitimately lives on
// for a while after reload() is called, and showing the error page during a reload that is merely slow would
// be worse than waiting.
const RELOAD_GRACE_MS = 30 * 1000;

// Tagged so ChunkErrorBoundary can tell this failure from any other render error, which it must not swallow.
export class ChunkLoadError extends Error {
  readonly reason: unknown;

  constructor(reason?: unknown) {
    super('A part of the application could not be loaded');
    this.name = 'ChunkLoadError';
    this.reason = reason;
  }
}

export type ChunkFailureAction = 'reload' | 'fail';

// Pure, like decideAuthFailureAction: no window, no storage, so the decision can be exercised on its own.
// Offline is 'fail' because a reload with no connection replaces the app with the browser's own error page.
// The in-app page keeps the service's header and footer and leaves the reload to the user, for when they are
// back online.
export const decideChunkFailureAction = ({ online, alreadyReloaded }: { online: boolean; alreadyReloaded: boolean }): ChunkFailureAction =>
  online && !alreadyReloaded ? 'reload' : 'fail';

// Pure for the same reason. Anything that is not a time within the window — no marker, a value that is not a
// number, a time in the future — is "not recent": we write the marker ourselves, so a loop always finds a valid one.
export const isRecentReload = (recordedAt: string | null, now: number): boolean => {
  if (recordedAt === null) return false;
  const age = now - Number(recordedAt);
  return age >= 0 && age <= RELOAD_GUARD_MS;
};

// Storage that throws (blocked, full, a privacy mode) cannot record that a reload happened, so it is reported as
// though one already had: no guard means no automatic reload.
const hasReloadedRecently = (): boolean => {
  try {
    return isRecentReload(window.sessionStorage.getItem(RELOAD_KEY), Date.now());
  } catch {
    return true;
  }
};

const recordReload = (): boolean => {
  try {
    window.sessionStorage.setItem(RELOAD_KEY, String(Date.now()));
    return true;
  } catch {
    return false;
  }
};

export const clearChunkReloadMarker = (): void => {
  try {
    window.sessionStorage.removeItem(RELOAD_KEY);
  } catch {
    // Nothing was recorded if storage is unavailable.
  }
};

// Call this only for a load the page being rendered is waiting on, never for a warm-up: a reload the user did
// not cause would discard whatever they were doing, and during the CIAM return it can lose the sign-in.
//
// On a reload the promise stays pending, so the caller's Suspense fallback stays up until the new document
// replaces this one (the same idiom as the session-end redirect in components/App) — and rejects only if, after
// RELOAD_GRACE_MS, this document is somehow still here. Otherwise it rejects straight away. Either rejection is
// a ChunkLoadError for the boundary.
//
// Both telemetry sinks are told, because the boundary handles the error and so it never reaches window.onerror.
// "The page did not load" covers a missing file, a blocked request and a view module that throws while it is
// being evaluated, and the last of those is a bug in our code that would otherwise look like a bad connection.
// Sentry gets the original error, stack included; it is the live sink exactly where App Insights is not
// configured, which is where trackEvent does nothing (see src/index.tsx). Only a failure the reload did not or
// could not fix is sent there: the stale-tab 404 that a reload cures is expected on every deploy.
export const recoverFromChunkFailure = (reason: unknown): Promise<never> => {
  const online = navigator.onLine !== false;
  const reloading = decideChunkFailureAction({ online, alreadyReloaded: hasReloadedRecently() }) === 'reload' && recordReload();
  trackEvent('chunk_load_failed', { action: reloading ? 'reload' : 'fail', online: String(online), reason: String(reason).slice(0, 300) });

  if (reloading) {
    window.location.reload();
    return new Promise<never>((_resolve, reject) => {
      window.setTimeout(() => reject(new ChunkLoadError(reason)), RELOAD_GRACE_MS);
    });
  }
  Sentry.captureException(reason);
  return Promise.reject(new ChunkLoadError(reason));
};

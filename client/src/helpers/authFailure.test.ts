import { describe, expect, it } from 'vitest';
import { type AuthFailureContext, decideAuthFailureAction, sessionEndedReasonFromError, stripBasePath } from './authFailure';

// Runs in the Node environment on purpose: decideAuthFailureAction is documented as pure (no MSAL, no window, no
// storage), and on Node 24 there is no window, document or Web Storage, so reaching for them fails here rather
// than passes. Globals Node does have (navigator, fetch) are not caught — see vitest.config.mts.

// The two base paths the client is served under: the root on our Azure environments, and DAC's Front Door prefix.
const BASE_PATHS = ['/', '/share-progress-preventing-gambling-harms/'];

const context = (overrides: Partial<AuthFailureContext> = {}): AuthFailureContext => ({
  status: 403,
  errorCode: 'MFA_REQUIRED',
  currentPath: '/reports',
  basePath: '/',
  sessionEndHandled: false,
  isRecentlyVerified: false,
  canAttemptSessionEnd: true,
  forcedSignOutReason: null,
  ...overrides,
});

describe('sessionEndedReasonFromError', () => {
  it.each([
    ['SESSION_IDLE_TIMEOUT', 'idle'],
    ['SESSION_REVOKED', 'revoked'],
    ['SESSION_EXPIRED', 'expired'],
  ] as const)('maps a 401 %s to %s', (errorCode, reason) => {
    expect(sessionEndedReasonFromError(401, errorCode)).toBe(reason);
  });

  it('maps a 429 MFA_LOCKED_OUT to locked, because the lockout revokes every session', () => {
    expect(sessionEndedReasonFromError(429, 'MFA_LOCKED_OUT')).toBe('locked');
  });

  it.each([
    [429, 'MFA_RATE_LIMITED'],
    [429, 'RATE_LIMITED'],
    [429, undefined],
    [401, 'MFA_LOCKED_OUT'],
    [401, 'MFA_REQUIRED'],
    [401, undefined],
    [403, 'SESSION_REVOKED'],
    [403, 'MFA_REQUIRED'],
    [500, 'SESSION_EXPIRED'],
    [undefined, 'SESSION_EXPIRED'],
  ])('does not treat %s %s as a session that ended', (status, errorCode) => {
    expect(sessionEndedReasonFromError(status, errorCode)).toBeNull();
  });
});

describe('decideAuthFailureAction', () => {
  describe('a session the server ended', () => {
    it.each([
      [401, 'SESSION_IDLE_TIMEOUT', 'idle'],
      [401, 'SESSION_REVOKED', 'revoked'],
      [401, 'SESSION_EXPIRED', 'expired'],
      [429, 'MFA_LOCKED_OUT', 'locked'],
    ] as const)('ends the CIAM sign-in on %s %s', (status, errorCode, reason) => {
      expect(decideAuthFailureAction(context({ status, errorCode }))).toEqual({ kind: 'end-session', reason });
    });

    it('suspends every later request once this tab has started ending the sign-in', () => {
      expect(decideAuthFailureAction(context({ status: 401, errorCode: 'SESSION_REVOKED', sessionEndHandled: true }))).toEqual({ kind: 'suspend' });
    });

    it('ignores the verification grace window, because a SESSION_* 401 is never a race', () => {
      expect(decideAuthFailureAction(context({ status: 401, errorCode: 'SESSION_EXPIRED', isRecentlyVerified: true }))).toEqual({ kind: 'end-session', reason: 'expired' });
    });

    it('lets the error through once the attempt budget is spent, rather than hanging the request', () => {
      expect(decideAuthFailureAction(context({ status: 401, errorCode: 'SESSION_IDLE_TIMEOUT', canAttemptSessionEnd: false }))).toEqual({ kind: 'reject' });
    });

    it('rejects without suspending when out of attempts, even if this tab already began the sign-out', () => {
      expect(decideAuthFailureAction(context({ status: 401, errorCode: 'SESSION_IDLE_TIMEOUT', canAttemptSessionEnd: false, sessionEndHandled: true }))).toEqual({
        kind: 'reject',
      });
    });

    it.each(BASE_PATHS)('announces the reason when out of attempts on the sign-in page (base %s)', (basePath) => {
      const action = decideAuthFailureAction(context({ status: 429, errorCode: 'MFA_LOCKED_OUT', canAttemptSessionEnd: false, basePath, currentPath: `${basePath}sign-in` }));

      expect(action).toEqual({ kind: 'reject', announceReason: 'locked' });
    });

    it('does not let a 429 MFA_RATE_LIMITED end the session; the view that asked shows it', () => {
      expect(decideAuthFailureAction(context({ status: 429, errorCode: 'MFA_RATE_LIMITED' }))).toEqual({ kind: 'reject' });
    });
  });

  describe('responses that are not the auth gates', () => {
    it.each([
      [400, 'validation_failed'],
      [404, undefined],
      [500, undefined],
      [undefined, undefined],
      [401, undefined],
      [403, undefined],
      [403, 'SOMETHING_ELSE'],
    ])('rejects %s %s for the caller to handle', (status, errorCode) => {
      expect(decideAuthFailureAction(context({ status, errorCode }))).toEqual({ kind: 'reject' });
    });
  });

  describe.each(BASE_PATHS)('the 403 gates under base path %s', (basePath) => {
    const at = (suffix: string): string => `${basePath}${suffix}`;

    it('sends a deactivated user to the account-deactivated page without remembering where they were', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'USER_DEACTIVATED' }))).toEqual({
        kind: 'redirect',
        to: at('account-deactivated'),
        rememberPath: false,
      });
    });

    it('suspends rather than reloading the account-deactivated page into the same 403', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'USER_DEACTIVATED', currentPath: at('account-deactivated') }))).toEqual({ kind: 'suspend' });
    });

    it('sends a suspended organisation to the organisation-suspended page', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'ORGANISATION_SUSPENDED' }))).toEqual({
        kind: 'redirect',
        to: at('organisation-suspended'),
        rememberPath: false,
      });
    });

    it('suspends on the organisation-suspended page', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'ORGANISATION_SUSPENDED', currentPath: at('organisation-suspended') }))).toEqual({
        kind: 'suspend',
      });
    });

    it.each(['USER_DEACTIVATED', 'ORGANISATION_SUSPENDED'])('still redirects on %s while a sign-out is in progress, because it is not a session problem', (errorCode) => {
      const action = decideAuthFailureAction(context({ basePath, errorCode, sessionEndHandled: true, isRecentlyVerified: true, forcedSignOutReason: 'revoked' }));

      expect(action.kind).toBe('redirect');
    });

    it('sends MFA_REQUIRED to verification and remembers the page', () => {
      expect(decideAuthFailureAction(context({ basePath, currentPath: at('reports') }))).toEqual({ kind: 'redirect', to: at('mfa/verify'), rememberPath: true });
    });

    it('sends MFA_SETUP_REQUIRED to enrolment', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'MFA_SETUP_REQUIRED', currentPath: at('reports') }))).toEqual({
        kind: 'redirect',
        to: at('mfa/setup'),
        rememberPath: true,
      });
    });

    it.each([
      ['the root', basePath === '/' ? '/' : basePath.replace(/\/$/, '')],
      ['the sign-in page', at('sign-in')],
    ])('does not remember %s as somewhere to return to', (_label, currentPath) => {
      expect(decideAuthFailureAction(context({ basePath, currentPath }))).toEqual({ kind: 'redirect', to: at('mfa/verify'), rememberPath: false });
    });

    it.each(['mfa/verify', 'mfa/setup', 'mfa/recovery-codes'])('rejects rather than redirecting when already on %s', (page) => {
      expect(decideAuthFailureAction(context({ basePath, currentPath: at(page) }))).toEqual({ kind: 'reject' });
    });

    it('rejects inside the grace window after a code was accepted', () => {
      expect(decideAuthFailureAction(context({ basePath, isRecentlyVerified: true }))).toEqual({ kind: 'reject' });
    });

    it('rejects while this tab is ending the sign-in, so in-flight requests cannot race the sign-out to /mfa/verify', () => {
      expect(decideAuthFailureAction(context({ basePath, sessionEndHandled: true }))).toEqual({ kind: 'reject' });
      expect(decideAuthFailureAction(context({ basePath, sessionEndHandled: true, forcedSignOutReason: 'revoked' }))).toEqual({ kind: 'reject' });
    });

    it('sends an account whose session was ended to sign in again, never to a code entry screen', () => {
      expect(decideAuthFailureAction(context({ basePath, currentPath: at('reports'), forcedSignOutReason: 'revoked' }))).toEqual({
        kind: 'redirect',
        to: at('sign-in'),
        rememberPath: false,
        forgetPath: true,
        announceReason: 'revoked',
      });
    });

    it('rejects but still announces when the forced sign-out finds the user already on the sign-in page', () => {
      expect(decideAuthFailureAction(context({ basePath, currentPath: at('sign-in'), forcedSignOutReason: 'idle' }))).toEqual({ kind: 'reject', announceReason: 'idle' });
    });

    it('lets MFA_SETUP_REQUIRED through the forced-sign-out guard, because a reset user has to re-enrol', () => {
      expect(decideAuthFailureAction(context({ basePath, errorCode: 'MFA_SETUP_REQUIRED', currentPath: at('reports'), forcedSignOutReason: 'revoked' }))).toEqual({
        kind: 'redirect',
        to: at('mfa/setup'),
        rememberPath: true,
      });
    });
  });

  it('ranks deactivation above suspension and MFA, matching the middleware order', () => {
    // One response carries one code, so the ranking is observable as: none of the guards that stop an MFA
    // redirect stop these two.
    const blockers = { sessionEndHandled: true, isRecentlyVerified: true, currentPath: '/mfa/verify', forcedSignOutReason: 'revoked' as const };

    expect(decideAuthFailureAction(context({ ...blockers, errorCode: 'USER_DEACTIVATED' }))).toEqual({ kind: 'redirect', to: '/account-deactivated', rememberPath: false });
    expect(decideAuthFailureAction(context({ ...blockers, errorCode: 'ORGANISATION_SUSPENDED' }))).toEqual({
      kind: 'redirect',
      to: '/organisation-suspended',
      rememberPath: false,
    });
    expect(decideAuthFailureAction(context({ ...blockers, errorCode: 'MFA_REQUIRED' }))).toEqual({ kind: 'reject' });
  });
});

describe('stripBasePath', () => {
  it.each([
    ['/', '/reports', '/reports'],
    ['/', '/', '/'],
    ['', '/reports', '/reports'],
    ['/prefix/', '/prefix/reports', '/reports'],
    ['/prefix/', '/prefix/reports/1?x=y', '/reports/1?x=y'],
    ['/prefix', '/prefix/reports', '/reports'],
    ['/prefix/', '/prefix', '/'],
    ['/prefix/', '/prefix/', '/'],
    ['/prefix/', '/elsewhere', '/elsewhere'],
  ])('strips base %j from %j to %j', (basePath, pathname, expected) => {
    expect(stripBasePath(basePath, pathname)).toBe(expected);
  });

  it('only strips on a path-segment boundary, so /foo does not eat the front of /foobar', () => {
    expect(stripBasePath('/foo/', '/foobar/x')).toBe('/foobar/x');
  });
});

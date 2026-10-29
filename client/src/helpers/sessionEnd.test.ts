// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  canAttemptSessionEnd,
  clearSessionEndAttempts,
  clearSessionEndedReason,
  clearSignedOutAccount,
  clearSignedOutAccountFor,
  readSessionEndedReason,
  readSignedOutAccount,
  reassertSignedOutAccount,
  recordSessionEnded,
  recordSignedOutAccount,
  releaseSignedOutAccount,
  setSessionEndedReason,
  signedOutAccountFor,
} from './sessionEnd';

// The storage keys are part of the contract: views/Public/SignIn reads the reason on the far side of the CIAM
// redirect, and every tab reads the account marker. They are written out here rather than imported so a rename
// fails a test.
const REASON_KEY = 'session_ended_reason';
const ATTEMPTS_KEY = 'session_end_attempts';
const ACCOUNT_KEY = 'session_ended_account';

const NOW = new Date('2026-10-14T12:00:00Z').getTime();
const MINUTE = 60 * 1000;
const HOUR = 60 * MINUTE;

const writeMarker = (marker: unknown): void => localStorage.setItem(ACCOUNT_KEY, JSON.stringify(marker));

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
  sessionStorage.clear();
  localStorage.clear();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the session-ended reason', () => {
  it('is null until one is recorded', () => {
    expect(readSessionEndedReason()).toBeNull();
  });

  it.each(['idle', 'expired', 'revoked', 'locked'] as const)('round-trips %s', (reason) => {
    setSessionEndedReason(reason);

    expect(readSessionEndedReason()).toBe(reason);
    expect(sessionStorage.getItem(REASON_KEY)).toBe(reason);
  });

  it('reads an unrecognised stored value as no reason', () => {
    sessionStorage.setItem(REASON_KEY, 'timeout');

    expect(readSessionEndedReason()).toBeNull();
  });

  it('is cleared by clearSessionEndedReason', () => {
    setSessionEndedReason('idle');
    clearSessionEndedReason();

    expect(readSessionEndedReason()).toBeNull();
  });

  it('lives in sessionStorage, per tab, not in localStorage', () => {
    recordSessionEnded('expired');

    expect(localStorage.length).toBe(0);
  });
});

describe('the sign-out attempt budget', () => {
  it('allows two attempts per tab', () => {
    expect(canAttemptSessionEnd()).toBe(true);

    recordSessionEnded('idle');
    expect(canAttemptSessionEnd()).toBe(true);

    recordSessionEnded('idle');
    expect(canAttemptSessionEnd()).toBe(false);
  });

  it('records the reason along with the attempt', () => {
    recordSessionEnded('revoked');

    expect(readSessionEndedReason()).toBe('revoked');
  });

  it('is not spent by setting the banner reason alone', () => {
    setSessionEndedReason('revoked');
    setSessionEndedReason('revoked');
    setSessionEndedReason('revoked');

    expect(canAttemptSessionEnd()).toBe(true);
  });

  it('is refilled once a request succeeds again', () => {
    recordSessionEnded('idle');
    recordSessionEnded('idle');
    clearSessionEndAttempts();

    expect(canAttemptSessionEnd()).toBe(true);
    expect(sessionStorage.getItem(ATTEMPTS_KEY)).toBeNull();
  });

  it('reads a corrupt count as no attempts rather than locking the tab out', () => {
    sessionStorage.setItem(ATTEMPTS_KEY, 'many');

    expect(canAttemptSessionEnd()).toBe(true);

    recordSessionEnded('idle');
    expect(sessionStorage.getItem(ATTEMPTS_KEY)).toBe('1');
  });
});

describe('recordSignedOutAccount', () => {
  it('writes the account, the reason and the time to localStorage', () => {
    recordSignedOutAccount('account-a', 'revoked');

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'revoked', at: NOW });
    expect(sessionStorage.length).toBe(0);
  });

  it.each([undefined, ''])('writes nothing without a cached account (%j), so nobody signing in next is locked out', (accountId) => {
    recordSignedOutAccount(accountId, 'revoked');

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });

  it('re-arms the guard: a new sign-out drops the release an earlier Continue click left behind', () => {
    recordSignedOutAccount('account-a', 'idle');
    releaseSignedOutAccount();
    vi.advanceTimersByTime(MINUTE);

    recordSignedOutAccount('account-a', 'expired');

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'expired', at: NOW + MINUTE });
    expect(signedOutAccountFor('account-a')).not.toBeNull();
  });
});

describe('readSignedOutAccount fails open', () => {
  it.each([
    ['nothing stored', null],
    ['invalid JSON', '{not json'],
    ['a JSON null', 'null'],
    ['an empty object', '{}'],
    ['an empty account id', JSON.stringify({ accountId: '', reason: 'revoked', at: NOW })],
    ['a numeric account id', JSON.stringify({ accountId: 42, reason: 'revoked', at: NOW })],
    ['an unknown reason', JSON.stringify({ accountId: 'account-a', reason: 'timeout', at: NOW })],
    ['no time', JSON.stringify({ accountId: 'account-a', reason: 'revoked' })],
    ['a string time', JSON.stringify({ accountId: 'account-a', reason: 'revoked', at: String(NOW) })],
    ['a null time', JSON.stringify({ accountId: 'account-a', reason: 'revoked', at: null })],
    // JSON cannot write Infinity (stringify turns it into null), but JSON.parse reads an out-of-range literal as one.
    ['a non-finite time', '{"accountId":"account-a","reason":"revoked","at":1e400}'],
  ])('reads %s as no marker', (_label, raw) => {
    if (raw !== null) {
      localStorage.setItem(ACCOUNT_KEY, raw);
    }

    expect(readSignedOutAccount()).toBeNull();
    expect(signedOutAccountFor('account-a')).toBeNull();
  });

  it('keeps a marker up to 24 hours old', () => {
    writeMarker({ accountId: 'account-a', reason: 'revoked', at: NOW - 24 * HOUR });

    expect(readSignedOutAccount()).not.toBeNull();
  });

  it('drops a marker older than 24 hours', () => {
    writeMarker({ accountId: 'account-a', reason: 'revoked', at: NOW - 24 * HOUR - 1 });

    expect(readSignedOutAccount()).toBeNull();
  });

  it('drops a marker more than 24 hours in the future, so a clock change cannot strand anyone', () => {
    writeMarker({ accountId: 'account-a', reason: 'revoked', at: NOW + 24 * HOUR + 1 });

    expect(readSignedOutAccount()).toBeNull();
  });

  it.each([
    ['a string release', JSON.stringify({ accountId: 'account-a', reason: 'revoked', at: NOW, releasedAt: 'soon' })],
    ['a non-finite release', `{"accountId":"account-a","reason":"revoked","at":${NOW},"releasedAt":1e400}`],
  ])('drops %s but keeps the marker, so the guard stays up', (_label, raw) => {
    localStorage.setItem(ACCOUNT_KEY, raw);

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'revoked', at: NOW });
    expect(signedOutAccountFor('account-a')).not.toBeNull();
  });

  it('ignores fields it does not know', () => {
    writeMarker({ accountId: 'account-a', reason: 'locked', at: NOW, extra: true });

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'locked', at: NOW });
  });
});

describe('signedOutAccountFor', () => {
  beforeEach(() => {
    recordSignedOutAccount('account-a', 'revoked');
  });

  it('returns the marker for the account it names', () => {
    expect(signedOutAccountFor('account-a')).toEqual({ accountId: 'account-a', reason: 'revoked', at: NOW });
  });

  it('is inert for a different account, so a leftover marker cannot block the next user', () => {
    expect(signedOutAccountFor('account-b')).toBeNull();
  });

  it.each([undefined, ''])('is inert when no account is cached (%j)', (accountId) => {
    expect(signedOutAccountFor(accountId)).toBeNull();
  });

  it('stands down for five minutes after the user asks to sign in again', () => {
    releaseSignedOutAccount();

    expect(signedOutAccountFor('account-a')).toBeNull();

    vi.advanceTimersByTime(5 * MINUTE);
    expect(signedOutAccountFor('account-a')).toBeNull();
  });

  it('re-asserts itself once the release window has passed, so an abandoned redirect corrects itself', () => {
    releaseSignedOutAccount();
    vi.advanceTimersByTime(5 * MINUTE + 1);

    expect(signedOutAccountFor('account-a')).toMatchObject({ accountId: 'account-a', reason: 'revoked', releasedAt: NOW });
  });

  it('treats a release stamped beyond the window in the future as expired', () => {
    writeMarker({ accountId: 'account-a', reason: 'revoked', at: NOW, releasedAt: NOW + 5 * MINUTE + 1 });

    expect(signedOutAccountFor('account-a')).not.toBeNull();
  });

  it('still reads the raw marker through readSignedOutAccount while released', () => {
    releaseSignedOutAccount();

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'revoked', at: NOW, releasedAt: NOW });
  });
});

describe('clearing the marker', () => {
  beforeEach(() => {
    recordSignedOutAccount('account-a', 'revoked');
  });

  it('clearSignedOutAccountFor leaves another account’s marker in place', () => {
    clearSignedOutAccountFor('account-b');

    expect(readSignedOutAccount()?.accountId).toBe('account-a');
  });

  it.each([undefined, ''])('clearSignedOutAccountFor(%j) never becomes the unconditional clear', (accountId) => {
    clearSignedOutAccountFor(accountId);

    expect(readSignedOutAccount()).not.toBeNull();
  });

  it('clearSignedOutAccountFor removes the account’s own marker', () => {
    clearSignedOutAccountFor('account-a');

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });

  it('clearSignedOutAccountFor removes a released marker too, because a success proves the recovery worked', () => {
    releaseSignedOutAccount();
    clearSignedOutAccountFor('account-a');

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });

  it('clearSignedOutAccount removes the marker whoever it names', () => {
    clearSignedOutAccount();

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });
});

describe('releasing and re-asserting', () => {
  it('releaseSignedOutAccount writes nothing when there is no marker', () => {
    releaseSignedOutAccount();

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });

  it('releaseSignedOutAccount stamps the release and keeps the original time', () => {
    recordSignedOutAccount('account-a', 'locked');
    vi.advanceTimersByTime(MINUTE);

    releaseSignedOutAccount();

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'locked', at: NOW, releasedAt: NOW + MINUTE });
  });

  it('reassertSignedOutAccount puts the guard straight back after a failed loginRedirect', () => {
    recordSignedOutAccount('account-a', 'locked');
    releaseSignedOutAccount();
    vi.advanceTimersByTime(MINUTE);

    reassertSignedOutAccount();

    expect(readSignedOutAccount()).toEqual({ accountId: 'account-a', reason: 'locked', at: NOW });
    expect(signedOutAccountFor('account-a')).not.toBeNull();
  });

  it('reassertSignedOutAccount leaves an unreleased marker untouched', () => {
    recordSignedOutAccount('account-a', 'idle');
    const setItem = vi.spyOn(Storage.prototype, 'setItem');

    reassertSignedOutAccount();

    expect(setItem).not.toHaveBeenCalled();
  });

  it('reassertSignedOutAccount does nothing without a marker', () => {
    reassertSignedOutAccount();

    expect(localStorage.getItem(ACCOUNT_KEY)).toBeNull();
  });
});

// Only the localStorage marker is written defensively, so only it is tested here. The sessionStorage reason and
// attempt budget are not: blocked sessionStorage also takes MSAL's token cache, so there is no signed-in session to
// end in the first place.
describe('blocked localStorage (the account marker)', () => {
  const blocked = (): never => {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  };

  it('a failed write does not take down the caller ending a session', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);

    expect(() => recordSignedOutAccount('account-a', 'revoked')).not.toThrow();
  });

  it('a failed release does not throw', () => {
    recordSignedOutAccount('account-a', 'revoked');
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(blocked);

    expect(() => releaseSignedOutAccount()).not.toThrow();
    expect(() => reassertSignedOutAccount()).not.toThrow();
  });

  it('a failed read reads as no marker', () => {
    recordSignedOutAccount('account-a', 'revoked');
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(blocked);

    expect(readSignedOutAccount()).toBeNull();
    expect(signedOutAccountFor('account-a')).toBeNull();
  });

  it('a failed delete does not throw', () => {
    recordSignedOutAccount('account-a', 'revoked');
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(blocked);

    expect(() => clearSignedOutAccount()).not.toThrow();
    expect(() => clearSignedOutAccountFor('account-a')).not.toThrow();
  });
});

it('never writes a key that MSAL’s clearCache would delete', () => {
  // clearCache() removes every sessionStorage key whose name *contains* "msal" (or the CIAM client id), and
  // components/App calls it when logoutRedirect fails — which would silently wipe the banner and the budget.
  recordSessionEnded('revoked');
  recordSignedOutAccount('account-a', 'revoked');
  releaseSignedOutAccount();

  const keys = [...Object.keys(sessionStorage), ...Object.keys(localStorage)];

  expect(keys.sort()).toEqual([ATTEMPTS_KEY, REASON_KEY, ACCOUNT_KEY].sort());
  expect(keys.some((key) => key.toLowerCase().includes('msal'))).toBe(false);
});

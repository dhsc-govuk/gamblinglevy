import { describe, expect, it } from 'vitest';
import { mfaCodeError, mfaCodeRefusalMessage, sanitiseMfaCode } from './messages';

const refusal = (status: number, data: unknown = '') => ({ response: { status, data, headers: {} } });

describe('sanitiseMfaCode', () => {
  it('keeps digits only, at most six', () => {
    expect(sanitiseMfaCode('123456')).toBe('123456');
    expect(sanitiseMfaCode('123 456')).toBe('123456');
    expect(sanitiseMfaCode('12a3-45b6')).toBe('123456');
    expect(sanitiseMfaCode('1234567')).toBe('123456');
    expect(sanitiseMfaCode('abc')).toBe('');
  });
});

describe('mfaCodeError', () => {
  it('asks for a code when there is none', () => {
    expect(mfaCodeError('')).toBe('Enter the 6-digit code shown in your authenticator app');
  });

  it('refuses a code that is not six digits', () => {
    expect(mfaCodeError('12345')).toBe('The code must be 6 digits. Check the code and try again');
  });

  it('accepts six digits', () => {
    expect(mfaCodeError('123456')).toBeNull();
  });
});

describe('mfaCodeRefusalMessage', () => {
  const fallback = 'Verification failed. Try again.';
  const badRequest = 'Your setup session has expired. Refresh the page to start again.';

  it('says a refused code no longer works', () => {
    expect(mfaCodeRefusalMessage(refusal(401), fallback)).toBe('That code no longer works. You need to enter the 6-digit code currently shown in your app');
  });

  it('uses the 429 copy, which tells a lockout from the damper', () => {
    expect(mfaCodeRefusalMessage(refusal(429, { error: 'MFA_LOCKED_OUT' }), fallback)).toContain('you cannot sign in for 30 minutes');
    expect(mfaCodeRefusalMessage(refusal(429, { error: 'MFA_RATE_LIMITED' }), fallback)).toBe('You have tried too many times. Try again in a few minutes.');
  });

  it("uses the page's own copy for a 400 only when it gives one", () => {
    expect(mfaCodeRefusalMessage(refusal(400), fallback, badRequest)).toBe(badRequest);
    expect(mfaCodeRefusalMessage(refusal(400), fallback)).toBe(fallback);
  });

  it('falls back for anything else, including no response at all', () => {
    expect(mfaCodeRefusalMessage(refusal(500), fallback, badRequest)).toBe(fallback);
    expect(mfaCodeRefusalMessage(new Error('Network Error'), fallback)).toBe(fallback);
  });
});

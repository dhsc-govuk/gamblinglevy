// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest';
import { getCookiePreferences, getStoredCookiePolicy, saveCookiePreferences } from './cookiePolicy';

// The cookie name is part of the contract (the Cookies page lists it), so it is written out rather than imported.
const NAME = 'cookie_policy';

const rawValue = (): string | undefined =>
  document.cookie
    .split('; ')
    .find((row) => row.startsWith(`${NAME}=`))
    ?.slice(NAME.length + 1);

const setRaw = (value: string): void => {
  document.cookie = `${NAME}=${value}; Path=/`;
};

afterEach(() => {
  document.cookie = `${NAME}=; Max-Age=0; Path=/`;
});

describe('saveCookiePreferences', () => {
  it('writes a compact token', () => {
    saveCookiePreferences({ analytics: true, functional: false });

    expect(rawValue()).toBe('v1.a1.f0');
  });

  // BLUM-9091: DAC's gateway WAF answered 403 to every request carrying the URL-encoded JSON this used to write.
  it('writes nothing a WAF reads as JSON or an encoded payload', () => {
    saveCookiePreferences({ analytics: false, functional: true });

    expect(rawValue()).toMatch(/^[A-Za-z0-9.]+$/);
  });

  it('round-trips every combination', () => {
    for (const analytics of [true, false]) {
      for (const functional of [true, false]) {
        saveCookiePreferences({ analytics, functional });

        expect(getStoredCookiePolicy()).toEqual({ version: 1, analytics, functional });
      }
    }
  });
});

describe('getStoredCookiePolicy', () => {
  it('returns null when no choice has been made', () => {
    expect(getStoredCookiePolicy()).toBeNull();
    expect(getCookiePreferences()).toEqual({ analytics: false, functional: false });
  });

  it('still reads the JSON written before BLUM-9091', () => {
    setRaw(encodeURIComponent(JSON.stringify({ analytics: true, functional: false, version: 1 })));

    expect(getStoredCookiePolicy()).toEqual({ version: 1, analytics: true, functional: false });
  });

  it('replaces the old JSON in place when the choice is saved again', () => {
    setRaw(encodeURIComponent(JSON.stringify({ analytics: true, functional: false, version: 1 })));

    saveCookiePreferences({ analytics: false, functional: false });

    expect(document.cookie.split('; ').filter((row) => row.startsWith(`${NAME}=`))).toEqual([`${NAME}=v1.a0.f0`]);
  });

  it('treats another version as no decision', () => {
    setRaw('v2.a1.f1');

    expect(getStoredCookiePolicy()).toBeNull();
  });

  it('treats an unrecognised value as no decision', () => {
    setRaw('not-a-policy');

    expect(getStoredCookiePolicy()).toBeNull();
  });
});

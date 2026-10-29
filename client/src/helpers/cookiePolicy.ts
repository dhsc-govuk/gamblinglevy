// Cookies page (BLUM-cookies). Records the user's consent for non-essential cookie categories in the
// `cookie_policy` cookie. That cookie is itself strictly necessary — it stores the choice the law
// requires us to remember — so it does not itself require consent (PECR / DTAC). No non-essential
// cookie is set anywhere until the matching category is accepted here.
//
// `version` lets us force re-consent if the set of categories ever changes: bump it and a stored policy
// from an older version is treated as "no decision made yet".
//
// The value is a compact token, `v1.a1.f0` (version, analytics, functional), not JSON (BLUM-9091). DAC's gateway WAF
// URL-decodes cookies and answered 403 to every request — every page, sign-in included — from a browser holding the
// URL-encoded JSON this used to write, locking the user out until they cleared their cookies. Letters, digits and
// dots give it nothing to match (the same WAF as BLUM-8917). The name, Path and version are unchanged, so a save
// overwrites an old JSON cookie in place, and the reader still accepts that format so no choice already made is lost.

export const COOKIE_POLICY_NAME = 'cookie_policy';
export const COOKIE_POLICY_VERSION = 1;

const TOKEN_PATTERN = /^v(\d+)\.a([01])\.f([01])$/;

const toToken = ({ analytics, functional }: CookiePreferences): string => `v${COOKIE_POLICY_VERSION}.a${analytics ? 1 : 0}.f${functional ? 1 : 0}`;

// The token, or the JSON written before BLUM-9091; anything else is no decision.
const parseStoredValue = (raw: string): Partial<StoredCookiePolicy> | null => {
  const token = TOKEN_PATTERN.exec(raw);
  if (token) {
    return { version: Number(token[1]), analytics: token[2] === '1', functional: token[3] === '1' };
  }
  try {
    const legacy: unknown = JSON.parse(decodeURIComponent(raw));
    return legacy !== null && typeof legacy === 'object' ? (legacy as Partial<StoredCookiePolicy>) : null;
  } catch {
    return null;
  }
};

// One year, per the GDS cookies-page pattern and the story's acceptance criteria.
const ONE_YEAR_SECONDS = 365 * 24 * 60 * 60;

export interface CookiePreferences {
  analytics: boolean;
  functional: boolean;
}

export interface StoredCookiePolicy extends CookiePreferences {
  version: number;
}

// Default to rejected — nothing non-essential runs until the user actively opts in.
export const defaultCookiePreferences: CookiePreferences = { analytics: false, functional: false };

// Returns the stored policy, or null when there is no decision yet (absent, unparseable, or a version we
// no longer recognise). Callers treat null as "show the defaults, no banner".
export const getStoredCookiePolicy = (): StoredCookiePolicy | null => {
  const match = document.cookie.split('; ').find((row) => row.startsWith(`${COOKIE_POLICY_NAME}=`));
  if (!match) {
    return null;
  }

  const parsed = parseStoredValue(match.slice(COOKIE_POLICY_NAME.length + 1));
  if (!parsed || parsed.version !== COOKIE_POLICY_VERSION) {
    return null;
  }
  return { version: COOKIE_POLICY_VERSION, analytics: parsed.analytics === true, functional: parsed.functional === true };
};

export const getCookiePreferences = (): CookiePreferences => {
  const stored = getStoredCookiePolicy();
  return stored ? { analytics: stored.analytics, functional: stored.functional } : defaultCookiePreferences;
};

export const saveCookiePreferences = (preferences: CookiePreferences): void => {
  // Secure only on https so local http dev still works; SameSite=Lax is correct for a first-party
  // preference cookie that never needs to travel on cross-site requests.
  const secure = window.location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${COOKIE_POLICY_NAME}=${toToken(preferences)}; Max-Age=${ONE_YEAR_SECONDS}; Path=/; SameSite=Lax${secure}`;
};

// Cookies page (BLUM-cookies). Records the user's consent for non-essential cookie categories in the
// `cookie_policy` cookie. That cookie is itself strictly necessary — it stores the choice the law
// requires us to remember — so it does not itself require consent (PECR / DTAC). No non-essential
// cookie is set anywhere until the matching category is accepted here.
//
// `version` lets us force re-consent if the set of categories ever changes: bump it and a stored policy
// from an older version is treated as "no decision made yet".

export const COOKIE_POLICY_NAME = 'cookie_policy';
export const COOKIE_POLICY_VERSION = 1;

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

  try {
    const parsed = JSON.parse(decodeURIComponent(match.slice(COOKIE_POLICY_NAME.length + 1))) as Partial<StoredCookiePolicy>;
    if (parsed.version !== COOKIE_POLICY_VERSION) {
      return null;
    }
    return { version: COOKIE_POLICY_VERSION, analytics: parsed.analytics === true, functional: parsed.functional === true };
  } catch {
    return null;
  }
};

export const getCookiePreferences = (): CookiePreferences => {
  const stored = getStoredCookiePolicy();
  return stored ? { analytics: stored.analytics, functional: stored.functional } : defaultCookiePreferences;
};

export const saveCookiePreferences = (preferences: CookiePreferences): void => {
  const payload: StoredCookiePolicy = { ...preferences, version: COOKIE_POLICY_VERSION };
  // Secure only on https so local http dev still works; SameSite=Lax is correct for a first-party
  // preference cookie that never needs to travel on cross-site requests.
  const secure = window.location.protocol === 'https:' ? '; Secure' : '';
  document.cookie = `${COOKIE_POLICY_NAME}=${encodeURIComponent(JSON.stringify(payload))}; Max-Age=${ONE_YEAR_SECONDS}; Path=/; SameSite=Lax${secure}`;
};

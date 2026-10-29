/**
 * BLUM-7237 — the canonical security-header set for this client.
 *
 * The built SPA is served by two different stacks, and both must send the same headers:
 *
 *   - DAC (client-managed): nginx, from `client/nginx.conf`, built by `client/Dockerfile.ci`.
 *   - Azure MVP/UAT: `client/server.mjs`, run by the App Service startup command.
 *
 * nginx cannot read this file, so `nginx.conf` restates the same values by hand and
 * `scripts/check-headers.mjs` fails the build when the two disagree. That guard is the
 * point of the exercise: a hardened CSP sat unnoticed in `nginx.conf` while the Azure
 * environments served no security headers at all, which is what BLUM-7237 reported.
 *
 * Plain ESM with no dependencies so the build scripts and the runtime server can share it.
 */

/**
 * Entra External ID (CIAM) endpoints MSAL reaches for OIDC metadata, tokens and the hidden
 * iframe it renews in. Both `connect-src` and `frame-src` need them.
 */
const MICROSOFT_IDENTITY_HOSTS = ['https://login.microsoftonline.com', 'https://*.microsoftonline.com', 'https://*.msftauth.net', 'https://*.ciamlogin.com'];

/** Application Insights ingestion and Sentry, both of which post from the browser. */
const TELEMETRY_HOSTS = [
  'https://dc.services.visualstudio.com',
  'https://*.applicationinsights.azure.com',
  'https://*.in.applicationinsights.azure.com',
  'https://*.monitor.azure.com',
  'https://*.sentry.io',
];

/** Paths whose filenames carry a content hash, so they can be cached forever. */
export const IMMUTABLE_PREFIXES = ['/assets/', '/fonts/'];

/**
 * Paths that must 404 rather than fall back to index.html. A stale index.html asking for a
 * deleted chunk otherwise gets HTML with a JavaScript content type, which `nosniff` turns
 * into a confusing MIME refusal instead of a plain missing-file error.
 */
export const NO_FALLBACK_PREFIXES = ['/assets/', '/fonts/', '/images/'];

/**
 * Builds the Content-Security-Policy value.
 *
 * `connectSrcExtra` carries the API origin, which differs per environment: on Azure the API is
 * a separate App Service host, so `'self'` does not cover it and every XHR would be blocked.
 * `nginx.conf` passes the literal `API_ORIGIN`, which `docker-entrypoint.sh` substitutes.
 */
export const buildCsp = ({ connectSrcExtra = [], frameSrcExtra = [] } = {}) => {
  // wss: permits outbound WebSocket connections alongside the plain https: origins above.
  const connectSrc = ["'self'", ...connectSrcExtra, ...MICROSOFT_IDENTITY_HOSTS, ...TELEMETRY_HOSTS, 'wss:'];
  // 'self' alongside the identity hosts: CIAM answers the hidden renewal frame with a redirect
  // back to our own redirect URI, and frame-src is re-checked on that hop. Without it the frame
  // is blocked at the last step, acquireTokenSilent times out, and every token renewal turns
  // into an interactive sign-in. Same frame the frame-ancestors note below is about.
  //
  // The app does not open that frame at present: since the move to MSAL v5 (BLUM-8757) silent
  // renewal stops at the refresh token, because v5's frame needs a redirect bridge page this
  // app does not serve. Both allowances are kept so adding the bridge is not also a header
  // change. Tightening them instead is its own decision, and has to land in nginx.conf too.
  const frameSrc = ["'self'", ...MICROSOFT_IDENTITY_HOSTS, ...frameSrcExtra];

  return [
    // Deny by default, then name every fetch type this app actually uses.
    "default-src 'none'",
    "manifest-src 'self'",
    // No inline script survives in index.html (BLUM-7237), so this needs no nonce or hash.
    "script-src 'self'",
    // Three things need 'unsafe-inline' here: styled-components (inside govuk-react) injects
    // <style> elements at runtime, index.html carries an inline <style> block for the fonts,
    // and React `style` attributes fall under style-src too (there is no style-src-attr). A
    // hash or nonce would make browsers ignore 'unsafe-inline' entirely and every styled
    // component would lose its styling, so this stays until all three are gone. MUI and
    // emotion, the other two injectors, were removed in BLUM-8758.
    "style-src 'self' 'unsafe-inline'",
    // blob: covers the object-URL anchors used for CSV/report downloads and recovery codes.
    "img-src 'self' data: blob:",
    "font-src 'self'",
    `connect-src ${connectSrc.join(' ')}`,
    `frame-src ${frameSrc.join(' ')}`,
    // 'self' rather than 'none': MSAL renews tokens in a hidden iframe pointed at our own
    // redirect URI, and 'none' blocks that same-origin frame. Cross-origin framing — the
    // actual clickjacking risk — is still refused.
    "frame-ancestors 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join('; ');
};

/** Non-CSP security headers, sent on every response including errors. */
export const STATIC_HEADERS = {
  'X-Content-Type-Options': 'nosniff',
  // Pairs with frame-ancestors 'self' for browsers that still read this.
  'X-Frame-Options': 'SAMEORIGIN',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()',
  'X-Permitted-Cross-Domain-Policies': 'none',
  'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
};

/**
 * Cache-Control by path class. Keyed on the directory rather than the file extension: the
 * un-hashed favicon lives under /images and must not be marked immutable for a year.
 */
export const cacheControlFor = (pathname, { isIndexHtml = false } = {}) => {
  if (isIndexHtml) {
    // The SPA entry point must always be fresh, or a browser keeps asking for asset hashes
    // that no longer exist.
    return 'no-store, max-age=0';
  }
  if (IMMUTABLE_PREFIXES.some((prefix) => pathname.startsWith(prefix))) {
    return 'public, max-age=31536000, immutable';
  }
  return 'public, max-age=3600';
};

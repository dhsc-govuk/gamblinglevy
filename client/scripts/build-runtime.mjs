/**
 * BLUM-7237 — postbuild step for the Azure App Service deployments.
 *
 * `bitbucket-pipelines.yml` zips the contents of `dist/` and nothing else, so the server that
 * sets the security headers has to end up inside `dist/`. Doing it here rather than in the
 * pipeline keeps `npm run build` producing exactly what CI deploys, which is what makes the
 * local verification recipe meaningful.
 *
 * It also writes the one part of the policy that differs per environment: the API origin. CI
 * overwrites `src/config.json` from pipeline variables before the build, so by the time this
 * runs the file already names the right API for this artifact.
 */

import { copyFile, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const CLIENT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DIST = path.join(CLIENT_ROOT, 'dist');

const originOf = (value) => {
  try {
    return new URL(value).origin;
  } catch {
    return null;
  }
};

const config = JSON.parse(await readFile(path.join(CLIENT_ROOT, 'src', 'config.json'), 'utf8'));

// On Azure the API is a separate App Service host, so connect-src 'self' does not reach it and
// every request would be blocked. An artifact that cannot name its API is not worth shipping.
//
// A path is the other supported topology, not a failure: docker-entrypoint.sh documents leaving
// API_ORIGIN unset "when the API is served from the same origin as this app", and a build
// configured that way has no second host to name — 'self' already covers it. An empty or
// malformed value stays fatal, because that is an unset pipeline variable rather than a choice.
const apiUrl = typeof config.api_url === 'string' ? config.api_url.trim() : '';
const apiOrigin = originOf(apiUrl);
const apiIsSameOrigin = apiUrl.startsWith('/');
if (!apiOrigin && !apiIsSameOrigin) {
  console.error(`[headers] api_url is neither an absolute URL nor a same-origin path (${JSON.stringify(config.api_url)}) — cannot build a working CSP.`);
  process.exit(1);
}

// The base policy already allows https://*.ciamlogin.com; only a custom-domain authority needs
// naming explicitly, in frame-src too because MSAL renews tokens in a hidden iframe.
const authorityOrigin = originOf(config.ad_authority);
const authorityNeedsListing = Boolean(authorityOrigin) && !/^https:\/\/[^./]+\.ciamlogin\.com$/.test(authorityOrigin);

const origins = {
  connectSrc: [...(apiOrigin ? [apiOrigin] : []), ...(authorityNeedsListing ? [authorityOrigin] : [])],
  frameSrc: authorityNeedsListing ? [authorityOrigin] : [],
};

await writeFile(path.join(DIST, 'csp-origins.json'), `${JSON.stringify(origins, null, 2)}\n`);
await copyFile(path.join(CLIENT_ROOT, 'server.mjs'), path.join(DIST, 'server.mjs'));
await copyFile(path.join(CLIENT_ROOT, 'security-headers.mjs'), path.join(DIST, 'security-headers.mjs'));

// Printed into the build log so "which API origin did this artifact get?" is answerable from
// the pipeline output alone.
console.log(`[headers] dist/server.mjs written; connect-src extras: ${origins.connectSrc.join(' ') || "(none — api_url is same-origin, covered by 'self')"}`);

/**
 * BLUM-7237 — the static server for the Azure App Service deployments (MVP/UAT).
 *
 * It replaces `pm2 serve /home/site/wwwroot --no-daemon --spa`, which sends no security
 * headers at all and sets `Access-Control-Allow-Origin: *` on every response. There is no
 * Front Door or gateway in front of those environments, so the headers can only come from
 * the process serving the files. DAC gets the same set from `nginx.conf` instead.
 *
 * `scripts/build-runtime.mjs` copies this file, `security-headers.mjs` and the generated
 * `csp-origins.json` into `dist/`, so `node dist/server.mjs` locally is exactly the process
 * that runs on App Service. The startup command lives in the infrastructure repo.
 *
 * ESM (`.mjs`) deliberately: no package.json ships in client.zip, so the extension is what
 * makes the module format unambiguous. Node built-ins only — nothing to install at deploy.
 */

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

import { NO_FALLBACK_PREFIXES, STATIC_HEADERS, buildCsp, cacheControlFor } from './security-headers.mjs';

const ROOT = path.dirname(fileURLToPath(import.meta.url));
const INDEX = path.join(ROOT, 'index.html');

// App Service injects PORT. The fallback is for `node dist/server.mjs` on a laptop.
const PORT = Number(process.env.PORT) || 8080;

// Escape hatch: setting CSP_REPORT_ONLY as an app setting downgrades enforcement to reporting
// without a redeploy, which turns a bad-policy incident into a restart. Defaults to enforcing.
//
// The value is read, not merely its presence: an app setting of `false` is the natural way to
// write "enforce", and a bare truthiness test would take that truthy string as "report only" and
// ship the whole policy unenforced. Nothing reports the violations either — no report-uri is
// configured — so the mode is logged at startup rather than left to be inferred.
const FALSEY = new Set(['', '0', 'false', 'no', 'off']);
const CSP_REPORT_ONLY = !FALSEY.has((process.env.CSP_REPORT_ONLY ?? '').trim().toLowerCase());
const CSP_HEADER_NAME = CSP_REPORT_ONLY ? 'Content-Security-Policy-Report-Only' : 'Content-Security-Policy';

// Part of the server, not of the site. Denied before the existence check so they can never be
// served, whether directly or through the SPA fallback.
const PRIVATE_PATHS = new Set(['/server.mjs', '/security-headers.mjs', '/csp-origins.json']);

const MIME_TYPES = {
  // Must be a JavaScript type: index.html loads the bundle as <script type="module">, and with
  // nosniff the wrong type makes the browser refuse the module and render a blank page.
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.gif': 'image/gif',
  '.ico': 'image/x-icon',
  '.webmanifest': 'application/manifest+json',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.eot': 'application/vnd.ms-fontobject',
};

const COMPRESSIBLE = /^(?:text\/|application\/(?:json|manifest\+json)|image\/svg)/;

// Nothing compresses today, so the 1.6 MB bundle ships raw. Compress on first request and keep
// the result: the artifact is immutable for the life of the process.
const gzipCache = new Map();

let origins = {};
try {
  origins = JSON.parse(await readFile(path.join(ROOT, 'csp-origins.json'), 'utf8'));
} catch (error) {
  // Deliberately not fatal: exiting here would restart-loop the site. Serving the base policy
  // keeps the app up, and this line in the App Service log stream says why the API is blocked.
  console.error('[headers] csp-origins.json missing or unreadable, serving the base policy — API calls will be blocked:', error.message);
}

const CSP = buildCsp({ connectSrcExtra: origins.connectSrc ?? [], frameSrcExtra: origins.frameSrc ?? [] });

const respond = (req, res, status, { body = Buffer.alloc(0), type = 'text/plain; charset=utf-8', cacheControl = 'no-store, max-age=0', gzipped = false, extra } = {}) => {
  res.writeHead(status, {
    ...STATIC_HEADERS,
    [CSP_HEADER_NAME]: CSP,
    'Content-Type': type,
    'Cache-Control': cacheControl,
    'Content-Length': body.length,
    // No Access-Control-* headers, ever. pm2 serve's wildcard let any origin read this app's
    // HTML and JavaScript; nothing here is meant to be fetched cross-origin.
    ...(gzipped ? { 'Content-Encoding': 'gzip', Vary: 'Accept-Encoding' } : {}),
    ...extra,
  });
  res.end(req.method === 'HEAD' ? undefined : body);
};

const serveFile = async (req, res, filePath, pathname) => {
  const type = MIME_TYPES[path.extname(filePath).toLowerCase()] ?? 'application/octet-stream';
  const isIndexHtml = filePath === INDEX;
  const cacheControl = cacheControlFor(pathname, { isIndexHtml });

  const gzipped = COMPRESSIBLE.test(type) && /\bgzip\b/.test(req.headers['accept-encoding'] ?? '');

  // Consulted before the read, not after: reading the file first and then discarding it for the
  // cached buffer would leave every request paying a full read of the bundle off the App Service
  // file share, which is the cost the cache exists to remove.
  let body = gzipped ? gzipCache.get(filePath) : undefined;
  if (!body) {
    body = await readFile(filePath);
    if (gzipped) {
      body = gzipSync(body);
      gzipCache.set(filePath, body);
    }
  }

  respond(req, res, 200, {
    body,
    type,
    cacheControl,
    gzipped,
    // index.html is the SPA entry point and is never cached, so belt-and-braces for proxies.
    extra: isIndexHtml ? { Pragma: 'no-cache' } : undefined,
  });
};

const notFound = (req, res) => respond(req, res, 404, { body: Buffer.from('Not Found') });

const handleRequest = async (req, res) => {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    respond(req, res, 405, { body: Buffer.from('Method Not Allowed'), extra: { Allow: 'GET, HEAD' } });
    return;
  }

  // Node hands over the raw request target, and only origin-form means anything here: absolute-form
  // is a proxy request. Concatenating the origin rather than passing a base is what stops
  // `//assets/app.js` being read as protocol-relative — `new URL(target, base)` takes `assets` for
  // a hostname, drops the segment, and the asset lands on the SPA fallback as HTML.
  if (!req.url.startsWith('/')) {
    respond(req, res, 400, { body: Buffer.from('Bad Request') });
    return;
  }

  let pathname;
  try {
    // Normalise after decoding, not before: `/%2fserver.mjs` decodes to `//server.mjs` and
    // `/x/../server.mjs` collapses to `/server.mjs`. Both have to reach the deny-list and the
    // prefix tests below in the shape those are written in, or they simply miss and the file
    // is served by the resolve-and-stat path underneath.
    pathname = path.posix.normalize(decodeURIComponent(new URL(`http://localhost${req.url}`).pathname));
  } catch {
    respond(req, res, 400, { body: Buffer.from('Bad Request') });
    return;
  }

  if (pathname.includes('\0') || PRIVATE_PATHS.has(pathname)) {
    notFound(req, res);
    return;
  }

  const resolved = path.resolve(ROOT, `.${pathname}`);
  if (resolved !== ROOT && !resolved.startsWith(ROOT + path.sep)) {
    notFound(req, res);
    return;
  }

  const stats = await stat(resolved).catch(() => null);
  if (stats?.isFile()) {
    await serveFile(req, res, resolved, pathname);
    return;
  }

  // A missing asset must not become HTML: it would fail as a MIME error under nosniff instead
  // of a plain 404. Every other unmatched path is a React Router route, or the redirect URI
  // registered in Entra for a file that does not exist, so it has to reach index.html with 200.
  // Compared with a trailing slash so the directory itself (`/assets`) is refused alongside its
  // contents, without `/assetsomething` matching the `/assets/` prefix.
  const fallbackKey = pathname.endsWith('/') ? pathname : `${pathname}/`;
  if (NO_FALLBACK_PREFIXES.some((prefix) => fallbackKey.startsWith(prefix))) {
    notFound(req, res);
    return;
  }

  await serveFile(req, res, INDEX, '/index.html');
};

// No ETag, Last-Modified or Range support: hashed assets are immutable and never revalidated,
// index.html is no-store, and nothing here streams media.
const server = createServer((req, res) => {
  handleRequest(req, res).catch((error) => {
    console.error('[server]', req.method, req.url, error);
    if (!res.headersSent) {
      respond(req, res, 500, { body: Buffer.from('Internal Server Error') });
    } else {
      res.end();
    }
  });
});

process.on('SIGTERM', () => server.close(() => process.exit(0)));

// No host argument: Node then binds dual-stack, so the platform's health probe is accepted over
// either IPv4 or IPv6.
server.listen(PORT, () => {
  console.log(
    `[headers] serving ${ROOT} on ${PORT}; CSP ${CSP_REPORT_ONLY ? 'REPORT-ONLY (not enforced)' : 'enforced'}; connect-src extras: ${JSON.stringify(origins.connectSrc ?? [])}`
  );
});

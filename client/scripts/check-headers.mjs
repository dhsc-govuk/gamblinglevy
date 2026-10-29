/**
 * BLUM-7237 — fails the build when `nginx.conf` and `security-headers.mjs` disagree.
 *
 * The client is served by two stacks: nginx on DAC, and `server.mjs` on the Azure App Services.
 * `server.mjs` imports the canonical definition, so it cannot drift; nginx has to restate the
 * same values by hand, so it can. That is not a hypothetical — a hardened CSP sat in
 * `nginx.conf` for weeks while the Azure environments served no security headers at all, and
 * nothing anywhere said so.
 *
 * The unit of comparison is the nginx block, not the file. nginx does not inherit `add_header`
 * into a location block that defines any of its own, so every such block has to repeat the full
 * set — a rule that is very easy to half-apply. Counting each header's occurrences file-wide
 * cannot see that: a new block declaring only `Cache-Control` leaves every count level and
 * serves that path with no CSP at all. So this parses the braces and checks block by block, in
 * both directions — nothing missing from nginx, and nothing in nginx that is not canonical.
 *
 * Runs from `postbuild`, which means it runs in the PR gate: `build-client` is the only
 * automated check this client has.
 */

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { IMMUTABLE_PREFIXES, STATIC_HEADERS, buildCsp, cacheControlFor } from '../security-headers.mjs';

const CLIENT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// `API_ORIGIN` is the placeholder docker-entrypoint.sh substitutes at container start; it is
// what nginx.conf carries in place of the per-environment API origin.
const expected = {
  ...STATIC_HEADERS,
  'Content-Security-Policy': buildCsp({ connectSrcExtra: ['API_ORIGIN'] }),
};

const expectedNames = new Set(Object.keys(expected).map((name) => name.toLowerCase()));

// Cache-Control is per-location rather than global, so it is not in `expected` — but every value
// nginx sets still has to be one cacheControlFor() can produce, or the two stacks cache the same
// file differently. Pragma rides along with the no-store block and pins nothing worth checking.
const ALLOWED_EXTRA_HEADERS = new Set(['cache-control', 'pragma']);
const IMMUTABLE_CACHE_CONTROL = cacheControlFor('/assets/app.js');
const CACHE_CONTROL_VALUES = new Set([cacheControlFor('/index.html', { isIndexHtml: true }), IMMUTABLE_CACHE_CONTROL, cacheControlFor('/images/favicon.ico')]);

// The one location allowed to cache forever, spelled out of IMMUTABLE_PREFIXES. Pinning the
// pattern is what stops nginx widening the immutable set behind the canonical definition's back:
// keying on file extension swept in /images, whose favicon carries no content hash and so has
// nothing to bust a year-long cache with.
const IMMUTABLE_LOCATION = `location ~ ^/(${IMMUTABLE_PREFIXES.map((prefix) => prefix.replaceAll('/', '')).join('|')})/`;

const normalise = (value) => value.replace(/\s+/g, ' ').trim();

/** Drops `#` comments, leaving a `#` inside a quoted header value alone. */
const stripComments = (conf) =>
  conf
    .split('\n')
    .map((line) => {
      let quoted = false;
      for (let index = 0; index < line.length; index += 1) {
        if (line[index] === '"') {
          quoted = !quoted;
        } else if (line[index] === '#' && !quoted) {
          return line.slice(0, index);
        }
      }
      return line;
    })
    .join('\n');

/**
 * Splits the config into brace-delimited blocks, attributing every `add_header` to the innermost
 * one. Quote-aware because the header values are full of both `;` and `#`.
 */
const parseBlocks = (conf) => {
  const blocks = [];
  const open = [];
  let token = '';
  let quoted = false;

  for (const char of conf) {
    if (char === '"') {
      quoted = !quoted;
      token += char;
    } else if (quoted) {
      token += char;
    } else if (char === '{') {
      open.push({ label: normalise(token), declarations: [] });
      token = '';
    } else if (char === '}') {
      const block = open.pop();
      if (block) {
        blocks.push(block);
      }
      token = '';
    } else if (char === ';') {
      const match = /^add_header\s+([\w-]+)\s+"([^"]*)"\s*(always)?$/.exec(normalise(token));
      if (match && open.length > 0) {
        open.at(-1).declarations.push({ name: match[1], value: match[2], always: Boolean(match[3]) });
      }
      token = '';
    } else {
      token += char;
    }
  }

  return blocks;
};

const nginxConf = await readFile(path.join(CLIENT_ROOT, 'nginx.conf'), 'utf8');
const blocks = parseBlocks(stripComments(nginxConf)).filter((block) => block.declarations.length > 0);

const errors = [];

if (blocks.length === 0) {
  errors.push('nginx.conf declares no add_header at all, so it sends none of the security headers security-headers.mjs defines.');
}

for (const block of blocks) {
  const where = `nginx.conf "${block.label}"`;

  for (const [name, expectedValue] of Object.entries(expected)) {
    const found = block.declarations.filter((declaration) => declaration.name.toLowerCase() === name.toLowerCase());

    if (found.length === 0) {
      errors.push(
        `${where} never sets ${name}. Every block that declares any add_header must repeat all of them, because nginx stops inheriting the server-level ones. security-headers.mjs expects "${normalise(
          expectedValue
        )}".`
      );
      continue;
    }

    if (found.length > 1) {
      errors.push(`${where} sets ${name} ${found.length} times, and nginx sends every one of them.`);
    }

    for (const declaration of found) {
      if (normalise(declaration.value) !== normalise(expectedValue)) {
        errors.push(`${where} ${name} does not match security-headers.mjs.\n  nginx:    ${normalise(declaration.value)}\n  expected: ${normalise(expectedValue)}`);
      }
    }
  }

  for (const declaration of block.declarations) {
    const name = declaration.name.toLowerCase();

    if (!declaration.always) {
      errors.push(`${where} sets ${declaration.name} without \`always\`, so nginx omits it from every error response.`);
    }

    if (!expectedNames.has(name) && !ALLOWED_EXTRA_HEADERS.has(name)) {
      errors.push(`${where} sets ${declaration.name}, which security-headers.mjs does not define — so server.mjs never sends it. Add it there or drop it here.`);
    }

    if (name === 'cache-control' && !CACHE_CONTROL_VALUES.has(normalise(declaration.value))) {
      errors.push(
        `${where} Cache-Control "${normalise(declaration.value)}" is not a value cacheControlFor() produces (${[...CACHE_CONTROL_VALUES].map((value) => `"${value}"`).join(', ')}).`
      );
    }
  }
}

const immutableBlocks = blocks.filter((block) =>
  block.declarations.some((declaration) => declaration.name.toLowerCase() === 'cache-control' && normalise(declaration.value) === IMMUTABLE_CACHE_CONTROL)
);

if (immutableBlocks.length !== 1) {
  errors.push(
    `nginx.conf has ${immutableBlocks.length} blocks caching as "${IMMUTABLE_CACHE_CONTROL}"; exactly one may, and it must be \`${IMMUTABLE_LOCATION}\` to match IMMUTABLE_PREFIXES.`
  );
} else if (immutableBlocks[0].label !== IMMUTABLE_LOCATION) {
  errors.push(
    `nginx.conf caches \`${immutableBlocks[0].label}\` forever, but IMMUTABLE_PREFIXES (${IMMUTABLE_PREFIXES.join(
      ', '
    )}) allows only \`${IMMUTABLE_LOCATION}\`. Anything wider catches files with no content hash in their name, which have no way to bust a year-long cache.`
  );
}

if (errors.length > 0) {
  console.error(`\n[headers] nginx.conf has drifted from security-headers.mjs:\n\n${errors.join('\n\n')}\n`);
  process.exit(1);
}

console.log(`[headers] nginx.conf matches security-headers.mjs (${blocks.length} blocks declaring add_header, ${Object.keys(expected).length} security headers each).`);

/**
 * BLUM-7237 — fails the build when `nginx.conf` and `security-headers.mjs` disagree.
 *
 * The client is served by two stacks: nginx on DAC, and `server.mjs` on the Azure App Services.
 * `server.mjs` imports the canonical definition, so it cannot drift; nginx has to restate the
 * same values by hand, so it can. That is not a hypothetical — a hardened CSP sat in
 * `nginx.conf` for weeks while the Azure environments served no security headers at all, and
 * nothing anywhere said so.
 *
 * It also compares the three `add_header` blocks in `nginx.conf` against each other. nginx does
 * not inherit `add_header` into a location block that defines any of its own, so every block
 * has to repeat the full set — a rule that is very easy to half-apply.
 *
 * Runs from `postbuild`, which means it runs in the PR gate: `build-client` is the only
 * automated check this client has.
 */

import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { STATIC_HEADERS, buildCsp } from '../security-headers.mjs';

const CLIENT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// `API_ORIGIN` is the placeholder docker-entrypoint.sh substitutes at container start; it is
// what nginx.conf carries in place of the per-environment API origin.
const expected = {
  ...STATIC_HEADERS,
  'Content-Security-Policy': buildCsp({ connectSrcExtra: ['API_ORIGIN'] }),
};

const normalise = (value) => value.replace(/\s+/g, ' ').trim();

const nginxConf = await readFile(path.join(CLIENT_ROOT, 'nginx.conf'), 'utf8');
const declarations = [...nginxConf.matchAll(/add_header\s+([\w-]+)\s+"([^"]*)"\s*always\s*;/g)].map(([, name, value]) => ({ name, value }));

const errors = [];
const counts = new Map();

for (const [name, expectedValue] of Object.entries(expected)) {
  const found = declarations.filter((declaration) => declaration.name.toLowerCase() === name.toLowerCase());
  counts.set(name, found.length);

  if (found.length === 0) {
    errors.push(`nginx.conf never sets ${name}. security-headers.mjs expects "${expectedValue}".`);
    continue;
  }

  for (const [index, declaration] of found.entries()) {
    if (normalise(declaration.value) !== normalise(expectedValue)) {
      errors.push(
        `nginx.conf ${name} (occurrence ${index + 1} of ${found.length}) does not match security-headers.mjs.\n  nginx:    ${normalise(declaration.value)}\n  expected: ${normalise(
          expectedValue
        )}`
      );
    }
  }
}

const blockCounts = new Set(counts.values());
if (blockCounts.size > 1) {
  const summary = [...counts.entries()].map(([name, count]) => `${name}=${count}`).join(', ');
  errors.push(
    `nginx.conf sets its security headers an uneven number of times (${summary}). Every server and location block that declares any add_header must repeat all of them, because nginx stops inheriting the server-level ones.`
  );
}

if (errors.length > 0) {
  console.error(`\n[headers] nginx.conf has drifted from security-headers.mjs:\n\n${errors.join('\n\n')}\n`);
  process.exit(1);
}

console.log(`[headers] nginx.conf matches security-headers.mjs (${[...blockCounts][0]} blocks, ${Object.keys(expected).length} headers each).`);

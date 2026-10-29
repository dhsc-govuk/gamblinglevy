import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react-swc';
import eslint from 'vite-plugin-eslint2';
import { readFileSync } from 'fs';

const packageJson = JSON.parse(readFileSync('./package.json', 'utf-8'));

// The views the entry bundle is allowed to contain (BLUM-8759): the sign-in path, the pages a failure lands on,
// the policy pages and the account page. Everything else under src/views is loaded by src/Routes.tsx through a
// dynamic import, by audience.
const EAGER_VIEWS = ['/src/views/Public/', '/src/views/Account/', '/src/views/Mfa/'];

// The two MFA pages that are not on the sign-in path. They share a folder with Verify, which is, so the folder
// is allowed above and these are taken back out — naming Verify's files one by one instead would fail the build
// the first time it imported something new from beside it.
const LAZY_VIEWS_IN_EAGER_FOLDERS = ['/src/views/Mfa/Setup/', '/src/views/Mfa/RecoveryCodes/'];

const isLazyView = (id: string): boolean =>
  id.includes('/src/views/') && (!EAGER_VIEWS.some((folder) => id.includes(folder)) || LAZY_VIEWS_IN_EAGER_FOLDERS.some((folder) => id.includes(folder)));

// Fails the build when a lazily loaded view ends up in the entry bundle. That is one line away at any time — a
// static `import X from './views/…'` in Routes.tsx, or a shared component reaching into a view folder — and
// nothing else notices: the app works exactly as before and only the first load gets slower. Same idea as
// scripts/check-headers.mjs: a contract nobody can see is checked where the PR gate already runs.
const entryChunkGuard = (): Plugin => ({
  name: 'entry-chunk-guard',
  apply: 'build',
  generateBundle(_options, bundle) {
    const entry = Object.values(bundle).find((file) => file.type === 'chunk' && file.isEntry);
    if (!entry || entry.type !== 'chunk') {
      this.error('The build produced no entry chunk.');
    }

    // Everything the entry chunk imports statically is downloaded with it, so it counts as the entry too.
    const leaked: string[] = [];
    const visited = new Set<string>();
    const queue = [entry.fileName];
    while (queue.length > 0) {
      const fileName = queue.pop() as string;
      if (visited.has(fileName)) continue;
      visited.add(fileName);
      const chunk = bundle[fileName];
      if (!chunk || chunk.type !== 'chunk') continue;
      queue.push(...chunk.imports);
      leaked.push(...Object.keys(chunk.modules).filter(isLazyView));
    }

    if (leaked.length > 0) {
      this.error(
        `${leaked.length} lazily loaded view module(s) are in the entry bundle, so route-level code splitting is not happening for them:\n` +
          leaked.map((id) => `  ${id}`).join('\n') +
          '\nSomething in the entry bundle imports them statically. Route views through their area barrel (see src/Routes.tsx) instead.'
      );
    }
  },
});

// Fails the build when the tenant id the bundle is about to embed is not the authority's tenant id (BLUM-8757).
// MSAL v5 validates the issuer in CIAM's discovery document, which is named by the tenant's id, and the app tells
// it to trust `{ad_tenant_id}.ciamlogin.com` (components/AuthProvider). The value was required but never read
// before that, so anything shipped without harm — MVP's pipeline variable held the id of the Azure subscription's
// own tenant, not the CIAM one. Now a wrong value stops every sign-in, and nothing says why until somebody tries.
//
// Two checks, because the first cannot see that case. The shape catches a placeholder or the tenant's domain
// name. Then the authority is asked: its discovery document states the issuer, so a well-formed id for the wrong
// tenant fails here with the right one in the message. If the authority cannot be reached from the build the
// second check is skipped with a warning rather than failing — a build must not depend on the identity provider
// being up, only refuse a mismatch it has actually seen.
//
// Here rather than in a post-build script because this is where the answer is known: `config.env` is what
// import.meta.env will hold, .env files included, so this reads both values exactly as src/config.ts does, and it
// fails before the bundle is built rather than after.
const TENANT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const CIAM_HOST_SUFFIX = '.ciamlogin.com';

// The tenant id the authority signs tokens as, or null when it cannot be asked or does not name one.
const tenantIdOfAuthority = async (authority: string): Promise<string | null> => {
  try {
    const url = new URL(authority);
    // MSAL's own rule for an authority with no tenant in its path: {name}.ciamlogin.com means {name}.onmicrosoft.com.
    const tenantPath =
      url.pathname.replace(/^\/+|\/+$/g, '') || (url.hostname.endsWith(CIAM_HOST_SUFFIX) ? `${url.hostname.slice(0, -CIAM_HOST_SUFFIX.length)}.onmicrosoft.com` : '');
    const response = await fetch(`${url.origin}/${tenantPath ? `${tenantPath}/` : ''}v2.0/.well-known/openid-configuration`, { signal: AbortSignal.timeout(10_000) });
    if (!response.ok) return null;
    const issuerHost = new URL(((await response.json()) as { issuer: string }).issuer).hostname;
    const tenantId = issuerHost.endsWith(CIAM_HOST_SUFFIX) ? issuerHost.slice(0, -CIAM_HOST_SUFFIX.length) : '';
    return TENANT_ID.test(tenantId) ? tenantId.toLowerCase() : null;
  } catch {
    return null;
  }
};

const tenantIdGuard = (): Plugin => ({
  name: 'tenant-id-guard',
  apply: 'build',
  async configResolved(config) {
    const staticConfig = JSON.parse(readFileSync('./src/config.json', 'utf-8'));
    const tenantId = config.env.VITE_AD_TENANT_ID || staticConfig.ad_tenant_id;
    const authority = config.env.VITE_AD_AUTHORITY || staticConfig.ad_authority;
    if (typeof tenantId !== 'string' || !TENANT_ID.test(tenantId)) {
      throw new Error(`ad_tenant_id must be the Entra tenant id, a GUID (${JSON.stringify(tenantId)}) — sign-in would fail with endpoints_resolution_error.`);
    }

    const actual = await tenantIdOfAuthority(authority);
    if (actual === null) {
      config.logger.warn(`[auth] Could not ask ${authority} which tenant it is, so only the shape of ad_tenant_id was checked.`);
      return;
    }
    if (actual !== tenantId.toLowerCase()) {
      throw new Error(
        `ad_tenant_id is ${tenantId}, but ${authority} is tenant ${actual} — sign-in would fail with endpoints_resolution_error. ` +
          `Set ad_tenant_id (AD_TENANT_ID in the pipeline) to ${actual}. A different GUID here is usually the Azure subscription's tenant rather than the CIAM one.`
      );
    }
  },
});

// https://vitejs.dev/config/
export default defineConfig({
  base: process.env.VITE_BASE_PATH || '/',
  plugins: [
    react(),
    // Replaces vite-plugin-eslint (unmaintained since 2023).
    //
    // `build: true` is NOT this plugin's default and is the whole point of the swap.
    // There is no separate lint step in the pipeline (the Vitest unit tests, `npm test`,
    // check logic, not lint), and the old plugin linted during `vite build` with
    // failOnError — so an ESLint error already fails the PR today. Left at the default (false), this upgrade would quietly
    // delete that gate. Errors abort the build; warnings stay warnings, exactly as
    // before (`lint-staged --max-warnings 0` is what holds the line on those).
    //
    // `cache: false` because the old plugin never enabled ESLint's cache, and a stale
    // .eslintcache waving an error through the only gate is a bad trade for a few
    // seconds of build time.
    eslint({
      dev: true,
      build: true,
      include: ['src/**/*.{js,jsx,ts,tsx}'],
      cache: false,
    }),
    entryChunkGuard(),
    tenantIdGuard(),
  ],
  server: {
    port: 3000,
    // Vite's host-header check allows only IP literals, `localhost` and `*.localhost`
    // unless a host is listed here. docker-compose fronts the dev server with Traefik on
    // `${APP_PREFIX}-client.localtest.me` (the URL the README tells developers to open),
    // which would otherwise get a 403 "Blocked request" instead of the app. A leading dot
    // matches the domain and its subdomains.
    allowedHosts: ['.localtest.me'],
  },
  build: {
    // Pinned to Vite 5's `'modules'` default. Vite 7 changed the default to
    // 'baseline-widely-available' (Chrome 107+/Edge 107+/Firefox 104+/Safari 16+),
    // which would drop browsers this service currently supports. Narrowing the
    // browser matrix of a public sector service must be a deliberate decision,
    // not a side effect of a tooling upgrade.
    target: ['es2020', 'edge88', 'firefox78', 'chrome87', 'safari14'],
    // One stylesheet, as before route-level code splitting (BLUM-8759) — only the JavaScript is split. Vite's
    // default would emit a stylesheet per lazy chunk and append it to <head> when the chunk loads, which is
    // after the <style> tags styled-components (inside govuk-react) injects at runtime. Today the single stylesheet comes
    // before them, so every rule that ties on specificity with a CSS-in-JS rule would flip, and there is no
    // visual regression suite to notice. The whole file is 29 kB gzipped; splitting it is not worth that.
    //
    // The order of rules inside the file follows chunk membership: the entry's CSS, then each lazy chunk's. So
    // moving a view between the entry and a chunk moves its rules. When BLUM-8759 did that, 32 page-scoped
    // rules changed position and none of them tied with a rule they moved past — worth re-checking, by diffing
    // the rule order of the built stylesheet, the next time the split changes.
    cssCodeSplit: false,
  },
  define: {
    'import.meta.env.PACKAGE_VERSION': JSON.stringify(packageJson.version),
  },
});

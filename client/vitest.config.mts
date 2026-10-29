import { defineConfig } from 'vitest/config';

// The unit tests (BLUM-8754). Deliberately its own file rather than a `test` block in vite.config.mts, which
// Vitest would otherwise load: that config runs vite-plugin-eslint2 on every transformed module and carries the
// build-only guards, none of which belong in a test run. Vitest prefers this file when both exist.
//
// The default environment is plain Node, which on Node 24 has no window, document, localStorage or sessionStorage,
// so a helper that claims to be pure (helpers/authFailure.ts) fails here if it reaches for the DOM or storage. It
// is not a full purity check: Node does have navigator, fetch and timers, and Node 25+ has Web Storage by default.
// A test that needs the DOM asks for it in its first line with `// @vitest-environment jsdom`.
export default defineConfig({
  test: {
    include: ['src/**/*.test.{ts,tsx}'],
    environment: 'node',
    restoreMocks: true,
    unstubGlobals: true,
    // Bitbucket collects JUnit XML from test-results/ by itself, so CI gets a test tab with no extra step.
    reporters: process.env.CI ? ['default', 'junit'] : ['default'],
    outputFile: { junit: 'test-results/junit.xml' },
  },
});

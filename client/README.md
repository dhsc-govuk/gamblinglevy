# aisight Core App

A TypeScript React app for the aisight project.

The React app uses hot reload, meaning that any changes to the code will be automatically reloaded. You'll also see any lint errors in the console. Keep them green!

## Commands

- `npm run dev`, which starts the app in development mode at [localhost:3300](http://localhost:3300)

## Serving the built client

The built SPA is served by two different stacks, and both must send the same security headers:

| | DAC (client-managed) | Azure MVP/UAT |
| --- | --- | --- |
| Server | nginx, from `nginx.conf`, built by `Dockerfile.ci` | `server.mjs`, run by the App Service startup command |
| Artifact | this repo, via the GitHub mirror | `client.zip` — the contents of `dist/` |
| API origin | `API_ORIGIN` env var, substituted by `docker-entrypoint.sh` (leave unset if the API is same-origin) | derived from `src/config.json` into `dist/csp-origins.json` at build time |

`npm run build` runs `postbuild`, which copies `server.mjs` and `security-headers.mjs` into `dist/`, writes `dist/csp-origins.json`, and then checks `nginx.conf` against the canonical policy. So:

```sh
npm run build
node dist/server.mjs   # this is exactly the process that runs on App Service
```

**`security-headers.mjs` is the single source of truth for the header set.** nginx cannot read it, so `nginx.conf` restates the same values by hand and `scripts/check-headers.mjs` fails the build if the two ever disagree — including if one of nginx's three `add_header` blocks is missing a header the others have. Change a header in both places, or the build tells you.

Setting `CSP_REPORT_ONLY` in the environment downgrades the policy to `Content-Security-Policy-Report-Only`, so a bad policy can be defused with an app setting and a restart rather than a redeploy.

# PWA service worker runbook (Feature 153)

The frontend registers `/sw.js?v=<build id>` with scope `/` in production builds on secure origins.
The worker caches **static assets only**:

| Request | Handling |
|---|---|
| `/assets/*` (content-hashed JS, CSS, fonts) | cache-first, cache `sapio-static-<build id>`, at most 200 entries |
| `/offline.html`, `/manifest.webmanifest`, `/favicon.ico`, `/icons/*` | precached on install |
| Google Fonts (`fonts.googleapis.com`, `fonts.gstatic.com`) | stale-while-revalidate, at most 30 entries |
| Page navigations (HTML) | network only; `/offline.html` when the network fails |
| `/_serverFn/*`, `/api/*`, the API origin, any other origin, any non-GET | not handled (straight to the network) |

Responses marked `no-store` or `private` are never stored. Conversations, financial data, tokens and
cookies never enter Cache Storage.

## Build and update

- Each build gets a new build ID: `VITE_APP_BUILD_ID` when set, otherwise a timestamp. A new ID means a new
  worker URL and a new static cache; on activation the worker deletes `sapio-static-*` caches of other builds.
- A new worker installs and **waits**. Open pages show «نسخه جدید آماده است» and activate it only when the
  user taps «به‌روزرسانی», never while a question is being answered.
- A missing chunk after a deploy (`vite:preloadError`) shows the same prompt instead of a blank page.

## Header verification

`src/frontend/public/_headers` configures the Workers asset server. After a deploy, check:

```sh
curl -sI https://<frontend-origin>/sw.js               # Content-Type: text/javascript; Cache-Control: no-cache
curl -sI https://<frontend-origin>/manifest.webmanifest # Content-Type: application/manifest+json; Cache-Control: no-cache
curl -sI https://<frontend-origin>/assets/<file>.js     # Cache-Control: public, max-age=31536000, immutable
```

`/offline.html` answers with a 307 to `/offline`, because the asset server removes `.html`. That is expected:
the worker stores a non-redirected copy when it precaches the page.

If a reverse proxy sits in front of the frontend, it must not add long-lived caching to `/sw.js` or the
manifest, or updates and the kill-switch will not reach users.

Verified on 2026-11-03 with `wrangler dev --config dist/server/wrangler.json --local`, the same command the
frontend Docker image runs. Docker itself was not available on the verification host.

## Kill-switch (remove the worker from all browsers)

Use this when a worker defect affects users. It is a deploy, not a configuration change.

1. Replace the contents of `src/frontend/public/sw.js` with `src/frontend/public/sw-kill.js` (keep the file
   name `sw.js`).
2. Build and deploy the frontend as usual.
3. Browsers check `/sw.js` when a page loads. The kill-switch version installs immediately, deletes every
   `sapio-*` cache, unregisters itself, and reloads open pages without a worker.
4. Verify in a browser: DevTools → Application → Service Workers shows none for the origin, and Cache Storage
   has no `sapio-*` caches.
5. To restore the worker later, deploy the normal `sw.js` again.

## Automatic phone redirects

They are separate from the worker and stay off unless the build sets `VITE_MOBILE_AUTO_REDIRECT=true`. Do not
enable them in production until the live frontend and API origins are confirmed from the deployment
configuration and the refresh cookie is verified to be set and sent on those origins (spec decision D-Q5).
With Docker Compose, set `FRONTEND_MOBILE_AUTO_REDIRECT=true` in the server's `.env` and rebuild the frontend image
(`docker compose -f docker-compose.yml -f docker-compose.local-build.yml build frontend`, then
`docker compose up -d frontend`); `docker-compose.local-build.yml` passes it to the build as `VITE_MOBILE_AUTO_REDIRECT`.
Rollback is the same rebuild with the value set to `false`.

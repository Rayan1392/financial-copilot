# Baseline before the change (T00, T01)

Captured 2026-11-12 on `develop` at `e0f922b`, in `src/frontend`, before any edit.

| Check | Result on unmodified code |
|---|---|
| `npm run typecheck` | 37 errors, the list in `specs/test-baseline.md` |
| `npm run test` (Vitest) | 5 failed files, 2 failed tests of 509: BUG-024, BUG-025, and three `scripts/*.browser.test.mjs` files that Vitest collects but cannot run |
| `npm run lint` | 33,064 errors and 12 warnings. 33,058 are `prettier/prettier` line-ending errors (the working tree is checked out with CRLF); 6 errors and 12 warnings are other rules |
| `npm run build` | green |
| `npm run test:mobile-browser` | 78 of 91 checks pass; the 13 failures are listed in `mobile-browser-before.txt` |
| `npm run test:quarterly-png` | fails with `TimeoutError` (BUG-027) |
| Client JavaScript, gzip | 630,604 bytes summed per file; 587,166 bytes as one stream (111 files) |
| Client CSS, gzip | 22,802 bytes |

Notes:

- Vitest must be started from an upper-case drive path (`D:\...`, for example from PowerShell). From Git Bash
  (`/d/...`) 70 of 71 files fail with "Vitest failed to find the runner".
- A build made while `wrangler dev` is serving `dist/` fails with `EPERM` on `dist/server/.wrangler`; stop the
  server first.
- "Before" screenshots: `before/`, 18 files (nine views, dark and light, 360×800).

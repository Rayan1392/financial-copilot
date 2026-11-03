# CyclicalWaves v2 (TahlilApp API v2) — authoritative provider contracts

These two documents are the versioned source of truth for Feature 148
([spec](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/spec.md)).

| File | Title | Version / date | SHA-256 |
|---|---|---|---|
| `tahlilapp-api-client.pdf` | مستندات API — سرویس داده (auth, financial screener, comprehensive analysis) | 2.0 | `77b6a2b2bc623797887c7dba94b5b797c10be8078a38328c9e99cbf927f55caa` |
| `income-statement-api-client.pdf` | مستندات سرویس صورت سود و زیان فصلی | ۲ آبان ۱۴۰۵ | `9d6cc5f2d9427aefdbe3d00ac3462482b75fb523729fa892881f8646df144d90` |

**Provenance:** supplied by the Product Owner as chat attachments on 2026-10-30 (Feature 148 phase 1) and
committed unchanged on 2026-10-31.

**Endpoints:** auth server `https://auth.cycfx.com/api` (`POST /auth/login`, `POST /auth/refresh`, header
`x-client-id: default`), data server `https://api.tahlilapp.com/api` (`GET /custom-filtering/tickers`,
`GET /custom-filtering/ticker/{ticker}`, `GET /income-statements/ticker/{ticker}?limit=N`,
`GET /income-statements/lines`, `GET /blogs?filter[type]=analysis`). Details the documents leave open were
verified against the live provider; see
[contract-verification.md](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/contract-verification.md).

**Legacy (retired by Feature 148, kept for history only):** `docs/cw_api.txt` and
`docs/Cyclical Waves_api_document_2026_05_26.pdf` describe the retired `back1.cyclicalwaves.com` integration.
No code calls those endpoints any more.

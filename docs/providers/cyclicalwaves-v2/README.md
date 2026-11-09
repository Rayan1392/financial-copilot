# CyclicalWaves v2 (TahlilApp API v2) — authoritative provider contracts

These three documents are the versioned source of truth for Feature 148
([spec](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/spec.md)).

| File | Title | Version / date | SHA-256 |
|---|---|---|---|
| `tahlilapp-api-client.pdf` | مستندات API — سرویس داده (auth, financial screener, comprehensive analysis) | 2.0 | `77b6a2b2bc623797887c7dba94b5b797c10be8078a38328c9e99cbf927f55caa` |
| `income-statement-api-client.pdf` | مستندات سرویس صورت سود و زیان فصلی | ۲ آبان ۱۴۰۵ | `9d6cc5f2d9427aefdbe3d00ac3462482b75fb523729fa892881f8646df144d90` |
| `gauge-equilibrium-api-client.pdf` | مستندات سرویس گیج P/E و P/S و بازه قیمت تعادلی | ۱۳ آبان ۱۴۰۵ | `E0FD0EAC5B1A69B6EC696794061D9E3450497D6FAF6431FC4DEFF84B54B2CDA4` |

**Provenance:** the first two PDFs were supplied by the Product Owner as chat attachments on 2026-10-30
(Feature 148 phase 1) and committed unchanged on 2026-10-31. The gauge/equilibrium PDF was supplied from
the Product Owner's local file on 2026-11-07 and copied unchanged for the post-closure scope extension.

**Endpoints:** auth server `https://auth.cycfx.com/api` (`POST /auth/login`, `POST /auth/refresh`, header
`x-client-id: default`), data server `https://api.tahlilapp.com/api` (`GET /custom-filtering/tickers`,
`GET /custom-filtering/ticker/{ticker}`, `GET /income-statements/ticker/{ticker}?limit=N`,
`GET /income-statements/lines`, `GET /blogs?filter[type]=analysis`). Details the documents leave open were
verified against the live provider; see
[contract-verification.md](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/contract-verification.md).

The gauge/equilibrium PDF documents `GET /pe-ps-gauges/tickers`, `GET /pe-ps-gauges/ticker/{ticker}`,
`GET /equilibrium-ranges/tickers`, and `GET /equilibrium-ranges/ticker/{ticker}`. Its transcribed
contract is [gauge-equilibrium-api-contract.md](./gauge-equilibrium-api-contract.md). It is part of
the Feature 148 post-closure extension, authorized for implementation on 2026-11-08 and currently in
progress. The routes are implemented in application code but have not been live-verified. The configuration is
`CyclicalWavesV2:GaugeBaseUrl=https://api.tahlilapp.com/api`, separate from the unchanged
`DataBaseAddress`, using the existing shared account and token lifecycle. The approved scope includes
all three Chat capabilities only in CyclicalWaves mode and a scoped Feature 115 standalone P/S gauge
restoration. Feature 115 historical/forward/monthly sub-capabilities and Features 114, 125, 126 and 127
remain disabled. NoavaranAmin never reads these tables and remains unchanged.

**Legacy (retired by Feature 148, kept for history only):** `docs/cw_api.txt` and
`docs/Cyclical Waves_api_document_2026_05_26.pdf` describe the retired `back1.cyclicalwaves.com` integration.
No code calls those endpoints any more.

# TahlilApp top-up partner JWT tool

Local testing tool that mints the partner Bearer JWT required by
`POST /api/v1/billing/partners/tahlilapp/top-ups` (scheme `TahlilAppTopUpPartner`, policy scope `topup.deliver`).

## Configuration

The project uses the **same `UserSecretsId` as `FinancialCopilot.API`**, so it signs with the exact keys the API validates:

| Key (user-secrets) | Owner | Purpose |
|---|---|---|
| `Billing:TahlilAppTopUps:Authentication:Issuer` | API | `iss` |
| `Billing:TahlilAppTopUps:Authentication:Audience` | API | `aud` (must be `sapio-tahlilapp-topup`) |
| `Billing:TahlilAppTopUps:Authentication:SigningKeys:<kid>` | API | HMAC-SHA256 key (≥ 32 UTF-8 bytes) |
| `TopUpTokenTool:KeyId` | tool | default `kid` |
| `TopUpTokenTool:Subject` / `Scope` / `LifetimeMinutes` | tool | `sub`, `scope`, lifetime (defaults `partner-topup`, `topup.deliver`, 5) |

Environment variables and command-line options override user-secrets.

## Usage

```bash
# one token (stdout only; diagnostics go to stderr)
dotnet run --project tools/TahlilApp.TopUpTokenTool

# several single-use tokens, 10-minute lifetime
dotnet run --project tools/TahlilApp.TopUpTokenTool -- --count 3 --minutes 10

# call the endpoint
TOKEN=$(dotnet run --project tools/TahlilApp.TopUpTokenTool)
curl -X POST https://localhost:<port>/api/v1/billing/partners/tahlilapp/top-ups \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"invoiceId":"inv-001","userId":"<tahlilapp-user-id>","creditsPurchased":100,"paidAtUtc":"2026-11-04T08:00:00Z"}'
```

Each token's `jti` is consumed on its first successful POST — mint a new token per request.
The tool re-validates every token with the API's rules (issuer, audience, `kid` key lookup, lifetime) before printing it.

Never point this tool at production signing keys on a shared machine; production keys belong only to TahlilApp.

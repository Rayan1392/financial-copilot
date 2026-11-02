# Feature 151 implementation report

**Status:** The repository implementation and contracts now match the four-field webhook and anonymous informational balance requirements. TahlilApp coordination, external user-ID entropy confirmation, and joint staging remain pending. Delivery is still disabled by default. No production migration, deployment, data change, or commit was performed.

## Changed contracts and files

- `GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=<URL-encoded-id>` is anonymous, read-only, rate limited by client IP, and `no-store`. It resolves the existing TahlilApp identity mapping, returns effective plan and dates, expiry-aware finite spendable credits or `null` for Unlimited, and `asOfUtc`. It does not refresh entitlements or evaluate checkout eligibility.
- `POST /api/v1/billing/partners/tahlilapp/top-ups` accepts exactly `invoiceId`, `userId`, `creditsPurchased`, and `paidAtUtc`. Partner JWT remains in the Authorization header. Current offer and plan checks use server-side subscription state. Invoice fingerprint includes exactly those four immutable fields.
- The old JWT-protected top-up eligibility endpoint was removed. No second eligibility check was added to checkout.
- If more than one persisted Plus/Pro source window contains `paidAtUtc`, SapioAI stores `ambiguous-unfulfilled` with no grant and no source assignment. Reconciliation can identify the invoice; retries return its original outcome. EF migration `20261102131403_Feature151SourceResolutionAndOptionalAuditWindow` makes source-window evidence nullable for this terminal ambiguous result and has not been applied.
- Feature 151 spec, integration contract, user stories, tasks, implementation report, shared API design docs, endpoint OpenAPI summaries/descriptions, and `docs/integrations/tahlilapp-feature151-fa.html` were updated. Feature 152's overlap resolver is reused; its plan policy is not changed.
- Frontend top-up status now stops polling for `ambiguous-unfulfilled` and tells the customer to contact support; admin reconciliation marks its unresolvable source window instead of rendering invalid dates.
- External user IDs are persisted by Feature 139 but generated outside this repository. The codebase cannot prove their entropy. A guessable ID would expose spendable balance, effective plan, and subscription dates to an unauthenticated caller. TahlilApp must guarantee cryptographically random, non-enumerable IDs with at least 128 bits of entropy; public IP rate limiting remains enabled in SapioAI and should also be enforced at the edge. JWT was not added to this endpoint.
- Existing uncommitted `.env.example`, `docker-compose.yml`, and Kesaveh-related work were preserved.

## Examples

Balance request:

```http
GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=d647420371eb1270418f59c20d6c7d02
```

```json
{"plan":"Max","subscriptionStartsAtUtc":"2026-10-20T00:00:00Z","subscriptionExpiresAtUtc":"2027-01-18T00:00:00Z","quotaMode":"unlimited","availableCredits":null,"asOfUtc":"2026-11-02T12:00:00Z"}
```

Webhook body:

```json
{"invoiceId":"ta-invoice-001","userId":"d647420371eb1270418f59c20d6c7d02","creditsPurchased":500,"paidAtUtc":"2026-11-01T08:30:00Z"}
```

## Verification performed in this revision

| Check | Result |
|---|---|
| Release API build (`dotnet build ...FinancialCopilot.API.csproj -c Release --no-restore`) | Passed, 0 warnings, 0 errors. |
| `TahlilAppTopUpEndpointTests` Release | Passed: 8 tests, 0 failed, 0 skipped. Covers anonymous URL-decoded balance lookup, `no-store`, Unlimited/finite overlap policy, expired allowance exclusion, unchanged balance, unknown user, OpenAPI auth/body shape, four-field contract/unknown fields, JWT, invoice retry/conflicts, active Max/Free restrictions, and ambiguous source resolution. |
| `TahlilAppTopUpPostgreSqlTests` Release | 9 skipped, 0 passed, 0 failed because the PostgreSQL integration fixture was unavailable. PostgreSQL concurrency, rollback, expiry, and refund races were not re-executed in this turn. |
| Focused `TahlilAppTopUpTests` unit test | Blocked during compilation by existing unrelated error `CompanyDisclosureFeed112Tests.cs(218)`: constructor call omits required `cache` argument. That test file was not changed. |
| Frontend `npm run build` | Passed. Existing route-file and large bundle warnings were reported by Vite. |
| `dotnet ef migrations has-pending-model-changes` for `BillingDbContext` | Passed: no model changes since the last migration. Migration remains unapplied. |

## External work and remaining constraints

TahlilApp must send only the four webhook properties, preserve one immutable invoice per verified payment, keep provider IDs on its side, and issue a fresh short-lived JWT with a unique random `jti` per attempt. It must guarantee high-entropy non-enumerable user IDs before using the no-JWT balance endpoint. It should route `ambiguous-unfulfilled` to manual reconciliation; SapioAI deliberately does not guess the original source. A separate authenticated invoice-keyed reversal event remains necessary for automated provider refund/chargeback handling. Joint staging must verify the balance endpoint, ID entropy, rate limits, JWT claims/key rotation, retry/idempotency, conflicts, source expiry/overlap, and reconciliation before enabling delivery.

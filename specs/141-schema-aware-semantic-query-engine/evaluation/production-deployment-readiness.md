# Feature 141 — Production deployment readiness (2026-10-30)

Audience: the operator deploying Financial Copilot with `docker-compose.yml`, and the product owner who approves the
public company scope. Base: `develop` at `e71617e`. No database migrations, no ingestion changes.

**Verdict: ready to deploy with `FEATURE141_ENABLED=false`; enable only after steps 2–5 pass.**

## 1. Deploy the code (flag off)

The change set is exactly the 64 paths in [`deployment-change-set.txt`](deployment-change-set.txt) (62 code, catalog,
tooling and test files plus `docker-compose.yml` and `.env.example`), plus this file and that list. It was applied to a clean worktree of `e71617e` (`D:\Source\f141-deploy`) and verified there.

```powershell
# From the verified worktree (it contains only the change set on top of e71617e).
cd D:\Source\f141-deploy
git switch -c release/feature141-chat-mvp
Get-Content specs\141-schema-aware-semantic-query-engine\evaluation\deployment-change-set.txt |
  Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { git add -- $_ }
git add -- specs/141-schema-aware-semantic-query-engine/evaluation/deployment-change-set.txt `
  specs/141-schema-aware-semantic-query-engine/evaluation/production-deployment-readiness.md
git status --short   # review: only the listed paths are staged
git commit -m "feat(141): chat MVP deployment change set"
```

Build the API image from that commit with the normal process (`docker/api.Dockerfile` packages the semantic catalog
paths in the change set). Deploy it with `FEATURE141_ENABLED=false` (the compose default) and confirm the API is healthy.

**Excluded on purpose** (still in the main working tree, untouched):

| Path | Reason |
|---|---|
| `src/frontend/**` (6 files) | Unrelated frontend work |
| `src/backend/FinancialCopilot.Infrastructure/Billing/Persistence/SubscriptionCreditLifecycleService.cs` | Ongoing billing work |
| `tests/FinancialCopilot.ArchitectureTests/CleanArchitectureDependencyTests.cs` | Follow-up to the committed billing change, not Feature 141 |
| `specs/139-tahlilapp-session-exchange.zip` (deleted) | Unrelated |
| `specs/141-…/artifacts/*-2026-10-09.*` (4 tracked files) | Overwritten by test runs; not needed at runtime |
| `specs/141-…/evaluation/artifacts/**` (tracked and untracked), `production-data-pilot-report.md`, `monthly-production-data-compatibility-report.md` | Evaluation evidence; commit separately if wanted |
| `src/backend/FinancialCopilot.API/appsettings.Development.Local.json` | Gitignored local developer setting |

## 2. Production read-only role (DBA, once)

Run as the database owner on the production server. The application never connects as the owner: the executor
refuses any principal that can write the financial tables or whose session is not read-only.

```bash
docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v expected_database="$POSTGRES_DB" \
  < tools/SemanticCatalog/sql/feature141-read-role-setup.sql
docker compose exec postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c '\password feature141_reader'
docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v expected_database="$POSTGRES_DB" \
  < tools/SemanticCatalog/sql/feature141-read-role-verify.sql
```

Use a generated password of letters and digits only (`;` breaks the connection string and `$` is interpolated by
compose). Expected verify output: every required column `select_allowed = t`; every other column of the six tables
`f`; no INSERT/UPDATE/DELETE/TRUNCATE; `public_create_allowed` and `database_create_allowed` `f`. `database_temp_allowed`
and CONNECT come from PostgreSQL's PUBLIC defaults and are acceptable (sessions are read-only, so CREATE is rejected).

Grants (column-level SELECT only): `Companies(Id, ProviderName, ExternalCompanyId, Name, Ticker, CompanySymbol)`,
`FinancialStatements` (18 reviewed columns), `FinancialStatementLineItems(Value, FinancialStatementId,
SourceItemCatalogId)`, `FinancialStatementSourceItems(Id, ProviderName, StatementType, SourceItemId, Unit)`,
`MonthlyReports` (10 columns), `MonthlyReportLineItems(MonthlyReportId, Title, Unit, ProductionQuantity, SalesQuantity,
SalesAmount, SourceRowFingerprint, SourceMultiplicity)`. No user, billing, conversation or memory table is readable.
Rollback: `feature141-read-role-revoke.sql` (same `-v expected_database`).

## 3. Production `.env`

`docker-compose.yml` and `.env.example` document every Feature 141 variable. Set:

```dotenv
FEATURE141_ENABLED=false
FEATURE141_READONLY_CONNECTION_STRING=Host=postgres;Port=5432;Database=<POSTGRES_DB>;Username=feature141_reader;Password=<generated>
```

## 4. Shared public company scope (owner approval required)

The existing mechanism is one discovery scope per tenant plus its reviewed `tenant:company` universe. The MVP scope is
every eligible listed company (`NoavaranEligibleCompanies`, 875 locally) for the default tenant
`11111111-1111-1111-1111-111111111111` (`Authentication:OwnedIdentity:DefaultTenantId`: web, Telegram and TahlilApp
users). There are no per-user lists; private user data is never in scope (the role cannot read it).

The implementation requires an explicit approval reference, which the product owner must supply. Generate the lines on
the production database (read-only query) and append them to `.env`:

```bash
docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -q -A -t \
  -v expected_database="$POSTGRES_DB" -v tenant_id=11111111-1111-1111-1111-111111111111 \
  -v approval_reference='<OWNER-APPROVAL-REFERENCE>' \
  < tools/SemanticCatalog/sql/feature141-public-company-scope-env.sql >> .env
```

It appends one block between `# BEGIN/# END Feature 141 public company scope` markers:
`FEATURE141_PUBLIC_SCOPE_TENANT_ID`, `FEATURE141_PUBLIC_SCOPE_ID` and `FEATURE141_PUBLIC_SCOPE_APPROVAL_REFERENCE`
(mapped by `docker-compose.yml` to `Feature141:CompanyDiscoveryScopes:0`, with `CompanyCoverageComplete=true`), the
matching `Feature141__QueryExecution__ReviewedTenantCompanyDiscoveryScopes__0` line, and one
`Feature141__QueryExecution__ReviewedTenantCompanyBindings__N` line per eligible company (these reach the container
through `env_file`). When the eligible company list changes, delete the old block, generate a new one, then recreate
the API container. Without the block (empty approval reference) the scope is inactive and discovery and ranking
questions are refused at no charge; everything else still works. API-client (partner) tenants have no scope.

## 5. Enable, verify, roll back

1. Set `FEATURE141_ENABLED=true`, then `docker compose up -d api`. Startup fails fast if the read-only connection or
   scope bindings are malformed.
2. With a test account, ask: `روند فروش کگهر`, `روند فروش گندله کگل در ۱۲ ماه اخیر`, `سود خالص وبملت در آخرین دوره`,
   `مقدار تولید گندله کگل در شهریور ۱۴۰۵`, `مبلغ فروش گندله کگهر در تیر ۱۴۰۵`, `رتبه‌بندی مبلغ فروش گندله در مرداد ۱۴۰۵`.
   Expect the trend answers from the existing specialists, the monthly values and the disclosed partial ranking from
   Feature 141, one credit per answer and none for refusals. In the API logs, `Feature141 query completed … Status=Succeeded`
   must appear and `database_read_only_identity_check_failed` must not.
3. Roll back at any time: `FEATURE141_ENABLED=false`, then `docker compose up -d api`. No code rollback is needed.

## Verification performed (local, production data copy, no production access)

- Role scripts on a disposable clone: database guard refuses a wrong or missing name; verify shows only reviewed
  columns readable; the role cannot write or read `Conversations`; all 6 distinct compiled Feature 141 SQL statements
  from today (statement value, monthly production and sales, producer ranking) run as `feature141_reader`.
- Scope generator: 880 well-formed lines; refuses a missing approval reference or one containing `|`.
- Clean change set (worktree of `e71617e` + 60 files): full solution builds (API, Worker, Telegram gateway, tests);
  unit tests 2,565 passed, 21 failed = the 20 failures pre-existing before this work plus one fixture-hash test that
  fails only in a fresh checkout (line endings) and passes in the main tree.
- Smoke (existing production-data pilot harness, scope = all eligible companies, flag off and on), all passed:

| Question | Flag off | Flag on | Credits (on) |
|---|---|---|---|
| روند فروش کگهر | `monthly_activity_trend` (no snapshots in the disposable copy) | same specialist kept the turn | 1 (existing specialist) |
| روند فروش گندله کگل در ۱۲ ماه اخیر | `product_sales_trend` answered | same; Feature 141 not called | 1 |
| سود خالص وبملت در آخرین دوره | clarification | Feature 141: ambiguous (two statement bases), no number | 0 |
| مقدار تولید گندله کگل در شهریور ۱۴۰۵ | disambiguation | ۱٬۰۸۵٬۱۵۷ تن | 1 |
| مبلغ فروش گندله کگهر در تیر ۱۴۰۵ | unsupported | ۹٬۱۳۰٫۲۵ میلیارد تومان | 1 |
| رتبه‌بندی مبلغ فروش گندله در مرداد ۱۴۰۵ | no data | کگل > کگهر > کچاد with the coverage disclosure | 1 |
| مقدار تولید گندله کگل در فروردین ۱۳۹۰ | disambiguation | NoData | 0 |
| تولید تجمعی گندله کگل … تا شهریور ۱۴۰۵ | disambiguation | Unsupported | 0 |

Every turn settled exactly once; with the flag off Feature 141 was never invoked.

## Known limitations (not blocking)

- A single-company statement question without a basis (consolidated or separate) returns "ambiguous" with no number.
- Feature 141's typed refusals are shown with the generic unsupported message.
- Pre-existing and independent of Feature 141: existing specialists charge one credit for NoData and some
  disambiguation turns (the billing owner's decision).

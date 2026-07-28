# User Stories — Comprehensive Analysis Sync
**Feature Name:** `ComprehensiveAnalysisSync`

---

## Epic: دریافت و ذخیره‌سازی تحلیل‌های جامع بازار سرمایه

---

### US-001 — احراز هویت و دریافت توکن

**As a** system (background service),
**I want to** authenticate against the CyclicalWaves API using credentials,
**So that** I can obtain a Bearer token for subsequent API calls.

**Acceptance Criteria:**
- POST to `https://back1.cyclicalwaves.com/api/auth/login` with `user_name` and `password`
- On success, store `access_token` and `refresh_token` securely (e.g. encrypted app settings / secrets store)
- Token expiry (`expires_in = 864000` seconds / 10 days) must be tracked
- If token is expired or a 401 is received, re-authenticate automatically before retrying
- Credentials must never be hardcoded; read from configuration/environment

---

### US-002 — دریافت اولیه (Full Sync) همه تحلیل‌های جامع

**As a** system administrator,
**I want to** trigger a one-time full data fetch of all comprehensive analyses,
**So that** the local database is fully seeded before the daily incremental sync begins.

**Acceptance Criteria:**
- Callable manually (e.g. via CLI command, admin endpoint, or startup flag)
- Iterates through **all pages** (`page=1,2,...` until `meta.last_page`) with `paginate=10`
- Fetches across **all allowed tag categories**:
  - `تحلیل تکنیکال`
  - `قیمت تعادلی`
  - `رصد معاملات عمده`
  - `گزارش فصلی`
  - `گزارش ماهانه`
  - `نمودار P/S`
  - `نمودار P/E`
- Each item is upserted into the database (no duplicates on re-run)
- Progress is logged (page X of Y, total items fetched)
- On partial failure, logs the error and continues remaining pages

---

### US-003 — همگام‌سازی روزانه (Daily Incremental Sync) از طریق Background Job

**As a** system,
**I want to** automatically fetch the latest comprehensive analyses once per day via a background worker,
**So that** the local database always reflects up-to-date market analyses without manual intervention.

**Acceptance Criteria:**
- Implemented as a recurring background job (e.g. Hangfire recurring job, hosted `IHostedService`, or Windows Service)
- Runs once per day at a configurable time (default: 06:00 local time)
- Uses `filter[from_date]` set to yesterday's date to fetch only new/updated content
- Upserts results into the database (idempotent — safe to re-run)
- On 401, triggers re-authentication (US-001) and retries once
- On 422 (invalid category), logs the invalid category and skips it; does not abort the entire job
- On 500, retries up to 3 times with exponential back-off, then logs failure and alerts
- Job execution history (start time, end time, items synced, status) is persisted

---

### US-004 — فیلتر بر اساس دسته‌بندی

**As a** developer / internal consumer,
**I want to** filter fetched analyses by one or more tag categories,
**So that** only relevant content is retrieved and stored per category.

**Acceptance Criteria:**
- `filter[tags][]` supports multiple values in a single request
- Only the 7 allowed category names (Persian strings) are ever sent — validated before dispatch
- Sending an invalid category is caught client-side; a meaningful exception/log is produced
- Numeric IDs are never sent as category values

---

### US-005 — فیلتر بر اساس بازه تاریخی

**As a** system,
**I want to** specify a date range (`filter[from_date]` / `filter[to_date]`) when fetching analyses,
**So that** the daily sync only retrieves content created within the relevant window.

**Acceptance Criteria:**
- Dates are formatted as `YYYY-MM-DD`
- `filter[from_date]` defaults to yesterday for the daily job
- `filter[to_date]` defaults to today for the daily job
- For full sync (US-002), no date filter is applied

---

### US-006 — جستجو در تحلیل‌ها

**As a** developer / internal consumer,
**I want to** pass a search term (`filter[search]`) to the API,
**So that** I can fetch analyses matching a specific keyword (e.g. stock symbol).

**Acceptance Criteria:**
- `filter[search]` is a string parameter, optional
- Can be combined with tag and date filters
- Search term is URL-encoded correctly for Persian characters

---

### US-007 — صفحه‌بندی و دریافت کامل نتایج

**As a** system,
**I want to** handle paginated responses automatically,
**So that** no analysis items are missed when the result set spans multiple pages.

**Acceptance Criteria:**
- On each response, read `meta.last_page` and `meta.current_page`
- Continue fetching `page=2`, `page=3`, ... until `current_page == last_page`
- `paginate` (page size) is configurable, default `10`
- Respects API rate limits — configurable delay between page requests

---

### US-008 — ذخیره‌سازی در پایگاه داده

**As a** system,
**I want to** persist fetched analysis items in the local database,
**So that** the AI assistant can query and serve them to users without calling the external API on every request.

**Acceptance Criteria:**
- Each item maps to a `ComprehensiveAnalysis` entity with fields:
  - `Title` (string)
  - `Summary` (string)
  - `CreatedAt` (UTC datetime — from `created_at`)
  - `PersianCreatedAt` (string — from `pcreate`)
  - `Categories` (collection / JSON)
  - `Tags` (collection / JSON)
  - `SyncedAt` (UTC datetime — when the record was last written)
- Upsert logic: if a record with the same title + created_at already exists, update it; otherwise insert
- Database migrations are provided

---

### US-009 — مانیتورینگ و لاگ‌گذاری

**As a** system administrator,
**I want to** monitor the sync job's health and review logs,
**So that** I can detect failures and take corrective action quickly.

**Acceptance Criteria:**
- Structured logs (Serilog / Microsoft.Extensions.Logging) for each sync run
- Log entries include: job name, run timestamp, pages fetched, items upserted, errors
- Failed runs are clearly marked; success runs show summary counts
- Job dashboard (e.g. Hangfire UI) shows last run status and next scheduled run

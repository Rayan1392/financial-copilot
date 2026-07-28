# Fix Root Cause Issues in Financial Metrics AI Responses

## Problem Statement

The following user query produced an incorrect answer:

"حاشیه سود عملیاتی شغدیر"

Current output contains multiple critical defects:

### Defect 1 — Wrong Price Source

The response displays PreviousTradingDay price instead of the latest available market price.

Expected behavior:

* If real-time/intraday price exists, always display the latest market price.
* PreviousTradingDay data should only be used as fallback.
* Response must explicitly indicate data timestamp and source.

---

### Defect 2 — Wrong Symbol Display

Current output:

PGDR

Expected output:

شغدیر

Rules:

* For Iranian stocks, user-facing responses must display:

  * Persian ticker symbol
  * Company Persian name

Example:

نماد: شغدیر
شرکت: پتروشیمی غدیر

Internal identifiers such as:

* PGDR
* InstrumentId
* TickerId
* Database keys

must NEVER be shown to end users unless explicitly requested.

---

### Defect 3 — Metric Label Not Localized

Current output:

OPERATING_PROFIT_MARGIN

Expected output:

حاشیه سود عملیاتی

Requirements:

Create a centralized Financial Metric Dictionary.

Example:

OPERATING_PROFIT_MARGIN → حاشیه سود عملیاتی

NET_PROFIT_MARGIN → حاشیه سود خالص

RETURN_ON_EQUITY → بازده حقوق صاحبان سهام

RETURN_ON_ASSETS → بازده دارایی‌ها

PE_RATIO → نسبت P/E

Every financial metric returned by tools must pass through this localization layer before rendering.

No raw English metric names should appear in Persian responses.

---

### Defect 4 — AI Incorrectly Claims Data Is Missing

Current behavior:

AI says:

"عدد دقیق در خروجی برنگشت"

while the metric exists in the database.

This is a critical hallucination and retrieval failure.

Required Fix:

Before generating any "data not found" statement:

1. Verify tool execution status.
2. Verify tool returned rows.
3. Verify metric mapping exists.
4. Verify localization layer.
5. Verify response renderer.

AI must NEVER assume data is missing.

Only say data is unavailable when:

* Query executed successfully
* Data source returned no record
* Existence check confirms absence

---

## Required Architecture Changes

### 1. Metric Resolution Layer

Implement:

User Query
→ Intent Detection
→ Metric Resolver
→ Database Metric Key
→ Data Retrieval
→ Localization
→ Response Rendering

Example:

"حاشیه سود عملیاتی شغدیر"

must resolve to:

OPERATING_PROFIT_MARGIN

before querying data.

---

### 2. Symbol Resolution Layer

Implement alias mapping:

شغدیر
پتروشیمی غدیر

→ canonical instrument

The final response must display Persian ticker and Persian company name.

---

### 3. Data Freshness Priority

Priority order:

1. RealTime
2. Intraday
3. Latest Trading Session
4. PreviousTradingDay

Renderer must always choose the freshest available value.

---

### 4. Strict Anti-Hallucination Rule

If tool returns metric data:

AI MUST output the metric value.

AI MUST NOT generate:

* "پیدا نشد"
* "در دسترس نیست"
* "عدد برنگشت"

unless a verified no-data state exists.

---

### 5. Response Validation Layer

Before sending response:

Validate:

* Persian ticker exists
* Persian metric label exists
* Metric value exists
* Freshest price selected
* No raw database field names shown
* No English metric keys shown

If any validation fails:

trigger fallback repair logic instead of responding.

---

## Expected Output Example

نماد: شغدیر

شرکت: پتروشیمی غدیر

آخرین قیمت: ۲۶٬۳۵۰ ریال

حاشیه سود عملیاتی: ۴۱٫۸٪

منبع: صورت‌های مالی TTM

تاریخ داده: ۱۴۰۵/۰۳/۲۰

اطمینان پاسخ: ۹۸٪

Under no circumstance should the response show:

* PGDR
* OPERATING_PROFIT_MARGIN
* Missing
* PreviousTradingDay (when fresher data exists)
* Generic "data not found" messages without verification.

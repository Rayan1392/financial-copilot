# Feature 080 — Tasks

## Feature
Monthly Production & Sales Quality Ranking

## Task 080.1 — Discover Current Data Model and Calculation Baselines

### Goal
Inspect the current codebase and database model to identify the exact source tables/entities for monthly production/sales, product line items, product revenue mix, company metadata, and industry metadata.

### Details
The agent must not assume table names blindly. It must verify the actual EF Core entities, DbSets, repositories, migrations, and query services.

### Checklist
- [ ] Locate monthly production/sales aggregate entity/table.
- [ ] Locate monthly production/sales line-item entity/table.
- [ ] Locate product revenue mix entity/table from Feature 075.
- [ ] Locate company identity source of truth.
- [ ] Locate industry/group fields.
- [ ] Locate existing derived monthly sales metrics if any.
- [ ] Document available fields:
  - ExternalCompanyId
  - CompanySymbol
  - CompanyName
  - ReportYear
  - ReportMonth
  - ProductName
  - ProductionQuantity
  - SalesQuantity
  - SalesRate
  - SalesAmount
  - TotalCompanySalesAmount
  - SourceProviderName
- [ ] Identify missing baselines and decide calculation fallback.

### Acceptance Criteria
- A short implementation note is added to the PR/commit summary explaining exactly which existing entities were reused.
- No duplicate source-of-truth table is introduced for raw monthly data.

---

## Task 080.2 — Add Application Contracts

### Goal
Create application-layer contracts for ranking query and response.

### Suggested Contracts
- `MonthlySalesQualityRankingQuery`
- `MonthlySalesQualityRankingResponse`
- `MonthlySalesQualityRankingItem`
- `MonthlySalesQualityDimensionScores`
- `MonthlySalesQualityDataCoverage`
- `MonthlySalesQualityDriver`
- `MonthlySalesQualityDirection`
- `MonthlySalesQualityScope`
- `MonthlySalesQualityLabel`

### Required Query Fields
- `int? ReportYear`
- `int? ReportMonth`
- `Guid? IndustryId`
- `Guid? IndustryGroupId`
- `IReadOnlyList<string>? Symbols`
- `MonthlySalesQualityScope Scope`
- `MonthlySalesQualityDirection Direction`
- `int Limit`
- `decimal? MinimumSalesAmount`
- `bool IncludeExplanation`
- `bool IncludeDimensionScores`
- `bool OnlyEligibleRows`

### Required Response Fields
- `ReportYear`
- `ReportMonth`
- `Scope`
- `Direction`
- `TotalEligibleCompanies`
- `GeneratedAtUtc`
- `Items`

### Acceptance Criteria
- Contracts are serializable and API-safe.
- Persian labels can be returned without coupling numeric logic to UI text.
- Query defaults are deterministic:
  - if period missing, use latest available report period.
  - default direction = Top.
  - default limit = 10.
  - max limit = 50.

---

## Task 080.3 — Implement Deterministic Score Calculator

### Goal
Implement deterministic scoring logic without LLM calls.

### Suggested Service
`IMonthlySalesQualityScoreCalculator`

Implementation:
`MonthlySalesQualityScoreCalculator`

### Inputs
For each company-period:
- Monthly sales amount
- 12M average sales amount
- previous month sales amount
- same month previous year sales amount if available
- product line items
- product revenue mix rows
- 3-month trend data
- industry peer baseline

### Dimension Scores
- `SalesGrowthVs12MScore`
- `QuantityGrowthQualityScore`
- `RateGrowthQualityScore`
- `ProductMixStrengthScore`
- `PersistenceTrendScore`
- `IndustryRelativeStrengthScore`

### Rules
- Score range must always be 0..100.
- Missing dimension must be marked unavailable.
- Available dimensions must be reweighted proportionally.
- Extreme values must be capped/winsorized.
- Invalid values must not throw unless data shape is corrupt.
- Confidence must be calculated separately from quality score.

### Acceptance Criteria
- Unit tests cover:
  - strong sales growth with quantity growth
  - sales growth only due to rate with quantity collapse
  - missing product line items
  - missing 12M average
  - insufficient industry peer count
  - outlier rate spike
  - negative/zero sales values
  - reweighting math
  - final score clamping

---

## Task 080.4 — Add Persistence Snapshot

### Goal
Persist calculated ranking snapshots for fast API and AI responses.

### Suggested Entity
`MonthlySalesQualityRankingSnapshot`

### Required Fields
- `Id`
- `ExternalCompanyId`
- `CompanySymbol`
- `CompanyName`
- `IndustryId`
- `IndustryTitle`
- `ReportYear`
- `ReportMonth`
- `MonthlySalesAmount`
- `Avg12MonthSalesAmount`
- `SalesVsAvg12MPercent`
- `SalesMonthOverMonthPercent`
- `SalesYearOverYearPercent`
- `QualityScore`
- `QualityLabel`
- `ConfidenceScore`
- `RankMarket`
- `RankIndustry`
- `DimensionScoresJson`
- `PositiveDriversJson`
- `NegativeDriversJson`
- `DataCoverageJson`
- `SourceProviderName`
- `CalculatedAtUtc`

### Indexes
- Unique: `(ExternalCompanyId, ReportYear, ReportMonth)`
- Query: `(ReportYear, ReportMonth, RankMarket)`
- Query: `(ReportYear, ReportMonth, IndustryId, RankIndustry)`
- Query: `(CompanySymbol, ReportYear, ReportMonth)`

### Acceptance Criteria
- Migration is created.
- Upsert is idempotent.
- Recalculation for same period does not duplicate rows.
- Snapshot can be queried by market rank and industry rank.
- JSON columns are query-safe and version-tolerant.

---

## Task 080.5 — Implement Ranking Repository

### Goal
Create repository methods for saving and querying ranking snapshots.

### Suggested Interface
`IMonthlySalesQualityRankingRepository`

### Required Methods
- `GetLatestAvailablePeriodAsync`
- `GetRankingAsync`
- `GetCompanyRankingAsync`
- `UpsertSnapshotsAsync`
- `DeletePeriodSnapshotsAsync` if delete-then-insert pattern is preferred
- `GetIndustryPeerCountAsync` if needed

### Acceptance Criteria
- Repository uses cancellation tokens.
- Query supports top and bottom direction.
- Query supports industry filter.
- Query supports symbol filter.
- Query supports minimum sales amount.
- Pagination/limit is handled safely.
- No client-side filtering over large tables if avoidable.

---

## Task 080.6 — Implement Recalculation Use Case

### Goal
Create use case that calculates ranking snapshots for a report period.

### Suggested Use Case
`RecalculateMonthlySalesQualityRankingUseCase`

### Behavior
1. Resolve target report period.
2. Load eligible company monthly data.
3. Load historical baselines.
4. Load product line items.
5. Load product revenue mix rows.
6. Load industry peer data.
7. Calculate dimension scores and confidence.
8. Assign market and industry ranks.
9. Persist snapshots.

### Acceptance Criteria
- Use case can be called after monthly ingestion.
- Use case can be called manually by admin/API/job.
- Use case logs:
  - selected period
  - number of eligible companies
  - number skipped
  - missing data statistics
  - calculation duration
- Use case is deterministic for the same input data.

---

## Task 080.7 — Integrate with Monthly Ingestion Pipeline

### Goal
Trigger ranking recalculation after monthly production/sales ingestion completes.

### Details
After successful ingestion of Noavaran/Nadpco monthly activity for a report period, enqueue or invoke recalculation for that period.

### Rules
- Do not block ingestion for too long if calculation is heavy.
- If background job infrastructure exists, prefer enqueue.
- If not, add a safe synchronous call only if expected data volume is acceptable.
- Failures in ranking recalculation must not corrupt raw monthly ingestion.

### Acceptance Criteria
- After monthly ingestion, ranking snapshot for the affected period is updated.
- Recalculation errors are logged with context.
- Raw monthly data ingestion remains successful even if ranking calculation fails, unless transaction policy explicitly requires all-or-nothing.

---

## Task 080.8 — Add API Endpoint

### Goal
Expose ranking results for UI and AI tools.

### Suggested Endpoint
`GET /api/ai/monthly-sales-quality-rankings`

### Query Parameters
- `reportYear`
- `reportMonth`
- `industryId`
- `industryGroupId`
- `symbols`
- `scope`
- `direction`
- `limit`
- `minimumSalesAmount`
- `includeExplanation`
- `includeDimensionScores`
- `onlyEligibleRows`

### Acceptance Criteria
- Endpoint returns deterministic JSON.
- Endpoint validates max limit.
- Endpoint returns latest period if no period is supplied.
- Endpoint returns empty result with clear metadata if no eligible rows exist.
- Endpoint does not call LLM.

---

## Task 080.9 — Add AI Intent and Routing

### Goal
Route Persian natural-language ranking questions to the new ranking use case.

### Suggested Intent
`MonthlySalesQualityRanking`

### Must Detect
- بهترین گزارش‌های ماهانه
- بهترین گزارش‌های تولید و فروش
- رتبه‌بندی گزارش ماهانه
- گزارش‌های فروش قوی
- گزارش‌های فروش ضعیف
- کدام نمادها گزارش ماهانه خوبی داشتند
- رشد باکیفیت فروش
- رشد فروش فقط از نرخ
- رکورد فروش ماهانه
- بالاتر از میانگین ۱۲ ماهه

### Must Not Misroute
- «آخرین فروش کچاد» should remain direct monthly sales lookup.
- «پرفروش‌ترین محصول کچاد» should route to ProductRevenueMix.
- «P/S کچاد» should route to valuation/direct metric.
- «آخرین قیمت کچاد» should route to price lookup.

### Acceptance Criteria
- Intent detector tests cover all examples above.
- Normalized Persian text handles ZWNJ and Arabic/Persian variants.
- AI orchestration passes raw user query to detector where applicable.
- Ranking response uses deterministic tool result, not hallucinated symbols.

---

## Task 080.10 — Add AI Response Composer

### Goal
Generate a concise Persian explanation based only on returned ranking data.

### Required Response Behavior
- Include a table of ranked symbols.
- Include quality score and label.
- Include main reason/drivers.
- Include confidence.
- Include period.
- Include disclaimer: not buy/sell advice.
- Mention missing data where confidence is low.

### Example Table Columns
- رتبه
- نماد
- شرکت
- صنعت
- امتیاز کیفیت
- برچسب
- دلیل اصلی
- اطمینان

### Table Layout Requirements
- Treat `دلیل اصلی` as the descriptive column with a wider preferred width than compact columns.
- Keep `رتبه`, `نماد`, `امتیاز کیفیت`, `برچسب`, and `اطمینان` compact and preferably nowrap.
- Allow `شرکت` and `صنعت` to use medium width.
- Preserve RTL readability and allow `دلیل اصلی` to wrap naturally, not word-by-word.
- Prefer horizontal scrolling on small screens over forcing all columns into equally narrow widths.

### Acceptance Criteria
- Response does not invent extra metrics.
- Response does not recommend buy/sell.
- Response explains whether ranking is market-wide or industry-filtered.
- For bottom ranking, wording is «ضعیف‌ترین گزارش‌ها از نظر کیفیت تولید و فروش» not «بدترین سهم‌ها».
- The rendered table keeps `دلیل اصلی` visually readable without wrapping after every 1–2 words.

---

## Task 080.11 — Add Tests

### Required Test Groups

#### Unit Tests
- Scoring dimensions
- Reweighting
- Confidence calculation
- Label mapping
- Driver generation

#### Repository Tests
- Upsert idempotency
- Latest period resolution
- Top/bottom ordering
- Industry filtering
- Symbol filtering

#### API Tests
- Default latest period
- Limit validation
- Empty result
- Include/exclude dimension scores

#### AI Routing Tests
- Top ranking query
- Bottom ranking query
- Industry ranking query
- No conflict with ProductRevenueMix
- No conflict with direct monthly sales
- No conflict with valuation/price

### Acceptance Criteria
- Tests are deterministic.
- Edge cases with nulls/zeros are covered.
- At least one test uses Persian query with ZWNJ.

---

## Task 080.12 — Documentation

### Goal
Document feature behavior for future maintainers and product/UI team.

### Required Docs
- Scoring formula summary
- API request/response examples
- AI query examples
- Data sufficiency rules
- Known limitations

### Acceptance Criteria
- Documentation explains that this is production/sales quality ranking, not investment advice.
- Documentation lists dimensions and default weights.
- Documentation explains confidence vs quality score.

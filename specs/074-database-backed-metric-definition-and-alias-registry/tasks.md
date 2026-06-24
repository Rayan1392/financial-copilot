# Tasks - Database-Backed Metric Definition and Alias Registry

## Implementation Tasks

### 1. Baseline audit

- [ ] Extract the complete set of distinct `MetricCode` and `PeriodType` values from `public."DerivedMetrics"`.
- [ ] Review current hard-coded Persian/English metric titles in source code.
- [ ] Review `PhaseOneFinancialSemanticCatalog` and identify all static metric aliases.
- [ ] Review `DynamicMetricAliases`, `CompositeMetricAliasResolver`, and alias candidate behavior.
- [ ] Review direct routing logic in V1 and V2 workflows.
- [ ] Review `LlmSymbolLookupParser` deterministic metric fallbacks.
- [ ] Review prompt-level metric trigger lists.
- [ ] Produce a migration note listing every place where metric vocabulary is duplicated.

### 2. Add database schema

- [ ] Create table `MetricDefinitions`.
- [ ] Create table `MetricAliases`.
- [ ] Create table `MetricPeriodAliases`.
- [ ] Create table `MetricAliasCandidates` if the existing dynamic alias candidate table does not already cover this shape.
- [ ] Add primary keys and timestamps.
- [ ] Add a unique index on `MetricDefinitions.MetricCode`.
- [ ] Add a filtered unique index on active `MetricAliases.NormalizedAliasText + Language` where safe.
- [ ] Add indexes for alias lookup:
  - [ ] `MetricAliases.NormalizedAliasText`
  - [ ] `MetricAliases.MetricCode`
  - [ ] `MetricPeriodAliases.NormalizedAliasText`
  - [ ] `MetricAliasCandidates.NormalizedExpression`
- [ ] Add foreign key from `MetricAliases.MetricCode` to `MetricDefinitions.MetricCode`.
- [ ] Do not add Persian labels or aliases to `DerivedMetrics`.

### 3. Define domain models and EF Core mappings

- [ ] Add domain/entity model for `MetricDefinition`.
- [ ] Add domain/entity model for `MetricAlias`.
- [ ] Add domain/entity model for `MetricPeriodAlias`.
- [ ] Add domain/entity model for `MetricAliasCandidate` or adapt the existing dynamic alias model.
- [ ] Add EF Core configuration for all new tables.
- [ ] Ensure normalized alias fields are required and indexed.
- [ ] Ensure `Status` supports at least `Active`, `Inactive`, `Rejected`, and `PendingReview` where applicable.
- [ ] Ensure migration scripts are deterministic and safe to run repeatedly.

### 4. Seed MetricDefinitions

- [ ] Seed `ASSET_TURNOVER` with Persian title `گردش دارایی‌ها`.
- [ ] Seed `AVERAGE_COLLECTION_PERIOD` with Persian title `دوره وصول مطالبات`.
- [ ] Seed `AVG_12M_MONTHLY_SALES` with Persian title `متوسط فروش ۱۲ ماهه`.
- [ ] Seed `AVG_4Q_REVENUE` with Persian title `متوسط فروش چهار فصل` or the approved product title.
- [ ] Seed `COMPREHENSIVE_LIQUIDITY_INDEX` with Persian title `شاخص جامع نقدینگی`.
- [ ] Seed `CURRENT_ASSETS_TO_TOTAL_ASSETS` with Persian title `نسبت دارایی جاری به کل دارایی`.
- [ ] Seed `CURRENT_RATIO` with Persian title `نسبت جاری`.
- [ ] Seed `DEBT_TO_EQUITY` with Persian title `نسبت بدهی به حقوق صاحبان سهام`.
- [ ] Seed `EBIT` with Persian title `سود قبل از بهره و مالیات`.
- [ ] Seed `EPS_GROWTH_QOQ` with Persian title `رشد سود هر سهم نسبت به فصل قبل`.
- [ ] Seed `EPS_GROWTH_YOY` with Persian title `رشد سود هر سهم نسبت به فصل مشابه سال قبل`.
- [ ] Seed `EQUITY_GROWTH_QOQ` with Persian title `رشد حقوق صاحبان سهام نسبت به فصل قبل`.
- [ ] Seed `EQUITY_GROWTH_YOY` with Persian title `رشد حقوق صاحبان سهام نسبت به فصل مشابه سال قبل`.
- [ ] Seed `GROSS_PROFIT` with Persian title `سود ناخالص`.
- [ ] Seed `GROSS_PROFIT_GROWTH_QOQ` with Persian title `رشد سود ناخالص نسبت به فصل قبل`.
- [ ] Seed `GROSS_PROFIT_GROWTH_YOY` with Persian title `رشد سود ناخالص نسبت به فصل مشابه سال قبل`.
- [ ] Seed `GROSS_PROFIT_MARGIN` with Persian title `حاشیه سود ناخالص`.
- [ ] Seed `MONTHLY_PRODUCTION_QUANTITY` with Persian title `مقدار تولید ماهانه`.
- [ ] Seed `MONTHLY_SALES` with Persian title `فروش ماهانه`.
- [ ] Seed `MONTHLY_SALES_GROWTH_MOM` with Persian title `رشد فروش ماهانه نسبت به ماه قبل`.
- [ ] Seed `MONTHLY_SALES_GROWTH_YOY` with Persian title `رشد فروش ماهانه نسبت به مدت مشابه سال قبل`.
- [ ] Seed `MONTHLY_SALES_QUANTITY` with Persian title `مقدار فروش ماهانه`.
- [ ] Seed `MONTHLY_SALES_RATE` with Persian title `نرخ فروش ماهانه`.
- [ ] Seed `MONTHLY_SALES_YTD` with Persian title `فروش تجمیعی سال جاری`.
- [ ] Seed `MONTHLY_SALES_YTD_PREVIOUS_MONTH` with Persian title `فروش تجمیعی تا ماه قبل`.
- [ ] Seed `NET_PROFIT` with Persian title `سود خالص`.
- [ ] Seed `NET_PROFIT_GROWTH_QOQ` with Persian title `رشد سود خالص نسبت به فصل قبل`.
- [ ] Seed `NET_PROFIT_GROWTH_YOY` with Persian title `رشد سود خالص نسبت به فصل مشابه سال قبل`.
- [ ] Seed `NET_PROFIT_MARGIN` with Persian title `حاشیه سود خالص`.
- [ ] Seed `NET_WORKING_CAPITAL` with Persian title `سرمایه در گردش خالص`.
- [ ] Seed `OPERATING_PROFIT` with Persian title `سود عملیاتی`.
- [ ] Seed `OPERATING_PROFIT_GROWTH_QOQ` with Persian title `رشد سود عملیاتی نسبت به فصل قبل`.
- [ ] Seed `OPERATING_PROFIT_GROWTH_YOY` with Persian title `رشد سود عملیاتی نسبت به فصل مشابه سال قبل`.
- [ ] Seed `OPERATING_PROFIT_MARGIN` with Persian title `حاشیه سود عملیاتی`.
- [ ] Seed `PE_TTM` with Persian title `نسبت قیمت به سود`.
- [ ] Seed `PS_TTM` with Persian title `نسبت قیمت به فروش`.
- [ ] Seed `REVENUE` with Persian title `درآمد عملیاتی / فروش`.
- [ ] Seed `REVENUE_GROWTH_QOQ` with Persian title `رشد درآمد نسبت به فصل قبل`.
- [ ] Seed `REVENUE_GROWTH_YOY` with Persian title `رشد درآمد نسبت به فصل مشابه سال قبل`.
- [ ] Seed `TANGIBLE_FIXED_ASSETS_TURNOVER` with Persian title `گردش دارایی‌های ثابت مشهود`.
- [ ] Assign category, default unit, default period type, and capability flags for every seeded metric.

### 5. Seed monthly aliases

- [ ] Add aliases for `MONTHLY_SALES`:
  - [ ] `فروش ماهانه`
  - [ ] `آخرین فروش`
  - [ ] `فروش آخرین ماه`
  - [ ] `مبلغ فروش`
  - [ ] `فروش شرکت`
  - [ ] `فروش ماه قبل`
  - [ ] `فروش ماه مشابه سال قبل`
- [ ] Add aliases for `AVG_12M_MONTHLY_SALES`:
  - [ ] `متوسط فروش ۱۲ ماهه`
  - [ ] `میانگین فروش ۱۲ ماهه`
  - [ ] `متوسط فروش یک ساله`
  - [ ] `میانگین فروش یک ساله`
  - [ ] `average monthly sales`
  - [ ] `12m average sales`
- [ ] Add aliases for `MONTHLY_SALES_YTD`:
  - [ ] `فروش تجمیعی`
  - [ ] `فروش از ابتدای سال`
  - [ ] `فروش سال جاری`
  - [ ] `YTD sales`
- [ ] Add aliases for `MONTHLY_SALES_YTD_PREVIOUS_MONTH`:
  - [ ] `فروش تجمیعی تا ماه قبل`
  - [ ] `فروش از ابتدای سال تا ماه قبل`
  - [ ] `YTD ماه قبل`
- [ ] Add aliases for `MONTHLY_SALES_QUANTITY`:
  - [ ] `مقدار فروش`
  - [ ] `حجم فروش`
  - [ ] `تناژ فروش`
  - [ ] `تعداد فروش`
- [ ] Add aliases for `MONTHLY_SALES_RATE`:
  - [ ] `نرخ فروش`
  - [ ] `متوسط نرخ فروش`
  - [ ] `قیمت فروش محصول`
- [ ] Add aliases for `MONTHLY_PRODUCTION_QUANTITY`:
  - [ ] `تولید ماهانه`
  - [ ] `مقدار تولید`
  - [ ] `حجم تولید`
  - [ ] `تناژ تولید`

### 6. Seed monthly growth aliases

- [ ] Add aliases for `MONTHLY_SALES_GROWTH_MOM`:
  - [ ] `رشد فروش ماهانه`
  - [ ] `رشد فروش نسبت به ماه قبل`
  - [ ] `تغییر فروش نسبت به ماه قبل`
  - [ ] `رشد ماه به ماه فروش`
  - [ ] `فروش نسبت به ماه قبل چقدر رشد کرده`
  - [ ] `MoM sales growth`
  - [ ] `sales growth mom`
- [ ] Add aliases for `MONTHLY_SALES_GROWTH_YOY`:
  - [ ] `رشد فروش سالانه`
  - [ ] `رشد فروش نسبت به سال قبل`
  - [ ] `رشد فروش نسبت به پارسال`
  - [ ] `رشد فروش ماهانه نسبت به سال قبل`
  - [ ] `رشد فروش ماه مشابه`
  - [ ] `رشد فروش ماه مشابه سال قبل`
  - [ ] `درصد رشد فروش نسبت به مدت مشابه`
  - [ ] `تغییر فروش نسبت به مدت مشابه`
  - [ ] `YoY sales growth`
  - [ ] `sales growth yoy`
- [ ] Ensure `رشد فروش نسبت به سال قبل` resolves to `MONTHLY_SALES_GROWTH_YOY`, not `MONTHLY_SALES`.
- [ ] Ensure `فروش ماه قبل` resolves to `MONTHLY_SALES` with period selector `M1`, not `MONTHLY_SALES_GROWTH_MOM`.

### 7. Seed profitability and growth aliases

- [ ] Add aliases for `NET_PROFIT_MARGIN`:
  - [ ] `حاشیه سود خالص`
  - [ ] `مارجین خالص`
  - [ ] `نسبت سود خالص به فروش`
  - [ ] `net margin`
- [ ] Add aliases for `GROSS_PROFIT_MARGIN`:
  - [ ] `حاشیه سود ناخالص`
  - [ ] `مارجین ناخالص`
  - [ ] `gross margin`
- [ ] Add aliases for `OPERATING_PROFIT_MARGIN`:
  - [ ] `حاشیه سود عملیاتی`
  - [ ] `مارجین عملیاتی`
  - [ ] `operating margin`
- [ ] Add aliases for `REVENUE`, `GROSS_PROFIT`, `OPERATING_PROFIT`, `NET_PROFIT`, and `EBIT`.
- [ ] Add aliases for all QoQ growth metrics using phrases such as `نسبت به فصل قبل`, `فصلی`, and `QoQ`.
- [ ] Add aliases for all YoY growth metrics using phrases such as `نسبت به فصل مشابه`, `نسبت به مدت مشابه`, `سالانه`, and `YoY`.

### 8. Seed valuation and financial ratio aliases

- [ ] Add aliases for `PE_TTM`:
  - [ ] `PE`
  - [ ] `P/E`
  - [ ] `پی به ای`
  - [ ] `پی ای`
  - [ ] `نسبت قیمت به سود`
  - [ ] `قیمت به سود`
- [ ] Add aliases for `PS_TTM`:
  - [ ] `PS`
  - [ ] `P/S`
  - [ ] `پی به اس`
  - [ ] `پی اس`
  - [ ] `نسبت قیمت به فروش`
  - [ ] `قیمت به فروش`
- [ ] Add aliases for `CURRENT_RATIO`, `DEBT_TO_EQUITY`, `ASSET_TURNOVER`, `TANGIBLE_FIXED_ASSETS_TURNOVER`, `AVERAGE_COLLECTION_PERIOD`, `NET_WORKING_CAPITAL`, `CURRENT_ASSETS_TO_TOTAL_ASSETS`, and `COMPREHENSIVE_LIQUIDITY_INDEX`.
- [ ] Ensure valuation phrases containing `قیمت` are protected from generic latest-price routing.

### 9. Seed period aliases

- [ ] Add `MetricPeriodAliases` for monthly periods:
  - [ ] `آخرین ماه` -> `Monthly`, `M0`
  - [ ] `ماه جاری` -> `Monthly`, `M0`
  - [ ] `ماه قبل` -> `Monthly`, `M1`
  - [ ] `ماه گذشته` -> `Monthly`, `M1`
  - [ ] `ماه مشابه سال قبل` -> `Monthly`, `M12`
  - [ ] `مدت مشابه سال قبل` -> `Monthly`, `M12`
  - [ ] `پارسال` -> `Monthly`, `M12` when used with monthly metrics
- [ ] Add `MetricPeriodAliases` for quarterly periods:
  - [ ] `آخرین فصل` -> `ThreeMonths`, `Q0`
  - [ ] `فصل جاری` -> `ThreeMonths`, `Q0`
  - [ ] `فصل قبل` -> `ThreeMonths`, `Q1`
  - [ ] `فصل گذشته` -> `ThreeMonths`, `Q1`
  - [ ] `فصل مشابه سال قبل` -> `ThreeMonths`, `Q4`
  - [ ] `دوره مشابه سال قبل` -> `ThreeMonths`, `Q4` when used with quarterly metrics
- [ ] Add period aliases for statement duration periods:
  - [ ] `سه ماهه` -> `ThreeMonths`, `Latest`
  - [ ] `شش ماهه` -> `SixMonths`, `Latest`
  - [ ] `نه ماهه` -> `NineMonths`, `Latest`
  - [ ] `دوازده ماهه` -> `TwelveMonths`, `Latest`
- [ ] Ensure period aliases do not override metric aliases by themselves.

### 10. Build registry service

- [ ] Add an application service such as `IMetricDefinitionRegistry`.
- [ ] Add an application service such as `IMetricAliasRegistry` or extend the existing resolver interface.
- [ ] The resolver must return:
  - [ ] `MetricCode`
  - [ ] canonical Persian title
  - [ ] canonical English title
  - [ ] category
  - [ ] capabilities
  - [ ] matched alias
  - [ ] match type
  - [ ] match source
  - [ ] confidence
  - [ ] optional `PeriodType`
  - [ ] optional `PeriodSelector`
  - [ ] ambiguity reason when applicable
- [ ] Add memory cache or distributed cache support with explicit invalidation.
- [ ] Add cache invalidation after alias approval, rejection, creation, update, or disable.
- [ ] Keep static semantic catalog fallback during rollout.

### 11. Implement normalization

- [ ] Normalize Arabic and Persian Yeh/Kaf variants.
- [ ] Normalize half-space and zero-width non-joiner variants.
- [ ] Normalize Arabic and Persian digits.
- [ ] Normalize `ي`/`ی`, `ك`/`ک`, and common punctuation.
- [ ] Normalize slash variants for `P/E` and `P/S`.
- [ ] Normalize extra whitespace.
- [ ] Store `NormalizedAliasText` during seed and write operations.
- [ ] Add unit tests for normalization.

### 12. Implement matching and ambiguity rules

- [ ] Implement exact alias match.
- [ ] Implement longest alias match.
- [ ] Implement alias plus period phrase match.
- [ ] Implement approved dynamic/database alias match.
- [ ] Implement fuzzy match only above a configured threshold.
- [ ] Prevent broad generic terms from resolving without context.
- [ ] Add explicit ambiguity result for unsafe phrases.
- [ ] Ensure `نسبت قیمت به سود` resolves to `PE_TTM`, not latest price.
- [ ] Ensure `قیمت به فروش` resolves to `PS_TTM`, not latest price.
- [ ] Ensure `رشد فروش نسبت به سال قبل` resolves to `MONTHLY_SALES_GROWTH_YOY`.
- [ ] Ensure `فروش ماه قبل` resolves to `MONTHLY_SALES + M1`.

### 13. Integrate with symbol metric lookup

- [ ] Update symbol metric lookup parsing to call the database-backed alias resolver.
- [ ] Pass resolved `MetricCode`, `PeriodType`, and `PeriodSelector` to the lookup service.
- [ ] Query `DerivedMetrics` by `ExternalCompanyId`, `MetricCode`, and `PeriodType`.
- [ ] Apply `PeriodSelector` after retrieving ordered candidate rows.
- [ ] Preserve missing/null handling.
- [ ] Preserve unit conversion and formatting rules.
- [ ] Render canonical Persian titles from `MetricDefinitions`.

### 14. Integrate with scanner parsing

- [ ] Update scanner metric resolution to use the database-backed alias resolver.
- [ ] Ensure scanner-eligible capability is respected.
- [ ] Ensure direct lookup-only metrics do not accidentally become scanner filters unless allowed.
- [ ] Preserve scanner result shaping rules from existing scanner specs.
- [ ] Add tests for PE/PS, growth, margin, and monthly activity scanner phrases.

### 15. Integrate with workflow routing

- [ ] Update V2 direct metric preflight to use registry-derived capabilities.
- [ ] Keep existing hard-coded routing as fallback during migration only.
- [ ] Update parser fallback logic so it does not own independent vocabulary.
- [ ] Update prompt/tool-routing wording to reference registry categories where practical.
- [ ] Preserve V1/V2 backward compatibility.
- [ ] Add routing diagnostics for matched metric and selected tool.

### 16. Alias candidate capture and review

- [ ] When a metric phrase cannot be resolved with sufficient confidence, create or update a `MetricAliasCandidate`.
- [ ] Increment frequency count for repeated unresolved expressions.
- [ ] Store sample query evidence in `EvidenceExamplesJson`.
- [ ] Include suggested metric code only when resolver/LLM confidence is high enough.
- [ ] Do not auto-approve LLM suggestions.
- [ ] Add service method to approve a candidate into `MetricAliases`.
- [ ] Add service method to reject or ignore a candidate.
- [ ] Ensure approved aliases refresh resolver cache.

### 17. Admin/API operations

- [ ] Add admin query to list metric definitions.
- [ ] Add admin query to list aliases by metric code.
- [ ] Add admin query to list unresolved alias candidates.
- [ ] Add admin command to create an alias.
- [ ] Add admin command to update an alias.
- [ ] Add admin command to disable an alias.
- [ ] Add admin command to approve/reject alias candidates.
- [ ] Enforce authorization using existing admin policies.
- [ ] Validate duplicate aliases and unsafe conflicts before saving.

### 18. Response rendering improvements

- [ ] Use `MetricDefinitions.DefaultPersianTitle` in direct metric answers.
- [ ] Show period label derived from `PeriodType`, `PeriodStart`, and `PeriodEnd`.
- [ ] Show units consistently using registry unit metadata and `DerivedMetrics.Unit`.
- [ ] Show source/provider evidence from `SourceEvidenceJson` when supported.
- [ ] Add warning when data is missing, stale, zero due to missing source, or ambiguous.
- [ ] Preserve monthly production/sales quote-column omission behavior.

### 19. Tests - seed coverage

- [ ] Test every distinct current `DerivedMetrics.MetricCode` has one active `MetricDefinition`.
- [ ] Test every active `MetricDefinition` has at least one Persian display title.
- [ ] Test lookup-eligible metrics have at least one active alias.
- [ ] Test monthly metrics have `Monthly` default period behavior.
- [ ] Test valuation metrics have valuation capability.
- [ ] Test growth metrics have growth capability.
- [ ] Test margin metrics have margin capability.

### 20. Tests - alias resolution

- [ ] `رشد فروش سالانه کچاد؟` -> `MONTHLY_SALES_GROWTH_YOY`.
- [ ] `رشد فروش نسبت به پارسال کگل؟` -> `MONTHLY_SALES_GROWTH_YOY`.
- [ ] `فروش کچاد نسبت به مدت مشابه چقدر رشد کرده؟` -> `MONTHLY_SALES_GROWTH_YOY`.
- [ ] `رشد فروش نسبت به ماه قبل کچاد؟` -> `MONTHLY_SALES_GROWTH_MOM`.
- [ ] `فروش ماه قبل کچاد؟` -> `MONTHLY_SALES` + `M1`.
- [ ] `فروش ماه مشابه سال قبل کچاد؟` -> `MONTHLY_SALES` + `M12`.
- [ ] `متوسط فروش ۱۲ ماهه کگل؟` -> `AVG_12M_MONTHLY_SALES` + latest monthly period.
- [ ] `متوسط فروش ۱۲ ماهه سال قبل کگل؟` -> `AVG_12M_MONTHLY_SALES` + `M12`.
- [ ] `حاشیه سود خالص فصل قبل کچاد؟` -> `NET_PROFIT_MARGIN` + `Q1`.
- [ ] `حاشیه سود ناخالص فصل مشابه سال قبل کچاد؟` -> `GROSS_PROFIT_MARGIN` + `Q4`.
- [ ] `نسبت قیمت به سود کگل؟` -> `PE_TTM`, not latest price.
- [ ] `قیمت به فروش کچاد؟` -> `PS_TTM`, not latest price.

### 21. Tests - DerivedMetrics lookup

- [ ] Lookup by resolved alias queries `DerivedMetrics` using `ExternalCompanyId`, `MetricCode`, and `PeriodType`.
- [ ] Latest monthly selector returns the latest `PeriodEnd` row.
- [ ] `M1` selector returns the previous monthly row.
- [ ] `M12` selector returns the same-month-prior-year row when available.
- [ ] `Q1` selector returns the previous quarterly row.
- [ ] `Q4` selector returns the same-quarter-prior-year row when available.
- [ ] Missing selector data returns a clear missing-data response instead of silent fallback.

### 22. Tests - ambiguity and safety

- [ ] `تغییر فروش کچاد؟` without comparison context returns ambiguous or follows explicitly configured product default.
- [ ] `رشد فروش کچاد؟` without comparison context returns ambiguous or follows explicitly configured product default.
- [ ] `قیمت به سود کگل؟` does not match generic price.
- [ ] `قیمت به فروش کگل؟` does not match generic price.
- [ ] Company names containing metric-like tokens are preserved.
- [ ] Follow-up messages do not treat previous answer text as the new symbol phrase.

### 23. Tests - dynamic candidates

- [ ] Unresolved metric phrase creates a candidate.
- [ ] Repeated unresolved phrase increments candidate frequency.
- [ ] Candidate approval creates active alias.
- [ ] Candidate rejection prevents repeated noisy suggestions.
- [ ] Approved alias participates in resolution after cache invalidation.
- [ ] LLM suggestion is not auto-approved.

### 24. Observability

- [ ] Add structured logs for alias resolution.
- [ ] Log normalized query, matched alias, metric code, period selector, match source, confidence, and ambiguity reason.
- [ ] Log final `DerivedMetrics` query shape in test/debug mode.
- [ ] Do not log sensitive user data beyond the query text already processed by the system.
- [ ] Ensure public API contracts remain unchanged unless existing metadata already supports diagnostics.

### 25. Documentation

- [ ] Document `MetricDefinitions` schema and purpose.
- [ ] Document `MetricAliases` schema and alias precedence rules.
- [ ] Document `MetricPeriodAliases` and period selector behavior.
- [ ] Document how to add a new metric definition.
- [ ] Document how to add a new alias safely.
- [ ] Document how unresolved phrases become alias candidates.
- [ ] Document ambiguity rules with examples.
- [ ] Document migration strategy from hard-coded aliases to database-backed registry.

## Acceptance Checklist

- [ ] All current `DerivedMetrics.MetricCode` values are seeded in `MetricDefinitions`.
- [ ] Each lookup-eligible metric has Persian display metadata and at least one active alias.
- [ ] Multiple Persian aliases can resolve to the same metric code.
- [ ] Period aliases are resolved separately from metric identity.
- [ ] The resolver returns `MetricCode`, `PeriodType`, and optional `PeriodSelector`.
- [ ] `MONTHLY_SALES_GROWTH_YOY` resolves from several natural Persian expressions.
- [ ] `MONTHLY_SALES_GROWTH_MOM` is not confused with `MONTHLY_SALES + M1`.
- [ ] PE/PS phrases containing `قیمت` are protected from generic price routing.
- [ ] Dynamic alias candidates are captured and reviewable.
- [ ] Source code no longer needs deployment for simple approved alias additions.
- [ ] Existing specs `045`, `046`, `072`, and `073` remain compatible.

## Suggested Follow-Up Stories

- [ ] Build an admin UI for metric definitions, aliases, and candidate review.
- [ ] Add telemetry dashboards for unresolved metric phrases.
- [ ] Add import/export support for alias seed data.
- [ ] Add versioned alias packs for Persian market slang.
- [ ] Add semantic-vector fallback for long-form metric descriptions after deterministic alias resolution.

# Tasks — AI Fund Intelligence Query and Explainability

## 1. Semantic Catalog and Intent Detection

- [ ] Add the proposed intents to the governed intent/alias registry, not scattered hardcoded branches.
- [ ] Add Persian aliases for صندوق، پرتفوی/پورتفوی، ترکیب دارایی، خرید/فروش صندوقی، ورود/خروج، افزایش/کاهش وزن، چرخش صنعت، تعدیل قیمت، درآمد تحقق‌یافته/تحقق‌نیافته، and related forms.
- [ ] Distinguish an investment fund's disclosed portfolio from the user's followed symbols or personal portfolio.
- [ ] Extract optional fund name, symbol/company, period/month, industry, top N, minimum score, minimum fund count, horizon, and base/quality-weighted mode.
- [ ] Clarify ambiguous fund/company/period rather than guessing.
- [ ] Add regression examples with ZWNJ, spelling variation, colloquial Persian, and misspellings.

## 2. Resolver Services

- [ ] Add canonical `InvestmentFundResolverService` using fund identifiers, governed aliases, and ambiguity scores.
- [ ] Reuse existing `CompanyResolverService`/trading-instrument resolution for symbols.
- [ ] Add period resolver supporting latest accepted report, exact Jalali period end, month key, and relative phrases.
- [ ] Reject superseded/failed report revisions by default.
- [ ] Return structured clarification candidates through existing AI patterns.

## 3. Narrow AI Tool Adapters

- [ ] Create narrow Application-service tool adapters for fund overview, holdings/activity, allocation/sector, risk/income/valuation, symbol fund activity, cross-fund consensus, and conviction quality.
- [ ] Tools must return structured deterministic contracts only.
- [ ] Do not expose repositories, arbitrary SQL, or raw workbook access to the agent.
- [ ] Enforce bounded top N, date range, page size, and evidence payload size.
- [ ] Preserve base and quality-weighted score components separately.

## 4. Workflow Routing

- [ ] Extend Microsoft Agent Framework V2/native workflow messages for fund-intelligence intents.
- [ ] Preserve V1/V2 switching and existing query behavior.
- [ ] Route questions to exactly one primary fund-intelligence tool or an explicit composite workflow.
- [ ] Prevent generic scanner routing from treating fund names as company symbols.
- [ ] Persist conversation context with canonical fund/company/report ids when follow-up questions are asked.

## 5. Persian Rendering and Explainability

- [ ] Add deterministic table/card renderers for holdings, purchases/sales, entries/exits, sector changes, allocation, income composition, adjustments, consensus, and historical quality.
- [ ] Show units in Rials/percent/quantity and period context clearly.
- [ ] State `گزارش ماهانه با تأخیر انتشار` for institutional activity.
- [ ] Include report period, report source/revision, import freshness, calculation version, coverage, confidence, and reconciliation warnings.
- [ ] For historical quality, include methodology, horizon, benchmark, sample count, and unavailable reasons.
- [ ] Do not use imperative buy/sell wording.

## 6. Billing, Entitlements, and Feedback

- [ ] Reuse existing Billing reservation/finalization and immutable usage ledger.
- [ ] Add entitlement codes for basic fund overview, advanced consensus, and historical conviction analytics if product plans require separation.
- [ ] Do not charge on unauthorized, validation, unresolved clarification, or internal failure according to existing policy.
- [ ] Send missing fund/report/section/coverage/intent gaps into Feature 028 with structured classification.
- [ ] Include tool/provider/token/cost telemetry without logging unrestricted holdings evidence.

## 7. Suggested Actions and Integration

- [ ] Add actions such as `OpenFund`, `OpenSymbol`, `OpenSourceReport`, `FollowSymbol`, `CreateFundActivityTracker`, and `AskAboutEvidence`.
- [ ] Link symbol results to Feature 085 followed-symbol actions without calling them a personal portfolio.
- [ ] Prepare alert/tracker handoff contracts for Feature 110/091.
- [ ] Ensure source links require authorization where raw report access is restricted.

## 8. Evaluation and Tests

- [ ] Add Feature 017 evaluation dataset covering all intents, ambiguity, periods, errors, delayed-data language, and recommendation guardrails.
- [ ] Unit-test extraction and rendering for Persian aliases, top N, periods, scores, confidence, and citations.
- [ ] Integration-test end-to-end AI facade, Billing, conversation persistence, actor isolation, missing feedback, and deterministic tool outputs.
- [ ] Regression-test existing company metrics/scanners so صندوق queries do not contaminate symbol or metric parsing.
- [ ] Given a question about funds buying a symbol, return exact contributing funds/report periods and state disclosure delay.
- [ ] Given insufficient historical samples, return methodology-aware unavailable status rather than ranking funds.

## Completion Gate

- [ ] Keep tasks unchecked until every intent is source-bound, period-aware, billed correctly, Persian-rendered, evaluated, and unable to alter deterministic evidence or offer advice.

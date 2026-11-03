# Feature 144 — EPS and comparative share-basis investigation

**Date:** 2026-10-20 (Asia/Tehran)  
**Phase:** Evidence, contract, implementation, and verification.  
**Status:** Product Owner decision received; comparable-EPS contract implemented and verified as listed below.

## Evidence boundary

The local Development `financial_copilot` PostgreSQL database was queried with `default_transaction_read_only=on`; the connection reported read-only mode. A single authenticated, read-only Noavaran `IncomeStatement/Values` request for company `12622`, FY1405 six months, was made on 2026-10-20. Its JSON response was inspected in memory and **not** ingested or saved. The supplied Bourse View screenshot is the comparison reference; no Bourse View authenticated API or report identifiers were available. Production FinancialCopilot records were not accessed. No financial rows were modified.

## Company, scope, fiscal calendar, and selected reports

Development company identity: `NoavaranCurrentApi`, `ExternalCompanyId=12622`, symbol `قاسم`, name `قاسم ایران`, `Companies.Id=93eab307-6eed-4603-aa42-71b33972247e`. The selected reports below are **separate** (`IsComposing=false` and source `isComposing=false`). The persisted evidence gives FY1404 end `1404/12/29` and FY1405 end `1405/12/29`. FY1404 6M and 12M audited reports outrank lower-priority separate variants; the separate 9M and FY1405 3M candidates are unaudited. Consolidated and legacy scope-mismatched variants were inspected but excluded. No selected report is marked restated.

| Fiscal YTD | ExternalStatementId | Persisted `FinancialStatements.Id` | Audit | Announced (Jalali) | Item 143 net profit, million IRR | Item 144 attributable profit, million IRR | Item 153 shares, thousand | Item 160 source EPS, IRR/share | Item 317 Codal EPS, IRR/share |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| FY1404 3M, end 1404/03/31 | `525337` | `f9cf4c93-4ff2-4c60-8b0f-bf568f92eee4` | No | 1404/04/28 | 1,019,350 | 1,019,350 | 8,400,000 | 121 | 121 |
| FY1404 6M, end 1404/06/31 | `530838` | `c27b8548-604e-4719-901d-bda1a8ef4966` | Yes | 1404/08/25 | 3,116,624 | 3,116,624 | 8,400,000 | 371 | 371 |
| FY1404 9M, end 1404/09/30 | `532605` | `cd0cd605-3934-4a48-bf13-36efb9f5c354` | No | 1404/10/23 | 6,668,740 | 6,668,740 | 8,400,000 | 793 | 794 |
| FY1404 12M, end 1404/12/29 | `538477` | `4ab26115-2758-433d-b4b8-f841e21e8af4` | Yes | 1405/02/23 | 11,405,105 | 11,405,105 | 41,345,696 | 275 | 276 |
| FY1405 3M, end 1405/03/31 | `547770` | `d59169c0-3f76-4c12-b2cc-53de5c3f19d3` | No | 1405/04/30 | 6,048,613 | 6,048,613 | 41,345,696 | 146 | 146 |
| FY1405 6M, end 1405/06/31 | `553578` | **Not persisted locally; live Noavaran response** | No | 1405/07/27 | 15,094,332 | 15,094,332 | 47,616,343 | 317 | 317 |

The local normalized statements stop at FY1405 3M. The live FY1405 6M report is source evidence, **not** proof that the current application has selected or ingested it. All inspected selected separate reports have `AssumedScale=MillionRials`, `SourceAmountUnit=N/A` in persisted metadata, provider-specific logical units of million IRR for profit, thousand shares for item 153, and IRR/share for items 160/317. The source catalogue IDs are `143=bd36909d-abf8-44c2-b7db-5e53dd678b53`, `144=52a19de6-4a0a-4be9-8dbc-3b487a05c08d`, `153=e98e6c00-7204-44b1-a279-bbbb68781d3c`, `160=2146da26-b28e-4d04-b50c-f49fc9b70e61`, and `317=1d244aae-7e5d-44e6-9ec4-c1659828856d`.

The selected FY1404 9M/12M source line IDs are: item 143 `e60bd3fc-38f2-471d-ac4b-978489180929` / `14bdd29d-b651-4ee5-b54e-59cde739e901`; item 144 `b81507ee-13b2-47f7-9809-766bc789dbdb` / `6a5e7d03-926d-4642-942b-f7e15a11253b`; item 153 `bf8d0d42-ed11-4d52-b7ed-59ab64357e8a` / `058adcb4-a731-442a-9a1c-f0adcc341b47`; item 160 `efe1b1c6-5eee-42f4-9c4f-94ffc3531cef` / `74360420-b80f-4a1e-9bb7-bc7a1839e8c9`. The live FY1405 6M item IDs have no local line UUIDs.

## Verified failure mechanism and arithmetic

The normalizer classifies item 160 as `CumulativeEps` but runs it through the same adjacent YTD subtraction as additive profit. Thus:

| Standalone quarter | Net-profit calculation, million IRR | Current EPS calculation | Source share observations | Comparison arithmetic from supplied screenshot |
| --- | ---: | ---: | --- | ---: |
| FY1404 Q2 | 3,116,624 − 1,019,350 = **2,097,274** | 371 − 121 = **250** | 8,400,000 in both reports | 2,097,274 × 1,000 / 47,616,343 = **44.0453** IRR/share on FY1405 Q2 basis |
| FY1404 Q4 | 11,405,105 − 6,668,740 = **4,736,365** | 275 − 793 = **−518** | **8,400,000 → 41,345,696** | 4,736,365 × 1,000 / 41,345,696 = **114.5552** IRR/share |
| FY1405 Q2 | 15,094,332 − 6,048,613 = **9,045,719** | 317 − 146 = **171** | **41,345,696 → 47,616,343** | 9,045,719 × 1,000 / 47,616,343 = **189.9709** IRR/share |

The arithmetic matches the generated table's `−518`, `250`, and `171` and the Bourse View screenshot's approximate `115`, `44`, and `190`. The exact Q4 net-profit amount in the selected source is **4,736,365**, correcting the earlier bug report's transcription `4,734,365`.

Across the six separate YTD reports in the evidence table, `item 144 × 1,000 / item 153` lies within one rial of item 160. Item 160 follows integer truncation for the inspected values (e.g. FY1404 9M `793.8976 → 793`, FY1404 12M `275.8475 → 275`); item 317 follows nearest-rial rounding in those two examples (`794`, `276`). This is **strong case-specific arithmetic correlation**, not a source contract proving that item 153 is weighted-average shares or that the same rule holds for every issuer. Item 144 is titled `سود خالص قابل انتساب به صاحبان سهام شرکت اصلی`; it equals item 143 for each inspected separate Qasem report. No explicitly named weighted-average share-count item was identified in these reports. A corporate-action record explaining each share jump was not established.

## Contract established by the Product Owner

1. **Separate reports only.** Do not use consolidated values or mix scopes. Preserve the existing deterministic audit/restatement priority within the separate scope.
2. **Monetary standalone profit stays unchanged.** Continue adjacent YTD subtraction for additive monetary flows; never multiply net profit by a share/capital ratio.
3. **Immutable source evidence.** Preserve raw cumulative EPS, attributable profit, net profit, share count, report/line IDs, and selection metadata independently of derived display values.
4. **Rolling same-quarter basis.** When fiscal year `Y+1` has the verified same quarter `Q`, the displayed share basis for `Y Q` is the approved `Y+1 Q` comparison basis; re-evaluate after a new or restated matching report. Do not globally use the latest share count. FY1404 Q2 must pair with FY1405 Q2; FY1404 Q4 must pair with FY1405 Q4 when the latter arrives.
5. **Missing counterpart.** When the next-year same-quarter report does not exist, use a verified own-period reported share count as the explicit fallback. Qasem FY1404 Q4 before FY1405 Q4 uses its own 41,345,696-thousand-share basis. If the matching next-year report exists but its share basis is missing, zero, duplicate, mismatched, or unsupported, return typed unavailable rather than silently falling back.
6. **Presentation provenance.** A derived/comparable EPS must identify numerator, denominator, reference fiscal quarter/report (or explicit own-period fallback), unit conversion, rounding, state, and reason. Source EPS and source shares remain available in typed provenance. Web table and PNG consume the same backend values.
7. **Product Owner approval (2026-10-20).** Use derived comparable EPS from standalone attributable profit and the evidence-checked share basis, while retaining the original database row title. Distinguish derived EPS from officially reported EPS in provenance and the methodology note. Reported period-end shares must be identified as not weighted-average shares. If numerator or basis evidence fails validation, show typed unavailable.

## Approved methodology and implementation

The source evidence supports a comparison metric for Qasem but does not establish weighted-average shares or an authoritative accounting-EPS denominator for every issuer. The approved value is therefore **comparable EPS**, explicitly distinguished from accounting EPS. It uses standalone item 144 × 1,000 / item 153 reported shares. Numerator reports must reconcile source YTD item 160 within one rial and item 317 within one rial when present. A following-year report is used only to supply its verified positive item 153 denominator; its EPS or profit lines are not required for that denominator. Output EPS rounds to the nearest whole rial, midpoint away from zero. The denominator is period-end reported shares, never described as weighted-average shares. This validation is an evidence/reconciliation gate; it is not a claim that the comparable value is statutory accounting EPS.

If selected statements fail the evidence gate, comparable EPS is unavailable. Monetary standalone profit remains available from item 143 and is not scaled. Same-quarter following-year share rebasing is dynamic from persisted report selection; a report beyond the displayed ending date may be selected as reference evidence.

Implementation records raw cumulative 143/144/153/160/317 values and report/line IDs, standalone attributable profit, selected basis, reference report, unit/rounding rule, and reconciliation outcome in typed cells. The backend renderer presents per-quarter basis notes. The frontend exposes those notes in a collapsible section and includes them in the same-model PNG export.

The Product Owner approved comparable EPS and these rules on 2026-10-20.

## Verification after implementation

- Feature 144 unit suite: 39 passed, 0 failed, 0 skipped (includes Qasem 44/115/190, following-year Q4 arrival to 79, ending-period reference lookup, unavailable invalid reference, and unchanged standalone profits).
- V2 endpoint persistence/replay: 1 passed; JSON round-trip preserves comparable-EPS provenance and only one billing reservation is created.
- Feature 144 PostgreSQL integration suite: 4 passed, 0 failed, 0 skipped. Includes repository scope/source isolation, comparative-reference retrieval beyond the visible ending period, non-Esfand calendar handling, and read-only Development smoke checks.
- Frontend table and image unit tests: 9 passed, 0 failed.
- Browser PNG export: passed; generated the shared renderer export.
- Frontend production build: passed. Vite emitted its large-chunk advisory.
- Local Development Qasem records were queried read-only; the live FY1405 6M Noavaran response is used as an evidence fixture because it is not persisted locally. Chakhzar Development smoke output confirms unavailable EPS/share cells when required evidence is missing. Production records were not queried or modified.

## Calculation policy clarification (2026-10-21)

The earlier implementation and verification above required source EPS reconciliation and could prefer item 144. The current Feature 144 contract supersedes that gate: displayed comparable EPS is calculated from the row-143 net-profit numerator, using item 313 only when item 143 is absent/null, and the approved item-153 share basis. Raw items 160 and 317 are optional provenance only; they do not supply or gate the calculated value. See the current rules in [spec.md](spec.md).

## Product Owner decision: latest selected quarter share basis (2026-10-24)

The prior rolling same-quarter/own-period fallback policy recorded above is superseded. For each normalized Feature 144 table, take item 153 from the newest quarter in that table's selected window and use it as the displayed share count for every quarter. Derive each quarter's comparable EPS from that quarter's standalone row-143 net profit (item 313 remains the approved fallback) divided by this same share basis. The ending-period filter and requested quarter count determine the window; later reports outside the window cannot change an as-of table. If the newest selected quarter has no verified positive item 153 observation, the share row and comparable EPS are unavailable instead of silently reverting to an older quarter's capital. Preserve each quarter's original item-153 observation in provenance.

This changes only Feature 144 normalization and its presentation contract. Report acquisition, synchronization, persistence, report selection, standalone YTD normalization, comparable-EPS numerator selection, and existing rendering paths remain unchanged. The current implementation and tests are specified in [spec.md](spec.md).

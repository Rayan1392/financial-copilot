# Feature 141 product identity coverage and review

Audit date: 2026-10-09. This report describes the checked-in generated snapshots; it did not connect to a Development or production database. The monthly product-value dictionary records a bounded sample of 5,000 line items and contains 4,934 distinct persisted identity rows, so these counts are sample coverage, not market-wide totals.

## Inventory and provenance

| Finding | Snapshot result |
|---|---:|
| Product identity rows | 4,934 |
| Provider-scoped product codes | 4,539 |
| Provider/external-company scopes | 313 |
| Distinct source labels (exact string) | 2,699 |
| Distinct labels after Persian normalization | 2,629 |
| Repeated exact-label groups / rows in those groups | 334 / 2,569 |
| Normalized groups containing spelling/spacing variants | 59 |
| Rows in normalized-variant groups | 841 |
| Company identity resolution | {'external-company-only-pending-resolution': 4934} |
| Product review state | {'pending': 4934} |
| Candidate exact-label/unit groups | 224 (all pending; min/max company count 2 / 304) |

Provider: `NoavaranCurrentApi`. Source: `SemanticCatalog/dictionaries/product-values.yaml`; its entry provenance includes `externalReportId`, period, report type and output type. Every observed entry remains `external-company-only-pending-resolution` and `review.status=pending`, `executable=false`. These keys cannot yet be joined to canonical application companies for a complete company-wide population query.

The dictionary discovery metadata says `bounded-observation`, verified, scope `company-scoped product value identities`, sampled line items 5,000. The distinct code/key count is not evidence of equivalent products across companies.

## Approved product concepts

The reviewed catalog contains 1 concept(s). `product_iron_ore_pellets` is the sole approved concept; canonical label `گندله سنگ‌آهن` and aliases `گندله`, `گندله سنگ‌آهن`. Approval: `owner-confirmed:feature141-product-equivalence:2026-10-09`. Scope: `monthly production and sales report product titles`. Mapping coverage is `approved-aliases-only`, not exhaustive.

No other cross-company equivalences are approved by this audit. In particular, the 224 existing exact-label/unit proposals are only repeated-label candidates. Similarity, normalized spelling, equal units, or repeated labels do not establish business equivalence.

## Normalization findings

Normalization used Unicode NFKC, Persian/Arabic yeh and kaf normalization, removal of ZWNJ/directional marks, case folding, and punctuation/whitespace removal. It was used only to find review candidates; it does not merge persisted product IDs or approve aliases. There are 59 variant groups. Examples are preserved with exact source spellings in the decision CSV's labels and source identity references.

## Financial statement source items are separate

The separate source-item snapshot has 296 provider item identities. The unit review reports 296 rows: `{"MonetaryStatementItem": 285, "MonetaryPerShareItem": 8, "RequiresSemanticReview": 3}`. Those are financial statement source items, not monthly product aliases; they must not be used to fill product coverage. The source-item review remains governed separately.

## Missing mappings that constrain discovery

1. The approved pellet concept covers only its two approved Persian titles; no complete alias-coverage assertion is available for the bounded line-item snapshot.
2. All 4,934 sampled product rows lack canonical company resolution and have pending product review. Cross-company lists/rankings therefore cannot claim exhaustive company coverage from this dictionary.
3. The category candidate list has 224 non-executable proposals. Until reviewed, company-scoped product identities remain distinct.
4. Company population SQL can discover only rows matching approved product aliases. Unknown titles, unmapped provider identities, unauthorized companies, or missing report coverage must keep the answer `Incomplete`/disclosed rather than being treated as no production.

## Reviewer package

[`ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv`](ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv) has one row per candidate, all initialized to `PENDING_REVIEW`, with source label/unit, company count, provider external-company IDs, product identity IDs, report references, and blank decision/concept/reviewer/date fields. A reviewer may approve, reject, or defer each candidate after checking primary source evidence. The generator applied no new equivalence decision.

## Release boundary

This metadata snapshot is enough to support the owner-approved pellet alias concept as a bounded observation query, with partial coverage disclosed. It is not enough to claim arbitrary product discovery across canonical companies, industry-wide coverage, or complete product-specific sales rankings. Production readiness also requires canonical company mappings, approved equivalence decisions, and an independently complete coverage policy.

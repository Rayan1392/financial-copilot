#!/usr/bin/env python3
"""Create a source-backed Feature 141 product identity coverage audit."""
from __future__ import annotations

import csv
import json
import re
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / "specs" / "141-schema-aware-semantic-query-engine"
PRODUCT_VALUES = SPEC / "SemanticCatalog" / "dictionaries" / "product-values.yaml"
PRODUCT_CATALOG = SPEC / "ProductCatalog" / "product-catalog-v1.json"
SOURCE_ITEMS = SPEC / "SemanticCatalog" / "source-item-snapshot.json"
UNIT_REVIEW = SPEC / "source-item-unit-review-report.json"
REPORT = SPEC / "product-catalog-coverage-review-2026-10-09.md"
DECISIONS = SPEC / "ProductCatalog" / "reviewer-decisions" / "product-equivalence-candidates-v1.csv"


def normalized_fa(value: str) -> str:
    value = unicodedata.normalize("NFKC", value).translate(str.maketrans({
        "ي": "ی", "ى": "ی", "ك": "ک", "ۀ": "ه", "ة": "ه",
        "ؤ": "و", "أ": "ا", "إ": "ا", "ٱ": "ا",
    }))
    value = value.replace("\u200c", "").replace("\u200f", "").replace("\u200e", "")
    return re.sub(r"[\W_]+", "", value.casefold(), flags=re.UNICODE)


def load_json_or_json_yaml(path: Path):
    # These generated YAML artifacts are JSON documents, so stdlib JSON preserves exact source text.
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> None:
    dictionary = load_json_or_json_yaml(PRODUCT_VALUES)
    catalog = json.loads(PRODUCT_CATALOG.read_text(encoding="utf-8"))
    source_items = json.loads(SOURCE_ITEMS.read_text(encoding="utf-8"))
    unit_report = json.loads(UNIT_REVIEW.read_text(encoding="utf-8"))
    rows = dictionary["entries"]
    by_label: dict[str, list[dict]] = defaultdict(list)
    by_normalized: dict[str, list[dict]] = defaultdict(list)
    for row in rows:
        by_label[row["sourceLabel"]].append(row)
        by_normalized[normalized_fa(row["sourceLabel"])].append(row)
    spelling_variants = {
        key: values for key, values in by_normalized.items()
        if len({row["sourceLabel"] for row in values}) > 1
    }
    proposals = dictionary.get("crossCompanyCategoryProposals", [])
    entries = {row["id"]: row for row in rows}

    DECISIONS.parent.mkdir(parents=True, exist_ok=True)
    with DECISIONS.open("w", encoding="utf-8-sig", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=[
            "proposal_id", "candidate_label", "normalized_label", "observed_unit",
            "company_count", "provider", "external_company_ids", "product_identity_ids",
            "report_references", "evidence_rule", "decision", "canonical_concept_id",
            "reviewer", "review_date", "decision_notes",
        ])
        writer.writeheader()
        for proposal in proposals:
            candidates = [entries[item_id] for item_id in proposal["companyScopedProductEntryIds"] if item_id in entries]
            writer.writerow({
                "proposal_id": proposal["id"],
                "candidate_label": proposal["candidateLabel"],
                "normalized_label": normalized_fa(proposal["candidateLabel"]),
                "observed_unit": proposal.get("sourceUnit", ""),
                "company_count": proposal.get("companyCount", 0),
                "provider": ";".join(sorted({row.get("provider", "") for row in candidates})),
                "external_company_ids": ";".join(sorted({row.get("externalCompanyId", "") for row in candidates})),
                "product_identity_ids": ";".join(proposal.get("companyScopedProductEntryIds", [])),
                "report_references": ";".join(sorted({
                    row.get("provenance", {}).get("externalReportId", "") for row in candidates
                    if row.get("provenance", {}).get("externalReportId")
                })),
                "evidence_rule": proposal.get("evidenceRule", ""),
                "decision": "PENDING_REVIEW",
                "canonical_concept_id": "",
                "reviewer": "",
                "review_date": "",
                "decision_notes": "Exact label/unit recurrence is discovery evidence only; equivalence is not asserted.",
            })

    pellet = next((item for item in catalog["concepts"] if item.get("catalogId") == "product_iron_ore_pellets"), None)
    exact_duplicate_groups = sum(1 for values in by_label.values() if len(values) > 1)
    exact_duplicate_rows = sum(len(values) for values in by_label.values() if len(values) > 1)
    provider_product_codes = {(row.get("provider"), row.get("productCode")) for row in rows}
    provider_company_scopes = {(row.get("provider"), row.get("externalCompanyId")) for row in rows}
    company_resolution = Counter(row.get("companyResolution") for row in rows)
    identity_review = Counter((row.get("review") or {}).get("status") for row in rows)
    unit_counts = Counter(row.get("unit") or "(missing)" for row in rows)
    proposal_sizes = [proposal.get("companyCount", 0) for proposal in proposals]

    markdown = f"""# Feature 141 product identity coverage and review

Audit date: 2026-10-09. This report describes the checked-in generated snapshots; it did not connect to a Development or production database. The monthly product-value dictionary records a bounded sample of 5,000 line items and contains {len(rows):,} distinct persisted identity rows, so these counts are sample coverage, not market-wide totals.

## Inventory and provenance

| Finding | Snapshot result |
|---|---:|
| Product identity rows | {len(rows):,} |
| Provider-scoped product codes | {len(provider_product_codes):,} |
| Provider/external-company scopes | {len(provider_company_scopes):,} |
| Distinct source labels (exact string) | {len(by_label):,} |
| Distinct labels after Persian normalization | {len(by_normalized):,} |
| Repeated exact-label groups / rows in those groups | {exact_duplicate_groups:,} / {exact_duplicate_rows:,} |
| Normalized groups containing spelling/spacing variants | {len(spelling_variants):,} |
| Rows in normalized-variant groups | {sum(map(len, spelling_variants.values())):,} |
| Company identity resolution | {dict(company_resolution)} |
| Product review state | {dict(identity_review)} |
| Candidate exact-label/unit groups | {len(proposals):,} (all pending; min/max company count {min(proposal_sizes, default=0)} / {max(proposal_sizes, default=0)}) |

Provider: `NoavaranCurrentApi`. Source: `SemanticCatalog/dictionaries/product-values.yaml`; its entry provenance includes `externalReportId`, period, report type and output type. Every observed entry remains `external-company-only-pending-resolution` and `review.status=pending`, `executable=false`. These keys cannot yet be joined to canonical application companies for a complete company-wide population query.

The dictionary discovery metadata says `bounded-observation`, verified, scope `company-scoped product value identities`, sampled line items 5,000. The distinct code/key count is not evidence of equivalent products across companies.

## Approved product concepts

The reviewed catalog contains {len(catalog['concepts'])} concept(s). {('`' + pellet['catalogId'] + '` is the sole approved concept; canonical label `' + pellet['canonicalLabel'] + '` and aliases `' + '`, `'.join(pellet['aliases']) + '`. Approval: `' + pellet['approvalReference'] + '`. Scope: `' + pellet['equivalenceScope'] + '`. Mapping coverage is `' + pellet['mappingCoverageStatus'] + '`, not exhaustive.') if pellet else 'No approved pellet concept was found.'}

No other cross-company equivalences are approved by this audit. In particular, the 224 existing exact-label/unit proposals are only repeated-label candidates. Similarity, normalized spelling, equal units, or repeated labels do not establish business equivalence.

## Normalization findings

Normalization used Unicode NFKC, Persian/Arabic yeh and kaf normalization, removal of ZWNJ/directional marks, case folding, and punctuation/whitespace removal. It was used only to find review candidates; it does not merge persisted product IDs or approve aliases. There are {len(spelling_variants)} variant groups. Examples are preserved with exact source spellings in the decision CSV's labels and source identity references.

## Financial statement source items are separate

The separate source-item snapshot has {len(source_items.get('items', []))} provider item identities. The unit review reports {unit_report.get('snapshotItemCount')} rows: `{json.dumps(unit_report.get('classificationCounts', {}), ensure_ascii=False)}`. Those are financial statement source items, not monthly product aliases; they must not be used to fill product coverage. The source-item review remains governed separately.

## Missing mappings that constrain discovery

1. The approved pellet concept covers only its two approved Persian titles; no complete alias-coverage assertion is available for the bounded line-item snapshot.
2. All {len(rows):,} sampled product rows lack canonical company resolution and have pending product review. Cross-company lists/rankings therefore cannot claim exhaustive company coverage from this dictionary.
3. The category candidate list has {len(proposals):,} non-executable proposals. Until reviewed, company-scoped product identities remain distinct.
4. Company population SQL can discover only rows matching approved product aliases. Unknown titles, unmapped provider identities, unauthorized companies, or missing report coverage must keep the answer `Incomplete`/disclosed rather than being treated as no production.

## Reviewer package

[`ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv`](ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv) has one row per candidate, all initialized to `PENDING_REVIEW`, with source label/unit, company count, provider external-company IDs, product identity IDs, report references, and blank decision/concept/reviewer/date fields. A reviewer may approve, reject, or defer each candidate after checking primary source evidence. The generator applied no new equivalence decision.

## Release boundary

This metadata snapshot is enough to support the owner-approved pellet alias concept as a bounded observation query, with partial coverage disclosed. It is not enough to claim arbitrary product discovery across canonical companies, industry-wide coverage, or complete product-specific sales rankings. Production readiness also requires canonical company mappings, approved equivalence decisions, and an independently complete coverage policy.
"""
    REPORT.write_text(markdown, encoding="utf-8", newline="\n")
    print(f"Wrote {REPORT} and {DECISIONS} ({len(proposals)} pending decisions).")


if __name__ == "__main__":
    main()

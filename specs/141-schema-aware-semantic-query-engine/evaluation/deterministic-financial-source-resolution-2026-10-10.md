# Feature 141 — Deterministic Financial Source Resolution & Bounded Shadow Proof

Date: 2026-10-10

## Outcome

The 296-record approved source registry supports deterministic source-identity discovery by exact Persian title after conservative orthographic normalization. This establishes identity only; it does not add canonical business-concept aliases or query authorization.

| Registry measure | Result |
|---|---:|
| Approved source rows | 296 (BalanceSheet 107, IncomeStatement 119, CashFlow 70) |
| Unique normalized exact-title groups | 296 |
| Ambiguous/conflicting title groups | 0 |
| Existing approved canonical source binding | 1 (`operatingProfitPerShare` → `IncomeStatement/161`) |
| Other source records still needing reviewed canonical equivalence for concept-based binding | 295 |
| Query-authorized source records | 0 |

The count is against `SemanticCatalog/dictionaries/financial-statement-source-items.yaml`, whose source snapshot has the same 296 provider/statement/source identities as reported in the registry import report. No live database was queried.

## Implementation and safety boundary

- Added `FinancialSourceItemTitleResolver`, which compares only Unicode NFKC-normalized titles after Arabic/Persian Yeh and Kaf variant normalization, ZWNJ-to-separator handling, and whitespace folding. It does not case-fold, strip punctuation, use synonyms, fuzzy scores, embeddings, or nearest-neighbor fallback.
- Resolution filters for owner-approved source identity and title, verified persisted identity, and a present source UUID. Optional provider and statement-type constraints narrow exact matches. A conflicting scope yields `Incompatible`; multiple compatible matches yield `Ambiguous`; no match yields `NotFound`.
- Added a catalog source-reference index method to `JsonCatalogNavigationHierarchyProvider`, separate from the Navigator’s bounded/ranked natural-language outline. Extended parsed references to retain effective unit, unit classification, unit provenance, and query authorization state alongside UUID, official title, source/effective unit, approvals, and existing canonical bindings.
- Requested statement qualifiers are carried through the result. Since the current source registry has no standalone/consolidated qualifier evidence, a request that supplies such qualifiers returns `Incomplete` (`statement_qualifier_evidence_unavailable`) instead of asserting compatibility.
- The resolver does not create canonical bindings. `operatingProfitPerShare → IncomeStatement/161` remains the only approved binding. Title-only identities remain non-executable.

## Five-case deterministic proof

The focused `SchemaNavigatorTests` resolve each exact title against the generated 296-reference hierarchy and verify statement, source ID/UUID, unit and dimension evidence, and `not-authorized` state.

| Source identity | Result | Evidence retained |
|---|---|---|
| `IncomeStatement/143` — سود خالص | Resolved | MillionIRR; MonetaryStatementItem; pending semantic review; no canonical binding; not authorized |
| `IncomeStatement/161` — سود عملیاتی هر سهم | Resolved | IRRPerShare; CurrencyPerShare; existing approved per-share binding retained; not authorized |
| `BalanceSheet/138` — جمع کل دارایی‌ها | Resolved | MillionIRR; MonetaryStatementItem; pending semantic review; not authorized |
| `CashFlow/1` — جریان وجه نقد عملیاتی | Resolved | MillionIRR; MonetaryStatementItem; pending semantic review; not authorized |
| Synthetic duplicate exact title across two statement types | Ambiguous; no arbitrary reference selected | Supplying `IncomeStatement` disambiguates to the matching exact identity |

Additional assertions cover Arabic/Persian Yeh normalization, repeated whitespace, a non-exact title (`NotFound`), a wrong statement scope (`Incompatible`), and preservation plus typed rejection of an unverified consolidation qualifier.

## Navigator contract compatibility

The enriched `FinancialValueCatalogReference`/`SchemaContext` contract can carry full source identity and governance metadata. The LLM-facing outline’s `sourceItemCandidates` remains a bounded retrieval hint and omits UUID and unit/authorization evidence; it is not a complete registry-resolution contract. Exact title resolution therefore uses the explicit full source-reference index, not an LLM outline candidate or a canonical-measure binding.

The current source registry records `StatementType` but has no machine-readable standalone/consolidated or comparable statement-scope qualifier evidence. Thus exact title discovery is compatible as an identity operation, while qualifier-sensitive Navigator planning is incomplete. No production or Development chat route was changed.

## End-to-end status and next requirements

No PostgreSQL execution or production-like query was attempted. Catalog authorization remains disabled for all 296 records, as required. A restricted E2E test still requires (1) a typed caller path that carries this exact-identity result into the shadow planning boundary without asserting canonical equivalence, (2) governed evidence for any requested statement-scope qualifiers, and (3) explicitly fixture-only database scope with restricted credentials and a query policy that cannot reach production records. None of these requirements is satisfied by owner approval of identity/title alone.

## Verification

- Focused Release `SchemaNavigatorTests`: 28 passed, 0 failed.
- Release solution build (`--no-restore`): passed, 0 warnings/errors.
- SemanticCatalog validation: passed; source hash `ceaac8fcd3aca514bf8f3b51da93d7da098b90d2710b53e3c7bac98135f20eb2`; hierarchy hash `178e9370ad553e639bd514d8a75a21229e214712a516b31afcbc756c7689e1e4`.
- `git diff --check` reported an existing trailing-space issue in generated `artifacts/navigation-hierarchy.md` line 4, plus repository line-ending warnings. That unrelated generated artifact was left unchanged.
- No broad LLM benchmark, PostgreSQL query, authorization change, or routing change was performed.

## Recommendation

Source-identity discovery is proven for the frozen approved registry snapshot. Keep shadow SQL integration **NO_GO** until the typed planning handoff, qualifier evidence where needed, and fixture-only authorization boundary are implemented and verified. Do not treat title discovery as semantic approval.

# Feature 136 — KGL Pellet Product Identity Investigation

Date: 2026-10-04

Scope: targeted, read-only investigation of the real NADPCO ProductSales / OutputType=0 data persisted for KGL (`ExternalCompanyId=4`). No production code, normalized data, or provider payload was changed. No credentials or secrets were read into this report.

## Finding

The four candidates are `DISTINCT_PRODUCTS`, not historical aliases or duplicate normalized representations. They coexist in the same accepted monthly reports, have different provider titles, and carry different reporting dimensions: base pellet, fine-grained pellet, export-labeled pellet, and fine-grained export-labeled pellet.

The provider does not supply a usable product identity for these rows: `ProductCode` is absent and raw `ProductId` is `0` for every inspected candidate. The current company-scoped title/unit ProductKeys therefore correctly remain distinct.

## Evidence set

- Provider: `NoavaranCurrentApi` / NADPCO.
- Company: KGL (`کگل`), provider company ID `4`, raw symbol `کگل`.
- Instrument identifier: `35700344742885862` for all inspected rows.
- Accepted reports: 18, covering fiscal periods `1404/01` through `1405/06`.
- Report type: `ProductSales`; output type: `0` (the raw payload stores this data in the `ProductSalesType0` envelope; the individual raw rows do not repeat an output-type field).
- Raw payload lookup: the persisted provider payload matching each accepted report checksum.
- Normalized evidence: accepted `MonthlyReportLineItems` matched back to raw rows by exact title, exact unit, and fiscal year/month.

## Four candidate identities

All four rows use unit `تن` and the same provider category `استخراج سنگ معدن های فلزی آهنی`. Their ProductKeys are distinct because no valid provider code or positive provider ID is available, so the approved fallback is title plus unit.

| Candidate | ProductKey | Provider code | Provider ID | Identity provenance | Raw title / normalized title | Active periods | Observations | Production quantity total | Sales quantity total | Sales value total | Accepted report/revision identity |
|---|---|---|---|---|---|---|---:|---:|---:|---:|---|
| Base pellet | `NoavaranCurrentApi:4:TITLE:گندله\|UNIT:تن` | absent | `0` in raw / null normalized | company-scoped normalized title + unit | `گندله` / `گندله` | `1404/01`–`1405/06` | 18 | 18,366,917 | 18,137,178 | 1,727,700,086 | `ProductSales:4:1404-01..1404-12:output-0` revision `655F78…`; `1405/01` `25D5C8…`; `1405/02` `BB2B8C…`; `1405/03` `0802AF…`; `1405/04` `541DC2…`; `1405/05` `53AE07…`; `1405/06` `5AC79A…` |
| Fine-grained pellet | `NoavaranCurrentApi:4:TITLE:گندله ریزدانه\|UNIT:تن` | absent | `0` in raw / null normalized | company-scoped normalized title + unit | `گندله ریزدانه` / `گندله ریزدانه` | `1404/01`–`1405/06` | 18 | 133,254 | 160,511 | 7,560,469 | Same accepted report/revision sequence as the base pellet |
| Fine-grained export pellet | `NoavaranCurrentApi:4:TITLE:گندله ریزدانه صادراتی فروش صادراتی\|UNIT:تن` | absent | `0` in raw / null normalized | company-scoped normalized title + unit | `گندله ریزدانه صادراتی فروش صادراتی` / same | `1404/01`–`1404/05` | 5 | 0 | 0 | 0 | Accepted reports `ProductSales:4:1404-01..1404-05:output-0`, revision `655F78…` |
| Export-labeled pellet | `NoavaranCurrentApi:4:TITLE:گندله فروش صادراتی\|UNIT:تن` | absent | `0` in raw / null normalized | company-scoped normalized title + unit | `گندله فروش صادراتی` / `گندله فروش صادراتی` | `1404/01`–`1405/06` | 18 | 265,385 | 902,801 | 71,733,975 | Same accepted report/revision sequence as the base pellet |

The period ranges are inclusive. “Production/sales/value total” is the aggregate of the accepted normalized observations for that candidate; it is evidence of separate reporting streams, not an instruction to aggregate them.

The accepted report revision fingerprints, grouped by fiscal period, were:

| Fiscal periods | Revision fingerprint |
|---|---|
| `1404/01`–`1404/12` | `655F78D5BBD756C52382BD14DAE9DDEFE0839554AEBB9AFBD3E9163F6879AC42` |
| `1405/01` | `25D5C8F39CF06712CB696DDBD2B1C5BA2F3BE78E682B197ABCFE65EE68C15869` |
| `1405/02` | `BB2B8CD296DA5A924D79BBB718A43797597CFC2D28FB8293B410459B955F1FF3` |
| `1405/03` | `0802AFC01BE0ACF60857A9F2C93DAB3DD9DA3F01FD9E5635419A0CC8D9A22493` |
| `1405/04` | `541DC27588A0619E749793D50EB3A1DE487F535C006D69FA64ADC27133792EAE` |
| `1405/05` | `53AE0786D6F47CDEDC448D299CF2FA97A0AD0485A2B1169E8A1A07A497D25D14` |
| `1405/06` | `5AC79AE54F26C170EC931F1CB495D9A71E717D70B5CB4D6E61CA78D482D22C76` |

## Raw NADPCO evidence

The normalized rows were traced to exact raw provider rows. For every candidate and inspected period:

- raw title equals the normalized title exactly;
- raw unit is `تن`;
- raw `ProductCode` is absent;
- raw `ProductId` is `0`;
- raw company ID is `4`, raw symbol is `کگل`, and raw instrument is `35700344742885862`;
- raw category is `استخراج سنگ معدن های فلزی آهنی`;
- raw production quantity, sales quantity, sales value, and provider-reported sale rate equal the corresponding normalized values;
- no raw activity identifier was present for these product rows.

The raw titles are therefore not four spellings of one identical title:

1. `گندله`
2. `گندله ریزدانه`
3. `گندله فروش صادراتی`
4. `گندله ریزدانه صادراتی فروش صادراتی`

The titles explicitly distinguish fine-grained material (`ریزدانه`) and export-labeled sales (`فروش صادراتی`). The raw evidence does not establish that the base row is a duplicate of either export row, and the same-month coexistence disproves a simple historical rename. The common category is too broad to erase the title distinctions.

### Same-month economic comparison

In accepted fiscal month `1404/01`, all four rows are present in the same report/revision:

| Raw title | Production quantity | Sales quantity | Sales value | Implied value/quantity rate (`value * 100000 / quantity`) |
|---|---:|---:|---:|---:|
| `گندله` | 1,088,007 | 968,946 | 72,955,641 | 7,529,381.51352088 |
| `گندله فروش صادراتی` | 0 | 67,902 | 4,869,821 | 7,171,837.35383347 |
| `گندله ریزدانه صادراتی فروش صادراتی` | 0 | 0 | 0 | unavailable because quantity is zero |
| `گندله ریزدانه` | 8,431 | 0 | 0 | unavailable because quantity is zero |

The rows are separate facts in one provider report. Numerical similarity is not used as an identity rule, and no cross-product rate or weighted aggregate was created.

## Temporal overlap matrix

| Fiscal period | `گندله` | `گندله ریزدانه` | `گندله فروش صادراتی` | `گندله ریزدانه صادراتی فروش صادراتی` |
|---|---:|---:|---:|---:|
| `1404/01`–`1404/05` | present | present | present | present |
| `1404/06`–`1404/12` | present | present | present | absent |
| `1405/01`–`1405/06` | present | present | present | absent |

Thus all four coexist in the same accepted reports for `1404/01` through `1404/05`. The fifth candidate’s later absence is not evidence of a rename because the other three continue and the candidate was concurrently present before it disappeared. No historical-alias mapping is safe.

## Provider-code stability

There is no stable provider product identity to compare across months:

- `ProductCode`: absent for all four candidates in all inspected periods;
- `ProductId`: raw value `0` for all four candidates in all inspected periods, normalized as null because it is not a positive identity;
- no code transition, reuse, or report-local code migration was observed;
- title and unit are the only available identity provenance.

The missing provider identity is an ingestion limitation, but it is not a reason to merge the four rows. The provider’s exact titles and same-period coexistence are stronger evidence that these are distinct reporting rows.

## Exact-title and partial-match policy

The current resolver’s candidate predicate accepts either an exact normalized title, a `Contains` match, or an exact provider code. For requested text `گندله`:

- `گندله` is an exact normalized-title match;
- the other three candidates are only partial/contains matches;
- because the current predicate collects all four before deciding, the result is typed `Ambiguous`.

This observed ambiguity is safe and must not be weakened by selecting the first candidate. A bare natural-language request `گندله کگل` does not prove that the user meant only the provider row whose exact title is `گندله`; the same accepted reports contain fine-grade and export-labeled variants. The generic query cannot safely auto-resolve.

A future policy improvement may distinguish `ExactTitle` from `PartialTitle` in typed candidate metadata and resolve an exact match only when the caller expresses an explicit exact-title/product attribute. Without that explicit attribute, a generic request remains ambiguous when exact and partial candidates coexist.

## Classification

**`DISTINCT_PRODUCTS`**

The evidence supports distinct provider-reported economic dimensions:

- base pellet versus fine-grained pellet;
- base/export-labeled sales versus fine-grained export-labeled sales;
- separate rows and separate quantities/values in the same accepted monthly report.

This is not `HISTORICAL_ALIASES` because the rows overlap in time. It is not `DUPLICATE_IDENTITY_ARTIFACT` because raw titles and facts are distinct. It is not `SPLIT_COMPONENTS_OF_ONE_COMMERCIAL_PRODUCT` because the provider evidence does not prove a composition rule, and the titles describe different variants/channels. It is not `INSUFFICIENT_EVIDENCE` because the raw title, period-overlap, and numeric-row evidence is sufficient to preserve separate identities.

## Smallest safe resolution rule

1. Keep the current `Ambiguous` result for generic `گندله کگل`.
2. Never aggregate these ProductKeys for product sale rate merely because the titles contain the same base word or share unit/category.
3. If clarification UX is added later, expose bounded typed candidate metadata rather than opaque ProductKeys.
4. Only permit deterministic exact-title selection when the user/request contract explicitly carries an exact-title attribute; do not infer that attribute from the bare product word.

### Suggested typed ambiguity metadata

Each candidate should be able to expose, without exposing internal secrets:

- display/raw provider title;
- normalized title;
- unit;
- category title;
- provider ProductCode, when present;
- provider ProductId, when positive;
- ProductKey;
- identity provenance (`ProviderCode`, `ProviderId`, or `CompanyScopedTitleAndUnit`);
- match kind (`ExactTitle`, `PartialTitle`, or `ProviderCode`);
- provider company ID, symbol, and instrument identifier;
- first and last active fiscal period;
- bounded active-period summary or count;
- accepted-report evidence references.

This metadata supports a meaningful clarification choice without changing the domain invariant or finalizing user-facing copy in this investigation.

## Production code and re-ingestion decision

- Production code change required for this investigation: **No**.
- Current behavior: **domain-safe**; it returns typed `Ambiguous` and does not fall back to company trend.
- Additional re-ingestion required to classify the identity: **No**. The existing accepted reports and persisted raw payloads cover the necessary overlapping periods.
- A future provider-identity enrichment/backfill could improve provenance, but it must not merge these rows unless NADPCO explicitly supplies evidence that the titles are aliases of the same economic product.
- Generic `گندله کگل` can safely auto-resolve: **No**, not with the current provider evidence and request semantics.

FEATURE_136_KGL_PRODUCT_IDENTITY_INVESTIGATED

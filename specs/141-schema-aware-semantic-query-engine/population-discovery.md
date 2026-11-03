# Feature 141 Population Discovery

## Approved product metadata

`ProductCatalog/product-catalog-v1.json` records the owner's approval that «گندله» and
«گندله سنگ‌آهن» resolve to `product_iron_ore_pellets`. Runtime configuration mirrors that
versioned entry. Resolution is exact after Persian Unicode/spacing normalization; unknown,
colliding, or pending aliases are not promoted by lexical similarity. Current coverage is
`approved-aliases-only`, so a result cannot be presented as an exhaustive market-wide list.

## Query semantics

When a monthly query has one reviewed product concept and no company mention, the offline planner
creates a population-discovery plan with one unbound Product entity and no Company entity or company
IDs. The plan carries a separately authorized tenant discovery scope and a coverage policy. An
explicit reporting month selects only that report period. With no month, the planner uses the prior
12 calendar months and describes the results as observed production in that window; it does not
claim current/ongoing production.

The monthly compiler currently supports positive monthly production discovery over approved
Noavaran `ProductSales` output-type-zero reports. It joins line items to reports and reports to
Companies through catalog relationships, filters to accepted non-cumulative reports and approved
product aliases, then returns the latest eligible observation in the requested window per company.
Rows carry company identity, alias, product code, quantity/unit, report period, provider, report IDs,
and source-row fingerprint. Zero/negative values cannot qualify. Conflicting same-period evidence is
reported as ambiguous. Partial product/scope coverage is reported as incomplete with evidence when
the response contract permits it.

No producer tickers, company IDs, or product-to-company registry are present in the product catalog
or planner. The execution authorizer supplies its independently approved company scope; this is an
authorization boundary, not a producer registry. A tenant must have a configured discovery scope
before runtime execution is allowed. `Feature141:CompanyDiscoveryScopes` is empty by default, so the
application remains fail-closed until an owner/admin provisions an approved tenant scope and its
read-only database grants.

## Current implementation boundary

This increment establishes unbound production discovery. It does not yet implement generic
population predicates for sales thresholds, reviewed industry membership, arbitrary Boolean
compositions, company-level aggregate ranking, or population discovery over other financial
domains. Those requests must remain unsupported until their semantic definitions and catalog
relationships are approved. No query-specific producer list or SQL template should be added to
expand support.

## Verification

- Unit tests cover both aliases resolving to the same versioned concept, unknown/pending/colliding
  aliases, and tenant-scoped discovery metadata without a company-ID property.
- The restricted-role PostgreSQL integration fixture adds two existing synthetic producers, a new
  company with a positive observation under the long approved alias, and a company with zero
  production. It checks the plan has no company IDs, exact reporting-period filtering, source
  evidence, and exclusion of zero production.
- The integration test was compiled but skipped at runtime because the configured PostgreSQL test
  connection is absent and the local Docker PostgreSQL engine is unavailable. Therefore the fixture
  SQL assertions are not claimed as executed database evidence. See
  [dynamic-company-discovery.md](evaluation/dynamic-company-discovery.md).

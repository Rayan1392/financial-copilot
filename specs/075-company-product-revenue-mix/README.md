# Feature 075 — Company Product Revenue Mix

Status: **Implemented; deterministic follow-up extension specified for V2.**

Feature 075 owns the persisted company product-revenue composition, the typed
`ProductRevenueMixResponse`, and the `product_revenue_mix` query/result shown for requests such as
`ترکیب فروش محصولات فخوز`.

## Deterministic follow-up extension

After a successful typed ProductRevenueMix response, V2 owns an authoritative collection of at
most three existing `SuggestedAction` values:

1. product sales trend for the first valid row in the displayed revenue-mix order;
2. product sales trend for the second valid row in that order;
3. parent-company monthly sales trend.

The selector reads only the typed result. It does not query the database again and does not ask an
LLM to infer a product. Product actions use `روند فروش {CanonicalProductTitle}
{CanonicalCompanySymbol}` and target `monthly_product_trend`; the company action uses `روند فروش
ماهانه {CanonicalCompanySymbol}` and targets `monthly_activity_trend`.

The existing `DeterministicSuggestionsApplied` marker is authoritative. A successful response with
no safe action persists an explicit empty action collection and does not fall back to generic
guidance. Failed, missing, ambiguous, clarification, unsupported, and no-data results keep existing
guidance behavior.

See [Design.md](./Design.md) for eligibility and round-trip rules and [tasks.md](./tasks.md) for the
implementation extension.

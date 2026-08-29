# Feature 115 Design Amendment - P/S TTM Marker Source

## Scope

This amendment changes only the P/S gauge marker associated with `PS_TTM` (the
current TTM P/S value). The gauge still has its existing six provider-defined
slices and two markers:

- TTM marker: in scope; use `LastPS.data.ps_ratio`.
- Forward marker: out of scope; retain its existing source and behavior.

The P/E gauge and both of its marker sources are unchanged by this amendment.

## Authoritative source and lookup

For the resolved company, select the latest accepted row from
`CyclicalWavesMetricSnapshots` satisfying:

```text
MetricType = 'LastPS'
SymbolIsin = resolvedCompany.SymbolIsin
```

Extract the marker value from the persisted raw payload at:

```text
RawResponseJson.data.ps_ratio
```

The persisted-snapshot reader must explicitly include `LastPS` in its supported
metric types. The lookup is matched on the normalized, exact `SymbolIsin` of the
resolved company; ticker, company name, or normalized `PS_TTM` are not fallback
identities.

`RawResponseJson` remains immutable provider evidence. Do not add a parsed
consumer column to `CyclicalWavesMetricSnapshots`; the visualization read model
may project the value while retaining `SnapshotId`, `ResponseHash`,
`SymbolIsin`, acquisition time, and observation date for traceability.

An accepted row is one whose matching acquisition check has a successful
`Changed` or `NoChange` result and matching snapshot/hash identity. Filter to
rows with a valid numeric `data.ps_ratio` before applying the existing
deterministic latest-snapshot ordering: `AcquisitionDateUtc`, then
`CreatedAtUtc`, then `Id`, descending. Failed, malformed, or invalid snapshots
are not eligible. Missing or non-numeric `data.ps_ratio` is missing/invalid,
not zero and not a fallback. If the newest successful acquisition row has an
invalid marker,
an older valid row may be selected only when the explicit fallback policy allows
it, and the result must disclose that it is older/fallback data.

## Gauge calculation and presentation

Use the extracted `LastPS.data.ps_ratio` as the sole P/S TTM marker input for
the existing piecewise `start..min`, four `min..max`, and `max..end`
interpolation rules, including visible clamping outside `start..end`.

Gauge distribution/boundaries remain sourced from the existing PS gauge
snapshot. The Forward marker/value is not recalculated or replaced. Normalized
`PS_TTM` may continue to serve ordinary metric lookup, but must not silently
drive this visualization marker.

Standalone, compact monthly-sales, conversation-reload, and PNG-export views
must consume the same structured marker value and computed angle. The LLM does
not extract or calculate the value.

When `ttmPs.value` is present in the structured result, it must be the selected
`LastPS.data.ps_ratio`. `needle.sourceValue` is provenance for that same value,
not an independent fallback. Frontend and export renderers must never select a
different normalized `PS_TTM` value for the TTM marker.

## States and acceptance

Expose a semantic source such as `LastPS.ps_ratio` plus source snapshot/date.
Keep zero distinct from missing. If the gauge is otherwise renderable but the
LastPS value is unavailable or invalid, do not draw a guessed TTM marker and do
not substitute `PS_TTM`, gauge `close`, Forward P/S, or a locally calculated
ratio; use the existing partial/invalid state and warning.

No vendor request is allowed on AI, API, frontend, reload, or export paths.
Existing plain `PS_TTM` lookup, scanner, and ComprehensiveAnalysis behavior is
unchanged.

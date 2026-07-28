# Amendment — spec `065` Changes Required by spec `066`

This file documents the **minimal amendments** to `065-cyclicalwaves-comprehensive-analysis-sync/tasks.md`
that must be applied when delivering spec `066`. These are additive changes only; no existing
spec `065` task is removed or restructured.

---

## TASK-002 Amendment — Add `PlainTextSummary` column to persistence model

**Original task:** create `ComprehensiveAnalysisRow` persistence model.

**Amendment:** add `PlainTextSummary` column to the row type and EF configuration.

```csharp
// In ComprehensiveAnalysisRow
public string? PlainTextSummary { get; set; }   // populated at write time, null until backfill
```

EF configuration addition:

```csharp
builder.Property(r => r.PlainTextSummary)
    .HasColumnName("PlainTextSummary")
    .HasMaxLength(10_000)
    .IsRequired(false);
```

The column is nullable so the spec `065` migration (`AddComprehensiveAnalysis`) does not fail
on rows that exist before spec `066` is deployed. Spec `066` TASK-001 adds this column in its
own migration (`AddComprehensiveAnalysisPlainTextSummary`) — do not merge the two migrations.

> If spec `065` has not yet been applied to production at the time spec `066` is delivered,
> the two migrations may be consolidated into one at the operator's discretion. In that case,
> `PlainTextSummary` may be declared NOT NULL with a computed default of `''`.

---

## TASK-007 Amendment — Inject `IHtmlTextStripper` into upsert repository

**Original task:** implement `IComprehensiveAnalysisRepository.UpsertAsync`.

**Amendment:** inject `IHtmlTextStripper` (defined in spec `066` TASK-002) and set
`PlainTextSummary` on every row before `SaveChangesAsync`:

```csharp
// Existing upsert loop — add this line before SaveChangesAsync
row.PlainTextSummary = _stripper.Strip(item.Summary);
```

This is a one-line addition to the existing method body. The upsert logic, dedup key (`Id`),
child row delete-and-reinsert strategy, and `SyncLog` writes remain unchanged.

---

## TASK-010 Amendment — Add `SyncedAt` to the read query result

**Original task:** implement `IComprehensiveAnalysisQueryRepository.GetLatestBySymbolAsync`
and related methods.

**Amendment:** include `SyncedAt` in the `ComprehensiveAnalysisSummaryItem` projection so
spec `066` TASK-015 (confidence freshness calculation) can evaluate data age:

```csharp
// Add to ComprehensiveAnalysisSummaryItem record (defined in spec 065 TASK-010)
DateTimeOffset SyncedAt
```

Query projection:

```csharp
SyncedAt = a.SyncedAt
```

No change to filter logic, ordering, or indexes.

---

## Summary of net-new lines across spec `065`

| Location | Change |
|---|---|
| `ComprehensiveAnalysisRow` | `+1` property `PlainTextSummary` |
| EF configuration | `+3` lines column config |
| `UpsertAsync` | `+1` line `row.PlainTextSummary = _stripper.Strip(...)` |
| `ComprehensiveAnalysisSummaryItem` | `+1` property `SyncedAt` |
| Query projection | `+1` line `SyncedAt = a.SyncedAt` |
| DI registration | `+1` line — `IHtmlTextStripper` is injected via constructor |

Total changes to spec `065` code: **≤ 8 lines**. No spec `065` tests need to change;
the upsert unit test for spec `065` TASK-007 may add one assertion:
`row.PlainTextSummary` is not null and contains no HTML tags.

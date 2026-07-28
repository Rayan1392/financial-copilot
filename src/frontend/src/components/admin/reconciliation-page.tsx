import { GitCompare, Construction } from "lucide-react";

export function ReconciliationPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-xl font-bold flex items-center gap-2">
          <GitCompare className="size-5" /> Reconciliation
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          Source coverage by dataset/year/company, conflicts, missing periods, and stale data.
        </p>
      </div>

      <div className="rounded-xl border border-dashed border-border bg-surface/30 p-12 flex flex-col items-center justify-center text-center gap-4">
        <div className="rounded-2xl bg-muted p-4">
          <Construction className="size-8 text-muted-foreground" />
        </div>
        <div>
          <h2 className="text-sm font-semibold">Coming in a future release</h2>
          <p className="text-xs text-muted-foreground mt-1 max-w-sm">
            Reconciliation queries require dedicated backend read models that aggregate coverage,
            conflict detection, and staleness signals across all ingestion sources. These will be
            added once the direct TSETMC feed (spec 054 Phase 3) introduces shadow-mode comparison.
          </p>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 mt-4 w-full max-w-lg">
          {["Source Coverage", "Conflicts", "Missing Periods", "Stale Data"].map((label) => (
            <div key={label} className="rounded-lg border border-dashed border-border p-3 text-center">
              <p className="text-xs text-muted-foreground">{label}</p>
              <p className="text-lg font-bold text-muted-foreground/40 mt-1">—</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

import { useCallback, useEffect, useState } from "react";
import { Database, AlertCircle, CheckCircle2, RefreshCw, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  type AdminStockMarketSyncStateResponse,
  getStockMarketSyncState,
  runStockMarketSync,
} from "@/integrations/financial-copilot/data-admin-client";

const DATASETS = [
  "Instruments",
  "IntradayTrades",
  "DailyTrades",
  "IntradayIndices",
  "HistoricalDailyIndices",
] as const;

function formatDt(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("en-GB", { dateStyle: "short", timeStyle: "medium" });
}

function freshnessLabel(iso: string | null): { label: string; cls: string } {
  if (!iso) return { label: "Never synced", cls: "text-destructive" };
  const ageMin = (Date.now() - new Date(iso).getTime()) / 60_000;
  if (ageMin < 5) return { label: "Fresh", cls: "text-emerald-600" };
  if (ageMin < 30) return { label: `${Math.round(ageMin)} min ago`, cls: "text-amber-600" };
  return { label: formatDt(iso), cls: "text-muted-foreground" };
}

export function StockMarketDbPage() {
  const [states, setStates] = useState<AdminStockMarketSyncStateResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [lastMsg, setLastMsg] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setStates(await getStockMarketSyncState());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load StockMarketDB state.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { reload(); }, [reload]);

  async function doSync(dataset: string, fullReload: boolean) {
    const key = `${dataset}-${fullReload}`;
    setBusy(key);
    setActionError(null);
    setLastMsg(null);
    try {
      const result = await runStockMarketSync(dataset, fullReload);
      setLastMsg(`${dataset}: ${result.rowsRead} rows read, ${result.rowsPersisted} persisted.`);
      await reload();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : "Sync failed.");
    } finally {
      setBusy(null);
    }
  }

  if (loading) {
    return (
      <div className="space-y-4">
        {[...Array(3)].map((_, i) => <div key={i} className="h-20 rounded-xl bg-muted animate-pulse" />)}
      </div>
    );
  }

  // Map dataset name → state row
  const stateByDataset = new Map(states.map((s) => [s.dataset, s]));
  // Pick provenance from first row that has it
  const firstWithProv = states.find((s) => s.logicalVendor);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold flex items-center gap-2">
            <Database className="size-5" /> StockMarketDB Bridge
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            TSETMC market-trading data via read-only StockMarketDB SQL Server bridge.
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={reload} disabled={loading}>
          <RefreshCw className="size-4" />
        </Button>
      </div>

      {/* Transitional notice */}
      <div className="rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 flex gap-3">
        <AlertTriangle className="size-5 text-amber-500 shrink-0 mt-0.5" />
        <div className="text-sm">
          <p className="font-medium text-amber-700">Transitional Bridge Source</p>
          <p className="text-amber-600 mt-1">
            StockMarketDB is a <strong>MigrationBridge</strong> — it is maintained by a separate service
            querying TSETMC ASMX. TahlilApp-AI will migrate to a direct TSETMC feed (spec 054 Phase 2)
            once the provider client is implemented. Do not treat this source as permanent infrastructure.
          </p>
          {firstWithProv && (
            <p className="text-xs text-amber-500 mt-2">
              Provenance: vendor={firstWithProv.logicalVendor} · source={firstWithProv.physicalSource} · mode={firstWithProv.sourceMode}
            </p>
          )}
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive flex gap-2">
          <AlertCircle className="size-4 mt-0.5 shrink-0" />{error}
        </div>
      )}

      {actionError && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive flex gap-2">
          <AlertCircle className="size-4 mt-0.5 shrink-0" />{actionError}
        </div>
      )}

      {lastMsg && (
        <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 px-4 py-3 text-sm text-emerald-600 flex gap-2">
          <CheckCircle2 className="size-4 mt-0.5 shrink-0" />{lastMsg}
        </div>
      )}

      {/* Per-dataset sync state */}
      <div className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <h2 className="text-sm font-semibold">Datasets</h2>
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Dataset</TableHead>
                <TableHead>Watermark</TableHead>
                <TableHead>Last Run Started</TableHead>
                <TableHead>Last Run Completed</TableHead>
                <TableHead>Freshness</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {DATASETS.map((dataset) => {
                const row = stateByDataset.get(dataset);
                const fresh = freshnessLabel(row?.lastRunCompletedAt ?? null);
                const syncKey = `${dataset}-false`;
                const fullKey = `${dataset}-true`;
                return (
                  <TableRow key={dataset}>
                    <TableCell className="font-mono text-xs">{dataset}</TableCell>
                    <TableCell className="text-xs">{formatDt(row?.watermark ?? null)}</TableCell>
                    <TableCell className="text-xs">{formatDt(row?.lastRunStartedAt ?? null)}</TableCell>
                    <TableCell className="text-xs">{formatDt(row?.lastRunCompletedAt ?? null)}</TableCell>
                    <TableCell className={`text-xs ${fresh.cls}`}>{fresh.label}</TableCell>
                    <TableCell className="text-right">
                      <div className="flex gap-1 justify-end">
                        <Button
                          size="sm"
                          variant="outline"
                          className="h-7 px-2 text-xs gap-1"
                          disabled={busy !== null}
                          onClick={() => doSync(dataset, false)}
                        >
                          {busy === syncKey ? <RefreshCw className="size-3 animate-spin" /> : null}
                          Sync
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          className="h-7 px-2 text-xs gap-1 text-amber-600 hover:text-amber-700"
                          disabled={busy !== null}
                          onClick={() => {
                            if (window.confirm(`Full reload of ${dataset}? This re-fetches all data.`))
                              doSync(dataset, true);
                          }}
                        >
                          {busy === fullKey ? <RefreshCw className="size-3 animate-spin" /> : null}
                          Full Reload
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      </div>

      {/* Future TSETMC direct feed placeholder */}
      <div className="rounded-xl border border-dashed border-border bg-surface/30 p-4">
        <h2 className="text-sm font-semibold mb-1">TSETMC Direct Feed</h2>
        <p className="text-xs text-muted-foreground">
          Not yet operational. Phase 2 of spec 054 will add a direct TSETMC ASMX provider
          that bypasses StockMarketDB entirely. Status will appear here once the adapter is
          deployed.
        </p>
      </div>
    </div>
  );
}

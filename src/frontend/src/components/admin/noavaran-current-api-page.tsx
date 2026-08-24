import { useCallback, useEffect, useState } from "react";
import { BarChart2, AlertCircle, CheckCircle2, RefreshCw, Wifi, WifiOff } from "lucide-react";
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
  type AdminCurrentApiHealthResponse,
  type AdminNadpcoApiSyncStateItem,
  type AdminNadpcoScheduledSyncStatusResponse,
  type AdminMonthlyActivityBackfillProgressResponse,
  type AdminMonthlyActivityBackfillBatchResponse,
  type AdminFundamentalIndexCatchUpRunResponse,
  getNadpcoHealth,
  getNadpcoSyncState,
  getNadpcoScheduledSyncStatus,
  getNadpcoScheduledSyncRuns,
  getMonthlyActivityBackfillProgress,
  getMonthlyActivityBackfillBatches,
  getFundamentalIndexCatchUpRuns,
  runNadpcoScheduledSync,
  runNadpcoIncrementalSync,
  startMonthlyActivityBackfill,
  runFundamentalIndexCatchUp,
} from "@/integrations/financial-copilot/data-admin-client";

function formatDt(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("en-GB", { dateStyle: "short", timeStyle: "medium" });
}
function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Running: "bg-emerald-500/15 text-emerald-600",
    Completed: "bg-blue-500/15 text-blue-600",
    CompletedWithRetryables: "bg-amber-500/15 text-amber-600",
    CompletedWithFailures: "bg-destructive/15 text-destructive",
    Failed: "bg-destructive/15 text-destructive",
    PublishFailed: "bg-destructive/15 text-destructive",
    Queued: "bg-amber-500/15 text-amber-600",
    Publishing: "bg-amber-500/15 text-amber-600",
    InProgress: "bg-emerald-500/15 text-emerald-600",
    NothingToEnqueue: "bg-muted text-muted-foreground",
    Pending: "bg-muted text-muted-foreground",
  };
  return (
    <span
      className={`text-xs font-medium px-2 py-0.5 rounded-full ${map[status] ?? "bg-muted text-muted-foreground"}`}
    >
      {status}
    </span>
  );
}

function isActiveBackfillBatch(batch: AdminMonthlyActivityBackfillBatchResponse) {
  return (
    batch.completedAt === null &&
    ["Queued", "Publishing", "InProgress", "PublishFailed"].includes(batch.status)
  );
}

export function NadpcoCurrentApiPage() {
  const [health, setHealth] = useState<AdminCurrentApiHealthResponse | null>(null);
  const [syncState, setSyncState] = useState<AdminNadpcoApiSyncStateItem[]>([]);
  const [scheduledStatus, setScheduledStatus] =
    useState<AdminNadpcoScheduledSyncStatusResponse | null>(null);
  const [backfill, setBackfill] = useState<AdminMonthlyActivityBackfillProgressResponse | null>(
    null,
  );
  const [backfillBatches, setBackfillBatches] = useState<
    AdminMonthlyActivityBackfillBatchResponse[]
  >([]);
  const [catchUpRuns, setCatchUpRuns] = useState<AdminFundamentalIndexCatchUpRunResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [lastMsg, setLastMsg] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [h, ss, sched, bf, batches, cu] = await Promise.all([
        getNadpcoHealth(),
        getNadpcoSyncState(),
        getNadpcoScheduledSyncStatus(),
        getMonthlyActivityBackfillProgress(),
        getMonthlyActivityBackfillBatches(20),
        getFundamentalIndexCatchUpRuns(10),
      ]);
      setHealth(h);
      setSyncState(ss);
      setScheduledStatus(sched);
      setBackfill(bf);
      setBackfillBatches(batches);
      setCatchUpRuns(cu);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load Noavaran Current API state.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);

  async function doAction(key: string, fn: () => Promise<unknown>, msg: string) {
    setBusy(key);
    setActionError(null);
    setLastMsg(null);
    try {
      await fn();
      setLastMsg(msg);
      await reload();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : "Action failed.");
    } finally {
      setBusy(null);
    }
  }

  if (loading) {
    return (
      <div className="space-y-4">
        {[...Array(4)].map((_, i) => (
          <div key={i} className="h-24 rounded-xl bg-muted animate-pulse" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold flex items-center gap-2">
            <BarChart2 className="size-5" /> Noavaran Current API
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Noavaran Amin current HTTP API — recurring incremental sync from Shamsi 1403 onward.
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={reload} disabled={loading}>
          <RefreshCw className="size-4" />
        </Button>
      </div>

      {error && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive flex gap-2">
          <AlertCircle className="size-4 mt-0.5 shrink-0" />
          {error}
        </div>
      )}

      {actionError && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive flex gap-2">
          <AlertCircle className="size-4 mt-0.5 shrink-0" />
          {actionError}
        </div>
      )}

      {lastMsg && (
        <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 px-4 py-3 text-sm text-emerald-600 flex gap-2">
          <CheckCircle2 className="size-4 mt-0.5 shrink-0" />
          {lastMsg}
        </div>
      )}

      {/* Health card */}
      <div className="rounded-xl border border-border bg-surface/60 p-4">
        <div className="flex items-center justify-between mb-2">
          <h2 className="text-sm font-semibold">API Health</h2>
          {health?.isReachable ? (
            <span className="flex items-center gap-1 text-xs text-emerald-600">
              <Wifi className="size-3.5" /> Reachable{" "}
              {health.latencyMs != null ? `(${health.latencyMs} ms)` : ""}
            </span>
          ) : (
            <span className="flex items-center gap-1 text-xs text-destructive">
              <WifiOff className="size-3.5" /> Unreachable
            </span>
          )}
        </div>
        {health?.errorMessage && <p className="text-xs text-destructive">{health.errorMessage}</p>}
        <p className="text-xs text-muted-foreground">
          Checked: {formatDt(health?.lastCheckedAt ?? null)}
        </p>
      </div>

      {/* Scheduled sync section */}
      <div className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold">Scheduled Sync</h2>
          <Button
            size="sm"
            onClick={() =>
              doAction("scheduled", () => runNadpcoScheduledSync(), "Scheduled sync triggered.")
            }
            disabled={busy !== null}
            className="gap-1.5"
          >
            {busy === "scheduled" ? <RefreshCw className="size-3.5 animate-spin" /> : null}
            Run Now
          </Button>
        </div>
        {scheduledStatus?.currentRun && (
          <div className="rounded-lg bg-emerald-500/5 border border-emerald-500/20 px-3 py-2 text-xs">
            <span className="font-medium text-emerald-600">Running</span> — started{" "}
            {formatDt(scheduledStatus.currentRun.startedAt)},{" "}
            {scheduledStatus.currentRun.processedCompanies} companies processed
          </div>
        )}
        {scheduledStatus?.nextScheduledAt && (
          <p className="text-xs text-muted-foreground">
            Next run: {formatDt(scheduledStatus.nextScheduledAt)}
          </p>
        )}
        {scheduledStatus && scheduledStatus.recentRuns.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Status</TableHead>
                  <TableHead>Started</TableHead>
                  <TableHead>Completed</TableHead>
                  <TableHead>Companies</TableHead>
                  <TableHead>Errors</TableHead>
                  <TableHead>Trigger</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {scheduledStatus.recentRuns.map((run) => (
                  <TableRow key={run.runId}>
                    <TableCell>
                      <StatusBadge status={run.status} />
                    </TableCell>
                    <TableCell className="text-xs">{formatDt(run.startedAt)}</TableCell>
                    <TableCell className="text-xs">{formatDt(run.completedAt)}</TableCell>
                    <TableCell className="text-xs">{run.processedCompanies}</TableCell>
                    <TableCell className="text-xs">
                      {run.errorCount > 0 ? (
                        <span className="text-destructive">{run.errorCount}</span>
                      ) : (
                        0
                      )}
                    </TableCell>
                    <TableCell className="text-xs text-muted-foreground">
                      {run.triggerSource}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>

      {/* Sync state watermarks */}
      {syncState.length > 0 && (
        <div className="rounded-xl border border-border bg-surface/60 p-4">
          <h2 className="text-sm font-semibold mb-3">Dataset Watermarks</h2>
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Dataset</TableHead>
                  <TableHead>Watermark</TableHead>
                  <TableHead>Last Run Started</TableHead>
                  <TableHead>Last Run Completed</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {syncState.map((s) => (
                  <TableRow key={s.dataset}>
                    <TableCell className="font-mono text-xs">{s.dataset}</TableCell>
                    <TableCell className="text-xs">{formatDt(s.watermark)}</TableCell>
                    <TableCell className="text-xs">{formatDt(s.lastRunStartedAt)}</TableCell>
                    <TableCell className="text-xs">{formatDt(s.lastRunCompletedAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <div className="mt-3 flex gap-2">
            <Button
              size="sm"
              variant="outline"
              onClick={() =>
                doAction(
                  "incremental",
                  () => runNadpcoIncrementalSync(),
                  "Incremental sync triggered.",
                )
              }
              disabled={busy !== null}
              className="gap-1.5"
            >
              {busy === "incremental" ? <RefreshCw className="size-3.5 animate-spin" /> : null}
              Incremental Sync
            </Button>
          </div>
        </div>
      )}

      {/* Monthly activity backfill */}
      <div className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-sm font-semibold">Monthly Activity Backfill</h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Walks Shamsi months newest-first. Resumes on interruption.
            </p>
          </div>
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              doAction(
                "backfill",
                () => startMonthlyActivityBackfill(),
                "Monthly backfill started.",
              )
            }
            disabled={busy !== null || backfillBatches.some(isActiveBackfillBatch)}
            className="gap-1.5"
          >
            {busy === "backfill" ? <RefreshCw className="size-3.5 animate-spin" /> : null}
            {backfillBatches.some(isActiveBackfillBatch) ? "Running..." : "Start Backfill"}
          </Button>
        </div>
        {backfill && (
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            {[
              {
                label: "Completed",
                value: backfill.months.filter((month) => month.status === "Completed").length,
                color: "text-emerald-600",
              },
              {
                label: "Pending",
                value: backfill.months.filter((month) => month.status === "Pending").length,
                color: "text-amber-600",
              },
              {
                label: "Failed",
                value: backfill.months.reduce((sum, month) => sum + month.companiesFailed, 0),
                color: "text-destructive",
              },
              {
                label: "Retryable",
                value: backfill.months.reduce((sum, month) => sum + month.companiesNoDataYet, 0),
                color: "text-foreground",
              },
            ].map(({ label, value, color }) => (
              <div key={label} className="text-center">
                <div className={`text-xl font-bold ${color}`}>{value}</div>
                <div className="text-xs text-muted-foreground">{label}</div>
              </div>
            ))}
          </div>
        )}
        {backfill?.isCompleted && (
          <div className="text-xs text-emerald-600 flex gap-1">
            <CheckCircle2 className="size-3.5 mt-0.5" /> Backfill complete.
          </div>
        )}
        {backfillBatches.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Batch</TableHead>
                  <TableHead>Scope</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Published</TableHead>
                  <TableHead>Processed</TableHead>
                  <TableHead>Failed</TableHead>
                  <TableHead>Retryable</TableHead>
                  <TableHead>Created</TableHead>
                  <TableHead>Last error</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {backfillBatches.map((batch) => (
                  <TableRow key={batch.batchId}>
                    <TableCell className="font-mono text-xs" title={batch.batchId}>
                      {batch.batchId.slice(0, 8)}
                    </TableCell>
                    <TableCell>
                      {batch.targetShamsiYear && batch.targetShamsiMonth
                        ? `${batch.targetShamsiYear}/${String(batch.targetShamsiMonth).padStart(2, "0")}`
                        : "Full"}
                    </TableCell>
                    <TableCell>
                      <StatusBadge status={batch.status} />
                    </TableCell>
                    <TableCell>
                      {batch.publishedCount}/{batch.plannedCount}
                    </TableCell>
                    <TableCell>
                      {batch.processedCount}/{batch.plannedCount}
                    </TableCell>
                    <TableCell className={batch.failedCount > 0 ? "text-destructive" : undefined}>
                      {batch.failedCount}
                    </TableCell>
                    <TableCell className={batch.retryableCount > 0 ? "text-amber-600" : undefined}>
                      {batch.retryableCount}
                    </TableCell>
                    <TableCell>{formatDt(batch.createdAt)}</TableCell>
                    <TableCell className="max-w-64 truncate" title={batch.lastError ?? undefined}>
                      {batch.lastError ?? "—"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>

      {/* Fundamental index catch-up */}
      <div className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <div className="flex items-center justify-between">
          <div>
            <h2 className="text-sm font-semibold">Fundamental Index Catch-up</h2>
            <p className="text-xs text-muted-foreground mt-0.5">
              Sweeps all vendor indexes for all companies across years 1403–1405.
            </p>
          </div>
          <Button
            size="sm"
            variant="outline"
            onClick={() =>
              doAction(
                "catchup",
                () => runFundamentalIndexCatchUp(1403, 1405),
                "Fundamental index catch-up started.",
              )
            }
            disabled={busy !== null}
            className="gap-1.5"
          >
            {busy === "catchup" ? <RefreshCw className="size-3.5 animate-spin" /> : null}
            Run Catch-up
          </Button>
        </div>
        {catchUpRuns.length > 0 && (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Status</TableHead>
                  <TableHead>Started</TableHead>
                  <TableHead>Years</TableHead>
                  <TableHead>Companies</TableHead>
                  <TableHead>Errors</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {catchUpRuns.map((run) => (
                  <TableRow key={run.runId}>
                    <TableCell>
                      <StatusBadge status={run.status} />
                    </TableCell>
                    <TableCell className="text-xs">{formatDt(run.startedAt)}</TableCell>
                    <TableCell className="text-xs">
                      {run.fromShamsiYear}–{run.toShamsiYear}
                    </TableCell>
                    <TableCell className="text-xs">{run.processedCompanies}</TableCell>
                    <TableCell className="text-xs">
                      {run.errorCount > 0 ? (
                        <span className="text-destructive">{run.errorCount}</span>
                      ) : (
                        0
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>
    </div>
  );
}

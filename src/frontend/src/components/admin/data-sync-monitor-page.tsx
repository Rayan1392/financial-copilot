import { useState } from "react";
import { RefreshCw, Radio, Activity, AlertCircle, Clock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import type { DataSyncActivityItem } from "@/integrations/financial-copilot/data-admin-client";
import {
  useDataSyncActivityStream,
  type StreamStatus,
} from "@/hooks/use-data-sync-activity-stream";

// --------------------------------------------------------------------------
// Main page
// --------------------------------------------------------------------------

export function DataSyncMonitorPage() {
  const { status, snapshot, lastUpdatedAt, error, refresh } = useDataSyncActivityStream();
  const [filter, setFilter] = useState("");

  const filterItem = (item: DataSyncActivityItem) => {
    if (!filter) return true;
    const q = filter.toLowerCase();
    return (
      item.provider.toLowerCase().includes(q) ||
      item.dataset.toLowerCase().includes(q) ||
      item.status.toLowerCase().includes(q) ||
      (item.requestedShamsiMonth ?? "").includes(q)
    );
  };

  const activeRuns = (snapshot?.activeRuns ?? []).filter(filterItem);
  const recentRuns = (snapshot?.recentRuns ?? []).filter(filterItem);

  return (
    <div className="space-y-6">
      {/* Header strip */}
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div className="flex items-center gap-3">
          <h1 className="text-lg font-semibold">Data Sync Monitor</h1>
          <StreamStatusBadge status={status} />
        </div>
        <div className="flex items-center gap-2">
          {lastUpdatedAt && (
            <span className="text-xs text-muted-foreground flex items-center gap-1">
              <Clock className="size-3" />
              {lastUpdatedAt.toLocaleTimeString()}
            </span>
          )}
          <Button variant="outline" size="sm" onClick={refresh}>
            <RefreshCw className="size-3.5 mr-1.5" />
            Refresh
          </Button>
        </div>
      </div>

      {/* Summary strip */}
      <SummaryStrip
        activeCount={snapshot?.activeRuns.length ?? 0}
        recentCount={snapshot?.recentRuns.length ?? 0}
        errorCount={(snapshot?.activeRuns ?? []).filter((r) => r.errorCount > 0).length}
        status={status}
      />

      {/* Error banner */}
      {error && (
        <div className="flex items-center gap-2 rounded border border-destructive/30 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          <AlertCircle className="size-4 shrink-0" />
          {error}
        </div>
      )}

      {/* Filter */}
      <div>
        <input
          type="text"
          placeholder="Filter by provider, dataset, status, or month…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="w-full max-w-sm rounded-md border border-border bg-background px-3 py-1.5 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-ring"
        />
      </div>

      {status === "connecting" && !snapshot && (
        <LoadingDots />
      )}

      {/* Active runs */}
      {(status !== "connecting" || snapshot) && (
        <section className="rounded-xl border border-border bg-surface">
          <div className="px-4 pt-4 pb-2 flex items-center gap-2">
            <Activity className="size-4 text-emerald" />
            <h2 className="font-semibold text-sm">Active Runs</h2>
            <span className="ml-auto text-xs text-muted-foreground">{activeRuns.length} item{activeRuns.length !== 1 ? "s" : ""}</span>
          </div>
          {activeRuns.length === 0 ? (
            <p className="px-4 pb-4 text-sm text-muted-foreground">No active runs.</p>
          ) : (
            <ActivityTable items={activeRuns} />
          )}
        </section>
      )}

      {/* Recent runs */}
      {(status !== "connecting" || snapshot) && (
        <section className="rounded-xl border border-border bg-surface">
          <div className="px-4 pt-4 pb-2 flex items-center gap-2">
            <Clock className="size-4 text-muted-foreground" />
            <h2 className="font-semibold text-sm">Recent Runs</h2>
            <span className="ml-auto text-xs text-muted-foreground">{recentRuns.length} item{recentRuns.length !== 1 ? "s" : ""}</span>
          </div>
          {recentRuns.length === 0 ? (
            <p className="px-4 pb-4 text-sm text-muted-foreground">No recent runs.</p>
          ) : (
            <ActivityTable items={recentRuns} />
          )}
        </section>
      )}
    </div>
  );
}

// --------------------------------------------------------------------------
// Summary strip
// --------------------------------------------------------------------------

function SummaryStrip({
  activeCount,
  recentCount,
  errorCount,
  status,
}: {
  activeCount: number;
  recentCount: number;
  errorCount: number;
  status: StreamStatus;
}) {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
      <StatCard label="Active" value={activeCount} color="text-emerald" />
      <StatCard label="Recent" value={recentCount} color="text-foreground" />
      <StatCard label="With Errors" value={errorCount} color="text-destructive" />
      <StatCard
        label="Stream"
        value={status === "live" ? "Live" : status === "polling" ? "Polling" : status}
        color={status === "live" ? "text-emerald" : "text-muted-foreground"}
      />
    </div>
  );
}

function StatCard({
  label,
  value,
  color,
}: {
  label: string;
  value: string | number;
  color: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-surface px-4 py-3">
      <p className="text-xs text-muted-foreground mb-1">{label}</p>
      <p className={`text-xl font-bold tabular-nums ${color}`}>{value}</p>
    </div>
  );
}

// --------------------------------------------------------------------------
// Activity table
// --------------------------------------------------------------------------

function ActivityTable({ items }: { items: DataSyncActivityItem[] }) {
  const [selected, setSelected] = useState<DataSyncActivityItem | null>(null);

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Provider</TableHead>
            <TableHead>Dataset</TableHead>
            <TableHead>Status</TableHead>
            <TableHead>Started</TableHead>
            <TableHead className="text-right">Records</TableHead>
            <TableHead className="text-right">Errors</TableHead>
            <TableHead>Duration</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((item) => (
            <TableRow
              key={item.runId}
              className="cursor-pointer hover:bg-muted/30"
              onClick={() => setSelected((prev) => (prev?.runId === item.runId ? null : item))}
            >
              <TableCell className="font-medium text-sm">{item.provider}</TableCell>
              <TableCell className="text-sm">{item.dataset}</TableCell>
              <TableCell>
                <RunStatusBadge status={item.status} />
              </TableCell>
              <TableCell className="text-xs text-muted-foreground">
                {item.startedAt ? formatTime(item.startedAt) : "—"}
              </TableCell>
              <TableCell className="text-right text-sm tabular-nums">{item.processedRecords}</TableCell>
              <TableCell className="text-right text-sm tabular-nums">
                {item.errorCount > 0 ? (
                  <span className="text-destructive font-medium">{item.errorCount}</span>
                ) : (
                  item.errorCount
                )}
              </TableCell>
              <TableCell className="text-xs text-muted-foreground">
                {item.durationMs != null ? formatDuration(item.durationMs) : "—"}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {selected && (
        <div className="mx-4 mb-4 mt-2 rounded-lg border border-border bg-muted/20 p-4 text-sm space-y-2">
          <div className="flex items-center justify-between">
            <span className="font-semibold text-xs uppercase tracking-wide text-muted-foreground">
              Run Detail
            </span>
            <button
              className="text-xs text-muted-foreground hover:text-foreground"
              onClick={() => setSelected(null)}
            >
              Close
            </button>
          </div>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
            <DetailRow label="Run ID" value={selected.runId} />
            <DetailRow label="Trigger" value={selected.triggerSource} />
            <DetailRow label="Shamsi Month" value={selected.requestedShamsiMonth ?? "—"} />
            <DetailRow label="Logical Vendor" value={selected.logicalVendor ?? "—"} />
            <DetailRow label="Physical Source" value={selected.physicalSource ?? "—"} />
            <DetailRow label="Source Mode" value={selected.sourceMode ?? "—"} />
            {selected.errorMessage && (
              <div className="col-span-2">
                <dt className="text-muted-foreground">Error</dt>
                <dd className="text-destructive break-all">{selected.errorMessage}</dd>
              </div>
            )}
          </dl>
        </div>
      )}
    </>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-mono break-all">{value}</dd>
    </>
  );
}

// --------------------------------------------------------------------------
// Stream status badge
// --------------------------------------------------------------------------

function StreamStatusBadge({ status }: { status: StreamStatus }) {
  const variants: Record<StreamStatus, { label: string; className: string }> = {
    connecting: { label: "Connecting…", className: "bg-muted text-muted-foreground" },
    live: { label: "Live", className: "bg-emerald/10 text-emerald border-emerald/20" },
    reconnecting: { label: "Reconnecting…", className: "bg-amber-500/10 text-amber-600 border-amber-500/20" },
    polling: { label: "Polling", className: "bg-blue-500/10 text-blue-600 border-blue-500/20" },
    error: { label: "Error", className: "bg-destructive/10 text-destructive border-destructive/20" },
  };
  const v = variants[status];
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium ${v.className}`}
    >
      {status === "live" && <Radio className="size-2.5 animate-pulse" />}
      {v.label}
    </span>
  );
}

// --------------------------------------------------------------------------
// Run status badge
// --------------------------------------------------------------------------

function RunStatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Running: "bg-emerald/10 text-emerald border-emerald/20",
    Queued: "bg-blue-500/10 text-blue-600 border-blue-500/20",
    Completed: "bg-muted text-muted-foreground",
    Succeeded: "bg-muted text-muted-foreground",
    Failed: "bg-destructive/10 text-destructive border-destructive/20",
    Cancelled: "bg-muted text-muted-foreground",
  };
  const cls = map[status] ?? "bg-muted text-muted-foreground";
  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${cls}`}>
      {status}
    </span>
  );
}

// --------------------------------------------------------------------------
// Loading indicator
// --------------------------------------------------------------------------

function LoadingDots() {
  return (
    <div className="flex gap-1.5 py-6 justify-center">
      <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:0ms]" />
      <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:150ms]" />
      <div className="size-2 rounded-full bg-emerald animate-bounce [animation-delay:300ms]" />
    </div>
  );
}

// --------------------------------------------------------------------------
// Formatting helpers
// --------------------------------------------------------------------------

function formatTime(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return iso;
  }
}

function formatDuration(ms: number): string {
  if (ms < 1_000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1_000).toFixed(1)}s`;
  const m = Math.floor(ms / 60_000);
  const s = Math.round((ms % 60_000) / 1_000);
  return `${m}m ${s}s`;
}

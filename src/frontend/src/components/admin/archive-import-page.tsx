import { useCallback, useEffect, useState } from "react";
import { Archive, AlertCircle, CheckCircle2, RefreshCw, Snowflake } from "lucide-react";
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
  type AdminArchiveFreezeStateResponse,
  type AdminArchiveImportRunResponse,
  type AdminArchiveImportValidationResponse,
  getArchiveFreezeState,
  getArchiveRuns,
  getArchiveCoverage,
  runArchiveDryRun,
  runArchiveImport,
  runArchiveReImport,
  runArchiveValidate,
  runArchiveFreeze,
} from "@/integrations/financial-copilot/data-admin-client";

function formatDt(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("en-GB", { dateStyle: "short", timeStyle: "medium" });
}

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Running: "bg-emerald-500/15 text-emerald-600",
    Completed: "bg-blue-500/15 text-blue-600",
    Failed: "bg-destructive/15 text-destructive",
    DryRun: "bg-amber-500/15 text-amber-600",
    Pending: "bg-muted text-muted-foreground",
  };
  return (
    <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${map[status] ?? "bg-muted text-muted-foreground"}`}>
      {status}
    </span>
  );
}

type ActionState = "idle" | "busy" | "done" | "error";

function ActionButton({
  label,
  icon: Icon,
  variant = "outline",
  onClick,
  busy,
  destructive,
}: {
  label: string;
  icon?: React.ComponentType<{ className?: string }>;
  variant?: "outline" | "default" | "destructive";
  onClick: () => void;
  busy: boolean;
  destructive?: boolean;
}) {
  return (
    <Button
      variant={destructive ? "destructive" : variant}
      size="sm"
      onClick={onClick}
      disabled={busy}
      className="gap-1.5"
    >
      {busy ? <RefreshCw className="size-3.5 animate-spin" /> : Icon ? <Icon className="size-3.5" /> : null}
      {label}
    </Button>
  );
}

export function ArchiveImportPage() {
  const [freezeState, setFreezeState] = useState<AdminArchiveFreezeStateResponse | null>(null);
  const [runs, setRuns] = useState<AdminArchiveImportRunResponse[]>([]);
  const [coverage, setCoverage] = useState<AdminArchiveImportValidationResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionState, setActionState] = useState<ActionState>("idle");
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastResult, setLastResult] = useState<AdminArchiveImportRunResponse | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [freeze, runsData, cov] = await Promise.all([
        getArchiveFreezeState(),
        getArchiveRuns(20),
        getArchiveCoverage(),
      ]);
      setFreezeState(freeze);
      setRuns(runsData);
      setCoverage(cov);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load archive state.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { reload(); }, [reload]);

  const doAction = async (
    fn: () => Promise<AdminArchiveImportRunResponse>,
    confirmMsg?: string,
  ) => {
    if (confirmMsg && !window.confirm(confirmMsg)) return;
    setActionState("busy");
    setActionError(null);
    try {
      const result = await fn();
      setLastResult(result);
      setActionState("done");
      await reload();
    } catch (e) {
      setActionError(e instanceof Error ? e.message : "Action failed.");
      setActionState("error");
    }
  };

  const busy = actionState === "busy";

  if (loading) {
    return (
      <div className="space-y-4">
        {[...Array(3)].map((_, i) => (
          <div key={i} className="h-20 rounded-xl bg-muted animate-pulse" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold flex items-center gap-2">
            <Archive className="size-5" /> Archive Import
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Noavaran Amin frozen archive (CodalDB SQL). One-time import — not a recurring sync.
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

      {/* Freeze state card */}
      <div className="rounded-xl border border-border bg-surface/60 p-4">
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-semibold flex items-center gap-2">
            <Snowflake className="size-4 text-blue-500" /> Freeze State
          </h2>
          {freezeState?.isFrozen ? (
            <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-blue-500/15 text-blue-600">Frozen</span>
          ) : (
            <span className="text-xs font-medium px-2 py-0.5 rounded-full bg-muted text-muted-foreground">Not Frozen</span>
          )}
        </div>
        {freezeState?.isFrozen ? (
          <p className="text-xs text-muted-foreground">
            Frozen {formatDt(freezeState.frozenAt)}{freezeState.reason ? ` — ${freezeState.reason}` : ""}
          </p>
        ) : (
          <p className="text-xs text-muted-foreground">Archive is not frozen. Import operations are allowed.</p>
        )}
      </div>

      {/* Actions */}
      <div className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <h2 className="text-sm font-semibold">Actions</h2>
        <p className="text-xs text-muted-foreground">
          Run order: Dry-run → Import → Validate → Freeze. Re-import resets and re-runs from scratch.
        </p>
        <div className="flex flex-wrap gap-2">
          <ActionButton label="Dry-run" onClick={() => doAction(() => runArchiveDryRun())} busy={busy} />
          <ActionButton
            label="Import"
            variant="default"
            onClick={() => doAction(() => runArchiveImport(), "Start a full archive import? This may take several minutes.")}
            busy={busy}
          />
          <ActionButton label="Validate" onClick={() => doAction(() => runArchiveValidate())} busy={busy} />
          <ActionButton
            label="Freeze"
            onClick={() => doAction(() => runArchiveFreeze(), "Freeze the archive? This marks it as read-only.")}
            busy={busy}
          />
          <ActionButton
            label="Re-import"
            destructive
            onClick={() => doAction(() => runArchiveReImport(), "Re-import resets all archive data. Are you sure?")}
            busy={busy}
          />
        </div>
        {actionState === "error" && actionError && (
          <p className="text-xs text-destructive flex gap-1"><AlertCircle className="size-3.5 mt-0.5" />{actionError}</p>
        )}
        {actionState === "done" && lastResult && (
          <p className="text-xs text-emerald-600 flex gap-1">
            <CheckCircle2 className="size-3.5 mt-0.5" />
            {lastResult.action} started — run {lastResult.runId.slice(0, 8)}… status: {lastResult.status}
          </p>
        )}
      </div>

      {/* Coverage summary */}
      {coverage && (
        <div className="rounded-xl border border-border bg-surface/60 p-4">
          <h2 className="text-sm font-semibold mb-3">Coverage</h2>
          <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
            <div className="text-center">
              <div className="text-2xl font-bold">{coverage.symbolCount.toLocaleString()}</div>
              <div className="text-xs text-muted-foreground">Symbols</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-bold">{coverage.statementCount.toLocaleString()}</div>
              <div className="text-xs text-muted-foreground">Statements</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-bold">{coverage.missingSymbols.length}</div>
              <div className="text-xs text-muted-foreground">Missing Symbols</div>
            </div>
            <div className="text-center">
              <div className="text-2xl font-bold">{coverage.issues.length}</div>
              <div className="text-xs text-muted-foreground">Issues</div>
            </div>
          </div>
          {coverage.issues.length > 0 && (
            <div className="mt-3 space-y-1">
              {coverage.issues.slice(0, 5).map((issue, i) => (
                <p key={i} className="text-xs text-destructive flex gap-1">
                  <AlertCircle className="size-3.5 mt-0.5 shrink-0" />{issue}
                </p>
              ))}
              {coverage.issues.length > 5 && (
                <p className="text-xs text-muted-foreground">…and {coverage.issues.length - 5} more</p>
              )}
            </div>
          )}
        </div>
      )}

      {/* Run history */}
      <div className="rounded-xl border border-border bg-surface/60 p-4">
        <h2 className="text-sm font-semibold mb-3">Run History</h2>
        {runs.length === 0 ? (
          <p className="text-sm text-muted-foreground">No archive runs found.</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Action</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Started</TableHead>
                  <TableHead>Completed</TableHead>
                  <TableHead>Symbols</TableHead>
                  <TableHead>Statements</TableHead>
                  <TableHead>Reason</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {runs.map((run) => (
                  <TableRow key={run.runId}>
                    <TableCell className="font-mono text-xs">{run.action}</TableCell>
                    <TableCell><StatusBadge status={run.status} /></TableCell>
                    <TableCell className="text-xs">{formatDt(run.startedAt)}</TableCell>
                    <TableCell className="text-xs">{formatDt(run.completedAt)}</TableCell>
                    <TableCell className="text-xs">{run.symbolsProcessed}</TableCell>
                    <TableCell className="text-xs">{run.statementsProcessed}</TableCell>
                    <TableCell className="text-xs text-muted-foreground">{run.reason ?? "—"}</TableCell>
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

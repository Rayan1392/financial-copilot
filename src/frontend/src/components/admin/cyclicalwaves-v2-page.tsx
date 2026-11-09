import { useCallback, useEffect, useState } from "react";
import { AlertCircle, CheckCircle2, RefreshCw, Waves } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import {
  type CyclicalWavesV2AnalysisMode,
  type CyclicalWavesV2InlineRunResponse,
  type CyclicalWavesV2QueuedRunResponse,
  type CyclicalWavesV2RunCounts,
  type CyclicalWavesV2StatusResponse,
  getCyclicalWavesV2Status,
  queueCyclicalWavesV2AnalysisSync,
  queueCyclicalWavesV2FullSync,
  queueCyclicalWavesV2GaugeCatalogSync,
  queueCyclicalWavesV2GaugeDetailsSync,
  queueCyclicalWavesV2IncomeStatementsSync,
  queueCyclicalWavesV2EquilibriumCatalogSync,
  queueCyclicalWavesV2EquilibriumRangesSync,
  queueCyclicalWavesV2ScreenerSync,
  runCyclicalWavesV2CatalogSync,
  runCyclicalWavesV2TickerSync,
  runCyclicalWavesV2TickerGaugeEquilibriumSync,
} from "@/integrations/financial-copilot/data-admin-client";

const POLL_INTERVAL_MS = 5_000;
const RECENT_RUN_LIMIT = 10;

type ActionKey = "catalog" | "ticker" | "screener" | "income-statements" | "analysis" | "full" | "gauge-catalog" | "gauges" | "equilibrium-catalog" | "equilibrium-ranges" | "ticker-gauge";

function formatDt(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("en-GB", {
    dateStyle: "short",
    timeStyle: "medium",
    timeZone: "UTC",
  });
}

function formatCounts(counts: CyclicalWavesV2RunCounts) {
  return `کل ${counts.total} · پردازش ${counts.processed} · به‌روز ${counts.updated} · بدون تغییر ${counts.unchanged} · خطا ${counts.failed}`;
}

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Requested: "bg-amber-500/15 text-amber-600",
    Running: "bg-emerald-500/15 text-emerald-600",
    Succeeded: "bg-blue-500/15 text-blue-600",
    CompletedWithErrors: "bg-amber-500/15 text-amber-600",
    Failed: "bg-destructive/15 text-destructive",
    SkippedAlreadyRunning: "bg-muted text-muted-foreground",
  };
  return (
    <span
      className={`text-xs font-medium px-2 py-0.5 rounded-full ${map[status] ?? "bg-muted text-muted-foreground"}`}
    >
      {status}
    </span>
  );
}

function YesNo({ value }: { value: boolean }) {
  return (
    <span className={value ? "text-emerald-600" : "text-destructive"}>{value ? "بله" : "خیر"}</span>
  );
}

/**
 * Maps API failures to an operator-facing message. The shared API helper keeps only problem
 * `detail`/`title`/`type`, so a 409 without a problem `type` is the backend's
 * `{ code: "SyncAlreadyRunning" | "SyncAlreadyActive", existingRunId }` conflict body.
 */
export function describeCyclicalWavesV2Error(error: unknown): string {
  if (error instanceof FinancialCopilotApiError) {
    if (error.status === 409 && error.type === "IngestionDisabled")
      return `دریافت داده CyclicalWaves v2 غیرفعال است. (${error.message})`;
    if (error.status === 409)
      return "یک همگام‌سازی CyclicalWaves v2 هم‌اکنون فعال است؛ تا پایان آن صبر کنید.";
    return `${error.message} (HTTP ${error.status})`;
  }
  return error instanceof Error ? error.message : "درخواست ناموفق بود.";
}

export function CyclicalWavesV2Page() {
  const [status, setStatus] = useState<CyclicalWavesV2StatusResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<ActionKey | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [lastMsg, setLastMsg] = useState<string | null>(null);
  const [ticker, setTicker] = useState("");
  const [analysisMode, setAnalysisMode] = useState<CyclicalWavesV2AnalysisMode>("incremental");

  const reload = useCallback(async () => {
    setError(null);
    try {
      setStatus(await getCyclicalWavesV2Status(RECENT_RUN_LIMIT));
    } catch (e) {
      setError(describeCyclicalWavesV2Error(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const hasActiveRun = status?.activeRun != null;

  useEffect(() => {
    if (!hasActiveRun) return;
    const timer = window.setInterval(() => void reload(), POLL_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [hasActiveRun, reload]);

  async function runAction(
    key: ActionKey,
    label: string,
    call: () => Promise<CyclicalWavesV2InlineRunResponse | CyclicalWavesV2QueuedRunResponse>,
  ) {
    setBusy(key);
    setActionError(null);
    setLastMsg(null);
    try {
      const result = await call();
      if ("counts" in result) {
        setLastMsg(
          `${label}: ${result.status} — ${formatCounts(result.counts)}${result.errorSummary ? ` — ${result.errorSummary}` : ""}`,
        );
      } else {
        setLastMsg(`${label}: در صف Worker قرار گرفت (اجرا ${result.runId}).`);
      }
    } catch (e) {
      setActionError(`${label}: ${describeCyclicalWavesV2Error(e)}`);
    } finally {
      setBusy(null);
      await reload();
    }
  }

  const actionsDisabled = busy !== null || hasActiveRun;
  const trimmedTicker = ticker.trim();

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
    <div dir="rtl" className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold flex items-center gap-2">
            <Waves className="size-5" /> CyclicalWaves v2
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            ارائه‌دهنده مستقل داده‌های مالی؛ کاتالوگ و تک‌نماد به‌صورت درجا اجرا می‌شوند و سایر
            همگام‌سازی‌ها در صف Worker قرار می‌گیرند.
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={() => void reload()} aria-label="بازخوانی وضعیت">
          <RefreshCw className="size-4" />
        </Button>
      </div>

      {error && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm text-destructive flex gap-2">
          <AlertCircle className="size-4 mt-0.5 shrink-0" />
          {error}
        </div>
      )}

      {status && (
        <section
          aria-label="وضعیت CyclicalWaves v2"
          className="rounded-xl border border-border bg-surface/60 p-4 grid grid-cols-2 md:grid-cols-3 gap-3 text-sm"
        >
          <div>
            <div className="text-xs text-muted-foreground">دریافت داده فعال</div>
            <YesNo value={status.ingestionEnabled} />
          </div>
          <div>
            <div className="text-xs text-muted-foreground">همگام‌سازی روزانه فعال</div>
            <YesNo value={status.dailySyncEnabled} />
          </div>
          <div>
            <div className="text-xs text-muted-foreground">زمان‌بندی (Cron، UTC)</div>
            <span className="font-mono text-xs" dir="ltr">
              {status.dailySyncCronUtc}
            </span>
          </div>
          <div>
            <div className="text-xs text-muted-foreground">اجرای بعدی (UTC)</div>
            <span className="text-xs" dir="ltr">
              {formatDt(status.nextScheduledRunUtc)}
            </span>
          </div>
          <div>
            <div className="text-xs text-muted-foreground">اعتبارنامه پیکربندی شده</div>
            <YesNo value={status.credentialsConfigured} />
          </div>
          <div>
            <div className="text-xs text-muted-foreground">ارائه‌دهنده گفتگو</div>
            <span className="font-mono text-xs">{status.chatProvider}</span>
          </div>
          {status.lastSuccessByScope.length > 0 && (
            <div className="col-span-2 md:col-span-3 text-xs text-muted-foreground">
              آخرین موفقیت:{" "}
              {status.lastSuccessByScope
                .map((item) => `${item.scope} ${formatDt(item.completedAt)}`)
                .join(" · ")}
            </div>
          )}
        </section>
      )}

      {status?.activeRun && (
        <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 px-4 py-3 text-sm text-amber-700 flex items-center gap-2">
          <RefreshCw className="size-4 animate-spin shrink-0" />
          اجرای فعال: {status.activeRun.scope}
          {status.activeRun.ticker ? ` (${status.activeRun.ticker})` : ""} —{" "}
          <StatusBadge status={status.activeRun.status} /> — {formatCounts(status.activeRun.counts)}
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

      <section className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <h2 className="text-sm font-semibold">عملیات همگام‌سازی</h2>
        <div className="flex flex-wrap items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={actionsDisabled}
            onClick={() => void runAction("catalog", "کاتالوگ نمادها", runCyclicalWavesV2CatalogSync)}
          >
            {busy === "catalog" && <RefreshCw className="size-3 animate-spin" />}
            کاتالوگ نمادها
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("screener", "اسکرینر مالی", queueCyclicalWavesV2ScreenerSync)
            }
          >
            {busy === "screener" && <RefreshCw className="size-3 animate-spin" />}
            اسکرینر مالی
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction(
                "income-statements",
                "صورت‌های سود و زیان",
                queueCyclicalWavesV2IncomeStatementsSync,
              )
            }
          >
            {busy === "income-statements" && <RefreshCw className="size-3 animate-spin" />}
            صورت‌های سود و زیان
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <input
            value={ticker}
            onChange={(event) => setTicker(event.target.value)}
            placeholder="نماد (مثلاً فولاد)"
            aria-label="نماد"
            className="rounded border border-border bg-background px-2 py-1 text-xs"
          />
          <Button
            size="sm"
            variant="outline"
            disabled={actionsDisabled || trimmedTicker === ""}
            onClick={() =>
              void runAction("ticker", `همگام‌سازی ${trimmedTicker}`, () =>
                runCyclicalWavesV2TickerSync(trimmedTicker),
              )
            }
          >
            {busy === "ticker" && <RefreshCw className="size-3 animate-spin" />}
            همگام‌سازی یک نماد
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" disabled={actionsDisabled} onClick={() => void runAction("gauge-catalog", "کاتالوگ گیج‌ها", queueCyclicalWavesV2GaugeCatalogSync)}>کاتالوگ گیج‌ها</Button>
          <Button size="sm" variant="outline" disabled={actionsDisabled} onClick={() => void runAction("gauges", "جزئیات گیج‌ها", queueCyclicalWavesV2GaugeDetailsSync)}>جزئیات گیج‌ها</Button>
          <Button size="sm" variant="outline" disabled={actionsDisabled} onClick={() => void runAction("equilibrium-catalog", "کاتالوگ قیمت تعادلی", queueCyclicalWavesV2EquilibriumCatalogSync)}>کاتالوگ قیمت تعادلی</Button>
          <Button size="sm" variant="outline" disabled={actionsDisabled} onClick={() => void runAction("equilibrium-ranges", "بازه‌های قیمت تعادلی", queueCyclicalWavesV2EquilibriumRangesSync)}>بازه‌های قیمت تعادلی</Button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" variant="outline" disabled={actionsDisabled || trimmedTicker === ""}
            onClick={() => void runAction("ticker-gauge", `گیج و قیمت تعادلی ${trimmedTicker}`, () => runCyclicalWavesV2TickerGaugeEquilibriumSync(trimmedTicker))}>
            {busy === "ticker-gauge" && <RefreshCw className="size-3 animate-spin" />}
            گیج و قیمت تعادلی یک نماد
          </Button>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <select
            value={analysisMode}
            onChange={(event) => setAnalysisMode(event.target.value as CyclicalWavesV2AnalysisMode)}
            aria-label="حالت تحلیل جامع"
            className="rounded border border-border bg-background px-2 py-1 text-xs"
          >
            <option value="incremental">افزایشی (incremental)</option>
            <option value="full">کامل (full)</option>
          </select>
          <Button
            size="sm"
            variant="outline"
            disabled={actionsDisabled}
            onClick={() =>
              void runAction("analysis", `تحلیل جامع (${analysisMode})`, () =>
                queueCyclicalWavesV2AnalysisSync(analysisMode),
              )
            }
          >
            {busy === "analysis" && <RefreshCw className="size-3 animate-spin" />}
            تحلیل جامع
          </Button>
        </div>

        <div>
          <Button
            size="sm"
            variant="outline"
            className="text-amber-600 hover:text-amber-700"
            disabled={actionsDisabled}
            onClick={() => {
              if (window.confirm("همگام‌سازی کامل CyclicalWaves v2 در صف قرار گیرد؟"))
                void runAction("full", "همگام‌سازی کامل", queueCyclicalWavesV2FullSync);
            }}
          >
            {busy === "full" && <RefreshCw className="size-3 animate-spin" />}
            همگام‌سازی کامل
          </Button>
        </div>
      </section>

      <section className="rounded-xl border border-border bg-surface/60 p-4 space-y-3">
        <h2 className="text-sm font-semibold">اجراهای اخیر</h2>
        <div className="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="text-right">دامنه</TableHead>
                <TableHead className="text-right">نماد</TableHead>
                <TableHead className="text-right">منشأ</TableHead>
                <TableHead className="text-right">وضعیت</TableHead>
                <TableHead className="text-right">درخواست (UTC)</TableHead>
                <TableHead className="text-right">شروع (UTC)</TableHead>
                <TableHead className="text-right">پایان (UTC)</TableHead>
                <TableHead className="text-right">شمارش‌ها</TableHead>
                <TableHead className="text-right">خطا</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {(status?.recentRuns ?? []).map((run) => (
                <TableRow key={run.runId}>
                  <TableCell className="font-mono text-xs">
                    {run.scope}
                    {run.analysisMode ? ` (${run.analysisMode})` : ""}
                  </TableCell>
                  <TableCell className="text-xs">{run.ticker ?? "—"}</TableCell>
                  <TableCell className="text-xs">{run.trigger}</TableCell>
                  <TableCell>
                    <StatusBadge status={run.status} />
                  </TableCell>
                  <TableCell className="text-xs" dir="ltr">
                    {formatDt(run.requestedAt)}
                  </TableCell>
                  <TableCell className="text-xs" dir="ltr">
                    {formatDt(run.startedAt)}
                  </TableCell>
                  <TableCell className="text-xs" dir="ltr">
                    {formatDt(run.completedAt)}
                  </TableCell>
                  <TableCell className="text-xs whitespace-nowrap">{formatCounts(run.counts)}</TableCell>
                  <TableCell className="text-xs max-w-xs">
                    {run.errorSummary ? (
                      <details>
                        <summary className="cursor-pointer text-destructive">نمایش خطا</summary>
                        <pre className="mt-1 whitespace-pre-wrap break-words text-[11px]" dir="ltr">
                          {run.errorSummary}
                        </pre>
                      </details>
                    ) : (
                      "—"
                    )}
                  </TableCell>
                </TableRow>
              ))}
              {(status?.recentRuns.length ?? 0) === 0 && (
                <TableRow>
                  <TableCell colSpan={9} className="text-center text-xs text-muted-foreground">
                    اجرایی ثبت نشده است.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      </section>
    </div>
  );
}

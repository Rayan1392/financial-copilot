import { financialCopilotApi } from "./api-client";
import { apiUrl, getAccessToken } from "./auth";

// --------------------------------------------------------------------------
// Spec 055 — Data Management Console types
// --------------------------------------------------------------------------

// --- Archive (Noavaran Archive / CodalDB) ---

export type ArchiveDataset = string;

export type AdminArchiveImportRunResponse = {
  runId: string;
  action: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  datasets: string[];
  reason: string | null;
  errorMessage: string | null;
  symbolsProcessed: number;
  statementsProcessed: number;
};

export type AdminArchiveFreezeStateResponse = {
  isFrozen: boolean;
  frozenAt: string | null;
  reason: string | null;
};

export type AdminArchiveImportValidationResponse = {
  symbolCount: number;
  statementCount: number;
  coverageByDataset: Record<string, number>;
  missingSymbols: string[];
  issues: string[];
};

// --- Noavaran Current API (NadpcoApi) ---

export type AdminCurrentApiHealthResponse = {
  isReachable: boolean;
  latencyMs: number | null;
  lastCheckedAt: string | null;
  errorMessage: string | null;
};

export type AdminCurrentApiGapEntry = {
  symbol: string;
  dataset: string;
  missingSince: string | null;
};

export type AdminCurrentApiGapResponse = {
  gaps: AdminCurrentApiGapEntry[];
  checkedAt: string;
};

export type AdminNadpcoApiSyncStateItem = {
  dataset: string;
  watermark: string | null;
  lastRunStartedAt: string | null;
  lastRunCompletedAt: string | null;
};

export type AdminNadpcoScheduledSyncRunResponse = {
  runId: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  triggerSource: string;
  reason: string | null;
  processedCompanies: number;
  errorCount: number;
  errorMessage: string | null;
  scheduleSnapshotJson: string | null;
  datasetSelectionJson: string | null;
};

export type AdminNadpcoScheduledSyncStatusResponse = {
  currentRun: AdminNadpcoScheduledSyncRunResponse | null;
  recentRuns: AdminNadpcoScheduledSyncRunResponse[];
  nextScheduledAt: string | null;
};

export type AdminMonthlyActivityBackfillProgressResponse = {
  isRunning: boolean;
  isComplete: boolean;
  lastMonth: string | null;
  completedMonths: number;
  failedMonths: number;
  pendingMonths: number;
  monthRows: { shamsiMonth: string; status: string; completedAt: string | null; errorMessage: string | null }[];
};

export type AdminFundamentalIndexCatchUpRunResponse = {
  runId: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  fromShamsiYear: number;
  toShamsiYear: number;
  processedCompanies: number;
  errorCount: number;
  errorMessage: string | null;
};

// --- StockMarketDB ---

export type AdminStockMarketSyncStateResponse = {
  dataset: string;
  watermark: string | null;
  lastRunStartedAt: string | null;
  lastRunCompletedAt: string | null;
  logicalVendor: string | null;
  physicalSource: string | null;
  sourceMode: string | null;
};

// --------------------------------------------------------------------------
// Spec 055 — Archive API functions
// --------------------------------------------------------------------------

async function archiveAction(
  action: "dry-run" | "import" | "re-import" | "validate" | "freeze",
  datasets?: string[],
  reason?: string,
): Promise<AdminArchiveImportRunResponse> {
  return financialCopilotApi<AdminArchiveImportRunResponse>(
    `/api/v1/admin/noavaran-archive/${action}`,
    { method: "POST", body: JSON.stringify({ datasets: datasets ?? null, reason: reason ?? null }) },
  );
}

export const runArchiveDryRun = (datasets?: string[], reason?: string) =>
  archiveAction("dry-run", datasets, reason);
export const runArchiveImport = (datasets?: string[], reason?: string) =>
  archiveAction("import", datasets, reason);
export const runArchiveReImport = (datasets?: string[], reason?: string) =>
  archiveAction("re-import", datasets, reason);
export const runArchiveValidate = (datasets?: string[], reason?: string) =>
  archiveAction("validate", datasets, reason);
export const runArchiveFreeze = (datasets?: string[], reason?: string) =>
  archiveAction("freeze", datasets, reason);

export const getArchiveFreezeState = () =>
  financialCopilotApi<AdminArchiveFreezeStateResponse>("/api/v1/admin/noavaran-archive/freeze-state");

export const getArchiveRuns = (limit = 20) =>
  financialCopilotApi<AdminArchiveImportRunResponse[]>(
    `/api/v1/admin/noavaran-archive/runs?limit=${limit}`,
  );

export const getArchiveCoverage = () =>
  financialCopilotApi<AdminArchiveImportValidationResponse>("/api/v1/admin/noavaran-archive/coverage");

// --------------------------------------------------------------------------
// Spec 055 — Noavaran Current API functions
// --------------------------------------------------------------------------

export const getNadpcoHealth = () =>
  financialCopilotApi<AdminCurrentApiHealthResponse>("/api/v1/admin/noavaran-current/health");

export const getNadpcoGaps = () =>
  financialCopilotApi<AdminCurrentApiGapResponse>("/api/v1/admin/noavaran-current/gaps");

export const getNadpcoSyncState = () =>
  financialCopilotApi<AdminNadpcoApiSyncStateItem[]>("/api/v1/admin/nadpcoapi/sync-state");

export const runNadpcoFullSync = () =>
  financialCopilotApi<{ runId: string }>("/api/v1/admin/nadpcoapi/full-sync", { method: "POST" });

export const runNadpcoIncrementalSync = () =>
  financialCopilotApi<{ runId: string }>("/api/v1/admin/nadpcoapi/incremental-sync", { method: "POST" });

export const runNadpcoScheduledSync = (reason?: string) =>
  financialCopilotApi<AdminNadpcoScheduledSyncRunResponse>(
    "/api/v1/admin/nadpcoapi/scheduled-sync/run",
    { method: "POST", body: JSON.stringify({ reason: reason ?? null }) },
  );

export const getNadpcoScheduledSyncStatus = (recentRunLimit = 10) =>
  financialCopilotApi<AdminNadpcoScheduledSyncStatusResponse>(
    `/api/v1/admin/nadpcoapi/scheduled-sync/status?recentRunLimit=${recentRunLimit}`,
  );

export const getNadpcoScheduledSyncRuns = (limit = 20) =>
  financialCopilotApi<AdminNadpcoScheduledSyncRunResponse[]>(
    `/api/v1/admin/nadpcoapi/scheduled-sync/runs?limit=${limit}`,
  );

export const startMonthlyActivityBackfill = () =>
  financialCopilotApi<{ started: boolean }>("/api/v1/admin/noavaran-current/monthly-backfill", {
    method: "POST",
  });

export const getMonthlyActivityBackfillProgress = () =>
  financialCopilotApi<AdminMonthlyActivityBackfillProgressResponse>(
    "/api/v1/admin/noavaran-current/monthly-backfill",
  );

export const runFundamentalIndexCatchUp = (fromShamsiYear = 1403, toShamsiYear = 1405) =>
  financialCopilotApi<AdminFundamentalIndexCatchUpRunResponse>(
    "/api/v1/admin/nadpcoapi/fundamental-index-catch-up",
    { method: "POST", body: JSON.stringify({ fromShamsiYear, toShamsiYear }) },
  );

export const getFundamentalIndexCatchUpRuns = (limit = 20) =>
  financialCopilotApi<AdminFundamentalIndexCatchUpRunResponse[]>(
    `/api/v1/admin/nadpcoapi/fundamental-index-catch-up/runs?limit=${limit}`,
  );

// --------------------------------------------------------------------------
// Spec 055 — StockMarketDB functions
// --------------------------------------------------------------------------

export const getStockMarketSyncState = () =>
  financialCopilotApi<AdminStockMarketSyncStateResponse[]>("/api/v1/admin/stockmarketdb/sync-state");

export const runStockMarketSync = (dataset: string, fullReload = false) =>
  financialCopilotApi<{ rowsRead: number; rowsPersisted: number }>(
    `/api/v1/admin/stockmarketdb/${dataset}/sync?fullReload=${fullReload}`,
    { method: "POST" },
  );

// --------------------------------------------------------------------------
// Spec 058 — live data sync monitor types
// --------------------------------------------------------------------------

export type DataSyncActivityItem = {
  runId: string;
  provider: string;
  dataset: string;
  status: string;
  startedAt: string | null;
  completedAt: string | null;
  durationMs: number | null;
  processedRecords: number;
  errorCount: number;
  errorMessage: string | null;
  triggerSource: string;
  requestedShamsiMonth: string | null;
  logicalVendor: string | null;
  physicalSource: string | null;
  sourceMode: string | null;
};

export type DataSyncActivitySnapshot = {
  activeRuns: DataSyncActivityItem[];
  recentRuns: DataSyncActivityItem[];
};

export type DataSyncActivityEventKind = "Snapshot" | "Update" | "Heartbeat" | "Close";

export type DataSyncActivityEvent =
  | { kind: "Snapshot"; snapshot: DataSyncActivitySnapshot }
  | { kind: "Update"; updatedItems: DataSyncActivityItem[] }
  | { kind: "Heartbeat"; heartbeatAt: string }
  | { kind: "Close"; closeReason: string };

// --------------------------------------------------------------------------
// REST snapshot endpoint
// --------------------------------------------------------------------------

export async function getDataSyncActivitySnapshot(
  recentPerProvider = 5,
): Promise<DataSyncActivitySnapshot> {
  return financialCopilotApi<DataSyncActivitySnapshot>(
    `/api/v1/admin/data-sync/activity?recentPerProvider=${recentPerProvider}`,
  );
}

// --------------------------------------------------------------------------
// SSE stream helpers
// --------------------------------------------------------------------------

export function openDataSyncActivityStream(
  onEvent: (event: DataSyncActivityEvent) => void,
  onError: (error: Event) => void,
  signal: AbortSignal,
): () => void {
  const url = apiUrl("/api/v1/admin/data-sync/activity/stream");

  let es: EventSource | null = null;

  async function connect() {
    const token = await getAccessToken();
    const fullUrl = token
      ? `${url}?_token=${encodeURIComponent(token)}`
      : url.toString();

    es = new EventSource(fullUrl, { withCredentials: true });

    es.addEventListener("snapshot", (e) => {
      try {
        const snapshot: DataSyncActivitySnapshot = JSON.parse((e as MessageEvent).data);
        onEvent({ kind: "Snapshot", snapshot });
      } catch { /* ignore malformed */ }
    });

    es.addEventListener("update", (e) => {
      try {
        const updatedItems: DataSyncActivityItem[] = JSON.parse((e as MessageEvent).data);
        onEvent({ kind: "Update", updatedItems });
      } catch { /* ignore malformed */ }
    });

    es.addEventListener("heartbeat", (e) => {
      try {
        const heartbeatAt: string = JSON.parse((e as MessageEvent).data);
        onEvent({ kind: "Heartbeat", heartbeatAt });
      } catch { /* ignore malformed */ }
    });

    es.addEventListener("close", (e) => {
      try {
        const closeReason: string = JSON.parse((e as MessageEvent).data) ?? "Server closed.";
        onEvent({ kind: "Close", closeReason });
      } catch { /* ignore malformed */ }
      es?.close();
    });

    es.onerror = (e) => {
      onError(e);
    };
  }

  connect();

  const cleanup = () => { es?.close(); es = null; };
  signal.addEventListener("abort", cleanup);
  return cleanup;
}

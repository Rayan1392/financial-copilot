import { financialCopilotApi } from "./api-client";
import { apiUrl, getAccessToken } from "./auth";

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

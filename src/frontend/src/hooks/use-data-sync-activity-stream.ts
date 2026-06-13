import { useCallback, useEffect, useRef, useState } from "react";
import {
  getDataSyncActivitySnapshot,
  openDataSyncActivityStream,
  type DataSyncActivityItem,
  type DataSyncActivitySnapshot,
} from "@/integrations/financial-copilot/data-admin-client";

export type StreamStatus =
  | "connecting"
  | "live"
  | "reconnecting"
  | "polling"
  | "error";

export type DataSyncActivityStreamState = {
  status: StreamStatus;
  snapshot: DataSyncActivitySnapshot | null;
  lastUpdatedAt: Date | null;
  lastHeartbeatAt: Date | null;
  error: string | null;
};

const INITIAL_STATE: DataSyncActivityStreamState = {
  status: "connecting",
  snapshot: null,
  lastUpdatedAt: null,
  lastHeartbeatAt: null,
  error: null,
};

const POLLING_INTERVAL_MS = 5_000;
const MAX_SSE_RETRIES = 3;

export function useDataSyncActivityStream() {
  const [state, setState] = useState<DataSyncActivityStreamState>(INITIAL_STATE);
  const abortRef = useRef<AbortController | null>(null);
  const pollingTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const sseRetriesRef = useRef(0);

  const mergeUpdatedItems = useCallback(
    (prev: DataSyncActivitySnapshot, items: DataSyncActivityItem[]): DataSyncActivitySnapshot => {
      const updatedIds = new Set(items.map((i) => i.runId));

      const mergeList = (list: DataSyncActivityItem[]) =>
        list.map((i) => (updatedIds.has(i.runId) ? items.find((u) => u.runId === i.runId)! : i));

      const newActive = [
        ...mergeList(prev.activeRuns),
        ...items.filter(
          (i) =>
            ["Running", "Queued"].includes(i.status) &&
            !prev.activeRuns.some((a) => a.runId === i.runId),
        ),
      ].filter((i) => ["Running", "Queued"].includes(i.status));

      const newRecent = [
        ...items.filter(
          (i) =>
            !["Running", "Queued"].includes(i.status) &&
            !prev.recentRuns.some((r) => r.runId === i.runId),
        ),
        ...mergeList(prev.recentRuns),
      ].filter((i) => !["Running", "Queued"].includes(i.status));

      return { activeRuns: newActive, recentRuns: newRecent };
    },
    [],
  );

  const startPolling = useCallback(() => {
    if (pollingTimerRef.current) return;
    setState((prev) => ({ ...prev, status: "polling" }));

    const poll = async () => {
      try {
        const snapshot = await getDataSyncActivitySnapshot(5);
        setState((prev) => ({
          ...prev,
          snapshot,
          lastUpdatedAt: new Date(),
          error: null,
        }));
      } catch {
        // Keep polling silently; don't thrash the error state on transient failures.
      }
    };

    poll();
    pollingTimerRef.current = setInterval(poll, POLLING_INTERVAL_MS);
  }, []);

  const stopPolling = useCallback(() => {
    if (pollingTimerRef.current) {
      clearInterval(pollingTimerRef.current);
      pollingTimerRef.current = null;
    }
  }, []);

  const startSSE = useCallback(() => {
    abortRef.current?.abort();
    const ac = new AbortController();
    abortRef.current = ac;

    setState((prev) => ({ ...prev, status: "connecting", error: null }));

    openDataSyncActivityStream(
      (event) => {
        if (ac.signal.aborted) return;

        if (event.kind === "Snapshot") {
          sseRetriesRef.current = 0;
          stopPolling();
          setState((prev) => ({
            ...prev,
            status: "live",
            snapshot: event.snapshot,
            lastUpdatedAt: new Date(),
            error: null,
          }));
        } else if (event.kind === "Update") {
          setState((prev) => ({
            ...prev,
            lastUpdatedAt: new Date(),
            snapshot: prev.snapshot
              ? mergeUpdatedItems(prev.snapshot, event.updatedItems)
              : { activeRuns: event.updatedItems, recentRuns: [] },
          }));
        } else if (event.kind === "Heartbeat") {
          setState((prev) => ({ ...prev, lastHeartbeatAt: new Date() }));
        } else if (event.kind === "Close") {
          // Server closed the stream; fall back to polling.
          startPolling();
        }
      },
      () => {
        if (ac.signal.aborted) return;
        sseRetriesRef.current += 1;
        if (sseRetriesRef.current > MAX_SSE_RETRIES) {
          setState((prev) => ({
            ...prev,
            status: "polling",
            error: null,
          }));
          startPolling();
        } else {
          setState((prev) => ({ ...prev, status: "reconnecting" }));
          // Exponential backoff before retrying SSE.
          const delay = Math.min(1_000 * Math.pow(2, sseRetriesRef.current - 1), 30_000);
          setTimeout(() => { if (!ac.signal.aborted) startSSE(); }, delay);
        }
      },
      ac.signal,
    );
  }, [mergeUpdatedItems, startPolling, stopPolling]);

  useEffect(() => {
    startSSE();
    return () => {
      abortRef.current?.abort();
      stopPolling();
    };
  }, [startSSE, stopPolling]);

  const refresh = useCallback(async () => {
    try {
      const snapshot = await getDataSyncActivitySnapshot(5);
      setState((prev) => ({ ...prev, snapshot, lastUpdatedAt: new Date() }));
    } catch (err) {
      setState((prev) => ({
        ...prev,
        error: err instanceof Error ? err.message : "Refresh failed.",
      }));
    }
  }, []);

  return { ...state, refresh };
}

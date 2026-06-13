# Tasks — Live Data Sync Monitor

Implementation must keep dependency direction (domain → application → infrastructure → API →
frontend) and introduce no new persistent tables. All run state is read from existing rows.

## A. Backend — Activity aggregation contract (Application)

1. **`IDataSyncActivityReader` (Application).** Define a read-only application interface that
   aggregates the current activity snapshot from all provider run sources:
   - In-progress and recent `DataSyncRun` rows (existing `IDataSyncRunReader`).
   - `NadpcoScheduledSyncRun` rows (existing `INadpcoScheduledSyncRunReader`).
   - `StockMarketSyncState` rows (existing `IStockMarketDbSyncStateReader`).
   - `ArchiveImportRun` rows (existing `IArchiveImportRunReader`).
   - `MonthlyActivityBackfillProgress` (existing `IMonthlyActivityBackfillCoordinator`).
   - `FundamentalIndexCatchUpRun` rows (existing `IFundamentalIndexCatchUpRunReader`).

   Returns a `DataSyncActivitySnapshot` DTO containing two lists:
   `ActiveRuns` (non-terminal) and `RecentRuns` (last N terminal runs per provider, N
   configurable, default 5, max 20). Each item is a `DataSyncActivityItem` with the fields
   from AC #1 of the user story. No writes. No background threads.

2. **`DataSyncActivityItem` shape.** Define the DTO with: `RunId` (string), `Provider`
   (string), `Dataset` (string), `Status` (string), `StartedAt` (nullable), `CompletedAt`
   (nullable), `DurationMs` (nullable long), `ProcessedRecords` (int), `ErrorCount` (int),
   `ErrorMessage` (nullable string), `TriggerSource` (string), `RequestedShamsiMonth`
   (nullable string), `LogicalVendor` (nullable string), `PhysicalSource` (nullable string),
   `SourceMode` (nullable string). No domain entities, no ORM rows in the DTO.

3. **`EfCoreDataSyncActivityReader` (Infrastructure).** Implement `IDataSyncActivityReader`
   by composing the existing readers listed in task 1. Each source is queried independently
   with individual error handling: a failure on one provider's reader produces a warning item
   (status = `Error`, error message set) and does not block the other providers. Register in
   DI.

## B. Backend — Snapshot REST endpoint

4. **`GET /api/v1/admin/data-sync/activity` endpoint.** Add to the existing
   `AdminDataOperationsController`. Accepts `?recentPerProvider=5` (1–20, default 5).
   Returns `DataSyncActivitySnapshotResponse` (two JSON arrays: `activeRuns`, `recentRuns`).
   DataAdmin policy; existing rate-limit policy. Map `DataSyncActivitySnapshot` →
   `DataSyncActivitySnapshotResponse` in the controller; no business logic in the controller.

5. **Contract types.** Add `DataSyncActivitySnapshotResponse` and `DataSyncActivityItemResponse`
   to `AdminDataOperationsContracts.cs`. All fields are nullable-safe strings or primitives
   matching AC #1.

## C. Backend — SSE streaming endpoint

6. **`IDataSyncActivityMonitor` (Application).** Define a monitor contract that:
   - Provides `GetSnapshotAsync`: returns the current snapshot (delegates to
     `IDataSyncActivityReader`).
   - Provides `SubscribeAsync(channel, cancellationToken)`: writes `DataSyncActivityEvent`
     instances to a `ChannelWriter<DataSyncActivityEvent>` as the run state changes.
   - `DataSyncActivityEvent` is a discriminated record with kind (`Snapshot`, `Update`,
     `Heartbeat`, `Close`) and optional payload.

7. **`PollingDataSyncActivityMonitor` (Infrastructure).** Implement `IDataSyncActivityMonitor`
   by polling `IDataSyncActivityReader` on a configurable interval (default 5 s, minimum 2 s,
   configured under `DataSyncMonitor:PollingIntervalSeconds`). On each poll, diff the new
   snapshot against the previous one; emit `Update` events only for items whose
   `Status`, `ProcessedRecords`, or `ErrorCount` changed. Emit `Heartbeat` every 15 seconds
   if no `Update` was sent. The monitor is a singleton service; multiple SSE connections share
   one polling loop via a fan-out channel pattern. Use `System.Threading.Channels` for
   fan-out; no external message broker.

8. **Connection cap.** Track active SSE connection count with an `AtomicInteger` or
   `SemaphoreSlim`. Reject new connections beyond `DataSyncMonitor:MaxConnections` (default
   10) with HTTP 429 before the SSE stream is opened. Release the slot on client disconnect
   or server shutdown.

9. **`GET /api/v1/admin/data-sync/activity/stream` endpoint.** Add to
   `AdminDataOperationsController`. Sets `Content-Type: text/event-stream; charset=utf-8`,
   `Cache-Control: no-cache`, and `X-Accel-Buffering: no`. Writes SSE lines directly to
   `Response.Body` without holding a thread between events (use `await channel.WaitToReadAsync`
   pattern). On client disconnect (`cancellationToken` cancelled), exits cleanly. DataAdmin
   policy. Event format:
   ```
   event: snapshot\ndata: {...}\n\n
   event: update\ndata: {...}\n\n
   event: heartbeat\ndata: {"at":"<iso8601>"}\n\n
   event: close\ndata: {"reason":"shutdown"}\n\n
   ```

10. **Graceful shutdown.** Register `IHostApplicationLifetime.ApplicationStopping` to emit a
    `close` event and drain all active SSE channels before the process exits.

## D. Backend — Tests

11. **Unit tests for `EfCoreDataSyncActivityReader`:** verify that a failure on one source
    reader does not prevent the snapshot from returning items from other sources; verify
    `recentPerProvider` cap is respected; verify `DurationMs` is computed correctly from
    start/complete timestamps.

12. **Unit tests for `PollingDataSyncActivityMonitor`:** verify that no `Update` event is
    emitted when the snapshot is unchanged; verify that a status change emits exactly one
    `Update`; verify `Heartbeat` emitted after 15 s of silence; verify connection cap rejects
    the N+1 connection.

13. **Integration test for `GET /api/v1/admin/data-sync/activity`:** seeded `DataSyncRunRow`
    in Running state; response includes it in `activeRuns`; DataAdmin auth required (401/403
    without credentials).

## E. Frontend — Monitor page

14. **Route registration.** Add `/admin/data/monitor` to the admin router. Add a "Live Monitor"
    navigation item under the **Data Operations** section in the admin sidebar, adjacent to
    the data management console from spec `055`. Apply the `DataAdmin` permission guard.

15. **`useDataSyncActivityStream` hook.** Encapsulate SSE lifecycle: open `EventSource` on
    mount; handle `snapshot`, `update`, `heartbeat`, `close` events; maintain a `Map<runId,
    DataSyncActivityItem>` in `useReducer`; reconnect with exponential back-off (1 s, 2 s,
    4 s, max 30 s) on error; fall back to polling `GET /api/v1/admin/data-sync/activity` every
    10 s when SSE is unavailable. Expose `items`, `lastHeartbeatAt`, `connectionStatus`
    (`connected` / `reconnecting` / `polling`).

16. **`SyncActivitySummaryStrip` component.** Shows running / queued / failed count badges.
    Counts derived from the `items` map; updates reactively on each hook emission.

17. **`SyncActivityCard` component.** Renders one `DataSyncActivityItem`:
    - Provider/source label, dataset name, status badge with color-coded CSS class.
    - Records processed and error count.
    - Duration: live-elapsed for running runs (updated every second via `setInterval`);
      final duration for completed/failed runs.
    - Trigger source badge; Shamsi month label when present.
    - Click handler opens the detail panel.

18. **`SyncRunDetailPanel` component.** Side drawer or accordion that renders full run detail:
    - All `SyncActivityCard` fields plus full `errorMessage`.
    - For NADPCO scheduled runs: `scheduleSnapshotJson` / `datasetSelectionJson` parsed and
      rendered as a table.
    - For monthly-activity backfill: fetches
      `GET /api/v1/admin/noavaran-current/monthly-backfill` (already implemented in spec `057`)
      on open and renders per-month status rows.
    - Loading and error states for the detail fetch.

19. **Filter bar.** Multi-select inputs for Provider, Dataset, Status. Filters apply to the
    local `items` map via `useMemo`; no API re-fetch. Provider and dataset option lists are
    derived from the items currently in memory.

20. **History table.** Below the activity feed, a table of the N most recent terminal runs
    sourced from `recentRuns` in the snapshot. Columns: Provider, Dataset, Status, Started,
    Duration, Records, Errors. Client-side sort by Started (default descending). Pagination:
    show 20 rows per page.

21. **Loading, empty, error, and reconnecting states.** Skeleton cards while the initial
    snapshot loads. "No activity found" empty state when all filters exclude all items.
    "Connection lost — reconnecting in Xs" banner with a manual retry button when SSE is
    unavailable. Individual error cards for runs in `failed` status.

22. **Frontend tests.** Add tests (Vitest / React Testing Library pattern used in the project):
    - Route is protected: unauthenticated user is redirected.
    - `useDataSyncActivityStream` falls back to polling when SSE fails.
    - Filter bar hides cards not matching the selected provider.
    - History table sorts by Started correctly.
    - Detail panel fetch for monthly-backfill is triggered only on open.

## F. Verification gate

23. **`dotnet test src/backend/FinancialCopilot.sln --configuration Release` passes.**

24. Manual evidence, in order:
    - Start the Worker with an active NADPCO or StockMarketDB sync; open `/admin/data/monitor`
      in the browser; confirm the running run card appears within 6 seconds of the sync
      starting and the record count advances live.
    - Disconnect the SSE deliberately (kill the network tab); confirm the page falls back to
      polling and shows the "reconnecting" banner within 12 seconds.
    - Complete the sync; confirm the run card moves to the history table.
    - Confirm DataAdmin permission is enforced: a non-admin user receives a 403 on the SSE
      endpoint.

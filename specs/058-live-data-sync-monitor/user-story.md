# Live Data Sync Monitor

## User Story

As a data administrator, I want a live monitoring view of every active and recently completed data
synchronization run so I can see which provider is currently ingesting data, which dataset is
being processed, how many records have been written, whether any errors are occurring, and when
the run finishes — without refreshing a page or polling a log file manually.

## Context

The Worker process runs data synchronization continuously:

- **NADPCO scheduled sync** fires on a configurable cadence, iterating all companies across
  financial statements, monthly activity, fundamental indexes, and the company catalog.
- **StockMarketDB polling** runs every minute (trades) / five minutes (indices) to ingest fresh
  intraday data.
- **CodalDB incremental sync** is triggered manually or via DataAdmin.
- **Monthly-activity backfill** walks Shamsi months newest-first, per company, until complete.
- **Fundamental-index catch-up** sweeps years 1403–1405 for all local companies.
- **Archive import** (one-time) runs on explicit DataAdmin invocation.

Today, while any of these are running, the only way to observe progress is to query individual
admin REST endpoints (`/api/v1/admin/nadpcoapi/scheduled-sync/status`,
`/api/v1/admin/stockmarketdb/sync-state`, etc.) or read raw server logs. There is no single
surface that shows live run activity across all providers, no push from the server when a run
advances, and no historical per-run detail beyond the current run snapshot.

## What This Story Adds

### Backend: Live Activity API

A new backend read-only endpoint aggregates in-progress and recent run snapshots across all
providers and datasets into a single response shape. A separate Server-Sent Events (SSE)
endpoint streams activity updates to subscribed admin clients so the browser receives push
notifications when a run starts, advances (record count change, status update), or completes,
without long-polling.

### Frontend: Live Data Sync Monitor Page

A new protected admin page at `/admin/data/monitor` subscribes to the SSE stream and displays:

- A live summary strip: counts of currently **running** / **queued** / **failed** runs across
  all providers.
- An activity feed: each active or recently completed run as a card showing provider name,
  dataset name, current status, records processed, error count, run duration, and any error
  message — auto-updated as events arrive.
- A run-detail drawer: selecting a run card expands a full timeline showing each lifecycle
  event recorded for that run (started, advanced, completed, failed) with timestamps.
- Filters for provider, dataset, and status (running / queued / completed / failed).
- A history table below the live feed listing the last N completed runs (configurable, default
  50) with the same columns, sortable by start time.

The page integrates into the existing admin shell from spec `037` / `055` without duplicating
the identity, role, or subscription administration panels.

## Acceptance Criteria

### Backend — Activity snapshot endpoint

1. `GET /api/v1/admin/data-sync/activity` returns the current activity snapshot:
   - All runs currently in a non-terminal state (queued / running / retrying) across all
     providers and datasets.
   - The N most recent completed / failed runs per provider (configurable server-side,
     default 5 per provider, max 20).
   - Each run row exposes: `runId`, `provider`, `dataset`, `status`, `startedAt`,
     `completedAt`, `durationMs`, `processedRecords`, `errorCount`, `errorMessage`,
     `triggerSource` (manual / scheduled / worker), `requestedShamsiMonth` (nullable,
     monthly-activity runs), `logicalVendor`, `physicalSource`, `sourceMode`.
   - DataAdmin policy; same rate-limit policy as other admin endpoints.

2. The response normalizes run sources:
   - `DataSyncRunRow` (enqueued symbol/statement/monthly-report/financial-ratio/fundamental-index
     messages) → `provider` = resolved logical vendor or provider name, `dataset` = dataset name.
   - `NadpcoScheduledSyncRunRow` → `provider = "NoavaranCurrentApi"`,
     `dataset = "ScheduledSync"`.
   - `StockMarketSyncStateRow` → one activity item per dataset with the last-run timestamps.
   - `ArchiveImportRunRow` → `provider = "NoavaranArchiveSql"`, `dataset = action name`.
   - `MonthlyActivityBackfillStateRow` / per-month progress → one item per in-progress or
     recently completed Shamsi month.
   - `FundamentalIndexCatchUpRunRow` → `provider = "NoavaranCurrentApi"`,
     `dataset = "FundamentalIndexCatchUp"`.

3. The snapshot reads are non-blocking reads on existing persistent state; no new background
   threads, no new persistent tables, no write side effects from reads.

### Backend — SSE stream endpoint

4. `GET /api/v1/admin/data-sync/activity/stream` opens an SSE connection:
   - Emits an initial `snapshot` event containing the same payload as AC #1.
   - Emits a `heartbeat` event every 15 seconds while the connection is idle so the client
     knows the server is still alive.
   - Emits an `update` event whenever an observed run changes state (new run started, record
     count advanced, status changed, run completed or failed). Polling interval is configurable
     (default 5 seconds, minimum 2 seconds).
   - Emits a `close` event if the server is shutting down gracefully.
   - DataAdmin policy; connections are counted but not metered.
   - Maximum active SSE connections per API instance is configurable (default 10) to prevent
     resource exhaustion; excess connections receive a `429 Too Many Requests` before the SSE
     stream is established.
   - A client that disconnects is cleaned up immediately; no connection leak.

5. The SSE endpoint must not block request-processing threads. It uses ASP.NET Core response
   streaming (`Response.Body`) on a dedicated channel and does not hold thread pool threads
   between events.

### Backend — No new persistence

6. No new database tables or EF migrations are introduced by this story. All activity data is
   read from existing tables (`DataSyncRunRows`, `NadpcoScheduledSyncRunRows`,
   `ArchiveImportRunRows`, `MonthlyActivityBackfillStateRow`, `FundamentalIndexCatchUpRunRows`,
   `StockMarketSyncStateRows`). If a provider's run state is only available as an in-memory
   snapshot (no persisted row), the existing provider exposes it through its
   `IXxxSyncStateReader` contract and the aggregator reads it there.

### Frontend — Monitor page

7. The `/admin/data/monitor` route is registered under the existing admin shell and listed in
   the admin navigation under **Data Operations** (alongside the existing data management
   console from spec `055`). It is protected by the `DataAdmin` permission check.

8. On mount, the page opens an SSE connection to `/api/v1/admin/data-sync/activity/stream`:
   - The initial `snapshot` event populates the live-activity section immediately.
   - Subsequent `update` events merge into the existing list without a full re-render
     (keyed by `runId`).
   - `heartbeat` events update a "last contact" timestamp shown discreetly in the UI.
   - On SSE error or close, the client falls back to polling
     `GET /api/v1/admin/data-sync/activity` every 10 seconds and shows a "reconnecting"
     badge until the stream is re-established.

9. The live-activity section shows a **summary strip** at the top:
   - Running count (green badge), Queued count (blue badge), Failed count (red badge).
   - Auto-updates when `update` events arrive.

10. Below the summary strip, an **activity feed** lists active and recently completed runs as
    cards (newest first). Each card shows:
    - Provider/source label (e.g. "Noavaran Current API / Monthly Activity").
    - Dataset or operation name.
    - Status badge (color-coded: running = green pulse, queued = blue, failed = red,
      completed = grey).
    - Records processed so far / error count.
    - Start time and elapsed duration (live-updating for running runs, final for completed).
    - Trigger source (Scheduled / Manual / Worker / DataAdmin).
    - If the run carries a `requestedShamsiMonth`, display it as "Shamsi month: 1405/02".

11. Clicking a run card opens a **detail panel** (side drawer or expandable section) showing:
    - All fields from the card.
    - Full error message if any.
    - For NADPCO scheduled runs: the `scheduleSnapshotJson` and `datasetSelectionJson` parsed
      into a readable table.
    - For monthly-activity backfill: a table of per-month rows that distinguishes at least
      completed with persisted rows, no data yet / retryable, failed, and pending / not
      attempted; the backfill detail view should also surface the aggregate backfill state as
      one of `Completed`, `CompletedWithFailures`, `InProgress`, `Pending`, or `Retryable`, and
      must not imply that `CompletedWithFailures` is terminal.

12. A **filter bar** above the activity feed allows filtering by:
    - Provider (multi-select, populated from known provider names).
    - Dataset (multi-select, populated from known dataset names).
    - Status (multi-select: Running, Queued, Failed, Completed).
    - Filters apply client-side to the in-memory activity list; they do not trigger new API
      calls.

13. A **history section** below the activity feed shows a paginated table of the last 50
    completed or failed runs across all providers. Columns: Provider, Dataset, Status, Started,
    Duration, Records, Errors. Sortable by Started (default descending). Pagination is
    client-side over the already-fetched history from the snapshot.

14. The page handles all loading, empty, error, and reconnecting states:
    - Loading: skeleton placeholders while the initial snapshot is fetching.
    - Empty: "No sync activity found" when there are no runs in scope.
    - SSE error: "Connection lost — reconnecting in Xs" with a manual retry button.
    - Run error detail: error message shown inline on the card and in full in the detail panel.

15. The page uses the existing authenticated API bridge from spec `031` and the existing
    frontend design system (components, layout, spacing, color tokens). No new UI library
    dependencies are introduced.

## What Is Out Of Scope

- Streaming individual log lines from the Worker process (log aggregation / ELK / OTLP is a
  separate concern).
- Starting or stopping sync runs from this page (trigger actions remain on the existing data
  management console pages from spec `055`).
- Per-record-level progress within a run (only aggregate counts are exposed).
- WebSocket upgrade (SSE is sufficient; WebSocket introduces a bidirectional protocol overhead
  not needed here).
- Alerting or notification delivery outside the browser (email, Slack, PagerDuty).
- Backend changes to how existing sync services record progress (they already persist run rows;
  this story reads those rows, it does not change how they are written).

## Dependencies

- `012` (admin data operations — existing run-persistence contracts and DataAdmin policy).
- `031` (authenticated API bridge — credentials and auth context for admin routes).
- `037` (admin panel shell — navigation and route registration).
- `055` (frontend data management console — co-located under `/admin/data/`).
- `030`, `043`, `044`, `052`, `053`, `057` — the sync providers whose run state is aggregated.

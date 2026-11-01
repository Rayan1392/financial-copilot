# Bug: Investigate and fix RabbitMQ connection churn in FinancialCopilot.DataSync

Date: 2026-11-01
Severity: High (broker resource pressure; production RabbitMQ observed near 95% CPU)
Status: Fixed in working tree (not committed). Root cause confirmed in code; fix verified with adapter-seam tests.
**Not verified against a live broker and not deployed.** Production effect on CPU is unmeasured.
Scope: `FinancialCopilot.Infrastructure` RabbitMQ adapters (`DataSyncMessaging`, `FeatureMessaging`),
their API and Worker publishers, Worker consumers. RabbitMQ.Client `7.1.2` (unchanged).

## Problem

Production RabbitMQ logs show connections named `FinancialCopilot.DataSync` being opened and closed
continuously from `172.19.0.5` and `172.19.0.6` (API and Worker containers on the compose network).
Many connections live only 13–117 ms.

## Production evidence (as reported, not reproduced here)

- One RabbitMQ instance at about 95% CPU.
- Short-lived `FinancialCopilot.DataSync` connections from two container addresses.
- At sampling time `financialcopilot.data-sync.requests` and
  `financialcopilot.features.recalculate.requested` each had one consumer, with
  `messages_ready = 0` and `messages_unacknowledged = 0`.
- Most of the connection log volume is from the day before the CPU sample, so a direct causal link
  between the churn and the CPU sample is **not proven** and remains unproven after this fix.

## Hypotheses and findings

| # | Hypothesis | Finding |
|---|---|---|
| H1 | Publisher opens a connection per message or batch | **Confirmed — root cause of the churn.** |
| H2 | Consumers reconnect in a tight loop / Worker restart loop | Not a churn source: consumers held one connection each for their lifetime. A **separate defect** was found instead: consumers never recover a broker-closed channel (see below). |
| H3 | Health check or poller opens a connection per probe | Rejected: only three source files reference `RabbitMQ.Client`; no health check does. The compose healthcheck runs `rabbitmq-diagnostics ping` inside the broker container. |
| H4 | Fast nack/requeue loop drives CPU | Possible but not a connection source and not active at sampling time (`messages_ready = 0`). Left unchanged to preserve the delivery contract; recorded as a residual risk. |

## Root cause

`RabbitMqDataSyncRequestBus.PublishBatchAsync` (and therefore `PublishAsync`) in
`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Messaging/RabbitMqDataSyncMessaging.cs`
built a new `ConnectionFactory` and did `await using var connection = await CreateConnectionAsync(...)`
named `"FinancialCopilot.DataSync"` on **every call**, then opened a confirm-enabled channel,
declared the queue, published, awaited the confirm, and closed everything. That sequence (TCP connect,
AMQP handshake + SASL auth, `channel.open`, `confirm.select`, `queue.declare`, `basic.publish`,
confirm, `channel.close`, `connection.close`) matches the observed 13–117 ms lifetimes.

Callers publish one message per call, in loops and in parallel:

- `NadpcoApiScheduledSyncService` — per company 2 requests (FinancialStatements, FundamentalIndexes)
  under `MaxReadParallelism`, then 5 MonthlyProductionSales output-type waves, plus one Symbols
  request: up to **7N + 1 connections per full daily run** for N companies (Worker, `172.19.0.x`).
- `FundamentalIndexCatchUp` (parallel per company), `EligibleFundamentalIndexBulkSyncService`,
  `SingleCompanyMonthlyIngestionService` (months × output types), `CodalDbScheduledSyncService`
  (3 per company), `MonthlyActivityBackfillOutboxRelay` (one per batch).
- `AdminDataOperationsController` — per company in admin batch operations, and per admin request
  (API, the other address).

The daily schedule (`NadpcoScheduledSync:CadenceSeconds = 86400`) produces bursts, which is
consistent with most connection logs falling on the day before the CPU sample.

`RabbitMqFeatureBus` had the identical pattern (`"FinancialCopilot.Features"`, one connection per
requested/completed/failed publish) and is fixed the same way.

### Separate defect found: silently lost consumers

`ConsumeAsync` registered a consumer and then awaited `Task.Delay(Infinite)`. If the broker closed
only the **channel** — for example RabbitMQ's default `consumer_timeout` (30 minutes) when a data-sync
request runs longer than that with its message unacknowledged; no `x-consumer-timeout` is configured
here — the connection stayed open, so client automatic recovery never ran, and that consumer was gone
for the life of the process while the Worker looked healthy. This **may** explain the production
sample showing one consumer on `financialcopilot.data-sync.requests` when `ConsumerCount` defaults to
4, but the production value of `DATA_SYNC_CONSUMER_COUNT` was not available, so this is unproven.

## Fix

New shared components in `src/backend/FinancialCopilot.Infrastructure/Financial/Messaging/`:

- `RabbitMqSharedConnection` — one long-lived connection per process and role, opened lazily.
  A single-flight gate means concurrent first use opens exactly one connection. An established
  connection is left to client automatic recovery; while it is down, callers fail fast with
  `RabbitMqUnavailableException` instead of opening another. Only if recovery has not succeeded
  within the grace period (30 s = 6 × `NetworkRecoveryInterval`) is the old connection disposed
  **before** a replacement is opened, so two connections never coexist. A failed open is paced:
  transient failures cool down with capped exponential backoff and jitter (1 s → 60 s);
  permanent failures (rejected credentials, 403/530, 406 queue-argument mismatch) cool down 5 minutes.
- `RabbitMqPublisherChannelPool` — at most `PublisherChannelLimit` (default 4) channels on the
  publisher connection. Each publish leases a channel **exclusively** (RabbitMQ.Client 7.1.2 does not
  document concurrent publishing on one channel as safe). A channel that threw or closed is disposed,
  never returned. The lease, including waiting for a channel and the broker confirm, is bounded by
  `PublishTimeoutSeconds` (default 30) per message and surfaces as `TimeoutException`, so callers
  record a failed publish instead of treating it as cancellation.
- `RabbitMqResilientConsumer` — each competing consumer owns one channel on the shared consumer
  connection. When the channel closes it is disposed and replaced after capped, jittered backoff
  (reset after a healthy run); permanent failures are logged as critical and rethrown, keeping the
  previous fail-fast behaviour.
- `RabbitMqConnectionPolicy` — limits, backoff, failure classification and the `ConnectionFactory`
  settings: `AutomaticRecoveryEnabled = true`, `TopologyRecoveryEnabled = false` (the adapters own
  queue declaration and consumer registration, so the library never re-registers a consumer the
  adapter already replaced), `NetworkRecoveryInterval = 5 s`.

Resulting connections per process (names now carry the role; the old names remain as prefixes):

| Process | Connections |
|---|---|
| API | `FinancialCopilot.DataSync.Publisher`, `FinancialCopilot.Features.Publisher` (each only once used) |
| Worker | the two publishers plus `FinancialCopilot.DataSync.Consumer` (`ConsumerCount` channels) and `FinancialCopilot.Features.Consumer` (1 channel) |

### Delivery contract preserved

Unchanged: queue names, default exchange routing, `durable: true`, `exclusive: false`,
`autoDelete: false`, `x-consumer-timeout` argument behaviour, queue declaration before every publish,
DataSync `Persistent`, `ContentType`, `MessageId`, `CorrelationId`, `mandatory: true`, publisher
confirms with tracking; Features `mandatory: false`, no confirms; `BasicQos` prefetch per consumer;
`autoAck: false`; DataSync ack after success, nack with requeue on handler failure, nack without
requeue for malformed messages; Features ack before handling. Consumer dispatch concurrency stays 1
per channel, so concurrency still equals `ConsumerCount`.

One deliberate behaviour change: a publish now fails with `TimeoutException` after the publish
budget instead of waiting indefinitely for a confirm. The broker may still accept that message, so a
retry can duplicate it; consumers already carry `IdempotencyKey`.

## Files changed

| File | Reason |
|---|---|
| `src/backend/FinancialCopilot.Infrastructure/Financial/Messaging/RabbitMqConnectionPolicy.cs` (new) | Limits, backoff with jitter, permanent/transient classification, connection factory settings |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Messaging/RabbitMqSharedConnection.cs` (new) | Long-lived, single-flight, recovery-aware connection |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Messaging/RabbitMqPublisherChannelPool.cs` (new) | Bounded exclusive-lease publisher channels |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Messaging/RabbitMqResilientConsumer.cs` (new) | Consumer channel ownership and replacement |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Messaging/RabbitMqDataSyncMessaging.cs` | Use shared connections, pool and resilient consumer; options `PublisherChannelLimit`, `PublishTimeoutSeconds`; disposal |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Features/Messaging/RabbitMqFeatureMessaging.cs` | Same pattern for the Features bus |
| `tests/FinancialCopilot.UnitTests/RabbitMqConnectionLifecycleTests.cs` (new) | Regression tests at the adapter seam |

No DI registration, configuration file, compose file or dependency version changed. The new options
have defaults and can be overridden with `DataSyncMessaging__PublisherChannelLimit`,
`DataSyncMessaging__PublishTimeoutSeconds` and the `FeatureMessaging__` equivalents.

## Verification

### Tests

`tests/FinancialCopilot.UnitTests/RabbitMqConnectionLifecycleTests.cs` runs the real adapters against
fakes of the client's own `IConnectionFactory` / `IConnection` / `IChannel`. It covers:

- 200 sequential publishes → 1 connection, 1 channel.
- Routing, durability, mandatory flag, persistence and message properties unchanged.
- 100 concurrent publishes during a slow first connect → 1 connection attempt, ≤ 4 channels, zero
  overlapping uses of a channel.
- A closed channel and a throwing channel are discarded and replaced on the same connection.
- Transient connect failure: 50 publishes cause 1 connection attempt until the cooldown ends.
- Rejected credentials: no new attempt until the 5-minute cooldown ends.
- Lost connection: no replacement while recovery is pending; recovery reuses the connection; after
  the grace period, exactly one replacement and the old one disposed.
- A stuck confirm fails as `TimeoutException` and releases its channel lease.
- Dispose closes the connection and channels; later publishes throw `ObjectDisposedException`.
- 4 competing consumers → 1 connection, 4 channels, prefetch 1, `autoAck: false`; cancellation
  stops them and disposes their channels.
- Ack on success, nack + requeue on handler failure, nack without requeue on malformed JSON.
- A broker-closed consumer channel (406) is replaced without a new connection.
- Unreachable broker: 2–12 attempts in 500 ms, not a hot loop; then recovers.
- Rejected credentials stop the consumer after one attempt.
- Features bus: 30 publishes on 1 connection without confirms, `mandatory: false`, correct queues;
  consumer still acks before handling.

### Results (2026-11-01, local, no broker)

| Check | Result |
|---|---|
| `dotnet build src/backend/FinancialCopilot.sln` | 0 errors, 0 warnings |
| `RabbitMqConnectionLifecycleTests` | 16 passed, 0 failed (3 consecutive runs plus a final run) |
| Full unit suite | 2785 passed, 20 failed, 0 skipped |
| Architecture tests | 14 passed, 1 failed |
| Integration `AdminDataOperationsEndpointTests` + `DerivedFeaturePersistenceTests` (the publisher callers) | 64 passed, 7 failed |
| `git diff --check` on changed sources | clean |

All failures are pre-existing. A clean detached worktree at `HEAD` (`f200c84`) without this change
fails a superset of the same unit tests (21; the 20 above plus
`CyclicalWavesPsProviderFixtureTests`, which passes in the working tree), the same 7 integration
tests (`JsonReaderException` on an empty response body; these tests replace the publisher with a
capturing fake), and the known architecture test
`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`. No failing test references the
messaging code.

### Live broker

Not executed: the local RabbitMQ installation is incomplete (server binaries missing) and Docker
does not respond on this machine. No broker was installed for this task.

Manual test with a disposable broker (for example `rabbitmq:4-management-alpine`):

1. Start the Worker and API against it with `DataSyncMessaging__Enabled=true` and
   `FeatureMessaging__Enabled=true`.
2. `rabbitmqctl list_connections name peer_host connected_at` → only the role-named connections above;
   `connected_at` stays unchanged while you trigger 100+ admin data-sync requests and a NADPCO sync.
3. `rabbitmqctl list_channels connection number consumer_count` → `ConsumerCount` + 1 consumer
   channels, ≤ 4 publisher channels per publisher connection.
4. `curl -u <user> http://<host>:15672/rabbitmq/api/overview` → `churn_rates.connection_created_details.rate`
   and `connection_closed_details.rate` stay near 0 during publishing.
5. Restart the broker: logs show "was shut down", then "recovered" or a single replacement; consumers
   re-register; `rabbitmqctl list_queues name consumers` returns the configured counts.
6. Force a consumer channel close (temporarily set a low `consumer_timeout` with a broker policy and
   process a slow message): log "consumer channel was closed by Peer (406 …); replacing it" and the
   consumer count recovers.
7. Wrong password: one error per connection role per 5 minutes on the publisher side; the Worker
   consumers log critical and the host stops, as before.

## Measurement

- **Before (from code, not measured on a broker):** one connection per `PublishAsync` /
  `PublishBatchAsync` call; up to 7N + 1 per full NADPCO daily run, plus per-company admin batches.
- **After (measured in the test harness):** 1 connection per process and role regardless of message
  count; ≤ `PublisherChannelLimit` publisher channels under 100 concurrent publishers.
- **CPU, message rate and connection churn on the production broker:** not measured. No CPU
  reduction is claimed.

## Post-deployment checks

- Compare the broker's connection-churn rate across a NADPCO daily sync before and after.
- Confirm `list_queues name consumers` matches `DATA_SYNC_CONSUMER_COUNT` and 1 for features, and
  stays there over a day.
- Watch Worker logs for "consumer channel was closed by Peer (406 …)". If it recurs, long data-sync
  handlers are exceeding `consumer_timeout`; raise it with a broker policy or `rabbitmq.conf`.
  Changing `x-consumer-timeout` on the existing durable queue would fail declaration with 406.
- Correlate broker CPU with the churn rate before attributing the original CPU sample to it.

## Residual risks

- Immediate `requeue: true` on handler failure can redeliver a persistently failing message in a tight
  loop (H4). Unchanged here because it is part of the delivery contract; a dead-letter or delayed
  retry policy needs its own decision.
- A permanent consumer failure still stops the Worker host (previous behaviour); with Docker
  `restart: unless-stopped` that repeats one failed connection attempt per restart.
- Queue declaration still runs before every publish (one extra round trip, no connection).

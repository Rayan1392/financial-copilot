# Feature 126 — Slice 1 Final Acceptance Review

## Verdict

**APPROVED**

## Review scope

Reviewed only the Slice 1 production changes, Slice 1 unit tests, and
`Feature126Slice1PostgreSqlTests`.

## Acceptance verification

### 1. Lease fencing

Confirmed. On the PostgreSQL path, `PersistAcceptedAsync` opens a transaction,
locks the existing lease row with `FOR UPDATE`, validates expiry and the exact
`Running` owner envelope (including the fencing token), inserts the source fact,
and commits as one atomic operation. A stale token, expired owner, or changed
owner envelope is rejected before insertion. A valid current owner persists
successfully.

### 2. Concurrency

Confirmed by the PostgreSQL integration tests:

- lease takeover creates a new fencing token;
- the stale owner cannot renew or write after takeover;
- the current owner can write;
- a stale write blocks on the lease-row lock while takeover is uncommitted,
  then rejects after takeover commits, with no source fact inserted.

Focused result: **4 unit tests passed; 4 PostgreSQL integration tests passed.**

### 3. Persistence

Confirmed. Source facts use the existing immutable observation identity and
unique provider/source-kind/observation index. Replays are deterministic
`Unchanged` no-ops; new observation identities can create immutable facts, and
rejected inputs do not alter existing facts. The existing source-fact and lease
tables/configuration and the existing migration provide the required schema;
there is no Slice 1 migration or schema gap.

### 4. Eligibility

Confirmed. `NoavaranEligibleCompanyUniverseReader` materializes only
`SymbolIsin` from `NoavaranEligibleCompanies`. The PostgreSQL test verifies that
the admitted set follows that view and is not rebuilt from an alternate
universe source.

### 5. Scope

Confirmed. Slice 1 introduces no activation, cutover, NADPCO changes, Feature
125 handoff, or Feature 114 changes. The changes are limited to shared
contracts, the CyclicalWaves P/S operation seam, the eligible-universe reader,
lease/source-fact stores, DI registration, and their tests.

## Blocking findings

None.

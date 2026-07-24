# Phase 2 — Execution Foundation (Dynamic HR Requests)

**Date:** 2026-07-25
**Status:** Approved design, pre-implementation
**Scope decision:** Execution engine only. Definition *versioning* (draft/published/versioned
RequestType+form+effects with per-instance snapshots) is explicitly **out of scope** here and gets its
own later spec.

## Problem

Today, when a request receives final approval, **every** effect runs synchronously inside the approval
transaction (`RequestEngine.ApproveAsync` → `CompletionEngine.ExecuteAsync`). Two execution modes exist:

- `Transactional` — runs inline; any throw rolls back the whole completion and the request lands in
  `CompletionFailed`.
- `Asynchronous` — runs inline too, but the executor only *enqueues* durable work (only user today:
  `Notification.Send` → `EmailNotificationQueue`, drained by `EmailQueueDrainer` /
  `EmailDeliveryHostedService`).

There is **no retry** (an `Attempts` column exists on `engine_completion_effects` but is never used for
retry), **no scheduling** (an effect cannot take effect on a future date), **no idempotent re-execution**,
and **no operator recovery** for a failed effect. This blocks the load-bearing business effects that are
inherently date-effective or long-running: date-effective promotion / transfer / bank-update,
resignation → EOS settlement + offboarding, overtime → payroll, etc.

## Goals (focus items)

Durable scheduled effects · idempotency · safe retries · effect execution history · failed-effect
recovery · date-effective actions · worker/background processing · transaction safety · preventing
duplicate executions · compatibility with existing requests and effects.

**Non-goal:** definition versioning; rebuilding the request/effect engine. Everything here is **additive**.

## Grounding (verified against current code, 2026-07-25)

- Effects fire in `HR.Modules.Platform.Services.Completion.CompletionEngine.ExecuteAsync`. Phase A
  persists the `CompletionRun` + `CompletionEffect` rows and commits (so history survives a later
  rollback and the caller's approval/workflow changes are flushed). Phase B runs all effects inside one
  `BeginTransactionAsync`; a throw rolls back every module mutation, clears the change tracker, and
  `PersistFailureAsync` records the failure on the already-committed run.
- History tables: `engine_completion_runs`, `engine_completion_effects`. Effect definitions:
  `engine_request_effect_definitions` (has `ExecutionMode`, `ConfigurationJson`, `IsEnabled`,
  `IsRequired`, `Sequence`).
- Effect execution contract: `IEffectExecutor` (`EffectType`, `Version`, `ExecuteAsync`), resolved via
  `IEffectExecutorRegistry`; `EffectContext` (RequestInstanceId/Number, RequestTypeCode, EmployeeId,
  ActorUserId, `Payload` JsonElement + typed readers); `EffectExecutionResult` (`Ok` / `Skip(reason)` /
  throw).
- Proven durable-worker pattern to mirror: `EmailQueueDrainer` (batch 25, `MaxAttempts` 5,
  `IgnoreQueryFilters` across tenants, per-row try/catch, pure `EmailDeliveryDecision.Apply`) drained by
  `EmailDeliveryHostedService : BackgroundService` (60s poll, `IServiceScopeFactory` scope).
- Background tenant/user context: `IBackgroundExecutionContext.Begin(tenantId, userId, email)` (used by
  payroll's Hangfire job) re-establishes ambient tenant/user so global query filters, audit, and
  `CreatedBy` work off the HTTP path.
- Hangfire is present but **off** (`Hangfire:Enabled`, Postgres storage), used only for payroll. Phase 2
  does **not** enable it — per the worker decision below.

## Key decisions (approved)

1. **Scope** = execution engine only; versioning deferred to its own spec.
2. **Worker** = a new durable table drained by a hosted `BackgroundService`, mirroring
   `EmailQueueDrainer`/`EmailDeliveryHostedService`. No Hangfire enablement, no prod config flip.
3. **Scheduling granularity** = date-level. `ScheduledFor` is honored by a periodically-polling worker;
   robust across restarts. (A `timestamptz` column is used so finer precision is possible later without a
   migration, but the product contract is "effective on a day".)
4. **Per-attempt history table** (`engine_effect_attempts`) is **included**.
5. **Retry defaults**: `MaxAttempts = 5`, exponential backoff base 1 min (≈ 1, 2, 4, 8, 16 min), then
   `ManualReview`.

## Architecture

### The deferred-effect model (a third execution kind)

A new `EffectExecutionMode.Deferred` is added alongside the two existing modes, which are **unchanged**:

| Mode | Behavior | Change |
| --- | --- | --- |
| `Transactional` | Runs inline in the approval transaction; failure → `CompletionFailed`. | none |
| `Asynchronous` | Runs inline; executor enqueues durable work (e.g. `Notification.Send`). | none |
| `Deferred` (new) | Not run at approval. Enqueued as a durable work item; a background worker executes it later — on its effective date, with idempotency + retry + recovery. | new |

**Transactional outbox.** At completion, `Deferred` effect rows are written **in the same commit as the
approval** (Phase A already commits the run + effect rows). Therefore:

- approval commits ⇒ deferred effects are durably queued (never lost);
- approval rolls back ⇒ the queued effects roll back with it (no orphans);
- the **worker is the only execution path** for deferred effects ⇒ a single place to enforce
  effectively-once.

The worker reuses the **same** `IEffectExecutorRegistry`, `EffectContext`, and `EffectExecutionResult` —
executors do not change. It re-establishes tenant/user context via `IBackgroundExecutionContext.Begin`.

## Data model & migrations

The existing `engine_completion_effects` row already **is** the per-effect history record. Rather than a
parallel queue table, we extend it so "the durable queue" is simply the set of completion effects that are
due — one source of truth for both history and recovery.

### Migration A — extend `engine_completion_effects`

| Column | Type | Purpose |
| --- | --- | --- |
| `ScheduledFor` | `timestamptz null` | Effective date; null = run ASAP. |
| `NextAttemptAt` | `timestamptz null` | Retry backoff gate. |
| `MaxAttempts` | `int not null default 1` | 1 preserves today's no-retry behavior. |
| `IdempotencyKey` | `varchar(200) null`, **unique index** | Effectively-once backstop. |
| `LeasedUntil` | `timestamptz null` | Worker lease expiry. |
| `LeasedBy` | `varchar(100) null` | Which worker holds the row. |

### Migration B — extend `engine_request_effect_definitions`

- Add `MaxAttempts int not null default 1`.
- `ExecutionMode` already exists; the `Deferred` value is a new int — no schema change.
- The effective-date input mapping rides inside the existing `ConfigurationJson` under the reserved key
  `__effectiveOn` (a normal `EffectValueMapping`, resolved by `CompletionEffectFactory` at enqueue) — no
  new column.

### Migration C — new `engine_effect_attempts`

One row per attempt: `Id`, `CompletionEffectId` (FK), `AttemptNumber`, `StartedAt`, `Status` (int),
`DurationMs int null`, `FailureReason varchar(2000) null`, plus tenant/audit columns consistent with the
codebase. Gives a real audit trail of every retry (serves "effect execution history" +
failed-effect diagnosis).

### Enum additions (int-stored — no schema change)

- `EffectExecutionMode`: add `Deferred`.
- `CompletionEffectStatus`: add `Scheduled` (waiting for `ScheduledFor`), `Retrying`, `ManualReview`
  (retries exhausted — needs a human).
- `CompletionRunStatus`: add `AwaitingDeferred` (inline effects done; deferred ones still pending).

All defaults preserve current behavior (`MaxAttempts=1`, no `ScheduledFor`, no `Deferred` effects).
Existing rows and runs are unaffected.

## Execution flow & transaction safety

### At approval — `CompletionEngine` (minimal change)

1. **Phase A** builds intents and persists the run + effect rows (as today). Each effect is tagged inline
   vs. deferred from its definition. Deferred effects are stored with `Status = Scheduled` (future
   `ScheduledFor`) or `Pending` (ASAP), a deterministic `IdempotencyKey`, and their `MaxAttempts`.
   **Committed with the approval.**
2. **Phase B** runs **only inline** (`Transactional` / `Asynchronous`) effects inside the transaction —
   exactly as today.
3. **Run status:** all-inline and done → `Completed` (unchanged). Any deferred remaining →
   `AwaitingDeferred`.
4. **Inline failure:** rollback + `PersistFailureAsync` as today; deferred effects (still pending) are set
   to `Cancelled` so they never run for a failed completion.

### At the worker — `ScheduledEffectDrainer` (mirrors `EmailQueueDrainer`)

1. Poll (~60s). **Claim** a batch of *due* deferred effects across tenants (`IgnoreQueryFilters`) where
   status ∈ {`Scheduled`,`Pending`,`Retrying`}, `ScheduledFor <= now`, `NextAttemptAt <= now`,
   `Attempts < MaxAttempts`, and the lease is free — using Postgres **`FOR UPDATE SKIP LOCKED`** plus the
   lease columns so two workers never grab the same row.
2. For each row: `IBackgroundExecutionContext.Begin(row.TenantId, actorUserId, …)`, resolve the executor,
   and run **the executor mutation and the `Status → Completed` update in ONE transaction**. This is the
   effectively-once guarantee:
   - commit ⇒ applied exactly once;
   - crash before commit ⇒ nothing applied, lease expires, row reclaimed and retried;
   - crash after commit ⇒ row is `Completed`, never retried.
3. One row failing never aborts the batch (per-row try/catch), just like the email drainer.

### Transaction boundaries (explicit)

- Inline effects remain atomic with the approval.
- The enqueue of deferred effects is atomic with the approval (outbox).
- Each deferred effect is atomic **within its own** worker transaction. There is no cross-effect
  transaction on the worker — deferred effects succeed or retry independently.

## Idempotency, retries, recovery, history

- **Idempotency.** The effect row is the unit of work; `IdempotencyKey = CompletionEffect.Id`. The
  same-transaction commit of mutation+status yields effectively-once for DB effects. The key is exposed on
  `EffectContext` so executors touching external systems can dedupe; the unique index is a hard backstop.
- **Safe retries.** A pure decision function (mirroring `EmailDeliveryDecision.Apply`) handles a throw:
  increment `Attempts`, set `NextAttemptAt = now + 1min · 2^(n-1)` (capped), status `Retrying`. When
  `Attempts` reaches `MaxAttempts` → `ManualReview` + admin notification (reusing the existing
  failure-notification path). An optional `NonRetryableEffectException` short-circuits to `ManualReview`.
- **Failed-effect recovery** (admin, permission-gated with an existing admin permission — no new perm):
  - `GET  /api/requests/effects/attention` — list `ManualReview` / failed deferred effects.
  - `POST /api/requests/effects/{id}/retry` — clear lease, `NextAttemptAt = now`, status → `Pending`.
  - `POST /api/requests/effects/{id}/skip`  — status → `Skipped` with a reason.
- **History.** Every attempt lands in `engine_effect_attempts`; the effect row carries current status,
  attempt count, last failure, and target record; the request **timeline** gets events when a deferred
  effect completes / needs review (reusing `ITimelineEngine`) so it is visible on the request.

## Compatibility

- No existing executor changes. No change to `Transactional` or `Asynchronous` dispatch.
  `Notification.Send` is untouched (it already has a durable queue + retry).
- All new columns default to current behavior; existing runs/effects are unaffected.
- **Target: all 220 Platform tests stay green at every commit.**

## Pilot (proves the whole path end-to-end)

A date-effective `Employee.UpdateField` configured as `Deferred` with `__effectiveOn` — reuses the
existing, already-tested executor (no new business logic). It exercises: schedule → claim on date →
execute once → idempotent on re-poll → recoverable when forced to fail.

## Delivery plan (one focused commit per stable, tested piece; push to `origin` + `sanad` before the next)

1. Migrations A/B/C + entity/enum changes (schema foundation).
2. `CompletionEngine` split — recognize/enqueue deferred effects; `AwaitingDeferred`; cancel-on-failure.
3. Worker — `ScheduledEffectDrainer` + pure decision + `ScheduledEffectHostedService` + leasing
   (`FOR UPDATE SKIP LOCKED`) + DI registration + `IBackgroundExecutionContext`.
4. Idempotency + retry/backoff + `ManualReview` terminal state.
5. Recovery endpoints + timeline events + permission gating.
6. Pilot deferred effect + end-to-end test.
7. Deploy — apply migrations (temporary firewall-rule dance, then delete it) + zip-redeploy the API.
   **User-gated**, as always.

## Testing

Follow existing conventions (xUnit + FluentAssertions, in-memory `ApplicationDbContext` + `FakeUser`,
`EffectContext` built from a serialized payload dictionary — as in `EmployeeUpdateFieldExecutorTests`).
New coverage: the pure retry/backoff decision; `CompletionEngine` deferred-split + cancel-on-failure; the
drainer's claim/lease/execute/idempotent-reclaim behavior; recovery endpoints; the end-to-end pilot.

## Risks & mitigations

- **Double execution on crash** → mutation + status commit in one transaction; lease + `SKIP LOCKED`;
  unique `IdempotencyKey`.
- **Behavior drift for existing effects** → additive `Deferred` mode; existing modes and executors
  untouched; full suite green each commit.
- **Worker starvation / stuck lease** → lease has an expiry; expired leases are reclaimable.
- **Prod migration access** → documented firewall-rule dance; create temp rule, apply, delete rule.

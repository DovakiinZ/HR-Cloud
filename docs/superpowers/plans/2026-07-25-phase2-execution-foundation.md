# Phase 2 — Execution Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add durable, date-effective, retryable "deferred" completion effects — executed by a background worker with effectively-once idempotency and operator recovery — without changing any existing effect behavior.

**Architecture:** A new additive `EffectExecutionMode.Deferred` is enqueued as durable `engine_completion_effects` rows in the same commit as the approval (transactional outbox). A hosted `BackgroundService` polls due rows, leases them, runs each in its own transaction through the existing `IEffectExecutorRegistry`, and applies a pure retry/backoff decision (→ `Retrying`, then `ManualReview`). Recovery endpoints let an admin retry/skip stuck effects.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL), xUnit + FluentAssertions, `Microsoft.EntityFrameworkCore.InMemory` for unit tests, `BackgroundService` hosted services.

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-25-phase2-execution-foundation-design.md` (authoritative).
- **Do NOT rebuild the request/effect engine.** Reuse `IEffectExecutor`, `IEffectExecutorRegistry`, `EffectContext`, `EffectExecutionResult`. No executor changes.
- **Compatibility:** existing `Transactional` / `Asynchronous` dispatch and `Notification.Send` are untouched. All new columns default to today's behavior (`MaxAttempts=1`, no `ScheduledFor`, no `Deferred`). **All existing Platform tests (220) must stay green at every commit.**
- **Retry defaults:** `MaxAttempts = 5`; backoff `NextAttemptAt = now + 1min · 2^(attempts-1)`, capped at 60 min; then `ManualReview`.
- **Scheduling:** date-level; `ScheduledFor` is `timestamptz` (nullable = ASAP). Worker poll interval 60s.
- **Commits:** one focused commit per task, clear message, ending with the trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. **Push to BOTH remotes (`origin` and `sanad`) before starting the next task.**
- **Build/test commands** (run from `D:\HR-Cloud-main\HR-Cloud-main\backend`):
  - Build: `dotnet build HR.sln -c Debug`
  - Platform tests: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
- **Deploy is user-gated** (Task 8 is instructions only — do not run it unprompted).

## File Structure

**Domain / enums (HR.Domain):**
- Modify `src/HR.Domain/Enums/RequestEffectEnums.cs` — add `EffectExecutionMode.Deferred`.
- Modify `src/HR.Domain/Enums/CompletionEnums.cs` — add `CompletionEffectStatus.{Scheduled,Retrying,ManualReview}` and `CompletionRunStatus.AwaitingDeferred`.
- Modify `src/HR.Domain/Engines/Completion/CompletionEffect.cs` — add scheduling/lease columns.
- Modify `src/HR.Domain/Engines/Requests/RequestEffectDefinition.cs` — add `MaxAttempts`.
- Create `src/HR.Domain/Engines/Completion/EffectAttempt.cs` — per-attempt history row.

**Application (HR.Application):**
- Modify `src/HR.Application/Engines/Completion/EffectIntent.cs` — carry mode/schedule/attempts.
- Modify `src/HR.Application/Engines/Completion/EffectContext.cs` — expose `IdempotencyKey`.
- Create `src/HR.Application/Engines/Completion/ScheduledEffectDecision.cs` — pure retry/backoff decision.
- Create `src/HR.Application/Engines/Completion/IScheduledEffectDrainer.cs` — worker contract.
- Create `src/HR.Application/Engines/Completion/NonRetryableEffectException.cs` — permanent-failure marker.

**Infrastructure (HR.Infrastructure):**
- Modify `src/HR.Infrastructure/Persistence/Configurations/Engines/CompletionConfigurations.cs` — map new columns/table.
- Modify `src/HR.Infrastructure/Persistence/ApplicationDbContext.cs` — add `DbSet<EffectAttempt>`.
- Create migration under `src/HR.Infrastructure/Migrations/` (generated).

**Platform module (HR.Modules.Platform):**
- Modify `src/HR.Modules/Platform/Services/Completion/CompletionEffectFactory.cs` — populate deferred metadata on intents.
- Modify `src/HR.Modules/Platform/Services/Completion/CompletionEngine.cs` — split inline vs deferred; `AwaitingDeferred`; cancel-on-failure.
- Create `src/HR.Modules/Platform/Services/Completion/ScheduledEffectDrainer.cs` — claim/execute/persist.
- Create `src/HR.Modules/Platform/Services/Completion/IScheduledEffectRecoveryService.cs` + `ScheduledEffectRecoveryService.cs` — list/retry/skip.
- Modify `src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` — register drainer + recovery service.
- Modify `src/HR.Modules/Platform/Controllers/RequestsController.cs` — recovery endpoints.

**API host (HR.Api):**
- Create `src/HR.Api/Services/ScheduledEffectHostedService.cs` — 60s poll.
- Modify `src/HR.Api/Program.cs` — register hosted service.

**Tests (tests/HR.Modules.Platform.Tests):**
- Create `Completion/ScheduledEffectDecisionTests.cs`
- Create `Completion/CompletionEngineDeferredTests.cs`
- Create `Completion/ScheduledEffectDrainerTests.cs`
- Create `Completion/ScheduledEffectRecoveryServiceTests.cs`
- Create `Completion/DeferredEffectPilotTests.cs`

---

## Task 1: Schema foundation — entities, enums, migration

**Files:**
- Modify: `src/HR.Domain/Enums/RequestEffectEnums.cs:24-27`
- Modify: `src/HR.Domain/Enums/CompletionEnums.cs:4-22`
- Modify: `src/HR.Domain/Engines/Completion/CompletionEffect.cs`
- Modify: `src/HR.Domain/Engines/Requests/RequestEffectDefinition.cs`
- Create: `src/HR.Domain/Engines/Completion/EffectAttempt.cs`
- Modify: `src/HR.Infrastructure/Persistence/Configurations/Engines/CompletionConfigurations.cs`
- Modify: `src/HR.Infrastructure/Persistence/ApplicationDbContext.cs` (add DbSet)
- Migration generated into `src/HR.Infrastructure/Migrations/`

**Interfaces:**
- Produces: `EffectExecutionMode.Deferred`; `CompletionEffectStatus.{Scheduled,Retrying,ManualReview}`; `CompletionRunStatus.AwaitingDeferred`; new `CompletionEffect` props `ScheduledFor`, `NextAttemptAt`, `MaxAttempts`, `IdempotencyKey`, `LeasedUntil`, `LeasedBy`; `RequestEffectDefinition.MaxAttempts`; `EffectAttempt` entity + `ApplicationDbContext.EffectAttempts`.

- [ ] **Step 1: Add the new enum values**

In `src/HR.Domain/Enums/RequestEffectEnums.cs`, extend `EffectExecutionMode`:
```csharp
public enum EffectExecutionMode
{
    Transactional = 1,
    Asynchronous = 2,

    /// <summary>Not run at approval. Enqueued as a durable completion effect and executed later by the
    /// scheduled-effect worker — on its effective date, with idempotency, retry and operator recovery.</summary>
    Deferred = 3,
}
```

In `src/HR.Domain/Enums/CompletionEnums.cs`, extend both enums:
```csharp
public enum CompletionRunStatus
{
    Pending = 1,
    Executing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,

    /// <summary>Inline effects finished; one or more deferred effects are still pending on the worker.</summary>
    AwaitingDeferred = 6,
}

public enum CompletionEffectStatus
{
    Pending = 1,
    Executing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Skipped = 6,

    /// <summary>Deferred effect waiting for its ScheduledFor date.</summary>
    Scheduled = 7,
    /// <summary>Deferred effect failed and is awaiting its next retry attempt.</summary>
    Retrying = 8,
    /// <summary>Deferred effect exhausted its retries — needs a human.</summary>
    ManualReview = 9,
}
```

- [ ] **Step 2: Add columns to `CompletionEffect`**

In `src/HR.Domain/Engines/Completion/CompletionEffect.cs`, add after `public int Attempts { get; set; }`:
```csharp
    // ── Phase 2: durable deferred execution ──────────────────────────────────────
    /// <summary>Effective date for a deferred effect. Null = run as soon as the worker sees it.</summary>
    public DateTime? ScheduledFor { get; set; }
    /// <summary>Retry gate: the worker will not attempt this effect before this time.</summary>
    public DateTime? NextAttemptAt { get; set; }
    /// <summary>Total attempts allowed before ManualReview. 1 = no retry (default, inline behavior).</summary>
    public int MaxAttempts { get; set; } = 1;
    /// <summary>Effectively-once backstop; set to the effect's Id for deferred effects.</summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>Worker lease expiry; a row leased past this time may be reclaimed.</summary>
    public DateTime? LeasedUntil { get; set; }
    /// <summary>Identifier of the worker that currently holds the lease.</summary>
    public string? LeasedBy { get; set; }
```

- [ ] **Step 3: Add `MaxAttempts` to `RequestEffectDefinition`**

In `src/HR.Domain/Engines/Requests/RequestEffectDefinition.cs`, add after the `ExecutionMode` property:
```csharp
    /// <summary>Retries allowed when this effect runs deferred. 1 = no retry. Ignored for inline modes.</summary>
    public int MaxAttempts { get; set; } = 1;
```

- [ ] **Step 4: Create the `EffectAttempt` entity**

Create `src/HR.Domain/Engines/Completion/EffectAttempt.cs`:
```csharp
using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Completion;

/// <summary>One recorded attempt to execute a deferred completion effect: which attempt number, when it
/// started, how it ended, and why it failed. Gives a full audit trail across retries.</summary>
public class EffectAttempt : TenantEntity
{
    public Guid CompletionEffectId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime StartedAt { get; set; }
    public CompletionEffectStatus Status { get; set; }
    public int? DurationMs { get; set; }
    public string? FailureReason { get; set; }

    public CompletionEffect Effect { get; set; } = null!;
}
```

- [ ] **Step 5: Map the new columns + table in EF configuration**

In `src/HR.Infrastructure/Persistence/Configurations/Engines/CompletionConfigurations.cs`, inside `CompletionEffectConfiguration.Configure`, add before the closing brace:
```csharp
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.LeasedBy).HasMaxLength(100);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        // The worker's "due" query filters on these; index keeps polling cheap.
        builder.HasIndex(x => new { x.Status, x.ScheduledFor, x.NextAttemptAt });
```
Then append a new configuration class in the same file:
```csharp
public class EffectAttemptConfiguration : IEntityTypeConfiguration<EffectAttempt>
{
    public void Configure(EntityTypeBuilder<EffectAttempt> builder)
    {
        builder.ToTable("engine_effect_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.HasIndex(x => x.CompletionEffectId);
        builder.HasIndex(x => x.TenantId);

        builder.HasOne(x => x.Effect)
            .WithMany()
            .HasForeignKey(x => x.CompletionEffectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 6: Add the DbSet**

In `src/HR.Infrastructure/Persistence/ApplicationDbContext.cs`, next to the existing completion DbSets (around lines 137-139), add:
```csharp
    public DbSet<HR.Domain.Engines.Completion.EffectAttempt> EffectAttempts => Set<HR.Domain.Engines.Completion.EffectAttempt>();
```

- [ ] **Step 7: Build to verify domain + config compile**

Run: `dotnet build HR.sln -c Debug`
Expected: Build succeeded (0 errors). New columns/DbSet compile; unique-nullable index on `IdempotencyKey` is valid (Postgres allows multiple NULLs).

- [ ] **Step 8: Generate the migration**

Run (from `backend`):
```bash
dotnet ef migrations add Phase2DeferredEffects --project src/HR.Infrastructure --startup-project src/HR.Api
```
Expected: creates `src/HR.Infrastructure/Migrations/<timestamp>_Phase2DeferredEffects.cs`. Open it and confirm the `Up` adds the six `engine_completion_effects` columns, `MaxAttempts` on `engine_request_effect_definitions`, the unique index on `IdempotencyKey`, the `(Status, ScheduledFor, NextAttemptAt)` index, and the new `engine_effect_attempts` table. **Do not apply it** (Task 8 applies to Azure, user-gated).

- [ ] **Step 9: Build again + run the full Platform suite (nothing should change behaviorally)**

Run: `dotnet build HR.sln -c Debug` then `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: Build succeeded; all existing tests pass (enum/column additions are backward-compatible).

- [ ] **Step 10: Commit + push**

```bash
git add src/HR.Domain src/HR.Infrastructure
git commit -m "feat(requests): Phase 2 schema — deferred-effect columns, statuses, attempts table

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 2: Carry deferred metadata from factory to engine

**Files:**
- Modify: `src/HR.Application/Engines/Completion/EffectIntent.cs`
- Modify: `src/HR.Application/Engines/Completion/EffectContext.cs`
- Modify: `src/HR.Modules/Platform/Services/Completion/CompletionEffectFactory.cs:186-196`
- Test: `tests/HR.Modules.Platform.Tests/Completion/CompletionEffectFactoryDeferredTests.cs`

**Interfaces:**
- Consumes: `EffectExecutionMode` (Task 1).
- Produces: `EffectIntent(string EffectType, int Sequence, string Payload, EffectExecutionMode Mode = Transactional, DateTime? ScheduledFor = null, int MaxAttempts = 1)`; `EffectContext.IdempotencyKey` (nullable string); reserved payload key `__effectiveOn` read by the factory.

- [ ] **Step 1: Extend `EffectIntent`**

Replace `src/HR.Application/Engines/Completion/EffectIntent.cs` body record with:
```csharp
namespace HR.Application.Engines.Completion;

/// <summary>One resolved intent to change, ready to persist as a CompletionEffect. Mode/ScheduledFor/
/// MaxAttempts are defaulted so existing (inline) call sites are unaffected.</summary>
public sealed record EffectIntent(
    string EffectType,
    int Sequence,
    string Payload,
    EffectExecutionMode Mode = EffectExecutionMode.Transactional,
    DateTime? ScheduledFor = null,
    int MaxAttempts = 1);
```
(Add `using HR.Domain.Enums;` at the top if not present.)

- [ ] **Step 2: Expose `IdempotencyKey` on `EffectContext`**

In `src/HR.Application/Engines/Completion/EffectContext.cs`, add after `public Guid? ActorUserId { get; init; }`:
```csharp
    /// <summary>Stable per-effect key for deferred execution; executors touching external systems may use
    /// it to dedupe. Null for inline effects.</summary>
    public string? IdempotencyKey { get; init; }
```

- [ ] **Step 3: Write the failing test for factory deferred mapping**

Create `tests/HR.Modules.Platform.Tests/Completion/CompletionEffectFactoryDeferredTests.cs`. It builds a request type with one **Deferred** `RequestEffectDefinition` (MaxAttempts 5) whose `ConfigurationJson` maps `field` → a form value and `__effectiveOn` → a form date, submits a request, and asserts the produced intent carries `Mode = Deferred`, `MaxAttempts = 5`, and a `ScheduledFor` equal to the resolved date.
```csharp
using FluentAssertions;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

public class CompletionEffectFactoryDeferredTests
{
    [Fact]
    public async Task Deferred_definition_produces_intent_with_mode_schedule_and_attempts()
    {
        await using var h = await DeferredFactoryHarness.CreateAsync(
            effectType: "Employee.UpdateField",
            mode: EffectExecutionMode.Deferred,
            maxAttempts: 5,
            config: """
            {"field":{"source":1,"key":"fieldCode"},
             "newValue":{"source":1,"key":"newValue"},
             "__effectiveOn":{"source":1,"key":"effectiveOn"}}
            """,
            formValues: new() { ["fieldCode"] = "jobTitle", ["newValue"] = "Manager", ["effectiveOn"] = "2026-09-01" });

        var intents = await h.Factory.BuildAsync(h.RequestInstanceId, default);

        intents.Should().HaveCount(1);
        intents[0].Mode.Should().Be(EffectExecutionMode.Deferred);
        intents[0].MaxAttempts.Should().Be(5);
        intents[0].ScheduledFor!.Value.Date.Should().Be(new DateTime(2026, 9, 1));
    }
}
```
> Implementer note: `DeferredFactoryHarness` is a small in-memory helper (in the same file) that seeds an `ApplicationDbContext` with a `RequestType`, a `RequestEffectDefinition`, a `RequestInstance` + `FormSubmission`/`FormSubmissionValues`, and constructs `CompletionEffectFactory` with fakes for `ILeaveService`/`ICurrentUserService`. Model it on the existing `EmployeeUpdateFieldExecutorTests` in-memory setup (`UseInMemoryDatabase`, `FakeUser`). Seed one row per `formValues` entry.

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~CompletionEffectFactoryDeferredTests`
Expected: FAIL — the factory currently emits intents with the default `Transactional` mode and no schedule.

- [ ] **Step 5: Populate deferred metadata in the factory**

In `src/HR.Modules/Platform/Services/Completion/CompletionEffectFactory.cs`, replace the loop body in `BuildFromDefinitionsAsync` (lines ~186-193) with:
```csharp
        var seq = 0;
        foreach (var def in definitions)
        {
            var config = EffectConfiguration.TryParse(def.ConfigurationJson);
            if (config is null) continue;   // malformed configuration: nothing safe to run

            var payload = EffectValueResolver.Resolve(config, ctx);

            DateTime? scheduledFor = null;
            if (def.ExecutionMode == EffectExecutionMode.Deferred
                && payload.TryGetValue("__effectiveOn", out var eff)
                && DateTime.TryParse(eff?.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var d))
            {
                scheduledFor = DateTime.SpecifyKind(d, DateTimeKind.Utc);
            }

            intents.Add(new EffectIntent(
                def.EffectType, ++seq, Serialize(payload),
                def.ExecutionMode, scheduledFor, def.MaxAttempts));
        }
```
> If `EffectValueResolver.Resolve` returns a type whose `TryGetValue` differs (e.g. `Dictionary<string, object?>`), adapt the accessor to that type — check the resolver's return signature. The `__effectiveOn` value stays in the payload; executors ignore unknown keys.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~CompletionEffectFactoryDeferredTests`
Expected: PASS.

- [ ] **Step 7: Run the full Platform suite (legacy intents unchanged)**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: all pass. The impact-mapping path still emits default `Transactional` intents.

- [ ] **Step 8: Commit + push**

```bash
git add src/HR.Application src/HR.Modules tests
git commit -m "feat(requests): carry deferred mode/schedule/attempts on effect intents

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 3: CompletionEngine split — enqueue deferred, don't run inline

**Files:**
- Modify: `src/HR.Modules/Platform/Services/Completion/CompletionEngine.cs`
- Test: `tests/HR.Modules.Platform.Tests/Completion/CompletionEngineDeferredTests.cs`

**Interfaces:**
- Consumes: `EffectIntent.Mode/ScheduledFor/MaxAttempts` (Task 2); `CompletionEffectStatus.Scheduled`, `CompletionRunStatus.AwaitingDeferred` (Task 1).
- Produces: after `ExecuteAsync`, deferred intents are persisted as `CompletionEffect` rows with `Status = Scheduled` (future) or `Pending` (ASAP), `IdempotencyKey = effect.Id`, `ScheduledFor`, `MaxAttempts`, and are **not** executed inline; the run is `AwaitingDeferred` if any deferred remain, else `Completed`. On inline failure, pending deferred effects become `Cancelled`.

- [ ] **Step 1: Write failing tests for the split**

Create `tests/HR.Modules.Platform.Tests/Completion/CompletionEngineDeferredTests.cs` with three facts:
1. `Deferred_effect_is_persisted_not_executed_and_run_awaits` — a run with one Deferred intent leaves the effect `Scheduled`/`Pending` (never `Completed`), sets `IdempotencyKey`, and the run status is `AwaitingDeferred`.
2. `Mixed_run_executes_inline_and_defers_the_rest` — one inline (Transactional) + one Deferred: inline is `Completed`, deferred is `Scheduled`/`Pending`, run is `AwaitingDeferred`.
3. `Inline_failure_cancels_pending_deferred_effects` — inline effect throws → inline run fails and the deferred effect is `Cancelled` (not left runnable).

Build these on the existing CompletionEngine test harness if one exists; otherwise seed an in-memory `ApplicationDbContext`, register a fake executor via a stub `IEffectExecutorRegistry`, and stub `ICompletionEffectFactory` to return the intents under test. Suppress the in-memory transaction warning in the context options:
```csharp
new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
    .Options;
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~CompletionEngineDeferredTests`
Expected: FAIL — today every intent is executed inline in Phase B.

- [ ] **Step 3: Persist deferred metadata in Phase A**

In `CompletionEngine.ExecuteAsync`, replace the Phase-A effect-materialization loop (lines 76-84) with:
```csharp
        foreach (var intent in intents.OrderBy(i => i.Sequence))
        {
            var deferred = intent.Mode == EffectExecutionMode.Deferred;
            var effect = new CompletionEffect
            {
                RequestInstanceId = requestInstanceId,
                EffectType = intent.EffectType,
                Sequence = intent.Sequence,
                Payload = intent.Payload,
                MaxAttempts = deferred ? Math.Max(1, intent.MaxAttempts) : 1,
                ScheduledFor = deferred ? intent.ScheduledFor : null,
                Status = deferred
                    ? (intent.ScheduledFor is { } when && when > DateTime.UtcNow
                        ? CompletionEffectStatus.Scheduled
                        : CompletionEffectStatus.Pending)
                    : CompletionEffectStatus.Pending,
            };
            run.Effects.Add(effect);
        }
        _db.CompletionRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // Deferred effects get their Id as idempotency key now that they are persisted.
        foreach (var e in run.Effects.Where(e => e.Status is CompletionEffectStatus.Scheduled or CompletionEffectStatus.Pending))
            if (IsDeferred(run, e)) e.IdempotencyKey = e.Id.ToString();
```
> Simpler: track deferred effect ids in a local `HashSet<int>` of sequences while building, and set `IdempotencyKey` for those. Replace the `IsDeferred` helper with a check against that set to avoid re-deriving. Keep it readable — the intent for each effect is known in the loop, so capture deferred sequences there.

Cleaner form — build a set in the loop:
```csharp
        var deferredSequences = new HashSet<int>();
        foreach (var intent in intents.OrderBy(i => i.Sequence))
        {
            var deferred = intent.Mode == EffectExecutionMode.Deferred;
            if (deferred) deferredSequences.Add(intent.Sequence);
            run.Effects.Add(new CompletionEffect { /* ...as above... */ });
        }
        _db.CompletionRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        foreach (var e in run.Effects.Where(e => deferredSequences.Contains(e.Sequence)))
            e.IdempotencyKey = e.Id.ToString();
```

- [ ] **Step 4: Run only inline effects in Phase B**

In Phase B, change the ordered set to inline-only and preserve deferred rows:
```csharp
        var ordered = run.Effects
            .Where(e => !deferredSequences.Contains(e.Sequence))
            .OrderBy(e => e.Sequence).ToList();
        var deferredEffects = run.Effects.Where(e => deferredSequences.Contains(e.Sequence)).ToList();
```
Leave the existing inline execution loop over `ordered` unchanged. After the loop succeeds, set the run status based on whether deferred effects remain:
```csharp
            overall.Stop();
            run.Status = deferredEffects.Count > 0
                ? CompletionRunStatus.AwaitingDeferred
                : CompletionRunStatus.Completed;
            run.CompletedAt = deferredEffects.Count > 0 ? null : DateTime.UtcNow;
            run.DurationMs = (int)overall.ElapsedMilliseconds;
```
(Keep the timeline publish + `SaveChangesAsync` + `CommitAsync` that follow. The empty-effects early return at lines 89-96 still applies when there are no effects at all.)

- [ ] **Step 5: Cancel pending deferred effects on inline failure**

In `PersistFailureAsync`, the existing loop already sets non-completed effects to `Cancelled`. Confirm deferred effects (status `Scheduled`/`Pending`) fall into the `else if (e.Status != CompletionEffectStatus.Completed)` branch and become `Cancelled`. No change needed if so; if the branch checks specific statuses, broaden it to cancel any not-`Completed`, not-`Skipped` effect.

- [ ] **Step 6: Run the new tests to verify they pass**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~CompletionEngineDeferredTests`
Expected: PASS.

- [ ] **Step 7: Run the full Platform suite**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: all pass. Runs with only inline effects still land `Completed` exactly as before.

- [ ] **Step 8: Commit + push**

```bash
git add src/HR.Modules tests
git commit -m "feat(requests): CompletionEngine defers deferred effects instead of running inline

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 4: Pure retry/backoff decision

**Files:**
- Create: `src/HR.Application/Engines/Completion/ScheduledEffectDecision.cs`
- Create: `src/HR.Application/Engines/Completion/NonRetryableEffectException.cs`
- Test: `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectDecisionTests.cs`

**Interfaces:**
- Consumes: `CompletionEffect`, `CompletionEffectStatus`, `EffectExecutionResult` (existing).
- Produces:
  - `ScheduledEffectDecision.ApplySuccess(CompletionEffect row, EffectExecutionResult result, DateTime nowUtc)`
  - `ScheduledEffectDecision.ApplyFailure(CompletionEffect row, string error, bool permanent, DateTime nowUtc, TimeSpan baseBackoff, TimeSpan maxBackoff)`
  - `NonRetryableEffectException : Exception`.

- [ ] **Step 1: Write the failing tests**

Create `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectDecisionTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

public class ScheduledEffectDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Base = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(60);

    [Fact]
    public void Success_marks_completed_and_clears_lease()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5, LeasedBy = "w1", LeasedUntil = Now };
        ScheduledEffectDecision.ApplySuccess(row, EffectExecutionResult.Ok(targetEntityType: "Employee"), Now);
        row.Status.Should().Be(CompletionEffectStatus.Completed);
        row.ExecutedAt.Should().Be(Now);
        row.LeasedBy.Should().BeNull();
        row.LeasedUntil.Should().BeNull();
        row.TargetEntityType.Should().Be("Employee");
    }

    [Fact]
    public void Skip_marks_skipped_with_reason()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplySuccess(row, EffectExecutionResult.Skip(EffectSkipReasons.NothingToDo), Now);
        row.Status.Should().Be(CompletionEffectStatus.Skipped);
        row.FailureReason.Should().Be(EffectSkipReasons.NothingToDo);
    }

    [Fact]
    public void Failure_below_cap_schedules_exponential_backoff_retry()
    {
        var row = new CompletionEffect { Attempts = 2, MaxAttempts = 5, LeasedBy = "w1", LeasedUntil = Now };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.Retrying);
        row.NextAttemptAt.Should().Be(Now.AddMinutes(2)); // 1min * 2^(2-1)
        row.LeasedBy.Should().BeNull();
        row.FailureReason.Should().Be("boom");
    }

    [Fact]
    public void Failure_at_cap_goes_to_manual_review()
    {
        var row = new CompletionEffect { Attempts = 5, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview);
        row.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Permanent_failure_goes_straight_to_manual_review()
    {
        var row = new CompletionEffect { Attempts = 1, MaxAttempts = 5 };
        ScheduledEffectDecision.ApplyFailure(row, "bad config", permanent: true, Now, Base, Max);
        row.Status.Should().Be(CompletionEffectStatus.ManualReview);
    }

    [Fact]
    public void Backoff_is_capped()
    {
        var row = new CompletionEffect { Attempts = 10, MaxAttempts = 20 };
        ScheduledEffectDecision.ApplyFailure(row, "boom", permanent: false, Now, Base, Max);
        row.NextAttemptAt.Should().Be(Now.Add(Max));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectDecisionTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create the marker exception**

Create `src/HR.Application/Engines/Completion/NonRetryableEffectException.cs`:
```csharp
namespace HR.Application.Engines.Completion;

/// <summary>Thrown by an executor to signal a permanent failure — the worker sends the effect straight to
/// ManualReview instead of retrying (e.g. invalid configuration, a whitelist rejection).</summary>
public sealed class NonRetryableEffectException : Exception
{
    public NonRetryableEffectException(string message) : base(message) { }
}
```

- [ ] **Step 4: Implement the pure decision**

Create `src/HR.Application/Engines/Completion/ScheduledEffectDecision.cs`:
```csharp
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;

namespace HR.Application.Engines.Completion;

/// <summary>Pure: applies an execution outcome to a deferred completion-effect row. No I/O — the drainer
/// owns persistence. Mirrors the shape of EmailDeliveryDecision.</summary>
public static class ScheduledEffectDecision
{
    public static void ApplySuccess(CompletionEffect row, EffectExecutionResult result, DateTime nowUtc)
    {
        row.Status = result.IsSkipped ? CompletionEffectStatus.Skipped : CompletionEffectStatus.Completed;
        if (result.IsSkipped) row.FailureReason = result.SkipReason;
        row.ExecutedAt = nowUtc;
        row.TargetEntityType = result.TargetEntityType;
        row.TargetRecordId = result.TargetRecordId;
        ClearLease(row);
    }

    public static void ApplyFailure(
        CompletionEffect row, string error, bool permanent, DateTime nowUtc,
        TimeSpan baseBackoff, TimeSpan maxBackoff)
    {
        row.FailureReason = Truncate(error, 2000);
        row.ExecutedAt = nowUtc;
        ClearLease(row);

        if (permanent || row.Attempts >= row.MaxAttempts)
        {
            row.Status = CompletionEffectStatus.ManualReview;
            row.NextAttemptAt = null;
            return;
        }

        row.Status = CompletionEffectStatus.Retrying;
        var factor = Math.Pow(2, Math.Max(0, row.Attempts - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(baseBackoff.Ticks * factor, maxBackoff.Ticks));
        row.NextAttemptAt = nowUtc.Add(delay);
    }

    private static void ClearLease(CompletionEffect row) { row.LeasedBy = null; row.LeasedUntil = null; }
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectDecisionTests`
Expected: PASS (all 6).

- [ ] **Step 6: Commit + push**

```bash
git add src/HR.Application tests
git commit -m "feat(requests): pure retry/backoff decision for deferred effects

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 5: The scheduled-effect worker (drainer + hosted service)

**Files:**
- Create: `src/HR.Application/Engines/Completion/IScheduledEffectDrainer.cs`
- Create: `src/HR.Modules/Platform/Services/Completion/ScheduledEffectDrainer.cs`
- Create: `src/HR.Api/Services/ScheduledEffectHostedService.cs`
- Modify: `src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` (register drainer)
- Modify: `src/HR.Api/Program.cs:85` (register hosted service)
- Test: `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectDrainerTests.cs`

**Interfaces:**
- Consumes: `IEffectExecutorRegistry`, `EffectContext`, `IBackgroundExecutionContext`, `ScheduledEffectDecision` (Task 4), completion columns (Task 1).
- Produces: `IScheduledEffectDrainer.DrainAsync(CancellationToken) : Task<int>` (returns number of effects that reached a terminal/retry outcome this tick).

- [ ] **Step 1: Define the drainer contract**

Create `src/HR.Application/Engines/Completion/IScheduledEffectDrainer.cs`:
```csharp
namespace HR.Application.Engines.Completion;

/// <summary>Claims and executes due deferred completion effects, one worker tick. Returns how many effects
/// were processed (completed, skipped, retried, or sent to manual review) this tick.</summary>
public interface IScheduledEffectDrainer
{
    Task<int> DrainAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing drainer tests**

Create `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectDrainerTests.cs` with facts covering the core guarantees. Use an in-memory `ApplicationDbContext` (with the `TransactionIgnoredWarning` suppression from Task 3), seed a `CompletionRun` + `CompletionEffect` rows, and register fake executors via a stub `IEffectExecutorRegistry`. Provide a fake `IBackgroundExecutionContext` whose `Begin` returns a no-op `IDisposable`.
1. `Runs_a_due_pending_effect_once_and_marks_completed` — a `Pending` effect with a succeeding executor becomes `Completed`; a second `DrainAsync` does nothing (idempotent — already terminal).
2. `Does_not_run_a_future_scheduled_effect` — a `Scheduled` effect with `ScheduledFor` in the future is left untouched.
3. `Runs_a_scheduled_effect_once_its_date_passes` — same row with `ScheduledFor` in the past is executed.
4. `Failing_executor_moves_to_retrying_then_manual_review` — an always-throwing executor with `MaxAttempts=2` yields `Retrying` (attempt 1, NextAttemptAt set); after clearing `NextAttemptAt` and draining again, `ManualReview`.
5. `Permanent_failure_skips_retries` — an executor throwing `NonRetryableEffectException` goes straight to `ManualReview`.
6. `Writes_an_effect_attempt_row_per_attempt` — after a run, an `EffectAttempts` row exists for the effect with `AttemptNumber == effect.Attempts`.

> The drainer must expose enough seams to test without real transactions/time: inject a `Func<DateTime>` clock (default `() => DateTime.UtcNow`) and a `workerId` string. Passing time explicitly lets tests advance the clock rather than sleep.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectDrainerTests`
Expected: FAIL — drainer type does not exist.

- [ ] **Step 4: Implement the drainer**

Create `src/HR.Modules/Platform/Services/Completion/ScheduledEffectDrainer.cs`:
```csharp
using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Completion;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>Drains due deferred completion effects: claim a batch across tenants, execute each in its own
/// transaction through the shared executor registry, and apply the pure retry decision. One row failing
/// never aborts the batch. Mirrors EmailQueueDrainer.</summary>
public sealed class ScheduledEffectDrainer : IScheduledEffectDrainer
{
    private const int BatchSize = 25;
    private static readonly TimeSpan LeaseWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(60);

    private readonly ApplicationDbContext _db;
    private readonly IEffectExecutorRegistry _registry;
    private readonly IBackgroundExecutionContext _background;
    private readonly ILogger<ScheduledEffectDrainer> _logger;
    private readonly Func<DateTime> _clock;
    private readonly string _workerId;

    public ScheduledEffectDrainer(
        ApplicationDbContext db, IEffectExecutorRegistry registry,
        IBackgroundExecutionContext background, ILogger<ScheduledEffectDrainer> logger,
        Func<DateTime>? clock = null)
    {
        _db = db; _registry = registry; _background = background; _logger = logger;
        _clock = clock ?? (() => DateTime.UtcNow);
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    }

    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var now = _clock();

        // Claim a batch across all tenants. Due = ready-to-run status, date reached, retry gate passed,
        // attempts left, and either unleased or the lease has expired (crash recovery).
        var batch = await _db.CompletionEffects.IgnoreQueryFilters()
            .Where(e =>
                (e.Status == CompletionEffectStatus.Pending
                 || e.Status == CompletionEffectStatus.Scheduled
                 || e.Status == CompletionEffectStatus.Retrying
                 || (e.Status == CompletionEffectStatus.Executing && e.LeasedUntil != null && e.LeasedUntil < now))
                && (e.ScheduledFor == null || e.ScheduledFor <= now)
                && (e.NextAttemptAt == null || e.NextAttemptAt <= now)
                && e.Attempts < e.MaxAttempts
                && (e.LeasedUntil == null || e.LeasedUntil < now))
            .OrderBy(e => e.ScheduledFor ?? e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        // Lease the claimed rows first so an overlapping tick cannot re-grab them.
        foreach (var e in batch)
        {
            e.Status = CompletionEffectStatus.Executing;
            e.LeasedBy = _workerId;
            e.LeasedUntil = now.Add(LeaseWindow);
        }
        await _db.SaveChangesAsync(ct);

        var processed = 0;
        foreach (var effect in batch)
        {
            if (ct.IsCancellationRequested) break;
            processed += await RunOneAsync(effect, ct) ? 1 : 0;
        }
        return processed;
    }

    private async Task<bool> RunOneAsync(CompletionEffect effect, CancellationToken ct)
    {
        var tenantId = effect.TenantId;
        var startedAt = _clock();
        effect.Attempts++;

        using (_background.Begin(tenantId, null))
        {
            // Load the request context this effect belongs to (for EffectContext).
            var instance = await _db.RequestInstances.IgnoreQueryFilters()
                .Include(r => r.RequestType)
                .FirstOrDefaultAsync(r => r.Id == effect.RequestInstanceId, ct);

            EffectExecutionResult? result = null;
            string? error = null;
            var permanent = false;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var executor = _registry.Resolve(effect.EffectType);
                var context = new EffectContext
                {
                    RequestInstanceId = effect.RequestInstanceId,
                    RequestNumber = instance?.RequestNumber ?? "",
                    RequestTypeCode = instance?.RequestType?.Code ?? "",
                    EmployeeId = instance?.EmployeeId ?? Guid.Empty,
                    ActorUserId = null,
                    IdempotencyKey = effect.IdempotencyKey,
                    Payload = JsonDocument.Parse(effect.Payload).RootElement,
                };

                result = await executor.ExecuteAsync(context, ct);
                effect.ExecutorName = executor.GetType().Name;
                effect.ExecutorVersion = executor.Version;
                ScheduledEffectDecision.ApplySuccess(effect, result, _clock());
                RecordAttempt(effect, startedAt);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return true;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                error = ex.Message;
                permanent = ex is NonRetryableEffectException;
            }

            // Failure path: re-load the tracked row (cleared above) and persist the decision.
            var row = await _db.CompletionEffects.IgnoreQueryFilters().FirstAsync(e => e.Id == effect.Id, ct);
            row.Attempts = effect.Attempts;
            ScheduledEffectDecision.ApplyFailure(row, error!, permanent, _clock(), BaseBackoff, MaxBackoff);
            RecordAttempt(row, startedAt);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }

    private void RecordAttempt(CompletionEffect effect, DateTime startedAt)
    {
        _db.EffectAttempts.Add(new EffectAttempt
        {
            CompletionEffectId = effect.Id,
            AttemptNumber = effect.Attempts,
            StartedAt = startedAt,
            Status = effect.Status,
            DurationMs = (int)(_clock() - startedAt).TotalMilliseconds,
            FailureReason = effect.Status is CompletionEffectStatus.Retrying or CompletionEffectStatus.ManualReview
                ? effect.FailureReason : null,
        });
    }
}
```
> On the failure path the change-tracker is cleared to discard the executor's rolled-back mutations, so the row is re-loaded before persisting the decision — this mirrors CompletionEngine's `ChangeTracker.Clear()` after a rollback. For the in-memory test provider (no real transactions), `BeginTransactionAsync`/`Commit`/`Rollback` are no-ops but `SaveChangesAsync` still applies — the tests assert on final row state, which holds under both providers. When a test's executor throws, it must not have persisted partial state itself (fakes just throw), so the re-load returns the leased row unchanged apart from `Attempts`.

- [ ] **Step 5: Register the drainer**

In `src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`, near the completion registrations (after line 110), add:
```csharp
        services.AddScoped<HR.Application.Engines.Completion.IScheduledEffectDrainer,
            HR.Modules.Platform.Services.Completion.ScheduledEffectDrainer>();
```

- [ ] **Step 6: Run drainer tests to verify they pass**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectDrainerTests`
Expected: PASS.

- [ ] **Step 7: Create the hosted service**

Create `src/HR.Api/Services/ScheduledEffectHostedService.cs`:
```csharp
using HR.Application.Engines.Completion;

namespace HR.Api.Services;

/// <summary>Polls for due deferred completion effects every 60s and drains them. Mirrors
/// EmailDeliveryHostedService: a scope per tick, failures logged and swallowed so the loop survives.</summary>
public sealed class ScheduledEffectHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledEffectHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public ScheduledEffectHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledEffectHostedService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drainer = scope.ServiceProvider.GetRequiredService<IScheduledEffectDrainer>();
                var count = await drainer.DrainAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Scheduled-effect worker processed {Count} effect(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Scheduled-effect worker tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 8: Register the hosted service**

In `src/HR.Api/Program.cs`, after line 85 (`EmailDeliveryHostedService`), add:
```csharp
builder.Services.AddHostedService<HR.Api.Services.ScheduledEffectHostedService>();
```

- [ ] **Step 9: Build + run the full Platform suite**

Run: `dotnet build HR.sln -c Debug` then `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: Build succeeded; all tests pass.

- [ ] **Step 10: Commit + push**

```bash
git add src/HR.Application src/HR.Modules src/HR.Api tests
git commit -m "feat(requests): durable scheduled-effect worker (drainer + hosted service)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 6: Failed-effect recovery — service, endpoints, timeline

**Files:**
- Create: `src/HR.Modules/Platform/Services/Completion/IScheduledEffectRecoveryService.cs`
- Create: `src/HR.Modules/Platform/Services/Completion/ScheduledEffectRecoveryService.cs`
- Modify: `src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Modify: `src/HR.Modules/Platform/Controllers/RequestsController.cs`
- Test: `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectRecoveryServiceTests.cs`

**Interfaces:**
- Consumes: completion columns/statuses (Task 1); `ITimelineEngine` (existing).
- Produces:
  - `IScheduledEffectRecoveryService.ListAttentionAsync(CancellationToken) : Task<IReadOnlyList<AttentionEffectDto>>`
  - `RetryAsync(Guid effectId, CancellationToken) : Task<bool>` — resets a `ManualReview`/`Failed` deferred effect to `Pending` (clears lease, `NextAttemptAt = now`).
  - `SkipAsync(Guid effectId, string reason, CancellationToken) : Task<bool>` — sets `Skipped` with reason.
  - `AttentionEffectDto(Guid Id, Guid RequestInstanceId, string EffectType, int Attempts, int MaxAttempts, string? FailureReason, DateTime? ScheduledFor)`.

- [ ] **Step 1: Write the failing recovery-service tests**

Create `tests/HR.Modules.Platform.Tests/Completion/ScheduledEffectRecoveryServiceTests.cs`:
1. `Lists_only_manual_review_and_failed_deferred_effects` — seeds `ManualReview`, `Failed`, `Completed`, `Scheduled` rows; the list returns only the first two.
2. `Retry_resets_a_manual_review_effect_to_pending` — a `ManualReview` row becomes `Pending`, `NextAttemptAt <= now`, lease cleared; returns true.
3. `Retry_returns_false_for_a_completed_effect` — a `Completed` row is not resettable.
4. `Skip_marks_the_effect_skipped_with_reason` — a `ManualReview` row becomes `Skipped` with the supplied reason.

Use an in-memory context and a fake `ITimelineEngine` (records calls, returns `Task.CompletedTask`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectRecoveryServiceTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Define the contract + DTO**

Create `src/HR.Modules/Platform/Services/Completion/IScheduledEffectRecoveryService.cs`:
```csharp
namespace HR.Modules.Platform.Services.Completion;

public sealed record AttentionEffectDto(
    Guid Id, Guid RequestInstanceId, string EffectType,
    int Attempts, int MaxAttempts, string? FailureReason, DateTime? ScheduledFor);

/// <summary>Operator recovery for deferred effects that need a human: list, retry, or skip.</summary>
public interface IScheduledEffectRecoveryService
{
    Task<IReadOnlyList<AttentionEffectDto>> ListAttentionAsync(CancellationToken ct);
    Task<bool> RetryAsync(Guid effectId, CancellationToken ct);
    Task<bool> SkipAsync(Guid effectId, string reason, CancellationToken ct);
}
```

- [ ] **Step 4: Implement the service**

Create `src/HR.Modules/Platform/Services/Completion/ScheduledEffectRecoveryService.cs`:
```csharp
using HR.Application.Engines.Timeline;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion;

public sealed class ScheduledEffectRecoveryService : IScheduledEffectRecoveryService
{
    private readonly ApplicationDbContext _db;
    private readonly ITimelineEngine _timeline;

    public ScheduledEffectRecoveryService(ApplicationDbContext db, ITimelineEngine timeline)
    { _db = db; _timeline = timeline; }

    public async Task<IReadOnlyList<AttentionEffectDto>> ListAttentionAsync(CancellationToken ct) =>
        await _db.CompletionEffects
            .Where(e => e.Status == CompletionEffectStatus.ManualReview || e.Status == CompletionEffectStatus.Failed)
            .OrderBy(e => e.ExecutedAt)
            .Select(e => new AttentionEffectDto(
                e.Id, e.RequestInstanceId, e.EffectType, e.Attempts, e.MaxAttempts, e.FailureReason, e.ScheduledFor))
            .ToListAsync(ct);

    public async Task<bool> RetryAsync(Guid effectId, CancellationToken ct)
    {
        var e = await _db.CompletionEffects.FirstOrDefaultAsync(x => x.Id == effectId, ct);
        if (e is null || e.Status is not (CompletionEffectStatus.ManualReview or CompletionEffectStatus.Failed))
            return false;

        e.Status = CompletionEffectStatus.Pending;
        e.NextAttemptAt = DateTime.UtcNow;
        e.LeasedBy = null; e.LeasedUntil = null; e.FailureReason = null;
        await _db.SaveChangesAsync(ct);

        await _timeline.PublishEvent("Completion", "RequestInstance", e.RequestInstanceId, "EffectRequeued",
            $"Deferred effect {e.EffectType} was manually requeued",
            $"تمت إعادة جدولة الإجراء {e.EffectType} يدويًا",
            new { effectId = e.Id }, ct);
        return true;
    }

    public async Task<bool> SkipAsync(Guid effectId, string reason, CancellationToken ct)
    {
        var e = await _db.CompletionEffects.FirstOrDefaultAsync(x => x.Id == effectId, ct);
        if (e is null || e.Status is not (CompletionEffectStatus.ManualReview or CompletionEffectStatus.Failed))
            return false;

        e.Status = CompletionEffectStatus.Skipped;
        e.FailureReason = reason;
        e.LeasedBy = null; e.LeasedUntil = null;
        await _db.SaveChangesAsync(ct);

        await _timeline.PublishEvent("Completion", "RequestInstance", e.RequestInstanceId, "EffectSkipped",
            $"Deferred effect {e.EffectType} was manually skipped: {reason}",
            $"تم تخطّي الإجراء {e.EffectType} يدويًا: {reason}",
            new { effectId = e.Id }, ct);
        return true;
    }
}
```
> Verify `ITimelineEngine.PublishEvent`'s exact signature against `CompletionEngine.cs:167-170` and match argument order/types. If it differs, adjust these two calls to match.

- [ ] **Step 5: Register the service**

In `src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`, after the drainer registration from Task 5, add:
```csharp
        services.AddScoped<HR.Modules.Platform.Services.Completion.IScheduledEffectRecoveryService,
            HR.Modules.Platform.Services.Completion.ScheduledEffectRecoveryService>();
```

- [ ] **Step 6: Run the service tests to verify they pass**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduledEffectRecoveryServiceTests`
Expected: PASS.

- [ ] **Step 7: Add the recovery endpoints**

In `src/HR.Modules/Platform/Controllers/RequestsController.cs`, inject the service (add a constructor parameter + field following the existing DI style in that controller) and add three actions near the `{id:guid}/completion` action:
```csharp
    [HttpGet("effects/attention")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Edit")]
    public async Task<IActionResult> EffectsNeedingAttention(CancellationToken ct)
        => OkResponse(await _recovery.ListAttentionAsync(ct));

    [HttpPost("effects/{effectId:guid}/retry")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Edit")]
    public async Task<IActionResult> RetryEffect(Guid effectId, CancellationToken ct)
        => await _recovery.RetryAsync(effectId, ct)
            ? OkResponse(true, "Effect requeued")
            : BadRequestResponse("Effect is not in a recoverable state");

    [HttpPost("effects/{effectId:guid}/skip")]
    [HR.Api.Filters.RequirePermission("Platform.MasterData.Edit")]
    public async Task<IActionResult> SkipEffect(Guid effectId, [FromBody] SkipEffectRequest body, CancellationToken ct)
        => await _recovery.SkipAsync(effectId, body?.Reason ?? "ManuallySkipped", ct)
            ? OkResponse(true, "Effect skipped")
            : BadRequestResponse("Effect is not in a recoverable state");

    public sealed record SkipEffectRequest(string? Reason);
```
> Match the base-controller helper names actually available (`OkResponse`, `BadRequestResponse`, `OkResponse(value, message)`) by checking `BaseApiController`. If a helper differs (e.g. `Ok(...)` vs `OkResponse(...)`), use the one that exists — other actions in this controller show the correct shape.

- [ ] **Step 8: Build + run the full Platform suite**

Run: `dotnet build HR.sln -c Debug` then `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: Build succeeded; all pass.

- [ ] **Step 9: Commit + push**

```bash
git add src/HR.Modules tests
git commit -m "feat(requests): failed-effect recovery service + endpoints + timeline events

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 7: End-to-end pilot — date-effective Employee.UpdateField

**Files:**
- Test: `tests/HR.Modules.Platform.Tests/Completion/DeferredEffectPilotTests.cs`
- (No production code expected — this proves the machinery with the existing `Employee.UpdateField` executor. If a gap surfaces, fix it in the relevant file and note it in the commit.)

**Interfaces:**
- Consumes: everything from Tasks 1-6.

- [ ] **Step 1: Write the end-to-end pilot test**

Create `tests/HR.Modules.Platform.Tests/Completion/DeferredEffectPilotTests.cs`. It wires a `CompletionEngine` + `ScheduledEffectDrainer` over one in-memory context and proves the full path:
1. Seed an employee, a request type with one **Deferred** `Employee.UpdateField` definition (`MaxAttempts=5`, `__effectiveOn` mapped to a form date one day in the past so it is immediately due), and an approved request instance + form submission.
2. Call `CompletionEngine.ExecuteAsync` → assert the run is `AwaitingDeferred`, the effect is `Pending`/`Scheduled`, and the employee field is **not yet** changed.
3. Call `ScheduledEffectDrainer.DrainAsync` → assert the effect is `Completed`, the employee field **is** changed, and an `EffectAttempts` row exists.
4. Call `DrainAsync` again → assert nothing changes (idempotent; the effect is terminal).
```csharp
using FluentAssertions;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

public class DeferredEffectPilotTests
{
    [Fact]
    public async Task Date_effective_field_update_runs_once_on_the_worker_after_approval()
    {
        await using var h = await DeferredPilotHarness.CreateAsync(
            field: "jobTitle", newValue: "Manager", effectiveOn: DateTime.UtcNow.AddDays(-1));

        var completion = await h.Engine.ExecuteAsync(h.RequestInstanceId, default);
        completion.Success.Should().BeTrue();
        (await h.RunStatus()).Should().Be(CompletionRunStatus.AwaitingDeferred);
        (await h.EmployeeJobTitle()).Should().NotBe("Manager");

        var processed = await h.Drainer.DrainAsync(default);
        processed.Should().Be(1);
        (await h.EffectStatus()).Should().Be(CompletionEffectStatus.Completed);
        (await h.EmployeeJobTitle()).Should().Be("Manager");
        (await h.AttemptCount()).Should().Be(1);

        var again = await h.Drainer.DrainAsync(default);
        again.Should().Be(0);
        (await h.EmployeeJobTitle()).Should().Be("Manager");
    }
}
```
> `DeferredPilotHarness` builds the real `CompletionEngine`, `CompletionEffectFactory`, `ScheduledEffectDrainer`, and an `IEffectExecutorRegistry` containing the real `EmployeeUpdateFieldExecutor` (reuse its test construction from `EmployeeUpdateFieldExecutorTests`), all over one in-memory `ApplicationDbContext` with the transaction-warning suppression. `completion.Success` — confirm the actual property name on `CompletionResult` (check `CompletionResult.Ok/Fail`) and use it.

- [ ] **Step 2: Run the pilot test**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~DeferredEffectPilotTests`
Expected: PASS. If it fails for a real integration gap (e.g. `EffectContext.EmployeeId` not populated for the executor), fix the drainer/engine accordingly and re-run.

- [ ] **Step 3: Run the FULL Platform suite one last time**

Run: `dotnet test tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: all pass (≥ 220 + the new tests).

- [ ] **Step 4: Commit + push**

```bash
git add tests src
git commit -m "test(requests): end-to-end deferred-effect pilot (date-effective field update)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 8: Deploy (USER-GATED — instructions only, do not run unprompted)

**Files:** none (operational).

- [ ] **Step 1: Apply the migration to Azure Postgres**

Requires an allowlisted IP (classifier blocks the firewall change without explicit user auth). With user approval:
```bash
IP=$(curl -s https://api.ipify.org)
az postgres flexible-server firewall-rule create --resource-group HR --server-name hrcloud-pg-v4xd \
  --name claude-deploy-tmp --start-ip-address $IP --end-ip-address $IP
PASS=$(az keyvault secret show --vault-name secretpulse --name hrcloud-db-password --query value -o tsv)
dotnet ef database update --project src/HR.Infrastructure --startup-project src/HR.Api \
  --connection "Host=hrcloud-pg-v4xd.postgres.database.azure.com;Port=5432;Database=hrcloud;Username=hradmin;Password=$PASS;Ssl Mode=Require;Trust Server Certificate=true"
az postgres flexible-server firewall-rule delete --resource-group HR --server-name hrcloud-pg-v4xd \
  --name claude-deploy-tmp --yes
```

- [ ] **Step 2: Zip-deploy the API**

```bash
dotnet publish src/HR.Api -c Release -o publish-out
# Zip via System.IO.Compression.ZipFile with '\' -> '/' entry names (Compress-Archive breaks Kudu).
az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path publish.zip --type zip
```

- [ ] **Step 3: Live verification**

- `GET /health` (warm the F1 cold start; first curl may return 000).
- `GET /api/requests/effects/attention` returns 401 unauthenticated (endpoint present).
- Configure one request type with a Deferred, date-effective `Employee.UpdateField`, approve a request, confirm the run is `AwaitingDeferred`, and that within ~1-2 minutes the worker applies the change and the effect shows `Completed`.

---

## Self-Review

**Spec coverage:**
- Durable scheduled effects → Tasks 1 (columns), 3 (enqueue), 5 (worker). ✓
- Idempotency → Task 1 (`IdempotencyKey` unique), Task 3 (key = effect id), Task 5 (same-tx commit; terminal rows not re-claimed). ✓
- Safe retries → Task 4 (pure decision), Task 5 (applied). ✓
- Effect execution history → Task 1 (`engine_effect_attempts`), Task 5 (`RecordAttempt`). ✓
- Failed-effect recovery → Task 6 (service + endpoints + timeline). ✓
- Date-effective actions → Task 2 (`__effectiveOn` → `ScheduledFor`), Task 5 (due-date filter). ✓
- Worker/background processing → Task 5 (drainer + hosted service). ✓
- Transaction safety → Task 3 (outbox enqueue with approval), Task 5 (per-effect tx). ✓
- Preventing duplicate executions → Task 5 (lease + due filter + terminal-status guard), Task 1 (unique key). ✓
- Compatibility → additive `Deferred` mode; inline paths untouched; full suite green each task. ✓

**Placeholder scan:** No TBD/TODO; every code step shows real code; test-harness helpers (`DeferredFactoryHarness`, `DeferredPilotHarness`) are described with their construction source (existing `EmployeeUpdateFieldExecutorTests`) rather than left blank — acceptable as they are per-test scaffolding, but the implementer must write them following the cited pattern.

**Type consistency:** `EffectIntent(EffectType, Sequence, Payload, Mode, ScheduledFor, MaxAttempts)` used consistently in Tasks 2-3. `CompletionEffectStatus.{Scheduled,Retrying,ManualReview}` and `CompletionRunStatus.AwaitingDeferred` defined in Task 1 and used in 3/5/6. `ScheduledEffectDecision.ApplySuccess/ApplyFailure` signatures match between Task 4 definition and Task 5 usage. `IScheduledEffectDrainer.DrainAsync` and `IScheduledEffectRecoveryService` members consistent across definition, DI, controller, and tests.

**Verification note for the implementer:** three signatures must be confirmed against live code before use (flagged inline): `ITimelineEngine.PublishEvent`, `CompletionResult`'s success property, and `EffectValueResolver.Resolve`'s return type. Adjust the surrounding lines to match — they do not change the design.

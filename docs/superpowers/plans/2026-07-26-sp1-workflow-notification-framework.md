# SP1 — Workflow-Driven Notification Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `RequestEngine`'s hardcoded inline notifications with a configurable, workflow-event-driven pipeline (event → rule → recipient resolver → delivery), with Leave Request as the first consumer.

**Architecture:** A new `WorkflowNotificationRule` entity (recipients as validated JSON) is matched by precedence for each workflow event, resolved to concrete user ids by a promoted `INotificationRecipientResolver`, rendered via the existing `DocumentTokenResolver`, de-duplicated + idempotency-guarded, and delivered through the existing `INotificationService` (bell + email queue). `RequestEngine` calls a single `IWorkflowNotificationDispatcher` at 6 lifecycle points. Default Leave rules are seeded non-destructively via the existing provisioning/`SeedVersion` mechanism.

**Tech Stack:** .NET 8, EF Core (PostgreSQL, in-memory for tests), xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-07-26-sp1-workflow-notification-framework-design.md` (read it — this plan implements it exactly).

## Global Constraints

- **Notification work must never roll back or fail a request transition.** `DispatchAsync` is fully try/catch-guarded; delivery is enqueue-only; failed sends retry via the existing email queue.
- **Never silently redirect a notification.** An unresolved recipient is logged + skipped individually; it is never reassigned to another person, and never falls back to the requester.
- **Ponytail (active, full mode):** reuse before building. Reuse `INotificationService`, `EmailNotificationQueue`+drainer+ACS, `DocumentTokenResolver`/`ResolveTokens`, and the existing `RequestEngine` resolver queries. No MediatR event bus (single consumer). No child table for recipients (JSON). Delete the hardcoded notify helpers you replace.
- **Commit + push each stable, tested slice separately** to BOTH remotes: `git push origin main` then `git push sanad main`. Never commit broken/untested code.
- **Entities** derive from `HR.Domain.Common.TenantEntity` (gives `Id`, `TenantId`, audit fields). **EF configs** implement `IEntityTypeConfiguration<T>` in `HR.Infrastructure.Persistence.Configurations.Engines`. **Tests** live in `backend/tests/HR.Modules.Platform.Tests`, use `new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"x_{Guid.NewGuid()}").Options, fakeCurrentUser)`.
- **All build/test/migration commands run from `backend/`.** Build: `dotnet build HR.sln`. Test a class: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~<ClassName>"`.
- **Capability registry gates validity:** rules may only reference the 6 supported events and 11 supported recipient types (§10 of spec). Deferred enum values exist but are rejected by validation and hidden from APIs.

---

## File Structure

**New — Domain (`backend/src/HR.Domain`):**
- `Enums/WorkflowNotificationEvent.cs` — 12-value event enum.
- `Enums/NotificationRecipientType.cs` — 13-value recipient enum.
- `Engines/Notifications/WorkflowNotificationRule.cs` — rule entity.
- `Engines/Notifications/WorkflowNotificationDispatch.cs` — idempotency ledger entity.

**New — Application (`backend/src/HR.Application`):**
- `Engines/Notifications/RecipientSpec.cs` — `RecipientSpec`, `RecipientsEnvelope`, `RecipientParseResult`.
- `Engines/Notifications/RecipientSpecParser.cs` — parse + validate RecipientsJson.
- `Engines/Notifications/NotificationCapabilityRegistry.cs` — supported events/recipients + `RequiresRefId`.
- `Engines/Notifications/NotificationTokenWhitelist.cs` — allowed tokens + `FindUnknownTokens`.

**New — Platform module (`backend/src/HR.Modules/Platform/Services/Notifications`):**
- `INotificationRecipientResolver.cs` + `NotificationRecipientResolver.cs`.
- `IWorkflowNotificationDispatcher.cs` + `WorkflowNotificationDispatcher.cs`.
- `SystemWorkflowNotificationRules.cs` (in `Services/Requests`, beside `SystemRequestEffects.cs`).

**New — Infrastructure:**
- `Persistence/Configurations/Engines/WorkflowNotificationConfiguration.cs` — both EF configs.
- One migration `WorkflowNotifications`.

**Modified:**
- `HR.Infrastructure/Persistence/ApplicationDbContext.cs` — 2 DbSets.
- `HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` — 2 service registrations.
- `HR.Modules/Platform/Services/Requests/RequestEngine.cs` — 6 dispatch points; inject dispatcher; delete replaced helpers.
- `HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs` — `CurrentSeedVersion` 3→4; `ReconcileWorkflowNotificationRules`.

**Tests (`backend/tests/HR.Modules.Platform.Tests`):**
- `Notifications/RecipientSpecParserTests.cs`
- `Notifications/NotificationTokenWhitelistTests.cs`
- `Notifications/NotificationRecipientResolverTests.cs`
- `Notifications/WorkflowNotificationDispatcherTests.cs`
- `Notifications/WorkflowNotificationSeedTests.cs`

---

## Task 1: Data model — entities, enums, EF config, migration

**Files:**
- Create: `backend/src/HR.Domain/Enums/WorkflowNotificationEvent.cs`
- Create: `backend/src/HR.Domain/Enums/NotificationRecipientType.cs`
- Create: `backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationRule.cs`
- Create: `backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationDispatch.cs`
- Create: `backend/src/HR.Infrastructure/Persistence/Configurations/Engines/WorkflowNotificationConfiguration.cs`
- Modify: `backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs` (DbSets)

**Interfaces:**
- Produces: `WorkflowNotificationEvent` enum, `NotificationRecipientType` enum, `WorkflowNotificationRule` entity, `WorkflowNotificationDispatch` entity, DbSets `WorkflowNotificationRules` / `WorkflowNotificationDispatches`.

- [ ] **Step 1: Create the event enum**

`backend/src/HR.Domain/Enums/WorkflowNotificationEvent.cs`:
```csharp
namespace HR.Domain.Enums;

/// <summary>Request-lifecycle events a notification rule can subscribe to. Values 1-6 are dispatched
/// today (see NotificationCapabilityRegistry.SupportedEvents); 7-12 are defined for forward
/// compatibility and are rejected by validation / hidden from APIs until their SP lands.</summary>
public enum WorkflowNotificationEvent
{
    Submitted = 1,
    StepAssigned = 2,
    StepApproved = 3,
    Rejected = 4,
    Returned = 5,
    FinalApproved = 6,
    MoreInfoRequested = 7,
    EffectExecuted = 8,
    EffectFailed = 9,
    Cancelled = 10,
    SlaReminder = 11,
    EscalationTriggered = 12,
}
```

- [ ] **Step 2: Create the recipient-type enum**

`backend/src/HR.Domain/Enums/NotificationRecipientType.cs`:
```csharp
namespace HR.Domain.Enums;

/// <summary>Who a notification rule targets. Values 1-11 are resolved today (see
/// NotificationCapabilityRegistry.SupportedRecipientTypes); 12-13 are reserved and rejected by
/// validation until their resolver lands.</summary>
public enum NotificationRecipientType
{
    Requester = 1,
    EmployeeConcerned = 2,
    CurrentApprover = 3,
    PreviousApprover = 4,
    DirectManager = 5,
    DepartmentManager = 6,
    SpecificEmployee = 7,
    Role = 8,
    HrTeam = 9,
    FinanceTeam = 10,
    StepAssignees = 11,
    FormSelectedEmployee = 12,
    Custom = 13,
}
```

- [ ] **Step 3: Create the rule entity**

`backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationRule.cs`:
```csharp
using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Notifications;

/// <summary>An admin/seed-configured rule that fires notifications on a request-workflow event.
/// Recipients are stored as a validated JSON envelope (see RecipientSpecParser). System-seeded rows
/// carry a stable SystemKey and are never overwritten once a tenant customizes them.</summary>
public class WorkflowNotificationRule : TenantEntity
{
    /// <summary>Request type code this applies to, or null = all types.</summary>
    public string? RequestTypeCode { get; set; }

    public WorkflowNotificationEvent Event { get; set; }

    /// <summary>Approval step this applies to, or null = any step.</summary>
    public int? StepOrder { get; set; }

    /// <summary>Validated recipient envelope: {"v":1,"recipients":[{"type":"...","refId":"..."}]}.</summary>
    public string RecipientsJson { get; set; } = """{"v":1,"recipients":[]}""";

    public string SubjectAr { get; set; } = "";
    public string SubjectEn { get; set; } = "";
    public string BodyAr { get; set; } = "";
    public string BodyEn { get; set; } = "";

    public bool ChannelBell { get; set; } = true;
    public bool ChannelEmail { get; set; } = true;
    public bool IsActive { get; set; } = true;

    /// <summary>True for product-seeded rows; tenant-authored rules are false.</summary>
    public bool IsSystemOwned { get; set; }

    /// <summary>Stable seed identity (e.g. "LEAVE_REQUEST:Submitted:Requester"). Unique per tenant when set.</summary>
    public string? SystemKey { get; set; }

    /// <summary>Set true when a tenant edits a system rule — provisioning then never overwrites it.</summary>
    public bool IsCustomized { get; set; }
}
```

- [ ] **Step 4: Create the idempotency ledger entity**

`backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationDispatch.cs`:
```csharp
using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Notifications;

/// <summary>Deterministic delivery ledger. One row per (request, event, step, rule, user) guarantees
/// a replayed transition delivers a notification at most once. StepOrder uses -1 when step-agnostic
/// so the composite key stays non-null.</summary>
public class WorkflowNotificationDispatch : TenantEntity
{
    public Guid RequestInstanceId { get; set; }
    public WorkflowNotificationEvent Event { get; set; }
    public int StepOrder { get; set; }
    public Guid RuleId { get; set; }
    public Guid UserId { get; set; }
    public DateTime DispatchedAt { get; set; }
}
```

- [ ] **Step 5: Create the EF configuration (both entities, one file)**

`backend/src/HR.Infrastructure/Persistence/Configurations/Engines/WorkflowNotificationConfiguration.cs`:
```csharp
using HR.Domain.Engines.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class WorkflowNotificationRuleConfiguration : IEntityTypeConfiguration<WorkflowNotificationRule>
{
    public void Configure(EntityTypeBuilder<WorkflowNotificationRule> builder)
    {
        builder.ToTable("workflow_notification_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestTypeCode).HasMaxLength(100);
        builder.Property(x => x.RecipientsJson).HasColumnType("jsonb");
        builder.Property(x => x.SubjectAr).HasMaxLength(300);
        builder.Property(x => x.SubjectEn).HasMaxLength(300);
        builder.Property(x => x.SystemKey).HasMaxLength(200);
        // Dispatcher hot path: tenant + type + event + active.
        builder.HasIndex(x => new { x.TenantId, x.RequestTypeCode, x.Event, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.StepOrder });
        // Seed identity is unique per tenant (filtered to seeded rows).
        builder.HasIndex(x => new { x.TenantId, x.SystemKey })
            .IsUnique()
            .HasFilter("\"SystemKey\" IS NOT NULL");
    }
}

public class WorkflowNotificationDispatchConfiguration : IEntityTypeConfiguration<WorkflowNotificationDispatch>
{
    public void Configure(EntityTypeBuilder<WorkflowNotificationDispatch> builder)
    {
        builder.ToTable("workflow_notification_dispatches");
        builder.HasKey(x => x.Id);
        // The idempotency key.
        builder.HasIndex(x => new { x.RequestInstanceId, x.Event, x.StepOrder, x.RuleId, x.UserId })
            .IsUnique();
    }
}
```

- [ ] **Step 6: Register DbSets**

In `backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs`, beside the existing notification DbSets (search for `public DbSet<NotificationRule>`), add:
```csharp
    public DbSet<WorkflowNotificationRule> WorkflowNotificationRules => Set<WorkflowNotificationRule>();
    public DbSet<WorkflowNotificationDispatch> WorkflowNotificationDispatches => Set<WorkflowNotificationDispatch>();
```
Ensure `using HR.Domain.Engines.Notifications;` is present (it already is for `NotificationRule`).

- [ ] **Step 7: Build**

Run: `dotnet build backend/HR.sln`
Expected: build succeeds (0 errors). EF configs are auto-discovered by `ApplyConfigurationsFromAssembly` (confirm the pattern already used for `NotificationRuleConfiguration` — if configs are registered explicitly, add these two the same way).

- [ ] **Step 8: Create the migration**

Run:
```bash
cd backend && dotnet ef migrations add WorkflowNotifications --project src/HR.Infrastructure --startup-project src/HR.Api
```
Expected: a new `*_WorkflowNotifications.cs` under `src/HR.Infrastructure/Migrations` creating both tables + indexes. Open it and verify: two `CreateTable` calls, the unique filtered SystemKey index, and the unique idempotency index. Do NOT apply it (Azure apply is user-gated, batched with SP0's pending migration).

- [ ] **Step 9: Commit + push**

```bash
git add backend/src/HR.Domain/Enums/WorkflowNotificationEvent.cs backend/src/HR.Domain/Enums/NotificationRecipientType.cs backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationRule.cs backend/src/HR.Domain/Engines/Notifications/WorkflowNotificationDispatch.cs backend/src/HR.Infrastructure/Persistence/Configurations/Engines/WorkflowNotificationConfiguration.cs backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs backend/src/HR.Infrastructure/Migrations
git commit -m "feat(notifications): WorkflowNotificationRule + dispatch ledger entities + migration

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 2: Recipient JSON model + validator + capability registry + token whitelist

**Files:**
- Create: `backend/src/HR.Application/Engines/Notifications/RecipientSpec.cs`
- Create: `backend/src/HR.Application/Engines/Notifications/NotificationCapabilityRegistry.cs`
- Create: `backend/src/HR.Application/Engines/Notifications/RecipientSpecParser.cs`
- Create: `backend/src/HR.Application/Engines/Notifications/NotificationTokenWhitelist.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/RecipientSpecParserTests.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationTokenWhitelistTests.cs`

**Interfaces:**
- Consumes: `NotificationRecipientType`, `WorkflowNotificationEvent` (Task 1).
- Produces:
  - `record RecipientSpec(NotificationRecipientType Type, Guid? RefId)`
  - `record RecipientsEnvelope(int V, IReadOnlyList<RecipientSpec> Recipients)`
  - `record RecipientParseResult(RecipientsEnvelope? Envelope, IReadOnlyList<string> Errors) { bool IsValid => Errors.Count == 0; }`
  - `RecipientSpecParser.ParseAndValidate(string json) -> RecipientParseResult`
  - `RecipientSpecParser.Serialize(IEnumerable<RecipientSpec>) -> string`
  - `NotificationCapabilityRegistry.SupportedEvents` / `.SupportedRecipientTypes` (IReadOnlySet) / `.RequiresRefId(NotificationRecipientType) -> bool` / `.CurrentSchemaVersion = 1`
  - `NotificationTokenWhitelist.AllowedTokens` (IReadOnlySet<string>) / `.FindUnknownTokens(string template) -> IReadOnlyList<string>`

- [ ] **Step 1: Create the capability registry + recipient records**

`backend/src/HR.Application/Engines/Notifications/RecipientSpec.cs`:
```csharp
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

public sealed record RecipientSpec(NotificationRecipientType Type, Guid? RefId = null);

public sealed record RecipientsEnvelope(int V, IReadOnlyList<RecipientSpec> Recipients);

public sealed record RecipientParseResult(RecipientsEnvelope? Envelope, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
    public static RecipientParseResult Fail(string error) => new(null, new[] { error });
}
```

`backend/src/HR.Application/Engines/Notifications/NotificationCapabilityRegistry.cs`:
```csharp
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>The single source of truth for what the framework can actually do today. Validation
/// rejects rules referencing anything outside these sets; the future admin API lists only these.</summary>
public static class NotificationCapabilityRegistry
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxRecipients = 20;

    public static readonly IReadOnlySet<WorkflowNotificationEvent> SupportedEvents = new HashSet<WorkflowNotificationEvent>
    {
        WorkflowNotificationEvent.Submitted,
        WorkflowNotificationEvent.StepAssigned,
        WorkflowNotificationEvent.StepApproved,
        WorkflowNotificationEvent.Rejected,
        WorkflowNotificationEvent.Returned,
        WorkflowNotificationEvent.FinalApproved,
    };

    public static readonly IReadOnlySet<NotificationRecipientType> SupportedRecipientTypes = new HashSet<NotificationRecipientType>
    {
        NotificationRecipientType.Requester, NotificationRecipientType.EmployeeConcerned,
        NotificationRecipientType.CurrentApprover, NotificationRecipientType.PreviousApprover,
        NotificationRecipientType.DirectManager, NotificationRecipientType.DepartmentManager,
        NotificationRecipientType.SpecificEmployee, NotificationRecipientType.Role,
        NotificationRecipientType.HrTeam, NotificationRecipientType.FinanceTeam,
        NotificationRecipientType.StepAssignees,
    };

    /// <summary>Recipient types that need a refId (an entity reference) to resolve.</summary>
    public static bool RequiresRefId(NotificationRecipientType type) =>
        type is NotificationRecipientType.SpecificEmployee or NotificationRecipientType.Role;
}
```

- [ ] **Step 2: Write failing tests for the parser**

`backend/tests/HR.Modules.Platform.Tests/Notifications/RecipientSpecParserTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class RecipientSpecParserTests
{
    [Fact]
    public void Parses_valid_envelope()
    {
        var json = """{"v":1,"recipients":[{"type":"CurrentApprover"},{"type":"Role","refId":"11111111-1111-1111-1111-111111111111"}]}""";
        var r = RecipientSpecParser.ParseAndValidate(json);
        r.IsValid.Should().BeTrue();
        r.Envelope!.Recipients.Should().HaveCount(2);
        r.Envelope.Recipients[1].Type.Should().Be(NotificationRecipientType.Role);
        r.Envelope.Recipients[1].RefId.Should().NotBeNull();
    }

    [Fact]
    public void Rejects_unknown_recipient_type()
    {
        var json = """{"v":1,"recipients":[{"type":"Wizard"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_deferred_recipient_type()
    {
        var json = """{"v":1,"recipients":[{"type":"FormSelectedEmployee"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_missing_refId_when_required()
    {
        var json = """{"v":1,"recipients":[{"type":"Role"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_refId_when_forbidden()
    {
        var json = """{"v":1,"recipients":[{"type":"DirectManager","refId":"11111111-1111-1111-1111-111111111111"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unknown_property()
    {
        var json = """{"v":1,"recipients":[{"type":"Requester","color":"red"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_over_max_recipients()
    {
        var one = """{"type":"Requester"}""";
        var many = string.Join(",", System.Linq.Enumerable.Repeat(one, 21));
        var json = $$"""{"v":1,"recipients":[{{many}}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Collapses_duplicate_recipients()
    {
        var json = """{"v":1,"recipients":[{"type":"Requester"},{"type":"Requester"}]}""";
        var r = RecipientSpecParser.ParseAndValidate(json);
        r.IsValid.Should().BeTrue();
        r.Envelope!.Recipients.Should().HaveCount(1);
    }

    [Fact]
    public void Rejects_malformed_json()
    {
        RecipientSpecParser.ParseAndValidate("{not json").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_unsupported_schema_version()
    {
        var json = """{"v":999,"recipients":[{"type":"Requester"}]}""";
        RecipientSpecParser.ParseAndValidate(json).IsValid.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~RecipientSpecParserTests"`
Expected: FAIL — `RecipientSpecParser` does not exist.

- [ ] **Step 4: Implement the parser**

`backend/src/HR.Application/Engines/Notifications/RecipientSpecParser.cs`:
```csharp
using System.Text.Json;
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>Parses and validates a rule's RecipientsJson. Strict: unknown types, deferred types,
/// wrong/missing refId, unknown properties, over-max count, and bad schema versions all fail.
/// Duplicate recipients collapse. Never throws — returns a result with errors.</summary>
public static class RecipientSpecParser
{
    private static readonly HashSet<string> EnvelopeKeys = new(StringComparer.Ordinal) { "v", "recipients" };
    private static readonly HashSet<string> RecipientKeys = new(StringComparer.Ordinal) { "type", "refId" };

    public static RecipientParseResult ParseAndValidate(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return RecipientParseResult.Fail("RecipientsJson is not valid JSON."); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return RecipientParseResult.Fail("RecipientsJson must be an object.");

            foreach (var prop in root.EnumerateObject())
                if (!EnvelopeKeys.Contains(prop.Name))
                    return RecipientParseResult.Fail($"Unknown property '{prop.Name}' on recipients envelope.");

            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.Number
                || vEl.GetInt32() != NotificationCapabilityRegistry.CurrentSchemaVersion)
                return RecipientParseResult.Fail("Unsupported or missing recipients schema version 'v'.");

            if (!root.TryGetProperty("recipients", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return RecipientParseResult.Fail("'recipients' must be an array.");

            var errors = new List<string>();
            var specs = new List<RecipientSpec>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) { errors.Add("Each recipient must be an object."); continue; }
                foreach (var prop in item.EnumerateObject())
                    if (!RecipientKeys.Contains(prop.Name)) errors.Add($"Unknown property '{prop.Name}' on recipient.");

                if (!item.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String
                    || !Enum.TryParse<NotificationRecipientType>(typeEl.GetString(), ignoreCase: false, out var type))
                { errors.Add("Recipient 'type' is missing or unknown."); continue; }

                if (!NotificationCapabilityRegistry.SupportedRecipientTypes.Contains(type))
                { errors.Add($"Recipient type '{type}' is not yet supported."); continue; }

                Guid? refId = null;
                if (item.TryGetProperty("refId", out var refEl))
                {
                    if (refEl.ValueKind != JsonValueKind.String || !Guid.TryParse(refEl.GetString(), out var g))
                    { errors.Add($"Recipient '{type}' has a malformed refId."); continue; }
                    refId = g;
                }

                var needs = NotificationCapabilityRegistry.RequiresRefId(type);
                if (needs && refId is null) { errors.Add($"Recipient '{type}' requires a refId."); continue; }
                if (!needs && refId is not null) { errors.Add($"Recipient '{type}' must not carry a refId."); continue; }

                specs.Add(new RecipientSpec(type, refId));
            }

            var deduped = specs.DistinctBy(s => (s.Type, s.RefId)).ToList();
            if (deduped.Count > NotificationCapabilityRegistry.MaxRecipients)
                errors.Add($"A rule may have at most {NotificationCapabilityRegistry.MaxRecipients} recipients.");

            if (errors.Count > 0) return new RecipientParseResult(null, errors);
            return new RecipientParseResult(
                new RecipientsEnvelope(NotificationCapabilityRegistry.CurrentSchemaVersion, deduped),
                Array.Empty<string>());
        }
    }

    public static string Serialize(IEnumerable<RecipientSpec> recipients)
    {
        var items = recipients.Select(r => r.RefId is { } id
            ? new { type = r.Type.ToString(), refId = id.ToString() }
            : (object)new { type = r.Type.ToString() });
        return JsonSerializer.Serialize(new { v = NotificationCapabilityRegistry.CurrentSchemaVersion, recipients = items });
    }
}
```

- [ ] **Step 5: Run parser tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~RecipientSpecParserTests"`
Expected: PASS (all 10).

- [ ] **Step 6: Write failing tests for the token whitelist**

`backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationTokenWhitelistTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Notifications;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class NotificationTokenWhitelistTests
{
    [Fact]
    public void Known_token_is_allowed()
        => NotificationTokenWhitelist.FindUnknownTokens("Hello {{Employee.FullName}}").Should().BeEmpty();

    [Fact]
    public void Unknown_token_is_reported()
        => NotificationTokenWhitelist.FindUnknownTokens("{{Secret.Password}}").Should().Contain("Secret.Password");

    [Fact]
    public void Plain_text_has_no_unknown_tokens()
        => NotificationTokenWhitelist.FindUnknownTokens("no tokens here").Should().BeEmpty();
}
```

- [ ] **Step 7: Run to verify fail, then implement the whitelist**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~NotificationTokenWhitelistTests"` → FAIL.

`backend/src/HR.Application/Engines/Notifications/NotificationTokenWhitelist.cs`:
```csharp
using System.Text.RegularExpressions;

namespace HR.Application.Engines.Notifications;

/// <summary>The closed set of {{tokens}} a notification template may reference. Mirrors the keys
/// DocumentTokenResolver produces. Unknown tokens are reported for a validation warning and are left
/// visible at render time — never resolved against arbitrary object properties.</summary>
public static class NotificationTokenWhitelist
{
    private static readonly Regex TokenPattern = new(@"\{\{\s*([\w.]+)\s*\}\}", RegexOptions.Compiled);

    public static readonly IReadOnlySet<string> AllowedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Employee.FullName", "Employee.EmployeeNumber", "Employee.Department", "Employee.JobTitle",
        "Employee.Manager", "Employee.Nationality", "Employee.NationalId", "Employee.HireDate",
        "Employee.Email", "Employee.Phone",
        "Request.Number", "Request.Type", "Request.CreatedDate", "Request.ApprovalDate", "Request.Status",
        "Leave.Type", "Leave.StartDate", "Leave.EndDate", "Leave.Days",
        "Company.Name", "Company.NameEn", "Company.CR", "Company.VAT", "Company.Address",
        "Company.Phone", "Company.Email", "Company.Website",
        "System.Today",
    };

    /// <summary>Distinct tokens in the template that are not on the whitelist.</summary>
    public static IReadOnlyList<string> FindUnknownTokens(string? template)
    {
        if (string.IsNullOrEmpty(template)) return Array.Empty<string>();
        return TokenPattern.Matches(template)
            .Select(m => m.Groups[1].Value)
            .Where(t => !AllowedTokens.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```
> NOTE: verify these token keys against `DocumentTokenResolver.ResolveForRequestAsync` in `backend/src/HR.Modules/Platform/Services/Documents/DocumentTokenResolver.cs`. If that resolver emits additional keys (e.g. `Payroll.*` or legacy aliases), add them here so a legitimate template never warns. This whitelist must be a superset-safe mirror of what the renderer can actually fill.

- [ ] **Step 8: Run whitelist tests to verify pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~NotificationTokenWhitelistTests"`
Expected: PASS (3).

- [ ] **Step 9: Commit + push**

```bash
git add backend/src/HR.Application/Engines/Notifications backend/tests/HR.Modules.Platform.Tests/Notifications/RecipientSpecParserTests.cs backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationTokenWhitelistTests.cs
git commit -m "feat(notifications): recipient JSON validator, capability registry, token whitelist

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 3: Recipient resolver

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/INotificationRecipientResolver.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/NotificationRecipientResolver.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationRecipientResolverTests.cs`

**Interfaces:**
- Consumes: `RecipientSpec` (Task 2), `RequestInstance`, `RequestApproval`, `Employee`, `Department`, `User`, `UserRole`, `Role` entities.
- Produces: `INotificationRecipientResolver.ResolveAsync(RecipientSpec spec, RequestInstance instance, RequestApproval? currentStep, CancellationToken ct) -> Task<IReadOnlyList<Guid>>` (distinct user ids; empty when unresolved — the caller logs + skips).

- [ ] **Step 1: Define the interface**

`backend/src/HR.Modules/Platform/Services/Notifications/INotificationRecipientResolver.cs`:
```csharp
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Resolves a single recipient spec to concrete application user ids. Returns an empty list
/// when nothing resolves (e.g. an employee with no manager) — the dispatcher logs and skips that
/// recipient. Never falls back to another person.</summary>
public interface INotificationRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(
        RecipientSpec spec, RequestInstance instance, RequestApproval? currentStep, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing tests**

`backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationRecipientResolverTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class NotificationRecipientResolverTests
{
    private sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"rr_{Guid.NewGuid()}").Options, u);

    private static Employee Emp(Guid tenant, Guid? userId = null, Guid? managerId = null, Guid? deptId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, EmployeeNumber = $"E{Guid.NewGuid():N}".Substring(0, 8),
        FirstName = "F", LastName = "L", Email = "e@e.e", Gender = Gender.Male,
        DateOfBirth = new DateTime(1990, 1, 1), HireDate = new DateTime(2020, 1, 1),
        UserId = userId, ManagerId = managerId, DepartmentId = deptId,
    };

    private static RequestInstance Inst(Guid tenant, Guid empId) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, RequestTypeId = Guid.NewGuid(), RequestNumber = "REQ-1",
        EmployeeId = empId, FormSubmissionId = Guid.NewGuid(), Status = RequestStatus.InProgress,
        SubmittedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Requester_resolves_to_employee_user()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var reqUser = Guid.NewGuid();
        var emp = Emp(u.TenantId, userId: reqUser);
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.Requester), inst, null, default);
        r.Should().ContainSingle().Which.Should().Be(reqUser);
    }

    [Fact]
    public async Task DirectManager_resolves_to_manager_user()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var mgrUser = Guid.NewGuid();
        var mgr = Emp(u.TenantId, userId: mgrUser);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid(), managerId: mgr.Id);
        db.Set<Employee>().AddRange(mgr, emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.DirectManager), inst, null, default);
        r.Should().ContainSingle().Which.Should().Be(mgrUser);
    }

    [Fact]
    public async Task DirectManager_with_no_manager_resolves_empty()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid()); // no managerId
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.DirectManager), inst, null, default);
        r.Should().BeEmpty();
    }

    [Fact]
    public async Task CurrentApprover_resolves_from_step()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var emp = Emp(u.TenantId, userId: Guid.NewGuid());
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();
        var approverUser = Guid.NewGuid();
        var step = new RequestApproval { Id = Guid.NewGuid(), RequestInstanceId = inst.Id, StepOrder = 1,
            StepNameAr = "1", StepNameEn = "1", ApproverType = ApproverType.DirectManager,
            AssignedToUserId = approverUser, Status = RequestApprovalStatus.Pending };

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.CurrentApprover), inst, step, default);
        r.Should().ContainSingle().Which.Should().Be(approverUser);
    }

    [Fact]
    public async Task Role_resolves_all_active_members()
    {
        var u = new FakeUser();
        await using var db = Db(u);
        var roleId = Guid.NewGuid();
        var u1 = new User { Id = Guid.NewGuid(), TenantId = u.TenantId, Email = "u1@x.c", PasswordHash = "x", FullName = "U1", IsActive = true };
        var u2 = new User { Id = Guid.NewGuid(), TenantId = u.TenantId, Email = "u2@x.c", PasswordHash = "x", FullName = "U2", IsActive = true };
        db.Set<User>().AddRange(u1, u2);
        db.Set<UserRole>().AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = u1.Id, RoleId = roleId },
            new UserRole { Id = Guid.NewGuid(), UserId = u2.Id, RoleId = roleId });
        var emp = Emp(u.TenantId, userId: Guid.NewGuid());
        db.Set<Employee>().Add(emp);
        var inst = Inst(u.TenantId, emp.Id);
        db.Set<RequestInstance>().Add(inst);
        await db.SaveChangesAsync();

        var sut = new NotificationRecipientResolver(db);
        var r = await sut.ResolveAsync(new RecipientSpec(NotificationRecipientType.Role, roleId), inst, null, default);
        r.Should().BeEquivalentTo(new[] { u1.Id, u2.Id });
    }
}
```
> Confirm the `User`/`UserRole`/`Role` namespace (`HR.Domain.Entities.Identity`) and add the `using` if the resolver test needs it. Adjust `User`/`UserRole` construction to match their required properties if the build complains.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~NotificationRecipientResolverTests"`
Expected: FAIL — `NotificationRecipientResolver` does not exist.

- [ ] **Step 4: Implement the resolver**

`backend/src/HR.Modules/Platform/Services/Notifications/NotificationRecipientResolver.cs`:
```csharp
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Reuses the resolution queries formerly private to RequestEngine, returning ALL matching
/// users (so "HR team" means everyone in the HR role, not the first). Empty result = unresolved;
/// the dispatcher logs and skips. Never substitutes a different recipient.</summary>
public sealed class NotificationRecipientResolver : INotificationRecipientResolver
{
    private readonly ApplicationDbContext _db;
    public NotificationRecipientResolver(ApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        RecipientSpec spec, RequestInstance instance, RequestApproval? currentStep, CancellationToken ct)
    {
        switch (spec.Type)
        {
            case NotificationRecipientType.Requester:
            case NotificationRecipientType.EmployeeConcerned:
                return await UsersForEmployeeAsync(instance.EmployeeId, ct);

            case NotificationRecipientType.DirectManager:
            {
                var mgrId = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
                    .Select(e => e.ManagerId).FirstOrDefaultAsync(ct);
                return mgrId is { } m ? await UsersForEmployeeAsync(m, ct) : Array.Empty<Guid>();
            }

            case NotificationRecipientType.DepartmentManager:
            {
                var deptId = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
                    .Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
                if (deptId is not { } d) return Array.Empty<Guid>();
                var headEmpId = await _db.Departments.Where(x => x.Id == d)
                    .Select(x => x.ManagerId).FirstOrDefaultAsync(ct);
                return headEmpId is { } h ? await UsersForEmployeeAsync(h, ct) : Array.Empty<Guid>();
            }

            case NotificationRecipientType.SpecificEmployee:
                return spec.RefId is { } empId ? await UsersForEmployeeAsync(empId, ct) : Array.Empty<Guid>();

            case NotificationRecipientType.Role:
                return spec.RefId is { } roleId ? await UsersInRoleIdAsync(roleId, ct) : Array.Empty<Guid>();

            case NotificationRecipientType.HrTeam:
                return await UsersInRoleKeywordAsync("HR", ct);

            case NotificationRecipientType.FinanceTeam:
                return await UsersInRoleKeywordAsync("Finance", ct);

            case NotificationRecipientType.CurrentApprover:
            {
                var uid = currentStep?.AssignedToUserId
                    ?? await _db.RequestApprovals
                        .Where(a => a.RequestInstanceId == instance.Id && a.Status == RequestApprovalStatus.Pending)
                        .OrderBy(a => a.StepOrder).Select(a => a.AssignedToUserId).FirstOrDefaultAsync(ct);
                return uid is { } u ? new[] { u } : Array.Empty<Guid>();
            }

            case NotificationRecipientType.PreviousApprover:
            {
                var uid = await _db.RequestApprovals
                    .Where(a => a.RequestInstanceId == instance.Id && a.DecidedByUserId != null)
                    .OrderByDescending(a => a.StepOrder).Select(a => a.DecidedByUserId).FirstOrDefaultAsync(ct);
                return uid is { } u ? new[] { u } : Array.Empty<Guid>();
            }

            case NotificationRecipientType.StepAssignees:
                return await _db.RequestApprovals
                    .Where(a => a.RequestInstanceId == instance.Id && a.AssignedToUserId != null)
                    .Select(a => a.AssignedToUserId!.Value).Distinct().ToListAsync(ct);

            default:
                return Array.Empty<Guid>(); // deferred/greenfield types: caller logs + skips
        }
    }

    private async Task<IReadOnlyList<Guid>> UsersForEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var uid = await _db.Employees.Where(e => e.Id == employeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        return uid is { } u ? new[] { u } : Array.Empty<Guid>();
    }

    private async Task<IReadOnlyList<Guid>> UsersInRoleIdAsync(Guid roleId, CancellationToken ct)
    {
        var tid = _db is not null ? await _db.Employees.Select(e => e.TenantId).FirstOrDefaultAsync(ct) : default;
        return await (from ur in _db.Set<HR.Domain.Entities.Identity.UserRole>()
                      join usr in _db.Users on ur.UserId equals usr.Id
                      where ur.RoleId == roleId && usr.IsActive
                      select usr.Id).Distinct().ToListAsync(ct);
    }

    private async Task<IReadOnlyList<Guid>> UsersInRoleKeywordAsync(string keyword, CancellationToken ct)
    {
        return await (from ur in _db.Set<HR.Domain.Entities.Identity.UserRole>()
                      join usr in _db.Users on ur.UserId equals usr.Id
                      join role in _db.Set<HR.Domain.Entities.Identity.Role>() on ur.RoleId equals role.Id
                      where usr.IsActive && EF.Functions.ILike(role.Name, $"%{keyword}%")
                      select usr.Id).Distinct().ToListAsync(ct);
    }
}
```
> The `UsersInRoleIdAsync` `tid` line is dead — remove it; the global tenant query filter on `Users`/`UserRole` already scopes to the current tenant. Kept out here intentionally: rely on the DbContext tenant filter exactly as `RequestEngine.UserByRoleIdAsync` does. If `UserRole`/`Role` are exposed as `_db.UserRoles` / `_db.Roles` DbSets, use those instead of `_db.Set<...>()`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~NotificationRecipientResolverTests"`
Expected: PASS (5). Fix namespace/DbSet references until green.

- [ ] **Step 6: Register in DI**

In `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`, beside the notification registrations (near `AddScoped<INotificationService, NotificationService>()`), add:
```csharp
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
```

- [ ] **Step 7: Build + commit + push**

Run: `dotnet build backend/HR.sln` → succeeds.
```bash
git add backend/src/HR.Modules/Platform/Services/Notifications/INotificationRecipientResolver.cs backend/src/HR.Modules/Platform/Services/Notifications/NotificationRecipientResolver.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Notifications/NotificationRecipientResolverTests.cs
git commit -m "feat(notifications): shared recipient resolver (list-returning, reuses RequestEngine queries)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 4: Dispatcher — lookup, precedence, render, dedup, idempotency, failure isolation

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/IWorkflowNotificationDispatcher.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/WorkflowNotificationDispatcher.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationDispatcherTests.cs`

**Interfaces:**
- Consumes: `INotificationRecipientResolver` (Task 3), `RecipientSpecParser` (Task 2), `WorkflowNotificationRule`/`WorkflowNotificationDispatch` (Task 1), `INotificationService`, `DocumentTokenResolver`, `DocumentRenderer.ResolveTokens`.
- Produces: `IWorkflowNotificationDispatcher.DispatchAsync(WorkflowNotificationEvent evt, RequestInstance instance, RequestApproval? step, CancellationToken ct) -> Task`.

- [ ] **Step 1: Define the interface**

`backend/src/HR.Modules/Platform/Services/Notifications/IWorkflowNotificationDispatcher.cs`:
```csharp
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>The centralized "event → rule → resolver → delivery" service. Fully failure-isolated:
/// it never throws to its caller, so a notification problem can never roll back a request transition.</summary>
public interface IWorkflowNotificationDispatcher
{
    Task DispatchAsync(WorkflowNotificationEvent evt, RequestInstance instance, RequestApproval? step, CancellationToken ct);
}
```

- [ ] **Step 2: Write failing tests**

`backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationDispatcherTests.cs`:
```csharp
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class WorkflowNotificationDispatcherTests
{
    private sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    // Records every NotifyAsync call so we can assert who was notified.
    private sealed class SpyNotifier : INotificationService
    {
        public List<Guid> Notified { get; } = new();
        public Task NotifyAsync(Guid userId, string titleAr, string titleEn, string bodyAr, string bodyEn,
            string category, Guid? entityId, string link, DateTime? dueAt = null, bool email = true, CancellationToken ct = default)
        { Notified.Add(userId); return Task.CompletedTask; }
    }

    // Resolves whatever user ids we program per recipient type; can be told to throw.
    private sealed class ProgrammableResolver : INotificationRecipientResolver
    {
        public Dictionary<NotificationRecipientType, Guid[]> Map { get; } = new();
        public HashSet<NotificationRecipientType> Throws { get; } = new();
        public Task<IReadOnlyList<Guid>> ResolveAsync(RecipientSpec spec, RequestInstance instance, RequestApproval? step, CancellationToken ct)
        {
            if (Throws.Contains(spec.Type)) throw new InvalidOperationException("boom");
            return Task.FromResult<IReadOnlyList<Guid>>(Map.TryGetValue(spec.Type, out var v) ? v : Array.Empty<Guid>());
        }
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"wd_{Guid.NewGuid()}").Options, u);

    private static RequestInstance SeedInstance(ApplicationDbContext db, Guid tenant)
    {
        var emp = new Employee { Id = Guid.NewGuid(), TenantId = tenant, EmployeeNumber = "E1", FirstName = "F",
            LastName = "L", Email = "e@e.e", Gender = Gender.Male, DateOfBirth = new DateTime(1990,1,1),
            HireDate = new DateTime(2020,1,1), UserId = Guid.NewGuid() };
        db.Set<Employee>().Add(emp);
        var inst = new RequestInstance { Id = Guid.NewGuid(), TenantId = tenant, RequestTypeId = Guid.NewGuid(),
            RequestNumber = "REQ-1", EmployeeId = emp.Id, FormSubmissionId = Guid.NewGuid(),
            Status = RequestStatus.InProgress, SubmittedAt = DateTime.UtcNow };
        db.Set<RequestInstance>().Add(inst);
        return inst;
    }

    private static WorkflowNotificationRule Rule(Guid tenant, string? code, WorkflowNotificationEvent evt,
        int? step, params RecipientSpec[] recipients) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, RequestTypeCode = code, Event = evt, StepOrder = step,
        RecipientsJson = RecipientSpecParser.Serialize(recipients),
        SubjectEn = "S", SubjectAr = "س", BodyEn = "B", BodyAr = "ب", IsActive = true,
    };

    private static WorkflowNotificationDispatcher Sut(ApplicationDbContext db, INotificationRecipientResolver resolver, SpyNotifier notifier)
        // NOTE: constructor also needs the token resolver — pass a real DocumentTokenResolver(db) or a thin fake per its actual ctor.
        => new(db, resolver, notifier, new DocumentTokenResolverStub(), NullLogger<WorkflowNotificationDispatcher>.Instance);

    [Fact]
    public async Task Delivers_to_resolved_recipient()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var target = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { target };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(target);
    }

    [Fact]
    public async Task Most_specific_tier_wins()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var typeCode = "LEAVE_REQUEST";
        // load the instance's request type code by faking the lookup: dispatcher reads code via RequestType.
        db.Set<HR.Domain.Engines.Requests.RequestType>().Add(new RequestType { Id = inst.RequestTypeId,
            TenantId = u.TenantId, Code = typeCode, NameEn = "L", NameAr = "ل", FormDefinitionId = Guid.NewGuid(), IsActive = true });
        db.Set<WorkflowNotificationRule>().AddRange(
            Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null, new RecipientSpec(NotificationRecipientType.DirectManager)),
            Rule(u.TenantId, typeCode, WorkflowNotificationEvent.Submitted, null, new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var requester = Guid.NewGuid(); var manager = Guid.NewGuid();
        var resolver = new ProgrammableResolver();
        resolver.Map[NotificationRecipientType.Requester] = new[] { requester };
        resolver.Map[NotificationRecipientType.DirectManager] = new[] { manager };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(requester); // type+event tier beats global tier
    }

    [Fact]
    public async Task Dedups_same_user_across_recipients()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var dup = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester), new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver();
        resolver.Map[NotificationRecipientType.Requester] = new[] { dup };
        resolver.Map[NotificationRecipientType.DirectManager] = new[] { dup };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(dup);
    }

    [Fact]
    public async Task Duplicate_dispatch_is_a_noop()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var target = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { target };
        var spy = new SpyNotifier();
        var sut = Sut(db, resolver, spy);

        await sut.DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);
        await sut.DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle(); // second dispatch skipped by ledger
    }

    [Fact]
    public async Task Unresolved_recipient_is_skipped_not_redirected()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); // DirectManager maps to nothing
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().BeEmpty(); // never falls back to requester
    }

    [Fact]
    public async Task Resolver_exception_never_throws_to_caller()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Throws.Add(NotificationRecipientType.DirectManager);
        var spy = new SpyNotifier();

        var act = async () => await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Tenant_rules_are_isolated()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        // a rule owned by a DIFFERENT tenant must never fire
        db.Set<WorkflowNotificationRule>().Add(Rule(Guid.NewGuid(), null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { Guid.NewGuid() };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().BeEmpty();
    }
}
```
> The dispatcher's constructor takes the existing token resolver. If `DocumentTokenResolver` is not trivially constructable/fakeable, extract a tiny interface `IRequestTokenResolver { Task<IReadOnlyDictionary<string,string>> ResolveForRequestAsync(Guid, CancellationToken) }` implemented by the existing class, and use a stub in tests. Name that stub `DocumentTokenResolverStub` returning an empty dict. Keep the extraction minimal — one interface over the existing method, registered in DI.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~WorkflowNotificationDispatcherTests"`
Expected: FAIL — dispatcher not implemented.

- [ ] **Step 4: Implement the dispatcher**

`backend/src/HR.Modules/Platform/Services/Notifications/WorkflowNotificationDispatcher.cs`:
```csharp
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Documents; // IRequestTokenResolver (thin interface over DocumentTokenResolver)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Notifications;

public sealed class WorkflowNotificationDispatcher : IWorkflowNotificationDispatcher
{
    private const int StepAgnostic = -1;

    private readonly ApplicationDbContext _db;
    private readonly INotificationRecipientResolver _resolver;
    private readonly INotificationService _notifier;
    private readonly IRequestTokenResolver _tokens;
    private readonly ILogger<WorkflowNotificationDispatcher> _log;

    public WorkflowNotificationDispatcher(ApplicationDbContext db, INotificationRecipientResolver resolver,
        INotificationService notifier, IRequestTokenResolver tokens, ILogger<WorkflowNotificationDispatcher> log)
    { _db = db; _resolver = resolver; _notifier = notifier; _tokens = tokens; _log = log; }

    public async Task DispatchAsync(WorkflowNotificationEvent evt, RequestInstance instance, RequestApproval? step, CancellationToken ct)
    {
        try
        {
            var code = await _db.RequestTypes.Where(t => t.Id == instance.RequestTypeId)
                .Select(t => t.Code).FirstOrDefaultAsync(ct);
            var stepOrder = step?.StepOrder;

            var rules = await SelectWinningTierAsync(code, evt, stepOrder, ct);
            if (rules.Count == 0) return;

            // Resolve all recipients across the winning tier, guarding each independently.
            var userIds = new HashSet<Guid>();
            foreach (var rule in rules)
            {
                var parsed = RecipientSpecParser.ParseAndValidate(rule.RecipientsJson);
                if (!parsed.IsValid) { _log.LogWarning("Skipping rule {RuleId}: invalid RecipientsJson: {Errors}", rule.Id, string.Join("; ", parsed.Errors)); continue; }
                foreach (var spec in parsed.Envelope!.Recipients)
                {
                    try
                    {
                        var resolved = await _resolver.ResolveAsync(spec, instance, step, ct);
                        if (resolved.Count == 0)
                            _log.LogInformation("Recipient {Type} unresolved for request {Req}, event {Evt} — skipped.", spec.Type, instance.RequestNumber, evt);
                        foreach (var uid in resolved) userIds.Add(uid);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Recipient {Type} failed to resolve for request {Req} — skipped.", spec.Type, instance.RequestNumber);
                    }
                }
            }
            if (userIds.Count == 0) return;

            var tokens = await SafeTokensAsync(instance.Id, ct);
            var winningStepKey = stepOrder ?? StepAgnostic;

            foreach (var rule in rules)
            {
                foreach (var uid in userIds)
                {
                    // Idempotency: (instance, event, step, rule, user). Insert-then-catch on unique violation.
                    var already = await _db.WorkflowNotificationDispatches.AnyAsync(d =>
                        d.RequestInstanceId == instance.Id && d.Event == evt && d.StepOrder == winningStepKey
                        && d.RuleId == rule.Id && d.UserId == uid, ct);
                    if (already) continue;

                    _db.WorkflowNotificationDispatches.Add(new WorkflowNotificationDispatch
                    {
                        Id = Guid.NewGuid(), TenantId = instance.TenantId, RequestInstanceId = instance.Id,
                        Event = evt, StepOrder = winningStepKey, RuleId = rule.Id, UserId = uid, DispatchedAt = DateTime.UtcNow,
                    });

                    var subjAr = Render(rule.SubjectAr, tokens); var subjEn = Render(rule.SubjectEn, tokens);
                    var bodyAr = Render(rule.BodyAr, tokens); var bodyEn = Render(rule.BodyEn, tokens);
                    await _notifier.NotifyAsync(uid, subjAr, subjEn, bodyAr, bodyEn, "RequestWorkflow",
                        instance.Id, $"/requests/{instance.Id}", email: rule.ChannelEmail, ct: ct);
                }
            }
        }
        catch (Exception ex)
        {
            // Absolute guarantee: a notification problem NEVER fails the workflow transition.
            _log.LogError(ex, "Workflow notification dispatch failed for request {Req}, event {Evt} — swallowed.", instance.RequestNumber, evt);
        }
    }

    /// <summary>Most-specific non-empty tier wins (spec §6). Tiers, in order:
    /// type+event+step, type+event, global+event+step, global+event.</summary>
    private async Task<IReadOnlyList<WorkflowNotificationRule>> SelectWinningTierAsync(
        string? code, WorkflowNotificationEvent evt, int? step, CancellationToken ct)
    {
        var candidates = await _db.WorkflowNotificationRules
            .Where(r => r.IsActive && r.Event == evt
                && (r.RequestTypeCode == code || r.RequestTypeCode == null)
                && (r.StepOrder == step || r.StepOrder == null))
            .ToListAsync(ct);

        List<WorkflowNotificationRule> Tier(bool typed, bool stepped) => candidates
            .Where(r => (r.RequestTypeCode == code) == typed && (r.StepOrder == step && step != null) == stepped)
            .ToList();

        foreach (var (typed, stepped) in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            var tier = candidates.Where(r =>
                (r.RequestTypeCode != null) == typed &&
                (r.StepOrder != null) == stepped).ToList();
            if (tier.Count > 0) return tier;
        }
        return Array.Empty<WorkflowNotificationRule>();
    }

    private async Task<IReadOnlyDictionary<string, string>> SafeTokensAsync(Guid instanceId, CancellationToken ct)
    {
        try { return await _tokens.ResolveForRequestAsync(instanceId, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Token resolution failed for request {Id}; rendering with no tokens.", instanceId); return new Dictionary<string, string>(); }
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> tokens)
        => HR.Modules.Platform.Services.Documents.DocumentRenderer.ResolveTokens(template ?? "", tokens);
}
```
> Two integration details to confirm while implementing:
> 1. `RequestType` is exposed as `_db.RequestTypes`. If not, use `_db.Set<RequestType>()`.
> 2. `DocumentRenderer.ResolveTokens(string, IReadOnlyDictionary<string,string>)` is `public static` (spec §8). If its signature differs, adapt the call.
> The `Tier` local in `SelectWinningTierAsync` is redundant with the loop below it — delete `Tier` and keep only the `foreach` tier scan (kept both here only to show intent; ship the loop). The loop's `typed`/`stepped` predicate: a tier is "typed" when `RequestTypeCode != null` and "stepped" when `StepOrder != null`; the candidate `Where` already guarantees each row matches the dispatch's code/step or is null, so ranking by null-ness yields the precedence order in spec §6.

- [ ] **Step 5: Register the token interface + dispatcher in DI**

Extract the thin token interface (if not already present):
`backend/src/HR.Modules/Platform/Services/Documents/IRequestTokenResolver.cs`:
```csharp
namespace HR.Modules.Platform.Services.Documents;

/// <summary>Thin seam over DocumentTokenResolver so the dispatcher can be unit-tested and depend on
/// an abstraction. One method, one implementation (the existing resolver).</summary>
public interface IRequestTokenResolver
{
    Task<IReadOnlyDictionary<string, string>> ResolveForRequestAsync(Guid requestInstanceId, CancellationToken ct);
}
```
Make `DocumentTokenResolver` implement it (add `: IRequestTokenResolver` and ensure the method signature matches; if the existing method returns `Task<Dictionary<string,string>>`, that satisfies `IReadOnlyDictionary` covariantly only if you change the return type to `IReadOnlyDictionary` or add an explicit interface method — simplest: change the public method's return type to `Task<IReadOnlyDictionary<string,string>>`).

In `DependencyInjection.cs` add:
```csharp
        services.AddScoped<IRequestTokenResolver>(sp => sp.GetRequiredService<DocumentTokenResolver>());
        services.AddScoped<IWorkflowNotificationDispatcher, WorkflowNotificationDispatcher>();
```
(If `DocumentTokenResolver` isn't already registered, add `services.AddScoped<DocumentTokenResolver>();` first.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~WorkflowNotificationDispatcherTests"`
Expected: PASS (7). Iterate on precedence predicate + DbSet names until green.

- [ ] **Step 7: Commit + push**

```bash
git add backend/src/HR.Modules/Platform/Services/Notifications/IWorkflowNotificationDispatcher.cs backend/src/HR.Modules/Platform/Services/Notifications/WorkflowNotificationDispatcher.cs backend/src/HR.Modules/Platform/Services/Documents/IRequestTokenResolver.cs backend/src/HR.Modules/Platform/Services/Documents/DocumentTokenResolver.cs backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationDispatcherTests.cs
git commit -m "feat(notifications): workflow notification dispatcher (precedence, dedup, idempotency, failure isolation)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 5: Wire the dispatcher into RequestEngine (6 events) + remove hardcoded notifies

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/RequestEngine.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationDispatcherTests.cs` (add a regression fact) — or a new `RequestEngineNotificationWiringTests.cs` if RequestEngine is hard to instantiate in tests.

**Interfaces:**
- Consumes: `IWorkflowNotificationDispatcher` (Task 4).
- Produces: `RequestEngine` dispatches `Submitted`, `StepAssigned`, `StepApproved`, `Rejected`, `Returned`, `FinalApproved` at the 6 documented points.

- [ ] **Step 1: Inject the dispatcher**

In `RequestEngine.cs`, add a `private readonly IWorkflowNotificationDispatcher _dispatcher;` field and add it to the constructor parameter list + assignment (follow the existing `_completion` / `_docGen` injection pattern).

- [ ] **Step 2: Replace the submit notify (line ~144)**

Where `SubmitAsync` currently notifies the first approver (`RequestEngine.cs:144-150`), after the chain is built and `instance.Status = InProgress`, replace the inline `NotifyAsync(approverId, ...)` with:
```csharp
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Submitted, instance, null, ct);
            var firstStep = chain.OrderBy(a => a.StepOrder).FirstOrDefault();
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepAssigned, instance, firstStep, ct);
```
(For the no-chain branch that runs completion immediately, also dispatch `FinalApproved` after `completion.Success`, mirroring Step 6.)

- [ ] **Step 3: Replace the next-approver notify (line ~224)**

In `DecideAsync`, where it currently notifies `nextApprover` after advancing (`RequestEngine.cs:219-226`), replace the inline `NotifyAsync(nextApprover, ...)` with:
```csharp
                await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepAssigned, instance, next, ct);
```
And where a step is approved but not final (`RequestEngine.cs:209`, after `step.Status = Approved`), add:
```csharp
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.StepApproved, instance, step, ct);
```

- [ ] **Step 4: Replace reject notify (line ~203)**

In the reject branch, replace `NotifySubmitterAsync(instance, "تم رفض طلبك", ...)` with:
```csharp
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Rejected, instance, step, ct);
```

- [ ] **Step 5: Replace return notify (line ~274)**

In `ReturnAsync`, replace `NotifySubmitterAsync(instance, "أُعيد طلبك للتعديل", ...)` with:
```csharp
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.Returned, instance, step, ct);
```

- [ ] **Step 6: Replace final-approval notify (line ~238)**

In `DecideAsync`, inside `if (completion.Success)`, replace `NotifySubmitterAsync(instance, "تمت الموافقة على طلبك", ...)` with:
```csharp
            await _dispatcher.DispatchAsync(WorkflowNotificationEvent.FinalApproved, instance, null, ct);
```

- [ ] **Step 7: Delete the now-unused private helpers**

Remove `NotifyAsync` and `NotifySubmitterAsync` from `RequestEngine` **only if** they have no remaining callers (search the file). Move the private resolver methods (`ManagerUserAsync`, `DepartmentHeadUserAsync`, `UserByRoleIdAsync`, `UserByRoleKeywordAsync`, `ManagerChainUserAsync`) — leave those; they are still used by `ResolveApproverAsync` for building the approval chain. Only the notification helpers go.

- [ ] **Step 8: Build**

Run: `dotnet build backend/HR.sln`
Expected: succeeds. Fix any constructor-injection sites (DI is by interface, already registered in Task 4; the `RequestEngine` registration needs no change since it resolves constructor args from DI).

- [ ] **Step 9: Add a wiring regression test**

Add to `WorkflowNotificationDispatcherTests.cs` a test that a seeded `LEAVE_REQUEST` `StepAssigned`→`CurrentApprover` rule causes the assigned approver to be notified when dispatched with a pending step (this proves the approver-on-assign behavior survives the refactor):
```csharp
    [Fact]
    public async Task StepAssigned_notifies_current_approver()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        db.Set<HR.Domain.Engines.Requests.RequestType>().Add(new RequestType { Id = inst.RequestTypeId,
            TenantId = u.TenantId, Code = "LEAVE_REQUEST", NameEn = "L", NameAr = "ل", FormDefinitionId = Guid.NewGuid(), IsActive = true });
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, "LEAVE_REQUEST", WorkflowNotificationEvent.StepAssigned, null,
            new RecipientSpec(NotificationRecipientType.CurrentApprover)));
        await db.SaveChangesAsync();
        var approver = Guid.NewGuid();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.CurrentApprover] = new[] { approver };
        var spy = new SpyNotifier();
        var step = new RequestApproval { Id = Guid.NewGuid(), RequestInstanceId = inst.Id, StepOrder = 1,
            StepNameAr = "1", StepNameEn = "1", ApproverType = ApproverType.DirectManager,
            AssignedToUserId = approver, Status = RequestApprovalStatus.Pending };

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.StepAssigned, inst, step, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(approver);
    }
```

- [ ] **Step 10: Run the full Platform test suite**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: PASS (all — previously green count + the new notification tests). Investigate any regression in existing RequestEngine tests (they may assert on the old inline notifications — update those assertions to expect dispatcher-driven behavior, or to no longer assert on removed helpers).

- [ ] **Step 11: Commit + push**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/RequestEngine.cs backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationDispatcherTests.cs
git commit -m "refactor(requests): route lifecycle notifications through the workflow dispatcher

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Task 6: Seed default Leave rules + non-destructive provisioning

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs`
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationSeedTests.cs`

**Interfaces:**
- Consumes: `WorkflowNotificationRule` (Task 1), `RecipientSpecParser` (Task 2), `RequestType`.
- Produces: `SystemWorkflowNotificationRules.For(string requestCode) -> IReadOnlyList<SeededRule>`; `RequestProvisioningService.CurrentSeedVersion == 4`; `ReconcileWorkflowNotificationRules(RequestType)` inserts missing / upgrades untouched / never touches customized-or-tenant rules.

- [ ] **Step 1: Define the seed catalog**

`backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs`:
```csharp
using HR.Application.Engines.Notifications;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Requests;

public sealed record SeededRule(
    string SystemKey, WorkflowNotificationEvent Event, int? StepOrder,
    IReadOnlyList<RecipientSpec> Recipients,
    string SubjectAr, string SubjectEn, string BodyAr, string BodyEn);

/// <summary>Product-default workflow notification rules per request code. Mirrors SystemRequestEffects:
/// declared here so provisioning can reconcile them on a SeedVersion bump. Seeded rows are marked
/// system-owned and are never overwritten once a tenant customizes them.</summary>
public static class SystemWorkflowNotificationRules
{
    private static RecipientSpec R(NotificationRecipientType t) => new(t);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<SeededRule>> Rules =
        new Dictionary<string, IReadOnlyList<SeededRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["LEAVE_REQUEST"] = new[]
            {
                new SeededRule("LEAVE_REQUEST:Submitted:Requester", WorkflowNotificationEvent.Submitted, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم استلام طلب الإجازة", "Leave request received",
                    "تم استلام طلب إجازتك رقم {{Request.Number}} وهو قيد المراجعة.",
                    "Your leave request {{Request.Number}} was received and is under review."),
                new SeededRule("LEAVE_REQUEST:StepAssigned:CurrentApprover", WorkflowNotificationEvent.StepAssigned, null,
                    new[] { R(NotificationRecipientType.CurrentApprover) },
                    "طلب إجازة بانتظار موافقتك", "A leave request needs your approval",
                    "طلب إجازة رقم {{Request.Number}} من {{Employee.FullName}} بانتظار موافقتك.",
                    "Leave request {{Request.Number}} from {{Employee.FullName}} awaits your approval."),
                new SeededRule("LEAVE_REQUEST:Rejected:Requester", WorkflowNotificationEvent.Rejected, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم رفض طلب الإجازة", "Leave request rejected",
                    "نأسف لإبلاغك برفض طلب إجازتك رقم {{Request.Number}}.",
                    "Your leave request {{Request.Number}} was rejected."),
                new SeededRule("LEAVE_REQUEST:Returned:Requester", WorkflowNotificationEvent.Returned, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "أُعيد طلب الإجازة للتعديل", "Leave request returned",
                    "أُعيد طلب إجازتك رقم {{Request.Number}} للتعديل. يرجى مراجعته.",
                    "Your leave request {{Request.Number}} was returned for changes."),
                new SeededRule("LEAVE_REQUEST:FinalApproved:Requester", WorkflowNotificationEvent.FinalApproved, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تمت الموافقة على طلب الإجازة", "Leave request approved",
                    "تمت الموافقة على طلب إجازتك رقم {{Request.Number}}.",
                    "Your leave request {{Request.Number}} has been approved."),
            },
        };

    public static IReadOnlyList<SeededRule> For(string requestCode)
        => Rules.TryGetValue(requestCode, out var r) ? r : Array.Empty<SeededRule>();
}
```

- [ ] **Step 2: Write failing seed/provisioning tests**

`backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationSeedTests.cs`:
```csharp
using FluentAssertions;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class WorkflowNotificationSeedTests
{
    private sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"seed_{Guid.NewGuid()}").Options, u);

    private static RequestType LeaveType(Guid tenant) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, Code = "LEAVE_REQUEST", NameEn = "Leave", NameAr = "إجازة",
        FormDefinitionId = Guid.NewGuid(), IsActive = true, IsSystem = true, SeedVersion = 3,
    };

    [Fact]
    public void Seed_catalog_has_five_leave_rules()
        => SystemWorkflowNotificationRules.For("LEAVE_REQUEST").Should().HaveCount(5);

    [Fact]
    public void CurrentSeedVersion_is_four()
        => RequestProvisioningService.CurrentSeedVersion.Should().Be(4);

    // The reconcile method is exercised via the provisioning service. If it is private, expose an
    // internal method ReconcileWorkflowNotificationRules(RequestType) and add InternalsVisibleTo, or
    // test through the public provisioning entry point. Below assumes an internal method.
    [Fact]
    public async Task Reconcile_inserts_missing_rules_once()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var type = LeaveType(u.TenantId); db.Set<RequestType>().Add(type); await db.SaveChangesAsync();

        var svc = ProvisioningTestFactory.Create(db, u);
        svc.ReconcileWorkflowNotificationRules(type);
        await db.SaveChangesAsync();
        svc.ReconcileWorkflowNotificationRules(type); // idempotent second pass
        await db.SaveChangesAsync();

        db.Set<WorkflowNotificationRule>().Count(r => r.TenantId == u.TenantId).Should().Be(5);
        db.Set<WorkflowNotificationRule>().All(r => r.IsSystemOwned).Should().BeTrue();
    }

    [Fact]
    public async Task Reconcile_never_overwrites_a_customized_rule()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var type = LeaveType(u.TenantId); db.Set<RequestType>().Add(type); await db.SaveChangesAsync();
        var svc = ProvisioningTestFactory.Create(db, u);
        svc.ReconcileWorkflowNotificationRules(type); await db.SaveChangesAsync();

        var rule = db.Set<WorkflowNotificationRule>().First(r => r.SystemKey == "LEAVE_REQUEST:Submitted:Requester");
        rule.SubjectEn = "TENANT EDIT"; rule.IsCustomized = true; await db.SaveChangesAsync();

        svc.ReconcileWorkflowNotificationRules(type); await db.SaveChangesAsync();

        db.Set<WorkflowNotificationRule>().First(r => r.SystemKey == "LEAVE_REQUEST:Submitted:Requester")
            .SubjectEn.Should().Be("TENANT EDIT");
    }
}
```
> `ProvisioningTestFactory.Create(db, user)` is a tiny test helper you add in the test project that constructs `RequestProvisioningService` with the same fakes its other tests use (mirror the constructor args used by existing provisioning tests — check `RequestProvisioningService`'s ctor and any existing provisioning test for the exact dependencies: `ApplicationDbContext`, `IRequestSeeder`, `ILogger<>`, tenant scope). If reconcile is naturally reachable only through the public `ProvisionAsync`, test through that instead and assert the rule rows afterward.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~WorkflowNotificationSeedTests"`
Expected: FAIL.

- [ ] **Step 4: Implement the reconcile + bump the seed version**

In `RequestProvisioningService.cs`:
1. Change `public const int CurrentSeedVersion = 3;` → `= 4;`
2. Beside `changes.AddRange(ReconcileRequiredEffects(type));` (line ~95), add:
```csharp
            changes.AddRange(ReconcileWorkflowNotificationRules(type));
```
3. Add the method (mirror `ReconcileRequiredEffects`'s shape + the SP0 non-destructive guard):
```csharp
    /// <summary>Seed the product-default workflow notification rules for a system request type.
    /// Non-destructive: inserts only rules whose SystemKey is absent, upgrades an untouched
    /// system-owned rule's content, and never modifies a tenant-authored or customized rule.</summary>
    private List<string> ReconcileWorkflowNotificationRules(RequestType type)
    {
        var changes = new List<string>();
        var seeded = SystemWorkflowNotificationRules.For(type.Code);
        if (seeded.Count == 0) return changes;

        var existing = _db.WorkflowNotificationRules
            .Where(r => r.TenantId == type.TenantId && r.SystemKey != null)
            .ToDictionary(r => r.SystemKey!, StringComparer.Ordinal);

        foreach (var s in seeded)
        {
            if (existing.TryGetValue(s.SystemKey, out var row))
            {
                if (row.IsCustomized) continue;           // tenant edited a system rule → never touch
                // Safe in-place upgrade of an untouched system rule.
                row.Event = s.Event; row.StepOrder = s.StepOrder;
                row.RecipientsJson = RecipientSpecParser.Serialize(s.Recipients);
                row.SubjectAr = s.SubjectAr; row.SubjectEn = s.SubjectEn;
                row.BodyAr = s.BodyAr; row.BodyEn = s.BodyEn;
                continue;
            }
            _db.WorkflowNotificationRules.Add(new WorkflowNotificationRule
            {
                Id = Guid.NewGuid(), TenantId = type.TenantId, RequestTypeCode = type.Code,
                Event = s.Event, StepOrder = s.StepOrder,
                RecipientsJson = RecipientSpecParser.Serialize(s.Recipients),
                SubjectAr = s.SubjectAr, SubjectEn = s.SubjectEn, BodyAr = s.BodyAr, BodyEn = s.BodyEn,
                ChannelBell = true, ChannelEmail = true, IsActive = true,
                IsSystemOwned = true, SystemKey = s.SystemKey, IsCustomized = false,
            });
            changes.Add($"+notif:{s.SystemKey}");
        }
        return changes;
    }
```
Add `using HR.Application.Engines.Notifications;` and `using HR.Domain.Engines.Notifications;` to the file if missing.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter "FullyQualifiedName~WorkflowNotificationSeedTests"`
Expected: PASS (4). If reconcile visibility blocks the test, mark it `internal` + add `[assembly: InternalsVisibleTo("HR.Modules.Platform.Tests")]` where the other Platform internals are exposed (check if that attribute already exists for this test project).

- [ ] **Step 6: Full suite + build**

Run: `dotnet build backend/HR.sln` then `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: build succeeds; all tests PASS.

- [ ] **Step 7: Commit + push**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs backend/tests/HR.Modules.Platform.Tests/Notifications/WorkflowNotificationSeedTests.cs
git commit -m "feat(notifications): seed default LEAVE_REQUEST rules + non-destructive provisioning (SeedVersion 3->4)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

## Post-implementation (user-gated, do NOT run without the user)

- **Batch migration apply:** SP0's `FormFieldClassificationMetadata` + this SP's `WorkflowNotifications` migrations apply together to Azure Postgres (firewall rule + `dotnet ef database update` per the checkpoint's KEY MECHANICS), then delete the temp firewall rule.
- **API redeploy:** zip-deploy to `hrcloud-api-v4xd`.
- **Re-provision each tenant** (POST `/api/requests/provision`, needs `Platform.MasterData.Create`) so `SeedVersion` 3→4 seeds the Leave notification rules.
- **Verify:** submit a leave request → requester gets "received" bell/email; approver gets "awaiting your approval"; approve → requester gets "approved".

---

## Self-Review

**Spec coverage:**
- §3 four-stage pipeline → Tasks 3 (resolver), 4 (dispatcher: lookup+render+deliver). ✅
- §4 failure isolation → Task 4 Steps 4 (outer try/catch, per-recipient guard) + tests `Resolver_exception_never_throws`, dispatcher enqueue-only. ✅
- §5 recipient model + validation → Task 2 (parser: 10 tests covering type/refId/unknown-prop/max/dedup/version/malformed). ✅
- §6 precedence → Task 4 `SelectWinningTierAsync` + `Most_specific_tier_wins`. ✅
- §7 dedup + idempotency → Task 4 `Dedups_same_user`, `Duplicate_dispatch_is_a_noop` + ledger unique index (Task 1). ✅
- §8 templates + token whitelist → Task 2 `NotificationTokenWhitelist` + Task 4 render via `ResolveTokens` (leaves unknown visible). ✅
- §9 seed non-destructive → Task 6 `Reconcile_inserts_missing_rules_once`, `Reconcile_never_overwrites_a_customized_rule`. ✅
- §10 capability registry / hide unimplemented → Task 2 `NotificationCapabilityRegistry` + parser rejects deferred types (`Rejects_deferred_recipient_type`). ✅
- §11 data model + indexes → Task 1. ✅
- §12 RequestEngine 6 points → Task 5. ✅
- §13 test list → tenant isolation (Task 4), dedup (Task 4), unresolved (Task 4), invalid JSON (Task 2), missing token (Task 2), delivery failure (Task 4), duplicate dispatch (Task 4), precedence (Task 4), seed non-destruction (Task 6). ✅ All nine present.

**Placeholder scan:** No TBD/TODO. Two spots deliberately flag verification-against-real-signatures (token keys vs `DocumentTokenResolver`; DbSet names; provisioning ctor) with explicit fallback instructions — these are integration-confirmation notes, not deferred work, and each names exactly what to check and the default.

**Type consistency:** `RecipientSpec(Type, RefId)`, `RecipientParseResult{Envelope,Errors,IsValid}`, `INotificationRecipientResolver.ResolveAsync(spec, instance, step, ct)`, `IWorkflowNotificationDispatcher.DispatchAsync(evt, instance, step, ct)`, `SystemWorkflowNotificationRules.For(code)`, `RequestProvisioningService.CurrentSeedVersion`, `WorkflowNotificationDispatch` key `(RequestInstanceId, Event, StepOrder, RuleId, UserId)` — all consistent across tasks. The dispatcher `SelectWinningTierAsync` note tells the implementer to ship the loop and delete the vestigial `Tier` local (avoids a two-name bug).

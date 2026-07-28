# SP2 — Attendance Correction (real recalculation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `ATTENDANCE_CORRECTION` recompute a day's attendance from corrected punches (via the existing attendance engine), with HH:mm validation, effect-level idempotency, a finalized-payroll guard (block-unless-authorized + explicit signal), lifecycle notifications, and a non-destructive provisioning upgrade.

**Architecture:** The completion effect `AttendanceCorrectionExecutor` is rewritten to route through `IAttendanceService.CorrectAsync` / `AddManualPunchAsync` (which audit + `RecalcAsync` → `IAttendanceCalculationService.Calculate`), replacing brute-force penalty-zeroing. Declarations (form fields, effect-action inputs, system effect mapping, notification rules) are added as data, reconciled onto existing tenants by a `SeedVersion 4→5` bump. Notifications ride the existing SP1 dispatcher.

**Tech Stack:** .NET 8, EF Core (InMemory for tests), xUnit + FluentAssertions, hand-written fakes (no Moq in the suite).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-07-28-sp2-attendance-correction-recalculation-design.md` (authoritative).
- **Reuse, don't rebuild:** no shift/overtime math in SP2 — feed corrected punches to `IAttendanceService`. Overnight (`AttendanceCalculationService.cs:92-93`), effective-shift-for-date (`ShiftResolver.Resolve`), and policy (`LoadPolicyAsync`) are inherited.
- **Timezone:** stay timezone-naïve like the rest of the engine (`CombineTime` treats "HH:mm" as UTC-kind wall-clock). Do NOT add UTC↔local conversion. Add a `// TODO(tz):` marker only.
- **Punch fields:** `FieldType.Text`, "HH:mm", both optional, **≥1 required**; `reason` required.
- **Authorization for finalized periods:** permission string `Payroll.Run.Amend`.
- **Idempotency key:** an `AttendanceRecord` is "already corrected by this request" when `Source == AttendanceSources.AttendanceCorrection && ReferenceId == ctx.RequestInstanceId`.
- **Provisioning:** only ever touch **system-owned, un-customized** `ATTENDANCE_CORRECTION`; never overwrite tenant-customized forms/mappings/rules; never delete.
- **Commits:** one logical change per commit; every commit builds and tests green; end each message with the `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer. Push both remotes (`origin`, `sanad`) after each committed task.
- **Build/test commands:** build `dotnet build backend/HR.sln`; run finance suite `dotnet test backend/tests/HR.Domain.Finance.Tests`; platform suite `dotnet test backend/tests/HR.Modules.Platform.Tests`.

## File Structure

- Modify `backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs` — the rewrite (deps, validate, idempotency, finalized guard, route to service, signal).
- Create `backend/src/HR.Modules/Attendance/Completion/PunchTime.cs` — small static HH:mm parser/validator (independently testable).
- Modify `backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs` — add 5 `ATTENDANCE_CORRECTION` rules.
- Modify `backend/src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs` — `AttendanceCorrectionForm()` gains `checkIn`/`checkOut`.
- Modify `backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs` — `AttendanceCorrect` descriptor gains `checkIn`/`checkOut` inputs; relabel.
- Modify `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs` — map `checkIn`/`checkOut`.
- Modify `backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs` — `CurrentSeedVersion` 4→5 + reconcile extension (add missing system form fields; refresh system effect config for system-owned, un-customized types).
- Create `backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs` — executor unit tests (fake `IAttendanceService`/guard/resolver) + one real-service integration test.
- Modify `backend/tests/HR.Domain.Finance.Tests/AttendanceExcuseExecutorTests.cs` — update the now-obsolete `Correction_sets_present_and_zeroes` test to the new behavior.
- Create `backend/tests/HR.Modules.Platform.Tests/Requests/AttendanceCorrectionProvisioningTests.cs` — notification-seeding + reconcile tests.

---

### Task 1: Seed notification rules for ATTENDANCE_CORRECTION

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Requests/AttendanceCorrectionProvisioningTests.cs`

**Interfaces:**
- Consumes: `SystemWorkflowNotificationRules.For(string)`, `SeededRule`, `WorkflowNotificationEvent`, `NotificationRecipientType` (existing).
- Produces: `For("ATTENDANCE_CORRECTION")` → 5 `SeededRule`s.

- [ ] **Step 1: Write the failing test**

Create the test file:

```csharp
using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Requests;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

public class AttendanceCorrectionProvisioningTests
{
    [Fact]
    public void Seeds_five_attendance_correction_rules()
    {
        var rules = SystemWorkflowNotificationRules.For("ATTENDANCE_CORRECTION");
        rules.Should().HaveCount(5);
        rules.Select(r => r.Event).Should().BeEquivalentTo(new[]
        {
            WorkflowNotificationEvent.Submitted,
            WorkflowNotificationEvent.StepAssigned,
            WorkflowNotificationEvent.Rejected,
            WorkflowNotificationEvent.Returned,
            WorkflowNotificationEvent.FinalApproved,
        });
        rules.Single(r => r.Event == WorkflowNotificationEvent.StepAssigned)
             .Recipients.Single().Type.Should().Be(NotificationRecipientType.CurrentApprover);
        rules.Where(r => r.Event != WorkflowNotificationEvent.StepAssigned)
             .Should().OnlyContain(r => r.Recipients.Single().Type == NotificationRecipientType.Requester);
        rules.Select(r => r.SystemKey).Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter Seeds_five_attendance_correction_rules`
Expected: FAIL (returns 0 rules).

- [ ] **Step 3: Add the rules**

In `SystemWorkflowNotificationRules.cs`, add an entry to the `Rules` dictionary (mirror the `LEAVE_REQUEST` block, attendance copy):

```csharp
["ATTENDANCE_CORRECTION"] = new[]
{
    new SeededRule("ATTENDANCE_CORRECTION:Submitted:Requester", WorkflowNotificationEvent.Submitted, null,
        new[] { R(NotificationRecipientType.Requester) },
        "تم استلام طلب تصحيح الحضور", "Attendance correction received",
        "تم استلام طلب تصحيح الحضور رقم {{Request.Number}} وهو قيد المراجعة.",
        "Your attendance correction {{Request.Number}} was received and is under review."),
    new SeededRule("ATTENDANCE_CORRECTION:StepAssigned:CurrentApprover", WorkflowNotificationEvent.StepAssigned, null,
        new[] { R(NotificationRecipientType.CurrentApprover) },
        "طلب تصحيح حضور بانتظار موافقتك", "An attendance correction needs your approval",
        "طلب تصحيح حضور رقم {{Request.Number}} من {{Employee.FullName}} بانتظار موافقتك.",
        "Attendance correction {{Request.Number}} from {{Employee.FullName}} awaits your approval."),
    new SeededRule("ATTENDANCE_CORRECTION:Rejected:Requester", WorkflowNotificationEvent.Rejected, null,
        new[] { R(NotificationRecipientType.Requester) },
        "تم رفض طلب تصحيح الحضور", "Attendance correction rejected",
        "نأسف لإبلاغك برفض طلب تصحيح الحضور رقم {{Request.Number}}.",
        "Your attendance correction {{Request.Number}} was rejected."),
    new SeededRule("ATTENDANCE_CORRECTION:Returned:Requester", WorkflowNotificationEvent.Returned, null,
        new[] { R(NotificationRecipientType.Requester) },
        "أُعيد طلب تصحيح الحضور للتعديل", "Attendance correction returned",
        "أُعيد طلب تصحيح الحضور رقم {{Request.Number}} للتعديل. يرجى مراجعته.",
        "Your attendance correction {{Request.Number}} was returned for changes."),
    new SeededRule("ATTENDANCE_CORRECTION:FinalApproved:Requester", WorkflowNotificationEvent.FinalApproved, null,
        new[] { R(NotificationRecipientType.Requester) },
        "تمت الموافقة على تصحيح الحضور", "Attendance correction approved",
        "تمت الموافقة على طلب تصحيح الحضور رقم {{Request.Number}} وتم تطبيقه.",
        "Your attendance correction {{Request.Number}} has been approved and applied."),
},
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter Seeds_five_attendance_correction_rules`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs backend/tests/HR.Modules.Platform.Tests/Requests/AttendanceCorrectionProvisioningTests.cs
git commit -m "feat(sp2): seed 5 ATTENDANCE_CORRECTION notification rules

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 2: HH:mm parser/validator

**Files:**
- Create: `backend/src/HR.Modules/Attendance/Completion/PunchTime.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs` (new file — start it here)

**Interfaces:**
- Produces: `static bool PunchTime.IsValid(string? hhmm)` — true iff null/blank (means "unchanged") OR a valid 24h `HH:mm`. `static bool PunchTime.HasValue(string? hhmm)` — true iff non-blank.

- [ ] **Step 1: Write the failing test**

Create `AttendanceCorrectionExecutorTests.cs` with the harness + first test:

```csharp
using System.Text.Json;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendanceCorrectionExecutorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("08:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("25:00", false)]
    [InlineData("8am", false)]
    [InlineData("8:5", false)]
    public void PunchTime_validates_hhmm(string? input, bool expected)
        => PunchTime.IsValid(input).Should().Be(expected);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter PunchTime_validates_hhmm`
Expected: FAIL (`PunchTime` does not exist).

- [ ] **Step 3: Implement `PunchTime`**

```csharp
using System.Text.RegularExpressions;

namespace HR.Modules.Attendance.Completion;

/// <summary>Validates the "HH:mm" (24h) punch strings the correction form submits. A blank string is
/// valid and means "leave this punch unchanged". TODO(tz): times are wall-clock, timezone-naïve, matching
/// the rest of the attendance engine — do not convert here until the engine migrates system-wide.</summary>
public static class PunchTime
{
    private static readonly Regex Hhmm = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);
    public static bool HasValue(string? hhmm) => !string.IsNullOrWhiteSpace(hhmm);
    public static bool IsValid(string? hhmm) => !HasValue(hhmm) || Hhmm.IsMatch(hhmm!.Trim());
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter PunchTime_validates_hhmm`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Attendance/Completion/PunchTime.cs backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs
git commit -m "feat(sp2): HH:mm punch validator

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 3: Executor routing — validate + route to IAttendanceService (replaces zeroing)

**Files:**
- Modify: `backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs`

**Interfaces:**
- Consumes: `IAttendanceService.CorrectAsync(Guid, CorrectAttendanceRequest, ct)`, `AddManualPunchAsync(ManualPunchRequest, ct) : Task<Guid>`; `IPayrollPeriodGuard.EnsurePeriodOpenForAsync(Guid,DateTime,ct)`; `IPermissionResolver.ResolveAsync(Guid,ct)`; `EffectContext`; `AttendanceSources.AttendanceCorrection`.
- Produces: rewritten `AttendanceCorrectionExecutor` ctor `(ApplicationDbContext db, IAttendanceService attendance, IPayrollPeriodGuard payrollGuard, IPermissionResolver permissions)` (notification added in Task 5).

For unit tests, use hand-written fakes (no Moq). Add these fakes to the test file:

```csharp
private sealed class FakeUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
    public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    public string? Email => "t@t.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}
private static ApplicationDbContext Ctx(string n) => new(
    new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options, new FakeUser());
private static DateTime Utc(int y,int m,int d) => new(y,m,d,0,0,0,DateTimeKind.Utc);
private static EffectContext Eff(Guid emp, string json, Guid? actor = null) => new()
{
    RequestInstanceId = Guid.NewGuid(), RequestNumber="R1", RequestTypeCode="ATTENDANCE_CORRECTION",
    EmployeeId = emp, ActorUserId = actor, Payload = JsonDocument.Parse(json).RootElement,
};

// Records how the executor called the attendance engine; mutates the InMemory record to simulate recalc.
private sealed class FakeAttendance : HR.Modules.Attendance.Services.IAttendanceService
{
    private readonly ApplicationDbContext _db;
    public FakeAttendance(ApplicationDbContext db) => _db = db;
    public Guid? CorrectedRecordId; public HR.Modules.Attendance.DTOs.CorrectAttendanceRequest? CorrectReq;
    public HR.Modules.Attendance.DTOs.ManualPunchRequest? ManualReq;
    public Task CorrectAsync(Guid recordId, HR.Modules.Attendance.DTOs.CorrectAttendanceRequest req, CancellationToken ct)
    { CorrectedRecordId = recordId; CorrectReq = req; return Task.CompletedTask; }
    public async Task<Guid> AddManualPunchAsync(HR.Modules.Attendance.DTOs.ManualPunchRequest req, CancellationToken ct)
    {
        ManualReq = req;
        var rec = new AttendanceRecord { EmployeeId = req.EmployeeId, Date = req.Date, Status = AttendanceStatus.Present };
        _db.AttendanceRecords.Add(rec); await _db.SaveChangesAsync(ct); return rec.Id;
    }
    public Task<HR.Modules.Attendance.DTOs.AttendanceDailyResponse> GetDailyAsync(HR.Modules.Attendance.Services.AttendanceFilter f, DateTime d, CancellationToken ct) => throw new NotImplementedException();
    public Task<List<HR.Modules.Attendance.DTOs.AttendanceDayDto>> GetRangeRowsAsync(HR.Modules.Attendance.Services.AttendanceFilter f, DateTime a, DateTime b, CancellationToken ct) => throw new NotImplementedException();
    public Task<HR.Modules.Attendance.DTOs.AttendanceSummaryResponse> GetSummaryAsync(HR.Modules.Attendance.Services.AttendanceFilter f, DateTime a, DateTime b, CancellationToken ct) => throw new NotImplementedException();
    public Task<HR.Modules.Attendance.DTOs.AttendanceDetailDto?> GetDetailAsync(Guid id, CancellationToken ct) => throw new NotImplementedException();
}
private sealed class OpenPeriodGuard : HR.Application.Engines.Finance.IPayrollPeriodGuard
{ public Task EnsurePeriodOpenForAsync(Guid e, DateTime d, CancellationToken ct = default) => Task.CompletedTask; }
private sealed class FakePerms : HR.Application.Engines.Permissions.IPermissionResolver
{ private readonly string[] _p; public FakePerms(params string[] p)=>_p=p;
  public Task<IReadOnlyList<string>> ResolveAsync(Guid userId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<string>)_p); }

private static AttendanceCorrectionExecutor Sut(ApplicationDbContext db, HR.Modules.Attendance.Services.IAttendanceService att,
    HR.Application.Engines.Finance.IPayrollPeriodGuard? guard = null, HR.Application.Engines.Permissions.IPermissionResolver? perms = null)
    => new(db, att, guard ?? new OpenPeriodGuard(), perms ?? new FakePerms());
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Found_record_routes_to_CorrectAsync_and_stamps_reference()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    var rec = new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 45 };
    db.AttendanceRecords.Add(rec); await db.SaveChangesAsync();
    var att = new FakeAttendance(db);
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:00\",\"checkOut\":\"17:00\",\"reason\":\"fixed\"}");

    var result = await Sut(db, att).ExecuteAsync(ctx, default);

    att.CorrectedRecordId.Should().Be(rec.Id);
    att.CorrectReq!.CheckIn.Should().Be("08:00");
    att.CorrectReq!.CheckOut.Should().Be("17:00");
    att.CorrectReq!.Reason.Should().Be("fixed");
    var reloaded = await db.AttendanceRecords.SingleAsync(a => a.Id == rec.Id);
    reloaded.Source.Should().Be(AttendanceSources.AttendanceCorrection);
    reloaded.ReferenceId.Should().Be(ctx.RequestInstanceId);
    result.IsSkipped.Should().BeFalse();
    result.TargetRecordId.Should().Be(rec.Id);
}

[Fact]
public async Task Missing_record_routes_to_AddManualPunch()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    var att = new FakeAttendance(db);
    var ctx = Eff(emp, "{\"date\":\"2026-07-06\",\"checkOut\":\"17:00\",\"reason\":\"forgot out\"}");

    var result = await Sut(db, att).ExecuteAsync(ctx, default);

    att.ManualReq!.EmployeeId.Should().Be(emp);
    att.ManualReq!.CheckIn.Should().BeNull();
    att.ManualReq!.CheckOut.Should().Be("17:00");
    att.ManualReq!.Notes.Should().Be("forgot out");
    var rec = await db.AttendanceRecords.SingleAsync(a => a.EmployeeId == emp);
    rec.Source.Should().Be(AttendanceSources.AttendanceCorrection);
    rec.ReferenceId.Should().Be(ctx.RequestInstanceId);
}

[Theory]
[InlineData("{\"date\":\"2026-07-06\",\"reason\":\"x\"}")]          // no punch at all
[InlineData("{\"date\":\"2026-07-06\",\"checkIn\":\"25:00\",\"reason\":\"x\"}")] // invalid
[InlineData("{\"date\":\"2026-07-06\",\"checkIn\":\"08:00\"}")]     // no reason
public async Task Invalid_input_throws(string json)
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    var act = () => Sut(db, new FakeAttendance(db)).ExecuteAsync(Eff(emp, json), default);
    await act.Should().ThrowAsync<Exception>();
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendanceCorrectionExecutorTests`
Expected: FAIL to compile (ctor changed) / assertions fail.

- [ ] **Step 3: Rewrite the executor (routing + validation + stamp)**

Replace the body of `AttendanceCorrectionExecutor.cs`. (Idempotency + finalized guard land in Tasks 4–5; leave the injected `guard`/`permissions` unused-but-present now, or add them in those tasks — to keep this task's diff focused, inject `db` + `attendance` only here and add `guard`/`permissions` in Task 4. If your executor framework requires the final ctor now, inject all four and no-op the guard until Task 4.)

```csharp
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Modules.Attendance.DTOs;
using HR.Modules.Attendance.Services;
using HR.Domain.Engines.Attendance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: apply an approved attendance correction by recomputing the day from the corrected
/// punches (via IAttendanceService), instead of blindly zeroing penalties.</summary>
public sealed class AttendanceCorrectionExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IAttendanceService _attendance;

    public AttendanceCorrectionExecutor(ApplicationDbContext db, IAttendanceService attendance)
    { _db = db; _attendance = attendance; }

    public string EffectType => EffectTypes.AttendanceCorrect;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind((ctx.Date("date") ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var reason = ctx.Str("reason");
        var checkIn = ctx.Str("checkIn");
        var checkOut = ctx.Str("checkOut");

        // Validate: reason required; ≥1 punch; each provided punch is HH:mm.
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("سبب التصحيح مطلوب / Reason is required.");
        if (!PunchTime.HasValue(checkIn) && !PunchTime.HasValue(checkOut))
            throw new ValidationException("يجب إدخال وقت الحضور أو الانصراف / At least one punch is required.");
        if (!PunchTime.IsValid(checkIn) || !PunchTime.IsValid(checkOut))
            throw new ValidationException("صيغة الوقت يجب أن تكون HH:mm / Punch times must be HH:mm.");

        var existing = await _db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.EmployeeId == ctx.EmployeeId && a.Date == date, ct);

        Guid targetId;
        if (existing is not null)
        {
            await _attendance.CorrectAsync(existing.Id,
                new CorrectAttendanceRequest { CheckIn = checkIn, CheckOut = checkOut, Reason = reason }, ct);
            targetId = existing.Id;
        }
        else
        {
            targetId = await _attendance.AddManualPunchAsync(
                new ManualPunchRequest { EmployeeId = ctx.EmployeeId, Date = date, CheckIn = checkIn, CheckOut = checkOut, Notes = reason }, ct);
        }

        // Stamp provenance so the idempotency guard (Task 4) matches on re-run.
        var applied = await _db.AttendanceRecords.FirstAsync(a => a.Id == targetId, ct);
        applied.Source = AttendanceSources.AttendanceCorrection;
        applied.ReferenceId = ctx.RequestInstanceId;
        await _db.SaveChangesAsync(ct);

        return EffectExecutionResult.Ok(
            targetEntityType: "AttendanceRecord", targetRecordId: targetId,
            after: new { date, applied.LateMinutes, applied.ShortageMinutes, applied.Status },
            summary: $"Attendance recomputed for {date:yyyy-MM-dd}: late {applied.LateMinutes}m, shortage {applied.ShortageMinutes}m");
    }
}
```

> If `ValidationException` is not in `HR.Application.Common.Exceptions`, grep for the project's validation exception type (used by command validators) and use that; the test only asserts *some* exception is thrown.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendanceCorrectionExecutorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs
git commit -m "feat(sp2): route attendance correction through IAttendanceService (real recalc, not zeroing)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 4: Executor idempotency (no duplicate execution)

**Files:**
- Modify: `AttendanceCorrectionExecutor.cs`
- Test: `AttendanceCorrectionExecutorTests.cs`

**Interfaces:**
- Consumes: `AttendanceRecord.Source`, `AttendanceRecord.ReferenceId`, `EffectSkipReasons`.
- Produces: second execution for the same `RequestInstanceId` → `EffectExecutionResult.Skip("AlreadyApplied")`, no second correction.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Duplicate_run_for_same_request_is_skipped()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 45 });
    await db.SaveChangesAsync();
    var att = new FakeAttendance(db);
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:00\",\"checkOut\":\"17:00\",\"reason\":\"fixed\"}");

    await Sut(db, att).ExecuteAsync(ctx, default);           // first run applies
    att.CorrectedRecordId = null;                            // reset probe
    var second = await Sut(db, att).ExecuteAsync(ctx, default);

    second.IsSkipped.Should().BeTrue();
    second.SkipReason.Should().Be("AlreadyApplied");
    att.CorrectedRecordId.Should().BeNull();                 // engine NOT called again
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter Duplicate_run_for_same_request_is_skipped`
Expected: FAIL (engine called twice).

- [ ] **Step 3: Add the guard**

In `ExecuteAsync`, immediately after validation and before the `existing` lookup:

```csharp
var already = await _db.AttendanceRecords.AnyAsync(a =>
    a.EmployeeId == ctx.EmployeeId && a.Date == date
    && a.Source == AttendanceSources.AttendanceCorrection
    && a.ReferenceId == ctx.RequestInstanceId, ct);
if (already)
    return EffectExecutionResult.Skip("AlreadyApplied",
        targetEntityType: "AttendanceRecord",
        summary: $"Attendance for {date:yyyy-MM-dd} already corrected by this request.");
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendanceCorrectionExecutorTests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs
git commit -m "feat(sp2): idempotent attendance correction (skip duplicate execution)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 5: Finalized-payroll guard — block unless authorized; if authorized, apply + signal

**Files:**
- Modify: `AttendanceCorrectionExecutor.cs` (ctor gains `IPayrollPeriodGuard`, `IPermissionResolver`; body gains guard + signal)
- Test: `AttendanceCorrectionExecutorTests.cs`

**Interfaces:**
- Consumes: `IPayrollPeriodGuard.EnsurePeriodOpenForAsync` (throws `PayrollPeriodClosedException` when finalized), `IPermissionResolver.ResolveAsync`, `_db.Notifications` (signal row — same entity `NotificationService.cs:19-26` builds).
- Produces: final ctor `(ApplicationDbContext, IAttendanceService, IPayrollPeriodGuard, IPermissionResolver)`.

Add these fakes to the test file:

```csharp
private sealed class ClosedPeriodGuard : HR.Application.Engines.Finance.IPayrollPeriodGuard
{
    public Task EnsurePeriodOpenForAsync(Guid e, DateTime d, CancellationToken ct = default)
        => throw new HR.Application.Engines.Finance.PayrollPeriodClosedException(
            new HR.Application.Engines.Finance.PayrollPeriodClosedPayload(
                "PAYROLL_PERIOD_CLOSED", Guid.NewGuid(), "PR-1", Guid.NewGuid(), d.Year, d.Month, "Locked"));
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Finalized_period_without_authorization_blocks()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 45 });
    await db.SaveChangesAsync();
    var att = new FakeAttendance(db);
    var sut = new AttendanceCorrectionExecutor(db, att, new ClosedPeriodGuard(), new FakePerms(/* no perms */));
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:00\",\"checkOut\":\"17:00\",\"reason\":\"x\"}", actor: Guid.NewGuid());

    var act = () => sut.ExecuteAsync(ctx, default);
    await act.Should().ThrowAsync<Exception>();
    att.CorrectedRecordId.Should().BeNull();                 // not applied
}

[Fact]
public async Task Finalized_period_with_authorization_applies_and_signals()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid(); var actor = Guid.NewGuid();
    db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 45 });
    await db.SaveChangesAsync();
    var att = new FakeAttendance(db);
    var sut = new AttendanceCorrectionExecutor(db, att, new ClosedPeriodGuard(), new FakePerms("Payroll.Run.Amend"));
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:00\",\"checkOut\":\"17:00\",\"reason\":\"x\"}", actor: actor);

    var result = await sut.ExecuteAsync(ctx, default);

    att.CorrectedRecordId.Should().NotBeNull();              // applied
    (await db.Notifications.CountAsync(n => n.UserId == actor)).Should().Be(1); // signal to actor
    result.IsSkipped.Should().BeFalse();
}

[Fact]
public async Task Open_period_does_not_signal()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid(); var actor = Guid.NewGuid();
    db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 45 });
    await db.SaveChangesAsync();
    var att = new FakeAttendance(db);
    var sut = new AttendanceCorrectionExecutor(db, att, new OpenPeriodGuard(), new FakePerms("Payroll.Run.Amend"));
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:00\",\"checkOut\":\"17:00\",\"reason\":\"x\"}", actor: actor);

    await sut.ExecuteAsync(ctx, default);
    (await db.Notifications.CountAsync(n => n.UserId == actor)).Should().Be(0);
}
```

Update the `Sut(...)` helper's default ctor call and all earlier `new AttendanceCorrectionExecutor(db, att)` usages to the 4-arg ctor (helper already supplies defaults).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendanceCorrectionExecutorTests`
Expected: FAIL to compile (ctor) then assertion fails.

- [ ] **Step 3: Add ctor deps + guard + signal**

Change the ctor to `(ApplicationDbContext db, IAttendanceService attendance, IPayrollPeriodGuard payrollGuard, IPermissionResolver permissions)` and store them. After the idempotency guard and before the `existing` lookup, add finalized detection; after applying + stamping, emit the signal. Insert:

```csharp
// Finalized-payroll guard: block unless the actor is authorized (Payroll.Run.Amend).
bool periodFinalized = false;
try { await _payrollGuard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
catch (PayrollPeriodClosedException) { periodFinalized = true; }

if (periodFinalized)
{
    var perms = ctx.ActorUserId is { } uid
        ? await _permissions.ResolveAsync(uid, ct)
        : (IReadOnlyList<string>)System.Array.Empty<string>();
    if (!perms.Contains("Payroll.Run.Amend"))
        throw new ValidationException(
            $"لا يمكن تعديل فترة رواتب مقفلة ({date:yyyy-MM}) دون صلاحية / " +
            $"Payroll period {date:yyyy-MM} is finalized; correcting it requires payroll-amend authorization.");
}
```

(Requires `using HR.Application.Engines.Finance;` for the exception.) Then, immediately before the `return` (after provenance stamping), emit the signal when finalized+authorized:

```csharp
if (periodFinalized && ctx.ActorUserId is { } signalUser)
{
    _db.Notifications.Add(new Notification   // same entity NotificationService.cs:19-26 builds
    {
        UserId = signalUser,
        TitleAr = "تصحيح حضور بعد إقفال الرواتب", TitleEn = "Attendance corrected after payroll finalized",
        BodyAr = $"تم تصحيح حضور الموظف ليوم {date:yyyy-MM-dd} بعد إقفال رواتب الفترة (الطلب {ctx.RequestNumber}). قد يلزم تسوية في الرواتب: تأخير {applied.LateMinutes}د، نقص {applied.ShortageMinutes}د.",
        BodyEn = $"Attendance for {date:yyyy-MM-dd} was corrected after payroll was finalized (request {ctx.RequestNumber}). A payroll adjustment may be required: late {applied.LateMinutes}m, shortage {applied.ShortageMinutes}m.",
        Category = "PayrollAdjustmentNeeded",
        EntityId = ctx.RequestInstanceId, Link = "/payroll",
    });
    await _db.SaveChangesAsync(ct);
}
```

> Confirm the `Notification` type's namespace from `NotificationService.cs` (it constructs the same entity) and add the `using`. Set only the properties that file sets; leave `IsRead`/`CreatedAt` to their defaults if the entity assigns them.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendanceCorrectionExecutorTests`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Attendance/Completion/AttendanceCorrectionExecutor.cs backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs
git commit -m "feat(sp2): finalized-payroll guard + adjustment signal for attendance correction

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

> **DI note:** the executor is resolved from DI by the completion engine. `IPayrollPeriodGuard` and `IPermissionResolver` are already registered (used elsewhere). No registration change expected; if the build's DI validation complains, register in the same module that registers the other attendance executors.

---

### Task 6: Real-service recalculation + overnight (integration test)

**Files:**
- Test: `AttendanceCorrectionExecutorTests.cs` (uses the REAL `AttendanceService`, seeded shift)

**Interfaces:**
- Consumes: `new AttendanceService(db, user, calc, resolver)` (`AttendanceService.cs:38`), `IAttendanceCalculationService` impl `AttendanceCalculationService`, `IShiftResolver` impl `ShiftResolver`, `Shift`, `ShiftAssignment`.

This proves the routing actually recalculates (anti-regression against zeroing) and that overnight is inherited.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task Real_service_recomputes_late_from_corrected_punch()
{
    await using var db = Ctx($"t-{Guid.NewGuid()}");
    var emp = Guid.NewGuid();
    // Employee + a day shift starting 08:00, assignment effective in range.
    // (Seed Shift + ShiftAssignment scoped to this employee per the entities' required fields —
    //  mirror StandardPayrollSeederAttendanceTests / AttendanceDeductionRunTests seeding.)
    // ... seed shift 08:00-17:00 and assignment for emp effective 2026-01-01.. ...
    db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Present, CheckIn = Utc(2026,7,5).AddHours(8), CheckOut = Utc(2026,7,5).AddHours(17) });
    await db.SaveChangesAsync();

    var real = new HR.Modules.Attendance.Services.AttendanceService(
        db, new FakeUser(),
        new HR.Modules.Attendance.Services.AttendanceCalculationService(),
        new HR.Modules.Attendance.Services.ShiftResolver());
    // Correct the check-in to 08:45 → still late → LateMinutes must be > 0.
    var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:45\",\"checkOut\":\"17:00\",\"reason\":\"late fixed\"}");

    await Sut(db, real).ExecuteAsync(ctx, default);

    var rec = await db.AttendanceRecords.SingleAsync(a => a.EmployeeId == emp && a.Date == Utc(2026,7,5));
    rec.LateMinutes.Should().BeGreaterThan(0);   // recalculated, NOT zeroed
}
```

> Seed the `Shift`/`ShiftAssignment` exactly as an existing finance attendance test does (open `AttendanceDeductionRunTests.cs` or `StandardPayrollSeederAttendanceTests.cs` for the concrete field set + the `AttendanceCalculationService`/`ShiftResolver` constructors). If those ctors take arguments, match them. Add an overnight variant (`Shift` 22:00→06:00, checkout `05:30` next-logic) asserting non-negative shortage, to lock in `AttendanceCalculationService.cs:92-93`.

- [ ] **Step 2–4:** Run (fails: late is 0 or shift not resolved) → fix the seeding until the real service resolves the shift and recomputes → PASS.

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter Real_service_recomputes_late_from_corrected_punch`

- [ ] **Step 5: Commit**

```bash
git add backend/tests/HR.Domain.Finance.Tests/AttendanceCorrectionExecutorTests.cs
git commit -m "test(sp2): prove attendance correction recomputes late minutes via real engine (+overnight)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 7: Declare punch inputs — form, effect catalog, system effect mapping

**Files:**
- Modify: `RequestSeeder.cs` (`AttendanceCorrectionForm`), `EffectActionCatalog.cs` (`AttendanceCorrect` descriptor), `SystemRequestEffects.cs` (`ATTENDANCE_CORRECTION` mapping)
- Test: `AttendanceCorrectionProvisioningTests.cs`

**Interfaces:**
- Consumes: `F(code, ar, en, FieldType.Text, required, placeholder)` (RequestSeeder helper, see `:374-375`), `In(...)` + `FieldOrContext` (EffectActionCatalog helpers), `Map/Field` (SystemRequestEffects helpers).
- Produces: form fields `checkIn`/`checkOut`; catalog inputs `checkIn`/`checkOut`; system effect maps both.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void System_effect_maps_check_in_and_out()
{
    var specs = SystemRequestEffects.Required["ATTENDANCE_CORRECTION"];
    var correct = specs.Single(s => s.EffectType == EffectTypes.AttendanceCorrect);
    correct.Inputs.Keys.Should().Contain(new[] { "date", "reason", "checkIn", "checkOut" });
}
```

(Add `using HR.Domain.Enums;`/the namespaces of `SystemRequestEffects` + `EffectTypes` as needed.)

- [ ] **Step 2: Run to verify failure** — `dotnet test ... --filter System_effect_maps_check_in_and_out` → FAIL.

- [ ] **Step 3: Add the declarations**

`RequestSeeder.cs` `AttendanceCorrectionForm()` — add after `startDate`/`reason` (Text HH:mm, optional, like MISSING_PUNCH `:374-375`):

```csharp
F("checkIn", "وقت الحضور (HH:mm)", "Check In (HH:mm)", FieldType.Text, false, placeholder: "08:00"),
F("checkOut", "وقت الانصراف (HH:mm)", "Check Out (HH:mm)", FieldType.Text, false, placeholder: "17:00"),
```

`SystemRequestEffects.cs` `["ATTENDANCE_CORRECTION"]` — extend the `Map(...)`:

```csharp
Transactional(EffectTypes.AttendanceCorrect, Map(
    ("date", Field("startDate")),
    ("reason", Field("reason")),
    ("checkIn", Field("checkIn")),
    ("checkOut", Field("checkOut")))),
```

`EffectActionCatalog.cs` `AttendanceCorrect` descriptor — add two inputs and re-label:

```csharp
DescriptionAr = "يعيد احتساب يوم الحضور من الأوقات المصححة.",
DescriptionEn = "Recomputes an attendance day from the corrected punches.",
// ...Inputs:
In("checkIn", "وقت الحضور", "Check In", false, FieldOrContext),
In("checkOut", "وقت الانصراف", "Check Out", false, FieldOrContext),
```

- [ ] **Step 4: Run to verify pass** — `dotnet test ... --filter System_effect_maps_check_in_and_out` → PASS. Also `dotnet build backend/HR.sln`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs backend/tests/HR.Modules.Platform.Tests/Requests/AttendanceCorrectionProvisioningTests.cs
git commit -m "feat(sp2): declare checkIn/checkOut on correction form, catalog, and system effect

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 8: Provisioning bump 4→5 — reconcile new form fields + system effect config

**Files:**
- Modify: `RequestProvisioningService.cs` (`CurrentSeedVersion` 4→5; add form-field + effect-config reconcile for system-owned, un-customized types)
- Test: `AttendanceCorrectionProvisioningTests.cs`

**Interfaces:**
- Consumes: existing `ReconcileRequiredEffects`, `BackfillFieldClassificationsAsync`, `ReconcileWorkflowNotificationRules`, `SystemRequestEffects.Required`, `RequestSeeder` form specs, `FormField`, `RequestEffectDefinition`.
- Produces: `CurrentSeedVersion = 5`; a new reconcile step that (a) adds missing shipped **system** form fields by `Code`, (b) refreshes the system effect's `ConfigurationJson` for still-`IsSystem && IsRequired` effects.

- [ ] **Step 1: Write the failing tests**

Provisioning is DB-bound; use the same InMemory `ApplicationDbContext` harness. Seed a tenant `ATTENDANCE_CORRECTION` at `SeedVersion = 4` with the OLD form (only `startDate`/`reason`) + the old effect config, run `ProvisionTenantAsync`, then assert:

```csharp
[Fact]
public async Task Bump_adds_missing_punch_fields_and_keeps_customized_untouched()
{
    // Arrange: seed the request type + form at v4 with only startDate/reason,
    // and a system AttendanceCorrect effect with the old config; plus a SECOND
    // form field the "tenant added" (custom) that must survive.
    // (Mirror the arrange pattern in existing provisioning/seeder tests.)
    // Act: await new RequestProvisioningService(db, seeder, background, log).ProvisionTenantAsync(tenantId, actor, ct);
    // Assert:
    //  - form now has checkIn + checkOut fields (added by code)
    //  - the tenant-added custom field still exists (not removed)
    //  - SeedVersion == 5
    //  - the AttendanceCorrect effect config now maps checkIn/checkOut
    //  - re-running ProvisionTenantAsync is a no-op (idempotent: no duplicate fields)
}

[Fact]
public async Task Customized_type_is_not_touched()
{
    // Seed ATTENDANCE_CORRECTION with IsSystem=false (tenant-authored) → provisioning skips it entirely:
    // no fields added, config unchanged.
}
```

> Build the arrange helpers by reading how `RequestProvisioningService` is unit-tested today (grep the Platform test project for `ProvisionTenantAsync` / `RequestProvisioningService`); reuse that fixture. If none exists, seed the minimal graph the service reads: `RequestType` (IsSystem, SeedVersion=4, FormDefinitionId), its `FormField`s, its `RequestEffectDefinition`s.

- [ ] **Step 2: Run to verify failure** — fields not added / config not refreshed / version stays 4 → FAIL.

- [ ] **Step 3: Implement**

In `RequestProvisioningService.cs`:
1. `public const int CurrentSeedVersion = 5;` and extend the XML doc with a `v5:` note.
2. Add a new reconcile method and call it inside the upgrade block (alongside `ReconcileRequiredEffects` / `BackfillFieldClassificationsAsync` / `ReconcileWorkflowNotificationRules`), guarded to `type.IsSystem`:

```csharp
changes.AddRange(await ReconcileSystemFormFieldsAsync(type, ct));
```

```csharp
/// <summary>Add shipped system form fields that are missing from a system request's form (by Code),
/// and refresh the system effect's config mapping to the shipped set. Never removes or reorders tenant
/// fields; never touches a tenant-authored (IsSystem == false) type or a customized effect.</summary>
private async Task<List<string>> ReconcileSystemFormFieldsAsync(RequestType type, CancellationToken ct)
{
    var changes = new List<string>();
    if (!type.IsSystem || type.FormDefinitionId == Guid.Empty) return changes;

    // Shipped fields for this request code, from the seeder's form spec.
    var shipped = _seeder.SystemFormFields(type.Code);   // add this accessor (see below)
    if (shipped.Count == 0) return changes;

    var existing = await _db.Set<FormField>()
        .Where(f => f.FormDefinitionId == type.FormDefinitionId).ToListAsync(ct);
    var byCode = existing.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);
    var maxSort = existing.Count == 0 ? 0 : existing.Max(f => f.SortOrder);

    foreach (var spec in shipped)
    {
        if (byCode.ContainsKey(spec.Code)) continue;      // present (tenant may have edited it) → leave
        _db.Set<FormField>().Add(new FormField
        {
            FormDefinitionId = type.FormDefinitionId,
            Code = spec.Code, NameAr = spec.NameAr, NameEn = spec.NameEn,
            FieldType = spec.FieldType, IsRequired = spec.IsRequired,
            Placeholder = spec.Placeholder, SortOrder = ++maxSort,
            MetadataJson = FormFieldClassification.With(FieldClassification.Optional),
        });
        changes.Add($"+field:{spec.Code}");
    }

    // Refresh the system effect config for still-system/required effects (never a customized one).
    if (SystemRequestEffects.Required.TryGetValue(type.Code, out var specs))
    {
        foreach (var s in specs)
        {
            var eff = type.Effects.FirstOrDefault(e =>
                string.Equals(e.EffectType, s.EffectType, StringComparison.OrdinalIgnoreCase)
                && e.Trigger == s.Trigger && e.IsSystem && e.IsRequired);
            if (eff is null) continue;
            var shippedCfg = EffectConfiguration.Serialize(s.Inputs);
            if (eff.ConfigurationJson != shippedCfg) { eff.ConfigurationJson = shippedCfg; changes.Add($"~cfg:{s.EffectType}"); }
        }
    }
    return changes;
}
```

3. Add the seeder accessor `IReadOnlyList<FormFieldSpec> SystemFormFields(string requestCode)` to `IRequestSeeder`/`RequestSeeder` that returns the shipped `FormSpec` fields for a code (it already builds `AttendanceCorrectionForm()` etc.; expose a lookup by code). Reuse the existing `FormFieldSpec`/`FormSpec` types. If the effect entity's system/customized flags differ from `IsSystem`/`IsRequired`, use whatever "system-owned & not customized" markers the entity exposes (grep `RequestEffectDefinition`).

> This is the highest-risk task (§E). Keep every write additive and gated to `type.IsSystem`; verify the "customized untouched" test passes before committing.

- [ ] **Step 4: Run to verify pass** — both provisioning tests + full suite.

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests` and `dotnet test backend/tests/HR.Domain.Finance.Tests`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs backend/src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs backend/tests/HR.Modules.Platform.Tests/Requests/AttendanceCorrectionProvisioningTests.cs
git commit -m "feat(sp2): SeedVersion 4->5 reconcile — add system form fields + refresh system effect config

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 9: Update the obsolete zeroing test + full green

**Files:**
- Modify: `backend/tests/HR.Domain.Finance.Tests/AttendanceExcuseExecutorTests.cs`

The old `Correction_sets_present_and_zeroes_penalty_minutes` constructs `new AttendanceCorrectionExecutor(db)` and asserts unconditional zeroing — both now wrong (ctor changed; behavior changed).

- [ ] **Step 1:** Replace that test with one that reflects the new behavior using the fakes from Task 3 (route to a fake `IAttendanceService`, assert `CorrectAsync` was called and provenance stamped) — OR delete it if `AttendanceCorrectionExecutorTests` already covers the case (it does). Keep the `AttendanceApplyLeaveDaysExecutor` test intact.

- [ ] **Step 2: Run the whole suite**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests` and `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: all green.

- [ ] **Step 3: Commit**

```bash
git add backend/tests/HR.Domain.Finance.Tests/AttendanceExcuseExecutorTests.cs
git commit -m "test(sp2): retire obsolete zeroing assertion for attendance correction

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
git push origin main && git push sanad main
```

---

### Task 10: Whole-branch review + deploy/verify (manual finalization)

- [ ] Request a whole-branch review (superpowers:requesting-code-review) covering: idempotency correctness, the finalized-payroll guard/authorization, provisioning additivity (customized-untouched), and that no timezone conversion crept in.
- [ ] Deploy: `dotnet publish backend/src/HR.Api -c Release -o publish-out`, zip via `System.IO.Compression.ZipFile` (replace `\`→`/`), `az webapp deploy --resource-group HR --name hrcloud-api-v4xd --src-path publish.zip --type zip`. No schema migration expected (all data rows) — confirm `dotnet ef migrations list` shows nothing pending; if the entity changes added a column, generate + apply a migration first.
- [ ] Re-provision the tenant (SeedVersion 4→5) via `POST /api/requests/provision` (admin) so existing rows upgrade.
- [ ] Behavioral verify (reuse the SP1 admin-link-to-self-managed-employee trick, since employee self-service is blocked by the deferred tenant-less-login bug): submit an ATTENDANCE_CORRECTION with a still-late corrected punch → approve → confirm the `AttendanceRecord` shows recomputed **non-zero** late minutes + an `AttendanceCorrection` audit row + the 3 lifecycle bells (`RequestWorkflow` category) fire.
- [ ] Update memory `session-checkpoint` (SP2 done+deployed) and add an SP2 memory node.

---

## Self-Review

**Spec coverage:** A(form)→T7; B(executor rewrite)→T3–5; C(catalog+config)→T7; D(notifications)→T1; E(provisioning 4→5)→T8; F(tests: missing-punch T3, overnight/recalc T6, finalized T5, duplicate T4, HH:mm T2, audit — see note)→covered; G(migration/deploy)→T10; H(finalized)→T5; I(tz+validation)→T2/T3 + TODO marker; J(idempotency)→T4; K(audit identities)→**assert in T10 verify** (identities are pre-existing; no code task needed — acceptable, but if a unit assertion is wanted add a small test reading `CompletionRun.FinalApproverUserId` after a completion). **Gap noted:** K has no dedicated unit test (identities are engine-provided); the T10 behavioral verify covers it. Acceptable.

**Placeholder scan:** Task 6 and Task 8 arrange-blocks intentionally defer to "mirror existing seeding fixture" rather than inventing `Shift`/`ShiftAssignment`/provisioning-fixture field sets I have not read — the implementer reads the named sibling test. This is a deliberate, bounded instruction (named file + exact goal), not an open TODO.

**Type consistency:** ctor evolves `(db, attendance)` [T3] → `(db, attendance, payrollGuard, permissions)` [T5]; the `Sut` helper hides this so earlier tests keep compiling once updated in T5. `PunchTime.IsValid/HasValue`, `AttendanceSources.AttendanceCorrection`, `EffectSkipReasons`/`"AlreadyApplied"`, `Payroll.Run.Amend`, `CurrentSeedVersion=5` used consistently.

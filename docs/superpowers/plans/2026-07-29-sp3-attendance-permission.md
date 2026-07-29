# SP3 — Attendance Permission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an `ATTENDANCE_PERMISSION` (استئذان) request type whose approval writes a durable `AttendancePermission` row that the attendance calculation engine honors — excusing the late/early minutes in the permitted window so the day stays `Present` and payroll deducts less — enforced under a configurable monthly cap.

**Architecture:** A new tenant-scoped `AttendancePermission` entity is the source of truth. The pure `AttendanceCalculationService.Calculate` gains an optional list of permission windows and subtracts the window∩shift overlap from late/shortage before deciding status. Both the persist path (`RecalcAsync`) and the display path (`GetRangeRowsAsync`/`BuildDay`) load the day's approved permissions, so the excuse is durable across recalculation, punch sync, and regeneration. A new `AttendancePermissionExecutor` (mirroring SP2's `AttendanceCorrectionExecutor`) applies the effect with idempotency, a monthly-cap check, and the finalized-payroll guard. Seeding/notifications/provisioning mirror SP2 exactly.

**Tech Stack:** .NET 8, EF Core (Npgsql), xUnit, FluentValidation. Frontend needs **no** new code — the existing dynamic request form renders the seeded fields (like SP2's `checkIn`/`checkOut`).

## Global Constraints

- Backend under `backend/`; solution builds with `dotnet build backend/HR.sln`.
- Times are `HH:mm` strings on the form; internally minutes-from-midnight (int). Timezone kept **naïve** (`TODO(tz)`), same as SP2.
- Effect executors return `EffectExecutionResult.Ok(...)`/`.Skip(reason, ...)`; **throw** `HR.Application.Common.Exceptions.ValidationException` to roll back.
- Idempotency key for this effect: an `AttendancePermission` row with `RequestInstanceId == ctx.RequestInstanceId`.
- Finalized-payroll guard: `IPayrollPeriodGuard.EnsurePeriodOpenForAsync` throws `PayrollPeriodClosedException` when closed; permit only if `IPermissionResolver.ResolveAsync(actor)` contains `"Payroll.Run.Amend"`, then emit a bell `Notification` (Category `"PayrollAdjustmentNeeded"`).
- New effect type string: `"Attendance.Permission"`. New attendance source tag: `"AttendancePermission"`.
- Provisioning: bump `RequestProvisioningService.CurrentSeedVersion` **5 → 6**.
- Commit discipline (user's standing rule): one logical change per commit; commit **and push to both remotes** (`origin` + `sanad`) after each green task; never commit broken/untested code.
- Test suites to keep green: `backend/tests/HR.Domain.Finance.Tests` and the Platform test project. Run `dotnet test backend/tests/HR.Domain.Finance.Tests` and the attendance test project after each backend task.

---

### Task 1: Pure calc — permission windows excuse late/shortage

**Files:**
- Modify: `backend/src/HR.Modules/Attendance/Services/AttendanceCalculationService.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCalcTests.cs` (create)

**Interfaces:**
- Produces:
  - `public readonly record struct PermissionWindow(int FromMinutes, int ToMinutes);`
  - `AttendanceCalcResult.ExcusedMinutes` (int)
  - New optional param on `Calculate(...)`: `IReadOnlyList<PermissionWindow>? permissions = null` (added as the **last** parameter, after `policy`)
  - `public static class PermissionMath` with `int WindowMinutesWithinShift(Shift? shift, IReadOnlyList<PermissionWindow> windows)`

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCalcTests.cs`:

```csharp
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Modules.Attendance.Services;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCalcTests
{
    private static Shift FixedShift(TimeOnly start, TimeOnly end, int required, int breakMin = 0) => new()
    {
        NameAr = "ش", NameEn = "S", StartTime = start, EndTime = end,
        RequiredMinutes = required, BreakMinutes = breakMin, IsFlexible = false,
        WeekendDays = "5,6", OvertimeAllowed = false,
    };

    private static DateTime Day => new(2026, 8, 3); // a Monday (not weekend)
    private static DateTime At(int h, int m) => new DateTime(2026, 8, 3).AddHours(h).AddMinutes(m);
    private static PermissionWindow W(int fromH, int fromM, int toH, int toM)
        => new(fromH * 60 + fromM, toH * 60 + toM);

    private readonly AttendanceCalculationService _calc = new();

    [Fact] // Late arrival: shift 08:00-16:00 (480), arrived 09:00 → 60 late; permission 08:00-09:00 excuses it.
    public void Late_arrival_within_permission_is_excused()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(9, 0), At(16, 0), permissions: new[] { W(8, 0, 9, 0) });
        Assert.Equal(0, r.LateMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, r.Status);
    }

    [Fact] // Early departure: left 14:00 (shortage 120); permission 14:00-16:00 excuses it.
    public void Early_departure_within_permission_is_excused()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0), permissions: new[] { W(14, 0, 16, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(120, r.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, r.Status);
    }

    [Fact] // Temporary/partial exit: left 14:00 (shortage 120); permission 13:00-15:00 overlaps only [14:00,15:00]=60.
    public void Partial_window_excuses_only_the_overlapping_shortage()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0), permissions: new[] { W(13, 0, 15, 0) });
        Assert.Equal(60, r.ShortageMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
    }

    [Fact] // Overnight shift 20:00-04:00 (480). Left 03:00 → 60 shortage; permission 03:00-04:00 (after midnight) excuses it.
    public void Overnight_shift_permission_after_midnight_is_excused()
    {
        var shift = FixedShift(new(20, 0), new(4, 0), 480);
        var checkIn = new DateTime(2026, 8, 3, 20, 0, 0);
        var checkOut = new DateTime(2026, 8, 4, 3, 0, 0); // 03:00 next day
        var r = _calc.Calculate(shift, Day, checkIn, checkOut, permissions: new[] { W(3, 0, 4, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
    }

    [Fact] // Overlapping permissions must not double-count: two windows both covering 14:00-16:00 excuse 120, not 240.
    public void Overlapping_permissions_are_merged()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0),
            permissions: new[] { W(14, 0, 16, 0), W(14, 30, 16, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(120, r.ExcusedMinutes);
    }

    [Fact] // No permission → unchanged (regression guard).
    public void No_permission_leaves_penalties_intact()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(9, 0), At(14, 0));
        Assert.Equal(60, r.LateMinutes);
        Assert.True(r.ShortageMinutes > 0);
        Assert.Equal(0, r.ExcusedMinutes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionCalcTests`
Expected: FAIL — `Calculate` has no `permissions` param / `ExcusedMinutes` / `PermissionWindow` don't exist.

- [ ] **Step 3: Add `PermissionWindow`, `ExcusedMinutes`, `PermissionMath`, and the overlap logic**

In `AttendanceCalculationService.cs`:

1. Add `public int ExcusedMinutes { get; set; }` to `AttendanceCalcResult`.
2. Add the window type near the top (after the `using`s):

```csharp
/// <summary>A permitted absence window on the shift day, in minutes-from-midnight.</summary>
public readonly record struct PermissionWindow(int FromMinutes, int ToMinutes);
```

3. Add `IReadOnlyList<PermissionWindow>? permissions = null` as the last parameter of **both** the interface method and the implementation of `Calculate`.
4. Just **before** the "Resolve a single headline status" block (i.e. after `r.ShortageMinutes = Math.Max(0, required - worked);` and the overtime block, before line `if (isWorkFromHome) ...`), insert:

```csharp
        // Excuse late/early minutes covered by approved permission windows (fixed shifts only).
        if (permissions is { Count: > 0 } && !flexible && shift is not null)
        {
            var (exLate, exShort, exTotal) = PermissionMath.Excuse(
                shift, date, inT, outT, r.LateMinutes, r.ShortageMinutes, permissions);
            r.LateMinutes -= exLate;
            r.ShortageMinutes -= exShort;
            r.ExcusedMinutes = exTotal;
        }
```

5. Add the pure helper class at the bottom of the file (same namespace):

```csharp
/// <summary>Pure minute math for attendance permissions (استئذان): overlap of permitted windows with
/// the shift's absent intervals. All values are minutes-from-midnight on the shift date; overnight
/// shifts and after-midnight windows are lifted over the +1440 boundary.</summary>
public static class PermissionMath
{
    /// <summary>Excused (late, shortage, total) for one day. `total` = late-interval + early-interval
    /// coverage (disjoint, so never double-counted); `late`/`shortage` are capped at the raw penalties.</summary>
    public static (int excusedLate, int excusedShortage, int excusedTotal) Excuse(
        Shift shift, DateTime date, DateTime inT, DateTime outT,
        int rawLate, int rawShortage, IReadOnlyList<PermissionWindow> windows)
    {
        var (shiftStart, shiftEnd) = ShiftSpan(shift);

        int inMin = (int)Math.Round((inT - date.Date).TotalMinutes);
        int outMin = (int)Math.Round((outT - date.Date).TotalMinutes);
        if (outMin < inMin) outMin += 1440; // overnight punch pair

        // Absent-within-shift intervals: tardy [start, checkIn] and early-leave [checkOut, end].
        int lateFrom = shiftStart, lateTo = Clamp(inMin, shiftStart, shiftEnd);
        int earlyFrom = Clamp(outMin, shiftStart, shiftEnd), earlyTo = shiftEnd;

        var merged = Merge(windows, shiftStart, shiftEnd);
        int exLate = Math.Min(Overlap(merged, lateFrom, lateTo), rawLate);
        int exEarly = Overlap(merged, earlyFrom, earlyTo);
        int exShort = Math.Min(exLate + exEarly, rawShortage);
        return (exLate, exShort, exLate + exEarly);
    }

    /// <summary>Total permitted minutes lying within the shift span — the cap-tally value, computed
    /// without punches (works for permissions approved before the day happens). Falls back to raw
    /// window length when no shift is resolved.</summary>
    public static int WindowMinutesWithinShift(Shift? shift, IReadOnlyList<PermissionWindow> windows)
    {
        if (shift is null || shift.IsFlexible)
            return windows.Sum(w => Math.Max(0, w.ToMinutes - w.FromMinutes));
        var (shiftStart, shiftEnd) = ShiftSpan(shift);
        var merged = Merge(windows, shiftStart, shiftEnd);
        return merged.Sum(iv => iv.To - iv.From);
    }

    private static (int start, int end) ShiftSpan(Shift shift)
    {
        int start = (int)shift.StartTime.ToTimeSpan().TotalMinutes;
        int end = (int)shift.EndTime.ToTimeSpan().TotalMinutes;
        if (end <= start) end += 1440;
        return (start, end);
    }

    private static List<(int From, int To)> Merge(IReadOnlyList<PermissionWindow> ws, int shiftStart, int shiftEnd)
    {
        var list = new List<(int, int)>();
        foreach (var w in ws)
        {
            int f = w.FromMinutes, t = w.ToMinutes;
            if (t <= f) continue;
            if (f < shiftStart) { f += 1440; t += 1440; } // after-midnight window on an overnight shift
            f = Math.Max(f, shiftStart); t = Math.Min(t, shiftEnd);
            if (t > f) list.Add((f, t));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        var merged = new List<(int From, int To)>();
        foreach (var iv in list)
            if (merged.Count > 0 && iv.Item1 <= merged[^1].To)
                merged[^1] = (merged[^1].From, Math.Max(merged[^1].To, iv.Item2));
            else merged.Add(iv);
        return merged;
    }

    private static int Overlap(List<(int From, int To)> merged, int from, int to)
    {
        if (to <= from) return 0;
        int sum = 0;
        foreach (var iv in merged) sum += Math.Max(0, Math.Min(iv.To, to) - Math.Max(iv.From, from));
        return sum;
    }

    private static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(v, hi));
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionCalcTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Run the full Finance suite (no regressions)**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests`
Expected: PASS (existing + 6 new).

- [ ] **Step 6: Commit and push**

```bash
git add backend/src/HR.Modules/Attendance/Services/AttendanceCalculationService.cs backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCalcTests.cs
git commit -m "feat(sp3): attendance calc excuses late/early minutes from permission windows"
git push origin main; git push sanad main
```

---

### Task 2: `AttendancePermission` entity, source tag, record column, DbSet + config

**Files:**
- Create: `backend/src/HR.Domain/Engines/Attendance/AttendancePermission.cs`
- Modify: `backend/src/HR.Domain/Engines/Attendance/AttendanceRecord.cs` (add `ExcusedMinutes`; add `AttendanceSources.AttendancePermission`)
- Modify: `backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs` (add `DbSet<AttendancePermission>`)
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionEntityTests.cs` (create)

**Interfaces:**
- Produces: `AttendancePermission` entity; `DbSet<AttendancePermission> AttendancePermissions`; `AttendanceSources.AttendancePermission = "AttendancePermission"`; `AttendanceRecord.ExcusedMinutes` (int).
- Consumes: `TenantEntity` base (has `Id`, `TenantId`).

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionEntityTests.cs`. Use the same in-memory `ApplicationDbContext` construction the other Finance tests use — open one of them (e.g. `AttendanceDeductionSyncServiceTests.cs`) and copy its DbContext factory helper. Replace `NewDb()` below with that project's existing helper if named differently:

```csharp
using HR.Domain.Engines.Attendance;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionEntityTests
{
    [Fact]
    public async Task Permission_row_persists_and_reads_back()
    {
        using var db = TestDb.New(); // ← use this project's existing in-memory context factory
        var emp = Guid.NewGuid();
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = new DateTime(2026, 8, 3), FromMinutes = 900, ToMinutes = 1020,
            ExcusedMinutes = 120, Reason = "موعد طبي", RequestInstanceId = Guid.NewGuid(),
            Source = AttendanceSources.AttendancePermission,
        });
        await db.SaveChangesAsync();

        var row = await db.AttendancePermissions.SingleAsync(p => p.EmployeeId == emp);
        Assert.Equal(120, row.ExcusedMinutes);
        Assert.Equal("AttendancePermission", row.Source);
    }
}
```

> If the Finance test project has no shared context factory, copy the exact `new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(...).Options)` pattern from an existing test in that folder into a local `TestDb.New()`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionEntityTests`
Expected: FAIL — `AttendancePermission` / `AttendancePermissions` don't exist.

- [ ] **Step 3: Create the entity**

`backend/src/HR.Domain/Engines/Attendance/AttendancePermission.cs`:

```csharp
using HR.Domain.Common;

namespace HR.Domain.Engines.Attendance;

/// <summary>An approved attendance permission (استئذان): the employee is excused for a time window on
/// one day, so the late/shortage minutes overlapping the window are waived by the calculation engine.
/// Rows are immutable audit records; one is written per approved ATTENDANCE_PERMISSION request.</summary>
public class AttendancePermission : TenantEntity
{
    public Guid EmployeeId { get; set; }

    /// <summary>The working day the permission applies to (naïve local date; TODO(tz)).</summary>
    public DateTime Date { get; set; }

    /// <summary>Permitted window as minutes-from-midnight on <see cref="Date"/>.</summary>
    public int FromMinutes { get; set; }
    public int ToMinutes { get; set; }

    /// <summary>Snapshot of window∩shift minutes at approval — the value tallied against the monthly cap.</summary>
    public int ExcusedMinutes { get; set; }

    public string? Reason { get; set; }

    /// <summary>The request instance that produced this row (idempotency + audit link).</summary>
    public Guid RequestInstanceId { get; set; }

    /// <summary>Always <see cref="AttendanceSources.AttendancePermission"/>.</summary>
    public string? Source { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
```

- [ ] **Step 4: Add the source tag and record column**

In `AttendanceRecord.cs`:
- Add to `AttendanceSources`: `public const string AttendancePermission = "AttendancePermission";`
- Add to `AttendanceRecord` (after `BreakMinutes`): `public int ExcusedMinutes { get; set; }`

- [ ] **Step 5: Register the DbSet**

In `ApplicationDbContext.cs` add near the other attendance sets (search for `DbSet<AttendanceRecord>`):

```csharp
public DbSet<AttendancePermission> AttendancePermissions => Set<AttendancePermission>();
```

Verify the `using HR.Domain.Engines.Attendance;` is already present (it is, for `AttendanceRecord`). No explicit `IEntityTypeConfiguration` is required (convention maps it); if the project uses a conventions guard test for indexes, add none — defaults suffice.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionEntityTests`
Expected: PASS.

- [ ] **Step 7: Commit and push**

```bash
git add backend/src/HR.Domain/Engines/Attendance/AttendancePermission.cs backend/src/HR.Domain/Engines/Attendance/AttendanceRecord.cs backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs backend/tests/HR.Domain.Finance.Tests/AttendancePermissionEntityTests.cs
git commit -m "feat(sp3): AttendancePermission entity + ExcusedMinutes column + source tag"
git push origin main; git push sanad main
```

---

### Task 3: Service integration — load permissions on recalc and display

**Files:**
- Modify: `backend/src/HR.Modules/Attendance/Services/AttendanceService.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionServiceTests.cs` (create)

**Interfaces:**
- Consumes: `PermissionWindow`, `PermissionMath` (Task 1); `AttendancePermission`, `AttendanceSources.AttendancePermission` (Task 2).
- Produces: `RecalcAsync` and `GetRangeRowsAsync`/`BuildDay` both feed approved permission windows into `_calc.Calculate` and write `rec.ExcusedMinutes`.

- [ ] **Step 1: Write the failing test**

Create `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionServiceTests.cs`. Construct `AttendanceService` the way an existing attendance service/integration test does (copy DI of `ApplicationDbContext`, a stub `ICurrentUserService`, real `AttendanceCalculationService`, and the real `IShiftResolver`). Minimal shape:

```csharp
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Modules.Attendance.DTOs;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionServiceTests
{
    [Fact]
    public async Task Recalc_excuses_shortage_covered_by_an_approved_permission()
    {
        using var db = TestDb.New();
        var emp = SeedEmployeeWithShift(db, start: new(8, 0), end: new(16, 0), required: 480); // helper below
        // Day worked 08:00–14:00 → 120 shortage, no permission yet.
        var recId = await Service(db).AddManualPunchAsync(
            new ManualPunchRequest { EmployeeId = emp, Date = new DateTime(2026, 8, 3), CheckIn = "08:00", CheckOut = "14:00" }, default);
        var before = await db.AttendanceRecords.AsNoTracking().FirstAsync(r => r.Id == recId);
        Assert.Equal(120, before.ShortageMinutes);

        // Approve a permission 14:00–16:00, then force a recalc via a second manual punch (idempotent times).
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = new DateTime(2026, 8, 3), FromMinutes = 840, ToMinutes = 960,
            ExcusedMinutes = 120, Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        await Service(db).CorrectAsync(recId, new CorrectAttendanceRequest { CheckIn = "08:00", CheckOut = "14:00", Reason = "recalc" }, default);

        var after = await db.AttendanceRecords.AsNoTracking().FirstAsync(r => r.Id == recId);
        Assert.Equal(0, after.ShortageMinutes);      // durable across recalc
        Assert.Equal(120, after.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, after.Status);
    }

    // SeedEmployeeWithShift + Service(db) are small local helpers — mirror an existing attendance
    // integration test in this folder for the exact Employee/Shift/ShiftAssignment rows and the
    // AttendanceService constructor (db, ICurrentUserService stub, new AttendanceCalculationService(), IShiftResolver).
}
```

> Before writing, open the nearest existing attendance integration test (search the Finance/Attendance test projects for `new AttendanceService(`) and copy its employee+shift+assignment seeding and its `AttendanceService` construction verbatim into the two helpers. If none exists, seed: one `Employee` (Active, with `DepartmentId`/`BranchId`/`JobTitleId` null), one `Shift`, one `ShiftAssignment` binding the shift to that employee's scope, so `IShiftResolver.Resolve` returns it.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionServiceTests`
Expected: FAIL — `after.ShortageMinutes` is still 120 (permissions not yet consulted).

- [ ] **Step 3: Add a permission loader and thread windows through `RecalcAsync`**

In `AttendanceService.cs`:

Add a helper (near `LoadPolicyAsync`):

```csharp
/// <summary>Approved permission windows for one employee/day, as minutes-from-midnight.</summary>
private async Task<List<PermissionWindow>> LoadPermissionWindowsAsync(Guid employeeId, DateTime date, CancellationToken ct)
{
    var d = date.Date;
    return await _db.AttendancePermissions.AsNoTracking()
        .Where(p => p.EmployeeId == employeeId && p.Date == d)
        .Select(p => new PermissionWindow(p.FromMinutes, p.ToMinutes))
        .ToListAsync(ct);
}
```

In `RecalcAsync`, load windows and pass them, then persist `ExcusedMinutes`. Replace the `_calc.Calculate(...)` call and the write-back block:

```csharp
        var windows = await LoadPermissionWindowsAsync(rec.EmployeeId, rec.Date, ct);
        var calc = _calc.Calculate(shift, rec.Date, isLeave ? null : rec.CheckIn, isLeave ? null : rec.CheckOut,
            isLeave, rec.Status == AttendanceStatus.Holiday || isHolidayDate, false, policy, windows);
        ...
        rec.BreakMinutes = calc.BreakMinutes;
        rec.ExcusedMinutes = calc.ExcusedMinutes;   // NEW
        if (!isLeave) rec.Status = calc.Status;
```

- [ ] **Step 4: Thread windows through the display path**

`GetRangeRowsAsync` batch-loads permissions for the range, and `BuildDay` passes the day's windows into `Calculate`.

In `GetRangeRowsAsync`, after `var policy = await LoadPolicyAsync(ct);` add:

```csharp
        var permissions = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.Date >= from && p.Date <= to && empIds.Contains(p.EmployeeId))
            .ToListAsync(ct);
        var permByKey = permissions
            .GroupBy(p => (p.EmployeeId, p.Date.Date))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PermissionWindow>)g.Select(p => new PermissionWindow(p.FromMinutes, p.ToMinutes)).ToList());
```

Change the `BuildDay` signature to accept the windows and pass them to `Calculate`:

```csharp
    private AttendanceDayDto BuildDay(EmpRow e, DateTime date, Shift? shift, AttendanceRecord? rec, DateTime today,
        bool isHolidayDate, AttendancePolicySettings policy, IReadOnlyList<PermissionWindow> permissions)
    {
        ...
        var calc = _calc.Calculate(shift, date, ci, co, isLeave, isHoliday, isWfh, policy, permissions);
```

Update the two `BuildDay(...)` call sites:
- In `GetRangeRowsAsync`: `permByKey.TryGetValue((e.Id, d), out var pw); var dto = BuildDay(e, d, shift, rec, today, holidays.Contains(d.Date), policy, pw ?? Array.Empty<PermissionWindow>());`
- In `GetDetailAsync`: after `var policy = await LoadPolicyAsync(ct);` add `var perms = await LoadPermissionWindowsAsync(rec.EmployeeId, rec.Date, ct);` and pass `perms` as the new last arg to `BuildDay`.

Also surface `ExcusedMinutes` on `AttendanceDayDto` (add `public int ExcusedMinutes { get; set; }` to the DTO and set it in `BuildDay`: `ExcusedMinutes = calc.ExcusedMinutes,`). This is optional-but-cheap display plumbing.

- [ ] **Step 5: Verify no other `_calc.Calculate(` caller was missed**

Run: `rg -n "_calc.Calculate\(|\.Calculate\(" backend/src` — confirm only `AttendanceService` (RecalcAsync, BuildDay) and any sync service call it. If a punch-sync/regeneration service also calls `Calculate`, add the same `LoadPermissionWindowsAsync` + pass-through there (durability requirement). Note findings in the commit body.

- [ ] **Step 6: Run tests**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionServiceTests`
Expected: PASS.
Then: `dotnet test backend/tests/HR.Domain.Finance.Tests` → PASS (no regressions).

- [ ] **Step 7: Commit and push**

```bash
git add backend/src/HR.Modules/Attendance/Services/AttendanceService.cs backend/src/HR.Modules/Attendance/DTOs/*.cs backend/tests/HR.Domain.Finance.Tests/AttendancePermissionServiceTests.cs
git commit -m "feat(sp3): recalc + display honor approved attendance permissions (durable excuse)"
git push origin main; git push sanad main
```

---

### Task 4: Monthly-cap policy columns + cap mode enum

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Attendance/AttendancePolicy.cs`
- Modify: `backend/src/HR.Domain/Enums/RequestEnums.cs` (add `PermissionCapMode` enum — put it in the same enum file the other attendance enums live in; if `AttendanceStatus` is there, this is the right file)
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCapPolicyTests.cs` (create)

**Interfaces:**
- Produces: `AttendancePolicy.PermissionMaxPerMonth` (int?), `.PermissionMaxMinutesPerMonth` (int?), `.PermissionCapMode` (`PermissionCapMode`); `enum PermissionCapMode { Warn, Block }`.

- [ ] **Step 1: Write the failing test**

```csharp
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCapPolicyTests
{
    [Fact]
    public void Policy_defaults_to_unlimited_warn()
    {
        var p = new AttendancePolicy();
        Assert.Null(p.PermissionMaxPerMonth);
        Assert.Null(p.PermissionMaxMinutesPerMonth);
        Assert.Equal(PermissionCapMode.Warn, p.PermissionCapMode);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionCapPolicyTests`
Expected: FAIL — members/enum don't exist.

- [ ] **Step 3: Add the enum and columns**

In `RequestEnums.cs` (same file as `AttendanceStatus`):

```csharp
/// <summary>How the monthly attendance-permission cap is enforced.</summary>
public enum PermissionCapMode { Warn = 0, Block = 1 }
```

In `AttendancePolicy.cs` (after `CountOvertime`):

```csharp
    // ── Attendance-permission (استئذان) monthly cap. Null = unlimited. ──
    /// <summary>Max approved permissions per employee per calendar month (null = unlimited).</summary>
    public int? PermissionMaxPerMonth { get; set; }
    /// <summary>Max excused permission minutes per employee per calendar month (null = unlimited).</summary>
    public int? PermissionMaxMinutesPerMonth { get; set; }
    /// <summary>Block rejects an over-cap permission at approval; Warn only flags it.</summary>
    public PermissionCapMode PermissionCapMode { get; set; } = PermissionCapMode.Warn;
```

Ensure `AttendancePolicy.cs` has `using HR.Domain.Enums;` (add if missing).

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionCapPolicyTests`
Expected: PASS.

- [ ] **Step 5: Commit and push**

```bash
git add backend/src/HR.Domain/Engines/Attendance/AttendancePolicy.cs backend/src/HR.Domain/Enums/RequestEnums.cs backend/tests/HR.Domain.Finance.Tests/AttendancePermissionCapPolicyTests.cs
git commit -m "feat(sp3): monthly attendance-permission cap policy fields"
git push origin main; git push sanad main
```

---

### Task 5: `AttendancePermissionExecutor` — the effect

**Files:**
- Create: `backend/src/HR.Modules/Attendance/Completion/AttendancePermissionExecutor.cs`
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionExecutorTests.cs` (create)

**Interfaces:**
- Consumes: `IAttendanceService` (Task 3), `AttendancePermission`/source tag (Task 2), `PermissionMath.WindowMinutesWithinShift` (Task 1), `AttendancePolicy` cap fields (Task 4), `EffectContext`, `IEffectExecutor`, `EffectExecutionResult`, `IPayrollPeriodGuard`, `IPermissionResolver`, `PunchTime` (SP2 validator, in `HR.Modules.Attendance`).
- Produces: `AttendancePermissionExecutor : IEffectExecutor` with `EffectType => EffectTypes.AttendancePermission` (defined in Task 6 — this task references the constant, so **do Task 6 Step 3's constant addition first if the compiler complains**, or add the constant here and let Task 6 keep it).

> Note: add the `EffectTypes.AttendancePermission` constant now (it's trivial) so this task compiles; Task 6 assumes it exists.

- [ ] **Step 1: Add the effect-type constant** (needed to compile)

In `backend/src/HR.Application/Engines/Completion/EffectTypes.cs`, under `// Attendance`:

```csharp
    public const string AttendancePermission = "Attendance.Permission";
```

- [ ] **Step 2: Write the failing tests**

Create `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionExecutorTests.cs`. Build `EffectContext` the way `AttendanceCorrectionExecutor` tests do (open the SP2 executor test — search `new AttendanceCorrectionExecutor(` — and copy its context builder, fakes for `IPayrollPeriodGuard` and `IPermissionResolver`, and DbContext). Cover:

```csharp
// 1) Happy path: approving writes one AttendancePermission row (Source + ReferenceId) and recalcs the day.
// 2) Idempotency: running the same ctx twice → second call returns Skip("AlreadyApplied"), still one row.
// 3) Cap Block: policy PermissionMaxPerMonth = 1 with one existing row this month → throws ValidationException.
// 4) Cap Warn: same counts but mode Warn → succeeds (row written).
// 5) Finalized-payroll guard: guard throws closed + actor lacks Payroll.Run.Amend → ValidationException;
//    with the permission → succeeds and a "PayrollAdjustmentNeeded" Notification is added.
```

Write each as a `[Fact]` asserting the described outcome (mirror the assertions and fakes in the SP2 executor test file; reuse its `FakePayrollGuard`/`FakePermissionResolver` if present).

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionExecutorTests`
Expected: FAIL — executor doesn't exist.

- [ ] **Step 4: Implement the executor**

`backend/src/HR.Modules/Attendance/Completion/AttendancePermissionExecutor.cs`:

```csharp
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Permissions;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: apply an approved attendance permission (استئذان) by writing a durable
/// AttendancePermission row that the calc engine honors, then recomputing the day. Excuses the
/// late/early minutes overlapping the window — payroll then deducts less automatically.</summary>
public sealed class AttendancePermissionExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IAttendanceService _attendance;
    private readonly IPayrollPeriodGuard _payrollGuard;
    private readonly IPermissionResolver _permissions;

    public AttendancePermissionExecutor(ApplicationDbContext db, IAttendanceService attendance,
        IPayrollPeriodGuard payrollGuard, IPermissionResolver permissions)
    { _db = db; _attendance = attendance; _payrollGuard = payrollGuard; _permissions = permissions; }

    public string EffectType => EffectTypes.AttendancePermission;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind((ctx.Date("date") ?? DateTime.UtcNow).Date, DateTimeKind.Utc);
        var fromStr = ctx.Str("from");
        var toStr = ctx.Str("to");
        var reason = ctx.Str("reason");

        // Validate: reason + both punches present + HH:mm + from < to.
        if (string.IsNullOrWhiteSpace(reason))
            throw Fail("reason", "سبب الاستئذان مطلوب / Reason is required.");
        if (!PunchTime.IsValid(fromStr) || !PunchTime.IsValid(toStr) || !PunchTime.HasValue(fromStr) || !PunchTime.HasValue(toStr))
            throw Fail("window", "صيغة الوقت يجب أن تكون HH:mm / From/To must be HH:mm.");
        int fromMin = ToMinutes(fromStr!), toMin = ToMinutes(toStr!);
        if (toMin <= fromMin)
            throw Fail("window", "وقت النهاية يجب أن يكون بعد البداية / To must be after From.");

        // Idempotency.
        var existing = await _db.AttendancePermissions
            .FirstOrDefaultAsync(p => p.RequestInstanceId == ctx.RequestInstanceId, ct);
        if (existing is not null)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: "AttendancePermission",
                summary: $"Permission for {date:yyyy-MM-dd} already applied by this request.");

        // Resolve shift for this day → excused-minutes snapshot for the cap tally.
        var shift = await ResolveShiftAsync(ctx.EmployeeId, date, ct);
        int excused = PermissionMath.WindowMinutesWithinShift(shift,
            new[] { new PermissionWindow(fromMin, toMin) });

        // Monthly cap.
        await EnforceCapAsync(ctx, date, excused, ct);

        // Finalized-payroll guard (mirror SP2).
        bool periodFinalized = false;
        try { await _payrollGuard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
        catch (PayrollPeriodClosedException) { periodFinalized = true; }
        if (periodFinalized)
        {
            var perms = ctx.ActorUserId is { } uid ? await _permissions.ResolveAsync(uid, ct)
                : (IReadOnlyList<string>)Array.Empty<string>();
            if (!perms.Contains("Payroll.Run.Amend"))
                throw Fail("payrollPeriod",
                    $"لا يمكن تعديل فترة رواتب مقفلة ({date:yyyy-MM}) دون صلاحية / " +
                    $"Payroll period {date:yyyy-MM} is finalized; requires payroll-amend authorization.");
        }

        // Persist the permission row, then recalc the day so late/shortage/ExcusedMinutes update.
        var row = new AttendancePermission
        {
            EmployeeId = ctx.EmployeeId, Date = date, FromMinutes = fromMin, ToMinutes = toMin,
            ExcusedMinutes = excused, Reason = reason, RequestInstanceId = ctx.RequestInstanceId,
            Source = AttendanceSources.AttendancePermission, CreatedByUserId = ctx.ActorUserId,
        };
        _db.AttendancePermissions.Add(row);
        await _db.SaveChangesAsync(ct);

        await RecalcDayAsync(ctx.EmployeeId, date, ct);

        if (periodFinalized && ctx.ActorUserId is { } signalUser)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = signalUser,
                TitleAr = "استئذان بعد إقفال الرواتب", TitleEn = "Attendance permission after payroll finalized",
                BodyAr = $"تم اعتماد استئذان للموظف ليوم {date:yyyy-MM-dd} بعد إقفال رواتب الفترة (الطلب {ctx.RequestNumber}). قد يلزم تسوية.",
                BodyEn = $"An attendance permission for {date:yyyy-MM-dd} was applied after payroll was finalized (request {ctx.RequestNumber}). A payroll adjustment may be required.",
                Category = "PayrollAdjustmentNeeded", EntityId = ctx.RequestInstanceId, Link = "/payroll", IsRead = false,
            });
            await _db.SaveChangesAsync(ct);
        }

        return EffectExecutionResult.Ok(
            targetEntityType: "AttendancePermission", targetRecordId: row.Id,
            after: new { date, row.FromMinutes, row.ToMinutes, row.ExcusedMinutes },
            summary: $"Attendance permission applied for {date:yyyy-MM-dd}: {excused}m excused.");
    }

    private async Task EnforceCapAsync(EffectContext ctx, DateTime date, int excused, CancellationToken ct)
    {
        var policy = await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);
        if (policy is null) return;
        if (policy.PermissionMaxPerMonth is null && policy.PermissionMaxMinutesPerMonth is null) return;

        var monthStart = new DateTime(date.Year, date.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var rows = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.EmployeeId == ctx.EmployeeId && p.Date >= monthStart && p.Date < monthEnd)
            .Select(p => p.ExcusedMinutes).ToListAsync(ct);

        int newCount = rows.Count + 1;
        int newMinutes = rows.Sum() + excused;
        bool exceeds = (policy.PermissionMaxPerMonth is { } mc && newCount > mc)
                    || (policy.PermissionMaxMinutesPerMonth is { } mm && newMinutes > mm);

        if (exceeds && policy.PermissionCapMode == PermissionCapMode.Block)
            throw Fail("cap",
                $"تجاوز الحد الشهري للاستئذان ({date:yyyy-MM}) / Monthly permission cap exceeded for {date:yyyy-MM}.");
    }

    private async Task<Shift?> ResolveShiftAsync(Guid employeeId, DateTime date, CancellationToken ct)
    {
        // Reuse the same resolution AttendanceService uses; if IShiftResolver isn't injectable here,
        // load assignments+shifts and call it. Simplest: expose a helper on IAttendanceService, OR
        // inject IShiftResolver. Inject IShiftResolver + read employee scope:
        var emp = await _db.Employees.Where(e => e.Id == employeeId)
            .Select(e => new { e.DepartmentId, e.BranchId, e.JobTitleId }).FirstOrDefaultAsync(ct);
        var scope = new EmployeeScope(employeeId, emp?.DepartmentId, emp?.BranchId, emp?.JobTitleId);
        var assignments = await _db.ShiftAssignments.AsNoTracking().ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        return _shiftResolver.Resolve(assignments, shifts.ToDictionary(s => s.Id), scope, date);
    }

    private async Task RecalcDayAsync(Guid employeeId, DateTime date, CancellationToken ct)
    {
        // Trigger the same recompute path AttendanceService uses. If the day has a record, a no-op
        // correction recalcs it; if not, nothing to persist (the display path computes virtually and
        // the calc still honors the permission on read). Reuse a public recalc if present; otherwise:
        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date, ct);
        if (rec is not null)
            await _attendance.CorrectAsync(rec.Id,
                new HR.Modules.Attendance.DTOs.CorrectAttendanceRequest { Reason = "permission recalc" }, ct);
    }

    private static int ToMinutes(string hhmm) { var t = TimeSpan.Parse(hhmm); return (int)t.TotalMinutes; }
    private static ValidationException Fail(string prop, string msg)
        => new(new[] { new ValidationFailure(prop, msg) });
}
```

> **Two wiring notes for the implementer:**
> 1. `ResolveShiftAsync` needs `IShiftResolver _shiftResolver` and `EmployeeScope` — add the field + constructor param (mirror `AttendanceService`'s constructor) and `using HR.Application.Common.Interfaces;` (or wherever `IShiftResolver`/`EmployeeScope` live — confirm from `AttendanceService.cs`). If injecting the resolver is awkward, the simpler alternative is to add a `Task RecalcAsync(Guid employeeId, DateTime date)` method to `IAttendanceService` and call that (it already resolves the shift internally) — prefer this if it keeps the executor thin.
> 2. `CorrectAsync` with only `Reason` set keeps the existing punches (its `CombineTime(...) ?? rec.CheckIn` fallback) and just recalcs — that's why it's the recompute trigger.

- [ ] **Step 5: Run the executor tests**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests --filter AttendancePermissionExecutorTests`
Expected: PASS (5).

- [ ] **Step 6: Verify DI discovery**

Run: `rg -n "AttendanceCorrectionExecutor" backend/src` to find how SP2's executor is registered (assembly scan vs explicit). If explicit, register `AttendancePermissionExecutor` the same way. Rebuild: `dotnet build backend/HR.sln` → success.

- [ ] **Step 7: Commit and push**

```bash
git add backend/src/HR.Modules/Attendance/Completion/AttendancePermissionExecutor.cs backend/src/HR.Application/Engines/Completion/EffectTypes.cs backend/tests/HR.Domain.Finance.Tests/AttendancePermissionExecutorTests.cs
git commit -m "feat(sp3): attendance permission executor (idempotent, capped, payroll-guarded)"
git push origin main; git push sanad main
```

---

### Task 6: Register the effect in the catalog + required-effects map

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs` (add descriptor)
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs` (add `ATTENDANCE_PERMISSION` entry)
- Test: `backend/tests/…Platform tests` — add a small assertion (see Step 1)

**Interfaces:**
- Consumes: `EffectTypes.AttendancePermission` (added in Task 5 Step 1).
- Produces: catalog descriptor for `"Attendance.Permission"`; `SystemRequestEffects.Required["ATTENDANCE_PERMISSION"]`.

- [ ] **Step 1: Write the failing test**

Find the Platform test project (search `SystemRequestEffects` in `backend/tests`). Add a test:

```csharp
[Fact]
public void Attendance_permission_is_registered()
{
    Assert.True(new HR.Modules.Platform.Services.Completion.EffectActionCatalog()
        .IsKnown(HR.Application.Engines.Completion.EffectTypes.AttendancePermission));
    Assert.True(HR.Modules.Platform.Services.Requests.SystemRequestEffects.Required
        .ContainsKey("ATTENDANCE_PERMISSION"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run the Platform test project with `--filter Attendance_permission_is_registered`. Expected: FAIL.

- [ ] **Step 3: Add the catalog descriptor**

In `EffectActionCatalog.cs` `Descriptors`, after the `AttendanceCorrect` descriptor block (ends at the `RequiredPermissions = new[] { "Attendance.Edit" }, },` around line 105):

```csharp
        new()
        {
            EffectType = EffectTypes.AttendancePermission,
            LabelAr = "استئذان حضور", LabelEn = "Attendance permission",
            DescriptionAr = "يعذر دقائق التأخير أو الخروج المبكر ضمن الفترة المصرّح بها.",
            DescriptionEn = "Excuses late/early minutes within the permitted window.",
            Module = "Attendance",
            SupportedTriggers = FinalOnly,
            ExecutionMode = EffectExecutionMode.Transactional,
            Inputs = new[]
            {
                In("date", "التاريخ", "Date", true, FieldOrContext),
                In("from", "من", "From", true, FieldContextOrConstant),
                In("to", "إلى", "To", true, FieldContextOrConstant),
                In("reason", "السبب", "Reason", true, FieldContextOrConstant),
            },
            RequiredPermissions = new[] { "Attendance.Edit" },
        },
```

- [ ] **Step 4: Add the required-effect mapping**

In `SystemRequestEffects.cs`, after the `ATTENDANCE_CORRECTION` entry:

```csharp
            ["ATTENDANCE_PERMISSION"] = new[]
            {
                Transactional(EffectTypes.AttendancePermission, Map(
                    ("date", Field("startDate")),
                    ("from", Field("fromTime")),
                    ("to", Field("toTime")),
                    ("reason", Field("reason")))),
            },
```

- [ ] **Step 5: Run tests**

Run the Platform test filter → PASS. Then `dotnet build backend/HR.sln` → success.

- [ ] **Step 6: Commit and push**

```bash
git add backend/src/HR.Modules/Platform/Services/Completion/EffectActionCatalog.cs backend/src/HR.Modules/Platform/Services/Requests/SystemRequestEffects.cs backend/tests/
git commit -m "feat(sp3): register Attendance.Permission action + required effect mapping"
git push origin main; git push sanad main
```

---

### Task 7: Seed the request type + form

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs`
- Test: Platform test project (assert the seeder/form-builder includes it)

**Interfaces:**
- Consumes: `EnsureRequest`, `F(...)`, `FormSpec`, `FormBuilders` (existing).
- Produces: `ATTENDANCE_PERMISSION` request type with form fields `startDate`, `fromTime`, `toTime`, `reason`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Attendance_permission_form_has_window_fields()
{
    var codes = new HR.Modules.Platform.Services.Requests.RequestSeeder(/* ctor args as other tests use, or */)
        .SystemFormFields("ATTENDANCE_PERMISSION").Select(f => f.Code).ToList();
    Assert.Contains("startDate", codes);
    Assert.Contains("fromTime", codes);
    Assert.Contains("toTime", codes);
    Assert.Contains("reason", codes);
}
```

> If `RequestSeeder` is not trivially constructible in a unit test, instead assert via the `FormBuilders` path the existing tests use (search the Platform tests for `SystemFormFields(` to copy the established construction).

- [ ] **Step 2: Run to verify it fails.** Expected: FAIL (unknown code → empty list).

- [ ] **Step 3: Add the form builder**

In `RequestSeeder.cs`, after `AttendanceCorrectionForm()` (around line 350):

```csharp
    private static FormSpec AttendancePermissionForm() => new("FORM_ATTENDANCE_PERMISSION", "نموذج استئذان", "Attendance Permission Form", new()
    {
        F("startDate", "التاريخ", "Date", FieldType.Date, true),
        F("fromTime", "من (HH:mm)", "From (HH:mm)", FieldType.Text, true, placeholder: "15:00"),
        F("toTime", "إلى (HH:mm)", "To (HH:mm)", FieldType.Text, true, placeholder: "17:00"),
        F("reason", "السبب", "Reason", FieldType.TextArea, true),
    });
```

- [ ] **Step 4: Register the type + form builder**

Add the `EnsureRequest` call after the `ATTENDANCE_CORRECTION` line (~line 72):

```csharp
        created += await EnsureRequest("ATTENDANCE_PERMISSION", "استئذان", "Attendance Permission", catTimeOff, null, wfManager, null,
            AttendancePermissionForm(), Impact(attendance: true), "Clock4", "#2DD4BF", ct);
```

Add to the `FormBuilders` dictionary (after the `ATTENDANCE_CORRECTION` entry, ~line 479):

```csharp
            ["ATTENDANCE_PERMISSION"]  = AttendancePermissionForm,
```

- [ ] **Step 5: Run tests + build.** Platform filter → PASS; `dotnet build backend/HR.sln` → success.

- [ ] **Step 6: Commit and push**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/RequestSeeder.cs backend/tests/
git commit -m "feat(sp3): seed ATTENDANCE_PERMISSION request type + استئذان form"
git push origin main; git push sanad main
```

---

### Task 8: Workflow notification rules

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs`
- Test: Platform test project

**Interfaces:**
- Produces: 5 `ATTENDANCE_PERMISSION:*` seeded rules returned by `SystemWorkflowNotificationRules.For("ATTENDANCE_PERMISSION")`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Attendance_permission_has_five_notification_rules()
    => Assert.Equal(5, HR.Modules.Platform.Services.Requests.SystemWorkflowNotificationRules
        .For("ATTENDANCE_PERMISSION").Count);
```

- [ ] **Step 2: Run to verify it fails.** Expected: FAIL (0).

- [ ] **Step 3: Add the rules**

In `SystemWorkflowNotificationRules.cs`, add after the `ATTENDANCE_CORRECTION` entry in the `Rules` dictionary:

```csharp
            ["ATTENDANCE_PERMISSION"] = new[]
            {
                new SeededRule("ATTENDANCE_PERMISSION:Submitted:Requester", WorkflowNotificationEvent.Submitted, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم استلام طلب الاستئذان", "Attendance permission received",
                    "تم استلام طلب الاستئذان رقم {{Request.Number}} وهو قيد المراجعة.",
                    "Your attendance permission {{Request.Number}} was received and is under review."),
                new SeededRule("ATTENDANCE_PERMISSION:StepAssigned:CurrentApprover", WorkflowNotificationEvent.StepAssigned, null,
                    new[] { R(NotificationRecipientType.CurrentApprover) },
                    "طلب استئذان بانتظار موافقتك", "An attendance permission needs your approval",
                    "طلب استئذان رقم {{Request.Number}} من {{Employee.FullName}} بانتظار موافقتك.",
                    "Attendance permission {{Request.Number}} from {{Employee.FullName}} awaits your approval."),
                new SeededRule("ATTENDANCE_PERMISSION:Rejected:Requester", WorkflowNotificationEvent.Rejected, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تم رفض طلب الاستئذان", "Attendance permission rejected",
                    "نأسف لإبلاغك برفض طلب الاستئذان رقم {{Request.Number}}.",
                    "Your attendance permission {{Request.Number}} was rejected."),
                new SeededRule("ATTENDANCE_PERMISSION:Returned:Requester", WorkflowNotificationEvent.Returned, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "أُعيد طلب الاستئذان للتعديل", "Attendance permission returned",
                    "أُعيد طلب الاستئذان رقم {{Request.Number}} للتعديل. يرجى مراجعته.",
                    "Your attendance permission {{Request.Number}} was returned for changes."),
                new SeededRule("ATTENDANCE_PERMISSION:FinalApproved:Requester", WorkflowNotificationEvent.FinalApproved, null,
                    new[] { R(NotificationRecipientType.Requester) },
                    "تمت الموافقة على الاستئذان", "Attendance permission approved",
                    "تمت الموافقة على طلب الاستئذان رقم {{Request.Number}} وتم تطبيقه.",
                    "Your attendance permission {{Request.Number}} has been approved and applied."),
            },
```

- [ ] **Step 4: Run tests → PASS.**

- [ ] **Step 5: Commit and push**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/SystemWorkflowNotificationRules.cs backend/tests/
git commit -m "feat(sp3): 5 ATTENDANCE_PERMISSION workflow notification rules"
git push origin main; git push sanad main
```

---

### Task 9: Bump provisioning SeedVersion 5 → 6

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs` (line 50)
- Test: Platform test project (if a provisioning/version test exists, update it; else add one asserting the constant)

**Interfaces:**
- Produces: `RequestProvisioningService.CurrentSeedVersion == 6`. Reconcile already (a) EnsureRequest-creates the new type on provision, (b) `ReconcileRequiredEffects` adds the new required effect, (c) `ReconcileWorkflowNotificationRules` seeds the 5 rules, (d) `ReconcileSystemFormFieldsAsync` backfills the form fields — all keyed off the code, so no new reconcile code is needed.

- [ ] **Step 1: Update any existing version-pinned test** (search `CurrentSeedVersion` / `SeedVersion` in `backend/tests`). If a test asserts `== 5`, change it to `== 6` (write the failing expectation first).

- [ ] **Step 2: Change the constant**

`public const int CurrentSeedVersion = 6;`

- [ ] **Step 3: Run the Platform suite → PASS.** Then `dotnet build backend/HR.sln` → success.

- [ ] **Step 4: Commit and push**

```bash
git add backend/src/HR.Modules/Platform/Services/Requests/RequestProvisioningService.cs backend/tests/
git commit -m "feat(sp3): bump request SeedVersion 5->6 (provision attendance permission)"
git push origin main; git push sanad main
```

---

### Task 10: Preview endpoint (cap UX)

**Files:**
- Create: `backend/src/HR.Modules/Attendance/Services/AttendancePermissionPreviewService.cs` (+ interface)
- Modify: an attendance controller (add `POST api/attendance/permissions/preview`) — put it in the existing attendance controller (search `[Route("api/attendance` in `backend/src/HR.Api`)
- Test: `backend/tests/HR.Domain.Finance.Tests/AttendancePermissionPreviewTests.cs`

**Interfaces:**
- Produces: `IAttendancePermissionPreviewService.PreviewAsync(Guid employeeId, DateTime date, string from, string to, CancellationToken)` → `PermissionCapPreview(int UsedCount, int UsedMinutes, int RequestedMinutes, bool WouldExceed, string Mode)`.

- [ ] **Step 1: Write the failing test** — seed a policy with `PermissionMaxPerMonth = 1` + one existing permission this month; assert `PreviewAsync(...).WouldExceed == true` and `Mode == "Block"`. (Reuse `TestDb.New()` + the cap tally logic.)

- [ ] **Step 2: Run to verify it fails.**

- [ ] **Step 3: Implement the service** — same tally as `EnforceCapAsync` (Task 5) but returns the numbers instead of throwing. Factor the shared tally into this service and have the executor call it (DRY): the executor's `EnforceCapAsync` becomes "call preview; if `WouldExceed && mode==Block` throw". Compute `RequestedMinutes` via `PermissionMath.WindowMinutesWithinShift` (resolve shift as in Task 5).

- [ ] **Step 4: Add the controller action** — `[HttpPost("permissions/preview")]`, gated by an attendance-view permission consistent with the controller's other actions; bind `{ employeeId, date, from, to }`; return the preview DTO.

- [ ] **Step 5: Run tests + build → PASS.**

- [ ] **Step 6: Commit and push**

```bash
git add backend/src/HR.Modules/Attendance/Services/AttendancePermissionPreviewService.cs backend/src/HR.Api/ backend/src/HR.Modules/Attendance/Completion/AttendancePermissionExecutor.cs backend/tests/
git commit -m "feat(sp3): permission cap preview endpoint + shared tally"
git push origin main; git push sanad main
```

---

### Task 11: EF migration

**Files:**
- Create (generated): `backend/src/HR.Infrastructure/Migrations/*_Sp3AttendancePermission.cs`

**Interfaces:**
- Produces: DB schema — `attendance_permissions` table; `AttendanceRecord.ExcusedMinutes` column; `AttendancePolicy` 3 permission-cap columns.

- [ ] **Step 1: Generate the migration**

Run (from `backend`):

```bash
dotnet ef migrations add Sp3AttendancePermission --project src/HR.Infrastructure --startup-project src/HR.Api
```

- [ ] **Step 2: Review the generated migration** — confirm it contains: `CreateTable("attendance_permissions", ...)` with `EmployeeId, Date, FromMinutes, ToMinutes, ExcusedMinutes, Reason, RequestInstanceId, Source, CreatedAt, CreatedByUserId, TenantId, Id`; `AddColumn ExcusedMinutes` on the attendance records table (default 0); and 3 `AddColumn`s on the attendance policies table (`PermissionMaxPerMonth` nullable int, `PermissionMaxMinutesPerMonth` nullable int, `PermissionCapMode` int NOT NULL default 0). No unintended drops/renames.

- [ ] **Step 3: Verify it builds and applies to a scratch DB**

Run: `dotnet build backend/HR.sln` → success. (Do NOT apply to Azure here — deploy is a separate user-gated step.)

- [ ] **Step 4: Run the whole backend test suite once more**

Run: `dotnet test backend/tests/HR.Domain.Finance.Tests` and the Platform test project → all PASS.

- [ ] **Step 5: Commit and push**

```bash
git add backend/src/HR.Infrastructure/Migrations/
git commit -m "feat(sp3): EF migration — attendance_permissions + ExcusedMinutes + cap columns"
git push origin main; git push sanad main
```

---

## Deployment (user-gated, after all tasks green)

Follow SP2 mechanics (checkpoint memory):
1. Add a temp firewall rule for the dev IP; `dotnet ef database update` with the Key Vault password; delete the temp rule.
2. `dotnet publish src/HR.Api -c Release -o publish-out`; zip via `System.IO.Compression.ZipFile` (replace `\`→`/`); `az webapp deploy … --type zip`.
3. Reprovision tenant `00f97535`: `POST /api/requests/provision` → all types go SeedVersion **5→6**, `ATTENDANCE_PERMISSION` created with its form + 5 notif rules + required effect.
4. Behavioral verify via the SP1 admin-link-to-self-managed-employee trick: submit an استئذان for a late/early day, approve, confirm the day's shortage/late dropped and a permission row exists (employee self-service still blocked by the tenant-less login bug).

## Frontend

No new frontend code. The existing dynamic request form renders `startDate`/`fromTime`/`toTime`/`reason` exactly as it renders SP2's `checkIn`/`checkOut` (Text HH:mm). Optional later polish (out of this plan): show `AttendanceDayDto.ExcusedMinutes` in the attendance detail drawer.

## Self-Review

- **Spec coverage:** entity (T2) ✓; overlap/excuse calc with overnight+merge (T1) ✓; apply before late/shortage/payroll (T1+T3, payroll via persisted minutes) ✓; durable across recalc/sync/regeneration (T3 + T3 Step 5 sweep) ✓; monthly cap (T4 policy + T5 enforce + T10 preview) ✓; duplicate prevention (T5 idempotency) ✓; audit history (immutable rows T2 + notifications) ✓; workflow-driven notifications (T8) ✓; tests for late/early/temp-exit/overnight/overlap/cap (T1 + T5) ✓; migration (T11) ✓; provisioning (T6/T7/T8/T9) ✓.
- **Placeholder scan:** the only "copy from an existing test" notes are for test-harness construction (DbContext factory, executor context builder) that is codebase-specific and must be read at implementation time — each names the exact file to copy from. All production code is shown in full.
- **Type consistency:** `PermissionWindow(FromMinutes, ToMinutes)`, `PermissionMath.Excuse/.WindowMinutesWithinShift`, `AttendanceCalcResult.ExcusedMinutes`, `AttendancePermission` fields, `EffectTypes.AttendancePermission`, `AttendanceSources.AttendancePermission`, form field codes `startDate/fromTime/toTime/reason`, effect input keys `date/from/to/reason` — all consistent across tasks and matching the SystemRequestEffects Map in T6.
- **Open verification (flagged for the implementer, not blockers):** (a) confirm no third `_calc.Calculate` caller (T3 S5); (b) confirm executor DI registration style (T5 S6); (c) prefer adding `IAttendanceService.RecalcAsync(employeeId,date)` over injecting `IShiftResolver` into the executor if it keeps it thinner (T5 note).

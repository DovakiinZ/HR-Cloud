using System.Text.Json;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
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

    // ── Fakes ──────────────────────────────────────────────────────────────────

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

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static EffectContext Eff(Guid emp, string json, Guid? actor = null) => new()
    {
        RequestInstanceId = Guid.NewGuid(), RequestNumber = "R1", RequestTypeCode = "ATTENDANCE_CORRECTION",
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

    private sealed class ClosedPeriodGuard : HR.Application.Engines.Finance.IPayrollPeriodGuard
    {
        public Task EnsurePeriodOpenForAsync(Guid e, DateTime d, CancellationToken ct = default)
            => throw new HR.Application.Engines.Finance.PayrollPeriodClosedException(
                new HR.Application.Engines.Finance.PayrollPeriodClosedPayload(
                    "PAYROLL_PERIOD_CLOSED", Guid.NewGuid(), "PR-1", Guid.NewGuid(), d.Year, d.Month, "Locked"));
    }

    private sealed class FakePerms : HR.Application.Engines.Permissions.IPermissionResolver
    {
        private readonly string[] _p;
        public FakePerms(params string[] p) => _p = p;
        public Task<IReadOnlyList<string>> ResolveAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<string>)_p);
    }

    private static AttendanceCorrectionExecutor Sut(ApplicationDbContext db, HR.Modules.Attendance.Services.IAttendanceService att,
        HR.Application.Engines.Finance.IPayrollPeriodGuard? guard = null, HR.Application.Engines.Permissions.IPermissionResolver? perms = null)
        => new(db, att, guard ?? new OpenPeriodGuard(), perms ?? new FakePerms());

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Found_record_routes_to_CorrectAsync_and_stamps_reference()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = Guid.NewGuid();
        var rec = new AttendanceRecord { EmployeeId = emp, Date = Utc(2026, 7, 5), Status = AttendanceStatus.Late, LateMinutes = 45 };
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

    // ── Real-service recalculation tests ─────────────────────────────────────

    /// <summary>
    /// Proves that correcting a punch via the REAL AttendanceService actually recomputes late minutes
    /// from the shift schedule (anti-regression against the old "always zero" behaviour).
    ///
    /// Shift: 08:00–17:00, GraceAfterStart=0, no flexible flag.
    /// Correction: check-in at 08:45 → 45 minutes late → LateMinutes must be > 0.
    /// </summary>
    [Fact]
    public async Task Real_service_recomputes_late_from_corrected_punch()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");

        // Seed employee with Active status (default) so RecalcAsync can resolve its scope.
        var empEntity = new Employee
        {
            EmployeeNumber = "E-LATE-01", FirstName = "Rami", LastName = "Test",
            Email = "rami@t.local", BasicSalary = 3000m,
            // Status defaults to EmployeeStatus.Active
        };
        db.Employees.Add(empEntity);
        await db.SaveChangesAsync();
        var emp = empEntity.Id;

        // Seed a fixed day-shift 08:00–17:00 (480 min required, no grace, non-flexible).
        var shift = new Shift
        {
            NameAr = "دوام صباحي", NameEn = "Morning Shift",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(17, 0),
            RequiredMinutes = 480,
            BreakMinutes = 0,
            GraceAfterStartMinutes = 0,
            IsFlexible = false,
            IsActive = true,
            WeekendDays = "5,6", // Fri+Sat; 2026-07-05 is Sunday → working day
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        // Assign the shift directly to this employee so ShiftResolver gives specificity=4.
        db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftId = shift.Id,
            EmployeeId = emp,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = null,   // open-ended
            Priority = 10,
            IsActive = true,
        });

        // Seed an existing attendance record for the day (originally on-time, LateMinutes = 0).
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp, Date = Utc(2026, 7, 5),
            Status = AttendanceStatus.Present, LateMinutes = 0,
            CheckIn = Utc(2026, 7, 5).AddHours(8),
            CheckOut = Utc(2026, 7, 5).AddHours(17),
        });
        await db.SaveChangesAsync();

        // Wire the REAL service stack.
        var real = new AttendanceService(db, new FakeUser(),
            new AttendanceCalculationService(),
            new ShiftResolver());

        // Correct check-in to 08:45 — 45 minutes late.
        var ctx = Eff(emp, "{\"date\":\"2026-07-05\",\"checkIn\":\"08:45\",\"checkOut\":\"17:00\",\"reason\":\"late fixed\"}");
        await Sut(db, real).ExecuteAsync(ctx, default);

        var rec = await db.AttendanceRecords.SingleAsync(a => a.EmployeeId == emp && a.Date == Utc(2026, 7, 5));
        rec.LateMinutes.Should().BeGreaterThan(0,
            "the real AttendanceService must recompute lateness from the shift schedule, not zero it out");
        rec.LateMinutes.Should().Be(45,
            "check-in at 08:45 against shift start 08:00 with 0 grace = exactly 45 late minutes");
    }

    /// <summary>
    /// Locks in AttendanceCalculationService overnight-shift handling (line 92-93).
    ///
    /// Shift: 22:00–06:00 (overnight, 480 min required), GraceAfterStart=0.
    /// Correction: check-in 22:00, check-out 05:30 → gross=(05:30+24h - 22:00)=450 min,
    /// worked=450, shortage=30 min. ShortageMinutes must be ≥ 0 (no negative blowup).
    /// </summary>
    [Fact]
    public async Task Real_service_handles_overnight_shift_without_negative_minutes()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");

        var empEntity = new Employee
        {
            EmployeeNumber = "E-NIGHT-01", FirstName = "Nour", LastName = "Test",
            Email = "nour@t.local", BasicSalary = 3000m,
        };
        db.Employees.Add(empEntity);
        await db.SaveChangesAsync();
        var emp = empEntity.Id;

        // Overnight shift: starts 22:00, ends 06:00 next day (480 min required).
        var shift = new Shift
        {
            NameAr = "دوام ليلي", NameEn = "Night Shift",
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(6, 0),
            RequiredMinutes = 480,
            BreakMinutes = 0,
            GraceAfterStartMinutes = 0,
            IsFlexible = false,
            IsActive = true,
            WeekendDays = "5,6", // 2026-07-07 is Tuesday → working day
        };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        db.ShiftAssignments.Add(new ShiftAssignment
        {
            ShiftId = shift.Id,
            EmployeeId = emp,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EffectiveTo = null,
            Priority = 10,
            IsActive = true,
        });

        // Seed an attendance record for 2026-07-07 with on-time punches.
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            EmployeeId = emp, Date = Utc(2026, 7, 7),
            Status = AttendanceStatus.Present,
            CheckIn = Utc(2026, 7, 7).AddHours(22),
            CheckOut = Utc(2026, 7, 8).AddHours(6),
        });
        await db.SaveChangesAsync();

        var real = new AttendanceService(db, new FakeUser(),
            new AttendanceCalculationService(),
            new ShiftResolver());

        // Correct check-out to 05:30 (30 min early) — shortage but no negative minutes.
        var ctx = Eff(emp, "{\"date\":\"2026-07-07\",\"checkIn\":\"22:00\",\"checkOut\":\"05:30\",\"reason\":\"early out corrected\"}");
        await Sut(db, real).ExecuteAsync(ctx, default);

        var rec = await db.AttendanceRecords.SingleAsync(a => a.EmployeeId == emp && a.Date == Utc(2026, 7, 7));
        rec.ShortageMinutes.Should().BeGreaterOrEqualTo(0,
            "overnight gross is corrected by +24h; shortage must never go negative");
        rec.WorkedMinutes.Should().Be(450,
            "22:00→05:30 overnight = 450 worked minutes (gross 450, break 0)");
        rec.ShortageMinutes.Should().Be(30,
            "required 480 - worked 450 = 30 shortage minutes");
    }
}

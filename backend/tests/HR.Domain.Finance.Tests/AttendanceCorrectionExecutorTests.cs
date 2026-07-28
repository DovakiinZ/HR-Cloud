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

    private sealed class FakePerms : HR.Application.Engines.Permissions.IPermissionResolver
    {
        private readonly string[] _p;
        public FakePerms(params string[] p) => _p = p;
        public Task<IReadOnlyList<string>> ResolveAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<string>)_p);
    }

    // Uses 2-arg ctor for this task; guard/perms fakes kept for future tasks.
    private static AttendanceCorrectionExecutor Sut(ApplicationDbContext db, HR.Modules.Attendance.Services.IAttendanceService att,
        HR.Application.Engines.Finance.IPayrollPeriodGuard? guard = null, HR.Application.Engines.Permissions.IPermissionResolver? perms = null)
        => new(db, att);

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
}

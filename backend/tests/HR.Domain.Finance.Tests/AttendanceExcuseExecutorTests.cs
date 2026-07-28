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

public class AttendanceExcuseExecutorTests
{
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

    private static EffectContext Effctx(Guid emp, JsonElement payload) => new()
    { RequestInstanceId = Guid.NewGuid(), RequestNumber = "R1", RequestTypeCode = "X", EmployeeId = emp, Payload = payload };

    // NOTE: AttendanceCorrectionExecutor was rewritten in SP2 task 3 to route through
    // IAttendanceService (real recalc) instead of brute-force zeroing. The old test
    // for that behavior is superseded by AttendanceCorrectionExecutorTests.cs.

    [Fact]
    public async Task Leave_upserts_existing_day_and_zeroes_minutes_no_duplicate()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = Guid.NewGuid();
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp, Date = Utc(2026,7,5), Status = AttendanceStatus.Late, LateMinutes = 30 });
        await db.SaveChangesAsync();

        var payload = JsonDocument.Parse("{\"startDate\":\"2026-07-05\",\"endDate\":\"2026-07-05\"}").RootElement;
        await new AttendanceApplyLeaveDaysExecutor(db).ExecuteAsync(Effctx(emp, payload), default);

        var recs = await db.AttendanceRecords.Where(a => a.EmployeeId == emp && a.Date == Utc(2026,7,5)).ToListAsync();
        recs.Should().HaveCount(1); // upsert, not duplicate
        recs[0].Status.Should().Be(AttendanceStatus.OnLeave);
        recs[0].LateMinutes.Should().Be(0);
    }
}

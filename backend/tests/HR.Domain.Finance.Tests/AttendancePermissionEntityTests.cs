using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Attendance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionEntityTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext NewDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new FakeUser());

    [Fact]
    public async Task Permission_row_persists_and_reads_back()
    {
        await using var db = NewDb();
        var emp = Guid.NewGuid();
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp,
            Date = new DateTime(2026, 8, 3),
            FromMinutes = 900,
            ToMinutes = 1020,
            ExcusedMinutes = 120,
            Reason = "موعد طبي",
            RequestInstanceId = Guid.NewGuid(),
            Source = AttendanceSources.AttendancePermission,
        });
        await db.SaveChangesAsync();

        var row = await db.AttendancePermissions.SingleAsync(p => p.EmployeeId == emp);
        Assert.Equal(120, row.ExcusedMinutes);
        Assert.Equal("AttendancePermission", row.Source);
    }
}

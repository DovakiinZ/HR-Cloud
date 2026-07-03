using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Modules.Employees.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

// NOTE: Employee lives in HR.Modules.Employees.Entities. IPayrollValidator / PayrollRun / PayrollRunPopulation
// namespaces: resolve by searching if the compiler complains (they are HR.Application.Engines.Finance and
// HR.Domain.Engines.Finance.Entities respectively).
namespace HR.Domain.Finance.Tests;

public class AttendanceDeductionRunTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }
    // Audit is invoked by CalculateAsync; a no-op fake keeps the test focused.
    private sealed class FakeAudit : IAuditLogService
    {
        public Task LogAsync(string action, string entityType, Guid entityId, object? oldValues, object? newValues, CancellationToken ct = default)
            => Task.CompletedTask;
    }
    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options, new FakeUser());
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static PayrollRunEngine Engine(ApplicationDbContext db)
    {
        var calc = new AttendanceWageCalculator(db);
        var facts = new PayrollFactProvider(db, null!, calc); // scope unused: population is explicit
        var computation = new PayrollComputation(db, facts, new RuleEngine(db), new PayrollTransactionConsumer(db));
        var sync = new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
        return new PayrollRunEngine(db, computation,
            new PayrollValidationEngine(Array.Empty<IPayrollValidator>()),
            new FakeUser(), new FakeAudit(), null!, sync, // scope null!: CalculateAsync never calls it
            new PayrollRunStalenessEvaluator(db, new PayrollTransactionConsumer(db)));
    }

    [Fact]
    public async Task Calculate_materializes_approved_attendance_records_even_without_sync_now()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();
        var version = await db.PayrollDefinitionVersions.FirstAsync(v => v.PayrollDefinitionId == defId);

        var emp = new Employee { EmployeeNumber = "E1", FirstName = "Ali", LastName = "S", Email = "a@t.local", BasicSalary = 3000m };
        db.Employees.Add(emp);
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
            db.MasterDataItems.Add(new MasterDataItem { ObjectType = MasterDataObjectType.DeductionType, Code = code, NameAr = code, NameEn = code });
        db.AttendanceRecords.Add(new AttendanceRecord { EmployeeId = emp.Id, Date = Utc(2026,7,2), Status = AttendanceStatus.Absent });
        await db.SaveChangesAsync();

        var run = new PayrollRun
        {
            RunNumber = "PR-TEST-1", PayrollDefinitionId = defId, PayrollDefinitionVersionId = version.Id,
            RuleSetVersionId = version.RuleSetVersionId, PeriodStart = Utc(2026,7,1), PeriodEnd = Utc(2026,7,31),
            State = PayrollRunState.Draft, Currency = "SAR",
        };
        db.PayrollRuns.Add(run);
        db.PayrollRunPopulations.Add(new PayrollRunPopulation { PayrollRunId = run.Id, EmployeeId = emp.Id, IsIncluded = true });
        await db.SaveChangesAsync();

        await Engine(db).CalculateAsync(run.Id);

        (await db.PayrollTransactions.CountAsync(t => t.SourceModule == "Attendance"
            && t.Status == PayrollTransactionStatus.Approved && t.EmployeeId == emp.Id)).Should().Be(1);
    }
}

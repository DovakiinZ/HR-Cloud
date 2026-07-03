using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 17 (SP3): create-from-run endpoint — context inheritance,
/// EffectiveDate defaulting, period validation, RunPage origin stamping.</summary>
public class CreateFromRunTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId           => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId         => CreateFromRunTests.TenantId;
        public string? Email         => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated  => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    // ── local factory / seed helpers (mirror the brief's TestFactory / TestSeed) ──

    private static ICreateFromRunService CreateFromRunSvc(ApplicationDbContext db) =>
        new CreateFromRunService(db, new PayrollTransactionService(db, new FakeUser(), new PayrollPeriodGuard(db)));

    /// <summary>Seeds a standard definition, a Draft run for the given year/month, and one employee.
    /// Returns (defId, employeeId, run).</summary>
    private static async Task<(Guid defId, Guid empId, PayrollRun run)>
        DraftRunWithEmployee(ApplicationDbContext db, int year, int month)
    {
        // Definition + version (cutoff=27, carry=true → July period end = Jul 31)
        var def = new PayrollDefinition
        {
            TenantId = TenantId,
            Code     = $"MONTHLY-{Guid.NewGuid():N}",
            Name     = "Monthly",
            Status   = PayrollDefinitionStatus.Active,
        };
        db.PayrollDefinitions.Add(def);

        var ver = new PayrollDefinitionVersion
        {
            TenantId            = TenantId,
            PayrollDefinitionId = def.Id,
            VersionNumber       = 1,
            Status              = VersionStatus.Published,
            Frequency           = PayFrequency.Monthly,
            CutoffDay           = 27,
            CarryToNextPeriod   = true,
            DayBasis            = DayBasis.Fixed30,
            Currency            = "SAR",
            PublishedAt         = DateTime.UtcNow,
        };
        db.PayrollDefinitionVersions.Add(ver);
        def.CurrentVersionId = ver.Id;

        var emp = new Employee
        {
            TenantId       = TenantId,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2020, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
            BasicSalary    = 3000m,
        };
        db.Employees.Add(emp);

        // PeriodEnd = realistic calendar month-end (e.g. Jul 31).
        // With CutoffDay=27 and CarryToNextPeriod=true, Resolve(Jul-31) would yield August — proving
        // that the old PeriodEnd default was broken. Fix 1 changes the default to PeriodStart (Jul 1)
        // which always resolves to the run's own period, making the no-date path safe for carry runs.
        var periodEnd = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);

        var run = new PayrollRun
        {
            TenantId                   = TenantId,
            RunNumber                  = $"RUN-{year}-{month:D2}-{Guid.NewGuid():N}",
            PayrollDefinitionId        = def.Id,
            PayrollDefinitionVersionId = ver.Id,
            TargetPeriodYear           = year,
            TargetPeriodMonth          = month,
            PeriodStart                = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd                  = periodEnd,
            State                      = PayrollRunState.Draft,
            Currency                   = "SAR",
        };
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();

        return (def.Id, emp.Id, run);
    }

    /// <summary>Seeds a DeductionType master-data item with the given code.</summary>
    private static async Task<Guid> DeductionType(ApplicationDbContext db, string code)
    {
        var item = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code       = code,
            NameAr     = code,
            NameEn     = code,
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_from_run_inherits_context_and_stamps_provenance()
    {
        await using var ctx = Ctx($"t17-provenance-{Guid.NewGuid()}");
        var svc = CreateFromRunSvc(ctx);
        var (defId, emp, run) = await DraftRunWithEmployee(ctx, 2026, 7);
        var typeId = await DeductionType(ctx, "MANUAL");

        var id = await svc.CreateAsync(run.Id, new CreateFromRunRequest(
            emp, PayrollTransactionKind.Deduction, typeId, 100m, EffectiveDate: null, Notes: "adj"), default);

        var txn = await ctx.PayrollTransactions.FindAsync(id);
        txn!.Origin.Should().Be(PayrollTransactionOrigin.RunPage);
        txn.CreatedFromRunId.Should().Be(run.Id);
        txn.SourceModule.Should().Be("Manual");
        txn.Status.Should().Be(PayrollTransactionStatus.PendingApproval);
        (txn.TargetPeriodYear, txn.TargetPeriodMonth).Should().Be((2026, 7));
    }

    [Fact]
    public async Task Supplied_effective_date_outside_run_period_is_rejected()
    {
        await using var ctx = Ctx($"t17-out-of-period-{Guid.NewGuid()}");
        var svc = CreateFromRunSvc(ctx);
        var (defId, emp, run) = await DraftRunWithEmployee(ctx, 2026, 7);
        var typeId = await DeductionType(ctx, "MANUAL2");

        var act = () => svc.CreateAsync(run.Id, new CreateFromRunRequest(
            emp, PayrollTransactionKind.Deduction, typeId, 100m,
            EffectiveDate: new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc), Notes: null), default);

        await act.Should().ThrowAsync<DomainException>();
    }
}

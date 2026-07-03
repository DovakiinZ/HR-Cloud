using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Modules.Employees.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 5 (SP3): guard wired into PayrollTransactionService at the
/// becomes-Approved boundary. ApproveAsync must throw PayrollPeriodClosedException when the
/// target period is immutable; it must allow approval when the run is still mutable.</summary>
public class PayrollTransactionGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId    => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId  => Tenant;
        public string? Email  => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    private static PayrollTransactionService Svc(ApplicationDbContext db) =>
        new(db, new FakeUser(), new PayrollPeriodGuard(db));

    // ── seed helpers ──────────────────────────────────────────────────────────────

    /// <summary>Seeds a published version + employee + run in the given state + population row.
    /// Returns (definitionId, employeeId, runId).</summary>
    private static async Task<(Guid defId, Guid empId, Guid runId)> SeedRunWithEmployeeAsync(
        ApplicationDbContext db, int year, int month, PayrollRunState state)
    {
        var def = new HR.Domain.Engines.Finance.Entities.PayrollDefinition
        {
            TenantId = Tenant,
            Code     = $"MONTHLY-{Guid.NewGuid():N}",
            Name     = "Monthly",
            Status   = PayrollDefinitionStatus.Active,
        };
        db.PayrollDefinitions.Add(def);

        var ver = new PayrollDefinitionVersion
        {
            TenantId            = Tenant,
            PayrollDefinitionId = def.Id,
            VersionNumber       = 1,
            Status              = VersionStatus.Published,
            Frequency           = PayFrequency.Monthly,
            CutoffDay           = 27,
            CarryToNextPeriod   = false,
            PublishedAt         = DateTime.UtcNow,
        };
        db.PayrollDefinitionVersions.Add(ver);
        def.CurrentVersionId = ver.Id;

        var emp = new Employee
        {
            TenantId       = Tenant,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2024, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
        };
        db.Employees.Add(emp);

        var run = new PayrollRun
        {
            TenantId                   = Tenant,
            RunNumber                  = $"RUN-{year}-{month:D2}-{Guid.NewGuid():N}",
            PayrollDefinitionId        = def.Id,
            PayrollDefinitionVersionId = ver.Id,
            TargetPeriodYear           = year,
            TargetPeriodMonth          = month,
            PeriodStart                = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd                  = new DateTime(year, month, 27, 0, 0, 0, DateTimeKind.Utc),
            State                      = state,
        };
        db.PayrollRuns.Add(run);

        var pop = new PayrollRunPopulation
        {
            TenantId       = Tenant,
            PayrollRunId   = run.Id,
            EmployeeId     = emp.Id,
            EmployeeNumber = emp.EmployeeNumber,
            EmployeeName   = $"{emp.FirstName} {emp.LastName}",
            IsIncluded     = true,
        };
        db.PayrollRunPopulations.Add(pop);

        await db.SaveChangesAsync();
        return (def.Id, emp.Id, run.Id);
    }

    /// <summary>Seeds a DeductionType master-data item and returns its id.</summary>
    private static async Task<Guid> SeedDeductionTypeAsync(ApplicationDbContext db, string code)
    {
        var type = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.DeductionType,
            Code       = code,
            NameAr     = code,
            NameEn     = code,
        };
        db.MasterDataItems.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_blocks_when_period_is_closed()
    {
        await using var ctx = Ctx($"guard-txn-block-{Guid.NewGuid()}");
        var (_, emp, _) = await SeedRunWithEmployeeAsync(ctx, 2026, 7, PayrollRunState.Approved);
        var svc    = Svc(ctx);
        var typeId = await SeedDeductionTypeAsync(ctx, "MANUAL");

        // Create a transaction with SubmitImmediately=true so it lands at PendingApproval
        var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp, typeId, 100m,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, null, null, SubmitImmediately: true), default);

        var act = () => svc.ApproveAsync(id, default);

        await act.Should().ThrowAsync<PayrollPeriodClosedException>();
    }

    [Fact]
    public async Task ApproveAsync_allows_when_run_is_mutable()
    {
        await using var ctx = Ctx($"guard-txn-allow-{Guid.NewGuid()}");
        var (_, emp, _) = await SeedRunWithEmployeeAsync(ctx, 2026, 7, PayrollRunState.Draft);
        var svc    = Svc(ctx);
        var typeId = await SeedDeductionTypeAsync(ctx, "MANUAL2");

        var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp, typeId, 100m,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, null, null, SubmitImmediately: true), default);

        var act = () => svc.ApproveAsync(id, default);

        await act.Should().NotThrowAsync();
    }
}

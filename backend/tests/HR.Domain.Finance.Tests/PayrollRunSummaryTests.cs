using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 14 (SP3): PayrollRunReadService.GetSummaryAsync — server-side KPI aggregates,
/// calc metadata, and CalculationStatus derived from IPayrollRunStalenessEvaluator.</summary>
public class PayrollRunSummaryTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId           => PayrollRunSummaryTests.UserId;
        public Guid TenantId         => PayrollRunSummaryTests.TenantId;
        public string? Email         => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated  => true;
    }

    private sealed class FakeAudit : IAuditLogService
    {
        public Task LogAsync(string action, string entityType, Guid entityId,
            object? oldValues, object? newValues, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class AllInclusiveScopeEngine : IScopeEngine
    {
        private readonly ApplicationDbContext _db;
        public AllInclusiveScopeEngine(ApplicationDbContext db) => _db = db;
        public IReadOnlyList<ScopeDimensionInfo> Dimensions() => Array.Empty<ScopeDimensionInfo>();
        public async Task<ScopeResolution> ResolveAsync(SelectionScope scope, CancellationToken ct)
        {
            var ids = await _db.Employees.AsNoTracking().Select(e => e.Id).ToListAsync(ct);
            return new ScopeResolution(ids, Array.Empty<ScopeExclusion>(), Array.Empty<string>());
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    private static PayrollRunEngine Engine(ApplicationDbContext db)
    {
        var scope     = new AllInclusiveScopeEngine(db);
        var calc      = new AttendanceWageCalculator(db);
        var facts     = new PayrollFactProvider(db, scope, calc);
        var comp      = new PayrollComputation(db, facts, new RuleEngine(db), new PayrollTransactionConsumer(db));
        var sync      = new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
        var staleness = new PayrollRunStalenessEvaluator(db, new PayrollTransactionConsumer(db));
        return new PayrollRunEngine(db, comp,
            new PayrollValidationEngine(Array.Empty<IPayrollValidator>()),
            new FakeUser(), new FakeAudit(), scope, sync, staleness);
    }

    private static PayrollRunReadService ReadSvc(ApplicationDbContext db)
        => new(db, new PayrollRunStalenessEvaluator(db, new PayrollTransactionConsumer(db)),
               new PayrollTransactionConsumer(db));

    private static PayrollTransactionService TxnSvc(ApplicationDbContext db) =>
        new(db, new FakeUser(), new PayrollPeriodGuard(db));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeds standard definition + attendance types + one employee + calculates a run.</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> SeedCalculatedRunAsync(
        ApplicationDbContext db, int year, int month)
    {
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
        {
            if (!await db.MasterDataItems.AnyAsync(m => m.Code == code &&
                    m.ObjectType == MasterDataObjectType.DeductionType))
            {
                db.MasterDataItems.Add(new MasterDataItem
                {
                    TenantId   = TenantId,
                    ObjectType = MasterDataObjectType.DeductionType,
                    Code = code, NameAr = code, NameEn = code,
                });
            }
        }

        var emp = new Employee
        {
            TenantId       = TenantId,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            BasicSalary    = 3000m,
            HireDate       = new DateTime(2020, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        var run = await Engine(db).CreateAsync(defId, PayrollPeriod.Monthly(year, month), default);
        await Engine(db).CalculateAsync(run.Id, default);
        run = (await db.PayrollRuns.FindAsync(run.Id))!;
        return (defId, emp, run);
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Summary_returns_null_for_unknown_run()
    {
        await using var db = Ctx($"summary-null-{Guid.NewGuid()}");
        var svc = ReadSvc(db);

        var result = await svc.GetSummaryAsync(Guid.NewGuid(), default);

        result.Should().BeNull("unknown run id returns null");
    }

    [Fact]
    public async Task Summary_returns_correct_KPIs_after_calculate()
    {
        await using var db = Ctx($"summary-kpis-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary.Should().NotBeNull();
        summary!.Id.Should().Be(run.Id);
        summary.RunNumber.Should().Be(run.RunNumber);
        summary.State.Should().Be("Preview");
        summary.TargetPeriodYear.Should().Be(2026);
        summary.TargetPeriodMonth.Should().Be(7);

        // KPI aggregates: 1 included employee, 0 excluded (scope includes all)
        summary.Kpis.IncludedEmployees.Should().Be(1);
        summary.Kpis.ExcludedEmployees.Should().Be(0);

        // Totals come from the run's maintained aggregates
        summary.Kpis.Gross.Should().Be(run.GrossTotal);
        summary.Kpis.Deductions.Should().Be(run.DeductionTotal);
        summary.Kpis.Net.Should().Be(run.NetTotal);

        // No approved transactions were added — consumable count = 0
        summary.Kpis.TransactionsConsumed.Should().Be(0);
        summary.Kpis.ApprovedNotConsumed.Should().Be(0);
    }

    [Fact]
    public async Task Summary_CalculationStatus_is_UpToDate_after_calculate()
    {
        await using var db = Ctx($"summary-uptodate-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary!.CalculationStatus.Should().Be("UpToDate",
            "a freshly-calculated run with no new consumable transactions is up to date");
    }

    [Fact]
    public async Task Summary_CalculationStatus_becomes_RecalculationRequired_when_approved_txn_added()
    {
        await using var db = Ctx($"summary-stale-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        // Seed a deduction type and create + approve a new transaction
        var dedType = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code = "MANUAL_DED_SUMMARY", NameAr = "MANUAL_DED_SUMMARY", NameEn = "MANUAL_DED_SUMMARY",
        };
        db.MasterDataItems.Add(dedType);
        await db.SaveChangesAsync();

        var txnSvc = TxnSvc(db);
        var txnId  = await txnSvc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, dedType.Id, 50m,
            Utc(2026, 7, 10), null, false, null, "task14-staleness", null,
            SubmitImmediately: true), default);
        await txnSvc.ApproveAsync(txnId, default);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary!.CalculationStatus.Should().Be("RecalculationRequired",
            "an approved transaction was added to the period after the snapshot was taken");
    }

    [Fact]
    public async Task Summary_ApprovedNotConsumed_is_1_when_one_new_approved_txn_exists()
    {
        await using var db = Ctx($"summary-approved-not-consumed-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var dedType = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code = "MANUAL_DED_ANC", NameAr = "MANUAL_DED_ANC", NameEn = "MANUAL_DED_ANC",
        };
        db.MasterDataItems.Add(dedType);
        await db.SaveChangesAsync();

        var txnSvc = TxnSvc(db);
        var txnId  = await txnSvc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, dedType.Id, 100m,
            Utc(2026, 7, 15), null, false, null, "task14-anc", null,
            SubmitImmediately: true), default);
        await txnSvc.ApproveAsync(txnId, default);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary!.Kpis.ApprovedNotConsumed.Should().Be(1,
            "one approved transaction exists that was not in the payslip snapshot");
    }

    [Fact]
    public async Task Summary_Calc_metadata_reflects_run_pointers()
    {
        await using var db = Ctx($"summary-calc-meta-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary!.Calc.Version.Should().Be(1, "first calculate produces version 1");
        summary.Calc.At.Should().NotBeNull();
        summary.Calc.ByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Summary_Timeline_contains_Draft_to_Preview_transition()
    {
        await using var db = Ctx($"summary-timeline-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var svc    = ReadSvc(db);
        var summary = await svc.GetSummaryAsync(run.Id, default);

        summary!.Timeline.Should().NotBeEmpty("calculate transitions Draft → Preview");
        summary.Timeline.Should().Contain(t => t.FromState == "Draft" && t.ToState == "Preview");
    }
}

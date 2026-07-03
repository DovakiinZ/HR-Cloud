using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Paging;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Common.Paging;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 15 (SP3): paginated run sub-resource read endpoints
/// (employees, excluded, validation, transactions, calculations).
/// Highest-risk test first: transaction bucketing by lifecycle state + snapshot membership.</summary>
public class PayrollRunSubResourcesTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId           => PayrollRunSubResourcesTests.UserId;
        public Guid TenantId         => PayrollRunSubResourcesTests.TenantId;
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

    /// <summary>Seeds standard definition + attendance types + one employee + calculates a run.
    /// Returns (defId, employee, run) where run is in Preview with a fresh snapshot.</summary>
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

    /// <summary>Identical to SeedCalculatedRunAsync but with one consumed approved transaction already folded
    /// into the payslip snapshot (recalculate after approving). Used by the bucketing test.</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> SeedCalculatedRunWithConsumedTxnAsync(
        ApplicationDbContext db, int year, int month)
    {
        var (defId, emp, run) = await SeedCalculatedRunAsync(db, year, month);

        // Create + approve a deduction THEN recalculate so the snapshot includes it as Consumed
        var dedType = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code = $"CONSUMED_TXN_{Guid.NewGuid():N}", NameAr = "X", NameEn = "X",
        };
        db.MasterDataItems.Add(dedType);
        await db.SaveChangesAsync();

        var txnSvc = TxnSvc(db);
        var txnId = await txnSvc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, dedType.Id, 25m,
            Utc(year, month, 10), null, false, null, "consumed-txn", null,
            SubmitImmediately: true), default);
        await txnSvc.ApproveAsync(txnId, default);

        // Recalculate to fold the approved txn into the snapshot (makes it "Consumed")
        await Engine(db).CalculateAsync(run.Id, default);
        run = (await db.PayrollRuns.FindAsync(run.Id))!;
        return (defId, emp, run);
    }

    /// <summary>Seeds an approved manual deduction for the given employee/period WITHOUT recalculating the
    /// run, so the txn is "ApprovedNotConsumed" (not yet in the snapshot).</summary>
    private static async Task<Guid> SeedApprovedManualDeductionAsync(
        ApplicationDbContext db, Employee emp, int year, int month, decimal amount)
    {
        var dedType = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code = $"MANUAL_ANC_{Guid.NewGuid():N}", NameAr = "Y", NameEn = "Y",
        };
        db.MasterDataItems.Add(dedType);
        await db.SaveChangesAsync();

        var txnSvc = TxnSvc(db);
        var txnId  = await txnSvc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, dedType.Id, amount,
            Utc(year, month, 15), null, false, null, "anc-txn", null,
            SubmitImmediately: true), default);
        await txnSvc.ApproveAsync(txnId, default);
        return txnId;
    }

    // ── Task 15 — highest-risk test: transaction bucketing ───────────────────────

    /// <summary>Seed a calculated run with 1 consumed txn + 1 approved-not-consumed txn.
    /// Assert buckets contain both "Consumed" and "ApprovedNotConsumed".</summary>
    [Fact]
    public async Task Transactions_are_bucketed_by_lifecycle()
    {
        await using var ctx = Ctx($"t15-buckets-{Guid.NewGuid()}");

        // 1 consumed txn (folded into the snapshot via recalculate)
        var (defId, emp, run) = await SeedCalculatedRunWithConsumedTxnAsync(ctx, 2026, 7);

        // 1 approved-not-consumed txn (approved after the last recalculate)
        await SeedApprovedManualDeductionAsync(ctx, emp, 2026, 7, 50m);

        var read = ReadSvc(ctx);
        var page = await read.GetTransactionsAsync(run.Id, new PagedRequest(), default);

        page.Items.Select(t => t.Bucket)
            .Should().Contain(new[] { "Consumed", "ApprovedNotConsumed" },
                "one txn was folded into the snapshot (Consumed) and one is approved but not yet included (ApprovedNotConsumed)");
    }

    // ── Employees sub-resource ────────────────────────────────────────────────────

    [Fact]
    public async Task GetEmployees_returns_paged_payslip_rows()
    {
        await using var ctx = Ctx($"t15-employees-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var page = await read.GetEmployeesAsync(run.Id, new PagedRequest(), default);

        page.Total.Should().Be(1, "one employee was in the population");
        page.Items.Should().HaveCount(1);
        page.Items[0].EmployeeNumber.Should().NotBeNullOrEmpty();
        page.Items[0].Gross.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task GetEmployees_search_filters_by_name()
    {
        await using var ctx = Ctx($"t15-employees-search-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var pageMatch = await read.GetEmployeesAsync(run.Id, new PagedRequest(Search: "Ali"), default);
        var pageNoMatch = await read.GetEmployeesAsync(run.Id, new PagedRequest(Search: "ZZZNoMatch"), default);

        pageMatch.Total.Should().Be(1, "employee name contains 'Ali'");
        pageNoMatch.Total.Should().Be(0, "no employee name matches 'ZZZNoMatch'");
    }

    // ── Excluded sub-resource ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetExcluded_returns_scope_excluded_rows()
    {
        await using var ctx = Ctx($"t15-excluded-{Guid.NewGuid()}");
        // We need an employee who was excluded by scope.
        // Seed a run directly with a population that has IsIncluded=false
        var defId = await new StandardPayrollSeeder(ctx).EnsureStandardMonthlyAsync();

        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
        {
            if (!await ctx.MasterDataItems.AnyAsync(m => m.Code == code &&
                    m.ObjectType == MasterDataObjectType.DeductionType))
            {
                ctx.MasterDataItems.Add(new MasterDataItem
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
            FirstName      = "Excluded",
            LastName       = "Guy",
            Email          = $"excl-{Guid.NewGuid():N}@test.local",
            BasicSalary    = 2000m,
            HireDate       = new DateTime(2020, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
        };
        ctx.Employees.Add(emp);
        await ctx.SaveChangesAsync();

        var run = await Engine(ctx).CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);

        // Manually add a scope-excluded population row for an additional employee
        var excludedEmp = new Employee
        {
            TenantId       = TenantId,
            EmployeeNumber = $"E-EXCL-{Guid.NewGuid():N}",
            FirstName      = "Scope",
            LastName       = "Excl",
            Email          = $"scope-excl-{Guid.NewGuid():N}@test.local",
            BasicSalary    = 0m,
            HireDate       = new DateTime(2020, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
        };
        ctx.Employees.Add(excludedEmp);
        ctx.PayrollRunPopulations.Add(new PayrollRunPopulation
        {
            TenantId           = TenantId,
            PayrollRunId       = run.Id,
            EmployeeId         = excludedEmp.Id,
            EmployeeNumber     = excludedEmp.EmployeeNumber,
            EmployeeName       = "Scope Excl",
            IsIncluded         = false,
            ExclusionReasonCode = "ExcludedByScope",
        });
        await ctx.SaveChangesAsync();

        var read = ReadSvc(ctx);
        var page = await read.GetExcludedAsync(run.Id, new PagedRequest(), default);

        page.Total.Should().BeGreaterThanOrEqualTo(1, "at least one scope-excluded population row exists");
        page.Items.Should().Contain(r => r.ReasonCode == "ExcludedByScope");
    }

    // ── Validation sub-resource ───────────────────────────────────────────────────

    [Fact]
    public async Task GetValidation_returns_empty_for_run_with_no_findings()
    {
        await using var ctx = Ctx($"t15-validation-empty-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var page = await read.GetValidationAsync(run.Id, new PagedRequest(), default);

        page.Total.Should().Be(0, "no validation findings were produced for this clean run");
        page.Items.Should().BeEmpty();
    }

    // ── Calculations sub-resource ────────────────────────────────────────────────

    [Fact]
    public async Task GetCalculations_returns_one_row_after_first_calculate()
    {
        await using var ctx = Ctx($"t15-calcs-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var page = await read.GetCalculationsAsync(run.Id, new PagedRequest(), default);

        page.Total.Should().Be(1);
        page.Items[0].Version.Should().Be(1);
        page.Items[0].TriggerSource.Should().Be("Manual");
        page.Items[0].IncludedEmployees.Should().Be(1);
    }

    [Fact]
    public async Task GetCalculations_orders_by_version_descending()
    {
        await using var ctx = Ctx($"t15-calcs-order-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);
        await Engine(ctx).CalculateAsync(run.Id, default); // version 2

        var read = ReadSvc(ctx);
        var page = await read.GetCalculationsAsync(run.Id, new PagedRequest(), default);

        page.Total.Should().Be(2);
        page.Items[0].Version.Should().Be(2, "latest version first");
        page.Items[1].Version.Should().Be(1);
    }

    [Fact]
    public async Task GetCalculation_by_version_returns_single_detail()
    {
        await using var ctx = Ctx($"t15-calc-detail-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var detail = await read.GetCalculationAsync(run.Id, 1, default);

        detail.Should().NotBeNull();
        detail!.Version.Should().Be(1);
        detail.Exclusions.Should().NotBeNull();
        detail.Findings.Should().NotBeNull();
    }

    [Fact]
    public async Task GetCalculation_returns_null_for_nonexistent_version()
    {
        await using var ctx = Ctx($"t15-calc-null-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var detail = await read.GetCalculationAsync(run.Id, 99, default);

        detail.Should().BeNull();
    }

    // ── Paging smoke test on /employees ─────────────────────────────────────────

    [Fact]
    public async Task GetEmployees_paging_respects_page_size()
    {
        await using var ctx = Ctx($"t15-paging-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(ctx, 2026, 7);

        var read = ReadSvc(ctx);
        var page1 = await read.GetEmployeesAsync(run.Id, new PagedRequest(Page: 1, PageSize: 1), default);
        var page2 = await read.GetEmployeesAsync(run.Id, new PagedRequest(Page: 2, PageSize: 1), default);

        page1.Total.Should().Be(1);
        page1.Items.Should().HaveCount(1);
        page2.Items.Should().BeEmpty("only 1 employee exists");
    }
}

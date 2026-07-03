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

/// <summary>TDD tests for Task 7 (SP3): PayrollRunStalenessEvaluator — derived check of whether a run's
/// payslip snapshot still matches its consumable transaction set.</summary>
public class PayrollRunStalenessTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId           => PayrollRunStalenessTests.UserId;
        public Guid TenantId         => PayrollRunStalenessTests.TenantId;
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

    /// <summary>Scope engine that resolves to all employees currently in the DB.</summary>
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
        var calc  = new AttendanceWageCalculator(db);
        var scope = new AllInclusiveScopeEngine(db);
        var facts = new PayrollFactProvider(db, scope, calc);
        var comp  = new PayrollComputation(db, facts, new RuleEngine(db), new PayrollTransactionConsumer(db));
        var sync  = new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
        return new PayrollRunEngine(db, comp,
            new PayrollValidationEngine(Array.Empty<IPayrollValidator>()),
            new FakeUser(), new FakeAudit(), scope, sync);
    }

    private static PayrollRunStalenessEvaluator Evaluator(ApplicationDbContext db) =>
        new(db, new PayrollTransactionConsumer(db));

    private static PayrollTransactionService TxnSvc(ApplicationDbContext db) =>
        new(db, new FakeUser(), new PayrollPeriodGuard(db));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeds a standard monthly definition + attendance types + one employee + calculates a run
    /// for (year, month). Returns (defId, employee, run) where the run is in Preview state with a fresh
    /// payslip snapshot. Attendance deduction types (ABSENCE/LATE/SHORTAGE) must be present because
    /// AttendancePayrollSyncService.ResolveTypesAsync throws when includeDed=true (the default) and they
    /// are missing — same approach as AttendanceDeductionRunTests.</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> SeedCalculatedRunAsync(
        ApplicationDbContext db, int year, int month)
    {
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        // Seed attendance deduction types required by AttendancePayrollSyncService.
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
        {
            if (!await db.MasterDataItems.AnyAsync(m => m.Code == code &&
                    m.ObjectType == MasterDataObjectType.DeductionType))
            {
                db.MasterDataItems.Add(new MasterDataItem
                {
                    TenantId   = TenantId,
                    ObjectType = MasterDataObjectType.DeductionType,
                    Code       = code,
                    NameAr     = code,
                    NameEn     = code,
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

        // reload run to get the latest state
        run = (await db.PayrollRuns.FindAsync(run.Id))!;
        return (defId, emp, run);
    }

    /// <summary>Seeds a DeductionType master-data item and returns its id.</summary>
    private static async Task<Guid> SeedDeductionTypeAsync(ApplicationDbContext db, string code = "MANUAL_DED")
    {
        // Avoid duplicates if the seeder already created it
        var existing = await db.MasterDataItems.FirstOrDefaultAsync(m => m.Code == code &&
            m.ObjectType == MasterDataObjectType.DeductionType);
        if (existing is not null) return existing.Id;

        var type = new MasterDataItem
        {
            TenantId   = TenantId,
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
    public async Task Run_is_not_stale_right_after_calculate()
    {
        // A freshly-calculated run: snapshot exactly matches the consumable set.
        await using var db = Ctx($"staleness-fresh-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        var evaluator = Evaluator(db);
        var result = await evaluator.IsStaleAsync(run.Id, default);

        result.Should().BeFalse(
            "a run is not stale immediately after Calculate — snapshot and consumable set are identical");
    }

    [Fact]
    public async Task Run_is_stale_when_approved_txn_added_after_snapshot()
    {
        // After Calculate, we add a new Approved deduction that falls in the same period.
        // The snapshot doesn't include it yet → the run is stale.
        await using var db = Ctx($"staleness-stale-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7);

        // Verify the run is in Preview (mutable) so the guard allows approval.
        run.State.Should().Be(PayrollRunState.Preview);

        var typeId = await SeedDeductionTypeAsync(db, "MANUAL_TEST");
        var svc    = TxnSvc(db);

        // Create transaction for this employee in period Jul-2026, then submit+approve it.
        var txnId = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, typeId, 50m,
            Utc(2026, 7, 10),       // effective date inside the period
            null, false, null, "test-staleness", null,
            SubmitImmediately: true),
            default);
        await svc.ApproveAsync(txnId, default);

        // Now the consumable set contains this new txn but the snapshot doesn't.
        var evaluator = Evaluator(db);
        var result = await evaluator.IsStaleAsync(run.Id, default);

        result.Should().BeTrue(
            "the snapshot was taken before the new Approved deduction was added");
    }
}

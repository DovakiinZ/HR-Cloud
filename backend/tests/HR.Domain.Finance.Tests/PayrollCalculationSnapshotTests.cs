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

/// <summary>TDD tests for Task 12 (SP3): append-only versioned PayrollRunCalculation snapshots
/// written on each CalculateAsync call — monotonic version chain, run pointer updates,
/// TriggerSource, and PreviousCalculationId linkage.</summary>
public class PayrollCalculationSnapshotTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── fakes ─────────────────────────────────────────────────────────────────────
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => PayrollCalculationSnapshotTests.TenantId;
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
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
        var scope = new AllInclusiveScopeEngine(db);
        var calc  = new AttendanceWageCalculator(db);
        var facts = new PayrollFactProvider(db, scope, calc);
        var comp  = new PayrollComputation(db, facts, new RuleEngine(db), new PayrollTransactionConsumer(db));
        var sync  = new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
        var staleness = new PayrollRunStalenessEvaluator(db, new PayrollTransactionConsumer(db));
        return new PayrollRunEngine(db, comp,
            new PayrollValidationEngine(Array.Empty<IPayrollValidator>()),
            new FakeUser(), new FakeAudit(), scope, sync, staleness);
    }

    /// <summary>Seeds standard definition + one employee and returns a Draft run for (year, month).</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> DraftRunWithEmployee(
        ApplicationDbContext db, int year, int month)
    {
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        // Seed attendance deduction types required by AttendancePayrollSyncService.
        foreach (var code in new[] { "ABSENCE", "LATE", "SHORTAGE" })
        {
            if (!await db.MasterDataItems.AnyAsync(m => m.Code == code &&
                    m.ObjectType == MasterDataObjectType.DeductionType))
            {
                db.MasterDataItems.Add(new HR.Domain.Engines.MasterData.MasterDataItem
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
        return (defId, emp, run);
    }

    // ── core snapshot test ───────────────────────────────────────────────────────

    [Fact]
    public async Task Each_calculate_appends_a_monotonic_versioned_snapshot()
    {
        await using var ctx = Ctx($"calc-snapshot-{Guid.NewGuid()}");
        var (defId, emp, run) = await DraftRunWithEmployee(ctx, 2026, 7);

        await Engine(ctx).CalculateAsync(run.Id, default);
        await Engine(ctx).CalculateAsync(run.Id, default);   // recalc

        var calcs = await ctx.PayrollRunCalculations.Where(c => c.PayrollRunId == run.Id)
            .OrderBy(c => c.CalculationVersion).ToListAsync();
        calcs.Select(c => c.CalculationVersion).Should().Equal(1, 2);
        calcs[1].PreviousCalculationId.Should().Be(calcs[0].Id);
        (await ctx.PayrollRuns.FindAsync(run.Id))!.CurrentCalculationVersion.Should().Be(2);
    }

    // ── supporting property tests ─────────────────────────────────────────────────

    [Fact]
    public async Task First_calculate_creates_snapshot_with_version_1_and_Manual_trigger()
    {
        await using var ctx = Ctx($"calc-snapshot-first-{Guid.NewGuid()}");
        var (_, _, run) = await DraftRunWithEmployee(ctx, 2026, 7);

        await Engine(ctx).CalculateAsync(run.Id, default);

        var calc = await ctx.PayrollRunCalculations
            .SingleAsync(c => c.PayrollRunId == run.Id);

        calc.CalculationVersion.Should().Be(1);
        calc.PreviousCalculationId.Should().BeNull();
        calc.TriggerSource.Should().Be(PayrollCalculationTriggerSource.Manual);
        calc.ChangeSummary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Second_calculate_uses_Recalculate_trigger_and_chains_correctly()
    {
        await using var ctx = Ctx($"calc-snapshot-chain-{Guid.NewGuid()}");
        var (_, _, run) = await DraftRunWithEmployee(ctx, 2026, 7);

        await Engine(ctx).CalculateAsync(run.Id, default);
        await Engine(ctx).CalculateAsync(run.Id, default);

        var calcs = await ctx.PayrollRunCalculations
            .Where(c => c.PayrollRunId == run.Id)
            .OrderBy(c => c.CalculationVersion)
            .ToListAsync();

        calcs[1].TriggerSource.Should().Be(PayrollCalculationTriggerSource.Recalculate);
        calcs[1].PreviousCalculationId.Should().Be(calcs[0].Id);
    }

    [Fact]
    public async Task Snapshot_captures_totals_and_engine_version()
    {
        await using var ctx = Ctx($"calc-snapshot-totals-{Guid.NewGuid()}");
        var (_, _, run) = await DraftRunWithEmployee(ctx, 2026, 7);

        await Engine(ctx).CalculateAsync(run.Id, default);

        var calc = await ctx.PayrollRunCalculations.SingleAsync(c => c.PayrollRunId == run.Id);
        var reloadedRun = await ctx.PayrollRuns.FindAsync(run.Id);

        calc.GrossTotal.Should().Be(reloadedRun!.GrossTotal);
        calc.DeductionTotal.Should().Be(reloadedRun.DeductionTotal);
        calc.NetTotal.Should().Be(reloadedRun.NetTotal);
        calc.PayrollEngineVersion.Should().NotBeNullOrEmpty();
        calc.CalculatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Snapshot_updates_run_calc_pointers()
    {
        await using var ctx = Ctx($"calc-pointers-{Guid.NewGuid()}");
        var (_, _, run) = await DraftRunWithEmployee(ctx, 2026, 7);

        await Engine(ctx).CalculateAsync(run.Id, default);

        var reloaded = await ctx.PayrollRuns.FindAsync(run.Id);
        reloaded!.CurrentCalculationVersion.Should().Be(1);
        reloaded.LastCalculatedAt.Should().NotBeNull();
        reloaded.LastCalculatedByUserId.Should().NotBeNull();
    }
}

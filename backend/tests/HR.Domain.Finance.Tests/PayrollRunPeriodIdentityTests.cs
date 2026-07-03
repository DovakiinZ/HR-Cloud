using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 2 (SP3): stored immutable TargetPeriodYear/Month on PayrollRun +
/// calc pointer columns (CurrentCalculationVersion, LastCalculatedAt, LastCalculatedByUserId).</summary>
public class PayrollRunPeriodIdentityTests
{
    // ── fakes ─────────────────────────────────────────────────────────────────────
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private sealed class FakeAudit : IAuditLogService
    {
        public Task LogAsync(string action, string entityType, Guid entityId,
            object? oldValues, object? newValues, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Scope that includes every employee that is already in db — sufficient for the seeded
    /// standard definition (no employees) and for tests that add employees before calling CreateAsync.</summary>
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

    // ── helpers ───────────────────────────────────────────────────────────────────
    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static PayrollRunEngine Engine(ApplicationDbContext db) =>
        Engine(db, new AllInclusiveScopeEngine(db));

    private static PayrollRunEngine Engine(ApplicationDbContext db, IScopeEngine scope)
    {
        var calc = new AttendanceWageCalculator(db);
        var facts = new PayrollFactProvider(db, scope, calc);
        var computation = new PayrollComputation(db, facts, new RuleEngine(db), new PayrollTransactionConsumer(db));
        var sync = new AttendancePayrollSyncService(db, facts, calc, new PayrollPeriodGuard(db));
        return new PayrollRunEngine(db, computation,
            new PayrollValidationEngine(Array.Empty<IPayrollValidator>()),
            new FakeUser(), new FakeAudit(), scope, sync);
    }

    // ── Step 1: failing test (RED before columns are added) ───────────────────────
    [Fact]
    public async Task CreateAsync_stamps_target_period_from_request()
    {
        await using var db = Ctx($"period-identity-{Guid.NewGuid()}");
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        var run = await Engine(db).CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);

        run.TargetPeriodYear.Should().Be(2026);
        run.TargetPeriodMonth.Should().Be(7);
    }

    // ── Step 7: immutability regression test ─────────────────────────────────────
    [Fact]
    public async Task Target_period_is_not_reassigned_by_calculate()
    {
        await using var db = Ctx($"period-immutability-{Guid.NewGuid()}");
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        var run = await Engine(db).CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);
        await Engine(db).CalculateAsync(run.Id);

        var reloaded = await db.PayrollRuns.FindAsync(run.Id);
        (reloaded!.TargetPeriodYear, reloaded.TargetPeriodMonth).Should().Be((2026, 7));
    }

    // ── Calc pointer columns exist and default correctly ──────────────────────────
    [Fact]
    public async Task CreateAsync_sets_calc_pointer_defaults()
    {
        await using var db = Ctx($"calc-pointers-{Guid.NewGuid()}");
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();

        var run = await Engine(db).CreateAsync(defId, PayrollPeriod.Monthly(2026, 7), default);

        run.CurrentCalculationVersion.Should().Be(0);
        run.LastCalculatedAt.Should().BeNull();
        run.LastCalculatedByUserId.Should().BeNull();
    }
}

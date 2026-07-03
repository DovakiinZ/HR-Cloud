using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 8 (SP3): staleness gate on Validate / Submit / Approve and
/// Recalculate-resets-to-Preview behaviour.</summary>
public class PayrollRunStaleGateTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId   = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ── fakes ────────────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId           => PayrollRunStaleGateTests.UserId;
        public Guid TenantId         => PayrollRunStaleGateTests.TenantId;
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

    private static PayrollTransactionService TxnSvc(ApplicationDbContext db) =>
        new(db, new FakeUser(), new PayrollPeriodGuard(db));

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Seeds standard definition + attendance deduction types + one employee and calculates
    /// a run for (year, month), returning (defId, employee, run-in-Preview).</summary>
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

    /// <summary>Seeds a DeductionType and creates + submits + approves a manual deduction
    /// transaction for emp in (year, month). This puts a new consumable txn that is not
    /// in the run snapshot, making the run stale.</summary>
    private static async Task ApprovedManualDeductionAsync(
        ApplicationDbContext db, Employee emp, int year, int month, decimal amount)
    {
        var code = "MANUAL_DED";
        var existing = await db.MasterDataItems.FirstOrDefaultAsync(m => m.Code == code &&
            m.ObjectType == MasterDataObjectType.DeductionType);
        Guid typeId;
        if (existing is not null)
        {
            typeId = existing.Id;
        }
        else
        {
            var type = new MasterDataItem
            {
                TenantId   = TenantId,
                ObjectType = MasterDataObjectType.DeductionType,
                Code = code, NameAr = code, NameEn = code,
            };
            db.MasterDataItems.Add(type);
            await db.SaveChangesAsync();
            typeId = type.Id;
        }

        var svc = TxnSvc(db);
        var txnId = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, typeId, amount,
            Utc(year, month, 10), null, false, null, "staleness-test", null,
            SubmitImmediately: true),
            default);
        await svc.ApproveAsync(txnId, default);
    }

    /// <summary>Drives a run all the way to Validated state (Calculate → Validate).</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> SeedValidatedRunAsync(
        ApplicationDbContext db, int year, int month)
    {
        var (defId, emp, run) = await SeedCalculatedRunAsync(db, year, month);
        var engine = Engine(db);
        await engine.ValidateAsync(run.Id, default);
        run = (await db.PayrollRuns.FindAsync(run.Id))!;
        return (defId, emp, run);
    }

    /// <summary>Drives a run to PendingApproval (Calculate → Validate → Submit).</summary>
    private static async Task<(Guid defId, Employee emp, PayrollRun run)> SeedSubmittedRunAsync(
        ApplicationDbContext db, int year, int month)
    {
        var (defId, emp, run) = await SeedValidatedRunAsync(db, year, month);
        var engine = Engine(db);
        await engine.SubmitForApprovalAsync(run.Id, default);
        run = (await db.PayrollRuns.FindAsync(run.Id))!;
        return (defId, emp, run);
    }

    // ── state-machine edge tests ──────────────────────────────────────────────────

    [Fact]
    public void StateMachine_allows_Validated_to_Preview()
    {
        PayrollRunStateMachine.CanTransition(PayrollRunState.Validated, PayrollRunState.Preview)
            .Should().BeTrue("recalculation from Validated must be allowed by the state machine");
    }

    [Fact]
    public void StateMachine_allows_PendingApproval_to_Preview()
    {
        PayrollRunStateMachine.CanTransition(PayrollRunState.PendingApproval, PayrollRunState.Preview)
            .Should().BeTrue("recalculation from PendingApproval must be allowed by the state machine");
    }

    [Fact]
    public void StateMachine_rejects_Approved_to_Preview()
    {
        PayrollRunStateMachine.CanTransition(PayrollRunState.Approved, PayrollRunState.Preview)
            .Should().BeFalse("an Approved run is immutable and cannot be recalculated back to Preview");
    }

    // ── stale gate tests ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cannot_validate_a_stale_run()
    {
        await using var db = Ctx($"gate-validate-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7); // Preview, calc snapshot
        await ApprovedManualDeductionAsync(db, emp, 2026, 7, 50m);     // makes it stale

        var engine = Engine(db);
        var act = () => engine.ValidateAsync(run.Id, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Message.Should().Contain("PAYROLL_RUN_STALE");
        ex.Code.Should().Be("PAYROLL_RUN_STALE");
    }

    [Fact]
    public async Task Cannot_submit_a_stale_run()
    {
        await using var db = Ctx($"gate-submit-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedCalculatedRunAsync(db, 2026, 7);
        await ApprovedManualDeductionAsync(db, emp, 2026, 7, 75m);

        var engine = Engine(db);
        var act = () => engine.SubmitForApprovalAsync(run.Id, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Message.Should().Contain("PAYROLL_RUN_STALE");
        ex.Code.Should().Be("PAYROLL_RUN_STALE");
    }

    [Fact]
    public async Task Cannot_approve_a_stale_run()
    {
        await using var db = Ctx($"gate-approve-{Guid.NewGuid()}");
        var (_, emp, run) = await SeedSubmittedRunAsync(db, 2026, 7); // PendingApproval, calc snapshot
        await ApprovedManualDeductionAsync(db, emp, 2026, 7, 50m);    // makes it stale

        var engine = Engine(db);
        var act = () => engine.ApproveAsync(run.Id, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Message.Should().Contain("PAYROLL_RUN_STALE");
        ex.Code.Should().Be("PAYROLL_RUN_STALE");
    }

    // ── recalculate-resets-to-Preview tests ──────────────────────────────────────

    [Fact]
    public async Task Recalculate_from_validated_resets_to_preview()
    {
        await using var db = Ctx($"recalc-validated-{Guid.NewGuid()}");
        var (_, _, run) = await SeedValidatedRunAsync(db, 2026, 7);
        run.State.Should().Be(PayrollRunState.Validated, "precondition");

        var engine = Engine(db);
        var updated = await engine.CalculateAsync(run.Id, default);

        updated.State.Should().Be(PayrollRunState.Preview,
            "recalculating from any mutable state always resets to Preview");
    }

    [Fact]
    public async Task Recalculate_from_pending_approval_resets_to_preview()
    {
        await using var db = Ctx($"recalc-pending-{Guid.NewGuid()}");
        var (_, _, run) = await SeedSubmittedRunAsync(db, 2026, 7);
        run.State.Should().Be(PayrollRunState.PendingApproval, "precondition");

        var engine = Engine(db);
        var updated = await engine.CalculateAsync(run.Id, default);

        updated.State.Should().Be(PayrollRunState.Preview,
            "recalculating from PendingApproval always resets to Preview");
    }

    [Fact]
    public async Task Recalculate_from_preview_stays_preview()
    {
        await using var db = Ctx($"recalc-preview-{Guid.NewGuid()}");
        var (_, _, run) = await SeedCalculatedRunAsync(db, 2026, 7);
        run.State.Should().Be(PayrollRunState.Preview, "precondition");

        var engine = Engine(db);
        var updated = await engine.CalculateAsync(run.Id, default);

        updated.State.Should().Be(PayrollRunState.Preview,
            "recalculating from Preview stays in Preview");
    }

    // ── reversed-transaction staleness coverage ───────────────────────────────────

    [Fact]
    public async Task Run_is_stale_when_snapshot_txn_is_no_longer_consumable()
    {
        // Seed a run where the initial Calculate picks up an approved deduction.
        // Then cancel/reverse that deduction so it is no longer consumable (status != Approved).
        // The snapshot still references it → run is stale (snapshotTxnIds.Except(consumableIds) branch).
        await using var db = Ctx($"stale-reversed-{Guid.NewGuid()}");
        var defId = await new StandardPayrollSeeder(db).EnsureStandardMonthlyAsync();
        var version = await db.PayrollDefinitionVersions
            .FirstAsync(v => v.PayrollDefinitionId == defId);

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
            FirstName      = "Khaled",
            LastName       = "Nasser",
            Email          = $"kh-{Guid.NewGuid():N}@test.local",
            BasicSalary    = 5000m,
            HireDate       = new DateTime(2020, 1, 1),
            DateOfBirth    = new DateTime(1985, 1, 1),
        };
        db.Employees.Add(emp);

        var typeCode = "DED_CANCELLED";
        var dedType = new MasterDataItem
        {
            TenantId   = TenantId,
            ObjectType = MasterDataObjectType.DeductionType,
            Code = typeCode, NameAr = typeCode, NameEn = typeCode,
        };
        db.MasterDataItems.Add(dedType);
        await db.SaveChangesAsync();

        // Create + approve a deduction BEFORE calculating so the snapshot picks it up.
        var svc = TxnSvc(db);
        var txnId = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp.Id, dedType.Id, 200m,
            Utc(2026, 7, 5), null, false, null, "will-be-cancelled", null,
            SubmitImmediately: true),
            default);
        await svc.ApproveAsync(txnId, default);

        // Calculate — snapshot now contains TXN:{txnId} in ComponentsJson.
        var run = new PayrollRun
        {
            RunNumber               = "PR-REV-TEST",
            PayrollDefinitionId     = defId,
            PayrollDefinitionVersionId = version.Id,
            RuleSetVersionId        = version.RuleSetVersionId,
            PeriodStart             = Utc(2026, 7, 1),
            PeriodEnd               = Utc(2026, 7, 31),
            TargetPeriodYear        = 2026,
            TargetPeriodMonth       = 7,
            State                   = PayrollRunState.Draft,
            Currency                = "SAR",
        };
        db.PayrollRuns.Add(run);
        db.PayrollRunPopulations.Add(new PayrollRunPopulation
        {
            PayrollRunId = run.Id, EmployeeId = emp.Id, IsIncluded = true,
        });
        run.EmployeeCount = 1;
        await db.SaveChangesAsync();

        await Engine(db).CalculateAsync(run.Id, default);

        // Confirm not stale right after calculate.
        var evaluator = new PayrollRunStalenessEvaluator(db, new PayrollTransactionConsumer(db));
        (await evaluator.IsStaleAsync(run.Id, default))
            .Should().BeFalse("not stale right after calculate — snapshot matches consumable set");

        // Simulate reversal/cancellation: mark the txn Cancelled so GetConsumableAsync no longer
        // includes it. The snapshot's ComponentsJson still references TXN:{txnId}.
        var txn = await db.PayrollTransactions.FindAsync(txnId);
        txn!.Status = PayrollTransactionStatus.Cancelled;
        await db.SaveChangesAsync();

        // The snapshot still references the original txn, but it's no longer consumable (Cancelled ≠ Approved).
        (await evaluator.IsStaleAsync(run.Id, default))
            .Should().BeTrue(
                "snapshot references a transaction that is no longer consumable " +
                "(snapshotTxnIds.Except(consumableIds) branch — simulates reversal/cancellation after snapshot)");
    }
}

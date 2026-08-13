using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

public class OvertimeEffectWiringTests
{
    [Fact]
    public void Overtime_request_is_wired_to_the_overtime_addition_effect()
    {
        var specs = SystemRequestEffects.Required["OVERTIME_REQUEST"];
        var overtime = Assert.Single(specs);

        Assert.Equal(EffectTypes.OvertimeCreateAddition, overtime.EffectType);
        Assert.Equal("startDate", overtime.Inputs["date"].Key);
        Assert.Equal("hours", overtime.Inputs["hours"].Key);
        Assert.Equal("reason", overtime.Inputs["reason"].Key);
    }

    // ─── Harness (mirrors AttendanceCorrectionProvisioningTests) ─────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public static readonly Guid UserId   = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        public static readonly Guid TenantId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
        Guid ICurrentUserService.UserId          => UserId;
        Guid ICurrentUserService.TenantId        => TenantId;
        string? ICurrentUserService.Email        => "overtime-test@hr.local";
        IReadOnlyList<string> ICurrentUserService.Permissions => Array.Empty<string>();
        bool ICurrentUserService.IsAuthenticated => true;
    }

    private static ApplicationDbContext MakeDb()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new FakeUser());

    private sealed class NoopPageTemplateSeeder : HR.Modules.Platform.Services.Documents.IPageTemplateSeeder
    {
        public Task<Guid> SeedAsync(CancellationToken ct) => Task.FromResult(Guid.Empty);
    }

    private sealed class NoopDocumentLibrarySeeder : HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder
    {
        public Task SeedAsync(Guid defaultPageTemplateId, CancellationToken ct) => Task.CompletedTask;
    }

    private static (ApplicationDbContext db, BackgroundExecutionContext bg, RequestProvisioningService svc)
        BuildHarness(ApplicationDbContext db)
    {
        var bg   = new BackgroundExecutionContext();
        _ = bg.Begin(FakeUser.TenantId, FakeUser.UserId, email: null, correlationId: Guid.NewGuid());
        var seeder = new RequestSeeder(db, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var svc    = new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
        return (db, bg, svc);
    }

    // ─── Provisioning regression: stale Attendance.Correct must be retired ───

    /// <summary>
    /// A tenant provisioned at SeedVersion=6 has an OVERTIME_REQUEST with the OLD wiring:
    ///   • Attendance.Correct (IsRequired=true, IsEnabled=true) — stale system effect
    ///   • Notification.Send  (IsRequired=false, IsEnabled=true) — tenant-added effect
    ///
    /// After re-provisioning to v7 the reconcile pass must:
    ///   • disable (retire) the stale Attendance.Correct effect,
    ///   • add a new Overtime.CreateAddition effect (IsEnabled=true),
    ///   • leave the tenant-added Notification.Send effect untouched (IsEnabled=true).
    /// </summary>
    [Fact]
    public async Task Stale_attendance_correct_effect_is_retired_and_overtime_addition_added_on_reprovision()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        // Provision the full catalogue first so all dependencies exist.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // Find the system OVERTIME_REQUEST type that was just seeded.
        var overtime = await db.RequestTypes
            .Include(t => t.Effects)
            .FirstAsync(t => t.Code == "OVERTIME_REQUEST");

        // Remove the correctly-wired Overtime.CreateAddition effect (simulating a v6 tenant
        // that was provisioned before Task 3 swapped the wiring).
        var correctEffect = overtime.Effects
            .FirstOrDefault(e => string.Equals(e.EffectType, EffectTypes.OvertimeCreateAddition, StringComparison.OrdinalIgnoreCase));
        if (correctEffect is not null)
            db.Set<RequestEffectDefinition>().Remove(correctEffect);

        // Add the OLD stale system effect (Attendance.Correct) — as it would have existed on a v6 tenant.
        var staleEffect = new RequestEffectDefinition
        {
            RequestTypeId = overtime.Id,
            EffectType = EffectTypes.AttendanceCorrect,
            Trigger = EffectTrigger.FinalApproval,
            IsRequired = true,
            IsEnabled = true,
            ExecutionMode = EffectExecutionMode.Transactional,
            ConfigurationJson = "{}",
            Sequence = 1,
        };
        db.Set<RequestEffectDefinition>().Add(staleEffect);

        // Add a tenant-authored (IsRequired=false) effect — must survive retire pass untouched.
        var tenantEffect = new RequestEffectDefinition
        {
            RequestTypeId = overtime.Id,
            EffectType = EffectTypes.NotificationSend,
            Trigger = EffectTrigger.FinalApproval,
            IsRequired = false,
            IsEnabled = true,
            ExecutionMode = EffectExecutionMode.Asynchronous,
            ConfigurationJson = "{}",
            Sequence = 99,
        };
        db.Set<RequestEffectDefinition>().Add(tenantEffect);

        // Roll back SeedVersion to 6 so the upgrade path runs.
        overtime.SeedVersion = 6;
        await db.SaveChangesAsync();

        // ── Act ───────────────────────────────────────────────────────────────
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // ── Assert ────────────────────────────────────────────────────────────
        var effects = await db.Set<RequestEffectDefinition>()
            .Where(e => e.RequestTypeId == overtime.Id)
            .ToListAsync();

        // 1. The stale Attendance.Correct system effect must have been retired (disabled).
        var staleRow = effects.FirstOrDefault(e =>
            string.Equals(e.EffectType, EffectTypes.AttendanceCorrect, StringComparison.OrdinalIgnoreCase));
        staleRow.Should().NotBeNull(because: "the stale Attendance.Correct row must still exist (never deleted)");
        staleRow!.IsEnabled.Should().BeFalse(
            because: "a stale required effect no longer in the shipped set must be retired (disabled)");

        // 2. The new Overtime.CreateAddition effect must exist and be enabled.
        var additionRow = effects.FirstOrDefault(e =>
            string.Equals(e.EffectType, EffectTypes.OvertimeCreateAddition, StringComparison.OrdinalIgnoreCase));
        additionRow.Should().NotBeNull(because: "reconcile must add the Overtime.CreateAddition effect");
        additionRow!.IsEnabled.Should().BeTrue(
            because: "the newly reconciled required effect must be enabled");

        // 3. The tenant-added Notification.Send effect must remain enabled (untouched).
        var tenantRow = effects.FirstOrDefault(e =>
            string.Equals(e.EffectType, EffectTypes.NotificationSend, StringComparison.OrdinalIgnoreCase)
            && !e.IsRequired);
        tenantRow.Should().NotBeNull(because: "tenant-added effects must not be removed");
        tenantRow!.IsEnabled.Should().BeTrue(
            because: "the retire pass must never touch tenant-added (IsRequired=false) effects");
    }
}

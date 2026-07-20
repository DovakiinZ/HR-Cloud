using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

/// <summary>
/// Provisioning of the built-in request types. Requires REPORTS_TEST_DB; skips without it.
///
/// These run against real PostgreSQL because the behaviour under test *is* the database behaviour:
/// the global tenant query filter and the SaveChanges tenant stamping are what make a tenant
/// execution scope work at all, and neither exists in an in-memory provider.
/// </summary>
public class RequestProvisioningTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    /// <summary>
    /// The real CurrentUserService shape: no HTTP principal, everything resolved from the ambient
    /// background scope. This is exactly how provisioning runs in production during onboarding.
    /// </summary>
    private sealed class ScopedUser : ICurrentUserService
    {
        private readonly IBackgroundExecutionContext _bg;
        public ScopedUser(IBackgroundExecutionContext bg) => _bg = bg;
        public Guid UserId => _bg.UserId ?? Guid.Empty;
        public Guid TenantId => _bg.IsActive ? _bg.TenantId : Guid.Empty;
        public string? Email => _bg.Email;
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => _bg.IsActive;
    }

    private sealed record Harness(
        ApplicationDbContext Db,
        BackgroundExecutionContext Background,
        RequestProvisioningService Provisioning);

    private static Harness Build()
    {
        var bg = new BackgroundExecutionContext();
        var user = new ScopedUser(bg);
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options, user);
        var seeder = new RequestSeeder(db,
            new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var provisioning = new RequestProvisioningService(
            db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
        return new Harness(db, bg, provisioning);
    }

    // Document seeding is a separate concern with its own tests; stubbing it keeps these focused on
    // request provisioning and keeps the fixtures fast.
    private sealed class NoopPageTemplateSeeder : HR.Modules.Platform.Services.Documents.IPageTemplateSeeder
    {
        public Task<Guid> SeedAsync(CancellationToken ct) => Task.FromResult(Guid.Empty);
    }

    private sealed class NoopDocumentLibrarySeeder : HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder
    {
        public Task SeedAsync(Guid defaultPageTemplateId, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>The default requests the product promises a new tenant.</summary>
    private static readonly string[] ExpectedCodes =
    {
        "LEAVE_REQUEST", "MISSING_PUNCH", "ATTENDANCE_CORRECTION", "OVERTIME_REQUEST",
        "EXPENSE_CLAIM", "LOAN_REQUEST", "SALARY_ADVANCE", "BUSINESS_TRIP",
        "SALARY_CERTIFICATE", "CUSTODY_REQUEST",
    };

    // ── 1. A new tenant gets the full catalogue ───────────────────────────────

    [SkippableFact]
    public async Task Provisioning_a_new_tenant_creates_every_default_request()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        var result = await h.Provisioning.ProvisionTenantAsync(tenantId, Guid.NewGuid(), default);

        result.TenantId.Should().Be(tenantId);
        result.Created.Should().BeGreaterThan(0);
        result.CorrelationId.Should().NotBeEmpty();

        using (h.Background.Begin(tenantId))
        {
            var codes = await db.RequestTypes.Select(t => t.Code).ToListAsync();
            codes.Should().Contain(ExpectedCodes);
            (await db.RequestTypes.AllAsync(t => t.IsSystem)).Should().BeTrue();
            (await db.RequestTypes.AllAsync(t => t.SeedVersion == RequestProvisioningService.CurrentSeedVersion))
                .Should().BeTrue(because: "every provisioned type is stamped with the shipped version");
        }

        await tx.RollbackAsync();
    }

    // ── 2. Idempotency ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Provisioning_twice_creates_nothing_the_second_time()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        var first = await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);
        var second = await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        first.Created.Should().BeGreaterThan(0);
        second.Created.Should().Be(0);
        second.Upgraded.Should().Be(0);
        second.AlreadyCurrent.Should().BeGreaterThan(0);

        using (h.Background.Begin(tenantId))
        {
            var duplicates = await db.RequestTypes.GroupBy(t => t.Code)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync();
            duplicates.Should().BeEmpty();

            // The same must hold for effects: a second pass must not re-add required effects.
            var leave = await db.RequestTypes.Include(t => t.Effects)
                .FirstAsync(t => t.Code == "LEAVE_REQUEST");
            leave.Effects.Select(e => e.EffectType).Should().OnlyHaveUniqueItems();
        }

        await tx.RollbackAsync();
    }

    // ── 3. SeedVersion upgrade ────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_tenant_behind_the_seed_version_is_upgraded_in_place()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        // Simulate a tenant provisioned by an older build: roll the version back and strip the
        // required effects, as a pre-effects build would have left them.
        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).FirstAsync(t => t.Code == "LEAVE_REQUEST");
            leave.SeedVersion = 0;
            db.RequestEffectDefinitions.RemoveRange(leave.Effects);
            await db.SaveChangesAsync();
        }

        var upgrade = await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        upgrade.Created.Should().Be(0, because: "the request types already exist");
        upgrade.Upgraded.Should().BeGreaterThan(0);

        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).FirstAsync(t => t.Code == "LEAVE_REQUEST");
            leave.SeedVersion.Should().Be(RequestProvisioningService.CurrentSeedVersion);
            leave.Effects.Select(e => e.EffectType).Should().Contain(new[]
            {
                EffectTypes.LeaveCreateApprovedLeave,
                EffectTypes.AttendanceApplyLeaveDays,
            });
            leave.Effects.Where(e => e.IsRequired).Should().OnlyContain(e => e.IsEnabled);
        }

        await tx.RollbackAsync();
    }

    // ── 4. Tenant customisations survive ──────────────────────────────────────

    [SkippableFact]
    public async Task An_upgrade_preserves_tenant_customisations()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        Guid customEffectId;
        Guid ownTypeId;
        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).FirstAsync(t => t.Code == "LEAVE_REQUEST");

            leave.NameAr = "طلب إجازة (مخصّص)";          // relabelled
            leave.Icon = "CustomIcon";
            leave.SeedVersion = 0;                        // force the upgrade path

            // An optional effect the tenant added themselves, and disabled.
            var custom = new RequestEffectDefinition
            {
                RequestTypeId = leave.Id,
                EffectType = EffectTypes.NotificationSend,
                Trigger = EffectTrigger.FinalApproval,
                Sequence = 99,
                IsEnabled = false,
                IsRequired = false,
                ExecutionMode = EffectExecutionMode.Asynchronous,
                ConfigurationJson = "{}",
            };
            db.RequestEffectDefinitions.Add(custom);

            // A request type of their own, which provisioning must never touch.
            var own = new RequestType
            {
                Code = "TENANT_OWN", NameAr = "طلب خاص", NameEn = "Tenant Own",
                FormDefinitionId = leave.FormDefinitionId, IsSystem = false, IsActive = true,
            };
            db.RequestTypes.Add(own);
            await db.SaveChangesAsync();
            customEffectId = custom.Id;
            ownTypeId = own.Id;
        }

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Code == "LEAVE_REQUEST");
            leave.NameAr.Should().Be("طلب إجازة (مخصّص)", because: "labels are the tenant's to change");
            leave.Icon.Should().Be("CustomIcon");

            var custom = await db.RequestEffectDefinitions.AsNoTracking().FirstAsync(e => e.Id == customEffectId);
            custom.IsEnabled.Should().BeFalse(because: "a disabled optional effect stays disabled");

            var own = await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Id == ownTypeId);
            own.SeedVersion.Should().Be(0, because: "provisioning never touches a tenant's own request types");
        }

        await tx.RollbackAsync();
    }

    // ── 5. Required effects are protected ─────────────────────────────────────

    [SkippableFact]
    public async Task A_disabled_required_effect_is_repaired_on_the_next_upgrade()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).FirstAsync(t => t.Code == "LEAVE_REQUEST");
            var balance = leave.Effects.First(e => e.EffectType == EffectTypes.LeaveCreateApprovedLeave);
            balance.IsEnabled = false;      // the damaging edit
            balance.IsRequired = false;
            leave.SeedVersion = 0;
            await db.SaveChangesAsync();
        }

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).AsNoTracking()
                .FirstAsync(t => t.Code == "LEAVE_REQUEST");
            var balance = leave.Effects.First(e => e.EffectType == EffectTypes.LeaveCreateApprovedLeave);
            balance.IsEnabled.Should().BeTrue(because: "leave balance deduction is not optional");
            balance.IsRequired.Should().BeTrue();
        }

        await tx.RollbackAsync();
    }

    // ── 6. Tenant isolation ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task Provisioning_one_tenant_does_not_leak_into_another()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantA, null, default);

        using (h.Background.Begin(tenantB))
        {
            (await db.RequestTypes.CountAsync()).Should().Be(0, because: "tenant B has not been provisioned");
        }

        await h.Provisioning.ProvisionTenantAsync(tenantB, null, default);

        int aCount, bCount;
        using (h.Background.Begin(tenantA)) aCount = await db.RequestTypes.CountAsync();
        using (h.Background.Begin(tenantB)) bCount = await db.RequestTypes.CountAsync();

        aCount.Should().Be(bCount).And.BeGreaterThan(0);

        using (h.Background.Begin(tenantA))
        {
            (await db.RequestTypes.AllAsync(t => t.TenantId == tenantA)).Should().BeTrue();
        }

        await tx.RollbackAsync();
    }

    // ── 7. Custody request and its effect ─────────────────────────────────────

    [SkippableFact]
    public async Task The_custody_request_is_seeded_with_its_configured_effect()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        using (h.Background.Begin(tenantId))
        {
            var custody = await db.RequestTypes.Include(t => t.Effects)
                .FirstOrDefaultAsync(t => t.Code == "CUSTODY_REQUEST");
            custody.Should().NotBeNull();

            var effect = custody!.Effects.Should().ContainSingle().Subject;
            effect.EffectType.Should().Be(EffectTypes.AssetsAssignCustody);
            effect.IsRequired.Should().BeTrue();
            effect.ExecutionMode.Should().Be(EffectExecutionMode.Transactional);

            var config = EffectConfiguration.TryParse(effect.ConfigurationJson);
            config.Should().NotBeNull();
            config!.Inputs["assetId"].Source.Should().Be(EffectValueSource.FormField);
            // The employee must come from the request, never a form field — otherwise a requester
            // could assign an asset to somebody else.
            config.Inputs["employeeId"].Source.Should().Be(EffectValueSource.RequestContext);

            // The mapped form fields must actually exist on the linked form.
            var fieldCodes = await db.FormFields
                .Where(f => f.FormDefinitionId == custody.FormDefinitionId)
                .Select(f => f.Code).ToListAsync();
            fieldCodes.Should().Contain(new[] { "assetId", "expectedReturnDate", "notes" });
        }

        await tx.RollbackAsync();
    }

    // ── 8. Legacy flags and configured effects never both fire ────────────────

    [SkippableFact]
    public async Task A_provisioned_request_does_not_emit_both_configured_and_legacy_effects()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantId = Guid.NewGuid();

        await h.Provisioning.ProvisionTenantAsync(tenantId, null, default);

        using (h.Background.Begin(tenantId))
        {
            var leave = await db.RequestTypes
                .Include(t => t.Effects).Include(t => t.ImpactMapping)
                .AsNoTracking().FirstAsync(t => t.Code == "LEAVE_REQUEST");

            // Both sources are populated on a provisioned system request — the impact mapping from
            // the original seeder, the definitions from provisioning. The factory's precedence rule
            // is what stops them both firing; this asserts the setup that rule has to handle.
            leave.ImpactMapping.Should().NotBeNull();
            leave.ImpactMapping!.AffectsLeaveBalance.Should().BeTrue();
            leave.Effects.Should().NotBeEmpty();

            var enabled = leave.Effects.Where(e => e.IsEnabled && e.Trigger == EffectTrigger.FinalApproval).ToList();
            enabled.Should().NotBeEmpty(because: "configured effects exist, so the factory must ignore the flags");
            enabled.Select(e => e.EffectType).Should().OnlyHaveUniqueItems(
                because: "one leave effect, not one per source");
        }

        await tx.RollbackAsync();
    }

    // ── 9. Correlation id flows into the scope ────────────────────────────────

    [SkippableFact]
    public async Task Provisioning_runs_inside_a_tenant_scope_and_restores_the_previous_one()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var outerTenant = Guid.NewGuid();
        var targetTenant = Guid.NewGuid();

        using (h.Background.Begin(outerTenant))
        {
            await h.Provisioning.ProvisionTenantAsync(targetTenant, null, default);

            // The nested scope must have been popped: provisioning another tenant from inside a
            // scope cannot leave the caller pointed at the wrong tenant.
            h.Background.TenantId.Should().Be(outerTenant);
        }

        h.Background.IsActive.Should().BeFalse();

        using (h.Background.Begin(targetTenant))
        {
            (await db.RequestTypes.CountAsync()).Should().BeGreaterThan(0);
        }
        using (h.Background.Begin(outerTenant))
        {
            (await db.RequestTypes.CountAsync()).Should().Be(0, because: "the outer tenant was never provisioned");
        }

        await tx.RollbackAsync();
    }
}

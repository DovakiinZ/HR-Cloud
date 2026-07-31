using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Forms;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

/// <summary>
/// Task 5: verifies that the ATTENDANCE_PERMISSION system request type and its
/// Attendance.CreatePermission effect are properly declared and provisioned.
///
/// Facts 1-2 are pure-static assertions (no DB) — they lock the catalogue declarations.
/// Facts 3-4 use an in-memory DB to verify the full provisioning round-trip.
/// </summary>
public class AttendancePermissionProvisioningTests
{
    // ─── Pure-static catalogue assertions (no DB needed) ─────────────────────

    [Fact]
    public void SystemFormFields_returns_six_expected_field_codes()
    {
        // Use a stub seeder — RequestSeeder.SystemFormFields is pure logic on FormBuilders dict.
        var seeder = new RequestSeeder(
            null!,   // db not needed for SystemFormFields
            new NoopPageTemplateSeeder(),
            new NoopDocumentLibrarySeeder());

        var fields = seeder.SystemFormFields("ATTENDANCE_PERMISSION");

        var codes = fields.Select(f => f.Code).ToList();
        codes.Should().BeEquivalentTo(
            new[] { "permissionType", "date", "fromTime", "toTime", "reason", "overrideReason" },
            options => options.WithoutStrictOrdering(),
            because: "the attendance-permission form must declare all six fields the executor reads");
    }

    [Fact]
    public void SystemFormFields_permissionType_is_required_dropdown_with_AttendancePermissionType_lookup()
    {
        var seeder = new RequestSeeder(null!, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var fields = seeder.SystemFormFields("ATTENDANCE_PERMISSION");

        var pt = fields.Single(f => f.Code == "permissionType");
        pt.FieldType.Should().Be(FieldType.Dropdown, because: "the user selects a permission type from a list");
        pt.IsRequired.Should().BeTrue(because: "a permission must always be linked to a type");
        pt.Options.Should().Contain("AttendancePermissionType",
            because: "the dropdown must feed from the AttendancePermissionType master-data object");
    }

    [Fact]
    public void SystemFormFields_time_fields_are_required_text_with_placeholder()
    {
        var seeder = new RequestSeeder(null!, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var fields = seeder.SystemFormFields("ATTENDANCE_PERMISSION");

        foreach (var key in new[] { "fromTime", "toTime" })
        {
            var f = fields.Single(f => f.Code == key);
            f.FieldType.Should().Be(FieldType.Text, because: $"{key} is a free-text HH:mm field");
            f.IsRequired.Should().BeTrue(because: $"{key} is required to define the permission window");
            f.Placeholder.Should().NotBeNullOrWhiteSpace(because: $"{key} needs a HH:mm format hint");
        }
    }

    [Fact]
    public void SystemFormFields_optional_fields_are_not_required()
    {
        var seeder = new RequestSeeder(null!, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var fields = seeder.SystemFormFields("ATTENDANCE_PERMISSION");

        fields.Single(f => f.Code == "reason").IsRequired.Should().BeFalse(
            because: "reason is optional — not every permission needs a written justification");
        fields.Single(f => f.Code == "overrideReason").IsRequired.Should().BeFalse(
            because: "overrideReason is only required at runtime when a cap limit is breached");
    }

    // ─── In-memory provisioning round-trip ───────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public static readonly Guid UserId   = Guid.Parse("cccccccc-0000-0000-0000-000000000005");
        public static readonly Guid TenantId = Guid.Parse("dddddddd-0000-0000-0000-000000000005");
        Guid ICurrentUserService.UserId          => UserId;
        Guid ICurrentUserService.TenantId        => TenantId;
        string? ICurrentUserService.Email        => "test@hr.local";
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

    private static (ApplicationDbContext db, RequestProvisioningService svc) BuildHarness()
    {
        var db = MakeDb();
        var bg = new BackgroundExecutionContext();
        _ = bg.Begin(FakeUser.TenantId, FakeUser.UserId, email: null, correlationId: Guid.NewGuid());
        var seeder = new RequestSeeder(db, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var svc    = new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
        return (db, svc);
    }

    [Fact]
    public async Task Provision_creates_attendance_permission_request_type()
    {
        var (db, svc) = BuildHarness();

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var type = await db.RequestTypes.FirstOrDefaultAsync(t => t.Code == "ATTENDANCE_PERMISSION");
        type.Should().NotBeNull(because: "RequestSeeder must create the ATTENDANCE_PERMISSION request type");
        type!.NameAr.Should().Be("استئذان");
        type.NameEn.Should().Be("Attendance Permission");
        type.IsSystem.Should().BeTrue();
        type.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Provision_wires_create_permission_effect_with_six_inputs()
    {
        var (db, svc) = BuildHarness();

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var type = await db.RequestTypes
            .Include(t => t.Effects)
            .FirstAsync(t => t.Code == "ATTENDANCE_PERMISSION");

        var effect = type.Effects.FirstOrDefault(e => e.EffectType == EffectTypes.AttendanceCreatePermission);
        effect.Should().NotBeNull(because: "the Attendance.CreatePermission effect must be provisioned on the request type");
        effect!.IsRequired.Should().BeTrue();
        effect.IsEnabled.Should().BeTrue();
        effect.Trigger.Should().Be(EffectTrigger.FinalApproval);
        effect.ExecutionMode.Should().Be(EffectExecutionMode.Transactional);

        var cfg = EffectConfiguration.TryParse(effect.ConfigurationJson);
        cfg.Should().NotBeNull(because: "the effect must have valid ConfigurationJson");
        cfg!.Inputs.Keys.Should().Contain(
            new[] { "permissionTypeId", "date", "fromTime", "toTime", "reason", "overrideReason" },
            because: "all six form-field mappings must be stored in the effect configuration");
    }

    [Fact]
    public async Task Provision_is_idempotent_for_attendance_permission()
    {
        var (db, svc) = BuildHarness();

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var count = await db.RequestTypes.CountAsync(t => t.Code == "ATTENDANCE_PERMISSION");
        count.Should().Be(1, because: "repeated provisioning must not create duplicate request types");
    }

    [Fact]
    public async Task Provision_adds_six_form_fields_to_attendance_permission_form()
    {
        var (db, svc) = BuildHarness();

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var type = await db.RequestTypes.FirstAsync(t => t.Code == "ATTENDANCE_PERMISSION");
        var fields = await db.FormFields
            .Where(f => f.FormDefinitionId == type.FormDefinitionId)
            .Select(f => f.Code)
            .ToListAsync();

        fields.Should().Contain(
            new[] { "permissionType", "date", "fromTime", "toTime", "reason", "overrideReason" },
            because: "all six form fields must be persisted by the seeder");
    }
}

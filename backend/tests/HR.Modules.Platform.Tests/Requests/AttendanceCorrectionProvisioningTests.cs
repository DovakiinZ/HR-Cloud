using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Domain.Engines.Forms;
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

/// <summary>
/// Task-8: provisioning bump 4→5 — verifies that the new ReconcileSystemFormFieldsAsync step
/// (a) adds shipped checkIn/checkOut fields to an ATTENDANCE_CORRECTION that was seeded before
///     those fields existed, (b) refreshes the system effect's ConfigurationJson, and (c) never
///     touches a tenant-authored (IsSystem = false) request type.
///
/// These use the in-memory provider because the behaviour under test (field-addition logic) does
/// not require the global tenant query filter or SaveChanges tenant stamping; those are covered by
/// RequestProvisioningTests (which needs a real PostgreSQL connection).
/// </summary>
public class AttendanceCorrectionProvisioningTests
{
    // ─── existing facts (notification + effect shape) ──────────────────────────

    [Fact]
    public void Seeds_five_attendance_correction_rules()
    {
        var rules = SystemWorkflowNotificationRules.For("ATTENDANCE_CORRECTION");
        rules.Should().HaveCount(5);
        rules.Select(r => r.Event).Should().BeEquivalentTo(new[]
        {
            WorkflowNotificationEvent.Submitted,
            WorkflowNotificationEvent.StepAssigned,
            WorkflowNotificationEvent.Rejected,
            WorkflowNotificationEvent.Returned,
            WorkflowNotificationEvent.FinalApproved,
        });
        rules.Single(r => r.Event == WorkflowNotificationEvent.StepAssigned)
             .Recipients.Single().Type.Should().Be(NotificationRecipientType.CurrentApprover);
        rules.Where(r => r.Event != WorkflowNotificationEvent.StepAssigned)
             .Should().OnlyContain(r => r.Recipients.Single().Type == NotificationRecipientType.Requester);
        rules.Select(r => r.SystemKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void System_effect_maps_check_in_and_out()
    {
        var specs = SystemRequestEffects.Required["ATTENDANCE_CORRECTION"];
        var correct = specs.Single(s => s.EffectType == EffectTypes.AttendanceCorrect);
        correct.Inputs.Keys.Should().Contain(new[] { "date", "reason", "checkIn", "checkOut" });
    }

    // ─── Harness for in-memory provisioning tests ─────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public static readonly Guid UserId   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid TenantId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
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

    private static (ApplicationDbContext db, BackgroundExecutionContext bg, RequestProvisioningService svc)
        BuildHarness(ApplicationDbContext db)
    {
        var bg   = new BackgroundExecutionContext();
        _ = bg.Begin(FakeUser.TenantId, FakeUser.UserId, email: null, correlationId: Guid.NewGuid());
        var seeder = new RequestSeeder(db, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var svc    = new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
        return (db, bg, svc);
    }

    // ─── Task-8 Fact 1 ────────────────────────────────────────────────────────

    /// <summary>
    /// Tenant was provisioned at SeedVersion=4 with an ATTENDANCE_CORRECTION form that only had
    /// startDate+reason (the old shipped fields). The v4→v5 upgrade must:
    ///   • add checkIn + checkOut to the form (by Code; additive only)
    ///   • leave the tenant-added custom field untouched
    ///   • stamp SeedVersion = 5
    ///   • refresh the AttendanceCorrect effect's ConfigurationJson to the shipped mapping
    ///   • be idempotent (a second ProvisionTenantAsync adds no duplicate fields)
    /// </summary>
    [Fact]
    public async Task Bump_adds_missing_punch_fields_and_keeps_customized_untouched()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        // Seed the full catalogue first so all dependencies (categories, workflows, etc.) exist.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // Simulate a tenant provisioned at v4: ATTENDANCE_CORRECTION only had startDate+reason.
        // Find the form and remove the checkIn/checkOut fields that the full seeder just added.
        var correction = await db.RequestTypes.FirstAsync(t => t.Code == "ATTENDANCE_CORRECTION");
        var formId = correction.FormDefinitionId;

        // Remove checkIn and checkOut — they were not present at v4.
        var newFields = await db.FormFields
            .Where(f => f.FormDefinitionId == formId && (f.Code == "checkIn" || f.Code == "checkOut"))
            .ToListAsync();
        db.FormFields.RemoveRange(newFields);

        // Add a tenant-authored custom field.
        var customField = new FormField
        {
            FormDefinitionId = formId,
            Code = "tenantCustomField",
            NameAr = "حقل مخصص",
            NameEn = "Custom Field",
            FieldType = FieldType.Text,
            IsRequired = false,
            SortOrder = 99,
        };
        db.Set<FormField>().Add(customField);

        // Set up an old-style AttendanceCorrect effect with only date+reason in config.
        var oldCfg = EffectConfiguration.Serialize(new Dictionary<string, EffectValueMapping>
        {
            ["date"]   = new() { Source = EffectValueSource.FormField, Key = "startDate" },
            ["reason"] = new() { Source = EffectValueSource.FormField, Key = "reason" },
        });
        var eff = await db.Set<RequestEffectDefinition>()
            .FirstOrDefaultAsync(e => e.RequestTypeId == correction.Id
                && e.EffectType == EffectTypes.AttendanceCorrect);
        if (eff is not null)
            eff.ConfigurationJson = oldCfg;

        // Roll back SeedVersion so provisioning enters the upgrade branch.
        correction.SeedVersion = 4;
        await db.SaveChangesAsync();

        // ── Act ───────────────────────────────────────────────────────────────
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // ── Assert ────────────────────────────────────────────────────────────
        var updatedType = await db.RequestTypes.FirstAsync(t => t.Code == "ATTENDANCE_CORRECTION");
        updatedType.SeedVersion.Should().Be(RequestProvisioningService.CurrentSeedVersion,
            because: "the type must be stamped to the new seed version");

        var fields = await db.FormFields
            .Where(f => f.FormDefinitionId == formId)
            .ToListAsync();

        // checkIn and checkOut must have been added back.
        fields.Should().Contain(f => f.Code == "checkIn",
            because: "checkIn is a shipped field that was missing at v4");
        fields.Should().Contain(f => f.Code == "checkOut",
            because: "checkOut is a shipped field that was missing at v4");

        // The tenant-added custom field must survive.
        fields.Should().Contain(f => f.Code == "tenantCustomField",
            because: "provisioning never removes tenant-added fields");

        // The effect config must now include checkIn and checkOut mappings.
        var updatedEff = await db.Set<RequestEffectDefinition>()
            .FirstAsync(e => e.RequestTypeId == correction.Id
                && e.EffectType == EffectTypes.AttendanceCorrect);
        var cfg = EffectConfiguration.TryParse(updatedEff.ConfigurationJson);
        cfg.Should().NotBeNull();
        cfg!.Inputs.Keys.Should().Contain("checkIn",
            because: "the refreshed effect config must map checkIn");
        cfg!.Inputs.Keys.Should().Contain("checkOut",
            because: "the refreshed effect config must map checkOut");

        // ── Idempotency: a second run must not duplicate fields ────────────────
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var fieldsAfterSecondRun = await db.FormFields
            .Where(f => f.FormDefinitionId == formId)
            .ToListAsync();

        var checkInCount = fieldsAfterSecondRun.Count(f => f.Code == "checkIn");
        var checkOutCount = fieldsAfterSecondRun.Count(f => f.Code == "checkOut");
        checkInCount.Should().Be(1, because: "idempotency: checkIn must appear exactly once");
        checkOutCount.Should().Be(1, because: "idempotency: checkOut must appear exactly once");
    }

    // ─── Task-8 Fact 2 ────────────────────────────────────────────────────────

    /// <summary>
    /// A tenant-authored ATTENDANCE_CORRECTION (IsSystem = false) must be completely ignored by
    /// the v5 reconcile: no fields are added, the effect config is unchanged, SeedVersion stays 0.
    /// </summary>
    [Fact]
    public async Task Customized_type_is_not_touched()
    {
        // ── Arrange ──────────────────────────────────────────────────────────
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        // Provision once to create master data, categories, workflows, etc.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // Retrieve the system ATTENDANCE_CORRECTION form so we can reuse its form definition
        // as a base for the tenant-authored type.
        var systemCorrection = await db.RequestTypes.FirstAsync(t => t.Code == "ATTENDANCE_CORRECTION");

        // Add a tenant-owned request type with the same code pattern but IsSystem=false.
        // It has its own form with only startDate+reason (old-style).
        var tenantForm = new HR.Domain.Engines.Forms.FormDefinition
        {
            Code = "FORM_TENANT_ATT_CORRECTION",
            NameEn = "Tenant Attendance Correction",
            NameAr = "تصحيح حضور مخصص",
            Module = "Requests",
            IsPublished = true,
            IsActive = true,
        };
        tenantForm.Fields.Add(new FormField { Code = "startDate", NameAr = "التاريخ", NameEn = "Date", FieldType = FieldType.Date, IsRequired = true, SortOrder = 0 });
        tenantForm.Fields.Add(new FormField { Code = "reason", NameAr = "السبب", NameEn = "Reason", FieldType = FieldType.TextArea, IsRequired = true, SortOrder = 1 });
        db.FormDefinitions.Add(tenantForm);

        var tenantType = new RequestType
        {
            Code = "ATTENDANCE_CORRECTION_CUSTOM",
            NameAr = "تصحيح حضور مخصص",
            NameEn = "Custom Attendance Correction",
            IsSystem = false,   // ← tenant-authored
            IsActive = true,
            SeedVersion = 0,
            FormDefinitionId = tenantForm.Id,
        };

        // Add an old-style effect with only date+reason.
        var oldCfg = EffectConfiguration.Serialize(new Dictionary<string, EffectValueMapping>
        {
            ["date"]   = new() { Source = EffectValueSource.FormField, Key = "startDate" },
            ["reason"] = new() { Source = EffectValueSource.FormField, Key = "reason" },
        });
        var tenantEff = new RequestEffectDefinition
        {
            RequestTypeId = tenantType.Id,
            EffectType = EffectTypes.AttendanceCorrect,
            Trigger = EffectTrigger.FinalApproval,
            IsRequired = true,
            IsEnabled = true,
            ExecutionMode = EffectExecutionMode.Transactional,
            ConfigurationJson = oldCfg,
            Sequence = 1,
        };
        db.Set<RequestType>().Add(tenantType);
        db.Set<RequestEffectDefinition>().Add(tenantEff);
        await db.SaveChangesAsync();

        // Capture state before.
        var fieldCountBefore = await db.FormFields
            .Where(f => f.FormDefinitionId == tenantForm.Id)
            .CountAsync();
        var cfgBefore = tenantEff.ConfigurationJson;

        // ── Act ───────────────────────────────────────────────────────────────
        // Roll back SeedVersion of SYSTEM type to force the upgrade path — our tenant type must still be skipped.
        systemCorrection.SeedVersion = 4;
        await db.SaveChangesAsync();
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // ── Assert ────────────────────────────────────────────────────────────
        // Tenant type's SeedVersion must remain 0 (provisioning never touches tenant types).
        var reloadedTenant = await db.RequestTypes
            .FirstAsync(t => t.Code == "ATTENDANCE_CORRECTION_CUSTOM");
        reloadedTenant.SeedVersion.Should().Be(0,
            because: "provisioning never touches a tenant-authored request type");

        // No new fields were added to the tenant form.
        var fieldCountAfter = await db.FormFields
            .Where(f => f.FormDefinitionId == tenantForm.Id)
            .CountAsync();
        fieldCountAfter.Should().Be(fieldCountBefore,
            because: "provisioning must not add fields to tenant-authored request types");

        // Effect config is unchanged.
        var reloadedEff = await db.Set<RequestEffectDefinition>()
            .FirstAsync(e => e.Id == tenantEff.Id);
        reloadedEff.ConfigurationJson.Should().Be(cfgBefore,
            because: "provisioning must not refresh effect config on tenant-authored types");
    }
}

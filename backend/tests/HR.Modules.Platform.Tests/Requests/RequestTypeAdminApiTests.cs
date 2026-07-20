using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Assets;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Engines.Workflows;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.DTOs.Requests;
using HR.Modules.Platform.Services.Assets;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

/// <summary>
/// Request-type and effect-definition administration. Requires REPORTS_TEST_DB; skips without it.
///
/// The protections under test are enforced in the service layer, so these exercise the services
/// directly — a UI-only guard would pass a controller test and still leave the API open.
/// </summary>
public class RequestTypeAdminApiTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

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
        IRequestTypeAdminService Types,
        IRequestEffectDefinitionService Effects,
        IAssetLookupService Assets,
        RequestProvisioningService Provisioning);

    private static Harness Build()
    {
        var bg = new BackgroundExecutionContext();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options, new ScopedUser(bg));
        var catalog = new EffectActionCatalog();
        var executors = new EffectExecutorRegistry(new IEffectExecutor[]
        {
            new HR.Modules.Platform.Services.Completion.Executors.AssetsAssignCustodyExecutor(db),
            new HR.Modules.Platform.Services.Completion.Executors.TaskCreateExecutor(db),
            new HR.Modules.Platform.Services.Completion.Executors.NotificationSendExecutor(
                db, NullLogger<HR.Modules.Platform.Services.Completion.Executors.NotificationSendExecutor>.Instance),
            new StubExecutor(EffectTypes.LeaveCreateApprovedLeave),
            new StubExecutor(EffectTypes.AttendanceApplyLeaveDays),
            new StubExecutor(EffectTypes.AttendanceCreatePunch),
            new StubExecutor(EffectTypes.AttendanceCorrect),
            new StubExecutor(EffectTypes.ExpenseCreateClaim),
            new StubExecutor(EffectTypes.LoanCreate),
        });
        var validator = new EffectConfigurationValidator(db, catalog, executors);
        var seeder = new RequestSeeder(db, new NoopPage(), new NoopLib());
        return new Harness(db, bg,
            new RequestTypeAdminService(db, validator, catalog, executors),
            new RequestEffectDefinitionService(db, validator, catalog, executors),
            new AssetLookupService(db),
            new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance));
    }

    /// <summary>Stands in for executors owned by other modules — the registry only needs the key
    /// to resolve for validation to pass.</summary>
    private sealed class StubExecutor : IEffectExecutor
    {
        public StubExecutor(string effectType) => EffectType = effectType;
        public string EffectType { get; }
        public Task<EffectExecutionResult> ExecuteAsync(EffectContext context, CancellationToken ct)
            => Task.FromResult(EffectExecutionResult.Ok());
    }

    private sealed class NoopPage : HR.Modules.Platform.Services.Documents.IPageTemplateSeeder
    { public Task<Guid> SeedAsync(CancellationToken ct) => Task.FromResult(Guid.Empty); }

    private sealed class NoopLib : HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder
    { public Task SeedAsync(Guid id, CancellationToken ct) => Task.CompletedTask; }

    /// <summary>A minimal form + workflow so a custom request type can be created and activated.</summary>
    private static async Task<(Guid FormId, Guid WorkflowId)> SeedFormAndWorkflowAsync(ApplicationDbContext db)
    {
        var form = new FormDefinition
        {
            Code = "FORM_T_" + Guid.NewGuid().ToString("N")[..6],
            NameEn = "Test Form", NameAr = "نموذج", Module = "Requests", IsActive = true,
        };
        form.Fields.Add(new FormField
        {
            FormDefinitionId = form.Id, Code = "assetId", NameEn = "Asset", NameAr = "الأصل",
            FieldType = FieldType.Dropdown, IsRequired = true, SortOrder = 0,
        });
        form.Fields.Add(new FormField
        {
            FormDefinitionId = form.Id, Code = "notes", NameEn = "Notes", NameAr = "ملاحظات",
            FieldType = FieldType.TextArea, IsRequired = false, SortOrder = 1,
        });
        db.FormDefinitions.Add(form);

        var wf = new WorkflowDefinition
        {
            Code = "WF_T_" + Guid.NewGuid().ToString("N")[..6],
            NameEn = "Test WF", NameAr = "دورة", TriggerEntityType = "RequestInstance", IsActive = true,
        };
        db.WorkflowDefinitions.Add(wf);
        await db.SaveChangesAsync();
        return (form.Id, wf.Id);
    }

    private static string CustodyConfig() => EffectConfiguration.Serialize(new Dictionary<string, EffectValueMapping>
    {
        ["assetId"] = new() { Source = EffectValueSource.FormField, Key = "assetId" },
        ["employeeId"] = new() { Source = EffectValueSource.RequestContext, Key = RequestContextKeys.EmployeeId },
    });

    // ── 1. System request cannot be deleted ───────────────────────────────────

    [SkippableFact]
    public async Task Deleting_a_system_request_type_is_refused()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var leave = await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Code == "LEAVE_REQUEST");
            var act = () => h.Types.DeleteAsync(leave.Id, default);
            await act.Should().ThrowAsync<ForbiddenException>();

            (await db.RequestTypes.AnyAsync(t => t.Id == leave.Id)).Should().BeTrue();
        }
        await tx.RollbackAsync();
    }

    // ── 2. Required effect cannot be deleted or disabled ──────────────────────

    [SkippableFact]
    public async Task A_required_effect_cannot_be_deleted_or_disabled()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).AsNoTracking()
                .FirstAsync(t => t.Code == "LEAVE_REQUEST");
            var required = leave.Effects.First(e => e.IsRequired);

            await h.Effects.Invoking(s => s.DeleteAsync(required.Id, default))
                .Should().ThrowAsync<ForbiddenException>();
            await h.Effects.Invoking(s => s.SetEnabledAsync(required.Id, false, default))
                .Should().ThrowAsync<ForbiddenException>();

            var still = await db.RequestEffectDefinitions.AsNoTracking().FirstAsync(e => e.Id == required.Id);
            still.IsEnabled.Should().BeTrue();
        }
        await tx.RollbackAsync();
    }

    // ── 3. Optional effects stay editable ─────────────────────────────────────

    [SkippableFact]
    public async Task An_optional_effect_can_be_added_disabled_and_deleted()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var custody = await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Code == "CUSTODY_REQUEST");

            var added = await h.Effects.AddAsync(custody.Id, new UpsertEffectDefinitionInput
            {
                EffectType = EffectTypes.NotificationSend,
                ConfigurationJson = EffectConfiguration.Serialize(new Dictionary<string, EffectValueMapping>
                {
                    ["subject"] = new() { Source = EffectValueSource.Constant, Key = "Custody assigned" },
                    ["body"] = new() { Source = EffectValueSource.Constant, Key = "Your asset is ready." },
                }),
            }, default);

            added.IsRequired.Should().BeFalse(because: "IsRequired is never taken from client input");
            added.CanDelete.Should().BeTrue();
            added.ExecutionMode.Should().Be(nameof(EffectExecutionMode.Asynchronous),
                because: "execution mode comes from the descriptor, not the caller");

            var disabled = await h.Effects.SetEnabledAsync(added.Id, false, default);
            disabled.IsEnabled.Should().BeFalse();

            await h.Effects.DeleteAsync(added.Id, default);
            (await db.RequestEffectDefinitions.AnyAsync(e => e.Id == added.Id)).Should().BeFalse();
        }
        await tx.RollbackAsync();
    }

    // ── 4 & 5. Invalid mapping and unknown effect are rejected ────────────────

    [SkippableFact]
    public async Task An_effect_mapped_to_a_missing_form_field_is_rejected()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();

        using (h.Background.Begin(tenant))
        {
            var (formId, wfId) = await SeedFormAndWorkflowAsync(db);
            var type = await h.Types.CreateAsync(NewTypeInput(formId, wfId), default);

            var act = () => h.Effects.AddAsync(type.Id, new UpsertEffectDefinitionInput
            {
                EffectType = EffectTypes.AssetsAssignCustody,
                ConfigurationJson = EffectConfiguration.Serialize(new Dictionary<string, EffectValueMapping>
                {
                    ["assetId"] = new() { Source = EffectValueSource.FormField, Key = "doesNotExist" },
                    ["employeeId"] = new() { Source = EffectValueSource.RequestContext, Key = RequestContextKeys.EmployeeId },
                }),
            }, default);

            var ex = (await act.Should().ThrowAsync<ValidationException>()).And;
            ex.Errors.SelectMany(kv => kv.Value).Should()
                .Contain(m => m.Contains("does not exist"),
                    because: "the message must name the missing form field, not just fail");
        }
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task An_unknown_effect_type_is_rejected()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();

        using (h.Background.Begin(tenant))
        {
            var (formId, wfId) = await SeedFormAndWorkflowAsync(db);
            var type = await h.Types.CreateAsync(NewTypeInput(formId, wfId), default);

            // The exact shape the catalog exists to refuse: a CLR type name from a client.
            var act = () => h.Effects.AddAsync(type.Id, new UpsertEffectDefinitionInput
            {
                EffectType = "HR.Domain.Entities.Employee, HR.Domain",
            }, default);

            await act.Should().ThrowAsync<ValidationException>();
            (await db.RequestEffectDefinitions.CountAsync(e => e.RequestTypeId == type.Id)).Should().Be(0);
        }
        await tx.RollbackAsync();
    }

    // ── 6. Activation validation ──────────────────────────────────────────────

    [SkippableFact]
    public async Task Activation_fails_without_a_workflow_and_succeeds_once_complete()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();

        using (h.Background.Begin(tenant))
        {
            var (formId, wfId) = await SeedFormAndWorkflowAsync(db);

            var noWorkflow = await h.Types.CreateAsync(NewTypeInput(formId, workflowId: null), default);
            var act = () => h.Types.SetActiveAsync(noWorkflow.Id, true, default);
            await act.Should().ThrowAsync<ValidationException>();
            (await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Id == noWorkflow.Id))
                .IsActive.Should().BeFalse();

            var complete = await h.Types.CreateAsync(NewTypeInput(formId, wfId), default);
            await h.Effects.AddAsync(complete.Id, new UpsertEffectDefinitionInput
            {
                EffectType = EffectTypes.AssetsAssignCustody,
                ConfigurationJson = CustodyConfig(),
            }, default);

            var activated = await h.Types.SetActiveAsync(complete.Id, true, default);
            activated.IsActive.Should().BeTrue();

            var validation = await h.Types.ValidateAsync(complete.Id, default);
            validation.IsValid.Should().BeTrue();
        }
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Activation_fails_when_a_required_effect_is_missing()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).FirstAsync(t => t.Code == "LEAVE_REQUEST");
            // Bypass the service to simulate rows lost to a bad migration or a direct edit.
            db.RequestEffectDefinitions.RemoveRange(leave.Effects);
            await db.SaveChangesAsync();

            var act = () => h.Types.SetActiveAsync(leave.Id, true, default);
            var ex = (await act.Should().ThrowAsync<ValidationException>()).And;
            ex.Errors.SelectMany(kv => kv.Value).Should().Contain(m => m.Contains("missing"));
        }
        await tx.RollbackAsync();
    }

    // ── 7 & 8. Duplication ────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Duplicating_a_system_request_produces_a_custom_deletable_copy()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).AsNoTracking()
                .FirstAsync(t => t.Code == "LEAVE_REQUEST");

            var copy = await h.Types.DuplicateAsync(leave.Id, new DuplicateRequestTypeInput
            {
                NameAr = "طلب إجازة مخصص", NameEn = "Custom Leave",
            }, default);

            copy.IsSystem.Should().BeFalse(because: "a copy belongs to the tenant, not the product");
            copy.SeedVersion.Should().Be(0, because: "provisioning must never adopt it");
            copy.IsActive.Should().BeFalse();
            copy.CanDelete.Should().BeTrue();
            copy.Code.Should().Be("LEAVE_REQUEST_COPY");
            copy.FormDefinitionId.Should().Be(leave.FormDefinitionId, because: "forms are referenced, not forked");
            copy.Effects.Should().HaveCount(leave.Effects.Count);
            copy.Effects.Should().OnlyContain(e => !e.IsRequired,
                because: "required-ness belongs to the system request; nothing would repair it on a copy");

            // And the copy really is removable, which is what makes duplication the supported way to
            // customise a system request beyond its labels.
            await h.Types.DeleteAsync(copy.Id, default);
            (await db.RequestTypes.AnyAsync(t => t.Id == copy.Id)).Should().BeFalse();
        }
        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Duplicating_twice_generates_distinct_codes()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenant, null, default);

        using (h.Background.Begin(tenant))
        {
            var custody = await db.RequestTypes.AsNoTracking().FirstAsync(t => t.Code == "CUSTODY_REQUEST");
            var input = new DuplicateRequestTypeInput { NameAr = "نسخة", NameEn = "Copy" };

            var first = await h.Types.DuplicateAsync(custody.Id, input, default);
            var second = await h.Types.DuplicateAsync(custody.Id, input, default);

            first.Code.Should().Be("CUSTODY_REQUEST_COPY");
            second.Code.Should().Be("CUSTODY_REQUEST_COPY2");
        }
        await tx.RollbackAsync();
    }

    // ── 9. Cross-tenant access ────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_request_type_from_another_tenant_is_not_reachable()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await h.Provisioning.ProvisionTenantAsync(tenantA, null, default);

        Guid foreignTypeId;
        Guid foreignEffectId;
        using (h.Background.Begin(tenantA))
        {
            var leave = await db.RequestTypes.Include(t => t.Effects).AsNoTracking()
                .FirstAsync(t => t.Code == "LEAVE_REQUEST");
            foreignTypeId = leave.Id;
            foreignEffectId = leave.Effects.First().Id;
        }

        using (h.Background.Begin(tenantB))
        {
            // NotFound rather than Forbidden: the query filter does not resolve the row at all, so
            // the response cannot confirm that it exists somewhere else.
            await h.Types.Invoking(s => s.GetAsync(foreignTypeId, default)).Should().ThrowAsync<NotFoundException>();
            await h.Types.Invoking(s => s.DeleteAsync(foreignTypeId, default)).Should().ThrowAsync<NotFoundException>();
            await h.Types.Invoking(s => s.SetActiveAsync(foreignTypeId, true, default)).Should().ThrowAsync<NotFoundException>();
            await h.Effects.Invoking(s => s.SetEnabledAsync(foreignEffectId, false, default)).Should().ThrowAsync<NotFoundException>();
            await h.Effects.Invoking(s => s.ListAsync(foreignTypeId, default)).Should().ThrowAsync<NotFoundException>();
        }
        await tx.RollbackAsync();
    }

    // ── 10. Asset lookup excludes assigned assets ─────────────────────────────

    [SkippableFact]
    public async Task Assignable_assets_exclude_anything_with_an_open_custody()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var h = Build();
        await using var db = h.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        var tenant = Guid.NewGuid();

        using (h.Background.Begin(tenant))
        {
            var free = new Asset { Code = "LAP-001", NameAr = "حاسوب", NameEn = "Laptop", IsActive = true };
            var held = new Asset { Code = "LAP-002", NameAr = "حاسوب ٢", NameEn = "Laptop 2", IsActive = true };
            var returned = new Asset { Code = "LAP-003", NameAr = "حاسوب ٣", NameEn = "Laptop 3", IsActive = true };
            var inactive = new Asset { Code = "LAP-004", NameAr = "حاسوب ٤", NameEn = "Laptop 4", IsActive = false };
            db.Set<Asset>().AddRange(free, held, returned, inactive);
            await db.SaveChangesAsync();

            db.Set<AssetCustody>().AddRange(
                new AssetCustody { AssetId = held.Id, EmployeeId = Guid.NewGuid(), AssignedAt = DateTime.UtcNow },
                // A returned custody must free the asset again.
                new AssetCustody
                {
                    AssetId = returned.Id, EmployeeId = Guid.NewGuid(),
                    AssignedAt = DateTime.UtcNow.AddDays(-30), ReturnedAt = DateTime.UtcNow.AddDays(-1),
                });
            await db.SaveChangesAsync();

            var assignable = await h.Assets.GetAssignableAsync(null, default);
            var codes = assignable.Select(a => a.Code).ToList();

            codes.Should().Contain("LAP-001");
            codes.Should().Contain("LAP-003", because: "its custody was returned");
            codes.Should().NotContain("LAP-002", because: "it is still out with someone");
            codes.Should().NotContain("LAP-004", because: "inactive assets are not assignable");
            assignable.Should().OnlyContain(a => a.Status == "Available");

            var all = await h.Assets.GetAllAsync(null, default);
            all.Should().Contain(a => a.Code == "LAP-002" && a.Status == "Assigned");

            var searched = await h.Assets.GetAssignableAsync("lap-001", default);
            searched.Should().ContainSingle(because: "search is case-insensitive");
        }
        await tx.RollbackAsync();
    }

    private static CreateRequestTypeInput NewTypeInput(Guid formId, Guid? workflowId) => new()
    {
        Code = "CUSTOM_" + Guid.NewGuid().ToString("N")[..6],
        NameAr = "طلب مخصص", NameEn = "Custom Request",
        FormDefinitionId = formId,
        WorkflowDefinitionId = workflowId,
    };
}

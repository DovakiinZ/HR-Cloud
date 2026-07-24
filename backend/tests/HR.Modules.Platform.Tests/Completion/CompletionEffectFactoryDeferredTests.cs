using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Harness ───────────────────────────────────────────────────────────────────

file sealed class FakeLeaveService : ILeaveService
{
    public LeaveRules GetRules(string? metadataJson) => new();
    public decimal ComputeDays(DateTime start, DateTime end, LeaveRules rules) => 0;
    public Task<List<LeaveTypeInfo>> GetLeaveTypesAsync(Guid employeeId, CancellationToken ct) => Task.FromResult(new List<LeaveTypeInfo>());
    public Task<LeavePreview> PreviewAsync(Guid employeeId, Guid leaveTypeId, DateTime? start, DateTime? end, bool hasAttachment, CancellationToken ct)
        => Task.FromResult(new LeavePreview());
}

file sealed class FakeUser2 : ICurrentUserService
{
    public Guid UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public string? Email => "test@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

/// <summary>
/// Sets up an in-memory ApplicationDbContext seeded with a RequestType that has one
/// RequestEffectDefinition (Deferred, MaxAttempts = 5) and a matching RequestInstance +
/// FormSubmission/FormSubmissionValues.
/// </summary>
file sealed class DeferredFactoryHarness : IAsyncDisposable
{
    public CompletionEffectFactory Factory { get; }
    public Guid RequestInstanceId { get; }
    private readonly ApplicationDbContext _db;

    private DeferredFactoryHarness(ApplicationDbContext db, CompletionEffectFactory factory, Guid instanceId)
    {
        _db = db;
        Factory = factory;
        RequestInstanceId = instanceId;
    }

    public static async Task<DeferredFactoryHarness> CreateAsync(
        string effectType,
        EffectExecutionMode mode,
        int maxAttempts,
        string configJson,
        Dictionary<string, string> formValues)
    {
        var dbName = $"DeferredHarness_{Guid.NewGuid()}";
        var fakeUser = new FakeUser2();
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options,
            fakeUser);

        var tenantId = fakeUser.TenantId;

        // FormDefinition (required FK on RequestType)
        var formDef = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "DEF_FORM",
            NameEn = "Test Form",
            NameAr = "نموذج اختبار",
            Module = "Platform",
        };
        db.Set<FormDefinition>().Add(formDef);

        // RequestType
        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "DEFERRED_TEST",
            NameEn = "Deferred Test",
            NameAr = "اختبار مؤجل",
            FormDefinitionId = formDef.Id,
            IsActive = true,
        };
        db.Set<RequestType>().Add(requestType);

        // RequestEffectDefinition
        var effectDef = new RequestEffectDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            EffectType = effectType,
            Trigger = EffectTrigger.FinalApproval,
            ExecutionMode = mode,
            MaxAttempts = maxAttempts,
            IsEnabled = true,
            Sequence = 1,
            ConfigurationJson = configJson,
        };
        db.Set<RequestEffectDefinition>().Add(effectDef);

        // FormSubmission
        var submission = new FormSubmission
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FormDefinitionId = formDef.Id,
            SubmittedById = fakeUser.UserId,
            SubmittedAt = DateTime.UtcNow,
            Status = FormSubmissionStatus.Submitted,
        };
        db.Set<FormSubmission>().Add(submission);

        // FormSubmissionValues — one row per entry
        foreach (var (fieldCode, value) in formValues)
        {
            db.FormSubmissionValues.Add(new FormSubmissionValue
            {
                Id = Guid.NewGuid(),
                FormSubmissionId = submission.Id,
                FormFieldId = Guid.NewGuid(), // FK not enforced in InMemory
                FieldCode = fieldCode,
                Value = value,
            });
        }

        // RequestInstance
        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-DEFER-001",
            EmployeeId = Guid.NewGuid(),
            FormSubmissionId = submission.Id,
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        db.Set<RequestInstance>().Add(instance);

        await db.SaveChangesAsync();

        var factory = new CompletionEffectFactory(db, new FakeLeaveService(), fakeUser);
        return new DeferredFactoryHarness(db, factory, instance.Id);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class CompletionEffectFactoryDeferredTests
{
    [Fact]
    public async Task Deferred_definition_produces_intent_with_mode_schedule_and_attempts()
    {
        await using var h = await DeferredFactoryHarness.CreateAsync(
            effectType: "Employee.UpdateField",
            mode: EffectExecutionMode.Deferred,
            maxAttempts: 5,
            configJson: """
            {"field":{"source":"FormField","key":"fieldCode"},
             "newValue":{"source":"FormField","key":"newValue"},
             "__effectiveOn":{"source":"FormField","key":"effectiveOn"}}
            """,
            formValues: new() { ["fieldCode"] = "jobTitle", ["newValue"] = "Manager", ["effectiveOn"] = "2026-09-01" });

        var intents = await h.Factory.BuildAsync(h.RequestInstanceId, default);

        intents.Should().HaveCount(1);
        intents[0].Mode.Should().Be(EffectExecutionMode.Deferred);
        intents[0].MaxAttempts.Should().Be(5);
        intents[0].ScheduledFor!.Value.Date.Should().Be(new DateTime(2026, 9, 1));
    }
}

using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Completion;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Completion;

// ── Harness (local fakes, no collision with the deferred-tests file scope) ─────

file sealed class FakeLeaveServiceMgr : ILeaveService
{
    public LeaveRules GetRules(string? metadataJson) => new();
    public decimal ComputeDays(DateTime start, DateTime end, LeaveRules rules) => 0;
    public Task<List<LeaveTypeInfo>> GetLeaveTypesAsync(Guid employeeId, CancellationToken ct)
        => Task.FromResult(new List<LeaveTypeInfo>());
    public Task<LeavePreview> PreviewAsync(Guid employeeId, Guid leaveTypeId, DateTime? start, DateTime? end,
        bool hasAttachment, CancellationToken ct)
        => Task.FromResult(new LeavePreview());
}

file sealed class FakeUserMgr : ICurrentUserService
{
    public Guid UserId => Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public Guid TenantId => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public string? Email => "actor@hr.local";
    public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
    public bool IsAuthenticated => true;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

public class ManagerContextResolutionTests
{
    // ── Fact 1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolver_returns_manager_values_from_context()
    {
        var managerUserId = Guid.NewGuid();
        const string managerEmail = "manager@hr.local";

        var ctx = new EffectResolutionContext
        {
            Instance = new RequestInstance
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                RequestTypeId = Guid.NewGuid(),
                RequestNumber = "REQ-001",
                EmployeeId = Guid.NewGuid(),
                FormSubmissionId = Guid.NewGuid(),
                Status = RequestStatus.Approved,
                SubmittedAt = DateTime.UtcNow,
            },
            RequestTypeCode = "TEST",
            TenantId = Guid.NewGuid(),
            FormValues = new Dictionary<string, (string? Value, string? FileUrl)>(),
            ManagerUserId = managerUserId,
            ManagerEmail = managerEmail,
        };

        var emailMapping = new EffectValueMapping { Source = EffectValueSource.RequestContext, Key = "managerEmail" };
        var idMapping = new EffectValueMapping { Source = EffectValueSource.RequestContext, Key = "managerUserId" };

        var config = new EffectConfiguration();
        config.Inputs["toEmail"] = emailMapping;
        config.Inputs["toUserId"] = idMapping;

        var payload = EffectValueResolver.Resolve(config, ctx);

        payload["toEmail"].Should().Be(managerEmail);
        payload["toUserId"].Should().Be(managerUserId);
    }

    // ── Fact 2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolver_returns_null_when_manager_absent()
    {
        var ctx = new EffectResolutionContext
        {
            Instance = new RequestInstance
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                RequestTypeId = Guid.NewGuid(),
                RequestNumber = "REQ-002",
                EmployeeId = Guid.NewGuid(),
                FormSubmissionId = Guid.NewGuid(),
                Status = RequestStatus.Approved,
                SubmittedAt = DateTime.UtcNow,
            },
            RequestTypeCode = "TEST",
            TenantId = Guid.NewGuid(),
            FormValues = new Dictionary<string, (string? Value, string? FileUrl)>(),
            // ManagerUserId and ManagerEmail intentionally omitted (null)
        };

        var config = new EffectConfiguration();
        config.Inputs["toEmail"] = new EffectValueMapping { Source = EffectValueSource.RequestContext, Key = "managerEmail" };
        config.Inputs["toUserId"] = new EffectValueMapping { Source = EffectValueSource.RequestContext, Key = "managerUserId" };

        var act = () => EffectValueResolver.Resolve(config, ctx);
        act.Should().NotThrow();

        var payload = EffectValueResolver.Resolve(config, ctx);
        payload["toEmail"].Should().BeNull();
        payload["toUserId"].Should().BeNull();
    }

    // ── Fact 3 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Factory_populates_manager_from_employee_manager()
    {
        var fakeUser = new FakeUserMgr();
        var dbName = $"ManagerCtx_{Guid.NewGuid()}";
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options,
            fakeUser);

        var tenantId = fakeUser.TenantId;
        var managerUserId = Guid.NewGuid();
        const string managerEmail = "mgr@company.local";

        // Seed manager employee
        var managerEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeNumber = "MGR-001",
            FirstName = "Manager",
            LastName = "One",
            Email = managerEmail,
            Gender = Gender.Male,
            DateOfBirth = new DateTime(1980, 1, 1),
            HireDate = new DateTime(2020, 1, 1),
            UserId = managerUserId,
        };
        db.Set<Employee>().Add(managerEmployee);

        // Seed requester employee with ManagerId pointing to managerEmployee
        var requesterEmployee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeNumber = "EMP-001",
            FirstName = "Requester",
            LastName = "One",
            Email = "emp@company.local",
            Gender = Gender.Female,
            DateOfBirth = new DateTime(1990, 1, 1),
            HireDate = new DateTime(2022, 1, 1),
            ManagerId = managerEmployee.Id,
        };
        db.Set<Employee>().Add(requesterEmployee);

        // FormDefinition
        var formDef = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MGR_FORM",
            NameEn = "Manager Test Form",
            NameAr = "نموذج المدير",
            Module = "Platform",
        };
        db.Set<FormDefinition>().Add(formDef);

        // RequestType with one effect that maps toEmail ← RequestContext:managerEmail
        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "MGR_NOTIFY_TEST",
            NameEn = "Manager Notify Test",
            NameAr = "إشعار المدير",
            FormDefinitionId = formDef.Id,
            IsActive = true,
        };
        db.Set<RequestType>().Add(requestType);

        var effectDef = new RequestEffectDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            EffectType = "Notification.Send",
            Trigger = EffectTrigger.FinalApproval,
            ExecutionMode = EffectExecutionMode.Transactional,
            MaxAttempts = 3,
            IsEnabled = true,
            Sequence = 1,
            ConfigurationJson = """{"toEmail":{"source":"RequestContext","key":"managerEmail"}}""",
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

        // RequestInstance for the requester employee
        var instance = new RequestInstance
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTypeId = requestType.Id,
            RequestNumber = "REQ-MGR-001",
            EmployeeId = requesterEmployee.Id,
            FormSubmissionId = submission.Id,
            Status = RequestStatus.Approved,
            SubmittedAt = DateTime.UtcNow,
        };
        db.Set<RequestInstance>().Add(instance);

        await db.SaveChangesAsync();

        var factory = new CompletionEffectFactory(db, new FakeLeaveServiceMgr(), fakeUser);
        var intents = await factory.BuildAsync(instance.Id, default);

        intents.Should().HaveCount(1);
        var payload = System.Text.Json.JsonDocument.Parse(intents[0].Payload).RootElement;
        payload.GetProperty("toEmail").GetString().Should().Be(managerEmail);

        await db.DisposeAsync();
    }

    // ── Fact 4 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ManagerEmail_is_an_allowed_request_context_key()
    {
        RequestContextKeys.All.Should().Contain("managerEmail");
        RequestContextKeys.All.Should().Contain("managerUserId");
    }
}

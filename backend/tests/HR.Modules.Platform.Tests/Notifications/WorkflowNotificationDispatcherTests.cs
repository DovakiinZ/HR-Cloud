using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Modules.Platform.Services.Documents;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

/// <summary>Stub that returns an empty token dictionary — avoids a real DB query for token data
/// that is irrelevant to dispatcher-behaviour tests.</summary>
internal sealed class DocumentTokenResolverStub : IRequestTokenResolver
{
    public Task<IReadOnlyDictionary<string, string>> ResolveForRequestAsync(Guid requestInstanceId, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

public class WorkflowNotificationDispatcherTests
{
    private sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    // Records every NotifyAsync call so we can assert who was notified.
    private sealed class SpyNotifier : INotificationService
    {
        public List<Guid> Notified { get; } = new();
        public Task NotifyAsync(Guid userId, string titleAr, string titleEn, string bodyAr, string bodyEn,
            string category, Guid? entityId, string link, DateTime? dueAt = null, bool email = true, CancellationToken ct = default)
        { Notified.Add(userId); return Task.CompletedTask; }
    }

    // Resolves whatever user ids we program per recipient type; can be told to throw.
    private sealed class ProgrammableResolver : INotificationRecipientResolver
    {
        public Dictionary<NotificationRecipientType, Guid[]> Map { get; } = new();
        public HashSet<NotificationRecipientType> Throws { get; } = new();
        public Task<IReadOnlyList<Guid>> ResolveAsync(RecipientSpec spec, RequestInstance instance, RequestApproval? step, CancellationToken ct)
        {
            if (Throws.Contains(spec.Type)) throw new InvalidOperationException("boom");
            return Task.FromResult<IReadOnlyList<Guid>>(Map.TryGetValue(spec.Type, out var v) ? v : Array.Empty<Guid>());
        }
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"wd_{Guid.NewGuid()}").Options, u);

    private static RequestInstance SeedInstance(ApplicationDbContext db, Guid tenant)
    {
        var emp = new Employee { Id = Guid.NewGuid(), TenantId = tenant, EmployeeNumber = "E1", FirstName = "F",
            LastName = "L", Email = "e@e.e", Gender = Gender.Male, DateOfBirth = new DateTime(1990,1,1),
            HireDate = new DateTime(2020,1,1), UserId = Guid.NewGuid() };
        db.Set<Employee>().Add(emp);
        var inst = new RequestInstance { Id = Guid.NewGuid(), TenantId = tenant, RequestTypeId = Guid.NewGuid(),
            RequestNumber = "REQ-1", EmployeeId = emp.Id, FormSubmissionId = Guid.NewGuid(),
            Status = RequestStatus.InProgress, SubmittedAt = DateTime.UtcNow };
        db.Set<RequestInstance>().Add(inst);
        return inst;
    }

    private static WorkflowNotificationRule Rule(Guid tenant, string? code, WorkflowNotificationEvent evt,
        int? step, params RecipientSpec[] recipients) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, RequestTypeCode = code, Event = evt, StepOrder = step,
        RecipientsJson = RecipientSpecParser.Serialize(recipients),
        SubjectEn = "S", SubjectAr = "س", BodyEn = "B", BodyAr = "ب", IsActive = true,
    };

    private static WorkflowNotificationDispatcher Sut(ApplicationDbContext db, INotificationRecipientResolver resolver, SpyNotifier notifier)
        => new(db, resolver, notifier, new DocumentTokenResolverStub(), NullLogger<WorkflowNotificationDispatcher>.Instance);

    [Fact]
    public async Task Delivers_to_resolved_recipient()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var target = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { target };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(target);
    }

    [Fact]
    public async Task Most_specific_tier_wins()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var typeCode = "LEAVE_REQUEST";
        // load the instance's request type code by faking the lookup: dispatcher reads code via RequestType.
        db.Set<HR.Domain.Engines.Requests.RequestType>().Add(new RequestType { Id = inst.RequestTypeId,
            TenantId = u.TenantId, Code = typeCode, NameEn = "L", NameAr = "ل", FormDefinitionId = Guid.NewGuid(), IsActive = true });
        db.Set<WorkflowNotificationRule>().AddRange(
            Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null, new RecipientSpec(NotificationRecipientType.DirectManager)),
            Rule(u.TenantId, typeCode, WorkflowNotificationEvent.Submitted, null, new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var requester = Guid.NewGuid(); var manager = Guid.NewGuid();
        var resolver = new ProgrammableResolver();
        resolver.Map[NotificationRecipientType.Requester] = new[] { requester };
        resolver.Map[NotificationRecipientType.DirectManager] = new[] { manager };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(requester); // type+event tier beats global tier
    }

    [Fact]
    public async Task Dedups_same_user_across_recipients()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var dup = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester), new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver();
        resolver.Map[NotificationRecipientType.Requester] = new[] { dup };
        resolver.Map[NotificationRecipientType.DirectManager] = new[] { dup };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle().Which.Should().Be(dup);
    }

    [Fact]
    public async Task Duplicate_dispatch_is_a_noop()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var target = Guid.NewGuid();
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { target };
        var spy = new SpyNotifier();
        var sut = Sut(db, resolver, spy);

        await sut.DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);
        await sut.DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().ContainSingle(); // second dispatch skipped by ledger
    }

    [Fact]
    public async Task Unresolved_recipient_is_skipped_not_redirected()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); // DirectManager maps to nothing
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().BeEmpty(); // never falls back to requester
    }

    [Fact]
    public async Task Resolver_exception_never_throws_to_caller()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        db.Set<WorkflowNotificationRule>().Add(Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Throws.Add(NotificationRecipientType.DirectManager);
        var spy = new SpyNotifier();

        var act = async () => await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Tenant_rules_are_isolated()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        // a rule owned by a DIFFERENT tenant must never fire
        db.Set<WorkflowNotificationRule>().Add(Rule(Guid.NewGuid(), null, WorkflowNotificationEvent.Submitted, null,
            new RecipientSpec(NotificationRecipientType.Requester)));
        await db.SaveChangesAsync();
        var resolver = new ProgrammableResolver(); resolver.Map[NotificationRecipientType.Requester] = new[] { Guid.NewGuid() };
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        spy.Notified.Should().BeEmpty();
    }

    /// <summary>
    /// Two rules in the SAME winning tier (both global, event=Submitted, step=null).
    /// Rule A → Requester, Rule B → DirectManager.
    /// Both resolve to the SAME userX.
    /// Expected: only ONE notification (claim-first dedup across rules).
    /// </summary>
    [Fact]
    public async Task Two_rules_same_tier_resolving_same_user_deliver_once()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var inst = SeedInstance(db, u.TenantId);
        var userX = Guid.NewGuid();

        // Both rules: global (no code), Submitted, no step — same tier.
        db.Set<WorkflowNotificationRule>().AddRange(
            Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
                new RecipientSpec(NotificationRecipientType.Requester)),
            Rule(u.TenantId, null, WorkflowNotificationEvent.Submitted, null,
                new RecipientSpec(NotificationRecipientType.DirectManager)));
        await db.SaveChangesAsync();

        var resolver = new ProgrammableResolver();
        resolver.Map[NotificationRecipientType.Requester] = new[] { userX };
        resolver.Map[NotificationRecipientType.DirectManager] = new[] { userX }; // same user!
        var spy = new SpyNotifier();

        await Sut(db, resolver, spy).DispatchAsync(WorkflowNotificationEvent.Submitted, inst, null, default);

        // userX must appear exactly once — not once per matching rule.
        spy.Notified.Should().ContainSingle().Which.Should().Be(userX);
    }
}

using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

/// <summary>
/// TDD for Task 6: SystemWorkflowNotificationRules seed catalog + non-destructive reconcile logic.
///
/// Uses in-memory DB (same pattern as FieldClassificationSeedingTests): the logic under test is the
/// insert/guard/upgrade decision, not the tenant query filter, so in-memory is sufficient and fast.
/// </summary>
public class WorkflowNotificationSeedTests
{
    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid TenantId { get; init; } = Guid.NewGuid();
        public string? Email => "a@b.c";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Db(FakeUser u) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seed_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options, u);

    private static RequestType LeaveType(Guid tenant) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, Code = "LEAVE_REQUEST", NameEn = "Leave", NameAr = "إجازة",
        FormDefinitionId = Guid.NewGuid(), IsActive = true, IsSystem = true, SeedVersion = 3,
    };

    // ── Catalog sanity ─────────────────────────────────────────────────────────

    [Fact]
    public void Seed_catalog_has_five_leave_rules()
        => SystemWorkflowNotificationRules.For("LEAVE_REQUEST").Should().HaveCount(5);

    [Fact]
    public void CurrentSeedVersion_is_four()
        => RequestProvisioningService.CurrentSeedVersion.Should().Be(4);

    // ── Reconcile: insert + idempotency ───────────────────────────────────────

    [Fact]
    public async Task Reconcile_inserts_missing_rules_once()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var type = LeaveType(u.TenantId); db.Set<RequestType>().Add(type); await db.SaveChangesAsync();

        var svc = ProvisioningTestFactory.Create(db, u);
        svc.ReconcileWorkflowNotificationRules(type);
        await db.SaveChangesAsync();

        // Idempotent second pass — must not duplicate any rule.
        svc.ReconcileWorkflowNotificationRules(type);
        await db.SaveChangesAsync();

        db.Set<WorkflowNotificationRule>().Count(r => r.TenantId == u.TenantId).Should().Be(5);
        db.Set<WorkflowNotificationRule>().All(r => r.IsSystemOwned).Should().BeTrue();
    }

    // ── Reconcile: never overwrite a customized rule ───────────────────────────

    [Fact]
    public async Task Reconcile_never_overwrites_a_customized_rule()
    {
        var u = new FakeUser(); await using var db = Db(u);
        var type = LeaveType(u.TenantId); db.Set<RequestType>().Add(type); await db.SaveChangesAsync();
        var svc = ProvisioningTestFactory.Create(db, u);
        svc.ReconcileWorkflowNotificationRules(type); await db.SaveChangesAsync();

        // Tenant edits the "Submitted→Requester" rule and marks it customized.
        var rule = db.Set<WorkflowNotificationRule>()
            .First(r => r.SystemKey == "LEAVE_REQUEST:Submitted:Requester");
        rule.SubjectEn = "TENANT EDIT"; rule.IsCustomized = true; await db.SaveChangesAsync();

        // Second pass — must leave the tenant edit intact.
        svc.ReconcileWorkflowNotificationRules(type); await db.SaveChangesAsync();

        db.Set<WorkflowNotificationRule>()
            .First(r => r.SystemKey == "LEAVE_REQUEST:Submitted:Requester")
            .SubjectEn.Should().Be("TENANT EDIT");
    }
}

/// <summary>
/// Minimal construction helper that mirrors FieldClassificationSeedingTests.BuildHarness and
/// RequestProvisioningTests.Build: wires ApplicationDbContext + RequestSeeder + BackgroundExecutionContext
/// into a RequestProvisioningService, without requiring a real PostgreSQL connection.
/// </summary>
internal static class ProvisioningTestFactory
{
    private sealed class NoopPageTemplateSeeder : HR.Modules.Platform.Services.Documents.IPageTemplateSeeder
    {
        public Task<Guid> SeedAsync(CancellationToken ct) => Task.FromResult(Guid.Empty);
    }

    private sealed class NoopDocumentLibrarySeeder : HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder
    {
        public Task SeedAsync(Guid defaultPageTemplateId, CancellationToken ct) => Task.CompletedTask;
    }

    public static RequestProvisioningService Create(ApplicationDbContext db, ICurrentUserService user)
    {
        var bg = new BackgroundExecutionContext();
        // Activate a background scope so the ambient tenant matches the FakeUser.
        _ = bg.Begin(user.TenantId, user.UserId, email: user.Email, correlationId: Guid.NewGuid());
        var seeder = new RequestSeeder(db, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        return new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
    }
}

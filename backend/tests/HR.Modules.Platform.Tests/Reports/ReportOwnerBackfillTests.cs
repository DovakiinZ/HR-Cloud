using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.MappingProfiles;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// Ownership backfill for legacy reports. Requires REPORTS_TEST_DB; skips without it.
///
/// Context: CanEdit is "owner OR a share granting edit", so a report written before OwnerId was
/// recorded is editable by nobody. The backfill restores the owner the row already implies via
/// CreatedBy — and refuses to invent one when it cannot, because a wrong owner quietly hands
/// someone else's report away.
/// </summary>
public class ReportOwnerBackfillTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid userId, Guid tenantId, string email = "caller@test.example.com")
        { UserId = userId; TenantId = tenantId; Email = email; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email { get; }
        public IReadOnlyList<string> Permissions { get; } = new[] { "Platform.Reports.Edit" };
        public bool IsAuthenticated => true;
    }

    private static AutoMapper.IMapper Mapper() =>
        new AutoMapper.MapperConfiguration(c => c.AddProfile<PlatformMappingProfile>()).CreateMapper();

    private static ApplicationDbContext Ctx(ICurrentUserService u) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options, u);

    private static User NewUser(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Email = email,
        FullName = email, PasswordHash = "x", IsActive = true,
    };

    /// <param name="createdBy">The email to end up in CreatedBy, or null for "no evidence".</param>
    private static ReportDefinition NewReport(Guid tenantId, string code, string? createdBy, Guid? ownerId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, Code = code,
        NameEn = code, NameAr = code,
        PrimaryObjectId = Guid.NewGuid(), Scope = ReportScope.Company,
        OwnerId = ownerId, CreatedBy = createdBy, IsActive = true,
    };

    /// <summary>
    /// Persists reports with the CreatedBy the fixture asked for.
    ///
    /// ApplicationDbContext.SaveChangesAsync overwrites CreatedBy with the caller's email on every
    /// insert, unconditionally, so setting it at Add time is silently discarded. Its Modified branch
    /// touches only UpdatedAt/UpdatedBy, so a second save puts the intended value back — including a
    /// deliberate null, which is exactly the legacy shape under test.
    /// </summary>
    private static async Task SaveWithCreatedByAsync(ApplicationDbContext db, params ReportDefinition[] reports)
    {
        var wanted = reports.ToDictionary(r => r.Id, r => r.CreatedBy);
        db.Set<ReportDefinition>().AddRange(reports);
        await db.SaveChangesAsync();
        foreach (var r in reports) r.CreatedBy = wanted[r.Id];
        await db.SaveChangesAsync();
    }

    // ── 1. Legacy custom report with a valid creator ──────────────────────────

    [SkippableFact]
    public async Task Custom_report_with_a_resolvable_creator_gets_that_creator_as_owner()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();

        var creator = NewUser(tenantId, "creator@sanad.test");
        db.Set<User>().Add(creator);
        var report = NewReport(tenantId, "CUSTOM_OK", "creator@sanad.test");
        await db.SaveChangesAsync();
        await SaveWithCreatedByAsync(db, report);

        var result = await new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantId)).RunAsync(dryRun: false, default);

        result.Assigned.Should().ContainSingle();
        result.Assigned[0].ReportId.Should().Be(report.Id);
        result.Assigned[0].OwnerId.Should().Be(creator.Id);
        result.Unresolved.Should().BeEmpty();

        var stored = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == report.Id);
        stored.OwnerId.Should().Be(creator.Id);

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task The_backfill_is_a_no_op_the_second_time_and_never_overwrites_an_owner()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();

        var creator = NewUser(tenantId, "creator@sanad.test");
        var existingOwner = NewUser(tenantId, "someone.else@sanad.test");
        db.Set<User>().AddRange(creator, existingOwner);
        await db.SaveChangesAsync();
        // Already owned, and by somebody other than its creator: must be left exactly as it is.
        var owned = NewReport(tenantId, "CUSTOM_OWNED", "creator@sanad.test", ownerId: existingOwner.Id);
        await SaveWithCreatedByAsync(db, NewReport(tenantId, "CUSTOM_OK", "creator@sanad.test"), owned);

        var backfill = new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantId));
        var first = await backfill.RunAsync(dryRun: false, default);
        var second = await backfill.RunAsync(dryRun: false, default);

        first.Assigned.Should().ContainSingle(because: "only the ownerless report is a candidate");
        second.Assigned.Should().BeEmpty(because: "the first pass already assigned it");
        second.ScannedOwnerless.Should().Be(0);

        var stillOwned = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == owned.Id);
        stillOwned.OwnerId.Should().Be(existingOwner.Id, because: "an existing owner is never rewritten");

        await tx.RollbackAsync();
    }

    // ── 2. Legacy custom report with a missing creator ────────────────────────

    [SkippableFact]
    public async Task Custom_report_without_a_resolvable_creator_is_left_alone_and_reported()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();

        var noCreatedBy = NewReport(tenantId, "CUSTOM_NULL", null);
        var goneCreator = NewReport(tenantId, "CUSTOM_GONE", "departed@sanad.test");
        await SaveWithCreatedByAsync(db, noCreatedBy, goneCreator);

        var result = await new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantId)).RunAsync(dryRun: false, default);

        result.Assigned.Should().BeEmpty();
        result.Unresolved.Should().HaveCount(2);
        result.Unresolved.Should().Contain(u => u.ReportId == noCreatedBy.Id && u.Reason == BackfillSkipReason.NoCreatedBy);
        result.Unresolved.Should().Contain(u => u.ReportId == goneCreator.Id && u.Reason == BackfillSkipReason.CreatorNotFound);

        var stored = await db.Set<ReportDefinition>().AsNoTracking()
            .Where(r => r.Id == noCreatedBy.Id || r.Id == goneCreator.Id).ToListAsync();
        stored.Should().OnlyContain(r => r.OwnerId == null, because: "no owner is ever invented");

        await tx.RollbackAsync();
    }

    // ── 3. System reports stay ownerless and non-editable ─────────────────────

    [SkippableFact]
    public async Task System_report_is_left_ownerless_by_the_backfill()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();

        var creator = NewUser(tenantId, "creator@sanad.test");
        db.Set<User>().Add(creator);
        // Resolvable creator on purpose: being resolvable must not be enough to claim a system report.
        var sys = NewReport(tenantId, "SYS_ATTENDANCE", "creator@sanad.test");
        await db.SaveChangesAsync();
        await SaveWithCreatedByAsync(db, sys);

        var result = await new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantId)).RunAsync(dryRun: false, default);

        result.Assigned.Should().BeEmpty();
        result.SystemManaged.Should().ContainSingle()
            .Which.Reason.Should().Be(BackfillSkipReason.SystemManaged);

        var stored = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == sys.Id);
        stored.OwnerId.Should().BeNull();

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task System_report_is_not_editable_even_by_an_owner_or_an_edit_share()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenantId);
        await using var db = Ctx(user);
        await using var tx = await db.Database.BeginTransactionAsync();

        // The hostile shape: a system report that somehow *does* carry this user as owner and an
        // edit share. Both would grant edit on a custom report; neither may on a system one.
        var sys = NewReport(tenantId, "SYS_PAYROLL", "creator@sanad.test", ownerId: userId);
        sys.Shares.Add(new ReportShare
        {
            Id = Guid.NewGuid(), ReportDefinitionId = sys.Id, SharedWithUserId = userId, CanEdit = true,
        });
        db.Set<ReportDefinition>().Add(sys);
        await db.SaveChangesAsync();

        var access = new ReportAccessService(db, user);

        // Readable...
        await access.Invoking(a => a.EnsureCanReadAsync(sys.Id, default)).Should().NotThrowAsync();
        // ...but not editable, and not mutable through the command handlers either.
        await access.Invoking(a => a.EnsureCanEditAsync(sys.Id, default)).Should().ThrowAsync<ForbiddenException>();

        var addField = () => new AddReportFieldCommandHandler(db, Mapper(), access).Handle(new AddReportFieldCommand
        {
            ReportDefinitionId = sys.Id, FieldType = ReportFieldType.ObjectField,
            FieldCode = "X", DisplayNameEn = "X", DisplayNameAr = "X",
        }, default);
        var update = () => new UpdateReportCommandHandler(db, Mapper(), access)
            .Handle(new UpdateReportCommand { Id = sys.Id, NameEn = "Hijacked", NameAr = "مُختطف" }, default);

        await addField.Should().ThrowAsync<ForbiddenException>();
        await update.Should().ThrowAsync<ForbiddenException>();

        await tx.RollbackAsync();
    }

    // ── 4. Clone is the supported path off a system report ────────────────────

    [SkippableFact]
    public async Task Cloning_a_system_report_yields_an_editable_copy_owned_by_the_caller()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenantId);
        await using var db = Ctx(user);
        await using var tx = await db.Database.BeginTransactionAsync();

        var sys = NewReport(tenantId, "SYS_LEAVES", "creator@sanad.test");
        sys.Fields.Add(new ReportField
        {
            Id = Guid.NewGuid(), ReportDefinitionId = sys.Id, FieldType = ReportFieldType.ObjectField,
            FieldCode = "EmployeeId", DisplayNameEn = "Employee", DisplayNameAr = "الموظف", SortOrder = 0, IsVisible = true,
        });
        db.Set<ReportDefinition>().Add(sys);
        await db.SaveChangesAsync();

        var access = new ReportAccessService(db, user);
        var clone = await new CloneReportCommandHandler(db, Mapper(), access, user).Handle(new CloneReportCommand
        {
            SourceReportId = sys.Id, NewCode = "MY_LEAVES", NameEn = "My Leaves", NameAr = "إجازاتي",
        }, default);

        clone.Id.Should().NotBe(sys.Id);
        var stored = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == clone.Id);
        stored.OwnerId.Should().Be(userId, because: "the copy belongs to whoever made it");
        stored.Code.Should().Be("MY_LEAVES");
        ReportSystemPolicy.IsSystemManaged(stored.Code).Should().BeFalse();

        // And the copy really is editable, which is the whole point of clone-only.
        await access.Invoking(a => a.EnsureCanEditAsync(clone.Id, default)).Should().NotThrowAsync();
        (await db.Set<ReportField>().CountAsync(f => f.ReportDefinitionId == clone.Id))
            .Should().Be(1, because: "the source's fields come across");

        await tx.RollbackAsync();
    }

    // ── 5. Cross-tenant users can never become owners ─────────────────────────

    [SkippableFact]
    public async Task A_creator_email_matching_only_another_tenants_user_does_not_assign_ownership()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantA));
        await using var tx = await db.Database.BeginTransactionAsync();

        // Same email address, but the only account holding it lives in tenant B.
        const string shared = "shared@sanad.test";
        var report = NewReport(tenantA, "CUSTOM_XT", shared);
        await SaveWithCreatedByAsync(db, report);

        // Tenant B's user is written through a context scoped to tenant B, on the same transaction.
        var userB = new StubUser(Guid.NewGuid(), tenantB);
        await using var dbB = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options, userB);
        await dbB.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction());
        var foreigner = NewUser(tenantB, shared);
        dbB.Set<User>().Add(foreigner);
        await dbB.SaveChangesAsync();

        var result = await new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantA)).RunAsync(dryRun: false, default);

        result.Assigned.Should().BeEmpty(because: "the only match is outside this tenant");
        result.Unresolved.Should().ContainSingle()
            .Which.Reason.Should().Be(BackfillSkipReason.CreatorNotFound);

        var stored = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == report.Id);
        stored.OwnerId.Should().BeNull();
        stored.OwnerId.Should().NotBe(foreigner.Id);

        await tx.RollbackAsync();
    }

    // ── Dry run ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Dry_run_reports_the_same_plan_but_writes_nothing()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        await using var db = Ctx(new StubUser(Guid.NewGuid(), tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();

        var creator = NewUser(tenantId, "creator@sanad.test");
        db.Set<User>().Add(creator);
        var willAssign = NewReport(tenantId, "CUSTOM_OK", "creator@sanad.test");
        var willNot = NewReport(tenantId, "CUSTOM_GONE", "departed@sanad.test");
        var sys = NewReport(tenantId, "SYS_EMPLOYEES", "creator@sanad.test");
        await db.SaveChangesAsync();
        await SaveWithCreatedByAsync(db, willAssign, willNot, sys);

        var backfill = new ReportOwnerBackfill(db, new StubUser(Guid.NewGuid(), tenantId));
        var dry = await backfill.RunAsync(dryRun: true, default);

        dry.DryRun.Should().BeTrue();
        dry.ScannedOwnerless.Should().Be(3);
        dry.Assigned.Should().ContainSingle().Which.ReportId.Should().Be(willAssign.Id);
        dry.SystemManaged.Should().ContainSingle().Which.ReportId.Should().Be(sys.Id);
        dry.Unresolved.Should().ContainSingle().Which.ReportId.Should().Be(willNot.Id);

        var afterDry = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == willAssign.Id);
        afterDry.OwnerId.Should().BeNull(because: "a dry run must not write");

        // And the real run then does exactly what the dry run advertised.
        var wet = await backfill.RunAsync(dryRun: false, default);
        wet.Assigned.Select(a => a.ReportId).Should().BeEquivalentTo(dry.Assigned.Select(a => a.ReportId));
        (await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == willAssign.Id))
            .OwnerId.Should().Be(creator.Id);

        await tx.RollbackAsync();
    }
}

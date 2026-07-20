using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.MappingProfiles;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// Authorization on a report's child entities. Requires REPORTS_TEST_DB; skips without it.
///
/// The gap these cover: every handler in ReportCommands.cs mutated its target with no per-report
/// check at all. The controller's [RequirePermission("Platform.Reports.Edit")] is a tenant-wide
/// capability, not authorization for a *particular* report — so anyone holding it could add fields
/// to, reorder, or delete another user's Personal report, one they are not even allowed to read.
/// Edit access was strictly broader than read access.
/// </summary>
public class ReportFieldAuthorizationTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid userId, Guid tenantId) { UserId = userId; TenantId = tenantId; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email => "stub@test.example.com";
        public IReadOnlyList<string> Permissions { get; } = new[] { "Platform.Reports.Edit" };
        public bool IsAuthenticated => true;
    }

    private static AutoMapper.IMapper Mapper() =>
        new AutoMapper.MapperConfiguration(c => c.AddProfile<PlatformMappingProfile>()).CreateMapper();

    private sealed record World(Guid ReportId, Guid OwnerId, Guid EditorId, Guid ViewerId);

    private static ApplicationDbContext Ctx(string conn, ICurrentUserService u) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(conn).Options, u);

    /// <summary>
    /// One Personal report owned by `owner`, shared for edit with `editor` and read-only with
    /// `viewer`. A fourth user sits in a different tenant entirely.
    /// </summary>
    private static async Task<World> SeedAsync(ApplicationDbContext db, Guid tenantId, Guid ownerId)
    {
        var editorId = Guid.NewGuid();
        var viewerId = Guid.NewGuid();

        var report = new ReportDefinition
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Code = "AUTH_" + Guid.NewGuid().ToString("N")[..8],
            NameEn = "Auth Fixture", NameAr = "تجربة الصلاحيات",
            PrimaryObjectId = Guid.NewGuid(),
            Scope = ReportScope.Personal,       // not readable by non-owners except via a share
            OwnerId = ownerId,
            IsActive = true,
        };
        report.Fields.Add(new ReportField
        {
            Id = Guid.NewGuid(), ReportDefinitionId = report.Id, FieldType = ReportFieldType.ObjectField,
            FieldCode = "Seeded", DisplayNameEn = "Seeded", DisplayNameAr = "حقل", SortOrder = 0, IsVisible = true,
        });
        report.Shares.Add(new ReportShare
        {
            Id = Guid.NewGuid(), ReportDefinitionId = report.Id,
            SharedWithUserId = editorId, CanEdit = true,
        });
        report.Shares.Add(new ReportShare
        {
            Id = Guid.NewGuid(), ReportDefinitionId = report.Id,
            SharedWithUserId = viewerId, CanEdit = false,
        });
        db.Set<ReportDefinition>().Add(report);
        await db.SaveChangesAsync();

        return new World(report.Id, ownerId, editorId, viewerId);
    }

    private static AddReportFieldCommandHandler AddHandler(ApplicationDbContext db, ICurrentUserService u) =>
        new(db, Mapper(), new ReportAccessService(db, u));

    // ── Owner ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Owner_can_add_a_field()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var owner = new StubUser(ownerId, tenantId);
        await using var db = Ctx(Conn!, owner);
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        var dto = await AddHandler(db, owner).Handle(NewFieldCommand(w.ReportId, "OwnerAdded"), default);

        dto.Should().NotBeNull();
        (await db.Set<ReportField>().CountAsync(f => f.ReportDefinitionId == w.ReportId)).Should().Be(2);

        await tx.RollbackAsync();
    }

    // ── Editor (share with CanEdit) ───────────────────────────────────────────

    [SkippableFact]
    public async Task Editor_with_an_edit_share_can_add_a_field()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        var editor = new StubUser(w.EditorId, tenantId);
        var dto = await AddHandler(db, editor).Handle(NewFieldCommand(w.ReportId, "EditorAdded"), default);

        dto.Should().NotBeNull();
        await tx.RollbackAsync();
    }

    // ── Viewer (read-only share) ──────────────────────────────────────────────

    [SkippableFact]
    public async Task Viewer_with_a_read_only_share_cannot_add_a_field()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        var viewer = new StubUser(w.ViewerId, tenantId);
        var act = () => AddHandler(db, viewer).Handle(NewFieldCommand(w.ReportId, "ViewerAdded"), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await db.Set<ReportField>().CountAsync(f => f.ReportDefinitionId == w.ReportId)).Should().Be(1,
            because: "the read-only share must not permit a write");

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Viewer_cannot_delete_a_field_via_its_child_id()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);
        var fieldId = await db.Set<ReportField>().Where(f => f.ReportDefinitionId == w.ReportId)
            .Select(f => f.Id).FirstAsync();

        var viewer = new StubUser(w.ViewerId, tenantId);
        var handler = new DeleteReportFieldCommandHandler(db, new ReportAccessService(db, viewer));
        var act = () => handler.Handle(new DeleteReportFieldCommand(fieldId), default);

        // The delete command carries only the field's id; the parent's permissions must still apply.
        await act.Should().ThrowAsync<ForbiddenException>();
        (await db.Set<ReportField>().AnyAsync(f => f.Id == fieldId)).Should().BeTrue();

        await tx.RollbackAsync();
    }

    // ── A user in another tenant ──────────────────────────────────────────────

    [SkippableFact]
    public async Task A_user_from_another_tenant_cannot_touch_the_report()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        // Same connection and transaction, but a different tenant on the user service: the global
        // query filter must hide the report entirely, so it is Not Found rather than Forbidden.
        await using var foreignDb = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options,
            new StubUser(Guid.NewGuid(), Guid.NewGuid()));
        await foreignDb.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction());

        var foreignUser = new StubUser(Guid.NewGuid(), Guid.NewGuid());
        var act = () => AddHandler(foreignDb, foreignUser).Handle(NewFieldCommand(w.ReportId, "Foreign"), default);

        await act.Should().ThrowAsync<NotFoundException>();
        (await db.Set<ReportField>().CountAsync(f => f.ReportDefinitionId == w.ReportId)).Should().Be(1);

        await tx.RollbackAsync();
    }

    // ── The other child collections are guarded the same way ──────────────────

    [SkippableFact]
    public async Task Viewer_cannot_add_filters_groupings_or_sortings()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        var viewer = new StubUser(w.ViewerId, tenantId);
        var access = new ReportAccessService(db, viewer);
        var mapper = Mapper();

        var addFilter = () => new AddReportFilterCommandHandler(db, mapper, access)
            .Handle(new AddReportFilterCommand { ReportDefinitionId = w.ReportId, FieldCode = "X", Operator = ReportFilterOperator.Equals }, default);
        var addGrouping = () => new AddReportGroupingCommandHandler(db, mapper, access)
            .Handle(new AddReportGroupingCommand { ReportDefinitionId = w.ReportId, FieldCode = "X" }, default);
        var addSorting = () => new AddReportSortingCommandHandler(db, mapper, access)
            .Handle(new AddReportSortingCommand { ReportDefinitionId = w.ReportId, FieldCode = "X", Direction = SortDirection.Ascending }, default);

        await addFilter.Should().ThrowAsync<ForbiddenException>();
        await addGrouping.Should().ThrowAsync<ForbiddenException>();
        await addSorting.Should().ThrowAsync<ForbiddenException>();

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Viewer_cannot_update_publish_or_delete_the_report()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using var db = Ctx(Conn!, new StubUser(ownerId, tenantId));
        await using var tx = await db.Database.BeginTransactionAsync();
        var w = await SeedAsync(db, tenantId, ownerId);

        var viewer = new StubUser(w.ViewerId, tenantId);
        var access = new ReportAccessService(db, viewer);
        var mapper = Mapper();

        var update = () => new UpdateReportCommandHandler(db, mapper, access)
            .Handle(new UpdateReportCommand { Id = w.ReportId, NameEn = "Hijacked", NameAr = "مُختطف" }, default);
        var publish = () => new PublishReportCommandHandler(db, mapper, access)
            .Handle(new PublishReportCommand(w.ReportId), default);
        var delete = () => new DeleteReportCommandHandler(db, access)
            .Handle(new DeleteReportCommand(w.ReportId), default);

        await update.Should().ThrowAsync<ForbiddenException>();
        await publish.Should().ThrowAsync<ForbiddenException>();
        await delete.Should().ThrowAsync<ForbiddenException>();

        var still = await db.Set<ReportDefinition>().AsNoTracking().FirstAsync(r => r.Id == w.ReportId);
        still.NameEn.Should().Be("Auth Fixture");
        still.IsDeleted.Should().BeFalse();

        await tx.RollbackAsync();
    }

    private static AddReportFieldCommand NewFieldCommand(Guid reportId, string code) => new()
    {
        ReportDefinitionId = reportId,
        FieldType = ReportFieldType.ObjectField,
        FieldCode = code,
        DisplayNameEn = code,
        DisplayNameAr = code,
        SortOrder = 1,
    };
}

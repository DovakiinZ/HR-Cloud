using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.MappingProfiles;
using HR.Modules.Platform.Queries.Reports;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

/// <summary>
/// Report list search. Requires REPORTS_TEST_DB; skips without it.
///
/// Before: `NameEn.Contains(s) || NameAr.Contains(s)` translated to a case-SENSITIVE LIKE and never
/// looked at Code — so "attendance" found nothing while "Attendance" found the report, and no code
/// like DEMO_001 was ever findable. Now ILIKE across NameEn, NameAr and Code.
/// </summary>
public class ReportSearchTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid userId, Guid tenantId) { UserId = userId; TenantId = tenantId; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email => "search@test.example.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private sealed record Fixture(ApplicationDbContext Db, GetReportsQueryHandler Handler, Guid TenantId, Guid UserId);

    private static async Task<Fixture> SeedAsync(string conn)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenantId);
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(conn).Options, user);

        // Company scope so they are visible without shares; the visibility rule is covered elsewhere.
        void Add(string code, string en, string ar) => db.Set<ReportDefinition>().Add(new ReportDefinition
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code, NameEn = en, NameAr = ar,
            PrimaryObjectId = Guid.NewGuid(), Scope = ReportScope.Company, OwnerId = userId, IsActive = true,
        });

        Add("DEMO_001", "Daily Attendance Report", "تقرير الحضور اليومي");
        Add("DEMO_002", "Absence Report", "تقرير الغياب");
        Add("SYS_PAYROLL", "Payroll Register", "سجل الرواتب");
        Add("MISC_X1", "Employee Directory", "دليل الموظفين");

        return new Fixture(db, new GetReportsQueryHandler(db, Mapper(), new ReportAccessService(db, user), user), tenantId, userId);
    }

    private static AutoMapper.IMapper Mapper() =>
        new AutoMapper.MapperConfiguration(c => c.AddProfile<PlatformMappingProfile>()).CreateMapper();

    private static Task<HR.Application.Common.Models.PaginatedList<HR.Modules.Platform.DTOs.Reports.ReportDefinitionDto>>
        Search(GetReportsQueryHandler h, string? term, int pageSize = 50)
        => h.Handle(new GetReportsQuery { Search = term, PageNumber = 1, PageSize = pageSize }, default);

    // ── Case-insensitivity: the reported symptom ──────────────────────────────

    [SkippableTheory]
    [InlineData("Attendance")]
    [InlineData("attendance")]
    [InlineData("ATTENDANCE")]
    [InlineData("aTtEnDaNcE")]
    public async Task English_search_is_case_insensitive(string term)
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        var res = await Search(f.Handler, term);

        res.Items.Should().ContainSingle(because: $"'{term}' must find 'Daily Attendance Report' regardless of casing");
        res.Items[0].Code.Should().Be("DEMO_001");

        await tx.RollbackAsync();
    }

    // ── Arabic ────────────────────────────────────────────────────────────────

    [SkippableTheory]
    [InlineData("الغياب", "DEMO_002")]
    [InlineData("الحضور", "DEMO_001")]
    [InlineData("الرواتب", "SYS_PAYROLL")]
    public async Task Arabic_names_are_searchable(string term, string expectedCode)
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        var res = await Search(f.Handler, term);

        res.Items.Should().ContainSingle();
        res.Items[0].Code.Should().Be(expectedCode);

        await tx.RollbackAsync();
    }

    // ── Code ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Code_is_searchable_and_underscore_is_literal()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        // A code that would be matched by an unescaped "DEMO_001" pattern, since "_" is LIKE's
        // single-character wildcard.
        db.Set<ReportDefinition>().Add(new ReportDefinition
        {
            Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "DEMOX001", NameEn = "Decoy", NameAr = "شرك",
            PrimaryObjectId = Guid.NewGuid(), Scope = ReportScope.Company, OwnerId = f.UserId, IsActive = true,
        });
        await db.SaveChangesAsync();

        var exact = await Search(f.Handler, "DEMO_001");
        exact.Items.Should().ContainSingle(because: "the underscore must be a literal, not a wildcard");
        exact.Items[0].Code.Should().Be("DEMO_001");

        var lower = await Search(f.Handler, "demo_001");
        lower.Items.Should().ContainSingle(because: "code search is case-insensitive too");

        var prefix = await Search(f.Handler, "DEMO");
        prefix.Items.Should().HaveCount(3, because: "DEMO_001, DEMO_002 and the DEMOX001 decoy all contain 'DEMO'");

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Percent_in_a_term_does_not_match_everything()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        var res = await Search(f.Handler, "%");

        res.Items.Should().BeEmpty(because: "'%' is escaped to a literal, so it matches no name or code here");

        await tx.RollbackAsync();
    }

    // ── Tenant isolation + pagination survive the new predicate ───────────────

    [SkippableFact]
    public async Task Search_does_not_cross_tenants()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        // Same connection and transaction, a different tenant on the user service.
        var otherTenant = Guid.NewGuid();
        var otherUser = new StubUser(Guid.NewGuid(), otherTenant);
        await using var otherDb = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options, otherUser);
        await otherDb.Database.UseTransactionAsync(db.Database.CurrentTransaction!.GetDbTransaction());

        var otherHandler = new GetReportsQueryHandler(otherDb, Mapper(), new ReportAccessService(otherDb, otherUser), otherUser);
        var res = await Search(otherHandler, "Attendance");

        res.Items.Should().BeEmpty(because: "tenant B seeded nothing and must not see tenant A's reports");
        res.TotalCount.Should().Be(0);

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Search_is_paginated_and_reports_a_full_total()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        // "report" appears in exactly two of the four English names — "Daily Attendance Report" and
        // "Absence Report". "Payroll Register" and "Employee Directory" do not contain it.
        var page1 = await f.Handler.Handle(new GetReportsQuery { Search = "report", PageNumber = 1, PageSize = 1 }, default);
        var page2 = await f.Handler.Handle(new GetReportsQuery { Search = "report", PageNumber = 2, PageSize = 1 }, default);

        page1.TotalCount.Should().Be(2, because: "TotalCount counts all matches, not just the page");
        page1.Items.Should().HaveCount(1);
        page2.Items.Should().HaveCount(1);
        page1.Items.Select(i => i.Code).Should().NotIntersectWith(page2.Items.Select(i => i.Code));

        await tx.RollbackAsync();
    }

    [SkippableFact]
    public async Task Blank_search_returns_everything()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var f = await SeedAsync(Conn!);
        await using var db = f.Db;
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();

        (await Search(f.Handler, null)).TotalCount.Should().Be(4);
        (await Search(f.Handler, "   ")).TotalCount.Should().Be(4, because: "a whitespace-only term is not a filter");

        await tx.RollbackAsync();
    }
}

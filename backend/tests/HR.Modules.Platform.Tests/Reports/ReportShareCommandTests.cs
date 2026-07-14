using System;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.MappingProfiles;
using HR.Modules.Platform.Queries.Reports;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportShareCommandTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid u, Guid t) { UserId = u; TenantId = t; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email => "t@e.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    [SkippableFact]
    public async Task Owner_can_add_and_list_a_user_share()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid(); var owner = Guid.NewGuid();
        var user = new StubUser(owner, tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();

        var report = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code = "S" + Guid.NewGuid().ToString("N")[..6], NameEn = "r", NameAr = "ر", OwnerId = owner, Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
        db.Set<ReportDefinition>().Add(report); await db.SaveChangesAsync();

        var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<PlatformMappingProfile>()).CreateMapper();
        var access = new ReportAccessService(db, user);
        var shareWith = Guid.NewGuid();

        var added = await new AddReportShareCommandHandler(db, mapper, access)
            .Handle(new AddReportShareCommand(report.Id, shareWith, null, null, true), default);
        added.SharedWithUserId.Should().Be(shareWith);

        var list = await new GetReportSharesQueryHandler(db, mapper, access)
            .Handle(new GetReportSharesQuery(report.Id), default);
        list.Should().ContainSingle(s => s.SharedWithUserId == shareWith && s.CanEdit);

        await tx.RollbackAsync();
    }
}

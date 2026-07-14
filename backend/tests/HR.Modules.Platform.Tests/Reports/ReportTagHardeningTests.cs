using System;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportTagHardeningTests
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
    public async Task Create_tag_rejects_duplicate_name()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid(); var user = new StubUser(Guid.NewGuid(), tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper();
        var name = "Q" + Guid.NewGuid().ToString("N")[..6];
        await new CreateReportTagCommandHandler(db, mapper).Handle(new CreateReportTagCommand(name, null), default);
        var act = async () => await new CreateReportTagCommandHandler(db, mapper).Handle(new CreateReportTagCommand(name, null), default);
        await act.Should().ThrowAsync<HR.Application.Common.Exceptions.ValidationException>();
        await tx.RollbackAsync();
    }
}

using System;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.MappingProfiles;
using HR.Modules.Platform.Queries.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportScheduleQueryTests
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
    public async Task Returns_schedules_for_report()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid(); var user = new StubUser(Guid.NewGuid(), tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();
        var reportId = Guid.NewGuid();
        db.Set<HR.Domain.Engines.Reports.ReportSchedule>().Add(new() { ReportDefinitionId = reportId, Frequency = HR.Domain.Enums.ReportScheduleFrequency.Daily, ExportFormat = HR.Domain.Enums.ExportFormat.Csv, Recipients = "[\"a@b.com\"]", IsActive = true });
        await db.SaveChangesAsync();
        var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper();
        var res = await new GetReportSchedulesQueryHandler(db, mapper).Handle(new GetReportSchedulesQuery(reportId), default);
        res.Should().ContainSingle();
        await tx.RollbackAsync();
    }
}

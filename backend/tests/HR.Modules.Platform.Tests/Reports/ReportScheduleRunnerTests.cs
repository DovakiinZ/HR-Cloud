using System;
using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ScheduleMathTests
{
    private static readonly DateTime Base = new(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ReportScheduleFrequency.Daily, 1)]
    [InlineData(ReportScheduleFrequency.Weekly, 7)]
    public void ComputeNextRun_adds_days(ReportScheduleFrequency f, int days)
        => ScheduleMath.ComputeNextRun(f, Base).Should().Be(Base.AddDays(days));

    [Fact]
    public void ComputeNextRun_monthly_adds_month()
        => ScheduleMath.ComputeNextRun(ReportScheduleFrequency.Monthly, Base).Should().Be(Base.AddMonths(1));

    [Fact]
    public void ComputeNextRun_quarterly_adds_three_months()
        => ScheduleMath.ComputeNextRun(ReportScheduleFrequency.Quarterly, Base).Should().Be(Base.AddMonths(3));

    [Fact]
    public void ParseEmails_extracts_only_addresses()
    {
        var emails = ScheduleMath.ParseEmails("[\"a@b.com\", \"not-an-email\", \"c@d.com\"]");
        emails.Should().BeEquivalentTo(new[] { "a@b.com", "c@d.com" });
    }

    [Fact]
    public void ParseEmails_tolerates_garbage()
        => ScheduleMath.ParseEmails("not json").Should().BeEmpty();
}

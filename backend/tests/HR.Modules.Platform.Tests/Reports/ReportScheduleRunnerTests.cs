using System;
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ScheduleMathTests
{
    // Base is 2026-07-16 08:00 UTC = 11:00 Riyadh.
    private static readonly DateTime Base = new(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc);

    private static ReportSchedule Sched(ReportScheduleFrequency f, int? tod = null, int? dow = null, int? dom = null)
        => new() { Frequency = f, TimeOfDayMinutes = tod, DayOfWeek = dow, DayOfMonth = dom };

    [Fact]
    public void Daily_next_is_tomorrow_at_configured_local_time()
    {
        // 06:00 Riyadh = 03:00 UTC. From 11:00 Riyadh, next 06:00 is tomorrow 03:00 UTC.
        var next = ScheduleMath.ComputeNextRun(Sched(ReportScheduleFrequency.Daily, tod: 360), Base);
        next.Should().Be(new DateTime(2026, 7, 17, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Daily_later_today_local_stays_today()
    {
        // 20:00 Riyadh = 17:00 UTC, still ahead of 11:00 Riyadh now.
        var next = ScheduleMath.ComputeNextRun(Sched(ReportScheduleFrequency.Daily, tod: 1200), Base);
        next.Should().Be(new DateTime(2026, 7, 16, 17, 0, 0, DateTimeKind.Utc));
    }

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

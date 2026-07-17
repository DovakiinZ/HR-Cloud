using System;
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class ScheduleMathTimeOfDayTests
{
    // 2026-07-16 is a Thursday. 05:00 UTC = 08:00 Riyadh.
    private static readonly DateTime From = new(2026, 7, 16, 5, 0, 0, DateTimeKind.Utc);
    private static ReportSchedule S(ReportScheduleFrequency f, int? tod = null, int? dow = null, int? dom = null)
        => new() { Frequency = f, TimeOfDayMinutes = tod, DayOfWeek = dow, DayOfMonth = dom };

    [Fact]
    public void Weekly_picks_next_named_weekday_at_time()
    {
        // Want Sunday(0) 09:00 Riyadh = 06:00 UTC. From Thu 08:00 Riyadh → Sun 2026-07-19 06:00 UTC.
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Weekly, tod: 540, dow: 0), From);
        next.Should().Be(new DateTime(2026, 7, 19, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Monthly_this_month_if_day_still_future()
    {
        // Day 25 at 08:00 Riyadh (05:00 UTC), from the 16th → same month.
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 25), From);
        next.Should().Be(new DateTime(2026, 7, 25, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Monthly_rolls_to_next_month_when_day_passed()
    {
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 5), From);
        next.Should().Be(new DateTime(2026, 8, 5, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DayOfMonth_clamped_to_short_month()
    {
        // Day 31 requested; from Feb should clamp to 28 (2027 not leap). Use a Feb "from".
        var feb = new DateTime(2027, 2, 3, 5, 0, 0, DateTimeKind.Utc);
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 31), feb);
        next.Should().Be(new DateTime(2027, 2, 28, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Quarterly_adds_three_months()
    {
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Quarterly, tod: 480, dom: 5), From);
        next.Should().Be(new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc));
    }
}

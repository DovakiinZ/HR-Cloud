using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Modules.Attendance.Services;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendancePermissionCalcTests
{
    private static Shift FixedShift(TimeOnly start, TimeOnly end, int required, int breakMin = 0) => new()
    {
        NameAr = "ش", NameEn = "S", StartTime = start, EndTime = end,
        RequiredMinutes = required, BreakMinutes = breakMin, IsFlexible = false,
        WeekendDays = "5,6", OvertimeAllowed = false,
    };

    private static DateTime Day => new(2026, 8, 3); // a Monday (not weekend)
    private static DateTime At(int h, int m) => new DateTime(2026, 8, 3).AddHours(h).AddMinutes(m);
    private static PermissionWindow W(int fromH, int fromM, int toH, int toM)
        => new(fromH * 60 + fromM, toH * 60 + toM);

    private readonly AttendanceCalculationService _calc = new();

    [Fact] // Late arrival: shift 08:00-16:00 (480), arrived 09:00 → 60 late; permission 08:00-09:00 excuses it.
    public void Late_arrival_within_permission_is_excused()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(9, 0), At(16, 0), permissions: new[] { W(8, 0, 9, 0) });
        Assert.Equal(0, r.LateMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, r.Status);
    }

    [Fact] // Early departure: left 14:00 (shortage 120); permission 14:00-16:00 excuses it.
    public void Early_departure_within_permission_is_excused()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0), permissions: new[] { W(14, 0, 16, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(120, r.ExcusedMinutes);
        Assert.Equal(AttendanceStatus.Present, r.Status);
    }

    [Fact] // Temporary/partial exit: left 14:00 (shortage 120); permission 13:00-15:00 overlaps only [14:00,15:00]=60.
    public void Partial_window_excuses_only_the_overlapping_shortage()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0), permissions: new[] { W(13, 0, 15, 0) });
        Assert.Equal(60, r.ShortageMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
    }

    [Fact] // Overnight shift 20:00-04:00 (480). Left 03:00 → 60 shortage; permission 03:00-04:00 (after midnight) excuses it.
    public void Overnight_shift_permission_after_midnight_is_excused()
    {
        var shift = FixedShift(new(20, 0), new(4, 0), 480);
        var checkIn = new DateTime(2026, 8, 3, 20, 0, 0);
        var checkOut = new DateTime(2026, 8, 4, 3, 0, 0); // 03:00 next day
        var r = _calc.Calculate(shift, Day, checkIn, checkOut, permissions: new[] { W(3, 0, 4, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(60, r.ExcusedMinutes);
    }

    [Fact] // Overlapping permissions must not double-count: two windows both covering 14:00-16:00 excuse 120, not 240.
    public void Overlapping_permissions_are_merged()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(8, 0), At(14, 0),
            permissions: new[] { W(14, 0, 16, 0), W(14, 30, 16, 0) });
        Assert.Equal(0, r.ShortageMinutes);
        Assert.Equal(120, r.ExcusedMinutes);
    }

    [Fact] // No permission → unchanged (regression guard).
    public void No_permission_leaves_penalties_intact()
    {
        var shift = FixedShift(new(8, 0), new(16, 0), 480);
        var r = _calc.Calculate(shift, Day, At(9, 0), At(14, 0));
        Assert.Equal(60, r.LateMinutes);
        Assert.True(r.ShortageMinutes > 0);
        Assert.Equal(0, r.ExcusedMinutes);
    }
}

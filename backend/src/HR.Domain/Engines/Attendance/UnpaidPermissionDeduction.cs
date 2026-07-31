namespace HR.Domain.Engines.Attendance;

/// <summary>Pure wage math for an unpaid attendance-permission deduction.
/// amount = round( (minutes / 60) * (monthlyWage / divisorDays) / dailyPayableHours, 2 )
/// Returns 0 when any guard condition is not met (zero/negative minutes, divisor, or hours).</summary>
public static class UnpaidPermissionDeduction
{
    /// <param name="monthlyWage">Total monthly wage subject to deduction (basic + eligible allowances).</param>
    /// <param name="minutes">Excused (unpaid) minutes from the permission window.</param>
    /// <param name="divisorDays">Proration divisor (e.g. 30, calendar days, or working days).</param>
    /// <param name="dailyPayableHours">Standard paid hours per day (e.g. 8).</param>
    public static decimal Amount(decimal monthlyWage, int minutes, int divisorDays, decimal dailyPayableHours)
    {
        if (minutes <= 0 || divisorDays <= 0 || dailyPayableHours <= 0m)
            return 0m;

        // (minutes / 60) × (monthlyWage / divisorDays) / dailyPayableHours
        var hours = minutes / 60m;
        var dailyWage = monthlyWage / divisorDays;
        var hourlyWage = dailyWage / dailyPayableHours;
        return Math.Round(hours * hourlyWage, 2);
    }
}

using HR.Domain.Engines.Attendance;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class UnpaidPermissionDeductionTests
{
    [Fact] // 4h unpaid, 12000/30/8 → hourly 50 → 200.
    public void Four_hours_default_basis() =>
        Assert.Equal(200m, UnpaidPermissionDeduction.Amount(12000m, 240, 30, 8m));

    [Fact] // Configurable basis changes the amount: divisor 26, 7 payable hours.
    public void Non_default_basis_changes_amount() =>
        Assert.Equal(Math.Round(240 / 60m * (12000m / 26m) / 7m, 2),
            UnpaidPermissionDeduction.Amount(12000m, 240, 26, 7m));

    [Fact]
    public void Zero_minutes_is_zero() =>
        Assert.Equal(0m, UnpaidPermissionDeduction.Amount(12000m, 0, 30, 8m));

    [Fact]
    public void Zero_divisor_days_is_zero() =>
        Assert.Equal(0m, UnpaidPermissionDeduction.Amount(12000m, 240, 0, 8m));

    [Fact]
    public void Zero_daily_hours_is_zero() =>
        Assert.Equal(0m, UnpaidPermissionDeduction.Amount(12000m, 240, 30, 0m));

    [Fact]
    public void Negative_divisor_days_is_zero() =>
        Assert.Equal(0m, UnpaidPermissionDeduction.Amount(12000m, 240, -1, 8m));
}

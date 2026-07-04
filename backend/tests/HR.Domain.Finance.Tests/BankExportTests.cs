using FluentAssertions;
using HR.Application.Engines.Finance.Export.Bank;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP5 Task 3 — the country-agnostic bank pipeline: a data-driven profile (Saudi WPS/SIF is the
/// first), a pure field mapper, and a validator SEPARATE from file generation.</summary>
public class BankExportTests
{
    private const string ValidIban = "SA0380000000608010167519";

    [Fact]
    public void SaudiWps_mapper_projects_profile_fields_and_formats_amount()
    {
        var profile = new SaudiWpsSifProfile();
        var rows = new[] { new BankPaymentRow("E1", "Ali", ValidIban, "RJHISARI", "1234567890", 5750m, "SAR") };

        var ds = BankFieldMapper.Map(rows, profile);

        ds.Columns.Select(c => c.Key).Should().Contain(new[] { "Iban", "NetAmount" });
        ds.Rows[0]["Iban"].Should().Be(ValidIban);
        ds.Rows[0]["NetAmount"].Should().Be("5750.00"); // profile formats amount to 2dp
    }

    [Fact]
    public void SaudiWps_validator_flags_missing_iban_and_nonpositive_amount()
    {
        var rows = new[]
        {
            new BankPaymentRow("E1", "Ali", null, "RJHISARI", "1", 5000m, "SAR"),
            new BankPaymentRow("E2", "Sara", ValidIban, "RJHISARI", "2", 0m, "SAR"),
        };

        var errors = new SaudiWpsSifValidator().Validate(rows);

        errors.Should().Contain(e => e.EmployeeNumber == "E1" && e.Field == "Iban");
        errors.Should().Contain(e => e.EmployeeNumber == "E2" && e.Field == "NetAmount");
    }

    [Fact]
    public void SaudiWps_validator_flags_malformed_iban()
    {
        var rows = new[] { new BankPaymentRow("E3", "Omar", "GB00INVALID", "RJHISARI", "3", 100m, "SAR") };
        new SaudiWpsSifValidator().Validate(rows).Should().Contain(e => e.EmployeeNumber == "E3" && e.Field == "Iban");
    }

    [Fact]
    public void SaudiWps_validator_passes_valid_rows()
    {
        var rows = new[] { new BankPaymentRow("E1", "Ali", ValidIban, "RJHISARI", "1", 5000m, "SAR") };
        new SaudiWpsSifValidator().Validate(rows).Should().BeEmpty();
    }

    [Fact]
    public void SaudiWps_profile_advertises_code_and_version()
    {
        var p = new SaudiWpsSifProfile();
        p.Code.Should().Be("SA_WPS_SIF");
        p.Version.Should().NotBeNullOrWhiteSpace();
    }
}

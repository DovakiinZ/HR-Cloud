using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Models;
using HR.Application.Engines.Finance;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>F1 — structured error contract. Every failure envelope can carry a machine-readable
/// <c>Code</c>, and coded domain exceptions surface it through the base <see cref="DomainException.Code"/>
/// so the middleware can map it uniformly (replacing the client's brittle message-regex).</summary>
public class StructuredErrorContractTests
{
    [Fact]
    public void DomainException_carries_optional_code()
    {
        new DomainException("stale", "PAYROLL_RUN_STALE").Code.Should().Be("PAYROLL_RUN_STALE");
    }

    [Fact]
    public void DomainException_code_defaults_to_null()
    {
        new DomainException("just a message").Code.Should().BeNull();
    }

    [Fact]
    public void ApiResponse_Fail_carries_optional_code()
    {
        ApiResponse.Fail("nope", code: "PAYROLL_RUN_STALE").Code.Should().Be("PAYROLL_RUN_STALE");
    }

    [Fact]
    public void ApiResponse_Fail_code_defaults_to_null()
    {
        ApiResponse.Fail("nope").Code.Should().BeNull();
    }

    [Fact]
    public void PayrollPeriodClosedException_surfaces_error_code_through_base_Code()
    {
        var ex = new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
            "PAYROLL_PERIOD_CLOSED", System.Guid.NewGuid(), "PR-2026-00007",
            System.Guid.NewGuid(), 2026, 7, "Approved"));
        ((DomainException)ex).Code.Should().Be("PAYROLL_PERIOD_CLOSED");
    }
}

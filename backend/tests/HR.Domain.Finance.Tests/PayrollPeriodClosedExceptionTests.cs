using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Finance;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PayrollPeriodClosedExceptionTests
{
    [Fact]
    public void Exception_carries_structured_payload()
    {
        var ex = new PayrollPeriodClosedException(new PayrollPeriodClosedPayload(
            "PAYROLL_PERIOD_CLOSED", System.Guid.NewGuid(), "PR-2026-00007",
            System.Guid.NewGuid(), 2026, 7, "Approved"));
        ex.Payload.ErrorCode.Should().Be("PAYROLL_PERIOD_CLOSED");
        ex.Payload.TargetPeriodMonth.Should().Be(7);
        ex.Should().BeAssignableTo<DomainException>();
    }
}

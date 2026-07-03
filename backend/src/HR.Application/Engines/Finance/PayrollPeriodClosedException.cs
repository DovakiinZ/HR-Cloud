using HR.Application.Common.Exceptions;

namespace HR.Application.Engines.Finance;

public sealed record PayrollPeriodClosedPayload(
    string ErrorCode,
    System.Guid BlockingRunId,
    string BlockingRunNumber,
    System.Guid PayrollDefinitionId,
    int TargetPeriodYear,
    int TargetPeriodMonth,
    string BlockingRunState);

public sealed class PayrollPeriodClosedException : DomainException
{
    public PayrollPeriodClosedPayload Payload { get; }

    public PayrollPeriodClosedException(PayrollPeriodClosedPayload payload)
        : base($"Payroll period {payload.TargetPeriodYear}-{payload.TargetPeriodMonth:D2} is closed by run {payload.BlockingRunNumber} ({payload.BlockingRunState}).")
        => Payload = payload;
}

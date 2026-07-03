using HR.Domain.Enums;

namespace HR.Application.Engines.Finance;

public sealed record CreatePayrollTransactionArgs(
    PayrollTransactionKind Kind,
    Guid EmployeeId,
    Guid TypeId,
    decimal Amount,
    DateTime EffectiveDate,
    DateTime? TransactionDate,
    bool IsRecurring,
    DateTime? RecurrenceEndDate,
    string? Notes,
    Guid? AttachmentFileId,
    bool SubmitImmediately,
    PayrollTransactionOrigin Origin = PayrollTransactionOrigin.System,
    Guid? CreatedFromRunId = null);

public sealed record UpdatePayrollTransactionArgs(
    Guid TypeId,
    decimal Amount,
    DateTime EffectiveDate,
    DateTime? TransactionDate,
    bool IsRecurring,
    DateTime? RecurrenceEndDate,
    string? Notes,
    Guid? AttachmentFileId);

public sealed record PayrollTransactionFilter(
    PayrollTransactionKind? Kind,
    Guid? EmployeeId,
    int? PeriodYear,
    int? PeriodMonth,
    Guid? TypeId,
    PayrollTransactionStatus? Status,
    DateTime? DateFrom,
    DateTime? DateTo);

public sealed record PayrollTransactionDto(
    Guid Id,
    PayrollTransactionKind Kind,
    Guid EmployeeId,
    string EmployeeName,
    string EmployeeNumber,
    Guid TypeId,
    string TypeName,
    decimal Amount,
    DateTime TransactionDate,
    DateTime EffectiveDate,
    int? TargetPeriodYear,
    int? TargetPeriodMonth,
    bool IsRecurring,
    DateTime? RecurrenceEndDate,
    string? Notes,
    Guid? AttachmentFileId,
    string SourceModule,
    string? ReferenceType,
    Guid? ReferenceId,
    PayrollTransactionStatus Status,
    string? StatusReason,
    Guid? PayrollRunId,
    DateTime? PostedAt,
    Guid? ReversesTransactionId,
    DateTime CreatedAt,
    PayrollTransactionOrigin Origin,
    Guid? CreatedFromRunId);

public interface IPayrollTransactionService
{
    Task<Guid> CreateAsync(CreatePayrollTransactionArgs args, CancellationToken ct);
    Task UpdateAsync(Guid id, UpdatePayrollTransactionArgs args, CancellationToken ct);
    Task<IReadOnlyList<PayrollTransactionDto>> ListAsync(PayrollTransactionFilter filter, CancellationToken ct);
    Task<PayrollTransactionDto?> GetAsync(Guid id, CancellationToken ct);
    Task SubmitAsync(Guid id, CancellationToken ct);
    Task ApproveAsync(Guid id, CancellationToken ct);
    Task RejectAsync(Guid id, string reason, CancellationToken ct);
    Task CancelAsync(Guid id, string? reason, CancellationToken ct);
    Task SetAttachmentAsync(Guid id, Guid fileId, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
